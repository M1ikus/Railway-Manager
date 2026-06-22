using NUnit.Framework;
using RailwayManager.Fleet;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M3/M7: testy MaintenanceCostCalculator — miesięczny koszt utrzymania. Czysta logika, EditMode.
    /// Testuje relacje + progi (cap age mult 1.5, floor condition mult 1.0), obie ścieżki
    /// (catalog-driven cost/km vs fallback per-type), guard age<0.
    /// </summary>
    public class MaintenanceCostCalculatorTests
    {
        const int Year = 2026;

        static FleetVehicleData Vehicle(FleetVehicleType type, int prodYear, float condition,
                                        int opCostPerKm = 0)
            => new FleetVehicleData
            {
                type = type, productionYear = prodYear, conditionPercent = condition,
                operationalCostPerKmGroszy = opCostPerKm
            };

        [Test]
        public void Cost_IsPositive()
        {
            var v = Vehicle(FleetVehicleType.EMU, Year, condition: 100f);
            Assert.That(MaintenanceCostCalculator.Calculate(v, Year), Is.GreaterThan(0));
        }

        [Test]
        public void Cost_OlderVehicle_MoreExpensive()
        {
            var newer = Vehicle(FleetVehicleType.EMU, Year, condition: 100f);
            var older = Vehicle(FleetVehicleType.EMU, Year - 20, condition: 100f);
            Assert.That(MaintenanceCostCalculator.Calculate(older, Year),
                Is.GreaterThan(MaintenanceCostCalculator.Calculate(newer, Year)),
                "Starszy pojazd droższy w utrzymaniu (age multiplier).");
        }

        [Test]
        public void Cost_AgeMultiplier_Capped()
        {
            // Bardzo stary (100 lat) nie może być droższy niż cap (1.5×). Porównaj z 50 lat
            // (już powyżej capa: 50×0.015=0.75 → +0.75 → ale cap 1.5). Oba na capie → równe.
            var veryOld = Vehicle(FleetVehicleType.EMU, Year - 100, condition: 100f);
            var atCap = Vehicle(FleetVehicleType.EMU, Year - 40, condition: 100f); // 40×0.015=0.6 → 1.6 > cap 1.5
            Assert.That(MaintenanceCostCalculator.Calculate(veryOld, Year),
                Is.EqualTo(MaintenanceCostCalculator.Calculate(atCap, Year)),
                "Age multiplier capped — 100 lat == 40 lat (oba na 1.5×).");
        }

        [Test]
        public void Cost_WorseCondition_MoreExpensive()
        {
            var good = Vehicle(FleetVehicleType.EMU, Year, condition: 100f);
            var bad = Vehicle(FleetVehicleType.EMU, Year, condition: 20f);
            Assert.That(MaintenanceCostCalculator.Calculate(bad, Year),
                Is.GreaterThan(MaintenanceCostCalculator.Calculate(good, Year)),
                "Gorszy stan → droższe utrzymanie (condition multiplier).");
        }

        [Test]
        public void Cost_PerfectCondition_FloorMultiplier()
        {
            // condition 100% → conditionMult = 2.0 - 1.0 = 1.0 (floor). Świeży EMU (age 0):
            // ageMult 1.0, condMult 1.0 → koszt == base EMU (12000).
            var v = Vehicle(FleetVehicleType.EMU, Year, condition: 100f);
            Assert.That(MaintenanceCostCalculator.Calculate(v, Year),
                Is.EqualTo(FleetBalanceConstants.MaintenanceMonthlyBaseEmu),
                "Świeży pojazd 100% kondycji → koszt = base (mult 1.0×1.0).");
        }

        [Test]
        public void Cost_CatalogDriven_UsesOperationalCost()
        {
            // opCostPerKm > 0 → catalog path: avgMileage(5000) × opCost / 100 → PLN.
            var v = Vehicle(FleetVehicleType.EMU, Year, condition: 100f, opCostPerKm: 600);
            long expected = (long)FleetBalanceConstants.MaintenanceAvgMonthlyMileageKm * 600 / 100;
            Assert.That(MaintenanceCostCalculator.Calculate(v, Year), Is.EqualTo(expected),
                "Catalog-driven: 5000km × 600gr/km / 100 = base (mult 1.0×1.0 dla świeżego 100%).");
        }

        [Test]
        public void Cost_FutureProductionYear_NoNegativeAge()
        {
            // productionYear > currentYear → age clamped do 0 (nie ujemny multiplier).
            var v = Vehicle(FleetVehicleType.EMU, Year + 5, condition: 100f);
            long cost = MaintenanceCostCalculator.Calculate(v, Year);
            Assert.That(cost, Is.EqualTo(FleetBalanceConstants.MaintenanceMonthlyBaseEmu),
                "Przyszły rok produkcji → age=0, koszt = base (nie ujemny mult).");
        }

        [Test]
        public void GetBaseCost_DiffersPerType()
        {
            // Różne typy mają różne bazowe koszty (loko ≠ wagon).
            long loco = MaintenanceCostCalculator.GetBaseCost(FleetVehicleType.ElectricLocomotive);
            long car = MaintenanceCostCalculator.GetBaseCost(FleetVehicleType.PassengerCar);
            Assert.That(loco, Is.GreaterThan(0));
            Assert.That(car, Is.GreaterThan(0));
            Assert.That(loco, Is.Not.EqualTo(car), "Loko i wagon mają różne bazowe koszty.");
        }

        [Test]
        public void Cost_CombinesAgeAndCondition()
        {
            // Stary + zniszczony znacznie droższy niż nowy + idealny (mnożniki się kumulują).
            var pristine = Vehicle(FleetVehicleType.EMU, Year, condition: 100f);
            var wreck = Vehicle(FleetVehicleType.EMU, Year - 40, condition: 10f);
            long pristineCost = MaintenanceCostCalculator.Calculate(pristine, Year);
            long wreckCost = MaintenanceCostCalculator.Calculate(wreck, Year);
            // wreck: ageMult 1.5 × condMult (2.0-0.1=1.9) = 2.85× base. pristine = 1.0× base.
            Assert.That(wreckCost, Is.GreaterThan(pristineCost * 2),
                "Stary+zniszczony >2× nowy+idealny (kumulacja age×condition).");
        }
    }
}
