using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.Timetable.Simulation;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M9c-D F5: testy round-trip własnej lokomotywy po wagon. Headless EditMode pokrywa:
    /// selekcję wolnej loko (FindAvailableFetchLoco — reflection) i guardy
    /// RequestOwnLocoWagonDelivery. Pełny round-trip (leg1→leg2 chaining przez OnRunDespawned)
    /// wymaga grafu → PlayMode (osobno, jeśli dojdzie). Tu chronimy logikę selekcji+guardów,
    /// bo to najłatwiejsze do zepsucia (warunki na status/typ/lokalizację).
    /// </summary>
    public class WagonFetchTests
    {
        const int WagonId = 950001;
        const int LocoId = 950002;

        int _homeBackup;
        DeliveryService _svc;
        MethodInfo _findLoco;

        [SetUp]
        public void SetUp()
        {
            _homeBackup = GameState.HomeDepotStationId;
            GameState.HomeDepotStationId = 600;

            DestroyExisting<VehicleLocationService>();
            DestroyExisting<DeliveryService>();
            CreateWithAwake<VehicleLocationService>();
            VehicleLocationService.ResetAll();
            _svc = CreateWithAwake<DeliveryService>();

            _findLoco = typeof(DeliveryService).GetMethod(
                "FindAvailableFetchLoco", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(_findLoco, Is.Not.Null, "FindAvailableFetchLoco musi istnieć (reflection).");

            FleetService.RemoveOwnedVehicle(WagonId);
            FleetService.RemoveOwnedVehicle(LocoId);
        }

        [TearDown]
        public void TearDown()
        {
            FleetService.RemoveOwnedVehicle(WagonId);
            FleetService.RemoveOwnedVehicle(LocoId);
            VehicleLocationService.ResetAll();
            DestroyExisting<DeliveryService>();
            DestroyExisting<VehicleLocationService>();
            GameState.HomeDepotStationId = _homeBackup;
        }

        FleetVehicleData FindLoco() => (FleetVehicleData)_findLoco.Invoke(_svc, null);

        static FleetVehicleData MakeWagon()
        {
            var w = new FleetVehicleData
            {
                id = WagonId, series = "WAG", type = FleetVehicleType.PassengerCar,
                status = FleetVehicleStatus.AwaitingPickup,
                supportedTractions = new List<TractionType> { TractionType.None },
                position = new VehiclePosition { externalLocation = "Krakow" }
            };
            FleetService.AddOwnedVehicle(w);
            return w;
        }

        /// <summary>Loco StoppedInDepot + InDepot (gotowa do fetcha). Zwraca instancję.</summary>
        static FleetVehicleData MakeAvailableLoco()
        {
            var l = new FleetVehicleData
            {
                id = LocoId, series = "EU07", type = FleetVehicleType.ElectricLocomotive,
                status = FleetVehicleStatus.StoppedInDepot,
                supportedTractions = new List<TractionType> { TractionType.Electric }
            };
            FleetService.AddOwnedVehicle(l);
            VehicleLocationService.Instance.SetInDepot(LocoId, depotTrackId: 1);
            return l;
        }

        [Test]
        public void FindLoco_ReturnsAvailableLocoInDepot()
        {
            MakeAvailableLoco();
            Assert.That(FindLoco()?.id, Is.EqualTo(LocoId));
        }

        [Test]
        public void FindLoco_IgnoresNonLocomotive()
        {
            // Wagon pasywny StoppedInDepot+InDepot — nie jest lokomotywą, nie może ciągnąć.
            var w = MakeWagon();
            w.status = FleetVehicleStatus.StoppedInDepot;
            VehicleLocationService.Instance.SetInDepot(WagonId, 1);

            Assert.That(FindLoco(), Is.Null, "Wagon (PassengerCar) nie jest kandydatem na fetch-loco.");
        }

        [Test]
        public void FindLoco_IgnoresLocoNotInDepotState()
        {
            var l = MakeAvailableLoco();
            l.status = FleetVehicleStatus.MovingOnMap; // w trasie — niedostępna
            Assert.That(FindLoco(), Is.Null);
        }

        [Test]
        public void FindLoco_IgnoresLocoWithDeliveryInProgress()
        {
            var l = MakeAvailableLoco();
            l.deliveryInProgress = true; // już wysłana po inny wagon
            Assert.That(FindLoco(), Is.Null);
        }

        [Test]
        public void RequestOwnLoco_RejectedWhenWagonNotAwaitingPickup()
        {
            var w = MakeWagon();
            w.status = FleetVehicleStatus.StoppedInDepot;
            Assert.That(_svc.RequestOwnLocoWagonDelivery(w), Is.False);
        }

        [Test]
        public void RequestOwnLoco_RejectedWhenNoHome()
        {
            var w = MakeWagon();
            GameState.HomeDepotStationId = -1;
            Assert.That(_svc.RequestOwnLocoWagonDelivery(w), Is.False);
        }

        [Test]
        public void RequestOwnLoco_PurchaseEqualsHome_EntersDepotDirectly()
        {
            // Headless: brak grafu → purchase fallbackuje do home → from.nodeId==home.nodeId →
            // wagon wjeżdża wprost (TriggerDepotEntry, fallback InDepot bez symulatora). Loco zbędne.
            var w = MakeWagon();

            bool ok = _svc.RequestOwnLocoWagonDelivery(w);

            Assert.That(ok, Is.True);
            Assert.That(w.status, Is.EqualTo(FleetVehicleStatus.StoppedInDepot),
                "Wagon kupiony w stacji home wjeżdża wprost, bez wysyłania lokomotywy.");
            Assert.That(VehicleLocationService.Instance.Get(WagonId)?.type,
                Is.EqualTo(VehicleLocationType.InDepot));
        }

        // ── Helpers ──────────────────────────────────────────────────

        static T CreateWithAwake<T>() where T : MonoBehaviour
        {
            var go = new GameObject(typeof(T).Name + "_Test");
            var comp = go.AddComponent<T>();
            typeof(T).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(comp, null);
            return comp;
        }

        static void DestroyExisting<T>() where T : MonoBehaviour
        {
            foreach (var c in Resources.FindObjectsOfTypeAll<T>())
                Object.DestroyImmediate(c.gameObject);
        }
    }
}
