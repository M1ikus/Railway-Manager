using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using DepotSystem;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;
using RailwayManager.SharedUI;

namespace DepotSystem.Furniture.Placement
{
    /// <summary>
    /// MF-7 — selector dla istniejących PlacedFurnitureItem instancji.
    ///
    /// Workflow:
    /// 1. Update co frame:
    ///    - LMB pressed + Furniture tool aktywny + nie jest aktywny placement +
    ///      kursor nie nad UI → raycast Y=0 → lookup instance po footprint cells
    ///    - Esc → deselect
    /// 2. Selection state: <see cref="SelectedInstanceId"/> (-1 = brak)
    /// 3. Visual highlight: LineRenderer wireframe rectangle wokół footprint base
    /// 4. Event <see cref="OnSelectionChanged"/> dla FurnitureContextMenuUI (pokazuje
    ///    panel z Move/Rotate/Delete) i innych konsumentów
    ///
    /// Singleton, lazy-create przez FurnitureToolBootstrap.
    /// </summary>
    public class FurnitureSelector : MonoBehaviour
    {
        public static FurnitureSelector Instance { get; private set; }

        [Header("Selection state (debug only)")]
        [SerializeField] private int _selectedInstanceId = -1;

        [Header("Highlight settings")]
        [SerializeField] private float highlightLineWidth = 0.08f;
        [SerializeField] private float highlightYOffset = 0.05f;
        [SerializeField] private Color highlightColor = new Color(1f, 1f, 0.2f, 1f);

        public int SelectedInstanceId => _selectedInstanceId;
        public event Action<int> OnSelectionChanged;

        private GameObject _highlightGO;
        private LineRenderer _highlightLine;
        private LineRenderer _highlightGlowLine;
        private Material _highlightMaterial;
        private Camera _mainCamera;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (Mathf.Approximately(highlightColor.r, 1f)
                && Mathf.Approximately(highlightColor.g, 1f)
                && Mathf.Approximately(highlightColor.b, 0.2f))
            {
                highlightColor = UITheme.PrimaryAccent;
            }
        }

        // ── Tool-mode gate ──
        private DepotSystem.ToolModeGate _toolGate;

        void Start()
        {
            _toolGate = new DepotSystem.ToolModeGate(
                this,
                m => m == DepotSystem.ToolMode.BuildRoom,
                OnToolDeactivated);
            _toolGate.Start();
        }

        private void OnToolDeactivated()
        {
            if (_selectedInstanceId != -1) Deselect();
        }

        void OnDestroy()
        {
            _toolGate?.Stop();
            if (Instance == this) Instance = null;
            CleanupHighlight();
        }

        // ════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════

        /// <summary>Anuluje aktualną selekcję.</summary>
        public void Deselect()
        {
            if (_selectedInstanceId == -1) return;
            int old = _selectedInstanceId;
            _selectedInstanceId = -1;
            CleanupHighlight();
            OnSelectionChanged?.Invoke(-1);
            Log.Info($"[FurnitureSelector] Deselected #{old}");
        }

        /// <summary>Wymusza wybór konkretnego instance (np. po RotateInstance refresh).</summary>
        public void Select(int instanceId)
        {
            if (instanceId < 0) { Deselect(); return; }
            var placer = FurniturePlacer.Instance;
            if (placer == null) return;

            var instance = placer.GetInstance(instanceId);
            if (instance == null)
            {
                Log.Warn($"[FurnitureSelector] Select: instance #{instanceId} nie istnieje");
                return;
            }

            _selectedInstanceId = instanceId;
            RebuildHighlight(instance);
            OnSelectionChanged?.Invoke(instanceId);
            Log.Info($"[FurnitureSelector] Selected #{instanceId} '{instance.itemId}'");
        }

        // ════════════════════════════════════════
        //  UPDATE LOOP
        // ════════════════════════════════════════

        void Update()
        {
            // MF-Furniture reorg (2026-05-03): selektor aktywny w BuildRoom (zamiast osobnego
            // ToolMode.Furniture). Meble są częścią BuildRoom flow przez RoomBuildPanelUI.
            // Gate (ToolModeGate w Start) enabled tylko gdy BuildRoom — Update tutaj zawsze
            // odpalony tylko w trybie BuildRoom.
            var uiManager = DepotUIManager.Instance;

            // Esc deselect (priority nad placement Esc — placement obsługuje, jeśli nieaktywny to my)
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame
                && _selectedInstanceId != -1
                && (FurniturePlacer.Instance == null || !FurniturePlacer.Instance.IsActive))
            {
                Deselect();
                return;
            }

            // LMB click handler
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;

            // Skip jeśli placement aktywny (placer obsługuje confirm)
            if (FurniturePlacer.Instance != null && FurniturePlacer.Instance.IsActive) return;

