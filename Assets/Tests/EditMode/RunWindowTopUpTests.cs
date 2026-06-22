using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RailwayManager.Timetable;
using TimetableData = RailwayManager.Timetable.Timetable; // klasa vs namespace (CS0118)

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-037 Etap F: rolling window — TopUpForCirculation dogenerowuje TYLKO brakujące pary
    /// (stepIndex, dateIso), BEZ Clear (ID istniejących runów nietknięte — CrewDuty linkuje po id).
    /// Obieg one-time (deterministyczna data, bez zależności od GameState).
    /// </summary>
    public class RunWindowTopUpTests
    {
        List<TrainRun> _backupRuns;
        List<TimetableData> _backupTimetables;

        [SetUp]
        public void SetUp()
        {
            _backupRuns = new List<TrainRun>(TimetableService.TrainRuns);
            _backupTimetables = new List<TimetableData>(TimetableService.Timetables);
            TimetableService.TrainRuns.Clear();
            TimetableService.Timetables.Add(new TimetableData
            {
                id = 9001,
                trainNumber = "TST 1",
                frequency = new FrequencySpec { firstRunMinutesFromMidnight = 480 },
            });
        }

        [TearDown]
        public void TearDown()
        {
            TimetableService.TrainRuns.Clear();
            TimetableService.TrainRuns.AddRange(_backupRuns);
            TimetableService.Timetables.Clear();
            TimetableService.Timetables.AddRange(_backupTimetables);
        }

        static Circulation MakeOneTime(string dateIso, int stepCount = 2)
        {
            var c = new Circulation
            {
                id = 901,
                name = "TopUp test",
                status = CirculationStatus.Active,
                isOneTime = true,
                oneTimeDateIso = dateIso,
            };
            for (int i = 0; i < stepCount; i++)
                c.steps.Add(new CirculationStep { timetableId = 9001 });
            return c;
        }

        [Test]
        public void TopUp_EmptyWorld_GeneratesFullWindow()
        {
            var c = MakeOneTime("2026-06-15");
            int added = TrainRunGenerator.TopUpForCirculation(c);
            Assert.AreEqual(2, added, "1 data × 2 kroki.");
            Assert.AreEqual(2, TrainRunGenerator.GetForCirculation(901).Count);
        }

        [Test]
        public void TopUp_Idempotent_SecondCallAddsNothing_IdsUntouched()
        {
            var c = MakeOneTime("2026-06-15");
            TrainRunGenerator.TopUpForCirculation(c);
            var idsBefore = TrainRunGenerator.GetForCirculation(901).Select(r => r.id).ToList();

            int added = TrainRunGenerator.TopUpForCirculation(c);

            Assert.AreEqual(0, added, "Idempotencja — okno już pokryte.");
            var idsAfter = TrainRunGenerator.GetForCirculation(901).Select(r => r.id).ToList();
            CollectionAssert.AreEqual(idsBefore, idsAfter, "ID istniejących runów NIETKNIĘTE (crew linkuje po id).");
        }

        [Test]
        public void TopUp_FillsOnlyMissing_PreservesExistingRuntimeState()
        {
            var c = MakeOneTime("2026-06-15");
            TrainRunGenerator.TopUpForCirculation(c);

            // Symulacja: krok 0 ma runtime state (jechał); krok 1 „zgubiony" (np. stary save sprzed top-upów)
            var runs = TrainRunGenerator.GetForCirculation(901);
            var kept = runs.First(r => r.circulationStepIndex == 0);
            kept.currentDelaySec = 300;
            kept.currentPositionOnRouteM = 12_345f;
            int keptId = kept.id;
            TimetableService.TrainRuns.Remove(runs.First(r => r.circulationStepIndex == 1));

            int added = TrainRunGenerator.TopUpForCirculation(c);

            Assert.AreEqual(1, added, "Dogenerowany tylko brakujący krok.");
            var after = TrainRunGenerator.GetForCirculation(901);
            Assert.AreEqual(2, after.Count);
            var keptAfter = after.First(r => r.circulationStepIndex == 0);
            Assert.AreEqual(keptId, keptAfter.id, "Istniejący run nietknięty (ten sam obiekt/id).");
            Assert.AreEqual(300, keptAfter.currentDelaySec, "Runtime state istniejącego runa zachowany.");
            Assert.AreEqual(12_345f, keptAfter.currentPositionOnRouteM, 1e-2f);
        }

        [Test]
        public void TopUp_NonActiveCirculation_NoOp()
        {
            var c = MakeOneTime("2026-06-15");
            c.status = CirculationStatus.Draft;
            Assert.AreEqual(0, TrainRunGenerator.TopUpForCirculation(c), "Draft → 0 (top-up tylko Active).");
        }
    }
}
