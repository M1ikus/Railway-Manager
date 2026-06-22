using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Dispatch Faza 2: testy decyzji TRZYMAJ/PUŚĆ (PredictiveDispatchDecider). Czyste,
    /// na recznie zbudowanych prognozach — bez sceny/symulatora. To NOWA warstwa (predykcyjna,
    /// przy odjezdzie); reaktywna warstwa blokowa (HasHigherPriorityWaiting, OvertakePriorityTests)
    /// pozostaje bez zmian.
    /// </summary>
    public class DispatchDecisionTests
    {
        const float MaxHold = 300f; // s
        const int B = 99;           // wspolny blok

        [Test]
        public void HigherValueRivalWouldBeBlocked_Holds()
        {
            // me (prio 3) wjechalby na 99 w [0,100]; rywal (prio 5) chce 99 w [10,40].
            // me wchodzi pierwszy i blokuje rywala -> oplaca sie ustapic (holdCost 3*40 < releaseCost 5*90).
            var me = FC(1, 3, (B, 0, 100));
            var rival = FC(2, 5, (B, 10, 40));

            var d = PredictiveDispatchDecider.Decide(me, new[] { rival }, nowSec: 0, maxExtraHoldSec: MaxHold);

            Assert.That(d.hold, Is.True, "Osobowy ustepuje wyzej-wazonemu posp. ktorego by zablokowal.");
            Assert.That(d.rivalTrainRunId, Is.EqualTo(2));
            Assert.That(d.conflictBlockKey, Is.EqualTo(B));
        }

        [Test]
        public void LowerOrEqualValueRival_DoesNotHold()
        {
            // me (prio 5) vs rywal (prio 3) — rywal nizszy -> nie trzymamy.
            var me = FC(1, 5, (B, 0, 100));
            var rival = FC(2, 3, (B, 10, 40));

            Assert.That(PredictiveDispatchDecider.Decide(me, new[] { rival }, 0, MaxHold).hold, Is.False);
        }

        [Test]
        public void HoldTooLong_Releases()
        {
            // Rywal (prio 5) zwalnia blok bardzo pozno (exit 100000) -> myHold ~ 100000 > MaxHold -> pusc.
            var me = FC(1, 3, (B, 0, 100));
            var rival = FC(2, 5, (B, 10, 100000));

            Assert.That(PredictiveDispatchDecider.Decide(me, new[] { rival }, 0, MaxHold).hold, Is.False,
                "Powyzej twardego limitu trzymania -> wypuszczamy (anti-starvation).");
        }

        [Test]
        public void HoldNotWorthIt_Releases()
        {
            // me wysoko-wazony (prio 5), rywal tylko nieco wyzej (prio 6), ale me blokuje rywala
            // ledwo (rivalDelay maly), a sam czekalby dlugo (myHold duzy) -> holdCost >= releaseCost.
            // me [0,12] (exit 12 -> rivalDelay 2), rywal [10,400] (myHold 400). maxHold duzy by izolowac koszt.
            var me = FC(1, 5, (B, 0, 12));
            var rival = FC(2, 6, (B, 10, 400));

            Assert.That(PredictiveDispatchDecider.Decide(me, new[] { rival }, 0, maxExtraHoldSec: 100000f).hold, Is.False,
                "Gdy trzymanie kosztuje wiecej niz zysk rywala -> wypuszczamy mimo nizszego priorytetu.");
        }

        [Test]
        public void NoBlockOverlap_Releases()
        {
            // Rozlaczne czasy na wspolnym bloku -> brak konfliktu -> pusc.
            var me = FC(1, 3, (B, 0, 30));
            var rival = FC(2, 5, (B, 40, 80));

            Assert.That(PredictiveDispatchDecider.Decide(me, new[] { rival }, 0, MaxHold).hold, Is.False);
        }

        [Test]
        public void RivalArrivesFirst_DoesNotPredictivelyHold()
        {
            // Rywal wjezdza wczesniej niz me (enterRival 0 < enterMe 5) -> me i tak nie wejdzie
            // (reaktywna warstwa zablokuje); predykcyjnie nie trzymamy (warunek me.enter <= rival.enter falszywy).
            var me = FC(1, 3, (B, 5, 100));
            var rival = FC(2, 5, (B, 0, 50));

            Assert.That(PredictiveDispatchDecider.Decide(me, new[] { rival }, 0, MaxHold).hold, Is.False);
        }

        [Test]
        public void CascadeBehindMe_FlipsHoldToRelease()
        {
            // Baza (me prio3 vs rywal prio5) sama trzymalaby (patrz HigherValueRivalWouldBeBlocked_Holds).
            // Ale za mna na tym samym bloku stoi Q prio10 — trzymanie mnie opoznia takze Q,
            // wiec efektywna waga me rosnie (3+10) -> oplaca sie PUSCIC.
            var me = FC(1, 3, (B, 0, 100));
            var rival = FC(2, 5, (B, 10, 40));
            var q = FC(3, 10, (B, 50, 80)); // wjezdza na 99 po mnie -> czeka za mna

            var d = PredictiveDispatchDecider.Decide(me, new[] { rival, q }, nowSec: 0, maxExtraHoldSec: MaxHold);

            Assert.That(d.hold, Is.False,
                "Wysoko-wazona kolejka za mna (kaskada) przewaza na 'pusc' mimo wyzszego priorytetu rywala.");
        }

        static TrainForecast FC(int id, int prio, params (int blockKey, double enter, double exit)[] res)
        {
            var list = new List<BlockReservation>();
            foreach (var r in res) list.Add(new BlockReservation(r.blockKey, r.enter, r.exit));
            return new TrainForecast(id, prio, list);
        }
    }
}
