using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;

namespace DepotSystem
{
    /// <summary>
    /// Obsługa interakcji stawiania rozjazdów: hover, preview, click.
    /// Delegowana z TrackBuildStateMachine gdy sub-mode != Track.
    /// Używa StraightChain — łańcucha połączonych odcinków prostych.
    ///
    /// Klasa rozbita na partial files:
    /// - <c>TurnoutPlacementStateMachine.cs</c>            — pola, lifecycle, HandleUpdate, ClearPreview,
    ///                                                       definitions getters (ten plik)
    /// - <c>TurnoutPlacementStateMachine.Hover.cs</c>      — HandleTurnoutHover, HandleDoubleCrossoverHover
    /// - <c>TurnoutPlacementStateMachine.Preview.cs</c>    — Show/Hide/Clear Preview (turnout, crossover,
    ///                                                       pair, branch return + dialog)
    /// - <c>TurnoutPlacementStateMachine.Place.cs</c>      — ExecuteTurnoutPlacement, ExecuteDoubleCrossover,
    ///                                                       CheckBranchCollision
    /// - <c>TurnoutPlacementStateMachine.Validation.cs</c> — overlap (tracks/buildings), buildable area,
    ///                                                       snap, endpoint extensibility, CanPlaceWithFlip
    /// - <c>TurnoutPlacementStateMachine.Helpers.cs</c>    — chain finding (mouse → track/endpoint),
    ///                                                       freestanding chain, preview line construction
    /// </summary>
    public partial class TurnoutPlacementStateMachine : MonoBehaviour
    {
        [Header("Preview")]
        public Color previewValidColor = new Color(0.3f, 1f, 0.3f, 0.8f);
        public Color previewInvalidColor = new Color(1f, 0.3f, 0.3f, 0.8f);
        public float previewWidth = 0.2f;
        public float previewHeight = 0.25f;

        private TurnoutPlacer turnoutPlacer;
        private PrefabTrackBuilder trackBuilder;
        private Camera mainCamera;
        private Bounds? buildableArea;

        // Preview objects
        private GameObject straightPreview;
        private LineRenderer straightLine;
        private GameObject divergingPreview;
        private LineRenderer divergingLine;
        private GameObject originMarker;

        // Crossover extra preview lines (4 diverging legs for X pattern)
        private GameObject straightPreviewB;
        private LineRenderer straightLineB;
        private GameObject divergingPreviewB;
        private LineRenderer divergingLineB;
        private GameObject divergingPreviewC;
        private GameObject divergingPreviewD;
        private GameObject originMarkerB;

        // Pair mode preview lines
        private GameObject pairStraightPreview;
        private LineRenderer pairStraightLine;
        private GameObject pairDivergingPreview;
        private LineRenderer pairDivergingLine;
        private GameObject pairInsertPreview;
        private LineRenderer pairInsertLine;
        // Extra lines for crossover B (back leg + diagonal)
        private GameObject pairDivBackPreview;
        private LineRenderer pairDivBackLine;
        private GameObject pairDiagonalPreview;
        private LineRenderer pairDiagonalLine;

        // State — 4 pozycje rozjazdu: przód/tył × lewo/prawo
        private bool divergeLeft = true;
        private bool flipDirection = false; // false = przód, true = tył
        private bool pairMode = false; // P — parowanie z równoległym torem
        private int pairSidePreference = 0; // 0=auto, 1=lewo, 2=prawo
        private PlacedTrackSegment hoveredTrack;
        private StraightChain hoveredChain;
        private float hoveredDistAlongChain;
        private bool isValidPlacement;
        private bool isSnappedToEndpoint = false; // czy rozjazd jest zesnappowany do wolnego końca toru

        // Freestanding mode — kąt obrotu (stopnie) gdy nie snapuje do toru
        private float freestandingAngleDeg = 0f; // 0° = od lewej do prawej (Vector3.right)

        // Pair mode state
        private StraightChain pairChain;
        private float pairDistAlongChain;
        private bool isPairValid;
        private bool pairDivergeLeft; // auto-detected diverge side for pair

