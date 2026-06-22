using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M9c-2: Watchdog nad nadchodzącymi kursami. Co tick sprawdza TrainRun'y na dziś
    /// i wykrywa te które:
    /// - Są zaplanowane w ciągu <see cref="NotificationLeadTimeSec"/> (default 5min)
    /// - Mają vehicleAssignment na dziś w assigned circulation
    /// - Pojazdy są aktualnie w stanie <see cref="VehicleLocationType.InDepot"/>
    ///
    /// Emituje <see cref="OnDepartureImminent"/> raz per run — subskrybuje M9c-2 handshake
    /// (notyfikacja UI + gotowość do EnqueueExit gdy gracz kliknie).
    ///
    /// Docelowo (M8 Personel): AI dyżurny ruchu będzie auto-klikał EnqueueExit w reakcji na ten event.
    /// </summary>
    public class DispatchService : MonoBehaviour
    {
        public static DispatchService Instance { get; private set; }

        /// <summary>Ile sekund przed odjazdem emitować notification (default 5 min = 300s).</summary>
        [Tooltip("Ile sekund przed odjazdem emitować OnDepartureImminent (default 5 min)")]
        public float NotificationLeadTimeSec = 300f;

        /// <summary>
        /// Event: kurs jest zaplanowany w ciągu NotificationLeadTimeSec, pojazdy są w depocie.
        /// Args: TrainRun + lista vehicleIds które mają go wykonać.
        /// Emitowany RAZ per run (deduplikacja via _notifiedRunIds).
        /// </summary>
        public event Action<TrainRun, List<int>> OnDepartureImminent;

        readonly HashSet<int> _notifiedRunIds = new();
        float _checkTimer;

        /// <summary>Co ile sekund sprawdzać (FixedUpdate jest za gęsto).</summary>
        const float CheckIntervalSec = 1f;

        /// <summary>
        /// Bootstrap — tworzy GO z service'em na DontDestroyOnLoad.
        /// </summary>
        public static DispatchService EnsureExists()
        {
            if (Instance != null) return Instance;

            var go = new GameObject("DispatchService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DispatchService>();
            Log.Info("[DispatchService] Bootstrapped");
            return Instance;
        }

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
        }

        void Update()
        {
            if (GameState.IsPaused) return;

            _checkTimer += Time.deltaTime * GameState.TimeScale;
            if (_checkTimer < CheckIntervalSec) return;
            _checkTimer = 0f;

            Check();
        }

        void Check()
        {
            var locSvc = VehicleLocationService.Instance;
            if (locSvc == null) return;

            string todayIso = GameState.CurrentDateIso;
            float nowSec = GameState.GameTimeSeconds;
            float threshold = nowSec + NotificationLeadTimeSec;

            var runs = TimetableService.TrainRuns;
            for (int i = 0; i < runs.Count; i++)
            {
                var tr = runs[i];
                if (tr.isCompleted || tr.isCancelled) continue;
                if (tr.runDateIso != todayIso) continue;
                if (_notifiedRunIds.Contains(tr.id)) continue;

                float departureSec = tr.startMinutesFromMidnight * 60f + tr.currentDelaySec;
                if (departureSec > threshold) continue; // jeszcze za wcześnie
                if (departureSec < nowSec - NotificationLeadTimeSec) continue; // już dawno powinien jechać (edge case)

                // Znajdź vehicleIds dla tego kursu
                var vehicleIds = GetVehicleIdsForRun(tr);
                if (vehicleIds == null || vehicleIds.Count == 0) continue;

                // Sprawdź czy WSZYSTKIE pojazdy są InDepot
                bool allInDepot = true;
                foreach (int vid in vehicleIds)
                {
                    var rec = locSvc.Get(vid);
                    if (rec == null || rec.type != VehicleLocationType.InDepot)
                    {
                        allInDepot = false;
                        break;
                    }
                }
                if (!allInDepot) continue;

                // Emit notification
                _notifiedRunIds.Add(tr.id);
                float minutesLeft = (departureSec - nowSec) / 60f;
                Log.Info($"[DispatchService] OnDepartureImminent: run#{tr.id} '{tr.trainNumberSnapshot}' " +
                         $"za {minutesLeft:F1}min, {vehicleIds.Count} pojazdów w depot: [{string.Join(",", vehicleIds)}]");
                OnDepartureImminent?.Invoke(tr, vehicleIds);
            }
        }

        /// <summary>
        /// Wyciąga vehicleIds dla danego kursu z obiegu + per-day assignment.
        /// Zwraca null gdy run standalone (circulationId=-1) albo brak assignment na today.
        /// </summary>
        static List<int> GetVehicleIdsForRun(TrainRun tr)
        {
            if (tr.circulationId < 0) return null; // standalone, M9c nie obsługuje

            var circulation = CirculationService.GetCirculation(tr.circulationId);
            if (circulation == null) return null;

            return circulation.GetVehiclesForDate(tr.runDateIso);
        }

        /// <summary>
        /// Usuwa run#X z listy notyfikowanych — wywoływane po udanym exit z depot
        /// (przez HandshakeService), żeby nie blokować re-notification w razie anulowania.
        /// </summary>
        public void ClearNotification(int runId) => _notifiedRunIds.Remove(runId);

        [ContextMenu("Debug: Dump notified runs")]
        public void DebugDumpNotified()
        {
            Log.Info($"[DispatchService] Notified runs: [{string.Join(",", _notifiedRunIds)}]");
        }

        [ContextMenu("Debug: Prime handshake test (z pierwszego Active circulation)")]
        public void DebugPrimeHandshakeTest()
        {
            // Wymaga: gracz ma w grze przynajmniej jeden Active obieg z assignment pojazdów.
            // Pobiera pierwszy taki obieg, set'uje home station + pojazdy jako InDepot.

            var locSvc = VehicleLocationService.Instance ?? VehicleLocationService.EnsureExists();

            Circulation found = null;
            string dateWithAssignment = null;
            List<int> vehiclesToday = null;

            foreach (var c in CirculationService.Circulations)
            {
                if (c.status != CirculationStatus.Active) continue;
                if (c.vehicleAssignmentsPerDay == null || c.vehicleAssignmentsPerDay.Count == 0) continue;
                foreach (var kvp in c.vehicleAssignmentsPerDay)
                {
                    if (kvp.Value == null || kvp.Value.Count == 0) continue;
                    found = c;
                    dateWithAssignment = kvp.Key;
                    vehiclesToday = kvp.Value;
                    break;
                }
                if (found != null) break;
            }

            if (found == null)
            {
                Log.Warn("[DispatchService] Brak Active circulation z assignment — utwórz obieg w M5 UI najpierw");
                return;
            }

            // Extract first step's route start station
            if (found.steps == null || found.steps.Count == 0)
            {
                Log.Warn($"[DispatchService] Circulation#{found.id} '{found.name}' bez steps");
                return;
            }
            var firstStep = found.steps[0];
            var timetable = TimetableService.GetTimetable(firstStep.timetableId);
            if (timetable == null) { Log.Warn("[DispatchService] Brak timetable dla pierwszego step'u"); return; }
            var route = TimetableService.GetRoute(timetable.routeId);
            if (route == null || route.stations == null || route.stations.Count == 0)
            { Log.Warn("[DispatchService] Brak route dla timetable"); return; }

            var firstStation = route.stations[0];
            GameState.HomeDepotStationId = firstStation.stationNodeId;

            // Set vehicles InDepot
            foreach (int vid in vehiclesToday)
                locSvc.SetInDepot(vid, depotTrackId: -1);

            Log.Info($"[DispatchService] PRIME OK: circulation#{found.id} '{found.name}' " +
                     $"dateUsed='{dateWithAssignment}', vehicles=[{string.Join(",", vehiclesToday)}] → InDepot, " +
                     $"HomeDepotStationId={GameState.HomeDepotStationId} ('{firstStation.stationName}')");
            Log.Info("[DispatchService] Uruchom grę, zaczekaj do T-5min przed odjazdem — powinno pojawić się OnDepartureImminent + notification UI. " +
                     "Następnie ręcznie wywołaj DepotMovementSimulator.EnqueueExit dla tych pojazdów żeby przetestować pełny handshake.");
        }
    }
}
