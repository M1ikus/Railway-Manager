using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.SaveLoad.Modules;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Dispatch Faza 4b: polityka dispatchera (DispatchPolicyService) + wplyw hold-bias na decidera.
    /// </summary>
    public class DispatchPolicyTests
    {
        const int B = 99;
        DispatchPolicy _backup;

        [SetUp] public void SetUp() => _backup = DispatchPolicyService.CurrentPolicy;
        [TearDown] public void TearDown() => DispatchPolicyService.CurrentPolicy = _backup;

        [Test]
        public void Service_HoldBias_PerPolicy()
        {
            DispatchPolicyService.CurrentPolicy = DispatchPolicy.Balanced;
            Assert.That(DispatchPolicyService.HoldingEnabled, Is.True);
            Assert.That(DispatchPolicyService.HoldBias, Is.EqualTo(1f).Within(0.001f));

            DispatchPolicyService.CurrentPolicy = DispatchPolicy.Punctuality;
            Assert.That(DispatchPolicyService.HoldBias,
                Is.EqualTo(TimetableTuningConstants.DispatchPunctualityHoldBias).Within(0.001f));

            DispatchPolicyService.CurrentPolicy = DispatchPolicy.Off;
            Assert.That(DispatchPolicyService.HoldingEnabled, Is.False, "Off wylacza predykcyjne trzymanie.");
        }

        [Test]
        public void Punctuality_HoldsWhereBalancedReleases()
        {
            // Skonstruowane tak, ze holdCost (240) jest TUZ powyzej releaseCost (200):
            // me waga 4 [99:(0,60)], rywal waga 5 [99:(20,60)] -> rivalDelay 40, myHold 60.
            var me = FC(1, 4, (B, 0, 60));
            var rival = FC(2, 5, (B, 20, 60));
            var others = new[] { rival };

            // Balanced (bias 1.0): 240 < 200 ? nie -> PUSC.
            Assert.That(PredictiveDispatchDecider.Decide(me, others, 0, 300f, holdBias: 1f).hold, Is.False,
                "Balanced: trzymanie nieoplacalne -> puszcza.");

            // Punktualnosc (bias 1.5): 240 < 300 ? tak -> TRZYMAJ.
            Assert.That(PredictiveDispatchDecider.Decide(me, others, 0, 300f, holdBias: 1.5f).hold, Is.True,
                "Punktualnosc: chetniej trzyma, by chronic punktualnosc wyzej-wazonego.");
        }

        [Test]
        public void Policy_SurvivesWorldSaveRoundTrip()
        {
            var backup = GameState.MapDispatchPolicy;
            try
            {
                var savable = new WorldSavable();
                // Snapshot biezacego GameState; podmieniamy tylko polityke -> Deserialize odtwarza
                // snapshot (brak netto zmiany reszty) z polityka = Punctuality.
                var jobj = savable.Serialize();
                jobj["dispatchPolicy"] = (int)DispatchPolicy.Punctuality;

                GameState.MapDispatchPolicy = DispatchPolicy.Off;
                savable.Deserialize(jobj, savable.SchemaVersion);

                Assert.That(GameState.MapDispatchPolicy, Is.EqualTo(DispatchPolicy.Punctuality),
                    "Polityka dispatchera przetrwala save->load (WorldSavable).");
            }
            finally { GameState.MapDispatchPolicy = backup; }
        }

        [Test]
        public void Policy_ResetsToBalanced_OnNewGame()
        {
            var backup = GameState.MapDispatchPolicy;
            try
            {
                GameState.MapDispatchPolicy = DispatchPolicy.Off;
                new WorldSavable().InitializeDefault();
                Assert.That(GameState.MapDispatchPolicy, Is.EqualTo(DispatchPolicy.Balanced),
                    "Nowa gra (InitializeDefault) -> polityka domyslna Balanced.");
            }
            finally { GameState.MapDispatchPolicy = backup; }
        }

        static TrainForecast FC(int id, int prio, params (int blockKey, double enter, double exit)[] res)
        {
            var list = new List<BlockReservation>();
            foreach (var r in res) list.Add(new BlockReservation(r.blockKey, r.enter, r.exit));
            return new TrainForecast(id, prio, list);
        }
    }
}