        // Branch return mode state (U key)
        private bool branchReturnMode = false;
        private float branchSpacing;
        private int branchReturnType;     // 0=arc, 1=R190, 2=R300
        private float branchReturnRadius;
        private bool branchReturnDialogOpen = false;

        // Branch return preview lines
        private GameObject branchInsertPreview;
        private LineRenderer branchInsertLine;
        private GameObject branchReturnPreview;
        private LineRenderer branchReturnLine;
        private GameObject branchReturnStraightPreview;
        private LineRenderer branchReturnStraightLine;
        private GameObject branchReturnDivPreview;
        private LineRenderer branchReturnDivLine;
        private GameObject branchParallelPreview;
        private LineRenderer branchParallelLine;

        // ── Input System ──
        private InputActions _inputActions;
        private InputActions.ToolBuildActions _toolBuild;
        private InputActions.ToolTurnoutActions _toolTurnout;

        void Awake()
        {
            _inputActions = new InputActions();
            RailwayManager.Core.Settings.RebindingService.ApplyOverridesTo(_inputActions);
            _toolBuild = _inputActions.ToolBuild;
            _toolTurnout = _inputActions.ToolTurnout;
        }

        void OnEnable()
        {
            _toolBuild.Enable();
            _toolTurnout.Enable();
        }

        void OnDisable()
        {
            _toolBuild.Disable();
            _toolTurnout.Disable();
        }

        void OnDestroy()
        {
            _inputActions?.Dispose();
        }

        void Start()
        {
            mainCamera = Camera.main;
            turnoutPlacer = DepotServices.Get<TurnoutPlacer>();
            trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            var groundGen = DepotServices.Get<GroundGenerator>();
            if (groundGen != null) buildableArea = groundGen.BuildableArea;
        }

        /// <summary>
        /// Wywoływana z TrackBuildStateMachine.Update() gdy sub-mode to rozjazd.
        /// </summary>
        public void HandleUpdate(TrackBuildSubMode subMode)
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return;
            if (turnoutPlacer == null) turnoutPlacer = DepotServices.Get<TurnoutPlacer>();
            if (trackBuilder == null) trackBuilder = DepotServices.Get<PrefabTrackBuilder>();

            // Wyczyść hoveredChain na początku klatki — musi być wyznaczony od nowa
            hoveredChain = null;
            hoveredTrack = null;

            // Ctrl + Scroll — obrót freestanding (po 5° na klik)
            // RotateScroll bound to Mouse/scroll in asset, we gate on Ctrl held in code
            // (Camera.Depot.Zoom also reads scroll but skips when Ctrl held - coordination).
            if (Keyboard.current != null && Keyboard.current.ctrlKey.isPressed)
            {
                Vector2 scrollVec = _toolTurnout.RotateScroll.ReadValue<Vector2>();
                float scroll = scrollVec.y;
                if (Mathf.Abs(scroll) > 0.001f)
                    freestandingAngleDeg = Mathf.Repeat(freestandingAngleDeg + (scroll > 0 ? 5f : -5f), 360f);
            }

            // R — obróć rozjazd
            if (_toolTurnout.Rotate.WasPressedThisFrame())
            {
                if (subMode == TrackBuildSubMode.DoubleCrossoverR190)
                {
                    // Krzyżowy: przełącz stronę łuków (lewo/prawo)
                    divergeLeft = !divergeLeft;
                }
                else if (isSnappedToEndpoint)
                {
                    // Zesnappowany do końca toru: tylko lewo/prawo (kierunek narzucony przez tor)
                    divergeLeft = !divergeLeft;
                }
                else
                {
                    // Freestanding / środek toru: cykl 4 pozycji
                    if (divergeLeft && !flipDirection)       // przód-lewo → tył-lewo
                        flipDirection = true;
                    else if (divergeLeft && flipDirection)   // tył-lewo → tył-prawo
                        divergeLeft = false;
                    else if (!divergeLeft && flipDirection)  // tył-prawo → przód-prawo
                        flipDirection = false;
                    else                                     // przód-prawo → przód-lewo
                        divergeLeft = true;
                }
            }

