using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Fleet;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M11 AS-P1: realny model paliwa — defaulty per typ vs override z katalogu,
    /// spalanie per km (floor 0, no-op dla elektrycznych), zasięg (filtr W1 plannera AS-5),
    /// koszt tankowania = brakujące litry × cena. Plus integracja: spalanie w
    /// DegradationService.ApplyDegradation (per-km hook), NIEzależne od reguły decay.
    /// </summary>
    public class FleetFuelMathTests
    {
        static FleetVehicleData MakeVehicle(FleetVehicleType type, bool diesel,
            int tankOverride = 0, int consumptionOverride = 0)
        {
            return new FleetVehicleData
            {
                id = 987001,
                type = type,
                supportedTractions = new List<TractionType>
                {
                    diesel ? TractionType.Diesel : TractionType.Electric
                },
                fuelLevelPercent = 100f,
                fuelTankCapacityLitres = tankOverride,
                fuelConsumptionLper100km = consumptionOverride
            };
        }

        // ────────────── Defaulty / override ──────────────

        [Test]
        public void Defaults_PerType_WhenCatalogFieldsZero()
        {
            var dmu = MakeVehicle(FleetVehicleType.DMU, diesel: true);
            Assert.That(FleetFuelMath.EffectiveTankLitres(dmu),
                Is.EqualTo(FleetBalanceConstants.DefaultFuelTankLitresDmu));
            Assert.That(FleetFuelMath.EffectiveConsumptionLper100km(dmu),
                Is.EqualTo(FleetBalanceConstants.DefaultFuelConsumptionDmuLper100km));

            var loco = MakeVehicle(FleetVehicleType.DieselLocomotive, diesel: true);
            Assert.That(FleetFuelMath.EffectiveTankLitres(loco),
                Is.EqualTo(FleetBalanceConstants.DefaultFuelTankLitresDieselLoco));
            Assert.That(FleetFuelMath.EffectiveConsumptionLper100km(loco),
                Is.EqualTo(FleetBalanceConstants.DefaultFuelConsumptionDieselLocoLper100km));
        }

        [Test]
        public void CatalogOverride_WinsOverDefaults()
        {
            var sa = MakeVehicle(FleetVehicleType.DMU, diesel: true,
                tankOverride: 1800, consumptionOverride: 120);
            Assert.That(FleetFuelMath.EffectiveTankLitres(sa), Is.EqualTo(1800f));
            Assert.That(FleetFuelMath.EffectiveConsumptionLper100km(sa), Is.EqualTo(120f));
        }

        [Test]
        public void ElectricAndCars_NoFuelMath()
        {
            var emu = MakeVehicle(FleetVehicleType.EMU, diesel: false);
            Assert.That(FleetFuelMath.EffectiveTankLitres(emu), Is.EqualTo(0f));
            Assert.That(FleetFuelMath.PercentPerKm(emu), Is.EqualTo(0f));
            Assert.That(FleetFuelMath.RangeKmOnFullTank(emu), Is.EqualTo(0f));
            Assert.That(FleetFuelMath.RefuelCostGroszy(emu), Is.EqualTo(0));

            FleetFuelMath.ConsumeForDistance(emu, 500f);
            Assert.That(emu.fuelLevelPercent, Is.EqualTo(100f), "Elektryczny nie pali — no-op");
        }

        // ────────────── Spalanie / zasięg / koszt ──────────────

        [Test]
        public void Consume_MathAndFloor()
        {
            // tank 1200 l, 95 l/100km → 0.95 l/km → 0.0791(6)% per km; 100 km → ~7.92 pkt %.
            var dmu = MakeVehicle(FleetVehicleType.DMU, diesel: true,
                tankOverride: 1200, consumptionOverride: 95);

            FleetFuelMath.ConsumeForDistance(dmu, 100f);
            Assert.That(dmu.fuelLevelPercent, Is.EqualTo(100f - 95f / 1200f * 100f).Within(0.01f));

            FleetFuelMath.ConsumeForDistance(dmu, 1_000_000f);
            Assert.That(dmu.fuelLevelPercent, Is.EqualTo(0f), "Floor 0 — bez wartości ujemnych");
        }

        [Test]
        public void Range_LitresMissing_RefuelCost()
        {
            var dmu = MakeVehicle(FleetVehicleType.DMU, diesel: true,
                tankOverride: 1200, consumptionOverride: 95);

            Assert.That(FleetFuelMath.RangeKmOnFullTank(dmu), Is.EqualTo(1200f / 0.95f).Within(0.1f));

            dmu.fuelLevelPercent = 50f;
            Assert.That(FleetFuelMath.LitresMissing(dmu), Is.EqualTo(600f).Within(0.01f));
            Assert.That(FleetFuelMath.RefuelCostGroszy(dmu),
                Is.EqualTo(UnityEngine.Mathf.RoundToInt(600f * FleetBalanceConstants.FuelPricePerLitreGroszy)),
                "Koszt = brakujące litry × cena (zastąpiło proxy z długości pojazdu)");

            dmu.fuelLevelPercent = 100f;
            Assert.That(FleetFuelMath.RefuelCostGroszy(dmu), Is.EqualTo(0), "Pełny bak → tankowanie za darmo");
        }

        // ────────────── Integracja: per-km hook ──────────────

        [Test]
        public void DegradationHook_ConsumesFuelForDieselInConsist()
        {
            var dmu = MakeVehicle(FleetVehicleType.DMU, diesel: true,
                tankOverride: 1200, consumptionOverride: 95);
            FleetService.AddOwnedVehicle(dmu);
            try
            {
                DegradationService.ApplyDegradation(new List<int> { dmu.id }, 200f);
                Assert.That(dmu.fuelLevelPercent, Is.LessThan(100f),
                    "Per-km hook pali olej (pierwsze realne spalanie — wcześniej fuelLevelPercent był martwy)");
                Assert.That(dmu.fuelLevelPercent,
                    Is.EqualTo(100f - 2f * 95f / 1200f * 100f).Within(0.05f));
            }
            finally
            {
                FleetService.RemoveOwnedVehicle(dmu.id);
            }
        }
    }
}
