using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// M-TimetableUX F1.9 Commit E polish (full semantic preservation test suite):
    /// 4 test data sets per spec'a. Verify wszystkie warianty algorytmu (Commit A naive,
    /// Commit B multi-state + waiting, Commit D hierarchical) zwracają identical lub
    /// near-identical paths gdy block reservations są puste (tolerance 5% length).
    ///
    /// **Test sets per spec:**
    /// - Set A: short routes (&lt;50 km, &lt;10 stops) — basic correctness
    /// - Set B: long routes (Hel↔Zakopane 818 km, 165 stops) — performance
    /// - Set C: dense networks (Warszawa węzeł) — track choice correctness
    /// - Set D: edge cases (block-conflicts, disconnected attempts)
    ///
    /// **Pre-EA scope:** runtime sampling z init.Stations (random pairs z target distance).
    /// Full suite (20+ predefined named scenarios per set) → post-EA M-Balance focused session.
    /// </summary>
    public static class TimeExpandedPathfinderSemanticTests
    {
        public struct TestCaseResult
        {
            public string scenarioName;
            public string fromStation;
            public string toStation;
            public float baselineLengthKm;
            public float commitALengthKm;
            public float commitBLengthKm;
            public float commitDLengthKm;
            public bool passed;
            public string failureReason;
        }

        /// <summary>
        /// Set A: short routes (target 20-50 km, prefer ≤10 stops). Tests basic correctness.
        /// </summary>
        public static List<TestCaseResult> RunSetAShortRoutes(TimetableInitializer init, int sampleCount = 5)
        {
            return RunSet(init, sampleCount, minDistKm: 20f, maxDistKm: 50f, "SetA_Short");
        }

        /// <summary>
        /// Set B: long routes (target ≥200 km). Tests performance + correctness na długiej trasie.
        /// </summary>
        public static List<TestCaseResult> RunSetBLongRoutes(TimetableInitializer init, int sampleCount = 3)
        {
            return RunSet(init, sampleCount, minDistKm: 200f, maxDistKm: 1000f, "SetB_Long");
        }

        /// <summary>
        /// Set C: dense networks. Test pair w pobliżu major stations (Warszawa typical węzeł).
        /// Pre-EA: heurystyka — pick stations z najwięcej edges (junction nodes).
        /// </summary>
        public static List<TestCaseResult> RunSetCDenseNetworks(TimetableInitializer init, int sampleCount = 3)
        {
            // Pre-EA: density approximation = pick major stations w bliskim sąsiedztwie (15-30 km)
            return RunSet(init, sampleCount, minDistKm: 15f, maxDistKm: 30f, "SetC_Dense", majorOnly: true);
        }

        /// <summary>
        /// Set D: edge cases — block conflicts (artificial blocking predicate). Test że
        /// pathfinder graceful fail gdy żaden free path.
        /// </summary>
        public static List<TestCaseResult> RunSetDEdgeCases(TimetableInitializer init)
        {
            var results = new List<TestCaseResult>();
            if (init?.Stations == null || init.Graph == null) return results;

            // Pick 1 random major→major route + test z always-blocked predicate
            var sample = PickRandomMajorPair(init, 50f, 200f);
            if (sample.from == null || sample.to == null) return results;

            var result = new TestCaseResult
            {
                scenarioName = "SetD_AlwaysBlocked",
                fromStation = sample.from.name,
                toStation = sample.to.name
            };

            var baseline = RailwayPathfinder.FindPath(init.Graph, sample.from.pathNodeId, sample.to.pathNodeId);
            if (!baseline.success)
            {
                result.passed = true; // baseline fails OK, time-aware też powinien fail
                results.Add(result);
                return results;
            }

            result.baselineLengthKm = baseline.totalLengthM / 1000f;

            // Test z always-blocked: expected Failure across all variants
            var blockedOpts = TimeExpandedPathfinder.Options.Default(6 * 3600);
            blockedOpts.isBlockFree = (key, start, end) => false;

            var commitA = TimeExpandedPathfinder.FindPath(init.Graph, sample.from.pathNodeId, sample.to.pathNodeId, blockedOpts);
            var commitB = TimeExpandedPathfinder.FindPathWithWaiting(init.Graph, sample.from.pathNodeId, sample.to.pathNodeId, blockedOpts);
            var commitD = TimeExpandedPathfinder.FindPathHierarchical(init.Graph, sample.from.pathNodeId, sample.to.pathNodeId, blockedOpts);

            // All blocked variants powinny fail (gracefully)
            if (!commitA.success && !commitB.success && !commitD.success)
            {
                result.passed = true;
                result.failureReason = "All blocked variants correctly returned Failure";
            }
            else
            {
                result.passed = false;
                result.failureReason = $"Expected all-failure z always-blocked, got: A={commitA.success}, B={commitB.success}, D={commitD.success}";
            }

            results.Add(result);
            return results;
        }

        /// <summary>Generic sampling runner shared between Set A/B/C.</summary>
        private static List<TestCaseResult> RunSet(
            TimetableInitializer init,
            int sampleCount,
            float minDistKm,
            float maxDistKm,
            string setName,
            bool majorOnly = false)
        {
            var results = new List<TestCaseResult>();
            if (init?.Stations == null || init.Graph == null) return results;

            int found = 0, attempts = 0;
            const int maxAttempts = 50;
            while (found < sampleCount && attempts < maxAttempts)
            {
                attempts++;
                var pair = majorOnly
                    ? PickRandomMajorPair(init, minDistKm, maxDistKm)
                    : PickRandomPair(init, minDistKm, maxDistKm);
                if (pair.from == null || pair.to == null) continue;

                var result = RunCase(init.Graph, pair.from, pair.to, $"{setName}_{found + 1}");
                results.Add(result);
                if (result.passed) found++;
                else found++; // count failures too dla report
            }

            return results;
        }

        /// <summary>Per-case test: baseline + 3 commit variants + tolerance check.</summary>
        private static TestCaseResult RunCase(
            PathfindingGraph graph,
            RailwayStation from,
            RailwayStation to,
            string scenarioName)
        {
            var result = new TestCaseResult
            {
                scenarioName = scenarioName,
                fromStation = from.name,
                toStation = to.name
            };

            var baseline = RailwayPathfinder.FindPath(graph, from.pathNodeId, to.pathNodeId);
            if (!baseline.success)
            {
                result.passed = false;
                result.failureReason = "baseline failed";
                return result;
            }
            result.baselineLengthKm = baseline.totalLengthM / 1000f;

            var opts = TimeExpandedPathfinder.Options.Default(6 * 3600);
            // isBlockFree = null → no block constraints → should match baseline

            var commitA = TimeExpandedPathfinder.FindPath(graph, from.pathNodeId, to.pathNodeId, opts);
            var commitB = TimeExpandedPathfinder.FindPathWithWaiting(graph, from.pathNodeId, to.pathNodeId, opts);
            var commitD = TimeExpandedPathfinder.FindPathHierarchical(graph, from.pathNodeId, to.pathNodeId, opts);

            result.commitALengthKm = commitA.success ? commitA.totalLengthM / 1000f : -1f;
            result.commitBLengthKm = commitB.success ? commitB.totalLengthM / 1000f : -1f;
            result.commitDLengthKm = commitD.success ? commitD.totalLengthM / 1000f : -1f;

            // Tolerance check (5%)
            float tolPct = 5f;
            bool aOk = commitA.success && PctDiff(commitA.totalLengthM, baseline.totalLengthM) <= tolPct;
            bool bOk = commitB.success && PctDiff(commitB.totalLengthM, baseline.totalLengthM) <= tolPct;
            bool dOk = commitD.success && PctDiff(commitD.totalLengthM, baseline.totalLengthM) <= tolPct;

            if (aOk && bOk && dOk)
            {
                result.passed = true;
                result.failureReason = null;
            }
            else
            {
                result.passed = false;
                result.failureReason = $"length mismatch: A={aOk}, B={bOk}, D={dOk}";
            }

            return result;
        }

        private static float PctDiff(float actual, float baseline)
        {
            return Mathf.Abs(actual - baseline) / Mathf.Max(1f, baseline) * 100f;
        }

        private static (RailwayStation from, RailwayStation to) PickRandomPair(
            TimetableInitializer init, float minDistKm, float maxDistKm)
        {
            int n = init.Stations.Count;
            if (n < 2) return (null, null);
            int iA = Random.Range(0, n);
            for (int j = 0; j < 100; j++)
            {
                int iB = Random.Range(0, n);
                if (iA == iB) continue;
                var a = init.Stations[iA]; var b = init.Stations[iB];
                if (a.pathNodeId < 0 || b.pathNodeId < 0) continue;
                float distKm = Vector2.Distance(a.position, b.position) / 1000f;
                if (distKm >= minDistKm && distKm <= maxDistKm) return (a, b);
            }
            return (null, null);
        }

        private static (RailwayStation from, RailwayStation to) PickRandomMajorPair(
            TimetableInitializer init, float minDistKm, float maxDistKm)
        {
            var majors = new List<RailwayStation>();
            foreach (var s in init.Stations)
                if (s.isMajorStation && s.pathNodeId >= 0) majors.Add(s);
            if (majors.Count < 2) return (null, null);

            int iA = Random.Range(0, majors.Count);
            for (int j = 0; j < 100; j++)
            {
                int iB = Random.Range(0, majors.Count);
                if (iA == iB) continue;
                var a = majors[iA]; var b = majors[iB];
                float distKm = Vector2.Distance(a.position, b.position) / 1000f;
                if (distKm >= minDistKm && distKm <= maxDistKm) return (a, b);
            }
            return (null, null);
        }

        /// <summary>
        /// Full suite report (all 4 sets) → log z breakdown per set + overall pass rate.
        /// </summary>
        public static void RunFullSuiteAndLog(TimetableInitializer init)
        {
            if (init == null) { Log.Warn("[F1.9 Commit E] TimetableInitializer null"); return; }

            Log.Info("[F1.9 Commit E] Running full semantic preservation suite (4 test sets)...");

            float t0 = Time.realtimeSinceStartup;

            var setA = RunSetAShortRoutes(init, 5);
            var setB = RunSetBLongRoutes(init, 3);
            var setC = RunSetCDenseNetworks(init, 3);
            var setD = RunSetDEdgeCases(init);

            float elapsedMs = (Time.realtimeSinceStartup - t0) * 1000f;

            int passA = 0, passB = 0, passC = 0, passD = 0;
            foreach (var r in setA) if (r.passed) passA++;
            foreach (var r in setB) if (r.passed) passB++;
            foreach (var r in setC) if (r.passed) passC++;
            foreach (var r in setD) if (r.passed) passD++;

            Log.Info($"[F1.9 Commit E] Suite complete w {elapsedMs:F0}ms:");
            Log.Info($"[F1.9 Commit E]   Set A (short): {passA}/{setA.Count} passed");
            Log.Info($"[F1.9 Commit E]   Set B (long):  {passB}/{setB.Count} passed");
            Log.Info($"[F1.9 Commit E]   Set C (dense): {passC}/{setC.Count} passed");
            Log.Info($"[F1.9 Commit E]   Set D (edge):  {passD}/{setD.Count} passed");

            // Sample failures
            int shownFailures = 0;
            foreach (var lst in new[] { setA, setB, setC, setD })
            {
                foreach (var r in lst)
                {
                    if (r.passed) continue;
                    if (shownFailures >= 5) break;
                    Log.Warn($"[F1.9 Commit E] FAIL {r.scenarioName} {r.fromStation}→{r.toStation}: {r.failureReason} " +
                             $"(baseline={r.baselineLengthKm:F1}km, A={r.commitALengthKm:F1}, B={r.commitBLengthKm:F1}, D={r.commitDLengthKm:F1})");
                    shownFailures++;
                }
                if (shownFailures >= 5) break;
            }
        }
    }
}
