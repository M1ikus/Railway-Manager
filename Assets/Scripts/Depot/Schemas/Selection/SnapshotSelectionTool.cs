using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;

namespace DepotSystem.Schemas.Selection
{
    /// <summary>
    /// MD-7 — narzędzie selekcji rectangle drag dla snapshot mode.
    ///
    /// Workflow:
    /// 1. <see cref="StartSelection"/> — aktywuje tryb (np. z UI "Utwórz nowy schemat")
    /// 2. Update tick:
    ///    - LMB pressed → start drag (zapisz cursor world position jako _dragStart)
    ///    - LMB held + ruch myszą → update _dragEnd, recalc selection, update visualization
    ///    - LMB released → confirm selection, wywołaj OnSelectionConfirmed event, exit mode
    /// 3. Esc anytime → CancelSelection
    ///
    /// Algorytm "który segment jest w selekcji" (Wariant B z spec'a, decyzja A11):
    /// - Tor: oba endpointy w prostokącie LUB &gt;50% punktów polyline w prostokącie
    /// - Rozjazd: origin w prostokącie
    ///
    /// MD-7 zapewnia tylko detection + visualization. MD-8 podpina serializer + opens save dialog.
    /// </summary>
    public class SnapshotSelectionTool : MonoBehaviour
    {
        public static SnapshotSelectionTool Instance { get; private set; }

        [Header("State (debug only — set by Update)")]
        [SerializeField] private bool _isActive;
        [SerializeField] private bool _isDragging;
        [SerializeField] private int _selectedTrackCount;
        [SerializeField] private int _selectedTurnoutCount;

        // ── Runtime ──
        private Vector3 _dragStartWorld;
        private Vector3 _dragEndWorld;
        private Vector3 _cursorWorldPos;
        private SnapshotSelectionResult _currentSelection;
        private SnapshotSelectionRenderer _renderer;
        private PrefabTrackBuilder _trackBuilder;
        private Camera _mainCamera;

        public bool IsActive => _isActive;
        public bool IsDragging => _isDragging;
        public SnapshotSelectionResult CurrentSelection => _currentSelection;

        /// <summary>
        /// Event firowany przy confirm (LMB released po drag). Przekazuje wynik selekcji.
        /// MD-8 podepnie tutaj otwarcie save dialog'u + serialize.
        /// </summary>
        public event Action<SnapshotSelectionResult> OnSelectionConfirmed;

        /// <summary>Event firowany przy cancel (Esc lub external CancelSelection).</summary>
        public event Action OnSelectionCancelled;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CleanupRenderer();
        }

        // ════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════

        public void StartSelection()
        {
            if (_isActive)
            {
                Log.Warn("[SnapshotSelectionTool] Already active, ignoring StartSelection");
                return;
            }

            _isActive = true;
            _isDragging = false;
            _currentSelection = null;
            _selectedTrackCount = 0;
            _selectedTurnoutCount = 0;
            EnsureRenderer();

            Log.Info("[SnapshotSelectionTool] Selection mode aktywny. LMB drag = zaznaczenie, Esc = anuluj.");
        }

        public void CancelSelection()
        {
            if (!_isActive) return;

            _isActive = false;
            _isDragging = false;
            _currentSelection = null;
            _selectedTrackCount = 0;
            _selectedTurnoutCount = 0;
            CleanupRenderer();

            Log.Info("[SnapshotSelectionTool] Selection cancelled.");
            OnSelectionCancelled?.Invoke();
        }

        // ════════════════════════════════════════
        //  UPDATE — input + drag + selection
        // ════════════════════════════════════════

        void Update()
        {
            if (!_isActive) return;

            // 1. Cursor → world position
            UpdateCursorWorldPosition();

            // 2. Esc cancel
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelSelection();
                return;
            }

            // 3. LMB handling
            if (Mouse.current == null) return;

