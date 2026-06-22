using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M9c-D F4/F5: dostawa rozkładem. Buduje syntetyczny delivery TrainRun (A → B) i spawnuje go
    /// na mapie 2D. Pojazd jedzie jak normalny kurs; po dojeździe do home istniejący handshake
    /// (<c>DepotMapHandshakeService.HandleRunDespawned</c>, endStation==home, brak next step)
    /// wprowadza go do zajezdni. Po despawnie sprzątamy Route/Timetable/Run.
    ///
    /// Rdzeń <see cref="SpawnDeliveryRun"/> jest współdzielony: F4 (samojezdny → home),
    /// F5 dealer (wagon → home), F5 własne loco (leg1 home→punkt, leg2 punkt→home).
    /// </summary>
    public partial class DeliveryService
    {
        /// <summary>Założona prędkość rozkładowa dostawy [m/s] (~80 km/h) do wyliczenia czasu przejazdu.</summary>
        const float DeliverySchedSpeedMps = 80f / 3.6f;

        /// <summary>
        /// F4: dostawa własnym rozkładem (samojezdny → home). F5 dealer: wagon (dealerProvided=true).
        /// Ustawia status pojazdu na MovingOnMap + deliveryInProgress. Zwraca false gdy brak ścieżki.
        /// </summary>
        bool BuildAndSpawnDeliveryRun(FleetVehicleData v, bool dealerProvided = false)
        {
            // F4: pojazd samojezdny jedzie sam. F5: wagon pasywny dozwolony tylko gdy dealerProvided
            // (loko producenta zapewnia trakcję) — własne loco używa osobnego round-trip flow.
            bool selfPropelled = v.supportedTractions != null
                && v.supportedTractions.Exists(t => t != TractionType.None);
            if (!selfPropelled && !dealerProvided)
            {
                Log.Warn($"[DeliveryService] Pojazd #{v.id} nie jest samojezdny — wymaga lokomotywy " +
                         "(dostawa producenta lub własne loco).");
                return false;
            }

            var from = DeliveryLocator.ResolvePurchaseLocation(v.position?.externalLocation);
            var home = DeliveryLocator.ResolveHome();
            if (!from.IsValid || !home.IsValid)
            {
                Log.Warn("[DeliveryService] Rozkład dostawczy: nie rozwiązano punktu zakupu lub home");
                return false;
            }

            // Punkt zakupu == home → pojazd już na stacji zajezdni, wjazd wprost (bez przejazdu).
            if (from.nodeId == home.nodeId)
            {
                Log.Info($"[DeliveryService] Pojazd #{v.id} jest już w stacji home — wjazd do depot bez przejazdu");
                TriggerDepotEntry(v);
                return true;
            }

            int runId = SpawnDeliveryRun(new List<int> { v.id }, from, home);
            if (runId < 0) return false;

            v.status = FleetVehicleStatus.MovingOnMap;
            v.deliveryInProgress = true; // F7: recovery — dostawa dokończona po load gdy run zniknął
            v.currentTask = $"Dostawa rozkładem do {home.stationName}";
            return true;
        }

        /// <summary>
        /// Rdzeń: buduje delivery Route+Timetable+TrainRun (from → to) i spawnuje na mapie.
        /// Zwraca id runa lub -1. NIE zmienia statusu pojazdów (robi to caller wg kontekstu).
        /// </summary>
        int SpawnDeliveryRun(List<int> vehicleIds, DeliveryLocator.Resolved from, DeliveryLocator.Resolved to)
        {
            var init = TimetableInitializer.Instance;
            if (init == null || init.Graph == null)
            {
                Log.Warn("[DeliveryService] Delivery run: graf/inicjalizator niegotowy");
                return -1;
            }

            var path = RailwayPathfinder.FindPath(init.Graph, from.nodeId, to.nodeId);
            if (!path.success || path.nodeIds == null || path.nodeIds.Count < 2)
            {
                Log.Warn($"[DeliveryService] Delivery run: brak ścieżki {from.stationName} → {to.stationName}");
                return -1;
            }

            int travelSec = Mathf.Max(60, Mathf.RoundToInt(path.totalLengthM / DeliverySchedSpeedMps));
            int nowMin = ((int)(GameState.GameTimeSeconds / 60f)) % 1440;
            string today = GameState.CurrentDateIso;

            var route = new Route
            {
                name = $"Dostawa: {from.stationName} → {to.stationName}",
                nodeIds = new List<int>(path.nodeIds),
                totalLengthM = path.totalLengthM,
                isDeliveryRoute = true
            };
            route.stations.Add(MakeDeliveryStation(from, 0f));
            route.stations.Add(MakeDeliveryStation(to, path.totalLengthM));
            TimetableService.AddRoute(route);

            var tt = new Timetable
            {
                name = route.name,
                routeId = route.id,
                irjCategory = new IrjCategory(IrjGroup.EmptyPassenger, TractionLetter.ElectricUnit),
                trainNumber = $"DOST-{vehicleIds[0]}",
                frequency = FrequencySpec.SingleRun(nowMin),
                status = TimetableStatus.Active,
                isDeliveryTimetable = true
            };
            tt.stops.Add(new TimetableStop
            {
                stationNodeId = from.nodeId, stationName = from.stationName,
                plannedArrivalSec = 0, plannedDepartureSec = 0, distanceFromStartM = 0f
            });
            tt.stops.Add(new TimetableStop
            {
                stationNodeId = to.nodeId, stationName = to.stationName,
                plannedArrivalSec = travelSec, plannedDepartureSec = travelSec,
                distanceFromStartM = path.totalLengthM
            });
            TimetableService.AddTimetable(tt);

            var run = new TrainRun
            {
                id = TimetableService.AllocateTrainRunId(),
                timetableId = tt.id,
                circulationId = -1,
                circulationStepIndex = -1,
                isDeliveryRun = true,
                runDateIso = today,
                startMinutesFromMidnight = nowMin,
                trainNumberSnapshot = tt.trainNumber
            };
            TimetableService.TrainRuns.Add(run);

            var sim = TrainRunSimulator.Instance;
            if (sim == null)
            {
                Log.Warn("[DeliveryService] Delivery run: TrainRunSimulator niegotowy — cofam");
                CleanupDeliveryArtifacts(run, tt, route);
                return -1;
            }

            if (!sim.SpawnTrainFromVehicles(run, new List<int>(vehicleIds), from.position))
            {
                Log.Warn("[DeliveryService] Delivery run: spawn nie powiódł się");
                CleanupDeliveryArtifacts(run, tt, route);
                return -1;
            }

            Log.Info($"[DeliveryService] Delivery run#{run.id}: {from.stationName} → {to.stationName} " +
                     $"({path.totalLengthM / 1000f:F0} km, ~{travelSec / 60} min, vehicles=[{string.Join(",", vehicleIds)}])");
            return run.id;
        }

        static RouteStation MakeDeliveryStation(DeliveryLocator.Resolved loc, float distM) => new()
        {
            stationNodeId = loc.nodeId,
            stationName = loc.stationName,
            distanceFromStartM = distM,
            position = loc.position,
            isMajorStation = true
        };

        /// <summary>
        /// Cleanup delivery run po despawnie. F5: jeśli to noga fetcha (loco po wagon) — najpierw
        /// łańcuchuj (leg1 → leg2), potem sprzątnij artefakty. Handshake (wjazd/AtStation) już zadziałał.
        /// </summary>
        void HandleDeliveryRunDespawned(TrainRun run)
        {
            if (run == null || !run.isDeliveryRun) return;

            // F5: obsługa nogi fetcha (przed cleanupem — leg2 trzeba zbudować z danych jobu)
            HandleFetchLegDespawned(run);

            var tt = TimetableService.GetTimetable(run.timetableId);
            var route = tt != null ? TimetableService.GetRoute(tt.routeId) : null;
            CleanupDeliveryArtifacts(run, tt, route);
            Log.Info($"[DeliveryService] Posprzątano delivery run#{run.id} (timetable#{run.timetableId})");
        }

        static void CleanupDeliveryArtifacts(TrainRun run, Timetable tt, Route route)
        {
            if (run != null) TimetableService.TrainRuns.Remove(run);
            if (tt != null) TimetableService.Timetables.Remove(tt);
            if (route != null) TimetableService.Routes.Remove(route);
        }
    }
}
