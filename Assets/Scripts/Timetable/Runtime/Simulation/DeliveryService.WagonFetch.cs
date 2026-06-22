using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Fleet;
using DepotSystem;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M9c-D F5: dostawa wagonu pasywnego WŁASNĄ lokomotywą (round-trip, za darmo poza eksploatacją).
    ///
    /// Flow:
    ///   1. Gracz wybiera "Wyślij własną lokomotywę po wagon".
    ///   2. Wolna loko z home depot wyrusza (despawn z depot) — leg 1: home → punkt zakupu.
    ///   3. Po dojechaniu (handshake zostawia loko AtStation punktu) — leg 2: loko+wagon → home.
    ///   4. Oba dojeżdżają do home → handshake wprowadza skład do zajezdni (loko wciąga wagon).
    ///
    /// Stan (<see cref="_fetchJobs"/>) jest runtime-only; po wczytaniu save oba pojazdy mają
    /// <see cref="FleetVehicleData.deliveryInProgress"/>=true → recovery (ProcessVehicle MovingOnMap)
    /// dowozi je do depot. Job jest tracony, ale pojazdy nie giną.
    /// </summary>
    public partial class DeliveryService
    {
        class FetchJob
        {
            public int locoId;
            public int wagonId;
            public int leg; // 1 = loko jedzie po wagon; 2 = loko+wagon wraca do home
            public DeliveryLocator.Resolved purchase;
            public DeliveryLocator.Resolved home;
        }

        readonly Dictionary<int, FetchJob> _fetchJobs = new();   // runId → job
        readonly HashSet<int> _locosFetching = new();             // locoId zajęte fetchem

        /// <summary>Znajduje wolną lokomotywę w home depot do wysłania po wagon. Null gdy brak.</summary>
        FleetVehicleData FindAvailableFetchLoco()
        {
            var locSvc = VehicleLocationService.Instance;
            foreach (var lv in FleetService.OwnedVehicles)
            {
                if (lv == null) continue;
                if (lv.type != FleetVehicleType.ElectricLocomotive && lv.type != FleetVehicleType.DieselLocomotive)
                    continue;
                if (lv.status != FleetVehicleStatus.StoppedInDepot) continue;
                if (lv.deliveryInProgress || _locosFetching.Contains(lv.id)) continue;
                var rec = locSvc?.Get(lv.id);
                if (rec == null || rec.type != VehicleLocationType.InDepot) continue;
                return lv;
            }
            return null;
        }

        /// <summary>
        /// F5: wysyła własną lokomotywę po wagon (round-trip). False gdy brak wolnej loko / ścieżki / home.
        /// </summary>
        public bool RequestOwnLocoWagonDelivery(FleetVehicleData wagon)
        {
            if (wagon == null || wagon.status != FleetVehicleStatus.AwaitingPickup) return false;
            if (!GameState.IsHomeDepotSet)
            {
                Log.Warn("[DeliveryService] Fetch: brak home depot");
                return false;
            }

            var from = DeliveryLocator.ResolvePurchaseLocation(wagon.position?.externalLocation);
            var home = DeliveryLocator.ResolveHome();
            if (!from.IsValid || !home.IsValid)
            {
                Log.Warn("[DeliveryService] Fetch: nie rozwiązano punktu zakupu / home");
                return false;
            }

            // Wagon stoi już w stacji home → loko niepotrzebne, wjazd wprost.
            if (from.nodeId == home.nodeId)
            {
                TriggerDepotEntry(wagon);
                return true;
            }

            var loco = FindAvailableFetchLoco();
            if (loco == null)
            {
                Log.Warn("[DeliveryService] Fetch: brak wolnej lokomotywy w zajezdni");
                return false;
            }

            // Leg 1: loko home → punkt zakupu (spawn na mapie zanim usuniemy z depot, by uniknąć limbo).
            int leg1Run = SpawnDeliveryRun(new List<int> { loco.id }, home, from);
            if (leg1Run < 0) return false;

            // Loko opuszcza zajezdnię (usuń visual + zwolnij tor).
            DepotMovementSimulator.Instance?.DespawnParkedConsist(new List<int> { loco.id });

            loco.status = FleetVehicleStatus.MovingOnMap;
            loco.deliveryInProgress = true;
            loco.currentTask = $"Jedzie po wagon do {from.stationName}";
            // Wagon zostaje AwaitingPickup w punkcie zakupu (normalna recovery po load); flagę
            // deliveryInProgress dostaje dopiero w leg2, gdy faktycznie jedzie (MovingOnMap).

            _fetchJobs[leg1Run] = new FetchJob
            {
                locoId = loco.id, wagonId = wagon.id, leg = 1, purchase = from, home = home
            };
            _locosFetching.Add(loco.id);

            Log.Info($"[DeliveryService] Loko #{loco.id} '{loco.series}' wyrusza po wagon #{wagon.id} " +
                     $"({from.stationName}), leg1 run#{leg1Run}");
            return true;
        }

        /// <summary>
        /// Wywoływane z <see cref="HandleDeliveryRunDespawned"/> PRZED cleanupem artefaktów.
        /// Leg1 dotarł do punktu → uruchom leg2 (loko+wagon → home). Leg2 dotarł do home → handshake
        /// już wprowadził skład do depot, kończymy job.
        /// </summary>
        void HandleFetchLegDespawned(TrainRun run)
        {
            if (!_fetchJobs.TryGetValue(run.id, out var job)) return;
            _fetchJobs.Remove(run.id);

            if (job.leg == 1)
            {
                var loco = FleetService.GetOwnedById(job.locoId);
                var wagon = FleetService.GetOwnedById(job.wagonId);
                if (loco == null || wagon == null)
                {
                    _locosFetching.Remove(job.locoId);
                    Log.Warn($"[DeliveryService] Fetch leg1: pojazd zniknął (loco={job.locoId}, wagon={job.wagonId})");
                    return;
                }

                // Leg 2: loko + wagon, punkt zakupu → home (loko ciągnie wagon).
                int leg2Run = SpawnDeliveryRun(new List<int> { job.locoId, job.wagonId }, job.purchase, job.home);
                if (leg2Run < 0)
                {
                    // Nie udało się — loko zostaje AtStation(punkt) + deliveryInProgress → recovery dowiezie je.
                    _locosFetching.Remove(job.locoId);
                    Log.Warn($"[DeliveryService] Fetch leg2 spawn nieudany — loko #{job.locoId} dojedzie przez recovery");
                    return;
                }

                job.leg = 2;
                _fetchJobs[leg2Run] = job;
                wagon.status = FleetVehicleStatus.MovingOnMap;
                wagon.deliveryInProgress = true; // teraz jedzie (leg2) → recovery dowiezie po load
                wagon.currentTask = $"Wieziony przez loko #{job.locoId} do zajezdni";
                Log.Info($"[DeliveryService] Fetch leg2: loko #{job.locoId} ciągnie wagon #{job.wagonId} → home (run#{leg2Run})");
            }
            else
            {
                // Leg 2 dotarł do home — handshake (endStation==home) już zlecił wjazd składu [loko,wagon]
                // do depot; MarkInDepot zresetuje deliveryInProgress obu pojazdom.
                _locosFetching.Remove(job.locoId);
                Log.Info($"[DeliveryService] Fetch zakończony: loko #{job.locoId} + wagon #{job.wagonId} w zajezdni");
            }
        }
    }
}
