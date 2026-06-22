using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Economy;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// M6: testy PassengerManager (agent-based hot path) na realnym grafie PL. PlayMode bo wymaga
    /// OD matrix (TryBuildODMatrix z grafu) + stationById. Ładuje MapScene.
    ///
    /// Pokrywa: build OD matrix, DebugForceSpawn (deterministyczny spawn N agentów na parze),
    /// CountWaitingAt, guard from==to, abandon (przekroczona cierpliwość → agent znika), DebugClearAgents.
    /// </summary>
    public class PassengerSimulationTests
    {
        const float ReadyTimeoutSec = 120f;

        [UnityTest]
        public IEnumerator ForceSpawn_AddsAgentsWaitingAtOrigin()
        {
            yield return LoadMapSceneAndWaitReady();
            var pm = SetupPassengerManager(out int from, out int to);
            if (pm == null) { Assert.Ignore("Brak OD matrix / pary stacji — środowisko bez mapy PL."); yield break; }

            try
            {
                pm.DebugClearAgents();
                int spawned = pm.DebugForceSpawn(from, to, 50);

                Assert.That(spawned, Is.EqualTo(50), "DebugForceSpawn powinno spawnować żądaną liczbę (w budżecie).");
                Assert.That(pm.CountWaitingAt(from), Is.EqualTo(50),
                    "50 agentów czeka na stacji origin (WaitingAtStation).");
            }
            finally { pm.DebugClearAgents(); }
        }

        [UnityTest]
        public IEnumerator ForceSpawn_SameStation_SpawnsZero()
        {
            yield return LoadMapSceneAndWaitReady();
            var pm = SetupPassengerManager(out int from, out _);
            if (pm == null) { Assert.Ignore("Brak OD matrix."); yield break; }

            try
            {
                pm.DebugClearAgents();
                int spawned = pm.DebugForceSpawn(from, from, 10);
                Assert.That(spawned, Is.EqualTo(0), "from==to → brak podróży, 0 agentów.");
            }
            finally { pm.DebugClearAgents(); }
        }

        [UnityTest]
        public IEnumerator Agents_AbandonAfterPatienceExceeded()
        {
            yield return LoadMapSceneAndWaitReady();
            var pm = SetupPassengerManager(out int from, out int to);
            if (pm == null) { Assert.Ignore("Brak OD matrix."); yield break; }

            float timeBackup = GameState.GameTimeSeconds;
            bool pausedBackup = GameState.IsPaused;
            try
            {
                GameState.IsPaused = false;
                GameState.GameTimeSeconds = 1000f;     // znana baza — abandonTime = 1000 + 3600 = 4600
                pm.DebugClearAgents();

                int spawned = pm.DebugForceSpawn(from, to, 20);
                Assert.That(spawned, Is.GreaterThan(0));
                Assert.That(pm.CountWaitingAt(from), Is.EqualTo(spawned), "Agenci czekają.");

                // Przewiń czas gry poza cierpliwość (60 min) — agenci powinni abandonować w tick.
                GameState.GameTimeSeconds = 5000f;     // > 4600 abandonTime

                float t0 = Time.realtimeSinceStartup;
                while (pm.CountWaitingAt(from) > 0)
                {
                    if (Time.realtimeSinceStartup - t0 > 30f)
                        Assert.Fail($"Agenci nie abandonowali w 30s (czeka {pm.CountWaitingAt(from)}).");
                    yield return new WaitForFixedUpdate();
                }

                Assert.That(pm.CountWaitingAt(from), Is.EqualTo(0),
                    "Po przekroczeniu cierpliwości wszyscy oczekujący agenci abandonują.");
            }
            finally
            {
                pm.DebugClearAgents();
                GameState.GameTimeSeconds = timeBackup;
                GameState.IsPaused = pausedBackup;
            }
        }

        [UnityTest]
        public IEnumerator ClearAgents_RemovesAll()
        {
            yield return LoadMapSceneAndWaitReady();
            var pm = SetupPassengerManager(out int from, out int to);
            if (pm == null) { Assert.Ignore("Brak OD matrix."); yield break; }

            pm.DebugForceSpawn(from, to, 30);
            Assert.That(pm.ActiveAgentCount, Is.GreaterThan(0));

            pm.DebugClearAgents();
            Assert.That(pm.ActiveAgentCount, Is.EqualTo(0), "DebugClearAgents czyści wszystkich agentów.");
            Assert.That(pm.CountWaitingAt(from), Is.EqualTo(0));
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>EnsureExists + build OD matrix + znajdź 2 stacje z węzłem. Null gdy graf bez stacji.</summary>
        static PassengerManager SetupPassengerManager(out int from, out int to)
        {
            from = -1; to = -1;
            var pm = PassengerManager.Instance ?? PassengerManager.EnsureExists();
            if (!pm.TryBuildODMatrix()) return null;

            var init = TimetableInitializer.Instance;
            var nodes = new List<int>();
            foreach (var s in init.Stations)
            {
                if (s.pathNodeId >= 0) nodes.Add(s.pathNodeId);
                if (nodes.Count >= 2) break;
            }
            if (nodes.Count < 2) return null;
            from = nodes[0]; to = nodes[1];
            return pm;
        }

        static IEnumerator LoadMapSceneAndWaitReady()
        {
            if (SceneManager.GetActiveScene().name != "MapScene")
            {
                var load = SceneManager.LoadSceneAsync("MapScene", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
            }
            var init = TimetableInitializer.Instance ?? TimetableInitializer.EnsureBootstrapped();
            if (init != null && !init.IsReady) init.Initialize();

            float t0 = Time.realtimeSinceStartup;
            while (init != null && !init.IsReady)
            {
                if (Time.realtimeSinceStartup - t0 > ReadyTimeoutSec)
                    Assert.Fail($"TimetableInitializer nie gotowy w {ReadyTimeoutSec}s.");
                yield return null;
            }
        }
    }
}
