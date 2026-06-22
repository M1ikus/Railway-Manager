using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    /// <summary>
    /// State machine dla budowy ścieżek, dróg i parkingów.
    /// Ścieżki/drogi: malowanie komórek siatki (klik/przeciąganie).
    /// Parking: prostokąt (2 kliknięcia narożników).
    ///
    /// Klasa rozbita na partial files:
    /// - <c>PathBuildStateMachine.cs</c>          — pola, lifecycle, Update dispatcher,
    ///                                              CancelBuild, helpers (cell/world conversion,
    ///                                              pending visuals) (ten plik)
    /// - <c>PathBuildStateMachine.Paint.cs</c>    — ścieżka/droga: HandlePaintMode + AddCellsToPending
    ///                                              + ShowCursorPreview + IsCellValid* +
    ///                                              PlaceCell + UndoPlaceCell/RemoveCell (public)
    /// - <c>PathBuildStateMachine.Parking.cs</c>  — parking: HandleParkingMode + IsValidParking +
    ///                                              BuildParking + RemoveCellsInRect
    /// - <c>PathBuildStateMachine.Preview.cs</c>  — walidacja kolizji (ValidateCellCrossing,
    ///                                              IsTrackPartOfTurnout, RectOverlapsTracks) +
    ///                                              preview visual (Show/Hide/Update Cell/Strip/Fill)
    /// - <c>PathBuildStateMachine.Demolish.cs</c> — wyburzanie: HandleDemolishPaths + DemolishParking
    ///                                              + UndoDemolishParking (public) + popup confirm
    /// </summary>
    public partial class PathBuildStateMachine : MonoBehaviour
    {
        private enum BuildState
        {
            Idle,
            Painting,          // Malowanie podglądu (LMB hold + drag)
            WaitingConfirm,    // Podgląd gotowy, czeka na klik potwierdzenia
            PlacingCornerA,    // Parking: czeka na narożnik A
            PreviewingRect     // Parking: preview prostokąta
        }

        public float gridSize = 2f;

        [Header("Preview Colors")]
        public Color validColor = new Color(0.3f, 0.9f, 0.3f, 0.5f);
        public Color invalidColor = new Color(0.9f, 0.3f, 0.3f, 0.5f);

        private PathGraph pathGraph;
        private PathVisualBuilder visualBuilder;
        private PrefabTrackBuilder trackBuilder;
        private GroundGenerator groundGenerator;

        private BuildState state = BuildState.Idle;
        private Vector3 pointA;
        private Camera mainCamera;
        private Bounds? buildableArea;

        // Malowanie
        private HashSet<Vector2Int> placedCells = new();
        private Dictionary<Vector2Int, int> cellToNodeId = new();
        private Dictionary<Vector2Int, GameObject> cellVisuals = new();
        private Dictionary<Vector2Int, PathBuildSubMode> cellTypes = new();

        // Droga — rozmiar pędzla
        public int roadBrushSize = 6;

        // Podgląd przed potwierdzeniem
        private HashSet<Vector2Int> pendingCells = new();
        private List<GameObject> pendingVisuals = new();
        private PathBuildSubMode pendingSubMode;

        // Preview
        private GameObject previewCell;
        private GameObject previewFill;
        private GameObject previewHudObj;
        private TextMesh previewInfoTextMesh;
        private TextMesh previewStatusTextMesh;
        private PathBuildValidationReason previewValidationReason = PathBuildValidationReason.None;
        private bool parkingEndSelectionPaused = false;

        [Header("Preview HUD")]
        [SerializeField] private float previewInfoHeight = 1.85f;
        [SerializeField] private float previewStatusHeight = 2.9f;

        private const string ParkingPausedMessage =
            "Rog startowy zachowany - porusz myszka, aby wskazac nowy przeciwlegly rog - ESC wylacz";

        // ── Input System ──
        private InputActions _inputActions;
        private InputActions.ToolBuildActions _toolBuild;

        void Awake()
        {
            _inputActions = new InputActions();
            RailwayManager.Core.Settings.RebindingService.ApplyOverridesTo(_inputActions);
            _toolBuild = _inputActions.ToolBuild;
        }

        void OnEnable()
        {
            _toolBuild.Enable();
        }

        void OnDisable()
        {
            _toolBuild.Disable();
        }

        void OnDestroy()
        {
            _toolGate?.Stop();
            _inputActions?.Dispose();
        }

        // ── Tool-mode gate ──
        private ToolModeGate _toolGate;

        void Start()
        {
            mainCamera = Camera.main;
            pathGraph = DepotServices.Get<PathGraph>();
            visualBuilder = DepotServices.Get<PathVisualBuilder>();
            trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            groundGenerator = DepotServices.Get<GroundGenerator>();

            if (groundGenerator != null)
                buildableArea = groundGenerator.BuildableArea;

            _toolGate = new ToolModeGate(
                this,
                m => m == ToolMode.BuildPath || m == ToolMode.Demolish,
                OnToolDeactivated);
            _toolGate.Start();
        }

        private void OnToolDeactivated()
        {
            if (state != BuildState.Idle) CancelBuild();
            HidePreviewCell();
            HidePreviewHud();
            HideDemolishConfirm();
        }

        void Update()
        {
            var tool = DepotUIManager.Instance.CurrentTool;

            if (tool == ToolMode.Demolish)
            {
                if (state != BuildState.Idle) CancelBuild();
                HidePreviewCell();
                HidePreviewHud();
                HandleDemolishPaths();
                return;
            }

            if (DepotUIManager.Instance.IsPointerOverUI())
            {
                HidePreviewCell();
                HidePreviewHud();
                return;
            }

            var subMode = DepotUIManager.Instance.CurrentPathSubMode;

            if (subMode == PathBuildSubMode.Parking)
                HandleParkingMode();
            else
                HandlePaintMode(subMode);
        }

        void LateUpdate()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            if (previewInfoTextMesh != null && previewInfoTextMesh.gameObject.activeSelf)
                previewInfoTextMesh.transform.rotation = mainCamera.transform.rotation;

            if (previewStatusTextMesh != null && previewStatusTextMesh.gameObject.activeSelf)
                previewStatusTextMesh.transform.rotation = mainCamera.transform.rotation;
        }

        // ═══════════════════════════════════════════
        //  HELPERS — cell/world conversion + pending visuals
        // ═══════════════════════════════════════════

        private void UpdatePendingVisuals()
        {
            ClearPendingVisuals();
            foreach (var cell in pendingCells)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "PendingCell";
                quad.transform.rotation = Quaternion.Euler(90, 0, 0);
                Object.Destroy(quad.GetComponent<MeshCollider>());
                var r = quad.GetComponent<MeshRenderer>();
                var m = MaterialFactory.CreateLine();
                MaterialFactory.SetBaseColor(m, validColor);
                r.material = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                Vector3 center = CellToWorld(cell);
                quad.transform.position = new Vector3(center.x, 0.04f, center.z);
                quad.transform.localScale = new Vector3(gridSize * 0.95f, gridSize * 0.95f, 1f);
                pendingVisuals.Add(quad);
            }
        }

        private void ClearPendingVisuals()
        {
            foreach (var v in pendingVisuals)
                if (v != null) Destroy(v);
            pendingVisuals.Clear();
        }

        private void CancelBuild()
        {
            state = BuildState.Idle;
            pendingCells.Clear();
            ClearPendingVisuals();
            HidePreviewCell();
            HidePreviewFill();
            HidePreviewHud();
            parkingEndSelectionPaused = false;
            previewValidationReason = PathBuildValidationReason.None;
        }

        private Vector2Int WorldToCell(Vector3 worldPos)
        {
            return new Vector2Int(
                Mathf.FloorToInt(worldPos.x / gridSize),
                Mathf.FloorToInt(worldPos.z / gridSize)
            );
        }

        private Vector3 CellToWorld(Vector2Int cell)
        {
            return new Vector3(
                (cell.x + 0.5f) * gridSize,
                0f,
                (cell.y + 0.5f) * gridSize
            );
        }

        private Vector3 SnapToGrid(Vector3 position)
        {
            if (gridSize <= 0) return new Vector3(position.x, 0f, position.z);
            return new Vector3(
                Mathf.Round(position.x / gridSize) * gridSize,
                0f,
                Mathf.Round(position.z / gridSize) * gridSize
            );
        }

        private Vector3? GetMouseWorldPosition()
        {
            if (mainCamera == null) return null;
            if (Mouse.current == null) return null;
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = mainCamera.ScreenPointToRay(mousePos);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float enter))
                return ray.GetPoint(enter);
            return null;
        }

        public List<PathCellSnapshot> GetPlacedCellSnapshots()
        {
            var result = new List<PathCellSnapshot>(placedCells.Count);
            foreach (var cell in placedCells)
            {
                result.Add(new PathCellSnapshot
                {
                    x = cell.x,
                    y = cell.y,
                    subMode = cellTypes.TryGetValue(cell, out var mode)
                        ? mode
                        : InferCellSubMode(cell)
                });
            }
            return result;
        }

        public void RestorePlacedCellsFromSave(IList<PathCellSnapshot> snapshots)
        {
            if (pathGraph == null) pathGraph = DepotServices.Get<PathGraph>();
            if (visualBuilder == null) visualBuilder = DepotServices.Get<PathVisualBuilder>();

            state = BuildState.Idle;
            pendingCells.Clear();
            ClearPendingVisuals();
            HidePreviewCell();
            HidePreviewFill();
            HidePreviewHud();
            parkingEndSelectionPaused = false;
            previewValidationReason = PathBuildValidationReason.None;

            foreach (var kv in cellVisuals)
                if (kv.Value != null) Destroy(kv.Value);
            placedCells.Clear();
            cellToNodeId.Clear();
            cellVisuals.Clear();
            cellTypes.Clear();

            if (snapshots == null) return;

            foreach (var snap in snapshots)
            {
                if (snap == null) continue;
                var cell = new Vector2Int(snap.x, snap.y);
                placedCells.Add(cell);
                cellTypes[cell] = snap.subMode;

                Vector3 center = CellToWorld(cell);
                if (pathGraph != null)
                {
                    int nodeId = pathGraph.GetNearestNode(center, Mathf.Max(0.1f, gridSize * 0.5f));
                    if (nodeId >= 0)
                        cellToNodeId[cell] = nodeId;
                }

                if (visualBuilder != null)
                {
                    var visual = visualBuilder.PlaceCell(center, gridSize, ToPathEdgeType(snap.subMode));
                    if (visual != null)
                        cellVisuals[cell] = visual;
                }
            }
        }

        private PathBuildSubMode InferCellSubMode(Vector2Int cell)
        {
            if (cellToNodeId.TryGetValue(cell, out int nodeId))
            {
                var node = pathGraph?.GetNode(nodeId);
                if (node != null)
                {
                    foreach (int edgeId in node.EdgeIds)
                    {
                        var edge = pathGraph.GetEdge(edgeId);
                        if (edge == null) continue;
                        return edge.EdgeType == PathEdgeType.Road
                            ? PathBuildSubMode.Road
                            : PathBuildSubMode.Path;
                    }
                }
            }
            return PathBuildSubMode.Path;
        }

        private static PathEdgeType ToPathEdgeType(PathBuildSubMode subMode)
            => subMode == PathBuildSubMode.Road ? PathEdgeType.Road : PathEdgeType.Path;
    }

    [System.Serializable]
    public class PathCellSnapshot
    {
        public int x;
        public int y;
        public PathBuildSubMode subMode = PathBuildSubMode.Path;
    }

    public enum PathBuildValidationReason
    {
        None,
        Ready,
        OccupiedCell,
        OutsideBuildableArea,
        TrackCrossingBlocked,
        TurnoutCrossingBlocked,
        BrushPartiallyBlocked,
        RectTooSmall,
        RectOverlapsTracks
    }
}
