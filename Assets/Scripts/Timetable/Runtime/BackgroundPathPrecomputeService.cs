using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// M-TimetableUX F1.9 Strategy 7 (background pre-compute): caches pre-computed
    /// pathfinding results dla repeated start/end pairs. Hook na stops edit triggers
    /// async recompute w tle — save action używa cached result (instant) zamiast
    /// blocking re-computation.
    ///
    /// **Cache semantics:**
    /// - Key: (startNodeId, endNodeId, startTimeSec)
    /// - Value: Task&lt;Result&gt; (running lub completed)
    /// - Cancellation: nowy trigger dla same key cancels poprzedni task
    ///
    /// **Use case:**
    /// 1. Player edytuje waypoints w kreator → kick precompute w tle
    /// 2. Player klika "Zapisz" → TryAwaitOrCompute returns cached lub kicks fresh
    /// 3. Save flow nie blokuje main thread (pre-compute already done w tle)
    ///
    /// **Limitations pre-EA:**
    /// - Cache reset na każdy graph change (różny graph = invalidate all)
    /// - Brak persistence — recompute po reload
    /// - Brak LRU eviction — cache rośnie linearnie z user edits (post-EA bound)
    /// </summary>
    public static class BackgroundPathPrecomputeService
    {
        private struct CacheKey : IEquatable<CacheKey>
        {
            public int startNodeId;
            public int endNodeId;
            public int startTimeSec;

            public bool Equals(CacheKey other) =>
                startNodeId == other.startNodeId
                && endNodeId == other.endNodeId
                && startTimeSec == other.startTimeSec;

            public override bool Equals(object obj) => obj is CacheKey k && Equals(k);
            public override int GetHashCode() => unchecked(startNodeId * 7919 + endNodeId * 13 + startTimeSec);
        }

        private class CacheEntry
        {
            public Task<TimeExpandedPathfinder.Result> task;
            public CancellationTokenSource cts;
        }

        private static readonly ConcurrentDictionary<CacheKey, CacheEntry> _cache = new();
        private static PathfindingGraph _lastGraph;

        /// <summary>
        /// Trigger async pre-compute w tle. Idempotent dla same key — jeśli task istnieje,
        /// no-op (current task już dostarczy result). Jeśli different key, kick nowy.
        /// </summary>
        public static void TriggerPrecompute(
            PathfindingGraph graph,
            int startNodeId,
            int endNodeId,
            TimeExpandedPathfinder.Options options)
        {
            if (graph == null) return;
            InvalidateOnGraphChange(graph);

            var key = new CacheKey
            {
                startNodeId = startNodeId,
                endNodeId = endNodeId,
                startTimeSec = options.startTimeSec
            };

            // Jeśli entry istnieje + nie completed → no-op (already running)
            if (_cache.TryGetValue(key, out var existing))
            {
                if (existing.task != null && !existing.task.IsCompleted) return;
                // Completed → keep cached (return na TryAwait)
                return;
            }

            // Kick nowy task. Task.Run(Func<Task<T>>) auto-unwraps → zwraca Task<T> bezpośrednio.
            var cts = new CancellationTokenSource();
            var entry = new CacheEntry { cts = cts };
            entry.task = Task.Run(() => TimeExpandedPathfinder.FindPathWithWaitingAsync(
                graph, startNodeId, endNodeId, options, cts.Token), cts.Token);
            _cache[key] = entry;

            Log.Info($"[F1.9 Strategy 7] Background precompute kicked: {startNodeId}→{endNodeId} @ {options.startTimeSec}");
        }

        /// <summary>
        /// Try await cached result. Jeśli cache miss lub task cancelled, fall back na sync compute
        /// inline (caller awaits). Caller powinien await this method dla save flow.
        /// </summary>
        public static async Task<TimeExpandedPathfinder.Result> TryAwaitOrComputeAsync(
            PathfindingGraph graph,
            int startNodeId,
            int endNodeId,
            TimeExpandedPathfinder.Options options,
            CancellationToken cancellationToken = default)
        {
            if (graph == null)
                return TimeExpandedPathfinder.Result.Failure("graph null");

            InvalidateOnGraphChange(graph);

            var key = new CacheKey
            {
                startNodeId = startNodeId,
                endNodeId = endNodeId,
                startTimeSec = options.startTimeSec
            };

            // Cache hit z completed task → return immediately
            if (_cache.TryGetValue(key, out var entry) && entry.task != null)
            {
                if (entry.task.IsCompleted)
                    return entry.task.Result;
                // Task running → await it (caller waits less than full compute)
                // Note: nie używamy Task.WaitAsync(ct) (.NET 6+ only). Caller cancellation
                // przez own CTS prowokuje cancel poprzez InvalidateKey jeśli potrzebne.
                try
                {
                    return await entry.task;
                }
                catch (OperationCanceledException)
                {
                    return TimeExpandedPathfinder.Result.Failure("cancelled (background precompute)");
                }
            }

            // Cache miss → kick fresh compute + await
            return await TimeExpandedPathfinder.FindPathWithWaitingAsync(
                graph, startNodeId, endNodeId, options, cancellationToken);
        }

        /// <summary>
        /// Cancel + invalidate cached entry dla key. Wywoływać gdy waypoints/stops zmienione
        /// (current pre-computed path stale).
        /// </summary>
        public static void InvalidateKey(int startNodeId, int endNodeId, int startTimeSec)
        {
            var key = new CacheKey
            {
                startNodeId = startNodeId,
                endNodeId = endNodeId,
                startTimeSec = startTimeSec
            };
            if (_cache.TryRemove(key, out var entry))
            {
                try { entry.cts?.Cancel(); }
                catch { /* ignore disposed */ }
            }
        }

        /// <summary>Full cache clear — np. na graph reload lub session restart.</summary>
        public static void ClearAll()
        {
            foreach (var entry in _cache.Values)
            {
                try { entry.cts?.Cancel(); }
                catch { /* ignore */ }
            }
            _cache.Clear();
        }

        public static int CacheCount => _cache.Count;

        /// <summary>Auto-invalidate na graph change — różny graph instance = clear cache.</summary>
        private static void InvalidateOnGraphChange(PathfindingGraph graph)
        {
            if (_lastGraph != graph)
            {
                if (_lastGraph != null) ClearAll();
                _lastGraph = graph;
            }
        }
    }
}
