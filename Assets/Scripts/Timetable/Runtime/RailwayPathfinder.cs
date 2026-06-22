using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// A* pathfinding po PathfindingGraph (M-TimetableUX 2026-05-11 refactor):
    /// **Waga krawędzi = travel time** (lengthM / speedMps) × directionPenalty.
    /// Heurystyka admissible = euclidean / maxLineSpeedMps (time lower bound).
    ///
    /// **Time-weighted (zamiast length-weighted):** pathfinder preferuje szybsze trasy
    /// (główne magistrale Vmax 160 km/h) nad krótsze geographicznie (sidelines Vmax 60 km/h).
    /// Per user request 2026-05-11: "algorytm powinien obliczać czas przejazdu na podstawie
    /// danych prędkości jakie są wpisane w tory".
    /// </summary>
    public static class RailwayPathfinder
    {
        /// <summary>
        /// M-TimetableUX F1.5: kara za jazdę "wrong direction" na linii dwutorowej z tagiem
        /// `railway:preferred_direction`. Soft penalty (×5, nie ∞).
        /// </summary>
        public const float WrongDirectionPenalty = 5.0f;

        /// <summary>Max line speed (km/h) dla heurystyki admissible time. Konserwatywnie 200 (EIP).</summary>
        public const float HeuristicMaxSpeedKmh = 200f;
        /// <summary>Fallback speed (km/h) gdy edge.maxSpeedKmh = 0 lub missing. 60 km/h = service track typical.</summary>
        public const int DefaultEdgeSpeedKmh = 60;
        // ── Global counters do diagnostyki bottleneck'u (resetable) ──
        /// <summary>Łączna liczba wywołań FindPath od ostatniego ResetCounters.</summary>
        public static int CallCount;
        /// <summary>Łączna liczba eksplorowanych nodów we wszystkich wywołaniach.</summary>
        public static long ExploredTotal;
        /// <summary>Łączny czas A* loop (ms) we wszystkich wywołaniach.</summary>
        public static double AStarTotalMs;
        /// <summary>Łączny czas FindPath total (ms) — z alokacjami i Reconstruct.</summary>
        public static double TotalTimeMs;
        /// <summary>Liczba wywołań które nie znalazły ścieżki (success=false).</summary>
        public static int FailedCalls;

        public static void ResetCounters()
        {
            CallCount = 0; ExploredTotal = 0;
            AStarTotalMs = 0; TotalTimeMs = 0;
            FailedCalls = 0;
        }

        /// <summary>Wynik pathfindingu — sekwencja węzłów i krawędzi oraz łączna długość.</summary>
        public struct PathResult
        {
            public bool success;
            public List<int> nodeIds;        // sekwencja węzłów od start do end
            public List<int> edgeIds;        // sekwencja krawędzi między kolejnymi węzłami
            public float totalLengthM;
            public int exploredNodes;        // do debugowania wydajności

            // ── Performance diagnostics (wypełniane zawsze, koszt pomijalny) ──
            /// <summary>Peak rozmiar openSet (MinHeap) — wskazuje ile stan queue musiał trzymać.</summary>
            public int maxOpenSetSize;
            /// <summary>Ile razy znaleziono lepszą ścieżkę do już odwiedzonego sąsiada (duplikat w heapie).</summary>
            public int relaxations;
            /// <summary>Łączny czas samej pętli A* (bez Reconstruct) w milisekundach.</summary>
            public double timeAStarMs;
            /// <summary>Czas rekonstrukcji trasy (odwracanie cameFrom) w milisekundach.</summary>
            public double timeReconstructMs;
            /// <summary>Łączny czas FindPath w milisekundach (AStar + Reconstruct + overhead).</summary>
            public double totalTimeMs;

            public static PathResult Failure(int explored) => new()
            {
                success = false,
                nodeIds = new List<int>(),
                edgeIds = new List<int>(),
                totalLengthM = 0f,
                exploredNodes = explored
            };
        }

        /// <summary>
        /// Znajduje trasę między dwoma węzłami w PathfindingGraph.
        /// Ograniczenia: maxNodesExplored zapobiega pożarowi czasu na ogromnym grafie.
        /// Opcjonalny `blockedEdges` HashSet — edges które są wyłączone (używane przez K-shortest
        /// paths Yen-style algorithm żeby wymusić alternatywne trasy).
        /// </summary>
        public static PathResult FindPath(PathfindingGraph graph, int startNodeId, int endNodeId,
                                          int maxNodesExplored = 500_000,
                                          HashSet<int> blockedEdges = null)
        {
            var swTotal = Stopwatch.StartNew();

            if (graph == null || startNodeId < 0 || endNodeId < 0
                || startNodeId >= graph.NodeCount || endNodeId >= graph.NodeCount)
                return PathResult.Failure(0);

            if (startNodeId == endNodeId)
                return new PathResult
                {
                    success = true,
                    nodeIds = new List<int> { startNodeId },
                    edgeIds = new List<int>(),
                    totalLengthM = 0f,
                    exploredNodes = 0
                };

            int n = graph.NodeCount;
            Vector2 goalPos = graph.GetNode(endNodeId).position;

            // gScore: najkrótsza znana odległość od startu do węzła
            var gScore = new Dictionary<int, float>();
            // cameFromEdge: krawędź która doprowadziła do węzła (do rekonstrukcji trasy)
            var cameFromEdge = new Dictionary<int, int>();
            // cameFromNode: poprzedni węzeł na ścieżce
            var cameFromNode = new Dictionary<int, int>();

            var openSet = new MinHeap<int>();
            var inOpenSet = new HashSet<int>();

            gScore[startNodeId] = 0f;
            openSet.Push(startNodeId, Heuristic(graph.GetNode(startNodeId).position, goalPos));
            inOpenSet.Add(startNodeId);

            int explored = 0;
            int maxOpenSetSize = 1;
            int relaxations = 0;

            var swAStar = Stopwatch.StartNew();
            while (openSet.TryPop(out int current))
            {
                inOpenSet.Remove(current);
                explored++;

                if (current == endNodeId)
                {
                    swAStar.Stop();
                    var swReconstruct = Stopwatch.StartNew();
                    var result = Reconstruct(graph, cameFromNode, cameFromEdge, endNodeId, explored);
                    swReconstruct.Stop();
                    swTotal.Stop();
                    result.maxOpenSetSize = maxOpenSetSize;
                    result.relaxations = relaxations;
                    result.timeAStarMs = swAStar.Elapsed.TotalMilliseconds;
                    result.timeReconstructMs = swReconstruct.Elapsed.TotalMilliseconds;
                    result.totalTimeMs = swTotal.Elapsed.TotalMilliseconds;
                    CallCount++; ExploredTotal += explored;
                    AStarTotalMs += result.timeAStarMs; TotalTimeMs += result.totalTimeMs;
                    return result;
                }

                if (explored >= maxNodesExplored)
                {
                    swAStar.Stop(); swTotal.Stop();
                    var f = PathResult.Failure(explored);
                    f.maxOpenSetSize = maxOpenSetSize;
                    f.relaxations = relaxations;
                    f.timeAStarMs = swAStar.Elapsed.TotalMilliseconds;
                    f.totalTimeMs = swTotal.Elapsed.TotalMilliseconds;
                    CallCount++; ExploredTotal += explored; FailedCalls++;
                    AStarTotalMs += f.timeAStarMs; TotalTimeMs += f.totalTimeMs;
                    return f;
                }

                var currentNode = graph.GetNode(current);
                float currentG = gScore[current];

                foreach (int edgeId in currentNode.edgeIds)
                {
                    // K-shortest paths: skip blocked edges (Yen deviation).
                    if (blockedEdges != null && blockedEdges.Contains(edgeId)) continue;
                    var edge = graph.GetEdge(edgeId);
                    int neighbor = edge.toNodeId;

                    // M-TimetableUX 2026-05-11 refactor: cost = travel TIME (sekundy) zamiast length.
                    // Time-weighted: pathfinder preferuje szybsze trasy (high-speed mainlines)
                    // nad krótsze geographicznie (slow sidelines). F1.5 directionPenalty zachowany.
                    int speedKmh = edge.maxSpeedKmh > 0 ? edge.maxSpeedKmh : DefaultEdgeSpeedKmh;
                    float speedMps = speedKmh * (1000f / 3600f);
                    float travelTimeSec = edge.lengthM / Mathf.Max(0.1f, speedMps);
                    float tentativeG = currentG + travelTimeSec * DirectionPenalty(edge);
                    if (gScore.TryGetValue(neighbor, out var existingG) && tentativeG >= existingG)
                        continue;

                    relaxations++;
                    cameFromNode[neighbor] = current;
                    cameFromEdge[neighbor] = edgeId;
                    gScore[neighbor] = tentativeG;

                    float fScore = tentativeG + Heuristic(graph.GetNode(neighbor).position, goalPos);
                    if (!inOpenSet.Contains(neighbor))
                    {
                        openSet.Push(neighbor, fScore);
                        inOpenSet.Add(neighbor);
                        if (inOpenSet.Count > maxOpenSetSize) maxOpenSetSize = inOpenSet.Count;
                    }
                }
            }

            swAStar.Stop(); swTotal.Stop();
            var fail = PathResult.Failure(explored);
            fail.maxOpenSetSize = maxOpenSetSize;
            fail.relaxations = relaxations;
            fail.timeAStarMs = swAStar.Elapsed.TotalMilliseconds;
            fail.totalTimeMs = swTotal.Elapsed.TotalMilliseconds;
            CallCount++; ExploredTotal += explored; FailedCalls++;
            AStarTotalMs += fail.timeAStarMs; TotalTimeMs += fail.totalTimeMs;
            return fail;
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11: K-shortest paths z filter max length ratio.
        ///
        /// Algorytm Yen-inspired (uproszczony, edge-removal):
        /// 1. Znajdź najkrótszą trasę P0 (FindPath).
        /// 2. Dla każdego iteracja k=1..K-1:
        ///    a. Pick edge z poprzedniej P_{k-1} do zablokowania (deviation point).
        ///    b. FindPath z blockedEdges=cumulative blocked.
        ///    c. Jeśli alt.totalLengthM > P0.totalLengthM × maxLengthRatio → stop.
        ///    d. Jeśli alt.edgeIds duplikat poprzednich → skip + try next deviation point.
        ///    e. Add alt do results, set as next iteration base.
        /// 3. Return results.
        ///
        /// Deviation point heuristyka: rotujemy przez middle, 1/4, 3/4 edges P_{k-1}.
        /// Próbujemy do K*5 attempts żeby znaleźć K unique alternatives w 1.3× budget.
        /// </summary>
        public static List<PathResult> FindKShortestPaths(
            PathfindingGraph graph, int startNodeId, int endNodeId,
            int k = 3, float maxLengthRatio = 1.3f, int maxNodesExplored = 2_000_000)
        {
            var results = new List<PathResult>();
            if (graph == null || k < 1) return results;

            var first = FindPath(graph, startNodeId, endNodeId, maxNodesExplored);
            if (!first.success) return results;
            results.Add(first);
            if (k == 1) return results;

            float maxAcceptableLength = first.totalLengthM * maxLengthRatio;
            var blockedEdges = new HashSet<int>();
            int maxAttempts = k * 6;
            int attempts = 0;

            // Pool of (path, edgeIdx) deviation points to try
            var deviationCandidates = new List<(PathResult basePath, int edgeIdx)>();
            void EnqueueDeviationsFromPath(PathResult p)
            {
                if (p.edgeIds == null || p.edgeIds.Count == 0) return;
                int len = p.edgeIds.Count;
                // Try middle first, then quartiles, then start/end
                int[] indices = new int[] { len / 2, len / 4, 3 * len / 4, len / 8, 7 * len / 8 };
                foreach (int idx in indices)
                {
                    if (idx >= 0 && idx < len) deviationCandidates.Add((p, idx));
                }
            }

            EnqueueDeviationsFromPath(first);

            while (results.Count < k && deviationCandidates.Count > 0 && attempts < maxAttempts)
            {
                attempts++;
                var (basePath, edgeIdx) = deviationCandidates[0];
                deviationCandidates.RemoveAt(0);
                if (basePath.edgeIds == null || edgeIdx >= basePath.edgeIds.Count) continue;

                int blockEdge = basePath.edgeIds[edgeIdx];
                if (blockedEdges.Contains(blockEdge)) continue;
                blockedEdges.Add(blockEdge);

                var alt = FindPath(graph, startNodeId, endNodeId, maxNodesExplored, blockedEdges);
                if (!alt.success)
                {
                    // Unblock if no path found — try inny deviation point next iteration
                    blockedEdges.Remove(blockEdge);
                    continue;
                }
                if (alt.totalLengthM > maxAcceptableLength)
                {
                    // Path zbyt długi — unblock i kontynuuj
                    blockedEdges.Remove(blockEdge);
                    continue;
                }

                // Unique check — alt musi mieć inny edgeIds set niż wszystkie poprzednie
                if (IsPathDuplicate(alt, results))
                {
                    // Same path mimo block — keep blocked, try inny deviation
                    continue;
                }

                results.Add(alt);
                EnqueueDeviationsFromPath(alt);
            }

            return results;
        }

        private static bool IsPathDuplicate(PathResult candidate, List<PathResult> existing)
        {
            if (candidate.edgeIds == null) return false;
            foreach (var p in existing)
            {
                if (p.edgeIds == null || p.edgeIds.Count != candidate.edgeIds.Count) continue;
                bool same = true;
                for (int i = 0; i < p.edgeIds.Count; i++)
                {
                    if (p.edgeIds[i] != candidate.edgeIds[i]) { same = false; break; }
                }
                if (same) return true;
            }
            return false;
        }

        /// <summary>Wariant z wskazywaniem pozycji startowej/końcowej zamiast nodeIds — wygodne dla klikania na mapie.</summary>
        public static PathResult FindPathByPosition(PathfindingGraph graph, Vector2 startPos, Vector2 endPos,
                                                    float snapRadiusM = 200f, int maxNodesExplored = 500_000)
        {
            int start = graph.FindNearestNode(startPos, snapRadiusM);
            int end = graph.FindNearestNode(endPos, snapRadiusM);
            if (start < 0 || end < 0) return PathResult.Failure(0);
            return FindPath(graph, start, end, maxNodesExplored);
        }

        /// <summary>
        /// Routing przez sekwencję waypointów — łańcuch A* calls.
        /// Każdy segment: waypoints[i] → waypoints[i+1]. Splajsuje wyniki.
        /// Używane do wymuszenia trasy przez konkretne tory stacyjne.
        /// </summary>
        public static PathResult FindPathViaWaypoints(PathfindingGraph graph, List<int> waypointNodeIds,
                                                      int maxNodesExplored = 500_000)
        {
            if (graph == null || waypointNodeIds == null || waypointNodeIds.Count < 2)
                return PathResult.Failure(0);

            var allNodeIds = new List<int>();
            var allEdgeIds = new List<int>();
            float totalLength = 0f;
            int totalExplored = 0;

            for (int i = 0; i < waypointNodeIds.Count - 1; i++)
            {
                int from = waypointNodeIds[i];
                int to = waypointNodeIds[i + 1];
                if (from == to) continue;

                var segment = FindPath(graph, from, to, maxNodesExplored);
                if (!segment.success)
                    return PathResult.Failure(totalExplored + segment.exploredNodes);

                // Splice: pomiń pierwszy node segmentu (duplikat z końca poprzedniego)
                if (allNodeIds.Count == 0)
                {
                    allNodeIds.AddRange(segment.nodeIds);
                }
                else
                {
                    for (int j = 1; j < segment.nodeIds.Count; j++)
                        allNodeIds.Add(segment.nodeIds[j]);
                }
                allEdgeIds.AddRange(segment.edgeIds);

                totalLength += segment.totalLengthM;
                totalExplored += segment.exploredNodes;
            }

            if (allNodeIds.Count < 2)
                return PathResult.Failure(totalExplored);

            return new PathResult
            {
                success = true,
                nodeIds = allNodeIds,
                edgeIds = allEdgeIds,
                totalLengthM = totalLength,
                exploredNodes = totalExplored
            };
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11: heurystyka admissible w time domain (sekundy).
        /// Lower bound time = euclidean distance / maxSpeed (najszybsza możliwa trasa).
        /// Real travel time ≥ heuristic bo: real distance ≥ euclidean (triangle inequality)
        /// + real speed ≤ maxSpeed → real time = real_dist / real_speed ≥ euclidean / maxSpeed.
        /// </summary>
        private static float Heuristic(Vector2 a, Vector2 b)
        {
            float euclideanM = Vector2.Distance(a, b);
            float maxSpeedMps = HeuristicMaxSpeedKmh * (1000f / 3600f);
            return euclideanM / maxSpeedMps;
        }

        /// <summary>
        /// M-TimetableUX F1.5: directionPenalty per edge.
        /// Reads OSM tag `railway:preferred_direction` (`forward` / `backward` / `both` / null).
        /// Compares z `edge.isOsmForward` żeby zdecydować czy jazda zgadza się z preferred direction.
        /// </summary>
        /// <returns>1.0 (no penalty — correct direction lub no tag) lub <see cref="WrongDirectionPenalty"/>.</returns>
        public static float DirectionPenalty(in PathfindingGraph.Edge edge)
        {
            if (edge.metadata == null) return 1.0f;
            if (!edge.metadata.TryGetValue("railway:preferred_direction", out var pd)) return 1.0f;
            if (string.IsNullOrEmpty(pd) || pd == "both") return 1.0f;

            // pd == "forward": jazda OK gdy edge.isOsmForward == true
            // pd == "backward": jazda OK gdy edge.isOsmForward == false
            bool preferredForward = pd == "forward";
            return edge.isOsmForward == preferredForward ? 1.0f : WrongDirectionPenalty;
        }

        private static PathResult Reconstruct(PathfindingGraph graph,
                                              Dictionary<int, int> cameFromNode,
                                              Dictionary<int, int> cameFromEdge,
                                              int endNodeId, int explored)
        {
            var nodeIds = new List<int>();
            var edgeIds = new List<int>();

            int current = endNodeId;
            nodeIds.Add(current);

            while (cameFromNode.TryGetValue(current, out int prev))
            {
                edgeIds.Add(cameFromEdge[current]);
                current = prev;
                nodeIds.Add(current);
            }

            nodeIds.Reverse();
            edgeIds.Reverse();

            // M-TimetableUX F1.5: totalLengthM to real distance (sum edge.lengthM), NIE A* cost
            // (który zawiera directionPenalty dla wrong-direction edges). Caller'om potrzebny
            // physical length (do fizyki, ETA, raportów).
            float totalLength = 0f;
            for (int i = 0; i < edgeIds.Count; i++)
                totalLength += graph.GetEdge(edgeIds[i]).lengthM;

            return new PathResult
            {
                success = true,
                nodeIds = nodeIds,
                edgeIds = edgeIds,
                totalLengthM = totalLength,
                exploredNodes = explored
            };
        }
    }
}
