using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;

namespace DepotSystem
{
    /// <summary>
    /// Maszyna stanów budowy torów: A→B z snap, preview i trybem zaawansowanym.
    /// Reaguje na DepotUIManager.CurrentTool == BuildTrack.
    /// Współpracuje z SnapPointSystem, TrackGeometry, PrefabTrackBuilder.
    ///
    /// Klasa rozbita na partial files:
    /// - <c>TrackBuildStateMachine.cs</c>            — pola, lifecycle, Update dispatcher,
    ///                                                 stany (HandlePlacingStart/PreviewingEnd),
    ///                                                 CancelBuild, helpers (mouse/snap/grid),
    ///                                                 SetAdvancedMode (public) (ten plik)
    /// - <c>TrackBuildStateMachine.Build.cs</c>      — BuildTrackAB (final placement +
    ///                                                 CSC/arc/straight selection), GetDirectionForPoint
    /// - <c>TrackBuildStateMachine.Preview.cs</c>    — UpdatePreview (geometria + walidacja),
    ///                                                 ShowErrorPreview, CreatePreviewObject,
    ///                                                 CreatePointAMarker, ClearPreview,
    ///                                                 CalculatePureArcRadius/GeneratePureArc
    /// - <c>TrackBuildStateMachine.Validation.cs</c> — IsPolylineOverlapping (z AABB early-out),
    ///                                                 IsPolylineOverlappingBuildings, IsPointNearPolyline,
    ///                                                 DistSqPointSegment2D
    /// </summary>
    public partial class TrackBuildStateMachine : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TrackGraph trackGraph;
        [SerializeField] private PrefabTrackBuilder trackBuilder;
        [SerializeField] private SnapPointSystem snapSystem;
        private TurnoutPlacementStateMachine turnoutStateMachine;

        [Header("Build Settings")]
        [Tooltip("Promień snap do istniejących node'ów")]
        public float snapRadius = 3f;

        [Tooltip("Rozmiar siatki do grid-snap (0 = wyłączony)")]
        public float gridSize = 1f;

        [Tooltip("Minimalny promień łuku (m)")]
        public float minRadius = 75f;

        [Header("Preview")]
        [Tooltip("Kolor podglądu trasy")]
        public Color previewColor = new Color(1f, 0.9f, 0.3f, 0.8f);

        [Tooltip("Kolor podglądu (błąd/za mały R)")]
        public Color previewErrorColor = new Color(1f, 0.3f, 0.3f, 0.8f);

        [Tooltip("Szerokość linii podglądu")]
        public float previewWidth = 0.25f;

        [Header("Preview HUD")]
        [Tooltip("Szacunkowy koszt preview za metr toru (tylko informacja wizualna).")]
        [SerializeField] private long estimatedCostPerMeterPln = 1200;

        [Tooltip("Wysokość panelu informacyjnego preview nad torem.")]
        [SerializeField] private float previewInfoHeight = 2.4f;

        [Tooltip("Wysokość statusu/powodu walidacji nad torem.")]
        [SerializeField] private float previewStatusHeight = 3.9f;

        [Header("Snap Direction Preview")]
        [Tooltip("Kolor strzalki kierunku aktywnego snapa.")]
        [SerializeField] private Color snapDirectionColor = new Color(0.5f, 1f, 0.8f, 0.95f);

        [Tooltip("Wysokosc strzalki kierunku nad wezlem.")]
        [SerializeField] private float snapDirectionHeight = 0.38f;

        [Tooltip("Dlugosc glownej strzalki kierunku snapa.")]
        [SerializeField] private float snapDirectionLength = 2.8f;

        [Tooltip("Dlugosc grota strzalki kierunku snapa.")]
        [SerializeField] private float snapDirectionArrowHeadLength = 0.65f;

        [Tooltip("Kat grota strzalki kierunku snapa.")]
        [SerializeField] private float snapDirectionArrowHeadAngle = 28f;

        [Header("Delete Preview")]
        [Tooltip("Maksymalna odleglosc kursora od toru dla trybu usuwania.")]
        [SerializeField] private float deletePreviewMaxDistance = 5f;

