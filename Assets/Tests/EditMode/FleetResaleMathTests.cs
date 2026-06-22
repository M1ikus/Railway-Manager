using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Fleet;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// Wycena odsprzedaży pojazdu (przycisk SPRZEDAJ): cena zakupu z historii × kondycja × haircut.
    /// Pure logic — wzór jak <see cref="FleetFuelMathTests"/>.
    /// </summary>
    public class FleetResaleMathTests
    {
        static FleetVehicleData MakeVehicle(long purchaseCostZl, float conditionPercent,
            string recordType = null)
        {
            var v = new FleetVehicleData
            {
                id = 991001,
                number = "TST-1",
                conditionPercent = conditionPercent,
                history = new List<MaintenanceRecord>()
            };
            if (recordType != null)
            {
                v.history.Add(new MaintenanceRecord
                {
                    gameTimeSeconds = 0,
                    recordType = recordType,
                    description = "test",
                    cost = purchaseCostZl,
                    mileageAtRecord = 0
                });
            }
            return v;
        }

        [Test]
        public void PurchasePrice_ReadFromHistory()
        {
            var v = MakeVehicle(1_000_000, 80f, MaintenanceRecordTypes.PurchaseUsed);
            Assert.That(FleetResaleMath.PurchasePriceZl(v), Is.EqualTo(1_000_000));
        }

        [Test]
        public void Resale_ScalesWithConditionAndHaircut()
        {
            // 1 000 000 zł × 0.80 kondycji × 0.85 haircut = 680 000 zł
            var v = MakeVehicle(1_000_000, 80f, MaintenanceRecordTypes.PurchaseUsed);
            long expectedZl = (long)(1_000_000L * (80f / 100f) * FleetBalanceConstants.ResaleValueHaircut);
            Assert.That(FleetResaleMath.ResaleValueGroszy(v) / 100, Is.EqualTo(expectedZl).Within(2));
        }

        [Test]
        public void Resale_FullCondition_HaircutOnly()
        {
            // Nowy pojazd (kondycja 100) sprzedany od razu = strata = haircut (anty-arbitraż).
            var v = MakeVehicle(2_000_000, 100f, MaintenanceRecordTypes.PurchaseNew);
            long expectedZl = (long)(2_000_000L * FleetBalanceConstants.ResaleValueHaircut);
            Assert.That(FleetResaleMath.ResaleValueGroszy(v) / 100, Is.EqualTo(expectedZl).Within(2));
            Assert.That(FleetResaleMath.ResaleValueGroszy(v) / 100,
                Is.LessThan(2_000_000), "Sprzedaż nowego < cena zakupu — brak arbitrażu");
        }

        [Test]
        public void Resale_NoPurchaseRecord_Zero()
        {
            var v = MakeVehicle(0, 90f, recordType: null); // pusta historia
            Assert.That(FleetResaleMath.PurchasePriceZl(v), Is.EqualTo(0));
            Assert.That(FleetResaleMath.ResaleValueGroszy(v), Is.EqualTo(0));
        }

        [Test]
        public void Resale_FreeStartingVehicle_Zero()
        {
            // Tabor "od początku gry" (cost=0) — zero wartości odsprzedaży, brak drukarki pieniędzy.
            var v = MakeVehicle(0, 90f, MaintenanceRecordTypes.Purchase);
            Assert.That(FleetResaleMath.ResaleValueGroszy(v), Is.EqualTo(0));
        }
    }
}
