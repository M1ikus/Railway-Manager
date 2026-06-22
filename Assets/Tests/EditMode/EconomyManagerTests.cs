using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;
using RailwayManager.Timetable.Economy;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M6: testy EconomyManager — bilans dzienny (revenue/cost/subsidy), per-line balance,
    /// archiwizacja na koniec dnia, difficulty scaling kosztów operacyjnych. EditMode.
    /// Tworzony AddComponent (OnEnable subskrybuje eventy — czyszczone przez DestroyImmediate→OnDisable).
    /// Mutuje GameState.Money (akumulator) — backup/restore.
    /// </summary>
    public class EconomyManagerTests
    {
        long _moneyBackup;
        DifficultyPreset _diffBackup;
        EconomyManager _em;

        [SetUp]
        public void SetUp()
        {
            _moneyBackup = GameState.Money;
            _diffBackup = DifficultyService.Preset;
            DifficultyService.ApplyNewGameConfig(DifficultyPreset.Normal); // mnożniki 1.0
            GameState.Money = 1_000_000;

            DestroyExisting();
            // GO aktywny → Unity woła OnEnable (subskrypcja eventów). Awake lekki (Instance=this).
            _em = new GameObject("EconomyManager_Test").AddComponent<EconomyManager>();
            // M-Economy: sumy dzienne są teraz STATIC w MoneyLedger — świeża instancja ich NIE zeruje.
            // ResetRuntime() (→ MoneyLedger.ResetAll) przywraca izolację per-test.
            _em.ResetRuntime();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyExisting(); // OnDisable odsubskrybuje eventy
            DifficultyService.ApplyNewGameConfig(_diffBackup);
            GameState.Money = _moneyBackup;
        }

        static void DestroyExisting()
        {
            foreach (var e in Resources.FindObjectsOfTypeAll<EconomyManager>())
                Object.DestroyImmediate(e.gameObject);
        }

        // ── Agregacja dzienna ────────────────────────────────────────

        [Test]
        public void AddRevenue_AccumulatesAndAffectsNet()
        {
            _em.AddRevenue(circulationId: 1, amountGroszy: 50000, "ticket"); // 500 zł
            _em.AddRevenue(circulationId: 1, amountGroszy: 30000, "ticket"); // 300 zł

            Assert.That(_em.RevenueTodayGroszy, Is.EqualTo(80000));
            Assert.That(_em.NetTodayGroszy, Is.EqualTo(80000), "Net = revenue gdy brak kosztów.");
        }

        [Test]
        public void AddCost_AccumulatesAndReducesNet()
        {
            _em.AddRevenue(1, 100000, "ticket");
            _em.AddCost(1, 40000, "operational", "fuel");

            Assert.That(_em.CostsTodayGroszy, Is.EqualTo(40000));
            Assert.That(_em.NetTodayGroszy, Is.EqualTo(100000 - 40000), "Net = revenue - costs.");
        }

        [Test]
        public void AddSubsidy_IncreasesNet()
        {
            _em.AddCost(1, 50000, "operational", "fuel");
            _em.AddSubsidy(1, 20000, "regional");

            Assert.That(_em.SubsidiesTodayGroszy, Is.EqualTo(20000));
            Assert.That(_em.NetTodayGroszy, Is.EqualTo(0 - 50000 + 20000), "Net = rev - cost + subsidy.");
        }

        [Test]
        public void NonPositiveAmounts_NoOp()
        {
            _em.AddRevenue(1, 0, "x");
            _em.AddRevenue(1, -100, "x");
            _em.AddCost(1, 0, "operational", "x");
            _em.AddSubsidy(1, -50, "x");
            Assert.That(_em.RevenueTodayGroszy, Is.EqualTo(0));
            Assert.That(_em.CostsTodayGroszy, Is.EqualTo(0));
            Assert.That(_em.SubsidiesTodayGroszy, Is.EqualTo(0));
        }

        [Test]
        public void Revenue_IncreasesMoney_Cost_DecreasesMoney()
        {
            long before = GameState.Money;
            _em.AddRevenue(1, 500000, "ticket"); // 5000 zł — całe złote flushują do Money
            Assert.That(GameState.Money, Is.GreaterThan(before), "Revenue zwiększa Money.");

            long afterRevenue = GameState.Money;
            _em.AddCost(1, 200000, "operational", "fuel"); // 2000 zł
            Assert.That(GameState.Money, Is.LessThan(afterRevenue), "Cost zmniejsza Money.");
        }

        // ── Per-line balance ─────────────────────────────────────────

        [Test]
        public void LineBalance_TracksPerCirculation()
        {
            _em.AddRevenue(circulationId: 10, amountGroszy: 60000, "ticket");
            _em.AddCost(circulationId: 10, amountGroszy: 20000, "operational", "fuel");
            _em.AddRevenue(circulationId: 20, amountGroszy: 15000, "ticket");

            Assert.That(_em.LineBalances.ContainsKey(10), Is.True);
            Assert.That(_em.LineBalances[10].revenueGroszy, Is.EqualTo(60000));
            Assert.That(_em.LineBalances[10].costsGroszy, Is.EqualTo(20000));
            Assert.That(_em.LineBalances[10].passengerCount, Is.EqualTo(1), "1 AddRevenue = 1 pasażer.");
            Assert.That(_em.LineBalances[20].revenueGroszy, Is.EqualTo(15000), "Osobne obiegi rozdzielone.");
        }

        [Test]
        public void Revenue_NoCirculation_SkipsLineBalance()
        {
            _em.AddRevenue(circulationId: -1, amountGroszy: 10000, "misc");
            Assert.That(_em.RevenueTodayGroszy, Is.EqualTo(10000), "Globalny revenue rośnie.");
            Assert.That(_em.LineBalances.ContainsKey(-1), Is.False, "circulationId<0 → brak per-line.");
        }

        // ── Day-end archiwizacja ─────────────────────────────────────

        [Test]
        public void OnDayEnded_ArchivesAndResets()
        {
            _em.AddRevenue(1, 70000, "ticket");
            _em.AddCost(1, 30000, "operational", "fuel");
            int historyBefore = _em.History.Count;

            _em.OnDayEnded("2026-06-01");

            Assert.That(_em.History.Count, Is.EqualTo(historyBefore + 1), "Dzień zarchiwizowany do History.");
            Assert.That(_em.RevenueTodayGroszy, Is.EqualTo(0), "RevenueToday wyzerowany po dniu.");
            Assert.That(_em.CostsTodayGroszy, Is.EqualTo(0));
            Assert.That(_em.LineBalances.Count, Is.EqualTo(0), "Per-line balances wyczyszczone.");

            var archived = _em.History[_em.History.Count - 1];
            Assert.That(archived.dateIso, Is.EqualTo("2026-06-01"));
            Assert.That(archived.revenueGroszy, Is.EqualTo(70000), "Zarchiwizowany revenue zachowany.");
        }

        // ── Difficulty scaling ───────────────────────────────────────

        [Test]
        public void OperationalCost_ScaledByDifficulty_HarderCostsMore()
        {
            // Normal (1.0) baseline.
            _em.AddCost(1, 100000, "operational", "fuel");
            long normalCost = _em.CostsTodayGroszy;

            // Hard preset → OperationalCostMultiplier > 1.0.
            DestroyExisting();
            DifficultyService.ApplyNewGameConfig(DifficultyPreset.Hard);
            _em = new GameObject("EconomyManager_Test2").AddComponent<EconomyManager>();
            _em.ResetRuntime(); // static MoneyLedger — wyzeruj normalCost przed pomiarem hard
            _em.AddCost(1, 100000, "operational", "fuel");
            long hardCost = _em.CostsTodayGroszy;

            if (DifficultyService.Modifiers.OperationalCostMultiplier > 1.0f)
                Assert.That(hardCost, Is.GreaterThan(normalCost),
                    "Hard preset → koszt operacyjny wyższy niż Normal (OperationalCostMultiplier).");
            else
                Assert.That(hardCost, Is.EqualTo(normalCost), "Brak różnicy multiplikatora — koszt równy.");
        }

        [Test]
        public void NonOperationalCost_NotScaledByDifficulty()
        {
            // Kategoria "Personnel" NIE jest w OperationalCategories → nieskalowana nawet na Hard.
            DifficultyService.ApplyNewGameConfig(DifficultyPreset.Hard);
            DestroyExisting();
            _em = new GameObject("EconomyManager_Test3").AddComponent<EconomyManager>();
            _em.ResetRuntime(); // static MoneyLedger — czysty start

            _em.AddCost(1, 100000, "Personnel", "salary");
            Assert.That(_em.CostsTodayGroszy, Is.EqualTo(100000),
                "Personnel (poza whitelist operacyjną) nieskalowane przez difficulty — dokładna kwota.");
        }
    }
}