        [Tooltip("Kolor podswietlenia toru gotowego do usuniecia.")]
        [SerializeField] private Color deletePreviewColor = new Color(1f, 0.42f, 0.24f, 0.95f);

        [Tooltip("Kolor podswietlenia toru, ktorego nie mozna usunac.")]
        [SerializeField] private Color deletePreviewBlockedColor = new Color(1f, 0.75f, 0.28f, 0.95f);

        [Header("Map Reference")]
        [Tooltip("Kąt torów zewnętrznych względem osi Z+ (world). Ustaw tu azymut toru zewnętrznego — stanie się 0°.")]
        [SerializeField] private float mapNorthAngle = 90f;

        [Header("Advanced Mode")]
        [Tooltip("Czy tryb zaawansowany jest aktywny")]
        public bool advancedMode = false;

        [Tooltip("Promień R w trybie zaawansowanym (0 = auto-fit)")]
        public float manualRadius = 0f;

        // Stan maszyny
        private BuildState state = BuildState.Idle;
        private Vector3 pointA;
        private Vector3 pointB;
        private int snappedNodeA = -1;
        private int snappedNodeB = -1;
        private Vector3 directionA = Vector3.forward;
        private bool hasPinnedStartDirection = false;

        // Preview
        private GameObject previewObj;
        private LineRenderer previewLine;
        private TextMesh angleTextMesh;
        private TextMesh statusTextMesh;
        private GameObject snapDirectionPreviewObj;
        private LineRenderer snapDirectionPreviewLine;
        private GameObject pointAMarker;
        private bool previewIsValid = false;
        private TrackBuildValidationReason previewValidationReason = TrackBuildValidationReason.None;
        private List<Vector3> previewPolyline;
        private float previewLengthMeters;
        private float previewDisplayRadiusMeters;
        private long previewEstimatedCostPln;
        private bool previewIsStraight;
        private bool previewUsesManualRadius;
        private string previewAnchorModeLabel = "GRID";
        private bool endSelectionPaused = false;
        private Vector3 pausedEndSelectionPoint;
        private int pausedEndSelectionSnapNodeId = -1;
        private string pausedEndSelectionMessage = DefaultPausedEndSelectionMessage;
        private bool deletePreviewActive = false;

        private const string DefaultPausedEndSelectionMessage =
            "Punkt startowy zachowany • porusz myszą aby wskazac nowy koniec • ESC wylacz";
        private const string ChainBuildPausedMessage =
            "Nowy start gotowy • porusz myszą aby poprowadzic kolejny odcinek • ESC anuluj";

        private const string DeletePreviewPausedMessage =
            "Tryb usuwania toru â€˘ wskaz tor do usuniecia â€˘ ESC wroc";

        private struct DeletePreviewTarget
        {
            public PlacedTrackSegment Segment;
            public DepotTrackData TrackData;
            public float DistanceMeters;
            public bool IsPermanent;
            public bool RemovesEntireTurnout;
            public bool CanRemove;
        }

        // Cache
        private Camera mainCamera;
        private Bounds? buildableArea;
        private TrackBuildSubMode lastSubMode = TrackBuildSubMode.Track;

        public BuildState CurrentState => state;
        public TrackBuildValidationReason CurrentValidationReason => previewValidationReason;

        /// <summary>Event: tor zbudowany (graphTrackId)</summary>
        public event System.Action<int> OnTrackBuilt;

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
            if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();
            if (trackBuilder == null) trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            if (snapSystem == null) snapSystem = DepotServices.Get<SnapPointSystem>();
            if (turnoutStateMachine == null) turnoutStateMachine = DepotServices.Get<TurnoutPlacementStateMachine>();

            var groundGen = DepotServices.Get<GroundGenerator>();
            if (groundGen != null) buildableArea = groundGen.BuildableArea;

