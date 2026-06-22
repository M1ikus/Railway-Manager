using System.Reflection;
using NUnit.Framework;
using RailwayManager.Core;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M9c-D: testy VehicleLocationService dla stanów HANDSHAKE/dostawy, których nie pokrywa
    /// VehicleLocationServiceTests (tam InDepot/OnRoute/AtStation + snapshot/reset). Tu:
    /// ExitingDepot (ożywiony w F6), EnteringDepot, InTransit — z poprawnym ustawianiem
    /// currentConsistId/currentTrainRunId i czyszczeniem pól przy zmianie typu.
    /// </summary>
    public class VehicleLocationHandshakeTests
    {
        int _homeBackup;

        [SetUp]
        public void SetUp()
        {
            _homeBackup = GameState.HomeDepotStationId;
            GameState.HomeDepotStationId = 500;
            DestroyExisting();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyExisting();
            GameState.HomeDepotStationId = _homeBackup;
        }

        [Test]
        public void SetExitingDepot_SetsConsistAndTrainRun()
        {
            var svc = CreateService();
            svc.SetInDepot(10, depotTrackId: 3);

            svc.SetExitingDepot(10, consistId: 77, trainRunId: 42);

            var rec = svc.Get(10);
            Assert.That(rec.type, Is.EqualTo(VehicleLocationType.ExitingDepot));
            Assert.That(rec.currentConsistId, Is.EqualTo(77));
            Assert.That(rec.currentTrainRunId, Is.EqualTo(42));
            // Per-type index zaktualizowany — pojazd już nie w InDepot.
            Assert.That(svc.GetInDepot(), Is.Empty);
            Assert.That(CountByType(svc, VehicleLocationType.ExitingDepot), Is.EqualTo(1));
        }

        [Test]
        public void SetEnteringDepot_SetsConsist_ClearsTrainRun()
        {
            var svc = CreateService();
            svc.SetOnRoute(11, trainRunId: 99, worldPos: Vector2.one);

            svc.SetEnteringDepot(11, consistId: 55);

            var rec = svc.Get(11);
            Assert.That(rec.type, Is.EqualTo(VehicleLocationType.EnteringDepot));
            Assert.That(rec.currentConsistId, Is.EqualTo(55));
            Assert.That(rec.currentTrainRunId, Is.EqualTo(-1),
                "EnteringDepot czyści currentTrainRunId (kurs zakończony, wjeżdża do depot).");
            Assert.That(svc.GetOnRoute(), Is.Empty);
        }

        [Test]
        public void SetInTransit_ClearsConsistAndTrainRun_KeepsPosition()
        {
            var svc = CreateService();
            svc.SetExitingDepot(12, consistId: 8, trainRunId: 3);

            var pos = new Vector2(123f, 456f);
            svc.SetInTransit(12, pos);

            var rec = svc.Get(12);
            Assert.That(rec.type, Is.EqualTo(VehicleLocationType.InTransit));
            Assert.That(rec.currentConsistId, Is.EqualTo(-1));
            Assert.That(rec.currentTrainRunId, Is.EqualTo(-1));
            Assert.That(rec.worldMapPosition, Is.EqualTo(pos));
        }

        [Test]
        public void FullHandshakeCycle_InDepot_Exiting_OnRoute_Entering_InDepot()
        {
            // Pełny cykl życia pojazdu w handshake — każde przejście emituje event + aktualizuje index.
            var svc = CreateService();
            int events = 0;
            svc.OnLocationChanged += (_, _, _) => events++;

            svc.SetInDepot(20, 1);                       // start: w zajezdni (InDepot→InDepot: brak emit)
            svc.SetExitingDepot(20, 30, 5);              // wyjazd
            svc.SetOnRoute(20, 5, Vector2.zero);         // na mapie
            svc.SetEnteringDepot(20, 31);                // powrót — wjazd
            svc.SetInDepot(20, 2);                       // zaparkowany

            Assert.That(svc.Get(20).type, Is.EqualTo(VehicleLocationType.InDepot));
            Assert.That(svc.Get(20).depotTrackId, Is.EqualTo(2));
            // 4 realne przejścia typu (InDepot start nie emituje bo GetOrCreate tworzy już jako InDepot).
            Assert.That(events, Is.EqualTo(4),
                "Każda zmiana TYPU emituje OnLocationChanged (Exiting/OnRoute/Entering/InDepot).");
        }

        [Test]
        public void SetInDepot_StampsHomeStationId()
        {
            var svc = CreateService();
            svc.SetInDepot(21, depotTrackId: 9);

            Assert.That(svc.Get(21).stationId, Is.EqualTo(500),
                "InDepot stempluje stationId = HomeDepotStationId (kontrakt VehicleLocationRecord).");
        }

        // ── Helpers ──────────────────────────────────────────────────

        static int CountByType(VehicleLocationService svc, VehicleLocationType type)
        {
            int n = 0;
            foreach (var _ in svc.GetByType(type)) n++;
            return n;
        }

        static VehicleLocationService CreateService()
        {
            var go = new GameObject("VehicleLocationService_Test");
            var svc = go.AddComponent<VehicleLocationService>();
            typeof(VehicleLocationService).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(svc, null);
            return svc;
        }

        static void DestroyExisting()
        {
            foreach (var s in Resources.FindObjectsOfTypeAll<VehicleLocationService>())
                Object.DestroyImmediate(s.gameObject);
        }
    }
}
