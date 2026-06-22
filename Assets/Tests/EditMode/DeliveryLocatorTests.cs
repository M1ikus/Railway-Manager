using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M9c-D F0: testy DeliveryLocator — rozwiązywanie nazwy lokalizacji zakupu → pathNodeId.
    /// EditMode pokrywa ścieżki BEZ grafu (TimetableInitializer.Instance == null): kontrakt
    /// fallbacków + sentinel -1 + konwencja exact flag. Dopasowanie po nazwie do realnej stacji
    /// wymaga grafu → PlayMode (DeliverySimulationTests.AwaitingPickup_Materializes...).
    /// </summary>
    public class DeliveryLocatorTests
    {
        int _homeBackup;

        [SetUp]
        public void SetUp()
        {
            _homeBackup = GameState.HomeDepotStationId;
            // Gwarancja braku grafu — DeliveryLocator ma fallbackować, nie dopasowywać po nazwie.
            DestroyInitializer();
        }

        [TearDown]
        public void TearDown()
        {
            GameState.HomeDepotStationId = _homeBackup;
            DestroyInitializer();
        }

        static void DestroyInitializer()
        {
            foreach (var i in Resources.FindObjectsOfTypeAll<TimetableInitializer>())
                Object.DestroyImmediate(i.gameObject);
        }

        [Test]
        public void ResolveHome_NoHomeSet_ReturnsInvalid()
        {
            GameState.HomeDepotStationId = -1;

            var r = DeliveryLocator.ResolveHome();

            Assert.That(r.IsValid, Is.False);
            Assert.That(r.nodeId, Is.EqualTo(-1));
        }

        [Test]
        public void ResolveHome_HomeSetButNoGraph_ReturnsNodeWithInexactPosition()
        {
            GameState.HomeDepotStationId = 4242;

            var r = DeliveryLocator.ResolveHome();

            Assert.That(r.IsValid, Is.True, "Home node jest ustawiony → IsValid true mimo braku grafu.");
            Assert.That(r.nodeId, Is.EqualTo(4242));
            Assert.That(r.exact, Is.False, "Bez grafu pozycja nieznana → exact=false.");
            Assert.That(r.position, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ResolvePurchaseLocation_NoGraphNoHome_ReturnsInvalid()
        {
            GameState.HomeDepotStationId = -1;

            var r = DeliveryLocator.ResolvePurchaseLocation("Bydgoszcz");

            Assert.That(r.IsValid, Is.False, "Brak grafu i brak home → nie da się rozwiązać.");
            Assert.That(r.nodeId, Is.EqualTo(-1));
        }

        [Test]
        public void ResolvePurchaseLocation_NoGraphButHomeSet_FallsBackToHomeInexact()
        {
            GameState.HomeDepotStationId = 777;

            var r = DeliveryLocator.ResolvePurchaseLocation("Bydgoszcz");

            Assert.That(r.IsValid, Is.True);
            Assert.That(r.nodeId, Is.EqualTo(777), "Bez grafu fallback do home node.");
            Assert.That(r.exact, Is.False, "Fallback do home (nie dopasowanie po nazwie) → exact=false.");
        }

        [Test]
        public void ResolvePurchaseLocation_NullOrEmptyName_FallsBackToHome()
        {
            GameState.HomeDepotStationId = 555;

            var rNull = DeliveryLocator.ResolvePurchaseLocation(null);
            var rEmpty = DeliveryLocator.ResolvePurchaseLocation("");

            Assert.That(rNull.nodeId, Is.EqualTo(555));
            Assert.That(rEmpty.nodeId, Is.EqualTo(555));
            Assert.That(rNull.exact, Is.False);
        }
    }
}
