using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Core MonoBehaviour for the timetable creator popup.
    /// Detailed route, stop, view, and workflow logic lives in partial files.
    /// </summary>
    public partial class TimetableCreatorUI : MonoBehaviour
    {
        public static TimetableCreatorUI Instance { get; private set; }

        private GameObject _panel;
        private GraphicRaycaster _raycaster;

        // Station inputs
        private TMP_InputField _startInput;
        private TMP_InputField _endInput;
        private Transform _startSuggestions;
        private Transform _endSuggestions;
        private TextMeshProUGUI _routeInfoText;

        // Waypoints
        private readonly List<RailwayStation> _waypoints = new();
        // M-TimetableUX 2026-05-11: parallel list selected trackRef per waypoint.
        // Empty string = "(auto)" — ResolveTrunkNode w pathfinder. Specific trackRef
        // = force pathfinder przez FindNodeOnTrack(station, trackRef).
        private readonly List<string> _waypointTracks = new();
        private Transform _waypointsContainer;

        // M-TimetableUX 2026-05-11: track override dla A i C (parity z waypoint tracks).
        // Empty string = "(auto)" — peronowy pathNodeId (existing behavior).
        private string _startTrack = "";
        private string _endTrack = "";
        private TMP_Dropdown _startTrackDropdown;
        private TMP_Dropdown _endTrackDropdown;

        // Display linii kolejowych aktualnej trasy (lk9+lk204 format z OSM ref tag).
        private TextMeshProUGUI _routeLinesText;

        // M-TimetableUX 2026-05-11: K-shortest paths alternatywy.
        // _routeAlternativesContainer: panel z buttonami "lk9+lk204 (153km)" — klik wybiera trasę.
        // _alternativeRoutes: lista PathResult zwróconych z FindKShortestPaths.
        // _selectedAlternativeIdx: indeks aktualnie wybranej alternatywy (0 = najkrótsza).
        // _alternativesK: max liczba alternatyw (default 3, configurable later).
        // _alternativesMaxRatio: max długość alternatywy / najkrótsza (default 1.3).
        private Transform _routeAlternativesContainer;
        private List<RailwayPathfinder.PathResult> _alternativeRoutes = new();
        private int _selectedAlternativeIdx = 0;
        private int _alternativesK = 3;
        private float _alternativesMaxRatio = 1.3f;
        private Coroutine _backgroundGenerationCoroutine;

        // Parameters
        private TMP_Dropdown _categoryDropdown;
        private Toggle _emuToggle;
        private TMP_Dropdown _assignmentDropdown;
        private TMP_InputField _vmaxInput;
        private TMP_InputField _startTimeInput;
        private TMP_InputField _startDateInput;
        private TMP_InputField _trainNumberInput;
        private TextMeshProUGUI _trainNumberStatus;
        private bool _trainNumberOverrideAccepted;
        private readonly Toggle[] _dayToggles = new Toggle[7];
        private TMP_InputField _weeksValidInput;

        // Takt
        private Toggle _taktToggle;
        private GameObject _taktRow;
        private TMP_InputField _taktIntervalInput;
        private TMP_InputField _taktLastInput;
        private TextMeshProUGUI _taktSummary;

        // Stops
        private Transform _stopsContent;
        private TextMeshProUGUI _summaryText;
        private Button _generateRouteBtn;
        private Button _computeBtn;
        private Button _confirmBtn;
        private Button _forceConfirmBtn;

        // Cached route
        private Route _currentRoute;
        private bool _showTimes;
        private readonly Dictionary<string, string> _manualTrackOverrides = new();

        // M-TimetableUX 2026-05-11: blacklist auto-discovered stops removed by user.
        // Pathfinder generuje route, F1.6 FindStationsPerSegment dodaje stacje on-path
        // jako auto-stops. User może klik "✕" żeby usunąć stop — wtedy stationNodeId trafia
        // do blacklist, BuildRoute pomija go przy każdym regen do reset (nowe A→B clear blacklist).
        private readonly HashSet<int> _blacklistedStationNodeIds = new();

        // Collisions
        private List<ReservationManager.CollisionInfo> _collisions = new();

        // State
        private RailwayStation _startStation;
        private RailwayStation _endStation;
        private List<TimetableStop> _stops;
        private CompositionMode _compositionMode = CompositionMode.MultipleUnit;
        private CompositionAssignment _compositionAssignment = CompositionAssignment.Symbolic;
        private bool _pickingStart;
        private CreatorPreset _activePreset;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            LocalizationService.OnLanguageChanged += OnLocaleChanged;
            // M-TimetableUX F1.6 click polish: subscribe na ghost marker clicks → append waypoint
            GhostStationMarkersOverlay.OnMarkerClicked += HandleGhostMarkerClicked;
            // M-TimetableUX F1.3 click polish: subscribe na waypoint marker clicks → remove waypoint
            WaypointMarkersOverlay.OnMarkerClicked += HandleWaypointMarkerClicked;
            // M-TimetableUX F1.3 drag polish: subscribe na drag → move waypoint do snapped station
            WaypointMarkersOverlay.OnMarkerDragged += HandleWaypointMarkerDragged;
        }

        void OnDestroy()
        {
            LocalizationService.OnLanguageChanged -= OnLocaleChanged;
            GhostStationMarkersOverlay.OnMarkerClicked -= HandleGhostMarkerClicked;
            WaypointMarkersOverlay.OnMarkerClicked -= HandleWaypointMarkerClicked;
            WaypointMarkersOverlay.OnMarkerDragged -= HandleWaypointMarkerDragged;
            _routePreview?.Dispose();
            if (Instance == this) Instance = null;
        }

        private void HandleWaypointMarkerDragged(int waypointIdx, RailwayStation snapped)
        {
            if (waypointIdx < 0 || waypointIdx >= _waypoints.Count) return;
            if (snapped == null) return;
            if (_panel == null || !_panel.activeSelf) return;
            var previous = _waypoints[waypointIdx];
            _waypoints[waypointIdx] = snapped;
            RefreshWaypointsUI();
            Refresh();
            Log.Info($"[F1.3 drag] Moved waypoint idx={waypointIdx}: {previous?.name} → {snapped.name}");
        }

        private void HandleWaypointMarkerClicked(int waypointIndex)
        {
            if (waypointIndex < 0 || waypointIndex >= _waypoints.Count) return;
            if (_panel == null || !_panel.activeSelf) return;
            var removed = _waypoints[waypointIndex];
            _waypoints.RemoveAt(waypointIndex);
            if (waypointIndex < _waypointTracks.Count) _waypointTracks.RemoveAt(waypointIndex);
            RefreshWaypointsUI();
            Refresh();
            Log.Info($"[F1.3 click] Removed waypoint via marker click: {removed?.name} (was idx {waypointIndex})");
        }

        private void HandleGhostMarkerClicked(RailwayStation station)
        {
            // F1.6 click: append ghost station jako waypoint + recompute path
            if (station == null) return;
            if (_panel == null || !_panel.activeSelf) return; // creator nie aktywny — ignore
            _waypoints.Add(station);
            _waypointTracks.Add(""); // (auto)
            RefreshWaypointsUI();
            Refresh();
            Log.Info($"[F1.6 click] Appended waypoint z ghost marker: {station.name}");
        }

        private void OnLocaleChanged()
        {
            if (_panel == null || !_panel.activeSelf) return;

            int startMin = ParseTime(_startTimeInput?.text ?? "06:00");
            if (_stops != null) RefreshStopsList(startMin);
            RefreshSummary(GetSelectedCategory());
            UpdateTaktSummary();
            ValidateTrainNumberUI();
            Refresh();
        }

        void Start()
        {
            var sm = RouteBuildStateMachine.Instance;
            if (sm != null) sm.OnStateChanged += OnSMStateChanged;
        }

        private void OnSMStateChanged(RouteBuildState state)
        {
            if (state == RouteBuildState.PickingStation)
                HideForPicking();
            else if (state == RouteBuildState.PopupOpen || state == RouteBuildState.RouteReady)
                ShowAfterPicking();
            else if (state == RouteBuildState.Inactive)
                Hide();
        }

        void Update()
        {
            if (_panel == null || !_panel.activeSelf) return;

            if (!SceneController.TimetablePopupOpen)
            {
                Hide();
                return;
            }

            if (UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SceneController.LastEscConsumedFrame = Time.frameCount;
                CancelAll();
            }
        }

        public void Open()
        {
            var init = TimetableInitializer.Instance;
            if (init != null && !init.IsReady) init.Initialize();

            _pickingStart = false;
            _pickingWaypointIndex = -1;
            _startStation = null;
            _endStation = null;
            _waypoints.Clear();
            _waypointTracks.Clear();
            _startTrack = "";
            _endTrack = "";
            _alternativeRoutes.Clear();
            _selectedAlternativeIdx = 0;
            _stops = null;
            _currentRoute = null;
            _showTimes = false;
            _manualTrackOverrides.Clear();
            _blacklistedStationNodeIds.Clear(); // reset blacklist na nowy timetable
            _collisions.Clear();
            _activePreset = null;

            if (_startInput != null) _startInput.text = "";
            if (_endInput != null) _endInput.text = "";
            if (_vmaxInput != null) _vmaxInput.text = "120";
            if (_startTimeInput != null) _startTimeInput.text = "06:00";
            if (_startDateInput != null) _startDateInput.text = DefaultStartDate();
            if (_weeksValidInput != null) _weeksValidInput.text = "4";

            RefreshCategoryDropdown();
            if (_categoryDropdown != null) _categoryDropdown.value = 0;

            _compositionAssignment = CompositionAssignment.Symbolic;
            if (_assignmentDropdown != null) _assignmentDropdown.value = 0;
            _compositionMode = CompositionMode.MultipleUnit;
            if (_emuToggle != null) _emuToggle.isOn = true;

            for (int i = 0; i < 7; i++)
                if (_dayToggles[i] != null) _dayToggles[i].isOn = true;

            if (_trainNumberInput != null) _trainNumberInput.text = "";
            if (_trainNumberStatus != null) _trainNumberStatus.text = "";
            _trainNumberOverrideAccepted = false;

            if (_taktToggle != null) _taktToggle.isOn = false;
            if (_taktIntervalInput != null) _taktIntervalInput.text = "60";
            if (_taktLastInput != null) _taktLastInput.text = "22:00";
            if (_taktSummary != null) _taktSummary.text = "";
            if (_taktRow != null) _taktRow.SetActive(false);

            if (_stopsContent != null)
                foreach (Transform ch in _stopsContent) Destroy(ch.gameObject);

            HideSuggestions(_startSuggestions);
            HideSuggestions(_endSuggestions);

            if (_summaryText != null) _summaryText.text = "";
            if (_routeInfoText != null) _routeInfoText.text = "";

            if (_confirmBtn != null) _confirmBtn.interactable = false;
            if (_forceConfirmBtn != null) _forceConfirmBtn.gameObject.SetActive(false);

            RefreshWaypointsUI();
            Show();
            Refresh();
            RefreshRoutePreview(); // pusty podgląd od razu po otwarciu (brak trasy → ClearOverlays)
        }

        public void Show()
        {
            if (_panel != null) _panel.SetActive(true);
            if (_raycaster != null) _raycaster.enabled = true;
            SceneController.TimetablePopupOpen = true;
        }

        public void Hide()
        {
            if (_panel != null) _panel.SetActive(false);
            if (_raycaster != null) _raycaster.enabled = false;
            HideSuggestions(_startSuggestions);
            HideSuggestions(_endSuggestions);
            _pickingStart = false;
            _pickingWaypointIndex = -1;
            SceneController.TimetablePopupOpen = false;

            // M-TimetableUX F1.6 polish: cleanup ghost markers gdy creator closing
            if (GhostStationMarkersOverlay.Instance != null)
                GhostStationMarkersOverlay.Instance.Clear();
            // M-TimetableUX F1.3 polish: cleanup waypoint markers
            if (WaypointMarkersOverlay.Instance != null)
                WaypointMarkersOverlay.Instance.Clear();
        }

        private void HideForPicking() => Hide();

        private void ShowAfterPicking()
        {
            var sm = RouteBuildStateMachine.Instance;
            if (sm != null)
            {
                if (_pickingWaypointIndex >= 0 && _pickingWaypointIndex < _waypoints.Count)
                {
                    var picked = sm.StartStation ?? sm.EndStation;
                    if (picked != null)
                    {
                        _waypoints[_pickingWaypointIndex] = picked;
                        RefreshWaypointsUI();
                    }
                    _pickingWaypointIndex = -1;
                }
                else if (_pickingWaypointIndex == -2)
                {
                    // F1.3 polish: append new waypoint (sentinel -2 = "add new")
                    var picked = sm.StartStation ?? sm.EndStation;
                    if (picked != null)
                    {
                        _waypoints.Add(picked);
                        _waypointTracks.Add(""); // (auto)
                        RefreshWaypointsUI();
                        Refresh();
                    }
                    _pickingWaypointIndex = -1;
                }
                else if (_pickingStart && sm.StartStation != null)
                {
                    _startStation = sm.StartStation;
                    if (_startInput != null) _startInput.text = _startStation.name;
                }
                else if (!_pickingStart && sm.EndStation != null)
                {
                    _endStation = sm.EndStation;
                    if (_endInput != null) _endInput.text = _endStation.name;
                }
            }

            Show();
            Refresh();
        }

        private void Refresh()
        {
            bool hasRoute = _startStation != null && _endStation != null;

            if (_routeInfoText != null)
            {
                if (hasRoute)
                {
                    var init = TimetableInitializer.Instance;
                    if (init != null && init.IsReady)
                    {
                        var result = RailwayPathfinder.FindPath(
                            init.Graph, _startStation.pathNodeId, _endStation.pathNodeId);
                        _routeInfoText.text = result.success
                            ? string.Format(
                                LocalizationService.Get("timetable.creator.route.info_distance_format"),
                                (result.totalLengthM / 1000f).ToString("F1"))
                            : LocalizationService.Get("timetable.creator.route.info_no_connection");
                    }
                }
                else
                {
                    _routeInfoText.text = "";
                }
            }

            if (_generateRouteBtn != null) _generateRouteBtn.interactable = hasRoute;
            if (_computeBtn != null) _computeBtn.interactable = _currentRoute != null;
            if (_confirmBtn != null) _confirmBtn.interactable = _stops != null && _stops.Count >= 2;
        }

        static int ParseTime(string t)
        {
            if (string.IsNullOrEmpty(t)) return 360;
            var p = t.Split(':');
            int h = 6, m = 0;
            if (p.Length >= 1) int.TryParse(p[0], out h);
            if (p.Length >= 2) int.TryParse(p[1], out m);
            return h * 60 + m;
        }

        static string FmtTime(int sec)
        {
            return $"{(sec / 3600) % 24:D2}:{(sec / 60) % 60:D2}:{sec % 60:D2}";
        }
    }
}
