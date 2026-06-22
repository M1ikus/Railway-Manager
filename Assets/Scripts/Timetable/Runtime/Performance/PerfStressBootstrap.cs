using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Timetable.Economy;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Timetable.Performance
{
    /// <summary>
    /// MP-1 (M-Performance milestone): stress test framework dla pomiarów wydajności CPU/symulacji
    /// pod skalę endgame fanatyk (1000 pociągów / 500k agentów).
    ///
    /// Spawnuje force-agents do <see cref="PassengerManager"/> (omijając OD matrix probabilistic
    /// rejection w <c>MaybeSpawnAgents</c>), nadpisuje cap, mierzy frame time przez
    /// <see cref="runDurationRealSec"/> sekund, dumpuje CSV do <c>Application.persistentDataPath/Logs/</c>.
    ///
    /// <para><b>Editor-only / debug-only.</b> Component jest skompilowany w release (przez ContextMenu API)
    /// ale nie powinien być spawn'owany w produkcji. OQ-1 (post-MP-1): czy wystawić jako "developer mode"
    /// w Steam beta branch dla power userów. Domyślnie tylko ręczny spawn GameObjectu w edytorze.</para>
    ///
    /// <para><b>Semantic preservation:</b> force-spawn omija OD matrix i prob. rejection — to symulacja
    /// stresowa, nie realny gameplay. Dane finansowe wynikłe z stress runu NIE są reprezentatywne
    /// dla normalnej rozgrywki — zignorować <c>EconomyManager</c> output po stress runie.</para>
    ///
    /// <para><b>Wymagania uruchomienia:</b> scena z <c>TimetableInitializer</c> + <c>PassengerManager</c>
    /// zainicjalizowane (OD matrix gotowa). Najlepsze użycie: <c>MapScene</c> po loadie pełnej Polski +
    /// kilku Active circulations (ActiveStationIds &gt;= 2).</para>
    ///
    /// <para><b>MVP scope MP-1 (2026-05-06):</b> mierzy z aktualnego workload'u TrainRun + force-spawn
    /// agentów. Generator fake TrainRun (target 1000 pociągów spec) odłożony do MP-1.5 / MP-2 —
    /// wymaga klonowania <c>TrainRun</c>+<c>Timetable</c>+<c>Route</c>+graph dependencies, niewspółmiernie
    /// drogie do MVP value (PassengerManager hot path to większy bottleneck niż TrainRunSimulator).</para>
    /// </summary>
    public class PerfStressBootstrap : MonoBehaviour
    {
        [Header("Target skala")]
        [Tooltip("Cel agentów do force-spawn'u. 500_000 = M-Performance target endgame.")]
        public int targetAgents = 500_000;

        [Tooltip("[TODO MP-1.5] Cel pociągów. Aktualnie ignorowany — generator fake TrainRun nie wstawiony w MVP.")]
        public int targetTrains = 1000;

        [Header("Pomiar")]
        [Tooltip("Czas trwania pomiaru w sekundach realtime (nie game time — pause/play nie wpływa na sampling).")]
        public float runDurationRealSec = 60f;

        [Tooltip("Seed dla UnityEngine.Random — powtarzalność rozkładu OD pairs przy force-spawnie.")]
        public int seed = 42;

        [Tooltip("Output folder dla CSV. Pusty = Application.persistentDataPath/Logs.")]
        public string outputFolder = "";

        [Header("Force-spawn detail")]
        [Tooltip("Cap aktywnych agentów na czas stress testu (default 50k → 600k z marginesem nad target 500k).")]
        public int stressOverrideCap = 600_000;

        [Tooltip("Liczba unikalnych OD pairs do dystrybucji agentów. 100 → 5000 agentów per pair przy 500k.")]
        public int odPairsCount = 100;

        [Tooltip("Yield co N pairs spawn'u — inaczej 500k Add do List w jednej klatce zamrozi editor.")]
        public int yieldEveryNPairs = 5;

        bool _running;

        // ── Public API ──────────────────────────────────────

        [ContextMenu("MP-1: Run stress test")]
        public void RunStressTest()
        {
            if (_running)
            {
                Log.Warn("[PerfStress] Stress test już działa — abort");
                return;
            }
            StartCoroutine(StressTestCoroutine());
        }

        [ContextMenu("MP-1: Reset stress cap to default")]
        public void ResetStressCap()
        {
            var pm = PassengerManager.Instance;
            if (pm != null) pm.DebugSetStressOverrideCap(-1);
            Log.Info("[PerfStress] Stress cap reset to default (50k)");
        }

        [ContextMenu("MP-1: Clear all agents (post-stress reset)")]
        public void ClearAgents()
        {
            var pm = PassengerManager.Instance;
            if (pm != null) pm.DebugClearAgents();
        }

        // ── Coroutine flow ──────────────────────────────────

        IEnumerator StressTestCoroutine()
        {
            _running = true;
            Log.Info($"[PerfStress] === STRESS TEST START ===");
            Log.Info($"[PerfStress] Target: {targetAgents} agents, {runDurationRealSec}s realtime, seed={seed}");

            var pm = PassengerManager.Instance;
            if (pm == null)
            {
                Log.Warn("[PerfStress] PassengerManager.Instance is null — abort. " +
                         "Załaduj MapScene z TrainRunSimulator (bootstrap'uje PassengerManager).");
                _running = false;
                yield break;
            }

            // Stage 1: ensure OD matrix (potrzebna do SpawnAgent — używa station importance map)
            if (pm.ActiveStationIds.Count < 2)
            {
                Log.Info("[PerfStress] Brak active stations — wywołuję TryBuildODMatrix");
                if (!pm.TryBuildODMatrix())
                {
                    Log.Warn("[PerfStress] TryBuildODMatrix failed — abort");
                    _running = false;
                    yield break;
                }
            }

            // Stage 1b: pool stacji do force-spawn'u
            // Preferujemy active stations (rozkłady na dziś), fallback na major stations
            // z TimetableInitializer gdy save nie ma circulations (typowy świeży save scenario).
            List<int> stationPool;
            bool usedFallback;
            if (pm.ActiveStationIds.Count >= 2)
            {
                stationPool = new List<int>(pm.ActiveStationIds);
                usedFallback = false;
                Log.Info($"[PerfStress] Station pool (active circulations): {stationPool.Count}");
            }
            else
            {
                stationPool = CollectMajorStationsFallback();
                usedFallback = true;
                if (stationPool.Count < 2)
                {
                    Log.Warn($"[PerfStress] Brak active stations + fallback major stations < 2 " +
                             $"(count={stationPool.Count}) — abort. Załaduj save z mapą Polski.");
                    _running = false;
                    yield break;
                }
                Log.Info($"[PerfStress] Station pool (FALLBACK major stations, 0 active circulations): " +
                         $"{stationPool.Count}. Test mierzy wyłącznie PassengerManager hot path; " +
                         "TrainRunSimulator.Advance workload = 0 trains.");
            }

            // Stage 2: set seed + cap override (pre-alokuje List Capacity → brak resize GC)
            UnityEngine.Random.InitState(seed);
            pm.DebugSetStressOverrideCap(stressOverrideCap);

            // Stage 3: force-spawn agents (yield batched — inaczej zamrozi editor)
            float forceSpawnStartRealtime = Time.realtimeSinceStartup;
            int totalSpawned = 0;
            int maxPairs = stationPool.Count * (stationPool.Count - 1);
            int pairsToUse = Mathf.Min(odPairsCount, maxPairs);
            int agentsPerPair = targetAgents / Mathf.Max(1, pairsToUse);

            Log.Info($"[PerfStress] Force-spawning {targetAgents} agents across {pairsToUse} OD pairs " +
                     $"(~{agentsPerPair} per pair)…");

            for (int pair = 0; pair < pairsToUse; pair++)
            {
                int fromIdx = UnityEngine.Random.Range(0, stationPool.Count);
                int toIdx = UnityEngine.Random.Range(0, stationPool.Count);
                while (toIdx == fromIdx) toIdx = UnityEngine.Random.Range(0, stationPool.Count);

                int fromId = stationPool[fromIdx];
                int toId = stationPool[toIdx];
                int spawned = pm.DebugForceSpawn(fromId, toId, agentsPerPair);
                totalSpawned += spawned;

                if (yieldEveryNPairs > 0 && pair % yieldEveryNPairs == yieldEveryNPairs - 1)
                    yield return null;
            }
            float forceSpawnDurationSec = Time.realtimeSinceStartup - forceSpawnStartRealtime;

            Log.Info($"[PerfStress] Force-spawned {totalSpawned}/{targetAgents} agents " +
                     $"in {forceSpawnDurationSec:F2}s realtime. " +
                     $"PassengerManager.ActiveAgentCount={pm.ActiveAgentCount}");

            // Stage 4: stabilize 1 frame przed sampling (niech FixedUpdate zaadaptuje)
            yield return null;

            // Stage 5: sampling loop
            Log.Info($"[PerfStress] Sampling frames przez {runDurationRealSec}s realtime…");
            int estimatedFrames = Mathf.CeilToInt(runDurationRealSec * 60f);
            var samples = new List<FrameSample>(estimatedFrames);
            float sampleStartRealtime = Time.realtimeSinceStartup;
            long gcStart = System.GC.GetTotalMemory(false);
            int activeTrainsAtStart = TrainRunSimulator.Instance != null ? TrainRunSimulator.Instance.ActiveTrainCount : 0;

            while (Time.realtimeSinceStartup - sampleStartRealtime < runDurationRealSec)
            {
                samples.Add(new FrameSample
                {
                    realTime = Time.realtimeSinceStartup - sampleStartRealtime,
                    frameMs = Time.unscaledDeltaTime * 1000f,
                    agentCount = pm.ActiveAgentCount,
                    trainCount = TrainRunSimulator.Instance != null ? TrainRunSimulator.Instance.ActiveTrainCount : 0,
                    gcAllocBytes = System.GC.GetTotalMemory(false),
                });
                yield return null;
            }

            long gcEnd = System.GC.GetTotalMemory(false);
            float elapsedRealtime = Time.realtimeSinceStartup - sampleStartRealtime;

            Log.Info($"[PerfStress] Sampling done: {samples.Count} frames in {elapsedRealtime:F2}s. " +
                     $"GC delta: {(gcEnd - gcStart) / (1024 * 1024):F1} MB.");

            // Stage 6: write CSV
            string csvPath = WriteCsv(samples, activeTrainsAtStart, totalSpawned, forceSpawnDurationSec, usedFallback);
            Log.Info($"[PerfStress] CSV: {csvPath}");

            // Stage 7: print summary
            ComputeAndLogSummary(samples, usedFallback);

            // Stage 8: cleanup nie-automatyczne — gracz może chcieć dalej eksplorować runtime stan
            // przez Profiler. Manual reset przez ContextMenu "MP-1: Clear all agents".
            Log.Info("[PerfStress] === STRESS TEST DONE === " +
                     "(agents pozostają, użyj 'MP-1: Clear all agents' żeby wyczyścić)");
            _running = false;
        }

        // ── Output ──────────────────────────────────────────

        struct FrameSample
        {
            public float realTime;
            public float frameMs;
            public int agentCount;
            public int trainCount;
            public long gcAllocBytes;
        }

        /// <summary>
        /// Fallback gdy brak active stations: bierzemy major stations bezpośrednio z TimetableInitializer.
        /// Replikuje pattern z <c>PassengerManager.DebugSpawnTestAgentsBypass</c>.
        /// </summary>
        List<int> CollectMajorStationsFallback()
        {
            var result = new List<int>();
            var init = TimetableInitializer.Instance;
            if (init == null || init.Stations == null) return result;
            foreach (var s in init.Stations)
            {
                if (s.isMajorStation && s.pathNodeId >= 0)
                    result.Add(s.stationId);
            }
            return result;
        }

        string WriteCsv(List<FrameSample> samples, int initialTrains, int spawnedAgents, float spawnDurationSec, bool usedFallback)
        {
            string folder = string.IsNullOrEmpty(outputFolder)
                ? AppPaths.LogsDir
                : outputFolder;
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            string fileName = $"perf-stress-{System.DateTime.Now:yyyyMMdd-HHmmss}-seed{seed}.csv";
            string fullPath = Path.Combine(folder, fileName);

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder(samples.Count * 64);
            sb.AppendLine("# RM M-Performance MP-1 stress test");
            sb.AppendLine($"# Date: {System.DateTime.Now:o}");
            sb.AppendLine($"# Seed: {seed}");
            sb.AppendLine($"# Target agents: {targetAgents}");
            sb.AppendLine($"# Spawned agents: {spawnedAgents}");
            sb.AppendLine($"# Active trains at sampling start: {initialTrains}");
            sb.AppendLine($"# Stress cap override: {stressOverrideCap}");
            sb.AppendLine($"# Force-spawn duration: {spawnDurationSec.ToString("F2", inv)}s realtime");
            sb.AppendLine($"# Sampling duration: {runDurationRealSec.ToString("F2", inv)}s realtime");
            sb.AppendLine($"# Frames sampled: {samples.Count}");
            sb.AppendLine($"# Used fallback (major stations, no active circulations): {usedFallback}");
            sb.AppendLine("# Notes: editor-only stress test, force-spawn omija OD matrix");
            sb.AppendLine("realTime,frameMs,agentCount,trainCount,gcBytes");

            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                sb.Append(s.realTime.ToString("F4", inv));
                sb.Append(',');
                sb.Append(s.frameMs.ToString("F3", inv));
                sb.Append(',');
                sb.Append(s.agentCount);
                sb.Append(',');
                sb.Append(s.trainCount);
                sb.Append(',');
                sb.Append(s.gcAllocBytes);
                sb.AppendLine();
            }

            File.WriteAllText(fullPath, sb.ToString());
            return fullPath;
        }

        void ComputeAndLogSummary(List<FrameSample> samples, bool usedFallback)
        {
            if (samples.Count == 0) return;

            var times = new List<float>(samples.Count);
            for (int i = 0; i < samples.Count; i++) times.Add(samples[i].frameMs);
            times.Sort();

            float sum = 0f, max = float.MinValue, min = float.MaxValue;
            for (int i = 0; i < times.Count; i++)
            {
                sum += times[i];
                if (times[i] > max) max = times[i];
                if (times[i] < min) min = times[i];
            }
            float avg = sum / times.Count;
            float p50 = times[times.Count / 2];
            float p95 = times[Mathf.Clamp((int)(times.Count * 0.95f), 0, times.Count - 1)];
            float p99 = times[Mathf.Clamp((int)(times.Count * 0.99f), 0, times.Count - 1)];
            int avgFps = avg > 0f ? Mathf.RoundToInt(1000f / avg) : 0;

            int firstAgents = samples[0].agentCount;
            int lastAgents = samples[samples.Count - 1].agentCount;
            int firstTrains = samples[0].trainCount;
            int lastTrains = samples[samples.Count - 1].trainCount;

            Log.Info($"[PerfStress] === SUMMARY ===");
            Log.Info($"[PerfStress] Frames: {samples.Count}, ~{avgFps} FPS avg");
            Log.Info($"[PerfStress] Frame time ms — avg={avg:F2} min={min:F2} max={max:F2} " +
                     $"p50={p50:F2} p95={p95:F2} p99={p99:F2}");
            Log.Info($"[PerfStress] Agents: {firstAgents} → {lastAgents} (delta {lastAgents - firstAgents})");
            Log.Info($"[PerfStress] Trains: {firstTrains} → {lastTrains} (delta {lastTrains - firstTrains})");
            if (usedFallback)
                Log.Info($"[PerfStress] (Note: użyty fallback major stations, 0 active circulations w save → " +
                         "test mierzy wyłącznie PassengerManager hot path bez TrainRunSimulator workload)");
        }
    }
}