            _toolGate = new ToolModeGate(this, m => m == ToolMode.BuildTrack, OnToolDeactivated);
            _toolGate.Start();
        }

        private void OnToolDeactivated()
        {
            UpdateHoveredSnapFeedback(-1);
            if (state != BuildState.Idle)
                CancelBuild();
            if (turnoutStateMachine != null)
                turnoutStateMachine.ClearPreview();
        }

        /// <summary>
        /// Sprawdza czy punkt jest w obszarze budowlanym.
        /// </summary>
        private bool IsInsideBuildableArea(Vector3 point)
        {
            if (!buildableArea.HasValue) return true;
            var ba = buildableArea.Value;
            return point.x >= ba.min.x && point.x <= ba.max.x
                && point.z >= ba.min.z && point.z <= ba.max.z;
        }

        void LateUpdate()
        {
            // Billboard: tekst kąta zawsze skierowany w stronę kamery
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;

            if (angleTextMesh != null && angleTextMesh.gameObject.activeSelf)
                angleTextMesh.transform.rotation = mainCamera.transform.rotation;

            if (statusTextMesh != null && statusTextMesh.gameObject.activeSelf)
                statusTextMesh.transform.rotation = mainCamera.transform.rotation;
        }

        void Update()
        {
            if (DepotUIManager.Instance.IsPointerOverUI())
            {
                UpdateHoveredSnapFeedback(-1);
                return;
            }

            // Deleguj do TurnoutPlacementStateMachine gdy sub-mode != Track
            var subMode = DepotUIManager.Instance.CurrentTrackSubMode;

            // Gdy zmienił się sub-mode, wyczyść stary preview
            if (subMode != lastSubMode)
            {
                if (turnoutStateMachine != null)
                    turnoutStateMachine.ClearPreview();
                lastSubMode = subMode;
            }

            // SubMode Schemas (M-DepotTools) obsługiwany przez TurnoutSchemaPlacer/SchemaPanelUI,
            // nie przez TrackBuildStateMachine ani TurnoutPlacementStateMachine. No-op tutaj.
            if (subMode == TrackBuildSubMode.Schemas)
            {
                // Anuluj ewentualny track build w toku + wyczyść preview rozjazdów
                UpdateHoveredSnapFeedback(-1);
                if (state != BuildState.Idle)
                    CancelBuild();
                if (turnoutStateMachine != null)
                    turnoutStateMachine.ClearPreview();
                return;
            }

            if (subMode != TrackBuildSubMode.Track)
            {
                // Anuluj ewentualny track build w toku
                UpdateHoveredSnapFeedback(-1);
                if (state != BuildState.Idle)
                    CancelBuild();

                if (turnoutStateMachine == null)
                    turnoutStateMachine = DepotServices.Get<TurnoutPlacementStateMachine>();
                if (turnoutStateMachine != null)
                    turnoutStateMachine.HandleUpdate(subMode);
                return;
            }

            // Wyczyść preview rozjazdów przy powrocie do Track mode
            if (turnoutStateMachine != null)
                turnoutStateMachine.ClearPreview();

            if (deletePreviewActive)
            {
                HandleDeletePreview();
                return;
            }

            switch (state)
            {
                case BuildState.Idle:
                case BuildState.PlacingStart:
                    HandlePlacingStart();
                    break;

                case BuildState.PreviewingEnd:
                    HandlePreviewingEnd();
                    break;
            }
        }

        // ═══════════════════════════════════════════
        //  STANY
        // ═══════════════════════════════════════════

        private void HandlePlacingStart()
        {
            Vector3 worldPos = GetMouseWorldPosition();
            if (worldPos == Vector3.zero) return;

            // Snap detection
            var (snapNodeId, snapPos) = FindSnapOrGrid(worldPos);

            UpdateHoveredSnapFeedback(snapNodeId);

            // ESC / RMB - wyłącz narzędzie
            if (_toolBuild.Cancel.WasPressedThisFrame())
            {
                CancelBuild();
                return;
            }

            // Delete - usuń najbliższy tor
            if (_toolBuild.Delete.WasPressedThisFrame())
            {
                EnterDeletePreviewMode();
                return;
            }

            // LMB - ustaw punkt A
            if (_toolBuild.Primary.WasPressedThisFrame())
            {
                pointA = snapNodeId >= 0 ? snapPos : SnapToGrid(worldPos);
                if (!IsInsideBuildableArea(pointA)) return;
                snappedNodeA = snapNodeId;

                // Kierunek A: z istniejącego node'a lub domyślny
                if (snappedNodeA >= 0 && trackGraph != null)
                {
                    directionA = trackGraph.GetNodeDirection(snappedNodeA);
                    hasPinnedStartDirection = true;
                }
                else
                {
                    directionA = Vector3.forward; // Będzie obliczony gdy ustalimy B
                    hasPinnedStartDirection = false;
                }

                ResetPausedEndSelection();
                state = BuildState.PreviewingEnd;
                CreatePointAMarker(pointA);
            }
        }

        private void HandlePreviewingEnd()
        {
            Vector3 worldPos = GetMouseWorldPosition();
            if (worldPos == Vector3.zero) return;

            // Snap detection
            var (snapNodeId, snapPos) = FindSnapOrGrid(worldPos);

            Vector3 currentB = snapNodeId >= 0 ? snapPos : SnapToGrid(worldPos);
            UpdateHoveredSnapFeedback(snapNodeId);

            // ESC / RMB - cofnij wybor konca; drugi ESC wstrzymanego wyboru zamyka narzedzie.
            if (_toolBuild.Cancel.WasPressedThisFrame())
            {
                if (endSelectionPaused)
                    CancelBuild();
                else
                    PauseEndSelection(currentB, snapNodeId);
                return;
            }

            if (_toolBuild.Delete.WasPressedThisFrame())
            {
                EnterDeletePreviewMode(currentB, snapNodeId);
                return;
            }

            if (endSelectionPaused)
            {
                if (!ShouldResumeEndSelection(currentB, snapNodeId))
                {
                    ShowPausedEndSelectionOverlay();
                    return;
                }

                ResetPausedEndSelection();
            }

            // Preview trasy
            UpdatePreview(pointA, currentB, snappedNodeA, snapNodeId);

            // LMB - ustaw punkt B i zbuduj tor
            if (_toolBuild.Primary.WasPressedThisFrame())
            {
                if (!IsInsideBuildableArea(currentB)) return;

                if (Vector3.Distance(pointA, currentB) < TrackGeometry.MIN_TRACK_LENGTH)
                    return; // Za krótki

                if (!previewIsValid)
                    return; // Trasa nieprawidłowa (za mały R, brak rozwiązania)

                pointB = currentB;
                snappedNodeB = snapNodeId;

                if (BuildTrackAB(out var builtSegment, out var continuationDirection))
                {
                    ContinueChainBuildFrom(builtSegment, continuationDirection);
                    return;
                }
            }
        }

        // ═══════════════════════════════════════════
        //  HELPERS — cancel + mouse/snap/grid + advanced mode toggle
        // ═══════════════════════════════════════════

        private void CancelBuild()
        {
            state = BuildState.Idle;
            snappedNodeA = -1;
            snappedNodeB = -1;
            directionA = Vector3.forward;
            hasPinnedStartDirection = false;
            previewIsValid = false;
            previewValidationReason = TrackBuildValidationReason.None;
            previewPolyline = null;
            previewLengthMeters = 0f;
            previewDisplayRadiusMeters = 0f;
            previewEstimatedCostPln = 0L;
            previewIsStraight = false;
            previewUsesManualRadius = false;
            previewAnchorModeLabel = "GRID";
            ResetPausedEndSelection();
            deletePreviewActive = false;
            ClearPreview();

            UpdateHoveredSnapFeedback(-1);

            if (turnoutStateMachine != null)
                turnoutStateMachine.ClearPreview();
        }

        private void PauseEndSelection(Vector3 currentEndPoint, int snapNodeId, string message = null)
        {
            endSelectionPaused = true;
            pausedEndSelectionPoint = currentEndPoint;
            pausedEndSelectionSnapNodeId = snapNodeId;
            pausedEndSelectionMessage = string.IsNullOrWhiteSpace(message)
                ? DefaultPausedEndSelectionMessage
                : message;
            previewIsValid = false;
            previewValidationReason = TrackBuildValidationReason.None;
            previewPolyline = null;
            previewLengthMeters = 0f;
            previewDisplayRadiusMeters = 0f;
            previewEstimatedCostPln = 0L;
            previewIsStraight = false;
            previewUsesManualRadius = false;
            previewAnchorModeLabel = "GRID";
            HidePreviewVisuals();
            UpdateHoveredSnapFeedback(-1);
            ShowPausedEndSelectionOverlay();
        }

        private bool ShouldResumeEndSelection(Vector3 currentEndPoint, int snapNodeId)
        {
            if (snapNodeId != pausedEndSelectionSnapNodeId)
                return true;

            float minMoveDistance = gridSize > 0f
                ? Mathf.Max(0.25f, gridSize * 0.35f)
                : 0.25f;

            return Vector3.Distance(currentEndPoint, pausedEndSelectionPoint) >= minMoveDistance;
        }

        private void ResetPausedEndSelection()
        {
            endSelectionPaused = false;
            pausedEndSelectionPoint = Vector3.zero;
            pausedEndSelectionSnapNodeId = -1;
            pausedEndSelectionMessage = DefaultPausedEndSelectionMessage;
        }

        private void ContinueChainBuildFrom(PlacedTrackSegment builtSegment, Vector3 continuationDirection)
        {
            if (builtSegment == null)
            {
                state = BuildState.PlacingStart;
                ClearPreview();
                return;
            }

            pointA = builtSegment.EndPosition;
            snappedNodeA = trackGraph != null ? trackGraph.FindNodeAtPosition(pointA) : -1;
            snappedNodeB = -1;
            directionA = continuationDirection.sqrMagnitude > 0.0001f
                ? continuationDirection.normalized
                : Vector3.forward;
            hasPinnedStartDirection = true;
            state = BuildState.PreviewingEnd;
            ResetPausedEndSelection();
            CreatePointAMarker(pointA);
            PauseEndSelection(pointA, snappedNodeA, ChainBuildPausedMessage);
        }

        private void EnterDeletePreviewMode()
        {
            deletePreviewActive = true;
            previewIsValid = false;
            previewValidationReason = TrackBuildValidationReason.None;
            UpdateHoveredSnapFeedback(-1);
        }

        private void EnterDeletePreviewMode(Vector3 currentEndPoint, int snapNodeId)
        {
            if (!endSelectionPaused)
                PauseEndSelection(currentEndPoint, snapNodeId, DeletePreviewPausedMessage);

            EnterDeletePreviewMode();
        }

        private void HandleDeletePreview()
        {
            Vector3 worldPos = GetMouseWorldPosition();
            if (worldPos == Vector3.zero)
                return;

            UpdateHoveredSnapFeedback(-1);

            bool hasTarget = TryGetDeletePreviewTarget(worldPos, out var target);
            ShowDeletePreview(worldPos, hasTarget ? target : (DeletePreviewTarget?)null);

            if (_toolBuild.Cancel.WasPressedThisFrame())
            {
                ExitDeletePreviewMode();
                return;
            }

            if (!_toolBuild.Primary.WasPressedThisFrame() && !_toolBuild.Delete.WasPressedThisFrame())
                return;

            if (!hasTarget || target.Segment == null || !target.CanRemove || trackBuilder == null)
                return;

            trackBuilder.RemoveTrack(target.Segment.GraphTrackId);
        }

        private void ExitDeletePreviewMode()
        {
            deletePreviewActive = false;
            previewValidationReason = TrackBuildValidationReason.None;
            previewIsValid = false;

            if (state == BuildState.PreviewingEnd && endSelectionPaused)
                ShowPausedEndSelectionOverlay();
            else
                HidePreviewVisuals();
        }

        private Vector3 GetMouseWorldPosition()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return Vector3.zero;
            if (Mouse.current == null) return Vector3.zero;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = mainCamera.ScreenPointToRay(mousePos);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

            if (groundPlane.Raycast(ray, out float distance))
                return ray.GetPoint(distance);

            return Vector3.zero;
        }

        private (int nodeId, Vector3 position) FindSnapOrGrid(Vector3 worldPos)
        {
            // Najpierw sprawdź snap do istniejących node'ów
            if (snapSystem != null)
            {
                var (nodeId, snapPos) = snapSystem.FindNearestSnapPoint(worldPos, snapRadius);
                if (nodeId >= 0)
                    return (nodeId, snapPos);
            }

            // Grid snap
            return (-1, SnapToGrid(worldPos));
        }

        private void UpdateHoveredSnapFeedback(int snapNodeId)
        {
            if (snapSystem != null)
                snapSystem.SetHighlight(snapNodeId);

            UpdateSnapDirectionPreview(snapNodeId);
        }

        private bool TryGetDeletePreviewTarget(Vector3 worldPos, out DeletePreviewTarget target)
        {
            target = default;
            if (trackBuilder == null || trackGraph == null)
                return false;

            Vector2 cursor = new Vector2(worldPos.x, worldPos.z);
            float maxDistanceSq = deletePreviewMaxDistance * deletePreviewMaxDistance;
            float bestDistanceSq = maxDistanceSq;
            bool found = false;

            foreach (var placed in trackBuilder.PlacedTracks)
            {
                if (placed == null || placed.GraphTrackId < 0)
                    continue;

                List<Vector3> polyline = placed.Polyline;
                if ((polyline == null || polyline.Count < 2) && trackGraph != null)
                    polyline = trackGraph.GetTrackPolyline(placed.GraphTrackId);
                if (polyline == null || polyline.Count < 2)
                    continue;

                float distanceSq = DistSqPointPolyline2D(cursor, polyline);
                if (distanceSq > bestDistanceSq)
                    continue;

                var trackData = trackGraph.GetTrack(placed.GraphTrackId);
                bool isPermanent = trackData != null && trackData.IsPermanent;
                bool removesEntireTurnout = trackBuilder.TryGetTurnoutForTrack(placed.GraphTrackId, out _);

                bestDistanceSq = distanceSq;
                target = new DeletePreviewTarget
                {
                    Segment = placed,
                    TrackData = trackData,
                    DistanceMeters = Mathf.Sqrt(distanceSq),
                    IsPermanent = isPermanent,
                    RemovesEntireTurnout = removesEntireTurnout,
                    CanRemove = !isPermanent
                };
                found = true;
            }

            return found;
        }

        private static float DistSqPointPolyline2D(Vector2 point, List<Vector3> polyline)
        {
            float bestDistanceSq = float.MaxValue;
            for (int i = 1; i < polyline.Count; i++)
            {
                Vector2 a = new Vector2(polyline[i - 1].x, polyline[i - 1].z);
                Vector2 b = new Vector2(polyline[i].x, polyline[i].z);
                float distanceSq = DistSqPointSegment2D(point, a, b);
                if (distanceSq < bestDistanceSq)
                    bestDistanceSq = distanceSq;
            }

            return bestDistanceSq;
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

        private bool IsCurrentStartPoint(Vector3 point)
        {
            Vector2 currentStart = new Vector2(pointA.x, pointA.z);
            Vector2 candidate = new Vector2(point.x, point.z);
            return (currentStart - candidate).sqrMagnitude <= 0.0001f;
        }

        /// <summary>
        /// Ustawia tryb zaawansowany z ręcznym promieniem R.
        /// Wywoływane z panelu UI.
        /// </summary>
        public void SetAdvancedMode(bool enabled, float radius = 0f)
        {
            advancedMode = enabled;
            manualRadius = radius;
        }
    }

    public enum BuildState
    {
        Idle,
        PlacingStart,
        PreviewingEnd
    }

    public enum TrackBuildValidationReason
    {
        None,
        Ready,
        TooShort,
        DuplicateExistingTrack,
        NoValidRoute,
        RadiusTooTight,
        OutsideBuildableArea,
        OverlapExistingTrack,
        OverlapBuilding
    }
}
