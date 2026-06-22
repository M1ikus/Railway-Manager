using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// TD-037: snapshot stanu aktywnego pociągu (pola SimulatedTrain, które NIE są derived).
    /// Serializowany przez SaveLoad `TrainRunsSavable` (moduł "trainruns"); cała geometria/cache
    /// (polyliny, bloki, profil prędkości, visual) odtwarza się z grafu w konstruktorze SimulatedTrain.
    /// </summary>
    [Serializable]
    public class ActiveRunSnapshot
    {
        public int trainRunId;

        // Fizyka + progres
        public float currentSpeedMps;
        public TrainState state;
        public int currentStopIndex;
        public int currentBlockIndex;

        // Awaria w trasie (M7-3) — timery absolutne game-time, tick podejmuje po load
        public int brokenComponentIndex = -1;
        public long breakdownStartedGameTime;
        public long selfRepairAttemptGameTime;
        public int brokenVehicleId = -1;
        public bool doorsBroken;
        public int wheelsSpeedLimitedKmh;
    }

    public partial class TrainRunSimulator
    {
        // ── TD-037: pending-restore aktywnych kursów ─────────────────
        //
        // SaveOrchestrator deserializuje SYNCHRONICZNIE, ale graf pathfindingu mapy buduje się
        // ASYNC (TimetableInitializer 8-step streaming) — odtworzenie SimulatedTrain wymaga grafu.
        // Moduł "trainruns" odkłada więc snapshoty tutaj (static — symulator może jeszcze nie
        // istnieć w momencie Deserialize), a FixedUpdate konsumuje je pod gate'em gotowości
        // (TryConsumePendingRestore → RestoreActiveRun). Wzorzec: TD-031 RestoreActiveMove.

        static List<ActiveRunSnapshot> _pendingRestore;
        float _restoreWaitLogTimer;

        /// <summary>TD-037: odkłada snapshoty do wznowienia po gotowości grafu. Null/empty czyści.</summary>
        public static void SetPendingRestore(List<ActiveRunSnapshot> snapshots)
        {
            _pendingRestore = (snapshots != null && snapshots.Count > 0) ? snapshots : null;
        }

        /// <summary>Liczba snapshotów czekających na wznowienie (diagnostyka + testy).</summary>
        public static int PendingRestoreCount => _pendingRestore?.Count ?? 0;

        /// <summary>
        /// TD-037: buduje snapshoty wszystkich aktywnych pociągów (do save). Kolejność deterministyczna
        /// (sort po trainRunId — Dictionary nie gwarantuje kolejności).
        /// </summary>
        public List<ActiveRunSnapshot> BuildActiveSnapshots()
        {
            var result = new List<ActiveRunSnapshot>(_activeTrains.Count);
            foreach (var kv in _activeTrains)
            {
                var st = kv.Value;
                if (st == null || st.trainRun == null) continue;
                result.Add(new ActiveRunSnapshot
                {
                    trainRunId = st.trainRun.id,
                    currentSpeedMps = st.currentSpeedMps,
                    state = st.state,
                    currentStopIndex = st.currentStopIndex,
                    currentBlockIndex = st.currentBlockIndex,
                    brokenComponentIndex = st.brokenComponentIndex,
                    breakdownStartedGameTime = st.breakdownStartedGameTime,
                    selfRepairAttemptGameTime = st.selfRepairAttemptGameTime,
                    brokenVehicleId = st.brokenVehicleId,
                    doorsBroken = st.doorsBroken,
                    wheelsSpeedLimitedKmh = st.wheelsSpeedLimitedKmh,
                });
            }
            result.Sort((a, b) => a.trainRunId.CompareTo(b.trainRunId));
            return result;
        }

        // ── Konsumpcja (FixedUpdate, deterministyczna) ────────────────

        /// <summary>
        /// TD-037: konsumuje pending-restore gdy świat gotowy (graf + stacje z TimetableInitializer).
        /// Wołane na początku FixedUpdate, PRZED CheckForNewTrains — wznowione kursy nie mogą zostać
        /// uznane za „nowe do spawnu". Pending przeżywa do skutku (throttled log gdy czeka).
        /// </summary>
        void TryConsumePendingRestore(float dt)
        {
            if (_pendingRestore == null) return;

            var graph = GetGraph();
            var init = TimetableInitializer.Instance;
            if (graph == null || init == null || init.Stations == null)
            {
                _restoreWaitLogTimer += dt;
                if (_restoreWaitLogTimer >= 5f)
                {
                    _restoreWaitLogTimer = 0f;
                    Log.Info($"[TrainRunSimulator] TD-037: {_pendingRestore.Count} snapshot(ów) czeka na gotowość grafu…");
                }
                return;
            }

            var snapshots = _pendingRestore;
            _pendingRestore = null;
            _restoreWaitLogTimer = 0f;

            int restored = 0, stale = 0, skipped = 0;
            foreach (var snap in snapshots)
            {
                switch (RestoreActiveRun(snap, graph))
                {
                    case RestoreResult.Restored: restored++; break;
                    case RestoreResult.StaleCancelled: stale++; break;
                    default: skipped++; break;
                }
            }
            Log.Info($"[TrainRunSimulator] TD-037 restore: {restored} wznowionych, {stale} stale-cancelled, {skipped} pominiętych");

            // TD-037 C: po wznowieniu — sieroty lokacji (OnRoute bez żywego runa) → AtStation fallback
            CleanupOrphanedOnRouteRecords();

            // TD-037 F: rolling window — save starszy niż okno generacji odżywa od dziś
            // (dogenerowanie brakujących dat dla Active obiegów; ID istniejących nietknięte).
            TrainRunWindowTopUp.TopUpAllActive();
        }

        enum RestoreResult { Restored, StaleCancelled, Skipped }

        /// <summary>
        /// TD-037: wznawia jeden aktywny kurs ze snapshotu — lustro SpawnTrain BEZ resetu pól runtime
        /// (pozycja/delay/pojazdy z save). Derived (polyliny/bloki/profil) liczy konstruktor SimulatedTrain.
        /// </summary>
        RestoreResult RestoreActiveRun(ActiveRunSnapshot snap, PathfindingGraph graph)
        {
            if (snap == null) return RestoreResult.Skipped;

            TrainRun tr = null;
            var runs = TimetableService.TrainRuns;
            for (int i = 0; i < runs.Count; i++)
                if (runs[i].id == snap.trainRunId) { tr = runs[i]; break; }
            if (tr == null)
            {
                Log.Warn($"[TrainRunSimulator] TD-037: snapshot run#{snap.trainRunId} bez TrainRun w save — pomijam");
                return RestoreResult.Skipped;
            }
            if (_activeTrains.ContainsKey(tr.id)) return RestoreResult.Skipped;
            if (tr.isCompleted || tr.isCancelled) return RestoreResult.Skipped;

            // Stale-policy: nieukończony kurs z INNEGO dnia (save sprzed >0 dni) → anuluj, nie spawnuj
            // ducha z przeszłości. Dzisiejsze nieaktywne runy idą naturalną ścieżką ShouldStart.
            if (tr.runDateIso != GameState.CurrentDateIso)
            {
                tr.isCancelled = true;
                Log.Info($"[TrainRunSimulator] TD-037: run#{tr.id} z {tr.runDateIso} ≠ dziś — stale-cancel");
                return RestoreResult.StaleCancelled;
            }

            var timetable = TimetableService.GetTimetable(tr.timetableId);
            var route = timetable != null ? TimetableService.GetRoute(timetable.routeId) : null;
            if (timetable == null || route == null)
            {
                Log.Warn($"[TrainRunSimulator] TD-037: run#{tr.id} bez Timetable/Route — pomijam restore");
                return RestoreResult.Skipped;
            }

            // Rebuild — derived geometry/cache z grafu; pola runtime TrainRun (pozycja/delay) NIETKNIĘTE
            var st = new SimulatedTrain(tr, timetable, route, graph);
            st.currentSpeedMps = snap.currentSpeedMps;
            st.state = snap.state;
            st.currentStopIndex = Mathf.Clamp(snap.currentStopIndex, 0, Mathf.Max(0, timetable.stops.Count - 1));
            st.currentBlockIndex = snap.currentBlockIndex;
            st.brokenComponentIndex = snap.brokenComponentIndex;
            st.breakdownStartedGameTime = snap.breakdownStartedGameTime;
            st.selfRepairAttemptGameTime = snap.selfRepairAttemptGameTime;
            st.brokenVehicleId = snap.brokenVehicleId;
            st.doorsBroken = snap.doorsBroken;
            st.wheelsSpeedLimitedKmh = snap.wheelsSpeedLimitedKmh;
            // Koszty per-km naliczane od delty — start od zapisanej pozycji (zero double-charge po load)
            st.lastCostDistanceM = tr.currentPositionOnRouteM;

            if (st.state == TrainState.Completed)
            {
                tr.isCompleted = true;
                return RestoreResult.Skipped;
            }

            CreateVisual(st);
            _activeTrains[tr.id] = st;
            UpdateVisualPosition(st);

            // Re-okupacja bieżącego bloku (historia zbędna — kolizje sprawdzane look-ahead na bieżąco)
            if (st.routeBlockCount > 0)
            {
                int bi = Mathf.Clamp(st.currentBlockIndex, 0, st.routeBlockCount - 1);
                OccupyBlock(st.routeBlockKeys[bi], tr.id);
            }

            // Peron: w StoppedAtStation currentStopIndex wskazuje BIEŻĄCY przystanek (inkrement przy odjeździe)
            if (st.state == TrainState.StoppedAtStation
                && st.currentStopIndex >= 0 && st.currentStopIndex < timetable.stops.Count)
            {
                OccupyPlatform(timetable.stops[st.currentStopIndex].platformId, tr.id);
            }

            AddToTrainListUI(st);

            Log.Info($"[TrainRunSimulator] TD-037 wznowiono: #{tr.id} '{tr.trainNumberSnapshot}' " +
                     $"state={st.state} pos={tr.currentPositionOnRouteM:F0}m v={st.currentSpeedMps * 3.6f:F0}km/h " +
                     $"delay={tr.currentDelaySec}s stop={st.currentStopIndex} block={st.currentBlockIndex}");

            // Załoga re-embeduje się sama (Personnel nasłuchuje), VehicleLocationService set'uje OnRoute
            OnRunSpawned?.Invoke(tr);
            return RestoreResult.Restored;
        }

        /// <summary>Wpis w panelu „Pociągi" (wspólne dla SpawnTrain i restore).</summary>
        void AddToTrainListUI(SimulatedTrain st)
        {
            var listUi = MapSystem.MapTrainListUI.Instance;
            if (listUi == null) return;
            var cat = IrjCategoryCatalog.GetCode(st.timetable.irjCategory);
            string itemName = $"{st.trainRun.trainNumberSnapshot} ({cat})";
            listUi.AddTrain(st.trainRun.id, itemName, st.route.name,
                GetCategoryColor(st.timetable.irjCategory.group),
                () => TrainPopupUI.Instance?.Show(st));
        }

        /// <summary>
        /// TD-037 C: sieroty po duchach kursów — rekordy OnRoute (zapisane w fleet) wskazujące na run,
        /// który NIE jest aktywny po restore (stale-cancelled / completed / brak) → AtStation fallback
        /// na ostatniej znanej pozycji + warn. Nie zombie. Bufor przed mutacją — GetOnRoute zwraca
        /// żywą listę indeksu _byType, a SetAtStation ją modyfikuje.
        /// </summary>
        void CleanupOrphanedOnRouteRecords()
        {
            var locSvc = VehicleLocationService.Instance;
            if (locSvc == null) return;

            var orphans = new List<VehicleLocationRecord>();
            foreach (var rec in locSvc.GetOnRoute())
            {
                if (rec == null) continue;
                if (rec.currentTrainRunId >= 0 && _activeTrains.ContainsKey(rec.currentTrainRunId)) continue;
                orphans.Add(rec);
            }

            foreach (var rec in orphans)
            {
                Log.Warn($"[TrainRunSimulator] TD-037: pojazd #{rec.vehicleId} OnRoute z martwym run#{rec.currentTrainRunId} " +
                         "— fallback AtStation (ostatnia znana pozycja)");
                locSvc.SetAtStation(rec.vehicleId, rec.stationId, rec.worldMapPosition);
            }
        }
    }
}
