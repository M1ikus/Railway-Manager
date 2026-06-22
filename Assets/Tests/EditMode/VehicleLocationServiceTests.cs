using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RailwayManager.Core;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    public class VehicleLocationServiceTests
    {
        private int _homeDepotBackup;

        [SetUp]
        public void SetUp()
        {
            _homeDepotBackup = GameState.HomeDepotStationId;
            GameState.HomeDepotStationId = 1001;
            DestroyExistingService();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyExistingService();
            GameState.HomeDepotStationId = _homeDepotBackup;
        }

        [Test]
        public void Transitions_UpdateIndexesAndEmitOnlyWhenTypeChanges()
        {
            var service = CreateService();
            var events = new List<(int vehicleId, VehicleLocationType oldType, VehicleLocationType newType)>();
            service.OnLocationChanged += (vehicleId, oldType, newType) => events.Add((vehicleId, oldType, newType));

            service.SetInDepot(10, depotTrackId: 7);

            Assert.That(events, Is.Empty);
            Assert.That(service.GetInDepot().Select(r => r.vehicleId), Is.EqualTo(new[] { 10 }));
            Assert.That(service.Get(10).stationId, Is.EqualTo(1001));
            Assert.That(service.Get(10).depotTrackId, Is.EqualTo(7));

            service.SetOnRoute(10, trainRunId: 55, worldPos: new Vector2(12f, 34f));

            Assert.That(events.Count, Is.EqualTo(1));
            Assert.That(events[0], Is.EqualTo((10, VehicleLocationType.InDepot, VehicleLocationType.OnRoute)));
            Assert.That(service.GetInDepot(), Is.Empty);
            Assert.That(service.GetOnRoute().Select(r => r.vehicleId), Is.EqualTo(new[] { 10 }));
            Assert.That(service.Get(10).currentTrainRunId, Is.EqualTo(55));
            Assert.That(service.Get(10).worldMapPosition, Is.EqualTo(new Vector2(12f, 34f)));

            service.UpdateRoutePosition(10, new Vector2(99f, 101f));

            Assert.That(events.Count, Is.EqualTo(1), "Position-only updates should not emit location change events.");
            Assert.That(service.Get(10).worldMapPosition, Is.EqualTo(new Vector2(99f, 101f)));

            service.SetAtStation(10, stationId: 2002, worldPos: new Vector2(1f, 2f));

            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events[1], Is.EqualTo((10, VehicleLocationType.OnRoute, VehicleLocationType.AtStation)));
            Assert.That(service.GetOnRoute(), Is.Empty);
            Assert.That(service.GetAtStation(2002).Select(r => r.vehicleId), Is.EqualTo(new[] { 10 }));
        }

        [Test]
        public void Snapshot_IsDetachedAndRestoreRebuildsTypeIndexes()
        {
            var service = CreateService();
            service.SetOnRoute(11, trainRunId: 77, worldPos: new Vector2(3f, 4f));
            service.SetAtStation(12, stationId: 3003, worldPos: new Vector2(5f, 6f));

            var snapshot = service.GetSnapshot();
            snapshot[0].type = VehicleLocationType.InTransit;
            snapshot[0].worldMapPosition = new Vector2(-1f, -1f);

            Assert.That(service.Get(11).type, Is.EqualTo(VehicleLocationType.OnRoute));
            Assert.That(service.Get(11).worldMapPosition, Is.EqualTo(new Vector2(3f, 4f)));

            service.RestoreSnapshot(new[]
            {
                null,
                new VehicleLocationRecord { vehicleId = -1, type = VehicleLocationType.InDepot },
                new VehicleLocationRecord { vehicleId = 21, type = VehicleLocationType.OnRoute, currentTrainRunId = 88, worldMapPosition = new Vector2(9f, 10f) },
                new VehicleLocationRecord { vehicleId = 22, type = VehicleLocationType.AtStation, stationId = 4004, worldMapPosition = new Vector2(11f, 12f) }
            });

            Assert.That(service.AllRecords.Select(r => r.vehicleId).OrderBy(id => id), Is.EqualTo(new[] { 21, 22 }));
            Assert.That(service.GetOnRoute().Select(r => r.vehicleId), Is.EqualTo(new[] { 21 }));
            Assert.That(service.GetAtStation(4004).Select(r => r.vehicleId), Is.EqualTo(new[] { 22 }));
            Assert.That(service.GetInDepot(), Is.Empty);
        }

        [Test]
        public void ResetAll_ClearsRecordsAndIndexes()
        {
            var service = CreateService();
            service.SetInDepot(31, depotTrackId: 1);
            service.SetOnRoute(32, trainRunId: 9, worldPos: Vector2.one);

            VehicleLocationService.ResetAll();

            Assert.That(service.AllRecords, Is.Empty);
            Assert.That(service.GetInDepot(), Is.Empty);
            Assert.That(service.GetOnRoute(), Is.Empty);
        }

        private static VehicleLocationService CreateService()
        {
            var go = new GameObject("VehicleLocationServiceTests");
            var service = go.AddComponent<VehicleLocationService>();
            typeof(VehicleLocationService)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(service, null);
            return service;
        }

        private static void DestroyExistingService()
        {
            foreach (var service in Resources.FindObjectsOfTypeAll<VehicleLocationService>())
                Object.DestroyImmediate(service.gameObject);
        }
    }
}
