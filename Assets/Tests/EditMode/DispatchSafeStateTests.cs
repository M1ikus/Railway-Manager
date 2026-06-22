using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Dispatch Faza 3a: testy anty-deadlock (DispatchSafeState) + dowod, ze decider
    /// odrzuca HOLD, ktory domknalby cykl wait-for. Czyste, na recznych prognozach.
    /// </summary>
    public class DispatchSafeStateTests
    {
        const int B = 99;

        [Test]
        public void TwoTrains_HoldIsAlwaysSafe()
        {
            // me wjezdza pierwszy na 99, rywal czeka. Trzymanie me (przepuszczenie rywala)
            // nie moze zakleszczyc — rywal nie ma INNEJ drogi z powrotem do me.
            var me = FC(1, 3, (B, 0, 100));
            var rival = FC(2, 5, (B, 10, 40));

            Assert.That(DispatchSafeState.HoldWouldDeadlock(1, 2, new[] { me, rival }), Is.False);
        }

        [Test]
        public void ThreeTrainCycle_HoldWouldDeadlock()
        {
            // Cykl: me wjezdza pierwszy na 99 (rywal->me), rywal czeka na C (200), C czeka na me (300).
            // Trzymanie me dla rywala domyka me->rywal->C->me -> deadlock.
            var me = FC(1, 3, (B, 0, 100), (300, 0, 40));
            var rival = FC(2, 5, (B, 10, 50), (200, 10, 50));
            var c = FC(3, 3, (200, 5, 30), (300, 5, 30));

            Assert.That(DispatchSafeState.HoldWouldDeadlock(1, 2, new[] { me, rival, c }), Is.True,
                "Trzymanie me dla rywala domyka cykl wait-for przez C -> zakleszczenie.");
        }

        [Test]
        public void NoSharedBlock_NotDeadlock()
        {
            var me = FC(1, 3, (B, 0, 100));
            var rival = FC(2, 5, (88, 0, 100)); // inny blok, brak relacji

            Assert.That(DispatchSafeState.HoldWouldDeadlock(1, 2, new[] { me, rival }), Is.False);
        }

        [Test]
        public void Decider_ReleasesInsteadOfHolding_WhenHoldWouldDeadlock()
        {
            // Ten sam uklad cyklu. Koszt sam w sobie mowilby "trzymaj" (rywal prio 5 > me 3,
            // me blokuje rywala oplacalnie), ale anty-deadlock wymusza PUSC (rozbij korek).
            var me = FC(1, 3, (B, 0, 100), (300, 0, 40));
            var rival = FC(2, 5, (B, 10, 50), (200, 10, 50));
            var c = FC(3, 3, (200, 5, 30), (300, 5, 30));

            var d = PredictiveDispatchDecider.Decide(me, new[] { rival, c }, nowSec: 0, maxExtraHoldSec: 100000f);

            Assert.That(d.hold, Is.False,
                "Mimo ze koszt sam zachecalby do trzymania, deadlock-guard wymusza wypuszczenie.");
        }

        static TrainForecast FC(int id, int prio, params (int blockKey, double enter, double exit)[] res)
        {
            var list = new List<BlockReservation>();
            foreach (var r in res) list.Add(new BlockReservation(r.blockKey, r.enter, r.exit));
            return new TrainForecast(id, prio, list);
        }
    }
}
