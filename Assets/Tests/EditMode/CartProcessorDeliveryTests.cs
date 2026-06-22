using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M9c-D F1: testy CartProcessor pod kątem zmian pipeline dostawy — weryfikują że zakup
    /// ustawia pojazdowi poprawny status startowy, ETA (sama produkcja, bez wliczania dostawy)
    /// oraz externalLocation (punkt zakupu → DeliveryLocator). Czysta logika, EditMode.
    ///
    /// Pokrywa regresję: wcześniej używany szedł InTransit + ETA=now+5dni dostawy, nowy
    /// InProduction + ETA=now+30dni+5dni. Po F1: dostawę wybiera gracz dopiero w punkcie zakupu,
    /// więc używany = AwaitingPickup (ETA=now), nowy = InProduction (ETA=now+produkcja).
    ///
    /// Nowy tabor: po usunięciu legacy NewVehicleModel flow (M-UIPolish 2026-06-18) idzie
    /// przez vehicleConfiguration → testy ładują FleetCatalog i używają pierwszej rodziny.
    /// </summary>
    public class CartProcessorDeliveryTests
    {
        readonly List<int> _spawnedIds = new();

        [SetUp]
        public void SetUp()
        {
            if (!FleetCatalog.IsLoaded) FleetCatalog.LoadAll();
            GameState.GameTimeSeconds = 0f;
            GameState.GameDay = 0;
            // Czysty stan floty — zapamiętaj istniejące id, usuwaj tylko swoje w TearDown.
            _spawnedIds.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (int id in _spawnedIds)
                FleetService.RemoveOwnedVehicle(id);
            _spawnedIds.Clear();
        }

        /// <summary>Łapie id pojazdów dodanych przez ProcessOrder (delta na OwnedVehicles).</summary>
        void TrackNewVehicles(System.Func<int> processCall)
        {
            var before = new HashSet<int>();
            foreach (var v in FleetService.OwnedVehicles) before.Add(v.id);
            processCall();
            foreach (var v in FleetService.OwnedVehicles)
                if (!before.Contains(v.id)) _spawnedIds.Add(v.id);
        }

        static FleetMarketVehicle MakeMarketVehicle(string location)
        {
            return new FleetMarketVehicle
            {
                seriesId = "EU07", series = "EU07-TEST", number = "EU07-9999",
                type = FleetVehicleType.ElectricLocomotive,
                location = location,
                supportedTractions = new List<TractionType> { TractionType.Electric },
                conditionPercent = 70, mileageKm = 1_000_000, productionYear = 1975,
                // Ustaw resolved paint z góry — omija MarketLiveryGenerator (katalogi) w teście.
                paintDefinitionResolved = new PaintDefinition()
            };
        }

        /// <summary>
        /// Nowy tabor przez vehicleConfiguration (pierwsza rodzina z katalogu + jej pierwszy wariant).
        /// Zastąpiło legacy NewVehicleModel-based CartItem po M-UIPolish 2026-06-18.
        /// </summary>
        static CartItem MakeNewVehicleItem(FleetFamily fam, CartDeliveryMode mode)
        {
            var variant = fam.variants[0];
            return new CartItem
            {
                isNewVehicle = true,
                vehicleConfiguration = new VehicleConfiguration
                {
                    familyId = fam.familyId,
                    variantKey = $"{fam.familyId}|{variant.memberCount}|{variant.voltageConfigId}"
                },
                quantity = 1,
                deliveryMode = mode,
                unitPrice = 20_000_000
            };
        }

        FleetVehicleData FindSpawned() =>
            _spawnedIds.Count > 0 ? FleetService.GetOwnedById(_spawnedIds[_spawnedIds.Count - 1]) : null;

        [Test]
        public void UsedVehicle_PurchasedAsAwaitingPickup_WithLocationAndImmediateEta()
        {
            long now = (long)GameState.GameTimeSeconds;
            var item = new CartItem
            {
                isNewVehicle = false,
                marketVehicle = MakeMarketVehicle("Krakow Plaszow"),
                deliveryMode = CartDeliveryMode.SelfPickup,
                unitPrice = 600000
            };

            TrackNewVehicles(() => CartProcessor.ProcessOrder(new List<CartItem> { item }));
            var v = FindSpawned();

            Assert.That(v, Is.Not.Null, "ProcessOrder powinno dodać pojazd.");
            Assert.That(v.status, Is.EqualTo(FleetVehicleStatus.AwaitingPickup),
                "Używany pojazd po zakupie czeka na odbiór (dostawę wybiera gracz w punkcie zakupu).");
            Assert.That(v.position.externalLocation, Is.EqualTo("Krakow Plaszow"),
                "externalLocation = miejsce zakupu (punkt materializacji na mapie).");
            Assert.That(v.estimatedCompletionGameTime, Is.EqualTo(now),
                "Używany jest od razu gotowy — ETA = teraz (bez wliczania dostawy).");
        }

        [Test]
        public void NewVehicle_PurchasedAsInProduction_WithFactoryLocationAndProductionEta()
        {
            long now = (long)GameState.GameTimeSeconds;
            Assert.That(FleetCatalog.Families.Count, Is.GreaterThan(0), "Katalog rodzin musi być załadowany.");
            var fam = FleetCatalog.Families[0];
            // deliveryMode (DeliverToDepot) jest ignorowane dla statusu/ETA po F1.
            var item = MakeNewVehicleItem(fam, CartDeliveryMode.DeliverToDepot);

            TrackNewVehicles(() => CartProcessor.ProcessOrder(new List<CartItem> { item }));
            var v = FindSpawned();

            Assert.That(v, Is.Not.Null);
            Assert.That(v.status, Is.EqualTo(FleetVehicleStatus.InProduction),
                "Nowy pojazd zaczyna w produkcji.");
            Assert.That(v.position.externalLocation, Is.EqualTo(fam.factoryLocation),
                "externalLocation = fabryka rodziny (punkt materializacji po produkcji).");
            Assert.That(v.estimatedCompletionGameTime, Is.GreaterThan(now),
                "ETA = teraz + czas produkcji (dostawa to osobny krok po F1, nie doliczana).");
        }

        [Test]
        public void UsedVehicle_Eta_UsesAbsoluteGameTime_NotWithinDay()
        {
            // Crash-hunt (krytyczny): ETA musi być w czasie ABSOLUTNYM (GameDay*86400+secs),
            // nie gołym GameTimeSeconds (wewnątrz-dobowym). Inaczej multi-dobowe timery nigdy
            // nie dojrzewają. Używany pojazd: estimatedCompletionGameTime = teraz (bez produkcji).
            GameState.GameDay = 5;
            GameState.GameTimeSeconds = 100f;
            long expectedAbsolute = GameState.TotalGameSeconds; // 5*86400 + 100 = 432100

            var item = new CartItem
            {
                isNewVehicle = false, marketVehicle = MakeMarketVehicle("Gdansk"),
                deliveryMode = CartDeliveryMode.SelfPickup, unitPrice = 1
            };
            TrackNewVehicles(() => CartProcessor.ProcessOrder(new List<CartItem> { item }));
            var v = FindSpawned();

            Assert.That(v.estimatedCompletionGameTime, Is.EqualTo(expectedAbsolute),
                "ETA = absolutny czas gry (432100), nie goły within-day (100) — inaczej multi-doba broken.");
        }

        [Test]
        public void NewVehicle_DeliveryModeDoesNotAffectEta()
        {
            // F1 regresja: deliveryMode (SelfPickup vs DeliverToDepot) NIE może zmieniać ETA —
            // dostawa jest osobnym krokiem wybieranym po materializacji. Oba warianty = ten sam ETA.
            Assert.That(FleetCatalog.Families.Count, Is.GreaterThan(0), "Katalog rodzin musi być załadowany.");
            var fam = FleetCatalog.Families[0];

            TrackNewVehicles(() => CartProcessor.ProcessOrder(new List<CartItem> {
                MakeNewVehicleItem(fam, CartDeliveryMode.SelfPickup) }));
            long etaSelf = FindSpawned().estimatedCompletionGameTime;

            TrackNewVehicles(() => CartProcessor.ProcessOrder(new List<CartItem> {
                MakeNewVehicleItem(fam, CartDeliveryMode.DeliverToDepot) }));
            long etaDepot = FindSpawned().estimatedCompletionGameTime;

            Assert.That(etaDepot, Is.EqualTo(etaSelf),
                "deliveryMode nie może wpływać na ETA produkcji (F1: dostawa osobno).");
        }
    }
}
