using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Simulation
{
    public partial class TrainRunSimulator
    {
        // ── Spawn / Despawn lifecycle ───────────────────────────────

        void CheckForNewTrains(string todayIso)
        {
            var trainRuns = TimetableService.TrainRuns;
            for (int i = 0; i < trainRuns.Count; i++)
            {
                var tr = trainRuns[i];
                if (_activeTrains.ContainsKey(tr.id)) continue;
                if (!ShouldStart(tr, todayIso)) continue;

                SpawnTrain(tr);
            }
        }

        bool ShouldStart(TrainRun tr, string todayIso)
        {
            if (tr.isCompleted || tr.isCancelled) return false;
            if (tr.runDateIso != todayIso) return false;

            // Planowany odjazd + kaskadowe opóźnienie z poprzedniego kursu
            float departureTimeSec = tr.startMinutesFromMidnight * 60f + tr.currentDelaySec;
            if (GameState.GameTimeSeconds < departureTimeSec) return false;

            // M9c-4: Block auto-spawn gdy pojazdy są InDepot — handshake jeszcze nie zaszedł.
            // Gracz musi kliknąć "Wyjedź z depot" (lub AI dyżurny w M8). Dopóki tego nie zrobi,
            // run czeka — currentDelaySec będzie rósł (propagation do następnych kroków obiegu).
            if (tr.circulationId >= 0)
            {
                var circ = CirculationService.GetCirculation(tr.circulationId);
                if (circ != null)
                {
                    var vehicles = circ.GetVehiclesForDate(tr.runDateIso);
                    var locSvc = VehicleLocationService.Instance;
                    if (vehicles != null && vehicles.Count > 0 && locSvc != null)
                    {
                        // M9c-D F6: pojazd niedostarczony (w produkcji / dostawie / oczekuje na odbiór)
                        // → kurs nie startuje (brak fizycznie dostępnego taboru). Bez akumulacji delay —
                        // to nie opóźnienie ruchu, tylko brak pojazdu (handshake/dostawa to rozwiąże).
                        foreach (int vid in vehicles)
                        {
                            var fv = RailwayManager.Fleet.FleetService.GetOwnedById(vid);
                            if (fv != null && (fv.status == RailwayManager.Fleet.FleetVehicleStatus.InProduction
                                            || fv.status == RailwayManager.Fleet.FleetVehicleStatus.InTransit
                                            || fv.status == RailwayManager.Fleet.FleetVehicleStatus.AwaitingPickup))
                                return false;
                        }

                        foreach (int vid in vehicles)
                        {
                            var rec = locSvc.Get(vid);
                            if (rec != null && (rec.type == VehicleLocationType.InDepot
                                              || rec.type == VehicleLocationType.ExitingDepot))
                            {
                                // Jakikolwiek pojazd jeszcze w depocie — czekamy, run się opóźnia
                                tr.currentDelaySec = Mathf.Max(tr.currentDelaySec,
                                    Mathf.CeilToInt(GameState.GameTimeSeconds - tr.startMinutesFromMidnight * 60f));
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        void SpawnTrain(TrainRun tr)
        {
            // M8-13: Sprawdz obsade zalogi (jesli hook Personnel aktywny + flag ON).
            // M9c-D F4: delivery run (dostawa do depot) pomija crew check — deadhead bez załogi gracza.
            if (!tr.isDeliveryRun && CrewCheckHook != null && !CrewCheckHook(tr.id, tr.runDateIso))
            {
                // Brak zalogi — odroc start, kumulowane opoznienie
                int elapsedSec = Mathf.CeilToInt(GameState.GameTimeSeconds - tr.startMinutesFromMidnight * 60f);
                tr.currentDelaySec = Mathf.Max(tr.currentDelaySec, elapsedSec + 60);
                return;
            }

            var timetable = TimetableService.GetTimetable(tr.timetableId);
            if (timetable == null)
            {
                Log.Warn($"[TrainRunSimulator] Brak Timetable #{tr.timetableId} dla TrainRun #{tr.id}");
                return;
            }

            var route = TimetableService.GetRoute(timetable.routeId);
            if (route == null)
            {
                Log.Warn($"[TrainRunSimulator] Brak Route #{timetable.routeId} dla Timetable #{timetable.id}");
                return;
            }

            // Graf potrzebny do obliczenia dystansów przystanków
            var graph = GetGraph();

            // Reset runtime fields TrainRun (mogą mieć stale values po domain reload).
            // M9c-D F6: NIE zerujemy currentDelaySec — to kasowało propagację kaskady opóźnień
            // (PropagateDelayToNextRun ustawia delay następnego kroku obiegu) oraz spóźniony start
            // po wyjeździe z depot. Delay jest akumulowany w Advance, świeży TrainRun ma 0 z generacji.
            tr.currentPositionOnRouteM = 0f;
            tr.isCompleted = false;
            tr.isCancelled = false;

            var st = new SimulatedTrain(tr, timetable, route, graph);
            st.state = TrainState.Running;
            st.currentStopIndex = 1; // jedzie do stops[1] (stops[0] = stacja startowa)
            if (graph == null)
            {
                Log.Warn($"[TrainRunSimulator] UWAGA: PathfindingGraph jest null! " +
                         $"TimetableInitializer.Instance={(TimetableInitializer.Instance != null ? "OK" : "NULL")}. " +
                         $"Pociąg nie będzie widoczny na mapie.");
            }

            // Diagnostyka trasy
            var activeNodeIds = st.effectiveNodeIds ?? route.nodeIds;
            int nodeCount = activeNodeIds?.Count ?? 0;
            Vector2 startPos = Vector2.zero;
            if (graph != null && nodeCount >= 2)
            {
                startPos = graph.GetNode(activeNodeIds[0]).position;
            }

            // Wizualizacja — prostokąt na mapie
            CreateVisual(st);

            _activeTrains[tr.id] = st;

            // Dodaj do panelu "Pociągi" po lewej stronie mapy (wspólne z TD-037 restore)
            AddToTrainListUI(st);

            Log.Info($"[TrainRunSimulator] Spawn: #{tr.id} '{tr.trainNumberSnapshot}' " +
                     $"trasa '{route.name}' ({route.totalLengthM:F0}m, {timetable.stops.Count} przystanków, " +
                     $"{nodeCount} nodes, startPos=({startPos.x:F0},{startPos.y:F0}), " +
                     $"graph={(graph != null ? "OK" : "NULL")})");

            // Dump pierwszych kilku przystanków — obliczone dystanse z Route.stations
            var stops = timetable.stops;
            for (int i = 0; i < Mathf.Min(stops.Count, 5); i++)
            {
                var s = stops[i];
                float computedDist = st.stopDistancesM[i];
                Log.Info($"  stop[{i}]: '{s.stationName}' dist={computedDist:F0}m, " +
                         $"arr={s.plannedArrivalSec}s, dep={s.plannedDepartureSec}s");
            }
            if (stops.Count > 5)
                Log.Info($"  ... i {stops.Count - 5} więcej");

            // Occupy first block
            if (st.routeBlockCount > 0)
            {
                int firstKey = st.routeBlockKeys[0];
                OccupyBlock(firstKey, tr.id);
                Log.Info($"  blocks: {st.routeBlockCount} (same as planning), " +
                         $"first blockKey={firstKey} occupied");
            }

            // M9c handshake: emit event — VehicleLocationService nasłuchuje
            // i set'uje pojazdy na OnRoute. runningVehicleIds może być ustawione
            // wcześniej (przez SpawnTrainFromVehicles) lub pusty (legacy spawn auto).
            OnRunSpawned?.Invoke(tr);
        }

        void CollectAndDespawnCompleted()
        {
            _despawnBuffer.Clear();

            foreach (var kvp in _activeTrains)
            {
                if (kvp.Value.state == TrainState.Completed)
                    _despawnBuffer.Add(kvp.Key);
            }

            for (int i = 0; i < _despawnBuffer.Count; i++)
            {
                var st = _activeTrains[_despawnBuffer[i]];
                DespawnTrain(st);
            }
        }

        void DespawnTrain(SimulatedTrain st)
        {
            st.trainRun.isCompleted = true;

            // Kaskada opóźnień do następnego kursu w obiegu (Etap 7)
            PropagateDelayToNextRun(st);

            // Release all occupancies for this train
            ReleaseAllBlocks(st.trainRun.id);
            ReleaseAllPlatforms(st.trainRun.id);

            // TD-013: cleanup z block-wait indexu (jeśli pociąg był BlockedBySignal w trakcie despawn)
            UnregisterFromBlockWaitIndex(st.trainRun.id);

            // Usuń z listy w panelu "Pociągi"
            var listUi = MapSystem.MapTrainListUI.Instance;
            if (listUi != null)
                listUi.RemoveTrain(st.trainRun.id);

            _activeTrains.Remove(st.trainRun.id);
            _alreadyWarnedTrains.Remove(st.trainRun.id); // BUG-033: clear log suppression flag

            if (st.visual != null)
                Destroy(st.visual);

            Log.Info($"[TrainRunSimulator] Despawn: #{st.trainRun.id} '{st.trainRun.trainNumberSnapshot}' " +
                     $"(delay={st.trainRun.currentDelaySec}s)");

            // M6-5: reputation events based on final delay
            ApplyDelayReputationEvent(st);

            // M9c handshake: emit event — VehicleLocationService nasłuchuje i decyduje
            // co z pojazdami (entry do depot jeśli home, inaczej AtStation).
            OnRunDespawned?.Invoke(st.trainRun);

            // Wyczyść runningVehicleIds po emit — kolejny spawn może mieć inny assignment
            st.trainRun.runningVehicleIds.Clear();
        }
    }
}