            // Skip jeśli klik na UI
            if (uiManager.IsPointerOverUI()) return;

            // Raycast Y=0 → world cell
            Vector3 worldPos = GetCursorWorldPos();
            int hitInstanceId = FindInstanceAtWorldPos(worldPos);

            if (hitInstanceId == -1)
            {
                Deselect();
            }
            else
            {
                Select(hitInstanceId);
            }
        }

        // ════════════════════════════════════════
        //  PRIVATE — raycast + lookup
        // ════════════════════════════════════════

        private Vector3 GetCursorWorldPos()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null || Mouse.current == null) return Vector3.zero;

            Vector2 mouseScreen = Mouse.current.position.ReadValue();
            Ray ray = _mainCamera.ScreenPointToRay(mouseScreen);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float dist)) return ray.GetPoint(dist);
            return Vector3.zero;
        }

        /// <summary>
        /// Iteruje wszystkie instancje, znajduje tę której footprint zawiera klik cell.
        /// Zwraca instanceId lub -1 gdy nic nie trafiono.
        /// </summary>
        private int FindInstanceAtWorldPos(Vector3 worldPos)
        {
            var placer = FurniturePlacer.Instance;
            if (placer == null) return -1;

            Vector2Int clickCell = new Vector2Int(
                Mathf.FloorToInt(worldPos.x),
                Mathf.FloorToInt(worldPos.z));

            foreach (var instance in placer.PlacedInstances)
            {
                if (instance == null) continue;
                var item = FurnitureCatalog.FindById(instance.itemId);
                if (item == null) continue;

                var cells = FurnitureSnapDetector.GetFootprintCells(
                    instance.position,
                    item.footprintCells.x,
                    item.footprintCells.y,
                    instance.rotation);

                for (int i = 0; i < cells.Count; i++)
                {
                    if (cells[i] == clickCell) return instance.instanceId;
                }
            }
            return -1;
        }

        // ════════════════════════════════════════
        //  PRIVATE — highlight wireframe
        // ════════════════════════════════════════

        private void RebuildHighlight(PlacedFurnitureItem instance)
        {
            CleanupHighlight();

            var item = FurnitureCatalog.FindById(instance.itemId);
            if (item == null) return;

            var (sizeX, sizeZ) = FurnitureSnapDetector.GetRotatedFootprintSize(
                item.footprintCells.x, item.footprintCells.y, instance.rotation);

            _highlightGO = new GameObject($"FurnitureHighlight_{instance.instanceId}");
            _highlightGO.transform.SetParent(transform, worldPositionStays: false);
            _highlightGO.transform.position = new Vector3(instance.position.x, highlightYOffset, instance.position.z);

            // LineRenderer ma [DisallowMultipleComponent] — drugi AddComponent na tym samym GO
            // zwraca null. Glow line musi być na osobnym child GO.
            _highlightLine = _highlightGO.AddComponent<LineRenderer>();

            var glowGO = new GameObject("Glow");
            glowGO.transform.SetParent(_highlightGO.transform, worldPositionStays: false);
            glowGO.transform.localPosition = Vector3.zero;
            glowGO.transform.localRotation = Quaternion.identity;
            _highlightGlowLine = glowGO.AddComponent<LineRenderer>();

            float halfX = sizeX * 0.5f;
            float halfZ = sizeZ * 0.5f;

            if (_highlightMaterial == null)
            {
                _highlightMaterial = MaterialFactory.CreateLine();
                MaterialFactory.SetBaseColor(_highlightMaterial, Color.white);
            }

            ConfigureHighlightLine(_highlightGlowLine, halfX, halfZ, highlightLineWidth * 2.6f, UITheme.WithAlpha(highlightColor, 0.24f));
            ConfigureHighlightLine(_highlightLine, halfX, halfZ, highlightLineWidth, highlightColor);
        }

        private void ConfigureHighlightLine(LineRenderer line, float halfX, float halfZ, float width, Color color)
        {
            line.useWorldSpace = false;
            line.startWidth = width;
            line.endWidth = width;
            line.loop = true;
            line.positionCount = 4;
            line.SetPosition(0, new Vector3(-halfX, 0f, -halfZ));
            line.SetPosition(1, new Vector3(+halfX, 0f, -halfZ));
            line.SetPosition(2, new Vector3(+halfX, 0f, +halfZ));
            line.SetPosition(3, new Vector3(-halfX, 0f, +halfZ));
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.material = _highlightMaterial;
            line.startColor = color;
            line.endColor = color;
        }

        private void CleanupHighlight()
        {
            if (_highlightGO != null)
            {
                Destroy(_highlightGO);
                _highlightGO = null;
            }
            _highlightLine = null;
            _highlightGlowLine = null;
        }
    }
}
