using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RailwayManager.Maintenance;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>TD-037 Etap E: round-trip OngoingRescue (czysto timerowy struct) + pending.</summary>
    public class RescueRoundTripTests
    {
        [TearDown]
        public void TearDown() => RescueService.SetPendingRestore(null);

        [Test]
        public void OngoingRescue_RoundTrip_AllFieldsSurvive()
        {
            var src = new OngoingRescue
            {
                brokenTrainRunId = 42,
                rescueLocoId = 7,
                brokenVehicleIds = new List<int> { 11, 12, 13 },
                startedGameTime = 100_000L,
                inboundFinishGameTime = 103_600L,
                returnFinishGameTime = 107_200L,
                pathLengthM = 48_000f,
                phase = RescuePhase.Returning,
            };

            var rt = JArray.FromObject(new List<OngoingRescue> { src })[0].ToObject<OngoingRescue>();

            Assert.AreEqual(42, rt.brokenTrainRunId);
            Assert.AreEqual(7, rt.rescueLocoId);
            CollectionAssert.AreEqual(new[] { 11, 12, 13 }, rt.brokenVehicleIds);
            Assert.AreEqual(100_000L, rt.startedGameTime);
            Assert.AreEqual(103_600L, rt.inboundFinishGameTime, "Timer absolutny — Update podejmie po load.");
            Assert.AreEqual(107_200L, rt.returnFinishGameTime);
            Assert.AreEqual(48_000f, rt.pathLengthM, 1e-2f);
            Assert.AreEqual(RescuePhase.Returning, rt.phase, "Faza przetrwała (Inbound/Returning).");
        }

        [Test]
        public void SetPendingRestore_StoresAndClears()
        {
            RescueService.SetPendingRestore(new List<OngoingRescue> { new OngoingRescue { brokenTrainRunId = 1 } });
            Assert.AreEqual(1, RescueService.PendingRestoreCount);
            RescueService.SetPendingRestore(null);
            Assert.AreEqual(0, RescueService.PendingRestoreCount);
        }
    }
}
