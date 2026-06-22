using UnityEngine;
using RailwayManager.Core;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    public enum ToolMode
    {
        Select,
        BuildTrack,
        BuildCatenary,
        BuildPath,
        BuildRoom,
        Furniture,    // M-Furniture (MF-3) — Furniture Tool: placement obiektow w pomieszczeniach
        Demolish
    }

    public enum TrackBuildSubMode
    {
        Track,
        TurnoutR190,
        TurnoutR300,
        DoubleCrossoverR190,
        Schemas,           // M-DepotTools — schematy głowic rozjazdowych (otwiera SchemaPanelUI)
        WashZone,
        Turntable,
        PitLift,
        FuelStation,       // MM-9 / MM-D14 — stacja paliw outdoor (DMU spalinowe)
        WaterService       // MM-17 — wodowanie (woda + zb. fekaliów, pasażerskie)
    }

    public enum CatenaryBuildSubMode
    {
        AddCatenary,
        RemoveCatenary
    }

    public enum RoomBuildSubMode
    {
        Hall,
        Storage,
        Dispatcher,
        Office,
        Social,
        Supervisor,
        Bathroom,
        Locker,
        Corridor,
        TrafficController
    }

    public enum PathBuildSubMode
    {
        Path,       // Sciezka piesza — moze przecinac tory, nie rozjazdy
        Road,       // Droga — nie moze przecinac torow
        Parking     // Parking — obiekt prostokatny
    }

    public enum RoomActionMode
    {
        None,
        BuildRoom,
        PlaceHallTrack,
        /// <summary>
        /// Elektryfikacja torów w hali. Status: TBD (OQ — czy chcemy tego w game design).
        /// Aktualnie button EL pokazuje się ale handler to stub bez logiki.
        /// </summary>
        ElectrifyHall,
        // PlaceWashBay/PlaceLift removed 2026-05-08 — myjnia/podnośnik zastąpione przez
        // outdoor equipment (TOR sub-mode → WSH/POD) + meble (wash_gate/pit_*) w MM-9.
    }

    public enum PairSecondaryType
    {
        SameAsPrimary,
        R190,
        R300,
        Crossover
    }

    /// <summary>
    /// Singleton UI Manager dla zajezdni.
    /// Automatycznie tworzy Canvas, EventSystem, TopBar, Toolbar, TrainListPanel.
    /// Jedyny wymagany setup w Unity: pusty GameObject z tym skryptem.
    /// </summary>
    public class DepotUIManager : MonoBehaviour
    {
        public static DepotUIManager Instance { get; private set; }

        // Auto-created references
        [HideInInspector] public TopBarUI topBar;
        [HideInInspector] public MainTabBarUI mainTabBar;
        [HideInInspector] public BuildMenuUI buildMenu;
        [HideInInspector] public TrackPopupUI trackPopupUI;
        [HideInInspector] public TrackSubToolbarUI trackSubToolbar;
        [HideInInspector] public CatenarySubToolbarUI catenarySubToolbar;
        [HideInInspector] public PathSubToolbarUI pathSubToolbar;
        [HideInInspector] public RoomSubToolbarUI roomSubToolbar;
        [HideInInspector] public RoomBuildPanelUI roomBuildPanel;
        [HideInInspector] public PairSecondaryToolbarUI pairSecondaryToolbar;
        [HideInInspector] public BranchReturnDialogUI branchReturnDialog;
        [HideInInspector] public PauseMenuUI pauseMenu;
        [HideInInspector] public RoomTypePopupUI roomTypePopup;
        [HideInInspector] public RoomLevelPopupUI roomLevelPopup;
        [HideInInspector] public BuildingPopupUI buildingPopup;
        [HideInInspector] public FleetPanelUI fleetPanel;
        [HideInInspector] public Canvas canvas;

        private ToolMode currentTool = ToolMode.Select;
        private TrackBuildSubMode currentTrackSubMode = TrackBuildSubMode.Track;
        private CatenaryBuildSubMode currentCatenarySubMode = CatenaryBuildSubMode.AddCatenary;
        private PathBuildSubMode currentPathSubMode = PathBuildSubMode.Path;
        private RoomBuildSubMode currentRoomSubMode = RoomBuildSubMode.Hall;
        private RoomActionMode currentRoomAction = RoomActionMode.None;
        private PairSecondaryType currentPairSecondary = PairSecondaryType.SameAsPrimary;
        private bool pairModeActive = false;
        private GameObject selectedObject;

        public ToolMode CurrentTool
        {
            get => currentTool;
            set
            {
                if (currentTool == value) return;
                currentTool = value;
                OnToolChanged?.Invoke(currentTool);
            }
        }

        public TrackBuildSubMode CurrentTrackSubMode
        {
            get => currentTrackSubMode;
            set
            {
                if (currentTrackSubMode == value) return;
                currentTrackSubMode = value;
                OnTrackSubModeChanged?.Invoke(currentTrackSubMode);
            }
        }

        public CatenaryBuildSubMode CurrentCatenarySubMode
        {
            get => currentCatenarySubMode;
            set
            {
                if (currentCatenarySubMode == value) return;
                currentCatenarySubMode = value;
                OnCatenarySubModeChanged?.Invoke(currentCatenarySubMode);
            }
        }

        public PathBuildSubMode CurrentPathSubMode
        {
            get => currentPathSubMode;
            set
            {
                if (currentPathSubMode == value) return;
                currentPathSubMode = value;
                OnPathSubModeChanged?.Invoke(currentPathSubMode);
            }
        }

        public RoomBuildSubMode CurrentRoomSubMode
        {
            get => currentRoomSubMode;
            set
            {
                if (currentRoomSubMode == value) return;
                currentRoomSubMode = value;
                OnRoomSubModeChanged?.Invoke(currentRoomSubMode);
            }
        }

        public RoomActionMode CurrentRoomAction
        {
            get => currentRoomAction;
            set
            {
                if (currentRoomAction == value) return;
                currentRoomAction = value;
                OnRoomActionChanged?.Invoke(currentRoomAction);
            }
        }

        public GameObject SelectedObject => selectedObject;

        public PairSecondaryType CurrentPairSecondary
        {
            get => currentPairSecondary;
            set
            {
                if (currentPairSecondary == value) return;
                currentPairSecondary = value;
                OnPairSecondaryChanged?.Invoke(currentPairSecondary);
            }
        }

        public bool PairModeActive
        {
            get => pairModeActive;
            set
            {
                if (pairModeActive == value) return;
                pairModeActive = value;
                OnPairModeChanged?.Invoke(pairModeActive);
            }
        }


        public event System.Action<ToolMode> OnToolChanged;
        public event System.Action<TrackBuildSubMode> OnTrackSubModeChanged;
        public event System.Action<CatenaryBuildSubMode> OnCatenarySubModeChanged;
        public event System.Action<PathBuildSubMode> OnPathSubModeChanged;
        public event System.Action<RoomBuildSubMode> OnRoomSubModeChanged;
        public event System.Action<RoomActionMode> OnRoomActionChanged;
        public event System.Action<PairSecondaryType> OnPairSecondaryChanged;
        public event System.Action<bool> OnPairModeChanged;
        public event System.Action<GameObject> OnObjectSelected;
        public event System.Action OnObjectDeselected;

        /// <summary>
        /// Firowane raz po Awake (gdy <see cref="Instance"/> jest gotowy + Canvas zbudowany).
        /// Pattern dla klientów które potrzebują DepotUIManager w Start ale ich Awake mógł
        /// wystartować przed naszym: subscribe statycznie + zrób immediate try. Eliminuje
        /// "lazy-retry-every-frame" anti-pattern (Update sprawdzający czy już ready).
        /// Klient odpowiada za <c>OnReady -= handler</c> w OnDestroy żeby uniknąć leak'u
        /// przy reload sceny.
        /// </summary>
        public static event System.Action OnReady;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildCanvas();
            OnToolChanged += OnToolChangedInternal;
            OnRoomSubModeChanged += _ => CurrentRoomAction = RoomActionMode.None;

            // Ctrl+Z undo listener
            gameObject.AddComponent<DepotSystem.Undo.UndoInputHandler>();

            OnReady?.Invoke();
        }

        void Start()
        {
            // Load MapScene additively in the background
            SceneController.Initialize(this);
        }

        private void OnToolChangedInternal(ToolMode mode)
        {
            if (mode != ToolMode.BuildTrack)
                currentTrackSubMode = TrackBuildSubMode.Track;
            if (mode != ToolMode.BuildCatenary)
                currentCatenarySubMode = CatenaryBuildSubMode.AddCatenary;
            if (mode != ToolMode.BuildPath)
                currentPathSubMode = PathBuildSubMode.Path;
            if (mode != ToolMode.BuildRoom)
            {
                currentRoomSubMode = RoomBuildSubMode.Hall;
                currentRoomAction = RoomActionMode.None;
            }
        }

        public void SelectObject(GameObject obj)
        {
            if (selectedObject == obj) return;

            if (selectedObject != null)
                DeselectCurrent();

            selectedObject = obj;
            OnObjectSelected?.Invoke(obj);
        }

        public void DeselectCurrent()
        {
            if (selectedObject == null) return;
            selectedObject = null;
            OnObjectDeselected?.Invoke();
        }

        public bool IsPointerOverUI()
        {
            // Block all mouse input when Depot is not the active scene
            if (SceneController.ActiveScene != SceneController.GameScene.Depot)
                return true;

            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // ─────────────────────────────────────────────
        //  AUTO-BUILD CANVAS
        // ─────────────────────────────────────────────

        private void BuildCanvas()
        {
            // 1. Canvas
            GameObject canvasObj = new GameObject("DepotCanvas");
            canvasObj.transform.SetParent(transform, false);

            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true; // MUI-11: snap quadow UI do calych pikseli — kasuje sub-pikselowy jitter cienkich linii ikon ("jedna strona grubsza")
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);

            canvasObj.AddComponent<GraphicRaycaster>();

            // 2. EventSystem (jeśli nie istnieje)
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject eventSystem = new GameObject("EventSystem");
                eventSystem.transform.SetParent(transform, false);
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<InputSystemUIInputModule>();
            }

            // 3. TopBar
            GameObject topBarObj = new GameObject("TopBar");
            topBarObj.transform.SetParent(canvasObj.transform, false);
            topBar = topBarObj.AddComponent<TopBarUI>();

            // 4. MainTabBar (left side panel)
            GameObject tabBarObj = new GameObject("MainTabBar");
            tabBarObj.transform.SetParent(canvasObj.transform, false);
            mainTabBar = tabBarObj.AddComponent<MainTabBarUI>();

            // 4b. BuildMenu (flyout from Build tab)
            GameObject buildMenuObj = new GameObject("BuildMenu");
            buildMenuObj.transform.SetParent(canvasObj.transform, false);
            buildMenu = buildMenuObj.AddComponent<BuildMenuUI>();

            // 5. FleetPanel (fullscreen fleet management)
            GameObject fleetPanelObj = new GameObject("FleetPanel");
            fleetPanelObj.transform.SetParent(canvasObj.transform, false);
            fleetPanel = fleetPanelObj.AddComponent<FleetPanelUI>();

            // M7-4: RailwayManager.Maintenance.WorkshopsPanelUI żyje w Timetable assembly (DontDestroyOnLoad),
            // tworzone przez TrainRunSimulator.Awake(). Klik w MainTabBarUI emituje
            // UIIntent.OpenWorkshopsPanel — panel subskrybuje w OnEnable.
            // TrainPopupUI z Timetable (RailwayManager.Timetable.Simulation) — spawned przez
            // TrainRunSimulator.Awake. Depot's legacy TrainPopupUI usunięty razem z
            // TrainSpawnSystem (2026-05-13).

            // 6. TrackPopupUI (floating popup)
            GameObject trackPopupObj = new GameObject("TrackPopupUI");
            trackPopupObj.transform.SetParent(canvasObj.transform, false);
            trackPopupUI = trackPopupObj.AddComponent<TrackPopupUI>();

            // 8. TrackSubToolbar (sub-options for BuildTrack)
            GameObject trackSubObj = new GameObject("TrackSubToolbar");
            trackSubObj.transform.SetParent(canvasObj.transform, false);
            trackSubToolbar = trackSubObj.AddComponent<TrackSubToolbarUI>();

            // 9. CatenarySubToolbar (sub-options for BuildCatenary)
            GameObject catenarySubObj = new GameObject("CatenarySubToolbar");
            catenarySubObj.transform.SetParent(canvasObj.transform, false);
            catenarySubToolbar = catenarySubObj.AddComponent<CatenarySubToolbarUI>();

            // 9b. PathSubToolbar (sub-options for BuildPath)
            GameObject pathSubObj = new GameObject("PathSubToolbar");
            pathSubObj.transform.SetParent(canvasObj.transform, false);
            pathSubToolbar = pathSubObj.AddComponent<PathSubToolbarUI>();

            // 9c. RoomSubToolbar (sub-options for BuildRoom)
            GameObject roomSubObj = new GameObject("RoomSubToolbar");
            roomSubObj.transform.SetParent(canvasObj.transform, false);
            roomSubToolbar = roomSubObj.AddComponent<RoomSubToolbarUI>();

            // 9c. RoomBuildPanel (right-side action panel for BuildRoom)
            GameObject roomBuildPanelObj = new GameObject("RoomBuildPanel");
            roomBuildPanelObj.transform.SetParent(canvasObj.transform, false);
            roomBuildPanel = roomBuildPanelObj.AddComponent<RoomBuildPanelUI>();

            // 9e. PairSecondaryToolbar (pair mode secondary turnout type)
            GameObject pairSubObj = new GameObject("PairSecondaryToolbar");
            pairSubObj.transform.SetParent(canvasObj.transform, false);
            pairSecondaryToolbar = pairSubObj.AddComponent<PairSecondaryToolbarUI>();

            // 10. BranchReturnDialog (tryb U — odgałęzienie z powrotem)
            GameObject branchDialogObj = new GameObject("BranchReturnDialog");
            branchDialogObj.transform.SetParent(canvasObj.transform, false);
            branchReturnDialog = branchDialogObj.AddComponent<BranchReturnDialogUI>();

            // 11. PauseMenu (ESC) — musi mieć RectTransform zanim Awake() go odczyta
            GameObject pauseMenuObj = new GameObject("PauseMenu", typeof(RectTransform));
            pauseMenuObj.transform.SetParent(canvasObj.transform, false);
            pauseMenu = pauseMenuObj.AddComponent<PauseMenuUI>();

            // 12. RoomTypePopupUI (popup wyboru typu pomieszczenia)
            GameObject roomTypeObj = new GameObject("RoomTypePopup");
            roomTypeObj.transform.SetParent(canvasObj.transform, false);
            roomTypePopup = roomTypeObj.AddComponent<RoomTypePopupUI>();

            // 12b. RoomLevelPopupUI (MM-3 — popup awansu lvla pokoju, auto-show po SetRoomType MM-D19)
            GameObject roomLevelObj = new GameObject("RoomLevelPopup");
            roomLevelObj.transform.SetParent(canvasObj.transform, false);
            roomLevelPopup = roomLevelObj.AddComponent<RoomLevelPopupUI>();

            // 13. BuildingPopupUI (popup info o pomieszczeniu)
            GameObject buildingPopupObj = new GameObject("BuildingPopup");
            buildingPopupObj.transform.SetParent(canvasObj.transform, false);
            buildingPopup = buildingPopupObj.AddComponent<BuildingPopupUI>();

            // 14. KeyboardLegendUI (opis klawiszologii per tryb budowania)
            GameObject legendObj = new GameObject("KeyboardLegend");
            legendObj.transform.SetParent(canvasObj.transform, false);
            legendObj.AddComponent<KeyboardLegendUI>();

            Log.Info("[DepotUIManager] Canvas built procedurally");
        }
    }
}
