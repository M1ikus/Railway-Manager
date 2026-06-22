using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using DepotSystem;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M9c glue — spina eventy DepotMovementSimulator + TrainRunSimulator + VehicleLocationService.
    ///
    /// Żyje w Timetable assembly (które widzi Depot + Core + Map). Bootstrapped razem
    /// z DispatchService'em przy ładowaniu sceny.
    ///
    /// Etap M9c-2: obsługa Depot → Map (wyjazd).
    /// Etap M9c-3: obsługa Map → Depot (wjazd).
    /// </summary>
    public class DepotMapHandshakeService : MonoBehaviour
    {
        public static DepotMapHandshakeService Instance { get; private set; }

        /// <summary>
        /// Mapa consistId → TrainRun przygotowany do spawn (set'owany przez handshake
        /// gdy gracz klika "Wyjedź z depot" — pairs up notified imminent run).
        /// </summary>
        readonly Dictionary<int, TrainRun> _pendingSpawns = new();

        public static DepotMapHandshakeService EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("DepotMapHandshakeService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<DepotMapHandshakeService>();
            Log.Info("[DepotMapHandshake] Bootstrapped");
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

        void OnEnable()
        {
            DepotMovementSimulator.OnConsistExitedDepot += HandleConsistExited;
            DepotMovementSimulator.OnConsistEnteredDepot += HandleConsistEntered;
            TrainRunSimulator.OnRunSpawned += HandleRunSpawned;
            TrainRunSimulator.OnRunDespawned += HandleRunDespawned;
        }

        void OnDisable()
        {
            DepotMovementSimulator.OnConsistExitedDepot -= HandleConsistExited;
            DepotMovementSimulator.OnConsistEnteredDepot -= HandleConsistEntered;
            TrainRunSimulator.OnRunSpawned -= HandleRunSpawned;
            TrainRunSimulator.OnRunDespawned -= HandleRunDespawned;
        }

        // ── M9c-2: Depot → Map (wyjazd) ─────────────────────────────

        void HandleConsistExited(int consistId, List<int> vehicleIds)
        {
            if (vehicleIds == null || vehicleIds.Count == 0)
            {
                Log.Warn($"[DepotMapHandshake] OnConsistExitedDepot#{consistId} bez vehicleIds — ignoruję");
                return;
            }

            var locSvc = VehicleLocationService.Instance;
            if (locSvc == null)
            {
                Log.Warn("[DepotMapHandshake] VehicleLocationService nie istnieje");
                return;
            }

            // Znajdź TrainRun dla tych pojazdów — najbliższy imminent / running dziś
            var run = FindMatchingRunForVehicles(vehicleIds);
            if (run == null)
            {
                // Consist wyjechał bez przypisanego kursu — flag vehicles jako InTransit
                Log.Warn($"[DepotMapHandshake] Consist#{consistId} wyjechał bez matching run " +
                         $"(vehicleIds=[{string.Join(",", vehicleIds)}]). Flagging jako InTransit.");
                foreach (int vid in vehicleIds)
                    locSvc.SetInTransit(vid, Vector2.zero);
                return;
            }

            // Wyciągnij pozycję startową z Route
            Vector2 spawnPos = GetRouteStartPosition(run);
            if (spawnPos == Vector2.zero)
            {
                Log.Warn($"[DepotMapHandshake] Nie znaleziono route start position dla run#{run.id}");
                // Kontynuuj mimo to — SpawnTrain użyje własnej logiki polyline startu
            }

            // Tranzycje: vehicles → OnRoute
            foreach (int vid in vehicleIds)
                locSvc.SetOnRoute(vid, run.id, spawnPos);

            // Spawn na mapie 2D
            var trainSim = TrainRunSimulator.Instance;
            if (trainSim == null)
            {
                Log.Warn("[DepotMapHandshake] TrainRunSimulator nie istnieje w scenie — run spawn nie powiódł się");
                return;
            }

            bool success = trainSim.SpawnTrainFromVehicles(run, vehicleIds, spawnPos);
            if (success)
            {
                // Wyczyść notification żeby DispatchService nie notyfikował ponownie
                DispatchService.Instance?.ClearNotification(run.id);
                Log.Info($"[DepotMapHandshake] HANDSHAKE OK: consist#{consistId} → run#{run.id} " +
                         $"({vehicleIds.Count} vehicles) at {spawnPos}");
            }
            else
            {
                Log.Warn($"[DepotMapHandshake] SpawnTrainFromVehicles failed dla run#{run.id}");
            }
        }

        // ── M9c-4: obsługa OnRunSpawned (auto-spawn dla multi-step + legacy) ───

        /// <summary>
        /// Reakcja na OnRunSpawned — niezależnie czy spawn przez SpawnTrainFromVehicles
        /// (handshake) czy automatyczny SpawnTrain (CheckForNewTrains gdy vehicles AtStation).
        /// Auto-populuje runningVehicleIds z circulation gdy pusty (legacy / multi-step auto-spawn)
        /// i set'uje vehicles na OnRoute.
        /// </summary>
        void HandleRunSpawned(TrainRun run)
        {
            var locSvc = VehicleLocationService.Instance;
            if (locSvc == null) return;

            // Auto-populate runningVehicleIds jeśli pusty
            if (run.runningVehicleIds == null || run.runningVehicleIds.Count == 0)
            {
                var fromCirc = GetVehiclesFromCirculationAssignment(run);
                if (fromCirc != null && fromCirc.Count > 0)
                {
                    run.runningVehicleIds = new List<int>(fromCirc);
                    Log.Info($"[DepotMapHandshake] Run#{run.id} auto-populated runningVehicleIds " +
                             $"z circulation#{run.circulationId}: [{string.Join(",", fromCirc)}]");
                }
            }

            if (run.runningVehicleIds == null || run.runningVehicleIds.Count == 0)
                return;

            Vector2 pos = GetRouteStartPosition(run);
            foreach (int vid in run.runningVehicleIds)
                locSvc.SetOnRoute(vid, run.id, pos);

            Log.Info($"[DepotMapHandshake] Run#{run.id} spawned — {run.runningVehicleIds.Count} vehicles → OnRoute");
        }

        /// <summary>Wyciąga vehicleIds z Circulation.vehicleAssignmentsPerDay[run.runDateIso]. Null jeśli brak.</summary>
        static List<int> GetVehiclesFromCirculationAssignment(TrainRun tr)
        {
            if (tr.circulationId < 0) return null;
            var circ = CirculationService.GetCirculation(tr.circulationId);
            if (circ == null) return null;
            return circ.GetVehiclesForDate(tr.runDateIso);
        }

        // ── M9c-3: Map → Depot (wjazd) ──────────────────────────────

        /// <summary>
        /// Reakcja na OnRunDespawned — kurs zakończony, decydujemy co z pojazdami:
        /// - endStation == HomeDepotStationId AND brak kolejnego kroku dziś → trigger entry do depot
        /// - W przeciwnym razie → pojazdy zostają AtStation(endStation), czekają na kolejny kurs lub idle
        /// </summary>
        void HandleRunDespawned(TrainRun run)
        {
            if (run == null || run.runningVehicleIds == null || run.runningVehicleIds.Count == 0)
                return;

            var locSvc = VehicleLocationService.Instance;
            if (locSvc == null) return;

            int endStationId = GetRouteEndStationId(run);
            Vector2 endStationPos = GetRouteEndStationPosition(run);
            bool isHome = endStationId == GameState.HomeDepotStationId && GameState.HomeDepotStationId >= 0;

            // Czy jest kolejny krok dla któregoś z pojazdów dziś?
            bool hasNextStepToday = HasNextStepForAnyVehicle(run);

            if (isHome && !hasNextStepToday)
            {
                // Trigger entry do depot — generuj consistId, spawn consist at entry
                var sim = DepotMovementSimulator.Instance;
                if (sim == null)
                {
                    Log.Warn($"[DepotMapHandshake] HandleRunDespawned: DepotMovementSimulator Instance null " +
                             $"(scena Depot nie załadowana?) — fallback: vehicles → InDepot bez animacji");
                    foreach (int vid in run.runningVehicleIds)
                        locSvc.SetInDepot(vid, depotTrackId: -1);
                    return;
                }

                int consistId = sim.GenerateConsistId();
                var vehicleIds = new List<int>(run.runningVehicleIds);

                // Set state EnteringDepot dla każdego pojazdu (przed spawn animacji)
                foreach (int vid in vehicleIds)
                    locSvc.SetEnteringDepot(vid, consistId);

                bool spawned = sim.SpawnConsistAtEntry(consistId, vehicleIds);
                if (spawned)
                {
                    Log.Info($"[DepotMapHandshake] Run#{run.id} → ENTRY: consist#{consistId} " +
                             $"({vehicleIds.Count} vehicles) wjeżdża do depot");
                }
                else
                {
                    Log.Warn($"[DepotMapHandshake] SpawnConsistAtEntry failed dla consist#{consistId} — " +
                             $"fallback: vehicles → InDepot bez animacji");
                    foreach (int vid in vehicleIds)
                        locSvc.SetInDepot(vid, depotTrackId: -1);
                }
            }
            else
            {
                // Zostaje na stacji end — czeka na kolejny kurs lub idle
                string reason = !isHome ? "endStation nie home" : "jest next step";
                foreach (int vid in run.runningVehicleIds)
                    locSvc.SetAtStation(vid, endStationId, endStationPos);
                Log.Info($"[DepotMapHandshake] Run#{run.id} → AtStation#{endStationId} " +
                         $"({run.runningVehicleIds.Count} vehicles, reason: {reason})");
            }
        }

        /// <summary>
        /// Handler OnConsistEnteredDepot — consist zatrzymał się na pieńku przy bramie po animacji.
        /// Set EnteringDepot → InDepot dla każdego pojazdu.
        /// </summary>
        void HandleConsistEntered(int consistId, List<int> vehicleIds)
        {
            var locSvc = VehicleLocationService.Instance;
            if (locSvc == null || vehicleIds == null) return;
            foreach (int vid in vehicleIds)
                locSvc.SetInDepot(vid, depotTrackId: -1);
            Log.Info($"[DepotMapHandshake] Consist#{consistId} wjechał do depot, " +
                     $"{vehicleIds.Count} pojazdów → InDepot");
        }

        // ── Helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Szuka TrainRun dla danej listy vehicleIds. Kryteria:
        /// - runDateIso == today
        /// - nie isCompleted, nie isCancelled
        /// - circulation.GetVehiclesForDate(today) zawiera WSZYSTKIE vehicleIds
        /// - Preferencja: najbliższy departureTime (najpierw imminent, potem już spóźnione)
        /// </summary>
        TrainRun FindMatchingRunForVehicles(List<int> vehicleIds)
        {
            string todayIso = GameState.CurrentDateIso;
            float nowSec = GameState.GameTimeSeconds;

            TrainRun best = null;
            float bestScore = float.MaxValue;

            foreach (var tr in TimetableService.TrainRuns)
            {
                if (tr.isCompleted || tr.isCancelled) continue;
                if (tr.runDateIso != todayIso) continue;
                if (tr.circulationId < 0) continue;

                var circulation = CirculationService.GetCirculation(tr.circulationId);
                if (circulation == null) continue;

                var runVehicles = circulation.GetVehiclesForDate(todayIso);
                if (runVehicles == null || runVehicles.Count == 0) continue;

                // Sprawdź czy wszystkie z vehicleIds są w runVehicles
                bool allMatch = true;
                foreach (int vid in vehicleIds)
                    if (!runVehicles.Contains(vid)) { allMatch = false; break; }
                if (!allMatch) continue;

                // Score: |departure - now|. Preferuj imminent/recent.
                float departureSec = tr.startMinutesFromMidnight * 60f + tr.currentDelaySec;
                float score = Mathf.Abs(departureSec - nowSec);
                if (score < bestScore) { bestScore = score; best = tr; }
            }

            return best;
        }

        /// <summary>
        /// Pozycja startowa kursu na mapie 2D (pozycja pierwszej stacji w Route).
        /// Vector2.zero gdy Route/Timetable nie znaleziony.
        /// </summary>
        static Vector2 GetRouteStartPosition(TrainRun tr)
        {
            var timetable = TimetableService.GetTimetable(tr.timetableId);
            if (timetable == null) return Vector2.zero;

            var route = TimetableService.GetRoute(timetable.routeId);
            if (route == null || route.stations == null || route.stations.Count == 0) return Vector2.zero;

            return route.stations[0].position;
        }

        /// <summary>ID końcowej stacji kursu (stationNodeId ostatniej stacji w Route). -1 gdy brak.</summary>
        static int GetRouteEndStationId(TrainRun tr)
        {
            var timetable = TimetableService.GetTimetable(tr.timetableId);
            if (timetable == null) return -1;

            var route = TimetableService.GetRoute(timetable.routeId);
            if (route == null || route.stations == null || route.stations.Count == 0) return -1;

            return route.stations[route.stations.Count - 1].stationNodeId;
        }

        /// <summary>Pozycja końcowej stacji kursu. Vector2.zero gdy brak.</summary>
        static Vector2 GetRouteEndStationPosition(TrainRun tr)
        {
            var timetable = TimetableService.GetTimetable(tr.timetableId);
            if (timetable == null) return Vector2.zero;

            var route = TimetableService.GetRoute(timetable.routeId);
            if (route == null || route.stations == null || route.stations.Count == 0) return Vector2.zero;

            return route.stations[route.stations.Count - 1].position;
        }

        // ── Debug ───────────────────────────────────────────────────

        [ContextMenu("Debug: Simulate run completion at HOME (entry flow)")]
        public void DebugSimulateRunCompletionAtHome()
        {
            if (GameState.HomeDepotStationId < 0)
            {
                Log.Warn("[DepotMapHandshake DEBUG] HomeDepotStationId nie ustawione — najpierw " +
                         "DispatchService.DebugPrimeHandshakeTest");
                return;
            }

            // Znajdź TrainRun w Active circulation którego endStation == home
            TrainRun picked = null;
            foreach (var tr in TimetableService.TrainRuns)
            {
                if (tr.circulationId < 0) continue;
                var circ = CirculationService.GetCirculation(tr.circulationId);
                if (circ == null || circ.status != CirculationStatus.Active) continue;
                if (GetRouteEndStationId(tr) != GameState.HomeDepotStationId) continue;
                picked = tr;
                break;
            }
            if (picked == null)
            {
                Log.Warn("[DepotMapHandshake DEBUG] Brak TrainRun z endStation == home. " +
                         "Utwórz obieg gdzie ostatni krok kończy się w home station.");
                return;
            }

            // Fake runningVehicleIds z circulation assignment (dowolnego dnia)
            var circulation = CirculationService.GetCirculation(picked.circulationId);
            List<int> fakeVehicles = null;
            foreach (var kvp in circulation.vehicleAssignmentsPerDay)
            {
                if (kvp.Value != null && kvp.Value.Count > 0) { fakeVehicles = kvp.Value; break; }
            }
            if (fakeVehicles == null)
            {
                Log.Warn("[DepotMapHandshake DEBUG] Obieg bez vehicle assignment");
                return;
            }

            picked.runningVehicleIds = new List<int>(fakeVehicles);
            Log.Info($"[DepotMapHandshake DEBUG] Simulating despawn of run#{picked.id} " +
                     $"(endStation=home={GameState.HomeDepotStationId}, vehicles=[{string.Join(",", fakeVehicles)}])");
            HandleRunDespawned(picked);
            picked.runningVehicleIds.Clear(); // cleanup jak zrobiłby DespawnTrain
        }

        [ContextMenu("Debug: Simulate run completion at NON-HOME (stay at station)")]
        public void DebugSimulateRunCompletionAwayFromHome()
        {
            // Znajdź TrainRun którego endStation != home
            TrainRun picked = null;
            foreach (var tr in TimetableService.TrainRuns)
            {
                if (tr.circulationId < 0) continue;
                var circ = CirculationService.GetCirculation(tr.circulationId);
                if (circ == null || circ.status != CirculationStatus.Active) continue;
                int endId = GetRouteEndStationId(tr);
                if (endId < 0 || endId == GameState.HomeDepotStationId) continue;
                picked = tr;
                break;
            }
            if (picked == null) { Log.Warn("[DepotMapHandshake DEBUG] Brak TrainRun z endStation != home"); return; }

            var circulation = CirculationService.GetCirculation(picked.circulationId);
            List<int> fakeVehicles = null;
            foreach (var kvp in circulation.vehicleAssignmentsPerDay)
            { if (kvp.Value != null && kvp.Value.Count > 0) { fakeVehicles = kvp.Value; break; } }
            if (fakeVehicles == null) { Log.Warn("[DepotMapHandshake DEBUG] Brak assignment"); return; }

            picked.runningVehicleIds = new List<int>(fakeVehicles);
            Log.Info($"[DepotMapHandshake DEBUG] Simulating despawn at non-home (endStation#{GetRouteEndStationId(picked)})");
            HandleRunDespawned(picked);
            picked.runningVehicleIds.Clear();
        }

        /// <summary>
        /// Czy któryś z pojazdów run.runningVehicleIds ma kolejny TrainRun dziś PO aktualnym czasie.
        /// Szuka innych TrainRun'ów tego samego circulationId, dziś, nieukończonych, startujących po now.
        /// </summary>
        static bool HasNextStepForAnyVehicle(TrainRun justFinishedRun)
        {
            if (justFinishedRun.circulationId < 0) return false; // standalone run

            string todayIso = GameState.CurrentDateIso;
            float nowSec = GameState.GameTimeSeconds;

            foreach (var tr in TimetableService.TrainRuns)
            {
                if (tr.id == justFinishedRun.id) continue;
                if (tr.circulationId != justFinishedRun.circulationId) continue;
                if (tr.runDateIso != todayIso) continue;
                if (tr.isCompleted || tr.isCancelled) continue;

                float depSec = tr.startMinutesFromMidnight * 60f + tr.currentDelaySec;
                if (depSec <= nowSec) continue; // już powinien był wyjechać, pomijamy

                // Który obieg, ten sam circulationId — zakładamy że vehicles są te same dziś.
                // (Per-day assignment jest per circulation+date, nie per step.)
                return true;
            }

            return false;
        }
    }
}