            // P — toggle pair mode
            if (_toolTurnout.PairToggle.WasPressedThisFrame())
            {
                pairMode = !pairMode;
                pairSidePreference = 0; // reset to auto
                if (DepotUIManager.Instance != null)
                    DepotUIManager.Instance.PairModeActive = pairMode;
            }

            // O — cykl strony pary: auto → lewo → prawo → auto
            if (pairMode && _toolTurnout.PairSideCycle.WasPressedThisFrame())
            {
                pairSidePreference = (pairSidePreference + 1) % 3;
                string[] labels = { "Auto", "Lewo", "Prawo" };
                Log.Info($"[Pair] Strona pary: {labels[pairSidePreference]}");
            }

            // U — tryb odgałęzienia z powrotem (nie działa z P ani z krzyżowym)
            if (_toolTurnout.BranchReturn.WasPressedThisFrame() && !pairMode
                && subMode != TrackBuildSubMode.DoubleCrossoverR190)
            {
                if (branchReturnMode)
                {
                    branchReturnMode = false;
                    ClearBranchReturnPreview();
                    Log.Info("[BranchReturn] Tryb U wyłączony");
                }
                else if (!branchReturnDialogOpen)
                {
                    ShowBranchReturnDialog(GetDefinition(subMode));
                    return;
                }
            }

            // Gdy dialog jest otwarty, nie przetwarzaj dalej
            if (branchReturnDialogOpen) return;

            // ESC / RMB — anuluj (Cancel action)
            if (_toolBuild.Cancel.WasPressedThisFrame())
            {
                if (branchReturnMode)
                {
                    branchReturnMode = false;
                    ClearBranchReturnPreview();
                    return;
                }
                ClearPreview();
                if (DepotUIManager.Instance != null)
                    DepotUIManager.Instance.CurrentTrackSubMode = TrackBuildSubMode.Track;
                return;
            }

            // 1a. MULTI-ANCHOR snap — sprawdza czy któryś z 3 endpointów NOWEGO rozjazdu (Origin /
            //     BodyFarEnd / DivergingEnd) jest blisko istniejącego endpoint toru. Jeśli tak,
            //     przesuwa NEW.origin tak by ten endpoint pokrywał target. Działa dla freestanding
            //     placement i obsługuje WSZYSTKIE 3 endpointy nowego rozjazdu (nie tylko origin).
            var def = GetDefinition(subMode);
            var endpointResult = FindMultiAnchorSnap(def);

            // 1b. Fallback — magnetic snap bezpośrednio do endpointów istniejących TurnoutEntity.
            if (endpointResult.chain == null)
                endpointResult = FindNearestTurnoutEndpointMagnet();

            // 1c. Fallback — wolny koniec toru (degree 1 lub 2-edge boundary).
            if (endpointResult.chain == null)
                endpointResult = FindNearbyEndpointChain();

            if (endpointResult.chain != null)
            {
                hoveredChain = endpointResult.chain;
                hoveredDistAlongChain = endpointResult.distAlong;
                hoveredTrack = endpointResult.track;
                isSnappedToEndpoint = true;

                // Auto-flip: body rozjazdu musi rosnąć NA ZEWNĄTRZ istniejącego toru
                // Synthetic chain (koniec krzywego toru): polyline JUŻ wskazuje na zewnątrz → flip=false
                // Straight chain @ distAlong=0: tangent wskazuje wgłąb → flip=true
                // Straight chain @ distAlong=totalLength: tangent wskazuje na zewnątrz → flip=false
                bool isSynthetic = endpointResult.chain.Segments == null || endpointResult.chain.Segments.Count == 0;
                if (isSynthetic)
                    flipDirection = false;
                else
                    flipDirection = endpointResult.distAlong < 0.01f;
            }
            else
            {
                isSnappedToEndpoint = false;

                // 2. Raycast na ciało toru (jeśli kursor jest nad torem)
                var (track, distAlongTrack) = FindTrackUnderMouse();
                hoveredTrack = track;

                if (track != null && turnoutPlacer != null)
                {
                    hoveredChain = turnoutPlacer.FindStraightChain(track);
                    if (hoveredChain != null)
                        hoveredDistAlongChain = turnoutPlacer.ConvertDistToChain(hoveredChain, track, distAlongTrack);
                }

                // 3. Freestanding: kursor poza torami i brak wolnego końca w 5m
                //    → syntetyczny chain w pozycji kursora, kierunek = camera forward
                if (hoveredChain == null)
                {
                    var freePos = GetMouseGroundPos();
                    if (freePos.HasValue)
                    {
                        hoveredChain = CreateFreestandingChain(freePos.Value);
                        hoveredDistAlongChain = 0f;
                        hoveredTrack = null;
                    }
                }
            }

