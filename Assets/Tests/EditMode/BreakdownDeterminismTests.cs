using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;
using RailwayManager.Core.GameRules;
using RailwayManager.Fleet;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// MP-9 determinizm na poziomie INTEGRACJI (BreakdownServiceTests testuje sciezki bez RNG;
    /// CoreTimeAndRandomTests testuje sam RandomRegistry). Tu: BreakdownService.CheckForBreakdown
    /// przez przechwycony static-readonly s_rng = RandomRegistry.GetRng("BreakdownService").
    ///
    /// Kontrakt MP-9 (fundament pod M10 MP + save/load): ten sam GameState.Seed -> ta sama
    /// sekwencja awarii; RandomRegistry.ToJson/ApplyFromJson (czyli snapshot w save'ie)
    /// odtwarza stan in-place przez przechwycona referencje -> kontynuacja identyczna.
    ///
    /// Setup: 1 pojazd, engineCondition=0 (failureThreshold=1.0), pozostale komponenty -1
    /// (pomijane guardem >=0) -> dokladnie 1 roll s_rng na wywolanie. deltaSec dobrane
    /// runtime'owo tak, by P(awaria)~0.3 niezaleznie od BaseRate/difficulty multiplier.
    /// </summary>
    public class BreakdownDeterminismTests
    {
        const int Vid = 880100;
        const float TargetProb = 0.3f;
        int _seedBackup;
        float _deltaSec;

        [SetUp]
        public void SetUp()
        {
            _seedBackup = GameState.Seed;
            GameRulesService.ResetToDefault();   // VehicleBreakdowns ON (default)
            DifficultyService.ResetToDefault();  // BreakdownChanceMultiplier znany (>0)

            Assert.That(GameRulesService.IsEnabled(GameRule.VehicleBreakdowns), Is.True,
                "Pre-warunek: awarie wlaczone.");
            float diff = DifficultyService.Modifiers.BreakdownChanceMultiplier;
            Assert.That(diff, Is.GreaterThan(0f), "Pre-warunek: difficulty multiplier > 0.");

            // health=0 -> failureThreshold^2 = 1; risk(engine)=1 -> P = BaseRate*diff*deltaSec.
            _deltaSec = TargetProb / (FleetBalanceConstants.BreakdownBaseRatePerSecond * diff);

            FleetService.RemoveOwnedVehicle(Vid);
            FleetService.AddOwnedVehicle(new FleetVehicleData
            {
                id = Vid,
                components = new VehicleComponents
                {
                    engineCondition = 0f,
                    // reszta -1 => guard `>= 0f` pomija => tylko engine rolluje (1 draw/call)
                    brakeCondition = -1f, doorsCondition = -1f, acCondition = -1f,
                    bodyCondition = -1f, wheelsCondition = -1f, electricalCondition = -1f,
                    interiorCondition = -1f, lightsCondition = -1f, toiletsCondition = -1f,
                    pantographCondition = -1f, couplingCondition = -1f
                },
                componentRisk = new ComponentRiskFactors() // engine=1.0 default
            });
        }

        [TearDown]
        public void TearDown()
        {
            FleetService.RemoveOwnedVehicle(Vid);
            GameState.Seed = _seedBackup;
            RandomRegistry.ResetAll();
        }

        List<bool> RunSequence(int seed, int n)
        {
            GameState.Seed = seed;
            RandomRegistry.ResetAll();
            return Advance(n);
        }

        List<bool> Advance(int n)
        {
            var ids = new List<int> { Vid };
            var result = new List<bool>(n);
            for (int i = 0; i < n; i++)
                result.Add(BreakdownService.CheckForBreakdown(ids, _deltaSec).HasValue);
            return result;
        }

        [Test]
        public void SameSeed_ProducesIdenticalBreakdownSequence()
        {
            var a = RunSequence(777, 300);
            var b = RunSequence(777, 300);

            Assert.That(b, Is.EqualTo(a), "Ten sam seed -> identyczna sekwencja awarii (MP-9).");
            // Signal: sekwencja nietrywialna (sa i awarie, i brak-awarii).
            Assert.That(a, Has.Some.EqualTo(true), "Test bez sygnalu: zadna awaria nie odpalila.");
            Assert.That(a, Has.Some.EqualTo(false), "Test bez sygnalu: kazde wywolanie odpalilo awarie.");
        }

        [Test]
        public void DifferentSeed_ProducesDifferentSequence()
        {
            var a = RunSequence(777, 300);
            var c = RunSequence(99999, 300);
            // 300 niezaleznych bitow ~p=0.3 pod dwoma seedami; kolizja praktycznie niemozliwa.
            Assert.That(c, Is.Not.EqualTo(a), "Rozny seed -> rozna sekwencja (seed faktycznie steruje RNG).");
        }

        [Test]
        public void RngSnapshot_SurvivesSaveLoadRoundTrip()
        {
            GameState.Seed = 777;
            RandomRegistry.ResetAll();

            Advance(50);                              // postep symulacji przed "save"
            var snapshot = RandomRegistry.ToJson();   // <- to wlasnie persistuje save (WorldSavable RNG)
            var afterSave = Advance(100);

            RandomRegistry.ApplyFromJson(snapshot);   // <- "load": restore stanu RNG in-place
            var afterLoad = Advance(100);

            Assert.That(afterLoad, Is.EqualTo(afterSave),
                "Snapshot RNG (save) -> ApplyFromJson (load) odtwarza stan przez przechwycony s_rng " +
                "-> kontynuacja awarii identyczna (MP-9 save/load determinism).");
        }
    }
}
