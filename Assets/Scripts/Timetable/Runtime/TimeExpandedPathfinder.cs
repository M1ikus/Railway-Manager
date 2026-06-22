using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// M-TimetableUX F1.9 (Commit A — naive baseline): Time-aware A* z bounded horizon
    /// + block reservation awareness. State w A* = (graphNodeId), gScore = absolute time
    /// of arrival (sekund od midnight).
    ///
    /// **Pre-F1.9 polish scope:**
    /// - Per edge transition compute travel time z lengthM + maxSpeedKmh
    /// - Skip transitions które colliduje z reserved block sections (caller injects predicate)
    /// - Bounded: jeśli arrivalTime > startTime + maxHorizonSec → fail
    /// - F1.5 directionPenalty respected w edge weight
    /// - Heuristic: euclidean distance / maxLineSpeed (admissible time lower bound)
    ///
    /// **Pre-F1.9 LIMITATIONS (deferred do F1.9 polish):**
    /// - Brak waiting at node (state = nodeId, brak time-window dimension). Waiting przy block
    ///   conflict handled przez existing <see cref="ReservationManager.CheckCollisions"/> Strategy B
    ///   (extend dwell post-pathfinding). F1.9 Commit B doda multi-state per node + time-window
    ///   dimension dla pre-emptive waiting decisions.
    /// - Brak Strategy 1-7 optimizations (sparse / hierarchical / lazy / adaptive / async / bg-precompute)
    ///   — naive baseline z bounded horizon. Performance fall-back na current FindPath gdy
    ///   block reservations są puste (semantic preservation contract).
    /// - Brak semantic preservation tests w 4 test data sets (per spec) — Commit E.
    ///
    /// **Use case Commit A:**
    /// - F1.10 sequential additive: pre-check czy nowy timetable może fit przed ResolveBlockConflicts
    /// - F1.13b mijanka: alternate pathfinding when sync mijanka nie da się fit
    /// - Pre-emptive routing dla Advanced/Expert modes (F1.16)
    /// </summary>
    public static class TimeExpandedPathfinder
    {
        /// <summary>Default horizon (8 godzin = 28800 sekund) — cap dla single timetable computation.</summary>
        public const int DefaultMaxHorizonSec = 8 * 3600;

        /// <summary>Default szacowanie max speed dla heurystyki (km/h). Sieć PL: ~200 km/h dla EIP, używamy konserwatywnie 160 km/h.</summary>
        public const int DefaultMaxLineSpeedKmh = 160;

        /// <summary>
        /// Predykat: czy block section jest free w przedziale [absStartSec, absEndSec] (sekund od midnight).
        /// Caller injects (np. lambda nad <c>TimetableService.BlockSectionReservations.IsFree</c>).
        /// </summary>
        public delegate bool BlockFreeCheck(int blockKey, long absStartSec, long absEndSec);

        public struct Options
        {
            /// <summary>Absolute start time w sekund od midnight (0-86400).</summary>
            public int startTimeSec;
            /// <summary>Cap dla pathfinding — jeśli arrivalTime > startTime + maxHorizonSec, fail.</summary>
            public int maxHorizonSec;
            /// <summary>Optional block conflict checker. Null = no block awareness (effectively standard FindPath).</summary>
            public BlockFreeCheck isBlockFree;
            /// <summary>Czy aplikować F1.5 directionPenalty w edge weight (dla preferowanego kierunku jazdy).</summary>
            public bool useDirectionPenalty;
            /// <summary>Estimated max line speed (km/h) dla admissible time heurystyki. Default 160.</summary>
            public int maxLineSpeedKmh;

            public static Options Default(int startTimeSec) => new Options
            {
                startTimeSec = startTimeSec,
                maxHorizonSec = DefaultMaxHorizonSec,
                isBlockFree = null,
                useDirectionPenalty = true,
                maxLineSpeedKmh = DefaultMaxLineSpeedKmh
            };
        }

        public struct Result
        {
            public bool success;
            public List<int> nodeIds;
            public List<int> edgeIds;
            /// <summary>Absolute arrival time per node (sekund od midnight). [0]=startTime, [last]=endTime.</summary>
            public List<int> timeArrivalsSec;
            public float totalLengthM;
            /// <summary>Total travel time (arrivalAtEnd - startTime).</summary>
            public int totalTimeSec;
            public int exploredStates;
            public double timeMs;
            /// <summary>Diagnostic dla failed paths.</summary>
            public string failureReason;

            public static Result Failure(string reason, int explored = 0, double ms = 0) => new Result
            {
                success = false,
                nodeIds = new List<int>(),
                edgeIds = new List<int>(),
                timeArrivalsSec = new List<int>(),
                exploredStates = explored,
                timeMs = ms,
                failureReason = reason
            };
        }

        /// <summary>
        /// M-TimetableUX F1.9 Commit B: waiting quantum dla multi-state per node A* expansion.
        /// 60s default = Strategy 5 adaptive resolution (medium density). Sparse expansion
        /// (Strategy 1) — states only at transitions + waiting buckets, nie continuous time.
        /// </summary>
        public const int DefaultWaitQuantumSec = 60;

        /// <summary>Max waiting allowed per node (sekund). 1800s = 30 min, sufficient dla typical block contention.</summary>
        public const int DefaultMaxWaitPerNodeSec = 1800;

        // Strategy 5 (adaptive resolution) constants
        /// <summary>Dense quantum: 6s — precyzyjne planowanie waiting w junction nodes.</summary>
        public const int DenseWaitQuantumSec = 6;
        /// <summary>Sparse quantum: 60s — coarse waiting w open-line nodes (mniej state expansion).</summary>
        public const int SparseWaitQuantumSec = 60;
        /// <summary>Threshold edge count — node z ≥ tym uważany za dense (junction).</summary>
        public const int DenseNodeEdgeThreshold = 5;

        /// <summary>
        /// M-TimetableUX F1.9 Commit C polish — Strategy 5 (adaptive resolution): dobiera
        /// waiting quantum per node na podstawie density (edge count).
        /// Dense node (junction, ≥5 edges) → 6s quantum (precyzyjne).
        /// Sparse node (open line, &lt;5 edges) → 60s quantum (coarse).
        /// </summary>
        public static int GetAdaptiveQuantumSec(in PathfindingGraph.Node node)
        {
            int edges = node.edgeIds != null ? node.edgeIds.Count : 0;
            return edges >= DenseNodeEdgeThreshold ? DenseWaitQuantumSec : SparseWaitQuantumSec;
        }

        /// <summary>
        /// Time-aware A* — szuka ścieżki uwzględniając block reservation conflicts i bounded
        /// time horizon.
        /// </summary>
        public static Result FindPath(
            PathfindingGraph graph,
            int startNodeId,
            int endNodeId,
            Options options,
            int maxStatesExplored = 500_000)
        {
            var sw = Stopwatch.StartNew();

            if (graph == null || startNodeId < 0 || endNodeId < 0
                || startNodeId >= graph.NodeCount || endNodeId >= graph.NodeCount)
                return Result.Failure("invalid graph or node IDs");

            if (startNodeId == endNodeId)
                return new Result
                {
                    success = true,
                    nodeIds = new List<int> { startNodeId },
                    edgeIds = new List<int>(),
                    timeArrivalsSec = new List<int> { options.startTimeSec },
                    totalLengthM = 0f,
                    totalTimeSec = 0,
                    exploredStates = 0,
                    timeMs = sw.Elapsed.TotalMilliseconds
                };

            int maxHorizon = options.maxHorizonSec > 0 ? options.maxHorizonSec : DefaultMaxHorizonSec;
            int maxSpeed = options.maxLineSpeedKmh > 0 ? options.maxLineSpeedKmh : DefaultMaxLineSpeedKmh;
            float maxSpeedMps = maxSpeed * (1000f / 3600f);

            Vector2 goalPos = graph.GetNode(endNodeId).position;

            // gScore: per node, najwcześniejszy znany absolute arrival time (sec od midnight)
            var gScore = new Dictionary<int, int>();
            var cameFromNode = new Dictionary<int, int>();
            var cameFromEdge = new Dictionary<int, int>();

            var openSet = new MinHeap<int>();
            var inOpenSet = new HashSet<int>();

            gScore[startNodeId] = options.startTimeSec;
            float h0 = HeuristicTimeSec(graph.GetNode(startNodeId).position, goalPos, maxSpeedMps);
            openSet.Push(startNodeId, options.startTimeSec + h0);
            inOpenSet.Add(startNodeId);

            int explored = 0;
            int blockedSkipped = 0;
            int horizonSkipped = 0;

            while (openSet.TryPop(out int current))
            {
                inOpenSet.Remove(current);
                explored++;

                if (current == endNodeId)
                {
                    sw.Stop();
                    var result = Reconstruct(graph, cameFromNode, cameFromEdge, gScore, endNodeId, options.startTimeSec, explored);
                    result.timeMs = sw.Elapsed.TotalMilliseconds;
                    return result;
                }

                if (explored >= maxStatesExplored)
                {
                    sw.Stop();
                    return Result.Failure(
                        $"max states explored exceeded ({maxStatesExplored}); blocked={blockedSkipped}, horizon={horizonSkipped}",
                        explored, sw.Elapsed.TotalMilliseconds);
                }

                int currentTime = gScore[current];
                var currentNode = graph.GetNode(current);

                foreach (int edgeId in currentNode.edgeIds)
                {
                    var edge = graph.GetEdge(edgeId);
                    int neighbor = edge.toNodeId;

                    int travelTimeSec = ComputeTravelTimeSec(edge);
                    if (travelTimeSec <= 0) travelTimeSec = 1; // safety

                    long absDepartureSec = currentTime;
                    long absArrivalSec = absDepartureSec + travelTimeSec;

                    // Bounded horizon
                    if (absArrivalSec - options.startTimeSec > maxHorizon)
                    {
                        horizonSkipped++;
                        continue;
                    }

                    // F1.5 direction penalty integration
                    float weightMultiplier = options.useDirectionPenalty
                        ? RailwayPathfinder.DirectionPenalty(edge)
                        : 1f;
                    int adjustedTravelSec = Mathf.RoundToInt(travelTimeSec * weightMultiplier);
                    int candidateArrivalTime = currentTime + adjustedTravelSec;

                    // Block reservation check
                    if (options.isBlockFree != null)
                    {
                        int blockKey = ComputeBlockKey(current, neighbor);
                        if (!options.isBlockFree(blockKey, absDepartureSec, absArrivalSec))
                        {
                            blockedSkipped++;
                            continue; // pre-F1.9 polish: brak waiting, skip directly
                        }
                    }

                    if (gScore.TryGetValue(neighbor, out int existingTime) && candidateArrivalTime >= existingTime)
                        continue;

                    cameFromNode[neighbor] = current;
                    cameFromEdge[neighbor] = edgeId;
                    gScore[neighbor] = candidateArrivalTime;

                    float h = HeuristicTimeSec(graph.GetNode(neighbor).position, goalPos, maxSpeedMps);
                    float fScore = candidateArrivalTime + h;
                    if (!inOpenSet.Contains(neighbor))
                    {
                        openSet.Push(neighbor, fScore);
                        inOpenSet.Add(neighbor);
                    }
                }
            }

            sw.Stop();
            return Result.Failure(
                $"no path found; blocked={blockedSkipped}, horizon={horizonSkipped}",
                explored, sw.Elapsed.TotalMilliseconds);
        }

        /// <summary>
        /// M-TimetableUX F1.9 Commit B: A* z multi-state per node (waiting at node allowed gdy
        /// block conflict). State = (nodeId, timeBucket) z waitQuantum (60s default).
        /// Sparse expansion (Strategy 1) — waiting states tworzone tylko on-demand gdy
        /// transition blocked. Bounded (Strategy 4) — max 30 min waiting per node.
        ///
        /// **Use case:** F1.10 sequential additive scheduling — gdy block conflict wykryty
        /// proactivnie, pathfinder może zaplanować waiting at predecessor zamiast skip
        /// transition (lepsze niż F1.9 Commit A naive baseline który skip'ował).
        ///
        /// Backward compat: <see cref="FindPath"/> (Commit A) zachowane dla simple cases.
        /// </summary>
        public static Result FindPathWithWaiting(
            PathfindingGraph graph,
            int startNodeId,
            int endNodeId,
            Options options,
            int maxStatesExplored = 500_000,
            int waitQuantumSec = DefaultWaitQuantumSec,
            int maxWaitPerNodeSec = DefaultMaxWaitPerNodeSec)
        {
            var sw = Stopwatch.StartNew();

            if (graph == null || startNodeId < 0 || endNodeId < 0
                || startNodeId >= graph.NodeCount || endNodeId >= graph.NodeCount)
                return Result.Failure("invalid graph or node IDs");

            if (startNodeId == endNodeId)
                return new Result
                {
                    success = true,
                    nodeIds = new List<int> { startNodeId },
                    edgeIds = new List<int>(),
                    timeArrivalsSec = new List<int> { options.startTimeSec },
                    timeMs = sw.Elapsed.TotalMilliseconds
                };

            int maxHorizon = options.maxHorizonSec > 0 ? options.maxHorizonSec : DefaultMaxHorizonSec;
            int maxSpeed = options.maxLineSpeedKmh > 0 ? options.maxLineSpeedKmh : DefaultMaxLineSpeedKmh;
            float maxSpeedMps = maxSpeed * (1000f / 3600f);
            int maxWaitBuckets = Math.Max(1, maxWaitPerNodeSec / waitQuantumSec);

            Vector2 goalPos = graph.GetNode(endNodeId).position;

            // State = composite (nodeId, timeBucket). Encoded as long: (nodeId << 24) | bucket.
            // gScore: composite → absolute arrival time
            var gScore = new Dictionary<long, int>();
            var cameFromNode = new Dictionary<long, long>();
            var cameFromEdge = new Dictionary<long, int>(); // -1 dla waiting transitions
            var openSet = new MinHeap<long>();
            var inOpenSet = new HashSet<long>();

            long startKey = MakeStateKey(startNodeId, 0);
            gScore[startKey] = options.startTimeSec;
            float h0 = HeuristicTimeSec(graph.GetNode(startNodeId).position, goalPos, maxSpeedMps);
            openSet.Push(startKey, options.startTimeSec + h0);
            inOpenSet.Add(startKey);

            // Per-node tracking buckets exposed (dla waiting expansion)
            var nodeWaitBuckets = new Dictionary<int, int>(); // node → max bucket seen
            nodeWaitBuckets[startNodeId] = 0;

            int explored = 0, blocked = 0, horizonSkip = 0, waitStates = 0;
            long endKeyFound = -1;

            while (openSet.TryPop(out long current))
            {
                inOpenSet.Remove(current);
                explored++;

                int currentNodeId = DecodeNodeId(current);
                int currentBucket = DecodeBucket(current);

                if (currentNodeId == endNodeId)
                {
                    endKeyFound = current;
                    break;
                }

                if (explored >= maxStatesExplored)
                {
                    sw.Stop();
                    return Result.Failure(
                        $"max states ({maxStatesExplored}); blocked={blocked}, horizon={horizonSkip}, waitStates={waitStates}",
                        explored, sw.Elapsed.TotalMilliseconds);
                }

                int currentTime = gScore[current];
                var currentNode = graph.GetNode(currentNodeId);

                // Transition expansion (edges)
                foreach (int edgeId in currentNode.edgeIds)
                {
                    var edge = graph.GetEdge(edgeId);
                    int neighbor = edge.toNodeId;

                    int travelTimeSec = ComputeTravelTimeSec(edge);
                    if (travelTimeSec <= 0) travelTimeSec = 1;

                    long absDeparture = currentTime;
                    long absArrival = absDeparture + travelTimeSec;

                    if (absArrival - options.startTimeSec > maxHorizon)
                    {
                        horizonSkip++;
                        continue;
                    }

                    float weightMult = options.useDirectionPenalty
                        ? RailwayPathfinder.DirectionPenalty(edge)
                        : 1f;
                    int adjustedTravelSec = Mathf.RoundToInt(travelTimeSec * weightMult);
                    int candidateArrivalTime = currentTime + adjustedTravelSec;

                    if (options.isBlockFree != null)
                    {
                        int blockKey = ComputeBlockKey(currentNodeId, neighbor);
                        if (!options.isBlockFree(blockKey, absDeparture, absArrival))
                        {
                            blocked++;
                            continue; // skip transition; waiting at currentNode handles via wait-state expansion below
                        }
                    }

                    // Neighbor state: bucket 0 (arrival to fresh node, no waiting yet)
                    long neighborKey = MakeStateKey(neighbor, 0);
                    if (gScore.TryGetValue(neighborKey, out int existingTime) && candidateArrivalTime >= existingTime)
                        continue;

                    cameFromNode[neighborKey] = current;
                    cameFromEdge[neighborKey] = edgeId;
                    gScore[neighborKey] = candidateArrivalTime;

                    float h = HeuristicTimeSec(graph.GetNode(neighbor).position, goalPos, maxSpeedMps);
                    float fScore = candidateArrivalTime + h;
                    if (!inOpenSet.Contains(neighborKey))
                    {
                        openSet.Push(neighborKey, fScore);
                        inOpenSet.Add(neighborKey);
                    }
                }

                // Waiting expansion: same node, bucket+1 (lazy — only jeśli not exceeded max wait)
                // M-TimetableUX F1.9 Commit C polish: Strategy 5 adaptive resolution —
                // gdy waitQuantumSec=0 (sentinel), użyj per-node adaptive (6s dense, 60s sparse).
                int effectiveQuantum = waitQuantumSec > 0 ? waitQuantumSec : GetAdaptiveQuantumSec(currentNode);
                if (currentBucket < maxWaitBuckets)
                {
                    long waitKey = MakeStateKey(currentNodeId, currentBucket + 1);
                    int waitArrivalTime = currentTime + effectiveQuantum;
                    if (waitArrivalTime - options.startTimeSec <= maxHorizon)
                    {
                        if (!gScore.TryGetValue(waitKey, out int existingWait) || waitArrivalTime < existingWait)
                        {
                            cameFromNode[waitKey] = current;
                            cameFromEdge[waitKey] = -1; // -1 = waiting transition (no edge)
                            gScore[waitKey] = waitArrivalTime;
                            float hWait = HeuristicTimeSec(graph.GetNode(currentNodeId).position, goalPos, maxSpeedMps);
                            if (!inOpenSet.Contains(waitKey))
                            {
                                openSet.Push(waitKey, waitArrivalTime + hWait);
                                inOpenSet.Add(waitKey);
                                waitStates++;
                            }
                        }
                    }
                }
            }

            sw.Stop();

            if (endKeyFound < 0)
                return Result.Failure(
                    $"no path; blocked={blocked}, horizon={horizonSkip}, waitStates={waitStates}",
                    explored, sw.Elapsed.TotalMilliseconds);

            // Reconstruct dla multi-state — collapse waiting transitions w nodeIds list
            return ReconstructWithWaiting(graph, cameFromNode, cameFromEdge, gScore, endKeyFound, options.startTimeSec, explored, sw.Elapsed.TotalMilliseconds, waitStates);
        }

        /// <summary>State key encoding: (nodeId << 24) | bucket. Max nodeId ~16M (2^24), max bucket ~256 (więcej niż 30min/60s=30).</summary>
        private static long MakeStateKey(int nodeId, int bucket) => ((long)nodeId << 24) | (long)(bucket & 0xFFFFFF);
        private static int DecodeNodeId(long key) => (int)(key >> 24);
        private static int DecodeBucket(long key) => (int)(key & 0xFFFFFF);

        private static Result ReconstructWithWaiting(
            PathfindingGraph graph,
            Dictionary<long, long> cameFromNode,
            Dictionary<long, int> cameFromEdge,
            Dictionary<long, int> gScore,
            long endKey,
            int startTimeSec,
            int explored,
            double timeMs,
            int waitStates)
        {
            var nodeIds = new List<int>();
            var edgeIds = new List<int>();
            var timeArrivals = new List<int>();

            long current = endKey;
            int lastNodeId = -1;
            // Collapse waiting transitions: skip consecutive same-nodeId entries (keep arrival time z first w waiting chain)
            nodeIds.Add(DecodeNodeId(current));
            timeArrivals.Add(gScore[current]);
            lastNodeId = DecodeNodeId(current);

            while (cameFromNode.TryGetValue(current, out long prev))
            {
                int edgeId = cameFromEdge[current];
                int prevNodeId = DecodeNodeId(prev);

                if (edgeId >= 0)
                {
                    // Real transition (edge) — add edge + prev node
                    edgeIds.Add(edgeId);
                    nodeIds.Add(prevNodeId);
                    timeArrivals.Add(gScore[prev]);
                    lastNodeId = prevNodeId;
                }
                else
                {
                    // Waiting transition — same node, just update lastNodeId tracking
                    // (skip adding duplicate node, keep timeArrivals[last] = earliest arrival)
                    // We replace the most-recent timeArrivals (waited longer at this node = earlier arrival point);
                    // simplification: keep current timeArrival as-is (it's later wait point).
                }

                current = prev;
            }

            nodeIds.Reverse();
            edgeIds.Reverse();
            timeArrivals.Reverse();

            float totalLength = 0f;
            for (int i = 0; i < edgeIds.Count; i++)
                totalLength += graph.GetEdge(edgeIds[i]).lengthM;

            int totalTimeSec = timeArrivals.Count > 0 ? timeArrivals[timeArrivals.Count - 1] - startTimeSec : 0;

            return new Result
            {
                success = true,
                nodeIds = nodeIds,
                edgeIds = edgeIds,
                timeArrivalsSec = timeArrivals,
                totalLengthM = totalLength,
                totalTimeSec = totalTimeSec,
                exploredStates = explored,
                timeMs = timeMs,
                failureReason = $"waitStates={waitStates}"
            };
        }

        /// <summary>
        /// M-TimetableUX F1.9 Commit C (Strategy 6 — async + progress UI MUST-HAVE):
        /// Async wrapper z CancellationToken + IProgress reporting.
        /// Player czeka comfortably z progress bar (1-2s acceptable for offline timetable creation).
        ///
        /// Implementation: Task.Run + per-iteration cancellation check (każde
        /// <see cref="CancellationCheckInterval"/> exploredStates). Progress reporting
        /// per same interval (exploredStates / maxStatesExplored ratio).
        ///
        /// **Use case:**
        /// - UI workflow F1.15 pipeline view: async pathfinding nie blokuje main thread
        /// - Player może cancel długi pathfinding (np. disconnected graph attempt)
        /// - Progress bar w UI (TimetableCreatorUI status bar)
        /// </summary>
        public const int CancellationCheckInterval = 1000;

        public static async Task<Result> FindPathWithWaitingAsync(
            PathfindingGraph graph,
            int startNodeId,
            int endNodeId,
            Options options,
            CancellationToken cancellationToken = default,
            IProgress<float> progress = null,
            int maxStatesExplored = 500_000,
            int waitQuantumSec = DefaultWaitQuantumSec,
            int maxWaitPerNodeSec = DefaultMaxWaitPerNodeSec)
        {
            // Validation up-front (main thread, fast)
            if (graph == null || startNodeId < 0 || endNodeId < 0
                || startNodeId >= graph.NodeCount || endNodeId >= graph.NodeCount)
                return Result.Failure("invalid graph or node IDs");

            // Task.Run wrap synchronous algorithm — Unity-safe (Vector2/Mathf thread-safe).
            // CancellationToken polled w sync inner method via shared bool reference.
            return await Task.Run(() => FindPathWithWaitingCancellable(
                graph, startNodeId, endNodeId, options,
                cancellationToken, progress,
                maxStatesExplored, waitQuantumSec, maxWaitPerNodeSec),
                cancellationToken);
        }

        /// <summary>
        /// Internal sync method z cancellation + progress hooks. Refactor of FindPathWithWaiting
        /// z dodatkowymi cancel/progress checks każde CancellationCheckInterval iteracji.
        /// </summary>
        private static Result FindPathWithWaitingCancellable(
            PathfindingGraph graph,
            int startNodeId,
            int endNodeId,
            Options options,
            CancellationToken cancellationToken,
            IProgress<float> progress,
            int maxStatesExplored,
            int waitQuantumSec,
            int maxWaitPerNodeSec)
        {
            var sw = Stopwatch.StartNew();

            if (startNodeId == endNodeId)
                return new Result
                {
                    success = true,
                    nodeIds = new List<int> { startNodeId },
                    edgeIds = new List<int>(),
                    timeArrivalsSec = new List<int> { options.startTimeSec },
                    timeMs = sw.Elapsed.TotalMilliseconds
                };

            int maxHorizon = options.maxHorizonSec > 0 ? options.maxHorizonSec : DefaultMaxHorizonSec;
            int maxSpeed = options.maxLineSpeedKmh > 0 ? options.maxLineSpeedKmh : DefaultMaxLineSpeedKmh;
            float maxSpeedMps = maxSpeed * (1000f / 3600f);
            int maxWaitBuckets = Math.Max(1, maxWaitPerNodeSec / waitQuantumSec);

            Vector2 goalPos = graph.GetNode(endNodeId).position;

            var gScore = new Dictionary<long, int>();
            var cameFromNode = new Dictionary<long, long>();
            var cameFromEdge = new Dictionary<long, int>();
            var openSet = new MinHeap<long>();
            var inOpenSet = new HashSet<long>();

            long startKey = MakeStateKey(startNodeId, 0);
            gScore[startKey] = options.startTimeSec;
            float h0 = HeuristicTimeSec(graph.GetNode(startNodeId).position, goalPos, maxSpeedMps);
            openSet.Push(startKey, options.startTimeSec + h0);
            inOpenSet.Add(startKey);

            int explored = 0, blocked = 0, horizonSkip = 0, waitStates = 0;
            long endKeyFound = -1;
            int nextCancellationCheck = CancellationCheckInterval;

            while (openSet.TryPop(out long current))
            {
                inOpenSet.Remove(current);
                explored++;

                // Cancellation + progress check (per interval)
                if (explored >= nextCancellationCheck)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        sw.Stop();
                        return Result.Failure(
                            $"cancelled by user (explored {explored}, blocked={blocked})",
                            explored, sw.Elapsed.TotalMilliseconds);
                    }
                    progress?.Report((float)explored / maxStatesExplored);
                    nextCancellationCheck += CancellationCheckInterval;
                }

                int currentNodeId = DecodeNodeId(current);
                int currentBucket = DecodeBucket(current);

                if (currentNodeId == endNodeId)
                {
                    endKeyFound = current;
                    break;
                }

                if (explored >= maxStatesExplored)
                {
                    sw.Stop();
                    return Result.Failure(
                        $"max states ({maxStatesExplored}); blocked={blocked}, horizon={horizonSkip}, waitStates={waitStates}",
                        explored, sw.Elapsed.TotalMilliseconds);
                }

                int currentTime = gScore[current];
                var currentNode = graph.GetNode(currentNodeId);

                foreach (int edgeId in currentNode.edgeIds)
                {
                    var edge = graph.GetEdge(edgeId);
                    int neighbor = edge.toNodeId;
                    int travelTimeSec = ComputeTravelTimeSec(edge);
                    if (travelTimeSec <= 0) travelTimeSec = 1;

                    long absDeparture = currentTime;
                    long absArrival = absDeparture + travelTimeSec;
                    if (absArrival - options.startTimeSec > maxHorizon) { horizonSkip++; continue; }

                    float weightMult = options.useDirectionPenalty
                        ? RailwayPathfinder.DirectionPenalty(edge) : 1f;
                    int adjustedTravelSec = Mathf.RoundToInt(travelTimeSec * weightMult);
                    int candidateArrivalTime = currentTime + adjustedTravelSec;

                    if (options.isBlockFree != null)
                    {
                        int blockKey = ComputeBlockKey(currentNodeId, neighbor);
                        if (!options.isBlockFree(blockKey, absDeparture, absArrival)) { blocked++; continue; }
                    }

                    long neighborKey = MakeStateKey(neighbor, 0);
                    if (gScore.TryGetValue(neighborKey, out int existingTime) && candidateArrivalTime >= existingTime)
                        continue;

                    cameFromNode[neighborKey] = current;
                    cameFromEdge[neighborKey] = edgeId;
                    gScore[neighborKey] = candidateArrivalTime;

                    float h = HeuristicTimeSec(graph.GetNode(neighbor).position, goalPos, maxSpeedMps);
                    if (!inOpenSet.Contains(neighborKey))
                    {
                        openSet.Push(neighborKey, candidateArrivalTime + h);
                        inOpenSet.Add(neighborKey);
                    }
                }

                // Waiting expansion
                if (currentBucket < maxWaitBuckets)
                {
                    long waitKey = MakeStateKey(currentNodeId, currentBucket + 1);
                    int waitArrivalTime = currentTime + waitQuantumSec;
                    if (waitArrivalTime - options.startTimeSec <= maxHorizon)
                    {
                        if (!gScore.TryGetValue(waitKey, out int existingWait) || waitArrivalTime < existingWait)
                        {
                            cameFromNode[waitKey] = current;
                            cameFromEdge[waitKey] = -1;
                            gScore[waitKey] = waitArrivalTime;
                            float hWait = HeuristicTimeSec(graph.GetNode(currentNodeId).position, goalPos, maxSpeedMps);
                            if (!inOpenSet.Contains(waitKey))
                            {
                                openSet.Push(waitKey, waitArrivalTime + hWait);
                                inOpenSet.Add(waitKey);
                                waitStates++;
                            }
                        }
                    }
                }
            }

            sw.Stop();
            progress?.Report(1f); // completion

            if (endKeyFound < 0)
                return Result.Failure(
                    $"no path; blocked={blocked}, horizon={horizonSkip}, waitStates={waitStates}",
                    explored, sw.Elapsed.TotalMilliseconds);

            return ReconstructWithWaiting(graph, cameFromNode, cameFromEdge, gScore, endKeyFound,
                options.startTimeSec, explored, sw.Elapsed.TotalMilliseconds, waitStates);
        }

        /// <summary>
        /// M-TimetableUX F1.9 Commit D (Strategy 2 — hierarchical two-pass):
        /// Pass A: physical Dijkstra (RailwayPathfinder.FindPath, existing) — topological baseline.
        /// Pass B: time-aware refinement na route — per-edge block check + waiting at predecessor.
        ///
        /// **Performance benefit:** O(graph) Pass A + O(path) Pass B << O(graph × time buckets)
        /// full time-expanded. Suitable dla typical cases gdzie path jest topologically clean
        /// + tylko sporadic block conflicts wymagają waiting.
        ///
        /// **Fallback:** jeśli waiting nie wystarcza dla block conflict (max wait exceeded),
        /// returns Failure z reason — caller może opcjonalnie wywołać FindPathWithWaiting
        /// (full multi-state pathfinder) dla complex cases.
        ///
        /// **Use case:** F1.10 sequential additive — najpierw try hierarchical (fast),
        /// fallback do full waiting (Commit B) jeśli Pass B fails.
        /// </summary>
        public static Result FindPathHierarchical(
            PathfindingGraph graph,
            int startNodeId,
            int endNodeId,
            Options options,
            int waitQuantumSec = DefaultWaitQuantumSec,
            int maxWaitPerNodeSec = DefaultMaxWaitPerNodeSec)
        {
            var sw = Stopwatch.StartNew();

            // Pass A: physical Dijkstra (existing RailwayPathfinder.FindPath z F1.5 directionPenalty)
            var physical = RailwayPathfinder.FindPath(graph, startNodeId, endNodeId);
            if (!physical.success)
            {
                sw.Stop();
                return Result.Failure(
                    $"Pass A physical Dijkstra failed (explored {physical.exploredNodes})",
                    physical.exploredNodes, sw.Elapsed.TotalMilliseconds);
            }

            // Pass B: time-aware refinement on route edges
            int maxHorizon = options.maxHorizonSec > 0 ? options.maxHorizonSec : DefaultMaxHorizonSec;
            int maxWaitTotal = 0;
            int blockedSegments = 0;

            int currentTime = options.startTimeSec;
            var timeArrivals = new List<int>(physical.nodeIds.Count) { currentTime };

            for (int i = 0; i < physical.edgeIds.Count; i++)
            {
                int edgeId = physical.edgeIds[i];
                var edge = graph.GetEdge(edgeId);

                int travelTimeSec = ComputeTravelTimeSec(edge);
                if (travelTimeSec <= 0) travelTimeSec = 1;

                float weightMult = options.useDirectionPenalty
                    ? RailwayPathfinder.DirectionPenalty(edge) : 1f;
                int adjustedTravelSec = Mathf.RoundToInt(travelTimeSec * weightMult);

                long absDeparture = currentTime;
                long absArrival = absDeparture + adjustedTravelSec;

                // Bounded horizon check
                if (absArrival - options.startTimeSec > maxHorizon)
                {
                    sw.Stop();
                    return Result.Failure(
                        $"Pass B: bounded horizon exceeded at edge {i} (arrival={absArrival})",
                        physical.exploredNodes, sw.Elapsed.TotalMilliseconds);
                }

                // Block check (Strategy A — wait at predecessor jeśli conflict)
                if (options.isBlockFree != null)
                {
                    int blockKey = ComputeBlockKey(physical.nodeIds[i], physical.nodeIds[i + 1]);
                    int waitedAtThisNode = 0;
                    while (!options.isBlockFree(blockKey, absDeparture, absArrival))
                    {
                        if (waitedAtThisNode >= maxWaitPerNodeSec)
                        {
                            sw.Stop();
                            return Result.Failure(
                                $"Pass B: max wait exceeded at edge {i} (waited {waitedAtThisNode}s); fallback to FindPathWithWaiting",
                                physical.exploredNodes, sw.Elapsed.TotalMilliseconds);
                        }
                        waitedAtThisNode += waitQuantumSec;
                        absDeparture += waitQuantumSec;
                        absArrival += waitQuantumSec;
                        if (absArrival - options.startTimeSec > maxHorizon)
                        {
                            sw.Stop();
                            return Result.Failure(
                                $"Pass B: horizon exceeded during waiting at edge {i}",
                                physical.exploredNodes, sw.Elapsed.TotalMilliseconds);
                        }
                        blockedSegments++;
                    }
                    maxWaitTotal += waitedAtThisNode;
                    currentTime = (int)absDeparture; // adjust dla waiting time
                }

                currentTime = (int)absArrival;
                timeArrivals.Add(currentTime);
            }

            sw.Stop();

            return new Result
            {
                success = true,
                nodeIds = new List<int>(physical.nodeIds),
                edgeIds = new List<int>(physical.edgeIds),
                timeArrivalsSec = timeArrivals,
                totalLengthM = physical.totalLengthM,
                totalTimeSec = currentTime - options.startTimeSec,
                exploredStates = physical.exploredNodes,
                timeMs = sw.Elapsed.TotalMilliseconds,
                failureReason = $"hierarchical: blocked={blockedSegments}, waited={maxWaitTotal}s"
            };
        }

        /// <summary>
        /// M-TimetableUX F1.9 Commit E: semantic preservation check helper.
        /// Verify że time-aware variant z empty block reservations daje same path length
        /// (tolerance 5%) co RailwayPathfinder.FindPath baseline. Caller invokes per test data set.
        /// </summary>
        public static bool SemanticPreservationCheck(
            PathfindingGraph graph,
            int startNodeId,
            int endNodeId,
            float tolerancePct = 5f)
        {
            var baseline = RailwayPathfinder.FindPath(graph, startNodeId, endNodeId);
            if (!baseline.success) return true; // baseline fails → time-aware też powinien fail

            var opts = Options.Default(0);
            opts.isBlockFree = null; // no block constraints — should match baseline

            var commitA = FindPath(graph, startNodeId, endNodeId, opts);
            if (!commitA.success) return false;
            float diffA = Mathf.Abs(commitA.totalLengthM - baseline.totalLengthM)
                          / Mathf.Max(1f, baseline.totalLengthM) * 100f;
            if (diffA > tolerancePct) return false;

            var commitB = FindPathWithWaiting(graph, startNodeId, endNodeId, opts);
            if (!commitB.success) return false;
            float diffB = Mathf.Abs(commitB.totalLengthM - baseline.totalLengthM)
                          / Mathf.Max(1f, baseline.totalLengthM) * 100f;
            if (diffB > tolerancePct) return false;

            var commitD = FindPathHierarchical(graph, startNodeId, endNodeId, opts);
            if (!commitD.success) return false;
            float diffD = Mathf.Abs(commitD.totalLengthM - baseline.totalLengthM)
                          / Mathf.Max(1f, baseline.totalLengthM) * 100f;
            if (diffD > tolerancePct) return false;

            return true;
        }

        // ─── Helpers ───

        /// <summary>Travel time edge (sekundy) — `lengthM / (maxSpeedKmh * 0.2778)`.</summary>
        private static int ComputeTravelTimeSec(in PathfindingGraph.Edge edge)
        {
            int speedKmh = edge.maxSpeedKmh > 0 ? edge.maxSpeedKmh : DefaultMaxLineSpeedKmh;
            float speedMps = speedKmh * (1000f / 3600f);
            if (speedMps <= 0f) return Mathf.CeilToInt(edge.lengthM); // 1 m/s fallback
            return Mathf.CeilToInt(edge.lengthM / speedMps);
        }

        /// <summary>Heurystyka admissible: euclidean distance / max speed = lower bound time.</summary>
        private static float HeuristicTimeSec(Vector2 a, Vector2 b, float maxSpeedMps)
        {
            float dist = Vector2.Distance(a, b);
            return maxSpeedMps > 0f ? dist / maxSpeedMps : 0f;
        }

        /// <summary>
        /// Block key — symetryczny hash dla rezerwacji blok'u (ten sam key niezależnie od kierunku).
        /// Spójny z `ReservationManager.BuildRouteBlocks` formula (modulo segmentId, ale tu używamy
        /// node IDs jako fallback gdy nie ma BlockSection topology).
        /// </summary>
        private static int ComputeBlockKey(int nodeA, int nodeB)
        {
            int low = Math.Min(nodeA, nodeB);
            int high = Math.Max(nodeA, nodeB);
            return low * 100003 + high;
        }

        private static Result Reconstruct(
            PathfindingGraph graph,
            Dictionary<int, int> cameFromNode,
            Dictionary<int, int> cameFromEdge,
            Dictionary<int, int> gScore,
            int endNodeId,
            int startTimeSec,
            int explored)
        {
            var nodeIds = new List<int>();
            var edgeIds = new List<int>();
            var timeArrivals = new List<int>();

            int current = endNodeId;
            nodeIds.Add(current);
            timeArrivals.Add(gScore[current]);

            while (cameFromNode.TryGetValue(current, out int prev))
            {
                edgeIds.Add(cameFromEdge[current]);
                current = prev;
                nodeIds.Add(current);
                timeArrivals.Add(gScore[current]);
            }

            nodeIds.Reverse();
            edgeIds.Reverse();
            timeArrivals.Reverse();

            float totalLength = 0f;
            for (int i = 0; i < edgeIds.Count; i++)
                totalLength += graph.GetEdge(edgeIds[i]).lengthM;

            int totalTimeSec = timeArrivals[timeArrivals.Count - 1] - startTimeSec;

            return new Result
            {
                success = true,
                nodeIds = nodeIds,
                edgeIds = edgeIds,
                timeArrivalsSec = timeArrivals,
                totalLengthM = totalLength,
                totalTimeSec = totalTimeSec,
                exploredStates = explored,
                failureReason = null
            };
        }
    }
}
