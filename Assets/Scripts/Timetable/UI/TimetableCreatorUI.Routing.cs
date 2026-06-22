using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    public partial class TimetableCreatorUI
    {
        // PERF diagnostics reset at the start of RefreshStopsList.
        static int _ftCallCount, _ftJsonHits, _ftOsmFallbacks;
        static double _ftJsonMs, _ftOsmMs;

        // Cache station -> platform indices for the active graph.
        static Dictionary<string, HashSet<int>> _stationToPlatformsCache;
        static PathfindingGraph _cachedForGraph;

        private Route BuildRoute()
        {
            var init = TimetableInitializer.Instance;
            if (init == null || _startStation == null || _endStation == null) return null;

            var points = new List<RailwayStation> { _startStation };
            foreach (var wp in _waypoints)
                if (wp != null) points.Add(wp);
            points.Add(_endStation);

            Log.Info($"[TimetableCreator] Route points ({points.Count}): "
                     + string.Join(" -> ", points.ConvertAll(p => p.name)));

            var allNodeIds = new List<int>();
            var segmentBoundaries = new List<int> { 0 };
            float totalLength = 0f;

            int sumExplored = 0, sumRelax = 0, sumMaxHeap = 0;
            double sumAStarMs = 0, sumReconstructMs = 0, sumTotalMs = 0;

            // M-TimetableUX 2026-05-11: zwiększony limit eksploracji 500k → 2M.
            // Polska graf po Union-Find ma 300-500k nodów. Limit 500k bywa wyczerpany na
            // trudnych trasach (Malbork → Szymankowo wskakuje na 500k bez znalezienia ścieżki),
            // mimo że stacje są ~9km od siebie. 2M = exhaustive dla pełnego grafu PL.
            const int CreatorMaxNodesExplored = 2_000_000;
            Log.Info($"[TimetableCreator] Graph total nodes: {init.Graph?.NodeCount ?? 0}, "
                   + $"pathfinder limit: {CreatorMaxNodesExplored:N0}");

            // M-TimetableUX 2026-05-11 v3 (user decision): respect user-defined waypoint order.
            // Multi-candidate retry per segment, ale NIE SKIP gdy fails — hard fail z konkretnym
            // komunikatem żeby user widział co poszło źle. User dał kolejność waypointów →
            // pathfinder MUSI ją przestrzegać.
            for (int i = 0; i < points.Count - 1; i++)
            {
                bool fromIsEndpoint = (i == 0);
                bool toIsEndpoint = (i + 1 == points.Count - 1);
                // A (i=0): _startTrack. C (i+1=last): _endTrack. Waypoints po środku: _waypointTracks.
                string fromTrack = fromIsEndpoint
                    ? _startTrack
                    : ((i > 0 && i - 1 < _waypointTracks.Count) ? _waypointTracks[i - 1] : "");
                string toTrack = toIsEndpoint
                    ? _endTrack
                    : ((i < _waypointTracks.Count) ? _waypointTracks[i] : "");

                var startCands = ResolveCandidates(init.Graph, points[i], fromTrack, fromIsEndpoint);
                var endCands = ResolveCandidates(init.Graph, points[i + 1], toTrack, toIsEndpoint);

                RailwayPathfinder.PathResult bestResult = default;
                bool foundPath = false;
                int attempts = 0;
                int lastStartTried = -1, lastEndTried = -1;

                foreach (int sn in startCands)
                {
                    if (foundPath) break;
                    foreach (int en in endCands)
                    {
                        attempts++;
                        lastStartTried = sn;
                        lastEndTried = en;
                        var r = RailwayPathfinder.FindPath(init.Graph, sn, en, CreatorMaxNodesExplored);
                        if (r.success)
                        {
                            bestResult = r;
                            foundPath = true;
                            Log.Info($"[TimetableCreator]   Segment: {points[i].name} -> {points[i + 1].name}: "
                                   + $"{r.totalLengthM / 1000f:F1} km, {r.nodeIds.Count} nodes "
                                   + $"(attempt {attempts}/{startCands.Count}×{endCands.Count}, start={sn}, end={en}) | "
                                   + $"PERF explored={r.exploredNodes} relax={r.relaxations} "
                                   + $"peakHeap={r.maxOpenSetSize} "
                                   + $"aStar={r.timeAStarMs:F0}ms reconstruct={r.timeReconstructMs:F1}ms "
                                   + $"total={r.totalTimeMs:F0}ms");
                            break;
                        }
                    }
                }

                if (!foundPath)
                {
                    // HARD FAIL: user-defined order has priority — no skip allowed.
                    // Diagnostic: pokazujemy ostatnią próbę żeby user mógł sprawdzić.
                    string lastStartInfo = lastStartTried >= 0
                        ? $"node {lastStartTried} (krawedzi: {init.Graph.GetNode(lastStartTried).edgeIds.Count})"
                        : "n/d";
                    string lastEndInfo = lastEndTried >= 0
                        ? $"node {lastEndTried} (krawedzi: {init.Graph.GetNode(lastEndTried).edgeIds.Count})"
                        : "n/d";
                    Log.Warn($"[TimetableCreator] Brak sciezki {points[i].name} -> {points[i + 1].name}\n"
                           + $"  attempts: {attempts} ({startCands.Count}×{endCands.Count} kombinacji)\n"
                           + $"  last start: {lastStartInfo}\n"
                           + $"  last end: {lastEndInfo}\n"
                           + $"  -> mozliwe przyczyny: izolowana komponenta grafu w binarce OSM, "
                           + $"łańcuch waypointów niepołaczony torami, "
                           + $"użyj 'Wybierz tor' obok waypointa albo usuń ten waypoint");
                    return null;
                }

                sumExplored += bestResult.exploredNodes;
                sumRelax += bestResult.relaxations;
                if (bestResult.maxOpenSetSize > sumMaxHeap) sumMaxHeap = bestResult.maxOpenSetSize;
                sumAStarMs += bestResult.timeAStarMs;
                sumReconstructMs += bestResult.timeReconstructMs;
                sumTotalMs += bestResult.totalTimeMs;

                int startIdx = (i == 0) ? 0 : 1;
                for (int j = startIdx; j < bestResult.nodeIds.Count; j++)
                    allNodeIds.Add(bestResult.nodeIds[j]);
                totalLength += bestResult.totalLengthM;
                segmentBoundaries.Add(allNodeIds.Count);
            }

            int graphNodes = init.Graph?.NodeCount ?? 0;
            int graphEdges = init.Graph?.EdgeCount ?? 0;
            double ratio = sumExplored > 0 && graphNodes > 0
                ? (double)sumExplored / graphNodes * 100.0 : 0.0;
            double throughput = sumAStarMs > 0 ? sumExplored / (sumAStarMs / 1000.0) : 0.0;
            Log.Info($"[TimetableCreator] === PATHFINDING SUMMARY === "
                     + $"graph={graphNodes} nodes / {graphEdges} edges, "
                     + $"segments={points.Count - 1}, "
                     + $"explored_total={sumExplored} ({ratio:F1}% grafu), "
                     + $"relax_total={sumRelax}, peakHeap={sumMaxHeap}, "
                     + $"throughput={throughput:F0} nodes/s, "
                     + $"aStar={sumAStarMs:F0}ms reconstruct={sumReconstructMs:F1}ms "
                     + $"TOTAL={sumTotalMs:F0}ms ({sumTotalMs / 1000.0:F1}s)");

            var route = new Route
            {
                name = $"{_startStation.name} \u2192 {_endStation.name}",
                nodeIds = allNodeIds,
                totalLengthM = totalLength
            };

            // M-TimetableUX 2026-05-11: ekstrakcja linii kolejowych (OSM `ref` tag) z edges trasy.
            // Lista unique refs w kolejno\u015bci pojawienia si\u0119. Display w _routeLinesText.
            UpdateRouteLinesDisplay(allNodeIds, init);

            FindStationsPerSegment(route, allNodeIds, segmentBoundaries, init);

            for (int i = route.stations.Count - 1; i > 0; i--)
                if (route.stations[i].stationName == route.stations[i - 1].stationName)
                    route.stations.RemoveAt(i);

            // M-TimetableUX F1.6 polish: show ghost station markers dla off-path stations near route
            var offPath = OffPathStationsDetector.GetOffPathStations(route, init);
            if (offPath.Count > 0)
            {
                GhostStationMarkersOverlay.EnsureExists().ShowMarkers(offPath);
            }
            else
            {
                if (GhostStationMarkersOverlay.Instance != null)
                    GhostStationMarkersOverlay.Instance.Clear();
            }

            // M-TimetableUX F1.3 polish: show waypoint markers dla explicit waypoints
            // (click-to-remove flow). Blue rings żeby distinguish od yellow ghost markers.
            if (_waypoints.Count > 0)
                WaypointMarkersOverlay.EnsureExists().ShowWaypoints(_waypoints);
            else if (WaypointMarkersOverlay.Instance != null)
                WaypointMarkersOverlay.Instance.Clear();

            return route;
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11: ekstrakcja linii kolejowych z trasy (OSM `ref` tag).
        /// Iteruje pary kolejnych node IDs, znajduje edge między nimi, zbiera unique `ref`
        /// w kolejności pojawienia się. Display w _routeLinesText jako "lk9 → lk204" format.
        /// </summary>
        private void UpdateRouteLinesDisplay(List<int> allNodeIds, TimetableInitializer init)
        {
            if (_routeLinesText == null || allNodeIds == null || allNodeIds.Count < 2 || init?.Graph == null)
                return;

            var graph = init.Graph;
            // Per linia: sumaryczna długość + pierwsza pozycja na trasie (chronologiczne sort).
            var lineDistances = new Dictionary<string, float>();
            var firstAppearance = new Dictionary<string, int>();
            float totalLength = 0f;

            for (int i = 0; i < allNodeIds.Count - 1; i++)
            {
                int nA = allNodeIds[i];
                int nB = allNodeIds[i + 1];
                if (nA < 0 || nA >= graph.NodeCount) continue;
                if (nB < 0 || nB >= graph.NodeCount) continue;

                var nodeA = graph.GetNode(nA);
                if (nodeA.edgeIds == null) continue;
                foreach (int eid in nodeA.edgeIds)
                {
                    if (eid < 0 || eid >= graph.EdgeCount) continue;
                    var edge = graph.GetEdge(eid);
                    if (edge.toNodeId != nB) continue;
                    if (edge.metadata == null) break;

                    string refTag = null;
                    if (edge.metadata.TryGetValue("railway:line_ref", out var lineRef) && !string.IsNullOrEmpty(lineRef))
                        refTag = lineRef;
                    else if (edge.metadata.TryGetValue("ref", out var simpleRef) && !string.IsNullOrEmpty(simpleRef))
                        refTag = simpleRef;
                    if (refTag == null) break;

                    totalLength += edge.lengthM;
                    foreach (var r in refTag.Split(';'))
                    {
                        var trimmed = r.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;
                        if (!IsValidLineRef(trimmed)) continue;
                        if (!lineDistances.TryGetValue(trimmed, out var d)) d = 0f;
                        lineDistances[trimmed] = d + edge.lengthM;
                        if (!firstAppearance.ContainsKey(trimmed))
                            firstAppearance[trimmed] = i;
                    }
                    break;
                }
            }

            if (lineDistances.Count == 0 || totalLength <= 0.1f)
            {
                _routeLinesText.text = "Linie: (brak ref tagów)";
                return;
            }

            // Filter ≥5% udział, sortuj by first appearance (chronologicznie wzdłuż trasy)
            const float MinShareFraction = 0.05f;
            var significant = new List<string>();
            foreach (var kv in lineDistances)
                if (kv.Value / totalLength >= MinShareFraction)
                    significant.Add(kv.Key);

            if (significant.Count == 0)
            {
                var top = new List<KeyValuePair<string, float>>(lineDistances);
                top.Sort((a, b) => b.Value.CompareTo(a.Value));
                for (int i = 0; i < Mathf.Min(3, top.Count); i++) significant.Add(top[i].Key);
            }

            significant.Sort((a, b) => firstAppearance[a].CompareTo(firstAppearance[b]));

            var parts = new List<string>(significant.Count);
            foreach (var r in significant)
            {
                float d = lineDistances[r];
                int km = Mathf.RoundToInt(d / 1000f);
                int pct = Mathf.RoundToInt(d / totalLength * 100f);
                parts.Add($"lk{r} ({km}km/{pct}%)");
            }
            _routeLinesText.text = "Linie: " + string.Join(" → ", parts);
        }

        /// <summary>
        /// Filter dla railway line ref. Akceptujemy numeric refs (max 4 znaki — polskie linie
        /// 1-9999). Filter dłuższych ("151111" = OSM artefakt na bocznicach) i pure-letter ("R").
        /// Numeric + literowy suffix akceptowany ("9a", "204X" — czasem OSM ma dla wariantów).
        /// </summary>
        private static bool IsValidLineRef(string r)
        {
            if (string.IsNullOrEmpty(r) || r.Length > 4) return false;
            bool hasDigit = false;
            foreach (char c in r)
            {
                if (char.IsDigit(c)) hasDigit = true;
                else if (!char.IsLetter(c)) return false;
            }
            return hasDigit;
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11: multi-candidate resolver dla pathfindera retry.
        /// Stacja może mieć WIELE potencjalnych path nodów (peron, trunk junction, sąsiednie tory).
        /// Jeśli jeden node jest w izolowanej komponencie grafu, pathfinder fails — retry z innym.
        ///
        /// Algorytm:
        /// 1. Jeśli user wybrał `selectedTrack` (!= "") → first candidate to `FindNodeOnTrack`.
        /// 2. Always include `station.pathNodeId` (peron centroid).
        /// 3. Jeśli `isEndpoint` (A lub C) → STOP, no expansion (chcemy konkretny peron).
        /// 4. Else (waypoint pośredni) → dodaj top-N najbliższych nodów w 500m (sort by edges desc, dist asc).
        /// </summary>
        private static List<int> ResolveCandidates(PathfindingGraph graph, RailwayStation station,
            string selectedTrack, bool isEndpoint, int count = 5)
        {
            var candidates = new List<int>();
            if (station == null || station.pathNodeId < 0 || graph == null) return candidates;

            // 1. User-selected track first (force-route przez ten tor).
            if (!string.IsNullOrEmpty(selectedTrack))
            {
                int trackNode = graph.FindNodeOnTrack(station.pathNodeId, selectedTrack);
                if (trackNode >= 0)
                {
                    candidates.Add(trackNode);
                    Log.Info($"[ResolveCandidates] {station.name} track '{selectedTrack}' → node {trackNode}");
                }
                else
                {
                    Log.Warn($"[ResolveCandidates] {station.name} track '{selectedTrack}' NOT FOUND in graph");
                }
            }

            // 2. Always include pathNodeId (peron / centroid).
            if (!candidates.Contains(station.pathNodeId)) candidates.Add(station.pathNodeId);

            // 3. Endpoint (A/C) — no expansion, chcemy konkretny peron.
            if (isEndpoint) return candidates;

            // 4. Waypoint pośredni — dodaj top-N nearby nodów.
            var buffer = new List<int>();
            graph.FindNodesInRadius(station.position, 500f, buffer);

            // Sort: edges desc, then distance asc (więcej krawędzi = większa szansa na ścieżkę).
            var snapPos = graph.GetNode(station.pathNodeId).position;
            buffer.Sort((a, b) =>
            {
                if (a < 0 || a >= graph.NodeCount) return 1;
                if (b < 0 || b >= graph.NodeCount) return -1;
                int eA = graph.GetNode(a).edgeIds?.Count ?? 0;
                int eB = graph.GetNode(b).edgeIds?.Count ?? 0;
                if (eA != eB) return eB.CompareTo(eA);
                float dA = (graph.GetNode(a).position - snapPos).sqrMagnitude;
                float dB = (graph.GetNode(b).position - snapPos).sqrMagnitude;
                return dA.CompareTo(dB);
            });

            foreach (int nid in buffer)
            {
                if (candidates.Count >= count) break;
                if (nid < 0 || nid >= graph.NodeCount) continue;
                if (!candidates.Contains(nid)) candidates.Add(nid);
            }
            return candidates;
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11: resolver punktu (A, waypoint, C) → path node dla pathfindera.
        /// **Single-candidate fallback** — zachowany dla backward compat, ale BuildRoute używa
        /// ResolveCandidates (multi-attempt retry).
        /// - A (pointIdx=0) i C (pointIdx=count-1) → pathNodeId stacji (peron docelowy).
        /// - Waypoint pośredni:
        ///   • Jeśli user wybrał konkretny tor (`_waypointTracks[wpIdx] != ""`) →
        ///     `graph.FindNodeOnTrack(pathNodeId, trackRef)`. Fallback do trunk gdy brak.
        ///   • Else → `ResolveTrunkNode` (najbliższy junction / max-edges w 500m).
        /// </summary>
        private int ResolvePointNode(PathfindingGraph graph, RailwayStation station, int pointIdx, int totalPoints)
        {
            if (station == null || station.pathNodeId < 0) return station?.pathNodeId ?? -1;

            // A albo C — peronowy pathNodeId, brak trunk substitution.
            if (pointIdx == 0 || pointIdx == totalPoints - 1)
                return station.pathNodeId;

            // Waypoint pośredni — mapping na _waypointTracks (A=0, wp0=1, wp1=2, ...).
            int wpIdx = pointIdx - 1;
            string selectedTrack = (wpIdx >= 0 && wpIdx < _waypointTracks.Count)
                ? _waypointTracks[wpIdx]
                : "";

            if (!string.IsNullOrEmpty(selectedTrack))
            {
                int trackNode = graph.FindNodeOnTrack(station.pathNodeId, selectedTrack);
                if (trackNode >= 0)
                {
                    Log.Info($"[ResolvePointNode] waypoint {wpIdx} ({station.name}) → "
                           + $"track '{selectedTrack}' node {trackNode}");
                    return trackNode;
                }
                Log.Warn($"[ResolvePointNode] waypoint {wpIdx} ({station.name}) track '{selectedTrack}' "
                       + $"NOT FOUND in graph — fallback do trunk node");
            }

            return ResolveTrunkNode(graph, station.pathNodeId, station.position);
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11 v2: znajduje "trunk node" dla waypointu pośredniego.
        /// Stacja często ma `pathNodeId` przypisany do peronowego toru (2 krawędzie = forward+backward
        /// single tor, brak rozgałęzień). Pathfinder od/do tego node nie ma jak kontynuować — utyka
        /// eksplorując całą komponentę grafu (500k node limit reached).
        ///
        /// **Algorytm (v2):**
        /// 1. Jeśli snap node ma już >= 3 krawędzie (rozjazd/krzyżówka) — zostaw.
        /// 2. Spatial 500m radius od stacji.
        /// 3. **Preferuj `JunctionNodeIds`** (rozjazd z OSM `railway=switch`) — najbliższy.
        /// 4. Fallback: node z największą liczbą krawędzi w radius.
        ///
        /// Radius 500m bo Malbork-typowe układy stacyjne mają 200-400m długość peronów —
        /// najbliższy rozjazd poza peronem może być 300-400m od snap node.
        ///
        /// Używane tylko dla waypointów POŚREDNICH. A (start) i C (end) zostają na pathNodeId
        /// peronu — pathfinder ma kończyć na właściwym torze rozkładowym, nie obok.
        /// </summary>
        private static int ResolveTrunkNode(PathfindingGraph graph, int snapNodeId, Vector2 stationPos)
        {
            if (graph == null || snapNodeId < 0 || snapNodeId >= graph.NodeCount) return snapNodeId;

            var snapNode = graph.GetNode(snapNodeId);
            int snapEdgeCount = snapNode.edgeIds?.Count ?? 0;

            // Snap node już jest trunk-ish (3+ krawędzie) — nie szukaj alternatywy.
            if (snapEdgeCount >= 3) return snapNodeId;

            var buffer = new List<int>();
            graph.FindNodesInRadius(stationPos, 500f, buffer);
            var snapPos = snapNode.position;

            // 1. Preferuj najbliższy JUNCTION node (rozjazd OSM railway=switch).
            int bestJunction = -1;
            int bestJunctionEdges = 0;
            float bestJunctionDistSq = float.MaxValue;
            if (graph.JunctionNodeIds != null)
            {
                foreach (int nid in buffer)
                {
                    if (nid < 0 || nid >= graph.NodeCount) continue;
                    if (!graph.JunctionNodeIds.Contains(nid)) continue;
                    var n = graph.GetNode(nid);
                    float distSq = (n.position - snapPos).sqrMagnitude;
                    if (distSq < bestJunctionDistSq)
                    {
                        bestJunctionDistSq = distSq;
                        bestJunction = nid;
                        bestJunctionEdges = n.edgeIds?.Count ?? 0;
                    }
                }
            }
            if (bestJunction >= 0)
            {
                Log.Info($"[TrunkResolver] Snap node {snapNodeId} ({snapEdgeCount} edges) "
                       + $"→ JUNCTION {bestJunction} ({bestJunctionEdges} edges, "
                       + $"{Mathf.Sqrt(bestJunctionDistSq):F1}m)");
                return bestJunction;
            }

            // 2. Fallback: node z największą liczbą krawędzi w radius.
            int bestNode = snapNodeId;
            int bestEdgeCount = snapEdgeCount;
            float bestDistSq = float.MaxValue;
            foreach (int nid in buffer)
            {
                if (nid < 0 || nid >= graph.NodeCount) continue;
                var n = graph.GetNode(nid);
                int ec = n.edgeIds?.Count ?? 0;
                if (ec < bestEdgeCount) continue;
                float distSq = (n.position - snapPos).sqrMagnitude;
                if (ec > bestEdgeCount || (ec == bestEdgeCount && distSq < bestDistSq))
                {
                    bestNode = nid;
                    bestEdgeCount = ec;
                    bestDistSq = distSq;
                }
            }
            if (bestNode != snapNodeId)
            {
                Log.Info($"[TrunkResolver] Snap node {snapNodeId} ({snapEdgeCount} edges) "
                       + $"→ max-edges fallback {bestNode} ({bestEdgeCount} edges, "
                       + $"{Mathf.Sqrt(bestDistSq):F1}m)");
            }
            else
            {
                Log.Warn($"[TrunkResolver] Snap node {snapNodeId} ({snapEdgeCount} edges) "
                       + $"NO better trunk found in 500m — pathfinder may fail");
            }
            return bestNode;
        }

        /// <summary>
        /// M-TimetableUX F1.6 (TD-019 fix 2026-05-10, v6 2026-05-11 user decision):
        /// **Strict topological filter — no spatial fallback, no multi-node match.**
        ///
        /// **Algorytm:** `station.pathNodeId ∈ allNodeIds` (single criterion).
        /// Stacja jest auto-dodana ⇔ pathfinder przechodzi dokładnie przez jej `pathNodeId`.
        ///
        /// **Rationale (v6):** próby rozluźnienia kryterium (v4 3m fallback, v5 multi-node z
        /// `railway:track_ref` filter w 50m) generowały zawijasy bo pathfinder znajdował
        /// peronowe tory stacji jako "krótsze" i zawijał przez każdy układ stacyjny. Trade-off:
        /// strict topological gubi część stacji (gdy `pathNodeId` na innym torze niż pathfinder),
        /// ale eliminuje totalny chaos zawijasów po peronach. User decision: lepiej brakujące
        /// stacje (ręczne dodawanie) niż patologiczna geometria trasy.
        ///
        /// **Blacklist:** user remove via "✕" button → `_blacklistedStationNodeIds` HashSet, reset
        /// na nowym A→B.
        /// </summary>
        private void FindStationsPerSegment(Route route, List<int> allNodeIds,
            List<int> segBounds, TimetableInitializer init)
        {
            var graph = init.Graph;

            // Build dict raz: pathNodeId → first occurrence routeIdx w allNodeIds.
            var nodeIdToRouteIdx = new Dictionary<int, int>(allNodeIds.Count);
            for (int i = 0; i < allNodeIds.Count; i++)
                if (!nodeIdToRouteIdx.ContainsKey(allNodeIds[i]))
                    nodeIdToRouteIdx[allNodeIds[i]] = i;

            // Pre-allocate per-segment buckets.
            int segmentCount = segBounds.Count - 1;
            var stationsPerSegment = new List<List<(int routeIdx, RouteStation rs)>>(segmentCount);
            for (int s = 0; s < segmentCount; s++)
                stationsPerSegment.Add(new List<(int, RouteStation)>());

            // M-TimetableUX 2026-05-11: force-include user waypoints.
            // User explicit dodał waypoint → MUSI być widoczny jako stop, nawet jeśli pathfinder
            // dotarł alternative node (489192 zamiast 489376 dla Szymankowa). HashSet stationId
            // żeby skip topological pass jeśli already going to force-include.
            var forcedStationIds = new HashSet<int>();
            foreach (var wp in _waypoints)
                if (wp != null) forcedStationIds.Add(wp.stationId);

            // Pass 1: strict topological match dla all stations (skip forced — handled w Pass 2).
            int topologicalCount = 0;
            int filteredHaltsCount = 0;
            foreach (var st in init.Stations)
            {
                if (st.pathNodeId < 0) continue;
                if (_blacklistedStationNodeIds.Contains(st.pathNodeId)) continue;
                if (forcedStationIds.Contains(st.stationId)) continue; // Pass 2 handles

                // M-TimetableUX 2026-05-11: filter halty bez `railway:track_ref` w OSM data.
                // Tymczasowy fix — halty bez track_ref nie mają dropdown opcji + mieszają w stops list.
                // Long-term: regen binarki w formap z poprawkami OSM.
                if (IsHaltWithoutTrackRef(st, init))
                {
                    filteredHaltsCount++;
                    continue;
                }

                // Exact topological match: pathNodeId MUSI być na ścieżce.
                if (!nodeIdToRouteIdx.TryGetValue(st.pathNodeId, out int routeIdx))
                    continue;

                int segIdx = FindSegmentIndex(segBounds, routeIdx);
                if (segIdx < 0 || segIdx >= segmentCount) continue;

                stationsPerSegment[segIdx].Add((routeIdx, new RouteStation
                {
                    stationNodeId = allNodeIds[routeIdx],
                    stationName = st.name,
                    position = st.position,
                    isMajorStation = st.isMajorStation,
                    voivodeship = st.voivodeship,
                    cityName = st.cityName
                }));
                topologicalCount++;
            }

            // Pass 2: force-include user waypoints PRZY ZACHOWANIU USER-DEFINED ORDER.
            // Waypoint wpIdx definicyjnie kończy segment wpIdx — routeIdx = segBounds[wpIdx + 1] - 1
            // (ostatni node tego segmentu = punkt dokąd pathfinder dotarł szukając tego waypointa).
            //
            // **Bug fix 2026-05-11 v2**: poprzednia wersja używała geographic-nearest w CAŁEJ
            // trasie. Problem: pathfinder mógł zbudować segment Gronowo→Stare Pole z trasą
            // geograficznie blisko Królewa Malborskiego. Force-include łapał ten "okoliczny" node
            // jako pozycję Królewa, którego routeIdx był MNIEJSZY niż Stare Pole → sort by
            // routeIdx wstawiało Królewo PRZED Stare Pole, łamiąc user-defined kolejność.
            int forcedCount = 0;
            for (int wpIdx = 0; wpIdx < _waypoints.Count; wpIdx++)
            {
                var wp = _waypoints[wpIdx];
                if (wp == null) continue;
                if (wp.pathNodeId >= 0 && _blacklistedStationNodeIds.Contains(wp.pathNodeId)) continue;

                // Segment wpIdx ends with this waypoint — routeIdx = last node of segment wpIdx.
                int segEndBoundary = wpIdx + 1;
                if (segEndBoundary >= segBounds.Count) continue; // safety
                int routeIdx = segBounds[segEndBoundary] - 1;
                if (routeIdx < 0 || routeIdx >= allNodeIds.Count) continue;

                int segIdx = wpIdx; // waypoint wpIdx należy do segmentu wpIdx (jego końcowy node)
                if (segIdx < 0 || segIdx >= segmentCount) continue;

                stationsPerSegment[segIdx].Add((routeIdx, new RouteStation
                {
                    stationNodeId = allNodeIds[routeIdx],
                    stationName = wp.name,
                    position = wp.position,
                    isMajorStation = wp.isMajorStation,
                    voivodeship = wp.voivodeship,
                    cityName = wp.cityName
                }));
                forcedCount++;
            }

            // Sort per segment by route position + flatten to route.stations.
            int totalAdded = 0;
            for (int s = 0; s < segmentCount; s++)
            {
                stationsPerSegment[s].Sort((a, b) => a.routeIdx.CompareTo(b.routeIdx));
                foreach (var (_, rs) in stationsPerSegment[s])
                    route.stations.Add(rs);
                totalAdded += stationsPerSegment[s].Count;
            }

            Log.Info($"[F1.6] FindStationsPerSegment: added {totalAdded} stations " +
                     $"(topological={topologicalCount}, forced waypoints={forcedCount}, " +
                     $"filtered halts bez track_ref={filteredHaltsCount}, " +
                     $"path={allNodeIds.Count} nodes / {segmentCount} segments)");
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11: tymczasowy filter halt bez `railway:track_ref` w OSM data.
        /// Halty (railway=halt, niemajor) bez torów w `init.TrackData` mają puste dropdown opcje
        /// i mieszają w stops list (nie da się im przypisać toru postoju).
        ///
        /// Long-term: regen binarki w formap z poprawkami OSM tags na haltsach.
        /// </summary>
        internal static bool IsHaltWithoutTrackRef(RailwayStation st, TimetableInitializer init)
        {
            if (st == null) return false;
            if (st.isMajorStation) return false; // tylko halty (railway=halt) filtrujemy
            if (init?.TrackData == null || !init.TrackData.IsLoaded) return false; // brak danych = nie filtruj
            var tracks = init.TrackData.GetTracks(st.name);
            return tracks == null || tracks.Count == 0;
        }

        /// <summary>
        /// Binary search w segBounds dla segment containing routeIdx.
        /// segBounds = [0, b1, b2, ..., end]. Segment i covers [segBounds[i], segBounds[i+1]).
        /// </summary>
        private static int FindSegmentIndex(List<int> segBounds, int routeIdx)
        {
            // Linear search dla małych segBounds (typowo 2-5 segments dla A→B z waypoints) — szybsze niż BS.
            for (int i = 0; i < segBounds.Count - 1; i++)
                if (routeIdx >= segBounds[i] && routeIdx < segBounds[i + 1])
                    return i;
            // Edge case: routeIdx == last node (segBounds.Count-1) — przypisz do ostatniego segmentu.
            if (routeIdx == segBounds[segBounds.Count - 1])
                return segBounds.Count - 2;
            return -1;
        }

        static void EnsurePlatformStationCache(TimetableInitializer init)
        {
            if (init?.Graph == null || init.Platforms == null || init.Stations == null) return;
            if (_cachedForGraph == init.Graph && _stationToPlatformsCache != null) return;

            _stationToPlatformsCache = new Dictionary<string, HashSet<int>>();
            _cachedForGraph = init.Graph;
            for (int pi = 0; pi < init.Platforms.Count; pi++)
            {
                var plat = init.Platforms[pi];
                if (plat.stationNodeId < 0 || plat.stationNodeId >= init.Graph.NodeCount) continue;
                var platPos = init.Graph.GetNode(plat.stationNodeId).position;

                float bestDist = float.MaxValue;
                string bestName = null;
                foreach (var st in init.Stations)
                {
                    float d = (st.position - platPos).sqrMagnitude;
                    if (d < bestDist) { bestDist = d; bestName = st.name; }
                }
                if (string.IsNullOrEmpty(bestName)) continue;
                if (!_stationToPlatformsCache.TryGetValue(bestName, out var set))
                {
                    set = new HashSet<int>();
                    _stationToPlatformsCache[bestName] = set;
                }
                set.Add(pi);
            }
        }

        List<StationTrackInfo> FindTracksForStop(TimetableStop stop)
        {
            _ftCallCount++;
            var result = new List<StationTrackInfo>();
            var init = TimetableInitializer.Instance;
            if (init?.Graph == null) return result;

            var swJson = System.Diagnostics.Stopwatch.StartNew();
            if (init.TrackData != null && init.TrackData.IsLoaded)
            {
                var fromFile = init.TrackData.GetTracks(stop.stationName);
                if (fromFile != null)
                {
                    foreach (var t in fromFile)
                        result.Add(new StationTrackInfo { trackRef = t.trackRef, hasPlatform = t.hasPlatform });
                    if (result.Count > 0)
                    {
                        swJson.Stop();
                        _ftJsonHits++;
                        _ftJsonMs += swJson.Elapsed.TotalMilliseconds;
                        return result;
                    }
                }
            }
            swJson.Stop();
            _ftJsonMs += swJson.Elapsed.TotalMilliseconds;

            _ftOsmFallbacks++;
            var swOsm = System.Diagnostics.Stopwatch.StartNew();
            var stopPos = init.Graph.GetNode(stop.stationNodeId).position;
            const float radiusM = 300f;

            var nearbyNodes = new List<int>();
            init.Graph.FindNodesInRadius(stopPos, radiusM, nearbyNodes);

            EnsurePlatformStationCache(init);
            var platformTrackRefs = new HashSet<string>();
            if (_stationToPlatformsCache != null
                && _stationToPlatformsCache.TryGetValue(stop.stationName, out var platIndices))
            {
                foreach (int pi in platIndices)
                {
                    var plat = init.Platforms[pi];
                    if (!string.IsNullOrEmpty(plat.platformName) && plat.platformName != "?")
                    {
                        foreach (var r in plat.platformName.Split(';'))
                        {
                            var trimmed = r.Trim();
                            if (trimmed.Length > 0) platformTrackRefs.Add(trimmed);
                        }
                    }
                }
            }
            bool hasPlatformData = platformTrackRefs.Count > 0;

            if (hasPlatformData)
            {
                var seenRefs = new HashSet<string>();
                foreach (int nid in nearbyNodes)
                {
                    var node = init.Graph.GetNode(nid);
                    foreach (var eid in node.edgeIds)
                    {
                        var edge = init.Graph.GetEdge(eid);
                        if (edge.metadata == null) continue;
                        edge.metadata.TryGetValue("railway:track_ref", out var trackRef);
                        if (string.IsNullOrEmpty(trackRef)) continue;
                        if (!seenRefs.Add(trackRef)) continue;
                        if (!platformTrackRefs.Contains(trackRef)) continue;

                        result.Add(new StationTrackInfo
                        {
                            trackRef = trackRef,
                            hasPlatform = true
                        });
                    }
                }
            }

            result.Sort((a, b) => CompareTrackRefs(a.trackRef, b.trackRef));

            if (result.Count == 0)
                result.Add(new StationTrackInfo { trackRef = "1", hasPlatform = true });

            swOsm.Stop();
            _ftOsmMs += swOsm.Elapsed.TotalMilliseconds;
            return result;
        }

        struct StationTrackInfo
        {
            public string trackRef;
            public bool hasPlatform;
        }

        List<StationTrackInfo> FilterTracksByReachability(
            List<StationTrackInfo> tracks, TimetableStop currentStop,
            TimetableStop prevStop, TimetableStop nextStop)
        {
            var graph = TimetableInitializer.Instance?.Graph;
            if (graph == null || tracks.Count <= 1) return tracks;
            if (currentStop.stationNodeId < 0) return tracks;

            var reachable = new List<StationTrackInfo>();

            foreach (var track in tracks)
            {
                int trackNode = graph.FindNodeOnTrack(currentStop.stationNodeId, track.trackRef);
                if (trackNode < 0)
                {
                    reachable.Add(track);
                    continue;
                }

                bool ok = true;

                if (nextStop != null && nextStop.stationNodeId >= 0)
                {
                    var result = RailwayPathfinder.FindPath(
                        graph, trackNode, nextStop.stationNodeId, maxNodesExplored: 50_000);
                    if (!result.success) ok = false;
                }

                if (ok && prevStop != null && prevStop.stationNodeId >= 0)
                {
                    var result = RailwayPathfinder.FindPath(
                        graph, prevStop.stationNodeId, trackNode, maxNodesExplored: 50_000);
                    if (!result.success) ok = false;
                }

                if (ok) reachable.Add(track);
            }

            return reachable.Count > 0 ? reachable : tracks;
        }

        void RankTracksByProximity(List<StationTrackInfo> tracks, TimetableStop currentStop,
                                   TimetableStop prevStop, TimetableStop nextStop)
        {
            var graph = TimetableInitializer.Instance?.Graph;
            if (graph == null || tracks.Count <= 1) return;

            bool hasPrev = prevStop != null && prevStop.stationNodeId >= 0;
            bool hasNext = nextStop != null && nextStop.stationNodeId >= 0;
            if (!hasPrev && !hasNext) return;

            Vector2 currentPos = graph.GetNode(currentStop.stationNodeId).position;
            Vector2 travelDir;
            if (hasPrev && hasNext)
                travelDir = (graph.GetNode(nextStop.stationNodeId).position
                            - graph.GetNode(prevStop.stationNodeId).position).normalized;
            else if (hasNext)
                travelDir = (graph.GetNode(nextStop.stationNodeId).position - currentPos).normalized;
            else
                travelDir = (currentPos - graph.GetNode(prevStop.stationNodeId).position).normalized;

            Vector2 rightVec = new Vector2(travelDir.y, -travelDir.x);

            var lengths = new Dictionary<string, float>();
            var preference = new Dictionary<string, int>();
            foreach (var track in tracks)
            {
                int trackNode = graph.FindNodeOnTrack(currentStop.stationNodeId, track.trackRef);
                if (trackNode < 0)
                {
                    lengths[track.trackRef] = float.MaxValue;
                    preference[track.trackRef] = -2;
                    continue;
                }

                preference[track.trackRef] = EvaluateTrackPreference(
                    graph, trackNode, track.trackRef, travelDir, currentPos, rightVec);

                float len = 0f;
                if (hasPrev)
                {
                    var r = RailwayPathfinder.FindPath(graph, prevStop.stationNodeId, trackNode, 50_000);
                    len += r.success ? r.totalLengthM : float.MaxValue / 2f;
                }
                if (hasNext)
                {
                    var r = RailwayPathfinder.FindPath(graph, trackNode, nextStop.stationNodeId, 50_000);
                    len += r.success ? r.totalLengthM : float.MaxValue / 2f;
                }
                lengths[track.trackRef] = len;
            }

            tracks.Sort((a, b) =>
            {
                int pa = preference.TryGetValue(a.trackRef, out int va) ? va : -2;
                int pb = preference.TryGetValue(b.trackRef, out int vb) ? vb : -2;

                if (pa != pb) return pb.CompareTo(pa);

                float sa = lengths.TryGetValue(a.trackRef, out float xa) ? xa : float.MaxValue;
                float sb = lengths.TryGetValue(b.trackRef, out float xb) ? xb : float.MaxValue;
                return sa.CompareTo(sb);
            });
        }

        static int EvaluateTrackPreference(PathfindingGraph graph, int trackNode, string trackRef,
                                            Vector2 travelDir, Vector2 stationPos, Vector2 rightVec)
        {
            var node = graph.GetNode(trackNode);
            foreach (int eid in node.edgeIds)
            {
                var edge = graph.GetEdge(eid);
                if (edge.metadata == null) continue;
                if (!edge.metadata.TryGetValue("railway:track_ref", out var tr) || tr != trackRef) continue;

                Vector2 fromPos = graph.GetNode(edge.fromNodeId).position;
                Vector2 toPos = graph.GetNode(edge.toNodeId).position;
                Vector2 edgeDir = (toPos - fromPos).normalized;
                float alignment = Vector2.Dot(edgeDir, travelDir);
                if (alignment < 0.3f) continue;

                if (!edge.metadata.TryGetValue("railway:preferred_direction", out var pd)) continue;

                bool preferredIsForward = pd == "forward";
                bool ourEdgeIsOsmForward = edge.isOsmForward;
                return (ourEdgeIsOsmForward == preferredIsForward) ? 1 : -1;
            }

            Vector2 trackPos = graph.GetNode(trackNode).position;
            float rightness = Vector2.Dot(trackPos - stationPos, rightVec);
            if (Mathf.Abs(rightness) < 0.5f) return 0;
            return rightness > 0 ? 1 : -1;
        }

        bool HasPlatformForTrack(TimetableInitializer init, string trackRef,
            Vector2 stopPos, Vector2 edgePosA, Vector2 edgePosB)
        {
            if (init.Platforms == null || string.IsNullOrEmpty(trackRef)) return false;
            const float stationRadiusSq = 500f * 500f;
            const float spatialRadiusSq = 3f * 3f;

            foreach (var plat in init.Platforms)
            {
                if (plat.stationNodeId < 0 || plat.stationNodeId >= init.Graph.NodeCount) continue;

                var platPos = init.Graph.GetNode(plat.stationNodeId).position;
                if ((platPos - stopPos).sqrMagnitude > stationRadiusSq) continue;

                if (!string.IsNullOrEmpty(plat.platformName) && plat.platformName != "?")
                {
                    var refs = plat.platformName.Split(';');
                    foreach (var r in refs)
                        if (r.Trim() == trackRef) return true;
                }

                if (!string.IsNullOrEmpty(plat.trackRef) && plat.trackRef == trackRef)
                    return true;

                if ((platPos - edgePosA).sqrMagnitude < spatialRadiusSq
                    || (platPos - edgePosB).sqrMagnitude < spatialRadiusSq)
                    return true;
            }
            return false;
        }

        static int CompareTrackRefs(string a, string b)
        {
            int.TryParse(a, out int na);
            int.TryParse(b, out int nb);
            if (na != 0 && nb != 0) return na.CompareTo(nb);
            return string.Compare(a, b, System.StringComparison.Ordinal);
        }
    }
}
