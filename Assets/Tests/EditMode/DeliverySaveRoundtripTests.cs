using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Fleet;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M9c-D F7: testy persystencji pól dostawy. FleetSavable serializuje przez JsonConvert
    /// (JArray.FromObject(OwnedVehicles) → ToObject), VehicleLocationService przez Get/RestoreSnapshot.
    /// Weryfikuje że flagi/pola dodane w pipeline dostawy PRZEŻYWAJĄ round-trip JSON:
    /// FleetVehicleData (status, deliveryInProgress, estimatedCompletionGameTime, position.externalLocation)
    /// + VehicleLocationRecord (stany handshake: Exiting/Entering/InTransit + consistId/trainRunId).
    /// </summary>
    public class DeliverySaveRoundtripTests
    {
        // ── FleetVehicleData JSON round-trip (jak FleetSavable) ──────

        static FleetVehicleData RoundTrip(FleetVehicleData v)
        {
            // Dokładnie ścieżka FleetSavable: JArray.FromObject → ToObject<FleetVehicleData>.
            var jo = JObject.FromObject(v);
            return jo.ToObject<FleetVehicleData>();
        }

        [Test]
        public void FleetVehicle_DeliveryFields_SurviveRoundtrip()
        {
            var v = new FleetVehicleData
            {
                id = 12345,
                series = "FLIRT",
                type = FleetVehicleType.EMU,
                status = FleetVehicleStatus.InTransit,
                deliveryInProgress = true,
                estimatedCompletionGameTime = 9_999_999,
                position = new VehiclePosition
                {
                    kind = VehicleLocationKind.None,
                    externalLocation = "Bydgoszcz"
                }
            };

            var r = RoundTrip(v);

            Assert.That(r.id, Is.EqualTo(12345));
            Assert.That(r.status, Is.EqualTo(FleetVehicleStatus.InTransit), "Status dostawy przeżywa.");
            Assert.That(r.deliveryInProgress, Is.True, "deliveryInProgress (F7 recovery flag) przeżywa.");
            Assert.That(r.estimatedCompletionGameTime, Is.EqualTo(9_999_999), "ETA przeżywa.");
            Assert.That(r.position, Is.Not.Null);
            Assert.That(r.position.externalLocation, Is.EqualTo("Bydgoszcz"),
                "externalLocation (punkt zakupu — kluczowy dla materializacji po load) przeżywa.");
            Assert.That(r.position.kind, Is.EqualTo(VehicleLocationKind.None));
        }

        [Test]
        public void FleetVehicle_AwaitingPickup_SurvivesRoundtrip()
        {
            var v = new FleetVehicleData
            {
                id = 1, status = FleetVehicleStatus.AwaitingPickup,
                position = new VehiclePosition { externalLocation = "Krakow Plaszow" }
            };
            var r = RoundTrip(v);
            Assert.That(r.status, Is.EqualTo(FleetVehicleStatus.AwaitingPickup));
            Assert.That(r.position.externalLocation, Is.EqualTo("Krakow Plaszow"));
        }

        [Test]
        public void FleetVehicle_DefaultDeliveryFlags_RoundtripFalse()
        {
            // Pojazd nie-w-dostawie: deliveryInProgress=false musi przeżyć jako false (nie zgubić się).
            var v = new FleetVehicleData { id = 2, status = FleetVehicleStatus.StoppedInDepot };
            var r = RoundTrip(v);
            Assert.That(r.deliveryInProgress, Is.False);
            Assert.That(r.status, Is.EqualTo(FleetVehicleStatus.StoppedInDepot));
        }

        // ── VehicleLocationService snapshot round-trip ───────────────

        [Test]
        public void VehicleLocation_HandshakeStates_SurviveSnapshotRestore()
        {
            int homeBackup = GameState.HomeDepotStationId;
            GameState.HomeDepotStationId = 300;
            DestroyExistingService();
            var svc = CreateService();
            try
            {
                // Ustaw rozmaite stany handshake.
                svc.SetExitingDepot(10, consistId: 50, trainRunId: 7);
                svc.SetOnRoute(11, trainRunId: 8, worldPos: new Vector2(100f, 200f));
                svc.SetEnteringDepot(12, consistId: 60);
                svc.SetInTransit(13, new Vector2(5f, 6f));

                var snapshot = svc.GetSnapshot();

                // Symuluj load: wyczyść + restore.
                VehicleLocationService.ResetAll();
                Assert.That(svc.Get(10), Is.Null, "Po ResetAll brak rekordów.");

                svc.RestoreSnapshot(snapshot);

                Assert.That(svc.Get(10).type, Is.EqualTo(VehicleLocationType.ExitingDepot));
                Assert.That(svc.Get(10).currentConsistId, Is.EqualTo(50));
                Assert.That(svc.Get(10).currentTrainRunId, Is.EqualTo(7));
                Assert.That(svc.Get(11).type, Is.EqualTo(VehicleLocationType.OnRoute));
                Assert.That(svc.Get(11).worldMapPosition, Is.EqualTo(new Vector2(100f, 200f)));
                Assert.That(svc.Get(12).type, Is.EqualTo(VehicleLocationType.EnteringDepot));
                Assert.That(svc.Get(12).currentConsistId, Is.EqualTo(60));
                Assert.That(svc.Get(13).type, Is.EqualTo(VehicleLocationType.InTransit));
            }
            finally
            {
                DestroyExistingService();
                GameState.HomeDepotStationId = homeBackup;
            }
        }

        [Test]
        public void VehicleLocation_SnapshotRebuildsTypeIndex()
        {
            DestroyExistingService();
            var svc = CreateService();
            try
            {
                svc.SetOnRoute(20, trainRunId: 1, worldPos: Vector2.zero);
                svc.SetOnRoute(21, trainRunId: 2, worldPos: Vector2.zero);
                var snap = svc.GetSnapshot();

                VehicleLocationService.ResetAll();
                svc.RestoreSnapshot(snap);

                // Per-type index musi być przebudowany — GetOnRoute zwraca oba.
                int onRouteCount = 0;
                foreach (var _ in svc.GetByType(VehicleLocationType.OnRoute)) onRouteCount++;
                Assert.That(onRouteCount, Is.EqualTo(2), "RestoreSnapshot przebudowuje per-type index.");
            }
            finally { DestroyExistingService(); }
        }

        // ── Helpers ──────────────────────────────────────────────────

        static VehicleLocationService CreateService()
        {
            var go = new GameObject("VehicleLocationService_Test");
            var svc = go.AddComponent<VehicleLocationService>();
            typeof(VehicleLocationService).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(svc, null);
            return svc;
        }

        static void DestroyExistingService()
        {
            foreach (var s in Resources.FindObjectsOfTypeAll<VehicleLocationService>())
                Object.DestroyImmediate(s.gameObject);
        }
    }
}
