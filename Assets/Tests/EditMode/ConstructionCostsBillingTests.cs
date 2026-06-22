using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Economy;
using DepotSystem;
using DepotSystem.OutdoorEquipment;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Economy Faza 5a: kalkulatory kosztów budowy (ConstructionCosts) + rozliczanie
    /// (ConstructionBilling — TryCharge „nie stać → nie buduj", Refund pełny zwrot). EditMode.
    /// </summary>
    public class ConstructionCostsBillingTests
    {
        long _moneyBackup;

        [SetUp]
        public void SetUp()
        {
            _moneyBackup = GameState.Money;
            MoneyLedger.ResetAll();
            GameState.Money = 1000; // zł
        }

        [TearDown]
        public void TearDown()
        {
            MoneyLedger.ResetAll();
            GameState.Money = _moneyBackup;
        }

        // ── Kalkulatory (czyste) ─────────────────────────────────────

        [Test]
        public void TrackCost_PerKm()
        {
            // TrackZajezdnia 5 mln zł/km = 500_000_000 gr/km. 5 km → 2.5 mld gr.
            Assert.That(ConstructionCosts.TrackGroszy(5000f), Is.EqualTo(2_500_000_000L));
            Assert.That(ConstructionCosts.TrackGroszy(0f), Is.EqualTo(0L));
        }

        [Test]
        public void CatenaryCost_PerKm()
        {
            // 1 mln zł/km = 100_000_000 gr/km. 2 km → 200 mln gr.
            Assert.That(ConstructionCosts.CatenaryGroszy(2000f), Is.EqualTo(200_000_000L));
        }

        [Test]
        public void TurnoutCost_ByTypeName()
        {
            Assert.That(ConstructionCosts.TurnoutGroszy("R190"), Is.EqualTo(ConstructionConstants.RozjazdR190Groszy));
            Assert.That(ConstructionCosts.TurnoutGroszy("R300 1:9"), Is.EqualTo(ConstructionConstants.RozjazdR300Groszy));
            Assert.That(ConstructionCosts.TurnoutGroszy("R500"), Is.EqualTo(ConstructionConstants.RozjazdR500Groszy));
            Assert.That(ConstructionCosts.TurnoutGroszy("R760"), Is.EqualTo(ConstructionConstants.RozjazdR760Groszy));
            Assert.That(ConstructionCosts.TurnoutGroszy(null),   Is.EqualTo(ConstructionConstants.RozjazdR190Groszy), "Domyślnie R190.");
        }

        [Test]
        public void TurnoutCost_Crossover_MapsToKrzyzownica()
        {
            // TD-035: krzyżownica = KrzyzownicaPodwojnaGroszy (900M gr), NIE fallback R190 z "190".
            Assert.That(ConstructionCosts.TurnoutGroszy("Krzyżowy R190"),
                Is.EqualTo(ConstructionConstants.KrzyzownicaPodwojnaGroszy), "DefName z TurnoutData.Crossover_R190.");
            Assert.That(ConstructionCosts.TurnoutGroszy("Crossover_R190"),
                Is.EqualTo(ConstructionConstants.KrzyzownicaPodwojnaGroszy), "Schema-string z SchemaTurnoutType.");
        }

        [Test]
        public void RoomCost_PerSqMByType()
        {
            // Office 5 tys zł/m² = 500_000 gr. 100 m² → 50 mln gr.
            Assert.That(ConstructionCosts.RoomGroszy(RoomType.Office, 100f), Is.EqualTo(50_000_000L));
            // Hall 4 tys/m² = 400_000 gr. 552 m² → 220.8 mln gr.
            Assert.That(ConstructionCosts.RoomGroszy(RoomType.Hall, 552f), Is.EqualTo(220_800_000L));
            Assert.That(ConstructionCosts.RoomGroszy(RoomType.None, 100f), Is.EqualTo(0L), "None = darmowy.");
        }

        [Test]
        public void OutdoorCost_ByType()
        {
            Assert.That(ConstructionCosts.OutdoorGroszy(OutdoorEquipmentType.FuelStation), Is.EqualTo(ConstructionConstants.StacjaPaliwGroszy));
            Assert.That(ConstructionCosts.OutdoorGroszy(OutdoorEquipmentType.Turntable),   Is.EqualTo(ConstructionConstants.TurntableGroszy));
            Assert.That(ConstructionCosts.OutdoorGroszy(OutdoorEquipmentType.PitLift),     Is.EqualTo(ConstructionConstants.PitLiftGroszy));
        }

        [Test]
        public void FurnitureCost_FlatPlaceholder()
        {
            Assert.That(ConstructionCosts.FurnitureGroszy(), Is.EqualTo(ConstructionConstants.FurnitureItemPlaceholderGroszy));
        }

        // ── Billing ──────────────────────────────────────────────────

        [Test]
        public void TryCharge_Affordable_ChargesAndTracks()
        {
            bool ok = ConstructionBilling.TryCharge(50000, "construction_track", "5 km"); // 500 zł
            Assert.That(ok, Is.True);
            Assert.That(GameState.Money, Is.EqualTo(500), "Money − 500 zł.");
            Assert.That(MoneyLedger.CostsTodayGroszy, Is.EqualTo(50000L), "Budowa w bilansie dziennym.");
        }

        [Test]
        public void TryCharge_Insufficient_BlocksNoCharge()
        {
            bool ok = ConstructionBilling.TryCharge(200000, "construction_track", "duży tor"); // 2000 zł > 1000
            Assert.That(ok, Is.False, "Nie stać → false (caller anuluje budowę).");
            Assert.That(GameState.Money, Is.EqualTo(1000), "Money bez zmian (nie pobrano).");
        }

        [Test]
        public void TryCharge_ZeroCost_NoOpTrue()
        {
            bool ok = ConstructionBilling.TryCharge(0, "x", "None");
            Assert.That(ok, Is.True, "Koszt 0 nie blokuje.");
            Assert.That(GameState.Money, Is.EqualTo(1000));
        }

        [Test]
        public void Refund_EarnsBack()
        {
            ConstructionBilling.Refund(10000, "construction_track_refund", "undo"); // +100 zł
            Assert.That(GameState.Money, Is.EqualTo(1100));
        }
    }
}
