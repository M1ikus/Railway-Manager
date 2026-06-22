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
    /// M9c-D: testy logiki pipeline dostawy taboru (EditMode, bez Play mode / sceny).
    ///
    /// Pokrywają przejścia stanów dostawy które NIE wymagają grafu/ruchu 3D:
    /// produkcja → odbiór, dostawa ekspresowa (koszt / pobranie kasy / InTransit /
    /// dostarczenie fallbackiem do depot), guardy (brak gotówki, zły status,
    /// wagon pasywny bez loco). Materializacja na mapie, scheduled-run i fetch
    /// wymagają TimetableInitializer.Graph + DepotMovementSimulator → weryfikacja
    /// w Play mode (smoke). Logika jest w publicznych metodach DeliveryService
    /// właśnie po to, by dało się ją unit-testować tutaj.
    /// </summary>
    public class DeliveryPipelineTests
    {
        const int Vid = 999001;

        long _moneyBackup;
        int _homeBackup;
        float _gameTimeBackup;

        DeliveryService _service;

        [SetUp]
        public void SetUp()
        {
            _moneyBackup = GameState.Money;
            _homeBackup = GameState.HomeDepotStationId;
            _gameTimeBackup = GameState.GameTimeSeconds;

            GameState.Money = 10_000_000;
            GameState.HomeDepotStationId = 1;   // fake home node → DeliveryLocator fallback (dystans zastępczy)
            GameState.GameTimeSeconds = 0f;

            DestroyExisting<VehicleLocationService>();
            DestroyExisting<DeliveryService>();

            CreateService<VehicleLocationService>(); // singleton authority lokalizacji
            VehicleLocationService.ResetAll();
            _service = CreateService<DeliveryService>();

            FleetService.RemoveOwnedVehicle(Vid);
        }

        [TearDown]
        public void TearDown()
        {
            FleetService.RemoveOwnedVehicle(Vid);
            VehicleLocationService.ResetAll();

            DestroyExisting<DeliveryService>();
            DestroyExisting<VehicleLocationService>();

            GameState.Money = _moneyBackup;
            GameState.HomeDepotStationId = _homeBackup;
            GameState.GameTimeSeconds = _gameTimeBackup;
        }

        static FleetVehicleData NewVehicle(FleetVehicleStatus status, string purchaseLocation = "Bydgoszcz")
        {
            var v = new FleetVehicleData
            {
                id = Vid,
                series = "TEST-EZT",
                type = FleetVehicleType.EMU,
                status = status,
                estimatedCompletionGameTime = 0,
                supportedTractions = new List<TractionType> { TractionType.Electric },
                position = new VehiclePosition { kind = VehicleLocationKind.None, externalLocation = purchaseLocation }
            };
            FleetService.AddOwnedVehicle(v);
            return v;
        }

        [Test]
        public void Production_CompletesToAwaitingPickup()
        {
            var v = NewVehicle(FleetVehicleStatus.InProduction);
            v.estimatedCompletionGameTime = 0;

            _service.ProcessVehicle(v, nowSec: 100000f);

            Assert.That(v.status, Is.EqualTo(FleetVehicleStatus.AwaitingPickup));
        }

        [Test]
        public void ExpressCost_IsPositive()
        {
            var v = NewVehicle(FleetVehicleStatus.AwaitingPickup);
            Assert.That(_service.EstimateExpressCostZl(v), Is.GreaterThan(0));
        }

        [Test]
        public void ExpressDelivery_RejectedWhenNotEnoughMoney_AndStatusUnchanged()
        {
            var v = NewVehicle(FleetVehicleStatus.AwaitingPickup);
            GameState.Money = 0;

            bool ok = _service.RequestExpressDelivery(v);

            Assert.That(ok, Is.False);
            Assert.That(v.status, Is.EqualTo(FleetVehicleStatus.AwaitingPickup));
        }

        [Test]
        public void ExpressDelivery_ChargesExactCostAndSetsInTransit()
        {
            var v = NewVehicle(FleetVehicleStatus.AwaitingPickup);
            int cost = _service.EstimateExpressCostZl(v);
            long before = GameState.Money;

            bool ok = _service.RequestExpressDelivery(v);

            Assert.That(ok, Is.True);
            Assert.That(v.status, Is.EqualTo(FleetVehicleStatus.InTransit));
            Assert.That(GameState.Money, Is.EqualTo(before - cost));
        }

        [Test]
        public void ExpressDelivery_ArrivesToDepotViaFallbackWhenNoSimulator()
        {
            // Brak DepotMovementSimulator (headless) → TriggerDepotEntry idzie fallbackiem do InDepot.
            var v = NewVehicle(FleetVehicleStatus.AwaitingPickup);
            Assert.That(_service.RequestExpressDelivery(v), Is.True);

            v.estimatedCompletionGameTime = 0; // ETA minął
            _service.ProcessVehicle(v, nowSec: 100000f);

            Assert.That(v.status, Is.EqualTo(FleetVehicleStatus.StoppedInDepot));
            Assert.That(VehicleLocationService.Instance.Get(Vid)?.type,
                Is.EqualTo(VehicleLocationType.InDepot));
        }

        [Test]
        public void ExpressDelivery_RejectedWhenNotAwaitingPickup()
        {
            var v = NewVehicle(FleetVehicleStatus.StoppedInDepot);
            Assert.That(_service.RequestExpressDelivery(v), Is.False);
        }

        [Test]
        public void ScheduledDelivery_RejectedForPassiveWagon()
        {
            // Wagon pasywny (None) wymaga lokomotywy — RequestScheduledDelivery musi odmówić.
            var v = NewVehicle(FleetVehicleStatus.AwaitingPickup);
            v.supportedTractions = new List<TractionType> { TractionType.None };

            Assert.That(_service.RequestScheduledDelivery(v), Is.False);
        }

        [Test]
        public void Production_CompletesAcrossDayBoundary_AbsoluteTime()
        {
            // Crash-hunt (krytyczny): produkcja 30 dni MUSI się skończyć. estimatedCompletionGameTime
            // jest absolutne; ProcessVehicle porównuje z TotalGameSeconds (też absolutnym). Na STARYM
            // kodzie (within-day GameTimeSeconds, max 86400) target 2.6M nigdy nieosiągalny → produkcja
            // nigdy się nie kończy = cała ścieżka "kup nowy pojazd" zepsuta.
            int dayBackup = GameState.GameDay;
            try
            {
                GameState.GameDay = 0;
                GameState.GameTimeSeconds = 43200f; // dzień 0, południe
                var v = NewVehicle(FleetVehicleStatus.InProduction);
                v.estimatedCompletionGameTime = GameState.TotalGameSeconds + 30L * 24 * 3600; // +30 dni absolutnie

                // Ten sam dzień — produkcja jeszcze trwa.
                _service.ProcessVehicle(v, GameState.TotalGameSeconds);
                Assert.That(v.status, Is.EqualTo(FleetVehicleStatus.InProduction),
                    "Dzień 0 — produkcja jeszcze trwa.");

                // 31 dni później (dziesiątki rolloverów doby — GameTimeSeconds dawno zresetowane).
                GameState.GameDay = 31;
                GameState.GameTimeSeconds = 0f;
                _service.ProcessVehicle(v, GameState.TotalGameSeconds);
                Assert.That(v.status, Is.EqualTo(FleetVehicleStatus.AwaitingPickup),
                    "Po 30 dniach produkcja kończy się (czas absolutny — z within-day NIGDY by nie ukończył).");
            }
            finally { GameState.GameDay = dayBackup; }
        }

        [Test]
        public void ExpressEstimates_AreDeterministicWithFallbackDistance()
        {
            // Bez grafu ComputeDeliveryDistanceKm zwraca FallbackDistanceKm → koszt/czas deterministyczne.
            var v = NewVehicle(FleetVehicleStatus.AwaitingPickup);

            int expectedCost = DeliveryConstants.ExpressBaseCostZl
                + Mathf.RoundToInt(DeliveryConstants.FallbackDistanceKm * DeliveryConstants.ExpressCostPerKmZl);
            long expectedTime = System.Math.Max(DeliveryConstants.ExpressMinTimeSec,
                (long)(DeliveryConstants.FallbackDistanceKm * DeliveryConstants.ExpressTimeSecPerKm));

            Assert.That(_service.EstimateExpressCostZl(v), Is.EqualTo(expectedCost));
            Assert.That(_service.EstimateExpressTimeSec(v), Is.EqualTo(expectedTime));
        }

        [Test]
        public void Production_StaysInProductionBeforeEta()
        {
            var v = NewVehicle(FleetVehicleStatus.InProduction);
            v.estimatedCompletionGameTime = 100000; // ETA w przyszłości

            _service.ProcessVehicle(v, nowSec: 50000f);

            Assert.That(v.status, Is.EqualTo(FleetVehicleStatus.InProduction));
        }

        [Test]
        public void Recovery_MovingOnMapWithDeliveryInProgress_EntersDepot()
        {
            // F7: scheduled delivery run NIE jest persystowany. Po load pojazd zostaje MovingOnMap +
            // OnRoute + deliveryInProgress, ale run zniknął (IsActive=false) → recovery dowozi do depot.
            var v = NewVehicle(FleetVehicleStatus.MovingOnMap);
            v.deliveryInProgress = true;
            VehicleLocationService.Instance.SetOnRoute(Vid, trainRunId: 12345, worldPos: Vector2.zero);

            _service.ProcessVehicle(v, nowSec: 1000f);

            Assert.That(v.status, Is.EqualTo(FleetVehicleStatus.StoppedInDepot));
            Assert.That(v.deliveryInProgress, Is.False);
            Assert.That(VehicleLocationService.Instance.Get(Vid)?.type,
                Is.EqualTo(VehicleLocationType.InDepot));
        }

        [Test]
        public void Recovery_MovingOnMapWithoutFlag_NoChange()
        {
            // Pojazd normalnie w trasie (deliveryInProgress=false) → recovery NIE odpala (guard).
            var v = NewVehicle(FleetVehicleStatus.MovingOnMap);
            v.deliveryInProgress = false;
            VehicleLocationService.Instance.SetOnRoute(Vid, trainRunId: 12345, worldPos: Vector2.zero);

            _service.ProcessVehicle(v, nowSec: 1000f);

            Assert.That(v.status, Is.EqualTo(FleetVehicleStatus.MovingOnMap));
        }

        [Test]
        public void DealerWagonDelivery_RejectedWhenNotEnoughMoney()
        {
            var v = NewVehicle(FleetVehicleStatus.AwaitingPickup);
            v.supportedTractions = new List<TractionType> { TractionType.None }; // wagon pasywny
            GameState.Money = 0;

            Assert.That(_service.RequestDealerWagonDelivery(v), Is.False);
            Assert.That(v.status, Is.EqualTo(FleetVehicleStatus.AwaitingPickup));
        }

        [Test]
        public void DealerWagonDelivery_PurchaseEqualsHome_EntersDepotAndCharges()
        {
            // Headless: brak grafu → ResolvePurchaseLocation fallbackuje do home, więc
            // purchase.nodeId == home.nodeId → BuildAndSpawnDeliveryRun idzie ścieżką
            // "wagon już w stacji home" → TriggerDepotEntry (fallback InDepot bez symulatora)
            // + pobranie opłaty (usługa wykonana). To realna ścieżka gdy gracz kupuje wagon
            // w mieście gdzie ma depot. Money-leak na spawn-fail (purchase != home, pathfinding
            // fail) jest weryfikowalny tylko z grafem → PlayMode.
            var v = NewVehicle(FleetVehicleStatus.AwaitingPickup);
            v.supportedTractions = new List<TractionType> { TractionType.None }; // wagon pasywny
            int cost = _service.EstimateExpressCostZl(v);
            long before = GameState.Money;

            bool ok = _service.RequestDealerWagonDelivery(v);

            Assert.That(ok, Is.True);
            Assert.That(v.status, Is.EqualTo(FleetVehicleStatus.StoppedInDepot));
            Assert.That(GameState.Money, Is.EqualTo(before - cost),
                "Po wykonanej dostawie producenta opłata powinna być pobrana dokładnie raz.");
        }

        [Test]
        public void TryParkInitialDepotFleet_NoOpWithoutSimulator()
        {
            // Brak DepotMovementSimulator (headless) → graceful no-op, bez wyjątku, bez materializacji.
            NewVehicle(FleetVehicleStatus.StoppedInDepot);

            Assert.DoesNotThrow(() => _service.TryParkInitialDepotFleet());
            Assert.That(VehicleLocationService.Instance.Get(Vid), Is.Null,
                "Bez symulatora pojazd nie zostaje zmaterializowany w zajezdni.");
        }

        // ── Helpers ──────────────────────────────────────────────────

        static T CreateService<T>() where T : MonoBehaviour
        {
            var go = new GameObject(typeof(T).Name + "_Test");
            var comp = go.AddComponent<T>();
            // Wymuś Awake (Unity nie woła go synchronicznie przy AddComponent w edit mode dla
            // niektórych ścieżek) — ustawia Instance + rejestruje hooki/subskrypcje.
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
