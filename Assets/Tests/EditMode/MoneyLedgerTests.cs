using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Economy;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Economy: MoneyLedger (asmdef Economy) — jedyny ruchacz GameState.Money + sumy dzienne +
    /// per-kategoria + capital Spend/Earn/CanAfford. Static → reset w SetUp/TearDown. EditMode.
    /// </summary>
    public class MoneyLedgerTests
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

        [Test]
        public void AddRevenue_MoneyUp_Tracked()
        {
            MoneyLedger.AddRevenue(50000); // 500 zł
            Assert.That(GameState.Money, Is.EqualTo(1500));
            Assert.That(MoneyLedger.RevenueTodayGroszy, Is.EqualTo(50000L));
        }

        [Test]
        public void AddCost_Capital_NoMult_MoneyDown_Tracked_PerCategory()
        {
            long actual = MoneyLedger.AddCost(30000, "construction_track"); // 300 zł, NIE operacyjny
            Assert.That(actual, Is.EqualTo(30000L), "Capital bez difficulty mult.");
            Assert.That(GameState.Money, Is.EqualTo(700));
            Assert.That(MoneyLedger.CostsTodayGroszy, Is.EqualTo(30000L));
            Assert.That(MoneyLedger.CostsByCategory["construction_track"], Is.EqualTo(30000L),
                "Breakdown per-kategoria (FinancePanelUI drill-down).");
        }

        [Test]
        public void CanAfford_And_Spend_Capital()
        {
            Assert.That(MoneyLedger.CanAfford(100000), Is.True);   // 1000 zł
            Assert.That(MoneyLedger.CanAfford(100001), Is.False);  // o 1 gr za mało
            MoneyLedger.Spend(20000, "vehicle_purchase", "EN57");  // 200 zł
            Assert.That(GameState.Money, Is.EqualTo(800));
        }

        [Test]
        public void Earn_Refund_MoneyUp()
        {
            MoneyLedger.Earn(15000, "construction_refund", "undo"); // +150 zł
            Assert.That(GameState.Money, Is.EqualTo(1150));
        }

        [Test]
        public void LargeCapital_NoIntOverflow()
        {
            GameState.Money = 100_000_000; // 100 mln zł
            MoneyLedger.Spend(4_000_000_000L, "construction_track", "5km"); // 40 mln zł > int.MaxValue gr
            Assert.That(MoneyLedger.CostsTodayGroszy, Is.EqualTo(4_000_000_000L));
            Assert.That(GameState.Money, Is.EqualTo(60_000_000));
        }

        [Test]
        public void ResetDayTotals_ClearsRunning_KeepsMoney()
        {
            MoneyLedger.AddCost(10000, "x");
            long moneyAfter = GameState.Money;
            MoneyLedger.ResetDayTotals();
            Assert.That(MoneyLedger.CostsTodayGroszy, Is.EqualTo(0L));
            Assert.That(GameState.Money, Is.EqualTo(moneyAfter), "Reset sum dziennych nie rusza Money.");
        }
    }
}
