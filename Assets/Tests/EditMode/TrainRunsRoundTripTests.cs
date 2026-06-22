using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RailwayManager.SaveLoad.Modules;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-037 Etap A: round-trip serializacji modułu "trainruns" — pełna lista TrainRuns
    /// (statyczne + runtime pola, Z ID) + snapshoty aktywnych do pending-restore.
    /// </summary>
    public class TrainRunsRoundTripTests
    {
        List<TrainRun> _backupRuns;

        [SetUp]
        public void SetUp()
        {
            _backupRuns = new List<TrainRun>(TimetableService.TrainRuns);
            TimetableService.RestoreTrainRunsFromSave(null);
            TrainRunSimulator.SetPendingRestore(null);
        }

        [TearDown]
        public void TearDown()
        {
            TimetableService.RestoreTrainRunsFromSave(_backupRuns);
            TrainRunSimulator.SetPendingRestore(null);
        }

        static TrainRun MakeRun(int id) => new TrainRun
        {
            id = id,
            timetableId = 7,
            circulationId = 3,
            circulationStepIndex = 1,
            isDeliveryRun = false,
            runDateIso = "2026-06-10",
            runDateGameTime = 1_780_000_000L,
            startMinutesFromMidnight = 870, // 14:30
            trainNumberSnapshot = "IR 1430",
            currentDelaySec = 125,
            currentPositionOnRouteM = 43_217.5f,
            currentSegmentId = 991,
            isCompleted = false,
            isCancelled = false,
            runningVehicleIds = new List<int> { 11, 12 },
        };

        [Test]
        public void TrainRuns_RoundTrip_AllFieldsSurvive()
        {
            TimetableService.TrainRuns.Add(MakeRun(42));
            TimetableService.TrainRuns.Add(MakeRun(43));
            TimetableService.TrainRuns[1].isCompleted = true;

            var module = new TrainRunsSavable();
            JObject data = module.Serialize();

            TimetableService.RestoreTrainRunsFromSave(null);
            Assert.AreEqual(0, TimetableService.TrainRuns.Count, "Wyczyszczone przed restore.");

            module.Deserialize(data, module.SchemaVersion);

            Assert.AreEqual(2, TimetableService.TrainRuns.Count);
            var r = TimetableService.TrainRuns[0];
            Assert.AreEqual(42, r.id, "ID przetrwało (krytyczne — CrewDuty linkuje po id).");
            Assert.AreEqual(7, r.timetableId);
            Assert.AreEqual(3, r.circulationId);
            Assert.AreEqual(1, r.circulationStepIndex);
            Assert.AreEqual("2026-06-10", r.runDateIso);
            Assert.AreEqual(870, r.startMinutesFromMidnight);
            Assert.AreEqual("IR 1430", r.trainNumberSnapshot);
            Assert.AreEqual(125, r.currentDelaySec, "Runtime: delay przetrwał.");
            Assert.AreEqual(43_217.5f, r.currentPositionOnRouteM, 1e-3f, "Runtime: pozycja przetrwała.");
            Assert.AreEqual(991, r.currentSegmentId);
            CollectionAssert.AreEqual(new[] { 11, 12 }, r.runningVehicleIds, "Runtime: pojazdy przetrwały.");
            Assert.IsTrue(TimetableService.TrainRuns[1].isCompleted, "Runtime: isCompleted przetrwało.");
        }

        [Test]
        public void ActiveSnapshots_RoundTrip_LandInPending()
        {
            // Snapshoty budowane normalnie z _activeTrains (PlayMode); tu round-trip przez JSON
            // tą samą ścieżką co moduł (JArray.FromObject + ToObject) → pending.
            var snap = new ActiveRunSnapshot
            {
                trainRunId = 42,
                currentSpeedMps = 27.5f,
                state = TrainState.Running,
                currentStopIndex = 4,
                currentBlockIndex = 9,
                brokenComponentIndex = 2,
                breakdownStartedGameTime = 123_456L,
                selfRepairAttemptGameTime = 124_000L,
                brokenVehicleId = 11,
                doorsBroken = true,
                wheelsSpeedLimitedKmh = 60,
            };

            var data = new JObject
            {
                ["trainRuns"] = new JArray(),
                ["activeSnapshots"] = JArray.FromObject(new List<ActiveRunSnapshot> { snap }),
            };

            var module = new TrainRunsSavable();
            module.Deserialize(data, module.SchemaVersion);

            Assert.AreEqual(1, TrainRunSimulator.PendingRestoreCount, "Snapshot wylądował w pending.");

            // Pola snapshotu przeżyły JSON (deserializacja tym samym ToObject co moduł)
            var rt = ((JArray)data["activeSnapshots"])[0].ToObject<ActiveRunSnapshot>();
            Assert.AreEqual(42, rt.trainRunId);
            Assert.AreEqual(27.5f, rt.currentSpeedMps, 1e-4f);
            Assert.AreEqual(TrainState.Running, rt.state);
            Assert.AreEqual(4, rt.currentStopIndex);
            Assert.AreEqual(9, rt.currentBlockIndex);
            Assert.AreEqual(2, rt.brokenComponentIndex);
            Assert.AreEqual(123_456L, rt.breakdownStartedGameTime);
            Assert.AreEqual(124_000L, rt.selfRepairAttemptGameTime);
            Assert.AreEqual(11, rt.brokenVehicleId);
            Assert.IsTrue(rt.doorsBroken);
            Assert.AreEqual(60, rt.wheelsSpeedLimitedKmh);
        }

        [Test]
        public void OldSave_NoSection_InitializeDefault_EmptyAndNoPending()
        {
            TimetableService.TrainRuns.Add(MakeRun(1));
            TrainRunSimulator.SetPendingRestore(new List<ActiveRunSnapshot> { new ActiveRunSnapshot() });

            var module = new TrainRunsSavable();
            module.InitializeDefault();

            Assert.AreEqual(0, TimetableService.TrainRuns.Count, "Default = brak runów (jak stare zachowanie).");
            Assert.AreEqual(0, TrainRunSimulator.PendingRestoreCount, "Pending wyczyszczony.");
        }

        [Test]
        public void EmptyData_Deserialize_Graceful()
        {
            var module = new TrainRunsSavable();
            module.Deserialize(new JObject(), module.SchemaVersion);
            Assert.AreEqual(0, TimetableService.TrainRuns.Count);
            Assert.AreEqual(0, TrainRunSimulator.PendingRestoreCount);
        }
    }
}
