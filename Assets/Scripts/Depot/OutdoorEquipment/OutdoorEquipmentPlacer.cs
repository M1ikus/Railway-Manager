using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem.OutdoorEquipment
{
    /// <summary>
    /// MVP 2026-05-03 — placer outdoor equipment (WashZone/Turntable/PitLift) wzorowany na
    /// WallBuildingSystem.HandleWallBuild (rect z 2 kliknięć).
    ///
    /// Aktywuje się gdy <c>CurrentTool == BuildTrack</c> i <c>CurrentTrackSubMode</c> jest jednym
    /// z trzech outdoor sub-mode'ów. Walidacja size + brak overlap z istniejącymi placement'ami.
    /// Na confirm spawn cuboid placeholder kolor wg presetu + zapisuje do listy.
    ///
    /// Save/Load + pełen gameplay impact (myjnia czyści pojazd, turntable obraca, pitlift
    /// podnosi) → M-Modernization.
    /// </summary>
    public class OutdoorEquipmentPlacer : MonoBehaviour
    {
        public static OutdoorEquipmentPlacer Instance { get; private set; }

        private enum BuildState { Idle, PlacingCornerA, PreviewingRect }

        [Header("State (debug)")]
        [SerializeField] private BuildState _state = BuildState.Idle;
        [SerializeField] private OutdoorEquipmentType _currentType;

        // Preview
        private GameObject _previewParent;
        private LineRenderer[] _previewLines;
        private GameObject _previewFill;
        private Material _previewLineMat;
        private Material _previewFillMat;

        // Build state
        private Vector3 _cornerA;
        private Camera _mainCamera;
        private const float GridSize = 1f;
        private const float MinBuildingSize = 2f;
        private static readonly Color PreviewValidColor = new Color(0.3f, 1f, 0.3f, 0.85f);
        private static readonly Color PreviewInvalidColor = new Color(1f, 0.3f, 0.3f, 0.85f);

        // Placed list (in-memory MVP — DepotSavable persistence M-Modernization)
        private readonly List<PlacedOutdoorEquipment> _placed = new();
        private int _nextInstanceId = 1;

        public IReadOnlyList<PlacedOutdoorEquipment> Placed => _placed;

        /// <summary>
        /// MM-9: counter dla DepotSavable serializacji (analog FurniturePlacer.NextInstanceId).
        /// </summary>
        public int NextInstanceId => _nextInstanceId;

        /// <summary>
        /// MM-9: restore listy z save'a + spawn visual cuboids. Wywoływane przez DepotSavable v5
        /// po deserialize. Bezpieczne wywołanie wielokrotne — czyści _placed przed restore.
        /// </summary>
        public void RestoreFromSave(List<PlacedOutdoorEquipment> instances)
        {
            // Cleanup current visuals
            foreach (var oe in _placed)
                if (oe != null && oe.visualObject != null) Destroy(oe.visualObject);
            _placed.Clear();

            int maxId = 0;
            if (instances != null)
            {
                foreach (var inst in instances)
                {
                    if (inst == null) continue;
                    _placed.Add(inst);
                    inst.visualObject = SpawnVisual(inst);
                    if (inst.instanceId > maxId) maxId = inst.instanceId;
                }
            }
            _nextInstanceId = maxId + 1;
            Log.Info($"[OutdoorEquipmentPlacer] RestoreFromSave: {_placed.Count} instances, " +
                     $"nextInstanceId={_nextInstanceId}");
        }

        // Input
        private InputActions _inputActions;
        private InputActions.ToolBuildActions _toolBuild;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var existing = FindAnyObjectByType<OutdoorEquipmentPlacer>();
            if (existing != null) return;
            var go = new GameObject("OutdoorEquipmentPlacer (auto-spawn)");
            go.AddComponent<OutdoorEquipmentPlacer>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _inputActions = new InputActions();
            RailwayManager.Core.Settings.RebindingService.ApplyOverridesTo(_inputActions);
            _toolBuild = _inputActions.ToolBuild;
        }

        void OnEnable() => _toolBuild.Enable();
        void OnDisable() => _toolBuild.Disable();
        void OnDestroy()
        {
            _toolGate?.Stop();
            _inputActions?.Dispose();
            if (Instance == this) Instance = null;
        }

        // ── Tool-mode gate ──
        private ToolModeGate _toolGate;

        void Start()
        {
            _mainCamera = Camera.main;
            CreatePreviewObjects();

            _toolGate = new ToolModeGate(this, m => m == ToolMode.BuildTrack, CancelIfActive);
            _toolGate.Start();
        }

        void Update()
        {
            var ui = DepotUIManager.Instance;
            // Sub-mode dispatch zostaje w Update (BuildTrack ma wiele sub-mode'ów —
            // tylko outdoor podzbiór aktywuje placera, reszta to no-op CancelIfActive).
            var typeOpt = OutdoorEquipmentDefinitions.FromSubMode(ui.CurrentTrackSubMode);
            if (!typeOpt.HasValue)
            {
                CancelIfActive();
                return;
            }

            // Sub-mode zmienił się w trakcie — reset
            if (_state != BuildState.Idle && _currentType != typeOpt.Value)
            {
                CancelIfActive();
            }
            _currentType = typeOpt.Value;

            if (ui.IsPointerOverUI()) { HidePreview(); return; }

            if (_mainCamera == null) _mainCamera = Camera.main;
            Vector3 mouseWorld = GetMouseWorldPosition();
            Vector3 snapped = SnapToGrid(mouseWorld);

            // ESC — anuluj
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelIfActive();
                return;
            }

            switch (_state)
            {
                case BuildState.Idle:
                case BuildState.PlacingCornerA:
                    HidePreview();
                    if (_toolBuild.Primary.WasPressedThisFrame())
                    {
                        _cornerA = snapped;
                        _state = BuildState.PreviewingRect;
                    }
                    break;

                case BuildState.PreviewingRect:
                    UpdateRectPreview(_cornerA, snapped, _currentType);

                    if (_toolBuild.Primary.WasPressedThisFrame())
                    {
                        if (IsValidRect(_cornerA, snapped, _currentType))
                        {
                            ConfirmPlacement(_cornerA, snapped, _currentType);
                            _state = BuildState.PlacingCornerA;
                        }
                    }

                    if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
                    {
                        CancelIfActive();
                    }
                    break;
            }
        }

        // ════════════════════════════════════════
        //  PLACEMENT + VALIDATION
        // ════════════════════════════════════════

        private bool IsValidRect(Vector3 a, Vector3 b, OutdoorEquipmentType type)
        {
            float dx = Mathf.Abs(b.x - a.x);
            float dz = Mathf.Abs(b.z - a.z);
            if (dx < MinBuildingSize || dz < MinBuildingSize) return false;

            // Walidacja size per typ (preset minWidth/minDepth, oba obroty)
            if (!OutdoorEquipmentDefinitions.Presets.TryGetValue(type, out var preset)) return false;
            bool fitsType = (dx >= preset.minWidth && dz >= preset.minDepth)
                         || (dx >= preset.minDepth && dz >= preset.minWidth);
            if (!fitsType) return false;

            // Brak overlap z istniejącymi outdoor equipments
            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x);
            float minZ = Mathf.Min(a.z, b.z), maxZ = Mathf.Max(a.z, b.z);
            foreach (var p in _placed)
            {
                if (p == null) continue;
                float pMinX = Mathf.Min(p.cornerA.x, p.cornerB.x), pMaxX = Mathf.Max(p.cornerA.x, p.cornerB.x);
                float pMinZ = Mathf.Min(p.cornerA.z, p.cornerB.z), pMaxZ = Mathf.Max(p.cornerA.z, p.cornerB.z);
                if (minX < pMaxX && maxX > pMinX && minZ < pMaxZ && maxZ > pMinZ) return false;
            }

            return true;
        }

        private void ConfirmPlacement(Vector3 a, Vector3 b, OutdoorEquipmentType type)
        {
            // M-Economy Faza 5: koszt budowy outdoor — blokada „nie stać → nie buduj".
            // Refund N/D: outdoor MVP nie ma ścieżki usuwania (_placed tylko Add/Clear) — gdy dojdzie
            // delete, dorzucić ConstructionBilling.Refund(OutdoorGroszy(type), ...).
            if (!ConstructionBilling.TryCharge(ConstructionCosts.OutdoorGroszy(type), "outdoor_build", type.ToString()))
            {
                Log.Warn($"[OutdoorEquipmentPlacer] ConfirmPlacement: brak srodkow na {type} → blocked");
                return;
            }

            var instance = new PlacedOutdoorEquipment
            {
                instanceId = _nextInstanceId++,
                type = type,
                cornerA = a,
                cornerB = b
            };
            _placed.Add(instance);
            instance.visualObject = SpawnVisual(instance);
            Log.Info($"[OutdoorEquipmentPlacer] CONFIRM #{instance.instanceId} {type} " +
                     $"a={a} b={b} size={Mathf.Abs(b.x - a.x):F1}x{Mathf.Abs(b.z - a.z):F1}m");
        }

        private GameObject SpawnVisual(PlacedOutdoorEquipment placed)
        {
            var preset = OutdoorEquipmentDefinitions.Presets[placed.type];
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"OutdoorEquipment_{placed.instanceId}_{placed.type}";
            go.transform.SetParent(transform, worldPositionStays: false);

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            float dx = Mathf.Abs(placed.cornerB.x - placed.cornerA.x);
            float dz = Mathf.Abs(placed.cornerB.z - placed.cornerA.z);
            float cx = (placed.cornerA.x + placed.cornerB.x) * 0.5f;
            float cz = (placed.cornerA.z + placed.cornerB.z) * 0.5f;
            const float height = 0.4f;
            go.transform.position = new Vector3(cx, height * 0.5f, cz);
            go.transform.localScale = new Vector3(dx, height, dz);

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var mat = MaterialFactory.CreateLine();
            MaterialFactory.SetBaseColor(mat, new Color(preset.color.r, preset.color.g, preset.color.b, 0.85f));
            renderer.material = mat;
            return go;
        }

        // ════════════════════════════════════════
        //  PREVIEW
        // ════════════════════════════════════════

        private void CreatePreviewObjects()
        {
            _previewParent = new GameObject("OutdoorEqPreview");
            _previewParent.transform.SetParent(transform, false);

            _previewLineMat = MaterialFactory.CreateLine();
            _previewLines = new LineRenderer[4];
            for (int i = 0; i < 4; i++)
            {
                var lr = new GameObject($"PreviewLine{i}").AddComponent<LineRenderer>();
                lr.transform.SetParent(_previewParent.transform, false);
                lr.useWorldSpace = true;
                lr.startWidth = 0.18f;
                lr.endWidth = 0.18f;
                lr.material = _previewLineMat;
                lr.positionCount = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _previewLines[i] = lr;
            }

            _previewFill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _previewFill.name = "PreviewFill";
            _previewFill.transform.SetParent(_previewParent.transform, false);
            _previewFill.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var fillCol = _previewFill.GetComponent<Collider>();
            if (fillCol != null) Destroy(fillCol);
            _previewFillMat = MaterialFactory.CreateLine();
            _previewFill.GetComponent<MeshRenderer>().material = _previewFillMat;

            _previewParent.SetActive(false);
        }

        private void UpdateRectPreview(Vector3 a, Vector3 bRaw, OutdoorEquipmentType type)
        {
            Vector3 b = SnapToGrid(bRaw);
            bool valid = IsValidRect(a, b, type);
            Color color = valid ? PreviewValidColor : PreviewInvalidColor;

            Vector3 c0 = new Vector3(a.x, 0.15f, a.z);
            Vector3 c1 = new Vector3(b.x, 0.15f, a.z);
            Vector3 c2 = new Vector3(b.x, 0.15f, b.z);
            Vector3 c3 = new Vector3(a.x, 0.15f, b.z);

            SetLine(_previewLines[0], c0, c1, color);
            SetLine(_previewLines[1], c1, c2, color);
            SetLine(_previewLines[2], c2, c3, color);
            SetLine(_previewLines[3], c3, c0, color);

            if (_previewFill != null)
            {
                _previewFill.SetActive(true);
                Vector3 center = (a + b) * 0.5f;
                center.y = 0.05f;
                _previewFill.transform.position = center;
                _previewFill.transform.localScale = new Vector3(Mathf.Abs(b.x - a.x), Mathf.Abs(b.z - a.z), 1f);
                MaterialFactory.SetBaseColor(_previewFillMat, new Color(color.r, color.g, color.b, 0.18f));
            }

            _previewParent.SetActive(true);
        }

        private static void SetLine(LineRenderer lr, Vector3 a, Vector3 b, Color c)
        {
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startColor = c;
            lr.endColor = c;
        }

        private void HidePreview()
        {
            if (_previewParent != null) _previewParent.SetActive(false);
        }

        private void CancelIfActive()
        {
            if (_state != BuildState.Idle)
            {
                _state = BuildState.Idle;
            }
            HidePreview();
        }

        // ════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════

        private Vector3 GetMouseWorldPosition()
        {
            if (_mainCamera == null || Mouse.current == null) return Vector3.zero;
            Vector2 screen = Mouse.current.position.ReadValue();
            Ray ray = _mainCamera.ScreenPointToRay(screen);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            return ground.Raycast(ray, out float dist) ? ray.GetPoint(dist) : Vector3.zero;
        }

        private static Vector3 SnapToGrid(Vector3 worldPos)
            => new Vector3(Mathf.Round(worldPos.x / GridSize) * GridSize, 0f, Mathf.Round(worldPos.z / GridSize) * GridSize);
    }
}
