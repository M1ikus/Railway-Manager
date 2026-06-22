using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using MapSystem;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace RailwayManager.Timetable
{
    public enum RouteBuildState
    {
        Inactive,        // kreator nieaktywny
        PopupOpen,       // popup otwarty w depot, user widzi formularz
        PickingStation,  // tymczasowo na mapie, czeka na klik stacji
        RouteReady       // trasa wyliczona, user może zatwierdzić
    }

    /// <summary>
    /// Kreator trasy — zarządza flow:
    /// 1. User klika "Rozkłady" w Depot → popup otwiera się (PopupOpen)
    /// 2. User klika "Wybierz" przy punkcie A/B → przełączenie na mapę (PickingStation)
    /// 3. User klika stację na mapie → powrót do depot, popup zaktualizowany
    /// 4. Po wybraniu A i B → A*, podgląd, RouteReady → Zatwierdź/Anuluj
    /// </summary>
    public class RouteBuildStateMachine : MonoBehaviour
    {
        public static RouteBuildStateMachine Instance { get; private set; }

        public RouteBuildState State { get; private set; } = RouteBuildState.Inactive;
        public event Action<RouteBuildState> OnStateChanged;
        public event Action<Route> OnRouteBuilt;

        // Wybrane stacje
        public RailwayStation StartStation { get; private set; }
        public RailwayStation EndStation { get; private set; }

        // Computed route (po A*)
        public float RouteLengthM { get; private set; }
        public int RouteNodeCount { get; private set; }
        public int RouteMinSpeed { get; private set; }
        public int RouteMaxSpeed { get; private set; }
        public int RouteAvgSpeed { get; private set; }
        private List<int> _routeNodeIds;
        private List<int> _routeEdgeIds;

        // Picking context: czy wybieramy start czy end
        private bool _pickingStart;

        // Preview
        private GameObject _previewObj;
        private LineRenderer _previewLine;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnEnable() { StationMarker.OnAnyStationClicked += HandleStationClicked; }
        void OnDisable() { StationMarker.OnAnyStationClicked -= HandleStationClicked; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (State == RouteBuildState.Inactive) return;

            // ESC w PickingStation → wróć do popup (inne stany obsługuje TimetableCreatorUI)
            if (State == RouteBuildState.PickingStation
                && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SceneController.LastEscConsumedFrame = Time.frameCount;
                ReturnToPopup();
                return;
            }

            // Explicit raycast picking — nie polegamy na OnMouseDown (wymaga Camera.main tag)
            if (State == RouteBuildState.PickingStation
                && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                // Block jeśli kursor nad UI
                var mapUI = MapUIManager.Instance;
                if (mapUI != null && mapUI.IsPointerOverUI()) return;

                // Znajdź map camera (CameraController w MapScene)
                var camCtrl = FindAnyObjectByType<RailwayManager.CameraController>();
                if (camCtrl == null) return;
                var cam = camCtrl.GetComponent<Camera>();
                if (cam == null || !cam.enabled) return;

                Vector2 mousePos = Mouse.current.position.ReadValue();
                Ray ray = cam.ScreenPointToRay(mousePos);

                if (Physics.Raycast(ray, out RaycastHit hit, 500000f))
                {
                    var marker = hit.collider.GetComponent<StationMarker>();
                    if (marker != null)
                        HandleStationClicked(marker);
                }
            }
        }

        // ─────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────

        /// <summary>Otwiera kreator trasy (popup).</summary>
        public void Activate()
        {
            if (!EnsureInitialized()) return;

            StartStation = null;
            EndStation = null;
            ClearRoute();
            ClearPreview();
            SetState(RouteBuildState.PopupOpen);
            Log.Info("[RouteBuild] Popup opened");
        }

        /// <summary>Rozpoczyna picking stacji na mapie (przełącza na mapę).</summary>
        public void StartPickingStation(bool pickStart)
        {
            _pickingStart = pickStart;
            SetState(RouteBuildState.PickingStation);
            SceneController.SwitchToMap();
            Log.Info($"[RouteBuild] Picking {(pickStart ? "start" : "end")} station on map...");
        }

        /// <summary>Anuluje cały kreator.</summary>
        public void Cancel()
        {
            ClearPreview();
            ClearRoute();
            StartStation = null;
            EndStation = null;
            SetState(RouteBuildState.Inactive);

            // Wróć do depot jeśli jesteśmy na mapie
            if (SceneController.ActiveScene == SceneController.GameScene.Map)
                SceneController.SwitchToDepot();

            Log.Info("[RouteBuild] Cancelled");
        }

        /// <summary>Zatwierdza trasę i dodaje do TimetableService.</summary>
        public void ConfirmRoute()
        {
            if (StartStation == null || EndStation == null || _routeNodeIds == null)
            {
                Log.Warn("[RouteBuild] Wybierz obie stacje najpierw");
                return;
            }

            var route = new Route
            {
                name = $"{StartStation.name} → {EndStation.name}",
                nodeIds = new List<int>(_routeNodeIds),
                totalLengthM = RouteLengthM
            };

            // Fill stations
            route.stations.Add(MakeRouteStation(StartStation, 0f));
            route.stations.Add(MakeRouteStation(EndStation, RouteLengthM));

            // Voivodeship
            var init = TimetableInitializer.Instance;
            if (init?.Resolver != null && init.Resolver.IsReady)
            {
                var polyline = init.Graph.BuildRoutePolyline(_routeNodeIds, out _);
                route.crossesVoivodeshipBorder = init.Resolver.CrossesVoivodeshipBorder(polyline);
                route.startVoivodeship = StartStation.voivodeship;
            }

            TimetableService.AddRoute(route);
            OnRouteBuilt?.Invoke(route);

            Log.Info($"[RouteBuild] ✓ Route: {route.name}, {RouteLengthM / 1000f:F1} km");

            ClearPreview();
            ClearRoute();
            StartStation = null;
            EndStation = null;
            SetState(RouteBuildState.Inactive);
        }

        // ─────────────────────────────────────────────
        //  Station click handler (na mapie)
        // ─────────────────────────────────────────────

        private void HandleStationClicked(StationMarker marker)
        {
            if (State != RouteBuildState.PickingStation) return;

            var init = TimetableInitializer.Instance;
            if (init == null || !init.IsReady) return;

            var station = init.FindStation(marker.stationName);
            if (station == null || station.pathNodeId < 0)
            {
                Log.Warn($"[RouteBuild] '{marker.stationName}' nie ma węzła w grafie");
                return;
            }

            if (_pickingStart)
                StartStation = station;
            else
                EndStation = station;

            Log.Info($"[RouteBuild] Selected {(_pickingStart ? "start" : "end")}: {station.name}");

            // Jeśli obie stacje wybrane → oblicz trasę
            if (StartStation != null && EndStation != null)
                ComputeRoute();

            // Wróć do depot
            ReturnToPopup();
        }

        // ─────────────────────────────────────────────
        //  Route computation
        // ─────────────────────────────────────────────

        private void ComputeRoute()
        {
            var init = TimetableInitializer.Instance;
            if (init == null || StartStation == null || EndStation == null) return;

            var result = RailwayPathfinder.FindPath(
                init.Graph, StartStation.pathNodeId, EndStation.pathNodeId);

            if (!result.success)
            {
                Log.Warn($"[RouteBuild] Brak ścieżki {StartStation.name} → {EndStation.name}");
                ClearRoute();
                return;
            }

            _routeNodeIds = result.nodeIds;
            _routeEdgeIds = result.edgeIds;
            RouteLengthM = result.totalLengthM;
            RouteNodeCount = result.nodeIds.Count;

            // Vmax stats
            RouteMinSpeed = int.MaxValue;
            RouteMaxSpeed = 0;
            float totalSpeed = 0f;
            foreach (int eid in result.edgeIds)
            {
                int s = init.Graph.GetEdge(eid).maxSpeedKmh;
                if (s < RouteMinSpeed) RouteMinSpeed = s;
                if (s > RouteMaxSpeed) RouteMaxSpeed = s;
                totalSpeed += s;
            }
            RouteAvgSpeed = result.edgeIds.Count > 0 ? (int)(totalSpeed / result.edgeIds.Count) : 0;

            UpdatePreview();
            SetState(RouteBuildState.RouteReady);

            Log.Info($"[RouteBuild] Route computed: {RouteLengthM / 1000f:F1} km, "
                     + $"Vmax {RouteMinSpeed}-{RouteMaxSpeed} km/h");
        }

        // ─────────────────────────────────────────────
        //  Preview
        // ─────────────────────────────────────────────

        private void UpdatePreview()
        {
            if (_routeNodeIds == null || _routeNodeIds.Count < 2) { ClearPreview(); return; }
            var init = TimetableInitializer.Instance;
            if (init == null) return;

            if (_previewObj == null)
            {
                _previewObj = new GameObject("RoutePreview");
                _previewLine = _previewObj.AddComponent<LineRenderer>();
                _previewLine.startWidth = 80f;
                _previewLine.endWidth = 80f;
                _previewLine.material = MaterialFactory.CreateLine();
                _previewLine.startColor = new Color(1f, 0.6f, 0f, 0.7f);
                _previewLine.endColor = new Color(1f, 0.6f, 0f, 0.7f);
                _previewLine.useWorldSpace = true;
                _previewLine.sortingOrder = 100;
            }

            var polyline = init.Graph.BuildRoutePolyline(_routeNodeIds, out _);
            _previewLine.positionCount = polyline.Count;
            for (int i = 0; i < polyline.Count; i++)
                _previewLine.SetPosition(i, new Vector3(polyline[i].x, 15f, polyline[i].y));
        }

        private void ClearPreview()
        {
            if (_previewObj != null) { Destroy(_previewObj); _previewObj = null; _previewLine = null; }
        }

        // ─────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────

        private void ReturnToPopup()
        {
            SceneController.SwitchToDepot();
            SetState(StartStation != null && EndStation != null && _routeNodeIds != null
                ? RouteBuildState.RouteReady
                : RouteBuildState.PopupOpen);
        }

        private void SetState(RouteBuildState newState)
        {
            State = newState;
            OnStateChanged?.Invoke(newState);
        }

        private void ClearRoute()
        {
            _routeNodeIds = null;
            _routeEdgeIds = null;
            RouteLengthM = 0f;
            RouteNodeCount = 0;
            RouteMinSpeed = 0;
            RouteMaxSpeed = 0;
            RouteAvgSpeed = 0;
        }

        private bool EnsureInitialized()
        {
            var init = TimetableInitializer.Instance;
            if (init != null && init.IsReady) return true;
            init = TimetableInitializer.EnsureBootstrapped();
            if (init == null) return false;
            if (!init.IsReady)
            {
                Log.Info("[RouteBuild] Initializing timetable data...");
                init.Initialize();
            }
            return init.IsReady;
        }

        private static RouteStation MakeRouteStation(RailwayStation rs, float distM) => new()
        {
            stationNodeId = rs.pathNodeId,
            stationName = rs.name,
            distanceFromStartM = distM,
            position = rs.position,
            isMajorStation = rs.isMajorStation,
            voivodeship = rs.voivodeship,
            cityName = rs.cityName
        };
    }
}
