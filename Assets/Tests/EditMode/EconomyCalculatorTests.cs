using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Fleet;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M6: testy kalkulatorów ekonomii — TicketSystem (ceny biletów) + CostCalculator (koszty
    /// operacyjne). Czysta logika finansowa, EditMode. Krytyczne dla balansu (M6.5).
    /// </summary>
    public class EconomyCalculatorTests
    {
        // ── TicketSystem ─────────────────────────────────────────────

        [Test]
        public void Ticket_NullCategory_ReturnsZero()
        {
            Assert.That(TicketSystem.CalculatePriceGroszy((CommercialCategory)null, 100f), Is.EqualTo(0));
        }

        [Test]
        public void Ticket_DistanceUnder1km_ReturnsZero()
        {
            var cat = new CommercialCategory { basePriceZl = 10f, pricePerKmZl = 1f };
            Assert.That(TicketSystem.CalculatePriceGroszy(cat, 0.5f), Is.EqualTo(0),
                "Dystans <1km za krótki na bilet → 0.");
        }

        [Test]
        public void Ticket_LegacyPricing_BasePlusPerKm()
        {
            // Brak tiers → legacy: (base + perKm*km) zł → gr. base=10, perKm=0.5, km=100 → 60 zł → 6000 gr.
            var cat = new CommercialCategory { basePriceZl = 10f, pricePerKmZl = 0.5f };
            Assert.That(TicketSystem.CalculatePriceGroszy(cat, 100f), Is.EqualTo(6000));
        }

        [Test]
        public void Ticket_TiersPricing_FlatTier()
        {
            // Tier 0-50km = 1500gr flat (perKmAbove=0). km=30 → 1500.
            var cat = new CommercialCategory { basePriceZl = 999f, pricePerKmZl = 999f }; // legacy ignorowane gdy tiers
            cat.pricingTiers.Add(new PricingTier { fromKm = 0, toKm = 50, priceGroszy = 1500, perKmAboveGroszy = 0 });
            Assert.That(TicketSystem.CalculatePriceGroszy(cat, 30f), Is.EqualTo(1500),
                "Tier ma priorytet nad legacy; flat price w zakresie.");
        }

        [Test]
        public void Ticket_TiersPricing_PerKmAbove()
        {
            // Tier 0-100km: base 2000gr + 50gr/km powyżej fromKm. km=40 → 2000 + 40*50 = 4000.
            var cat = new CommercialCategory();
            cat.pricingTiers.Add(new PricingTier { fromKm = 0, toKm = 100, priceGroszy = 2000, perKmAboveGroszy = 50 });
            Assert.That(TicketSystem.CalculatePriceGroszy(cat, 40f), Is.EqualTo(2000 + 40 * 50));
        }

        [Test]
        public void Ticket_TiersPricing_BeyondLastTier_UsesLast()
        {
            // km przekracza wszystkie tiers → fallback do ostatniego. Tier 0-50: 1000+10/km.
            var cat = new CommercialCategory();
            cat.pricingTiers.Add(new PricingTier { fromKm = 0, toKm = 50, priceGroszy = 1000, perKmAboveGroszy = 10 });
            // km=200 (>50): last tier, kmAbove = 200-0 = 200 → 1000 + 2000 = 3000.
            Assert.That(TicketSystem.CalculatePriceGroszy(cat, 200f), Is.EqualTo(1000 + 200 * 10));
        }

        // ── CostCalculator ───────────────────────────────────────────

        readonly List<int> _ids = new();
        int _nextId = 930000;

        [TearDown]
        public void TearDown()
        {
            foreach (int id in _ids) FleetService.RemoveOwnedVehicle(id);
            _ids.Clear();
        }

        FleetVehicleData Add(FleetVehicleType type, int opCostPerKm)
        {
            var v = new FleetVehicleData { id = _nextId++, type = type, operationalCostPerKmGroszy = opCostPerKm };
            FleetService.AddOwnedVehicle(v);
            _ids.Add(v.id);
            return v;
        }

        [Test]
        public void Cost_PrefersCatalogValue()
        {
            // operationalCostPerKmGroszy > 0 → użyj wprost (nie heurystyka).
            var v = Add(FleetVehicleType.EMU, opCostPerKm: 777);
            Assert.That(CostCalculator.GetVehicleOperationalCostPerKm(v), Is.EqualTo(777));
        }

        [Test]
        public void Cost_FallbackHeuristic_WhenNoCatalogValue()
        {
            // opCost=0 → fallback heurystyka. EMU = Electric(250)+TrackAccess. Sprawdzamy że > 0
            // i że diesel > electric (więcej paliwa) — kontrakt relacji bez hardcode stałych.
            var emu = Add(FleetVehicleType.EMU, opCostPerKm: 0);
            var dmu = Add(FleetVehicleType.DMU, opCostPerKm: 0);
            int emuCost = CostCalculator.GetVehicleOperationalCostPerKm(emu);
            int dmuCost = CostCalculator.GetVehicleOperationalCostPerKm(dmu);

            Assert.That(emuCost, Is.GreaterThan(0));
            Assert.That(dmuCost, Is.GreaterThan(emuCost), "Diesel droższy w eksploatacji niż elektryk.");
        }

        [Test]
        public void Cost_PassiveCar_Cheapest()
        {
            var car = Add(FleetVehicleType.PassengerCar, opCostPerKm: 0);
            var emu = Add(FleetVehicleType.EMU, opCostPerKm: 0);
            Assert.That(CostCalculator.GetVehicleOperationalCostPerKm(car),
                Is.LessThan(CostCalculator.GetVehicleOperationalCostPerKm(emu)),
                "Wagon pasywny tańszy per km niż jednostka trakcyjna.");
        }

        [Test]
        public void Cost_NullVehicle_ReturnsZero()
        {
            Assert.That(CostCalculator.GetVehicleOperationalCostPerKm(null), Is.EqualTo(0));
        }

        [Test]
        public void Cost_ConsistSum_AddsAllVehicles()
        {
            var loco = Add(FleetVehicleType.ElectricLocomotive, opCostPerKm: 300);
            var c1 = Add(FleetVehicleType.PassengerCar, opCostPerKm: 50);
            var c2 = Add(FleetVehicleType.PassengerCar, opCostPerKm: 50);

            int sum = CostCalculator.GetConsistOperationalCostPerKm(new List<int> { loco.id, c1.id, c2.id });
            Assert.That(sum, Is.EqualTo(300 + 50 + 50), "Koszt składu = suma pojazdów.");
        }

        [Test]
        public void Cost_ConsistEmpty_ReturnsZero()
        {
            Assert.That(CostCalculator.GetConsistOperationalCostPerKm(new List<int>()), Is.EqualTo(0));
            Assert.That(CostCalculator.GetConsistOperationalCostPerKm(null), Is.EqualTo(0));
        }

        [Test]
        public void PlatformFee_MajorStation_HigherThanHalt()
        {
            var major = new RailwayManager.Timetable.RailwayStation { isMajorStation = true };
            var halt = new RailwayManager.Timetable.RailwayStation { isMajorStation = false };
            Assert.That(CostCalculator.GetPlatformFeeGroszy(major),
                Is.GreaterThan(CostCalculator.GetPlatformFeeGroszy(halt)),
                "Postój na dużej stacji droższy niż na przystanku (halt).");
        }

        [Test]
        public void PlatformFee_NullStation_ReturnsZero()
        {
            Assert.That(CostCalculator.GetPlatformFeeGroszy(null), Is.EqualTo(0));
        }

        [Test]
        public void PlatformFee_ImportanceTiers_MapToOiuRates()
        {
            // TD-036a: importance → tier (Premium 130 / KatI 50 / KatII 20 / KatIII 5 / halt 1 zł)
            var s = new RailwayManager.Timetable.RailwayStation { isMajorStation = true };
            Assert.That(CostCalculator.GetPlatformFeeGroszy(s, 11f), Is.EqualTo(EconomyConstants.PlatformFeePremiumGroszy), "W-wa Centralna class.");
            Assert.That(CostCalculator.GetPlatformFeeGroszy(s, 9f),  Is.EqualTo(EconomyConstants.PlatformFeeKategoriaIGroszy), "Wojewódzka.");
            Assert.That(CostCalculator.GetPlatformFeeGroszy(s, 6f),  Is.EqualTo(EconomyConstants.PlatformFeeKategoriaIIGroszy), "Średnie miasto.");
            Assert.That(CostCalculator.GetPlatformFeeGroszy(s, 3f),  Is.EqualTo(EconomyConstants.PlatformFeeKategoriaIIIGroszy), "Mała stacja.");
            Assert.That(CostCalculator.GetPlatformFeeGroszy(s, 1.3f), Is.EqualTo(EconomyConstants.PlatformFeeHaltGroszy), "Polny halt.");
        }

        [Test]
        public void PlatformFee_NoImportance_FallbackByMajorFlag()
        {
            // Przed zbudowaniem OD matrix (importance -1) → fallback isMajorStation.
            var major = new RailwayManager.Timetable.RailwayStation { isMajorStation = true };
            var halt = new RailwayManager.Timetable.RailwayStation { isMajorStation = false };
            Assert.That(CostCalculator.GetPlatformFeeGroszy(major), Is.EqualTo(EconomyConstants.PlatformFeeKategoriaIIIGroszy));
            Assert.That(CostCalculator.GetPlatformFeeGroszy(halt), Is.EqualTo(EconomyConstants.PlatformFeeHaltGroszy));
        }
    }
}
