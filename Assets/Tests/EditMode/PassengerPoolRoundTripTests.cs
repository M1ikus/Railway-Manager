using System.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RailwayManager.SaveLoad.Modules;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-037 Etap D: round-trip kolumnowego snapshotu puli pasażerów (moduł "passengers")
    /// + spójność kolumn + perf sanity przy 50k (pełna serializacja — decyzja user 2026-06-10).
    /// Konsumpcja pending (rebuild indeksów + guard OnTrain) = PlayMode/E2E (wymaga managera+symulatora).
    /// </summary>
    public class PassengerPoolRoundTripTests
    {
        [TearDown]
        public void TearDown() => PassengerManager.SetPendingRestore(null);

        static PassengerPoolSnapshot MakePool(int n)
        {
            var s = new PassengerPoolSnapshot
            {
                nextAgentId = n + 1,
                spawnAccumulator = 4.25f,
                agentId = new int[n],
                originStationId = new int[n],
                destinationStationId = new int[n],
                preference = new int[n],
                walletGroszy = new int[n],
                state = new int[n],
                currentStationId = new int[n],
                currentTrainRunId = new int[n],
                spawnTimeSec = new float[n],
                abandonTimeSec = new float[n],
                paidTotalGroszy = new int[n],
                transferCount = new int[n],
                purpose = new int[n],
                desiredClass = new int[n],
                currentLegIndex = new int[n],
            };
            for (int i = 0; i < n; i++)
            {
                s.agentId[i] = i + 1;
                s.originStationId[i] = 100 + (i % 50);
                s.destinationStationId[i] = 200 + (i % 70);
                s.preference[i] = i % 3;
                s.walletGroszy[i] = 5000 + i;
                s.state[i] = (i % 5 == 0) ? (int)PassengerState.OnTrain : (int)PassengerState.WaitingAtStation;
                s.currentStationId[i] = (i % 5 == 0) ? -1 : 100 + (i % 50);
                s.currentTrainRunId[i] = (i % 5 == 0) ? 42 : -1;
                s.spawnTimeSec[i] = 1000f + i;
                s.abandonTimeSec[i] = 4600f + i;
                s.paidTotalGroszy[i] = i % 5 == 0 ? 1200 : 0;
                s.transferCount[i] = i % 2;
                s.purpose[i] = i % 4;
                s.desiredClass[i] = i % 2;
                s.currentLegIndex[i] = i % 2;
            }
            return s;
        }

        [Test]
        public void Pool_RoundTrip_AllColumnsSurvive()
        {
            var src = MakePool(7);
            var json = JObject.FromObject(src);
            var rt = json.ToObject<PassengerPoolSnapshot>();

            Assert.IsNotNull(rt);
            Assert.IsTrue(rt.IsConsistent(), "Kolumny spójne po round-trip.");
            Assert.AreEqual(7, rt.Count);
            Assert.AreEqual(8, rt.nextAgentId);
            Assert.AreEqual(4.25f, rt.spawnAccumulator, 1e-4f);
            Assert.AreEqual(src.agentId, rt.agentId);
            Assert.AreEqual(src.state, rt.state);
            Assert.AreEqual(src.currentTrainRunId, rt.currentTrainRunId);
            Assert.AreEqual(src.spawnTimeSec, rt.spawnTimeSec);
            Assert.AreEqual(src.desiredClass, rt.desiredClass);
            Assert.AreEqual(src.currentLegIndex, rt.currentLegIndex);
        }

        [Test]
        public void Module_Deserialize_LandsInPending()
        {
            var data = new JObject { ["pool"] = JObject.FromObject(MakePool(5)) };
            var module = new PassengersSavable();
            module.Deserialize(data, module.SchemaVersion);
            Assert.AreEqual(5, PassengerManager.PendingPoolRestoreCount, "Payload w pending (konsumpcja w FixedUpdate).");
        }

        [Test]
        public void Module_InitializeDefault_ClearsPending()
        {
            PassengerManager.SetPendingRestore(MakePool(3));
            new PassengersSavable().InitializeDefault();
            Assert.AreEqual(0, PassengerManager.PendingPoolRestoreCount);
        }

        [Test]
        public void Inconsistent_Snapshot_Detected()
        {
            var s = MakePool(4);
            s.walletGroszy = new int[2]; // zepsuta kolumna
            Assert.IsFalse(s.IsConsistent());
        }

        [Test]
        public void Perf_50k_SerializeDeserialize_Sane()
        {
            var src = MakePool(50_000);
            var sw = Stopwatch.StartNew();
            var json = JObject.FromObject(src);
            long serMs = sw.ElapsedMilliseconds;
            sw.Restart();
            var rt = json.ToObject<PassengerPoolSnapshot>();
            long deserMs = sw.ElapsedMilliseconds;

            Assert.AreEqual(50_000, rt.Count);
            UnityEngine.Debug.Log($"[TD-037 perf] pool 50k: serialize={serMs}ms, deserialize={deserMs}ms");
            // Sanity (nie benchmark): kolumnowy format musi być w okolicach setek ms, nie sekund.
            Assert.Less(serMs, 3000, "Serializacja 50k < 3s.");
            Assert.Less(deserMs, 3000, "Deserializacja 50k < 3s.");
        }
    }
}
