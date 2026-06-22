using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.SharedUI;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Automatyczna inicjalizacja pipeline Timetable.
    /// Buduje PathfindingGraph, ładuje stacje/perony/regiony/aglomeracje.
    /// Tworzy RouteBuildStateMachine i RouteBuildPanelUI w MapScene canvas.
    ///
    /// Tworzony lazy przez EnsureBootstrapped() przy pierwszym użyciu kreatora
    /// (nie musi być na scenie z góry — sam się doda do MapUIManager GO).
    ///
    /// Klasa rozbita na partial files (audit 2026-05-15 split god class 1506 → ~140 LOC root):
    /// - <c>TimetableInitializer.cs</c>             — pola, lifecycle, UIIntent router (ten plik)
    /// - <c>TimetableInitializer.Bootstrap.cs</c>   — RegisterSceneHook + BootstrapDelayer + EnsureBootstrapped factory
    /// - <c>TimetableInitializer.Init.cs</c>        — Initialize coroutine, 8-step streaming build,
    ///                                                  M-PL B2 fast-path (init-state-pl.bin), LoadSignals
    /// - <c>TimetableInitializer.Diagnostics.cs</c> — 13 ContextMenu DEBUG/DIAGNOSE/Validate metod
    /// </summary>
    public partial class TimetableInitializer : MonoBehaviour
    {
        public static TimetableInitializer Instance { get; private set; }

        public float stationSnapRadiusM = 300f;

        [Tooltip("Cell size dla Union-Find merging (m). Mniejsze = więcej nodes (precyzyjniejszy " +
                 "graph), większe = mniej nodes (szybszy build). 3m dla warm-maz, 10m+ dla pełnej PL.")]
        public float graphCellSizeM = 10f;

        [Tooltip("Maksymalna liczba tile do przetworzenia w streaming. -1 = wszystkie.")]
        public int maxTilesInStream = -1;

        [SerializeField] private bool isReady;

        public PathfindingGraph Graph { get; private set; }
        public List<RailwayStation> Stations { get; private set; }
        public List<StationPlatform> Platforms { get; private set; }
        public VoivodeshipResolver Resolver { get; private set; }
        public List<CityPlace> Places { get; private set; }
        public HashSet<string> Agglomerations { get; private set; }
        public StationTrackData TrackData { get; private set; }
        public BlockSectionGraph BlockSections { get; private set; }
        public List<SignalInfo> Signals { get; private set; }
        /// <summary>OSM natural=coastline lines — używane przez SyntheticWaterRenderer.</summary>
        public List<List<Vector2>> Coastlines { get; private set; }
        public bool IsReady => isReady;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // M-TimetableUX F1.16 polish: hook unlock notifications dla Advanced/Expert mode
            RailwayManager.SharedUI.PlayerProgressService.OnModeUnlocked += HandleModeUnlocked;
        }

        private void HandleModeUnlocked(RailwayManager.SharedUI.UIMode mode)
        {
            string label = mode switch
            {
                RailwayManager.SharedUI.UIMode.Advanced => "Advanced",
                RailwayManager.SharedUI.UIMode.Expert => "Expert",
                _ => mode.ToString()
            };
            RailwayManager.Timetable.Notifications.TimetableNotificationService.Add(
                RailwayManager.Timetable.Notifications.NotificationSeverity.Info,
                RailwayManager.Timetable.Notifications.NotificationType.SuggestionAvailable,
                $"Tryb {label} odblokowany! Otwórz kreator rozkładów żeby zmienić tryb.",
                stopIndex: -1,
                timeOfDaySec: 0,
                sourceTimetableId: -1);
        }

        void OnDestroy()
        {
            // M-TimetableUX F1.16 polish: unsubscribe OnModeUnlocked
            RailwayManager.SharedUI.PlayerProgressService.OnModeUnlocked -= HandleModeUnlocked;
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            UIIntents.OnIntent += HandleUIIntent;
        }

        void OnDisable()
        {
            UIIntents.OnIntent -= HandleUIIntent;
        }

        void HandleUIIntent(UIIntent intent)
        {
            if (intent == UIIntent.OpenScheduleCreator)
            {
                if (!isReady) Initialize();

                // Zamknij pozostałe popup'y żeby nie nakładały się na siebie.
                // Wszystkie popupy dzielą TimetablePopupOpen, więc nowy Show() ustawia
                // flagę na true zanim stare Update() zdąży się ukryć — musimy explicit.
                HideAllOtherPopups(exceptList: true);

                if (TimetableListUI.Instance != null) TimetableListUI.Instance.Open();
            }
            else if (intent == UIIntent.OpenCirculationList)
            {
                if (!isReady) Initialize();

                HideAllOtherPopups(exceptList: false);

                if (CirculationListUI.Instance != null) CirculationListUI.Instance.Open();
                else Log.Warn("[TimetableInitializer] CirculationListUI not built yet (Etap 5 TODO)");
            }
        }

        /// <summary>
        /// Ukrywa wszystkie popup'y Timetable oprócz docelowego. Używane przy przełączaniu
        /// zakładek Rozkłady↔Obiegi w MainTabBar — wszystkie panele dzielą TimetablePopupOpen
        /// więc explicit Hide() jest potrzebne żeby uniknąć nakładania się.
        /// </summary>
        /// <param name="exceptList">true = zostaw TimetableListUI, false = zostaw CirculationListUI</param>
        private static void HideAllOtherPopups(bool exceptList)
        {
            if (!exceptList && TimetableListUI.Instance != null) TimetableListUI.Instance.Hide();
            if (exceptList && CirculationListUI.Instance != null) CirculationListUI.Instance.Hide();
            // Pozostałe popup'y zawsze zamykamy (nie są celem z MainTabBar)
            if (TimetableCreatorUI.Instance != null) TimetableCreatorUI.Instance.Hide();
            if (CategoryEditorUI.Instance != null) CategoryEditorUI.Instance.Hide();
            // CirculationCreatorUI — removed in M5 Etap 6 refactor (zastąpiony przez inline editing w CirculationListUI)
        }
    }
}
