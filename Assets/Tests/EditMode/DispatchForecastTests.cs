using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Dispatch Faza 1: testy forecast czasoprzestrzennego + detektora konfliktow.
    /// Czyste, deterministyczne (brak sceny/GameState). Detektor testowany wprost na
    /// recznie zbudowanych TrainForecast; forecast na SimulatedTrain budowanym przez
    /// GetUninitializedObject + reflection (pola blokow/speed-profile sa readonly).
    ///
    /// To Faza 1 — sam fundament (prognoza + wykrycie kolizji). Nie zmienia zachowania
    /// ruchu; decyzja trzymaj/pusc + anty-deadlock to Fazy 2-3.
    /// </summary>
    public class DispatchForecastTests
    {
        // ── BlockConflictDetector ────────────────────────────────────

        [Test]
        public void Detect_OverlappingReservationsOnSameBlock_OneConflict()
        {
            var a = Forecast(1, prio: 5, (99, 0, 60));
            var b = Forecast(2, prio: 3, (99, 30, 90));

            var conflicts = BlockConflictDetector.Detect(new[] { a, b });

            Assert.That(conflicts.Count, Is.EqualTo(1), "Nakladajace sie rezerwacje 99 -> 1 konflikt.");
            Assert.That(conflicts[0].blockKey, Is.EqualTo(99));
            Assert.That(conflicts[0].firstTrainRunId, Is.EqualTo(1), "Pierwszy wjezdza ten o wczesniejszym enterSec.");
            Assert.That(conflicts[0].secondTrainRunId, Is.EqualTo(2));
            Assert.That(conflicts[0].overlapStartSec, Is.EqualTo(30).Within(0.001));
            Assert.That(conflicts[0].overlapEndSec, Is.EqualTo(60).Within(0.001));
        }

        [Test]
        public void Detect_DisjointTimesOnSameBlock_NoConflict()
        {
            var a = Forecast(1, 5, (99, 0, 30));
            var b = Forecast(2, 3, (99, 40, 80)); // wjazd po wyjezdzie a

            Assert.That(BlockConflictDetector.Detect(new[] { a, b }).Count, Is.EqualTo(0));
        }

        [Test]
        public void Detect_DifferentBlocks_NoConflict()
        {
            var a = Forecast(1, 5, (10, 0, 60));
            var b = Forecast(2, 3, (20, 0, 60)); // ten sam czas, INNY blok

            Assert.That(BlockConflictDetector.Detect(new[] { a, b }).Count, Is.EqualTo(0));
        }

        [Test]
        public void Detect_IsDeterministic_StableOrder()
        {
            var a = Forecast(1, 5, (99, 0, 60));
            var b = Forecast(2, 3, (99, 30, 90));
            var first = BlockConflictDetector.Detect(new[] { a, b });
            var second = BlockConflictDetector.Detect(new[] { b, a }); // odwrocona kolejnosc wejscia

            Assert.That(second.Count, Is.EqualTo(first.Count));
            Assert.That(second[0].firstTrainRunId, Is.EqualTo(first[0].firstTrainRunId),
                "Wynik niezalezny od kolejnosci wejscia (determinizm).");
        }

        // ── TrainForecastService ─────────────────────────────────────

        [Test]
        public void Compute_FreeRun_BlockEnterExitTimes()
        {
            // 2 bloki [0,1000],[1000,2000], 20 m/s, brak postojow.
            var st = BuildTrain(1, pos: 0f, curBlockIdx: 0,
                blockKeys: new[] { 10, 11 },
                entry: new[] { 0f, 1000f }, exit: new[] { 1000f, 2000f },
                vmaxMps: 20f, stops: null, stopDist: null, depTimeOfDay: 0f);

            var f = TrainForecastService.Compute(st, priority: 5, nowSec: 0.0, horizonSec: 99999f);

            Assert.That(f.reservations.Count, Is.EqualTo(2));
            Assert.That(f.reservations[0].blockKey, Is.EqualTo(10));
            Assert.That(f.reservations[0].enterSec, Is.EqualTo(0).Within(0.01));
            Assert.That(f.reservations[0].exitSec, Is.EqualTo(50).Within(0.01), "1000m / 20 m/s = 50s.");
            Assert.That(f.reservations[1].enterSec, Is.EqualTo(50).Within(0.01), "Wjazd w blok 2 = wyjazd z bloku 1.");
            Assert.That(f.reservations[1].exitSec, Is.EqualTo(100).Within(0.01));
        }

        [Test]
        public void Compute_ScheduledHold_ExtendsBlockOccupancy()
        {
            // Blok [0,1000], 20 m/s, postoj na dyst 500 z planowym odjazdem t=200s.
            // Pociag dojezdza do stopu w t=25s, czeka do 200, jedzie dalej -> wyjazd 225s.
            // (Modeluje: osobowy okupuje blok peronowy do planowego odjazdu.)
            var stops = new List<TimetableStop> { new TimetableStop { plannedDepartureSec = 200 } };
            var st = BuildTrain(1, pos: 0f, curBlockIdx: 0,
                blockKeys: new[] { 7 },
                entry: new[] { 0f }, exit: new[] { 1000f },
                vmaxMps: 20f, stops: stops, stopDist: new[] { 500f }, depTimeOfDay: 0f);

            var f = TrainForecastService.Compute(st, priority: 3, nowSec: 0.0, horizonSec: 99999f);

            Assert.That(f.reservations.Count, Is.EqualTo(1));
            Assert.That(f.reservations[0].exitSec, Is.EqualTo(225).Within(0.01),
                "50s przejazdu + 175s trzymania do planowego odjazdu = 225s.");
        }

        [Test]
        public void Compute_LateTrain_DoesNotWaitAtStop()
        {
            // Ten sam postoj (planowy odjazd t=200), ale pociag startuje prognoze o nowSec=500
            // (juz spozniony) -> dojazd do stopu > planowy odjazd -> hold 0 -> wyjazd = 500+50.
            var stops = new List<TimetableStop> { new TimetableStop { plannedDepartureSec = 200 } };
            var st = BuildTrain(2, pos: 0f, curBlockIdx: 0,
                blockKeys: new[] { 7 },
                entry: new[] { 0f }, exit: new[] { 1000f },
                vmaxMps: 20f, stops: stops, stopDist: new[] { 500f }, depTimeOfDay: 0f);

            var f = TrainForecastService.Compute(st, priority: 5, nowSec: 500.0, horizonSec: 99999f);

            Assert.That(f.reservations[0].exitSec, Is.EqualTo(550).Within(0.01),
                "Spozniony pociag NIE czeka na planowy odjazd (hold 0) — odzwierciedla brak predykcyjnego trzymania spoznionego.");
        }

        // ── Helpers ──────────────────────────────────────────────────

        static TrainForecast Forecast(int id, int prio, params (int blockKey, double enter, double exit)[] res)
        {
            var list = new List<BlockReservation>();
            foreach (var r in res) list.Add(new BlockReservation(r.blockKey, r.enter, r.exit));
            return new TrainForecast(id, prio, list);
        }

        static SimulatedTrain BuildTrain(int id, float pos, int curBlockIdx, int[] blockKeys,
            float[] entry, float[] exit, float vmaxMps, List<TimetableStop> stops, float[] stopDist, float depTimeOfDay)
        {
            var st = (SimulatedTrain)FormatterServices.GetUninitializedObject(typeof(SimulatedTrain));
            SetField(st, "trainRun", new TrainRun { id = id, currentPositionOnRouteM = pos });
            SetField(st, "timetable", new TimetableObj { id = id, stops = stops ?? new List<TimetableStop>() });
            SetField(st, "currentBlockIndex", curBlockIdx);
            SetField(st, "routeBlockCount", blockKeys.Length);
            SetField(st, "routeBlockKeys", blockKeys);
            SetField(st, "blockEntryDistM", entry);
            SetField(st, "blockExitDistM", exit);
            SetField(st, "segmentEndDistM", new float[] { 1e9f });        // jeden segment -> staly Vmax
            SetField(st, "segmentMaxSpeedMps", new float[] { vmaxMps });
            SetField(st, "stopDistancesM", stopDist ?? new float[0]);
            SetField(st, "departureTimeOfDaySec", depTimeOfDay);
            return st;
        }

        static void SetField(object o, string name, object val)
        {
            var fi = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(fi, Is.Not.Null, $"Pole '{name}' powinno istniec na {o.GetType().Name}.");
            fi.SetValue(o, val);
        }
    }
}