            if (!_isDragging)
            {
                // Wait for LMB press → start drag
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    _dragStartWorld = _cursorWorldPos;
                    _dragEndWorld = _cursorWorldPos;
                    _isDragging = true;
                }
            }
            else
            {
                // Update drag end position
                _dragEndWorld = _cursorWorldPos;

                // Update selection + visualization
                UpdateSelection();
                UpdateRendererVisuals();

                // Confirm na LMB released
                if (Mouse.current.leftButton.wasReleasedThisFrame)
                {
                    ConfirmSelection();
                }
            }
        }

        private void UpdateCursorWorldPosition()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null || Mouse.current == null) return;

            Vector2 mouseScreen = Mouse.current.position.ReadValue();
            Ray ray = _mainCamera.ScreenPointToRay(mouseScreen);

            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float dist))
            {
                _cursorWorldPos = ray.GetPoint(dist);
            }
        }

        // ════════════════════════════════════════
        //  SELECTION ALGORITHM
        // ════════════════════════════════════════

        private void UpdateSelection()
        {
            if (_trackBuilder == null) _trackBuilder = DepotServices.Get<PrefabTrackBuilder>();

            var result = new SnapshotSelectionResult();

            // Bounds w XZ (Y zawsze 0 dla zajezdni)
            Bounds rect = ComputeBounds(_dragStartWorld, _dragEndWorld);
            result.selectionBounds = rect;
            result.selectionCenter = rect.center;

            if (_trackBuilder == null)
            {
                _currentSelection = result;
                return;
            }

            // Tory
            foreach (var seg in _trackBuilder.PlacedTracks)
            {
                if (seg == null) continue;
                if (IsTrackInRectangle(seg, rect))
                    result.selectedTracks.Add(seg);
            }

            // Rozjazdy
            foreach (var t in _trackBuilder.TurnoutEntities.Values)
            {
                if (t == null) continue;
                if (IsPointInRectangleXZ(t.Origin, rect))
                    result.selectedTurnouts.Add(t);
            }

            _currentSelection = result;
            _selectedTrackCount = result.selectedTracks.Count;
            _selectedTurnoutCount = result.selectedTurnouts.Count;
        }

        private static Bounds ComputeBounds(Vector3 cornerA, Vector3 cornerC)
        {
            float minX = Mathf.Min(cornerA.x, cornerC.x);
            float maxX = Mathf.Max(cornerA.x, cornerC.x);
            float minZ = Mathf.Min(cornerA.z, cornerC.z);
            float maxZ = Mathf.Max(cornerA.z, cornerC.z);
            Vector3 center = new Vector3((minX + maxX) * 0.5f, 0, (minZ + maxZ) * 0.5f);
            Vector3 size = new Vector3(maxX - minX, 0.1f, maxZ - minZ);
            return new Bounds(center, size);
        }

        private static bool IsPointInRectangleXZ(Vector3 point, Bounds rect)
        {
            // Test tylko XZ (ignoruj Y, bo zajezdnia płaska)
            return point.x >= rect.min.x && point.x <= rect.max.x
                && point.z >= rect.min.z && point.z <= rect.max.z;
        }

        /// <summary>
        /// Wariant B z spec'a (decyzja A11): tor zaznaczony jeśli oba endpointy w prostokącie
        /// LUB &gt;50% punktów polyline w prostokącie.
        /// </summary>
        private static bool IsTrackInRectangle(PlacedTrackSegment seg, Bounds rect)
        {
            bool startIn = IsPointInRectangleXZ(seg.StartPosition, rect);
            bool endIn = IsPointInRectangleXZ(seg.EndPosition, rect);
            if (startIn && endIn) return true;

            // 50% długości heuristic — sample punktów polyline
            if (seg.Polyline == null || seg.Polyline.Count == 0)
                return startIn || endIn;

            int inCount = 0;
            for (int i = 0; i < seg.Polyline.Count; i++)
            {
                if (IsPointInRectangleXZ(seg.Polyline[i], rect)) inCount++;
            }
            return (float)inCount / seg.Polyline.Count > 0.5f;
        }

        // ════════════════════════════════════════
        //  CONFIRM / VISUALIZATION
        // ════════════════════════════════════════

        private void ConfirmSelection()
        {
            if (_currentSelection == null || _currentSelection.IsEmpty)
            {
                Log.Warn("[SnapshotSelectionTool] Empty selection — cancelling");
                CancelSelection();
                return;
            }

            Log.Info($"[SnapshotSelectionTool] Selection confirmed: {_currentSelection.selectedTracks.Count} tracks, {_currentSelection.selectedTurnouts.Count} turnouts, bounds={_currentSelection.selectionBounds.size}");

            // Cleanup highlight i rectangle visualization OD RAZU (= LMB released = save dialog
            // się otworzy, highlight nie powinien zostawać na ekranie).
            CleanupRenderer();

            // Exit mode
            _isActive = false;
            _isDragging = false;

            // Fire event — handler (np. SchemaPanelUI.OnSnapshotSelectionConfirmed) otwiera
            // save dialog, serializuje, etc.
            OnSelectionConfirmed?.Invoke(_currentSelection);
        }

        private void UpdateRendererVisuals()
        {
            if (_renderer == null) return;
            _renderer.SetRectangle(_dragStartWorld, _dragEndWorld);
            _renderer.SetHighlight(_currentSelection);
        }

        private void EnsureRenderer()
        {
            if (_renderer != null) return;
            var go = new GameObject("SnapshotSelectionRenderer");
            go.transform.SetParent(transform, false);
            _renderer = go.AddComponent<SnapshotSelectionRenderer>();
        }

        private void CleanupRenderer()
        {
            if (_renderer != null)
            {
                _renderer.ClearAll();
                Destroy(_renderer.gameObject);
                _renderer = null;
            }
        }
    }
}