            if (subMode == TrackBuildSubMode.DoubleCrossoverR190)
                HandleDoubleCrossoverHover();
            else
                HandleTurnoutHover(subMode);

            // LMB — postaw rozjazd
            if (_toolBuild.Primary.WasPressedThisFrame() && isValidPlacement && hoveredChain != null)
            {
                if (subMode == TrackBuildSubMode.DoubleCrossoverR190)
                    ExecuteDoubleCrossover();
                else
                    ExecuteTurnoutPlacement(subMode);

                ClearPreview();
            }
        }

        public void ClearPreview()
        {
            if (straightPreview != null) { Destroy(straightPreview); straightPreview = null; straightLine = null; }
            if (divergingPreview != null) { Destroy(divergingPreview); divergingPreview = null; divergingLine = null; }
            if (originMarker != null) { Destroy(originMarker); originMarker = null; }
            if (straightPreviewB != null) { Destroy(straightPreviewB); straightPreviewB = null; straightLineB = null; }
            if (divergingPreviewB != null) { Destroy(divergingPreviewB); divergingPreviewB = null; divergingLineB = null; }
            if (divergingPreviewC != null) { Destroy(divergingPreviewC); divergingPreviewC = null; }
            if (divergingPreviewD != null) { Destroy(divergingPreviewD); divergingPreviewD = null; }
            if (originMarkerB != null) { Destroy(originMarkerB); originMarkerB = null; }
            if (pairStraightPreview != null) { Destroy(pairStraightPreview); pairStraightPreview = null; pairStraightLine = null; }
            if (pairDivergingPreview != null) { Destroy(pairDivergingPreview); pairDivergingPreview = null; pairDivergingLine = null; }
            if (pairInsertPreview != null) { Destroy(pairInsertPreview); pairInsertPreview = null; pairInsertLine = null; }
            if (pairDivBackPreview != null) { Destroy(pairDivBackPreview); pairDivBackPreview = null; pairDivBackLine = null; }
            if (pairDiagonalPreview != null) { Destroy(pairDiagonalPreview); pairDiagonalPreview = null; pairDiagonalLine = null; }
            pairChain = null;
            isPairValid = false;
            hoveredTrack = null;
            ClearBranchReturnPreview();
            hoveredChain = null;
            isValidPlacement = false;
        }

        // ═══════════════════════════════════════════
        //  SUB-MODE → DEFINITION mapping
        // ═══════════════════════════════════════════

        private TurnoutData.TurnoutDefinition GetDefinition(TrackBuildSubMode subMode)
        {
            return subMode switch
            {
                TrackBuildSubMode.TurnoutR190 => TurnoutData.R190_1_9,
                TrackBuildSubMode.TurnoutR300 => TurnoutData.R300_1_9,
                TrackBuildSubMode.DoubleCrossoverR190 => TurnoutData.Crossover_R190,
                _ => TurnoutData.R190_1_9
            };
        }

        private TurnoutData.TurnoutDefinition GetPairSecondaryDefinition(TurnoutData.TurnoutDefinition primaryDef)
        {
            var mgr = DepotUIManager.Instance;
            if (mgr == null) return primaryDef;

            return mgr.CurrentPairSecondary switch
            {
                PairSecondaryType.R190 => TurnoutData.R190_1_9,
                PairSecondaryType.R300 => TurnoutData.R300_1_9,
                PairSecondaryType.Crossover => TurnoutData.Crossover_R190,
                _ => primaryDef // SameAsPrimary
            };
        }

        private bool IsCrossoverDefinition(TurnoutData.TurnoutDefinition def)
        {
            return def.Name == TurnoutData.Crossover_R190.Name;
        }
    }
}
