using System.Collections.Generic;
using formap;
using MapSystem;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Graf pathfindingu zbudowany z RailwayGraph — każdy endpoint segmentu staje się PathNode,
    /// każdy segment staje się PathEdge (bidirectional). Spatial hash pozwala szybko znaleźć
    /// węzeł po pozycji (tolerancja = cellSize).
    /// </summary>
    public class PathfindingGraph
    {
        public struct Node
        {
            public int id;
            public Vector2 position;
            /// <summary>Indeksy krawędzi wychodzących (w tablicy Edges).</summary>
            public List<int> edgeIds;
        }

        public struct Edge
        {
            public int id;
            public int fromNodeId;
            public int toNodeId;
            public int segmentId;     // original RailwaySegment.Id
            public float lengthM;
            public int maxSpeedKmh;
            /// <summary>Wskaźnik do metadata segmentu (elektryfikacja, usage, ...).</summary>
            public Dictionary<string, string> metadata;
            /// <summary>Pełna polyline od fromNode do toNode — oryginalne pozycje vertexów
            /// z MeshGeometry (OSM). Zawiera oba endpointy. Null = prosta linia między node'ami.</summary>
            public List<Vector2> geometry;
            /// <summary>true = ta krawędź idzie w kierunku OSM-forward (order vertexów w way).
            /// false = krawędź idzie w kierunku OSM-backward. Używane z railway:preferred_direction
            /// (forward/backward) żeby określić czy kierunek jazdy zgadza się z preferowanym kierunkiem toru.</summary>
            public bool isOsmForward;
        }

        private readonly List<Node> _nodes = new();
        private readonly List<Edge> _edges = new();
        private readonly Dictionary<long, List<int>> _spatialGrid = new();

        /// <summary>Rozmiar komórki spatial hash w metrach — używany też jako tolerancja mergowania węzłów.</summary>
        public float CellSize { get; private set; } = 1.0f;

        public IReadOnlyList<Node> Nodes => _nodes;
        public IReadOnlyList<Edge> Edges => _edges;
        public int NodeCount => _nodes.Count;
        public int EdgeCount => _edges.Count;

        /// <summary>Node IDs które są rozjazdami (OSM junction vertices merged do tego node'a).</summary>
        public HashSet<int> JunctionNodeIds { get; private set; } = new();

        // ─────────────────────────────────────────────
        //  Builder
        // ─────────────────────────────────────────────

        /// <summary>
        /// Buduje PathfindingGraph z istniejącego RailwayGraph (jego dedupowanych segmentów).
        /// </summary>
        public void BuildFrom(RailwayGraph railwayGraph, float cellSizeM = 1.0f)
        {
            CellSize = cellSizeM;
            _nodes.Clear();
            _edges.Clear();
            _spatialGrid.Clear();

            if (railwayGraph == null || railwayGraph.SegmentCount == 0) return;

            foreach (var seg in railwayGraph.Segments.Values)
            {
                int fromNode = GetOrCreateNode(seg.Start);
                int toNode   = GetOrCreateNode(seg.End);
                if (fromNode == toNode) continue;

                int maxSpeed = SegmentSpeedResolver.GetMaxSpeedKmh(seg);
                float length = seg.Length;

                AddEdge(fromNode, toNode, seg.Id, length, maxSpeed, seg.Metadata, null);
                AddEdge(toNode, fromNode, seg.Id, length, maxSpeed, seg.Metadata, null);
            }
        }

        /// <summary>
        /// NAJBARDZIEJ ROBUSTNA metoda — Union-Find based merging pozycji vertex między ways.
        ///
        /// Algorytm:
        /// 1. Każdy vertex każdego feature startuje jako osobny "raw node"
        /// 2. Union-Find merguje raw nodes których pozycje są w zadanej tolerancji (cellSizeM)
        ///    TRANSITYWNIE — jeśli A blisko B i B blisko C, wszystkie 3 są w jednym zbiorze
        /// 3. Finalne PathfindingGraph nodes = UF components
        /// 4. Edges z feature chain: segment per pair consecutive vertices (deduplikowane przez
        ///    dictionary (from,to)→edge żeby nie dodawać multi-edges)
        ///
        /// Spatial hash approach gubił topology dla short ways (wszystkie vertices <20m apart
        /// kolapsowały do jednego node → isolated component). Union-Find zachowuje feature
        /// chain structure bez premature collapsing.
        /// </summary>
        /// <param name="junctionOnlyMerge">Gdy true, merging między wayami tylko na junction
        /// vertices (OSM shared nodes). Zapobiega fałszywym skrótom gdzie równoległe linie
        /// mijają się w odległości &lt;cellSize.</param>
        public void BuildFromFeaturesUnionFind(List<MeshGeometry> railwayFeatures,
            float cellSizeM = 10f, bool junctionOnlyMerge = true)
        {
            CellSize = cellSizeM;
            _nodes.Clear();
            _edges.Clear();
            _spatialGrid.Clear();

            if (railwayFeatures == null) return;

            // M-PL: pre-pass żeby policzyć total vertices — daje single alloc dla wszystkich
            // List<T> zamiast wielokrotnych realloc (dramatic GC pressure reduction).
            float t0 = Time.realtimeSinceStartup;
            int totalVertices = 0;
            for (int fi = 0; fi < railwayFeatures.Count; fi++)
            {
                var f = railwayFeatures[fi];
                if (f != null && f.Vertices != null && f.Vertices.Count >= 2)
                    totalVertices += f.Vertices.Count;
            }
            Log.Info($"[PathfindingGraph] Pre-pass: {railwayFeatures.Count} features → {totalVertices} total vertices in {Time.realtimeSinceStartup - t0:F2}s");

            // Step 1: zbierz raw nodes + junction flags + endpoint flags + track_ref + direction
            float t1 = Time.realtimeSinceStartup;
            var rawPositions = new List<Vector2>(totalVertices);
            var rawIsJunction = new List<bool>(totalVertices);
            var rawIsEndpoint = new List<bool>(totalVertices);
            var rawTrackRef = new List<string>(totalVertices);
            var rawDirection = new List<Vector2>(totalVertices);
            var featureVertexIds = new int[railwayFeatures.Count][];
            int featuresWithTrackRef = 0;

            for (int fi = 0; fi < railwayFeatures.Count; fi++)
            {
                var f = railwayFeatures[fi];
                if (f == null || f.Vertices == null || f.Vertices.Count < 2)
                {
                    featureVertexIds[fi] = null;
                    continue;
                }

                var jSet = (f.JunctionIndices != null && f.JunctionIndices.Count > 0)
                    ? new HashSet<int>(f.JunctionIndices) : null;

                // Track ref z metadata feature'a — wspólny dla wszystkich vertexów w way
                string featureTrackRef = null;
                if (f.Metadata != null)
                    f.Metadata.TryGetValue("railway:track_ref", out featureTrackRef);
                if (!string.IsNullOrEmpty(featureTrackRef)) featuresWithTrackRef++;

                int lastVi = f.Vertices.Count - 1;
                int[] ids = new int[f.Vertices.Count];
                for (int vi = 0; vi < f.Vertices.Count; vi++)
                {
                    ids[vi] = rawPositions.Count;
                    rawPositions.Add(f.Vertices[vi]);
                    rawIsJunction.Add(jSet != null && jSet.Contains(vi));
                    rawIsEndpoint.Add(vi == 0 || vi == lastVi);
                    rawTrackRef.Add(featureTrackRef);

                    // Kierunek toru: prev→next (środkowe), start→next (pierwszy), prev→end (ostatni)
                    Vector2 dir;
                    if (vi == 0)
                        dir = f.Vertices[1] - f.Vertices[0];
                    else if (vi == lastVi)
                        dir = f.Vertices[vi] - f.Vertices[vi - 1];
                    else
                        dir = f.Vertices[vi + 1] - f.Vertices[vi - 1];
                    float mag = dir.magnitude;
                    rawDirection.Add(mag > 0.001f ? dir / mag : Vector2.right);
                }
                featureVertexIds[fi] = ids;
            }

            Log.Info($"[PathfindingGraph] Step 1 (collect vertices): {Time.realtimeSinceStartup - t1:F2}s, "
                     + $"features with track_ref: {featuresWithTrackRef}/{railwayFeatures.Count}");
            float t2 = Time.realtimeSinceStartup;

            // Step 2: Union-Find — merge junction↔junction ORAZ endpoint↔endpoint
            // Non-junction non-endpoint vertices NIE mergują z innymi wayami — zapobiega
            // fałszywym skrótom gdzie równoległe linie się mijają bez fizycznego połączenia.
            //
            // Dwa guardy blokujące fałszywe merge'e (oba stosowane do WSZYSTKICH typów merge,
            // łącznie z junction↔junction — formap może oznaczać vertexy na równoległych
            // torach jako junction mimo braku fizycznego rozjazdu):
            //
            // 1. Perpendicular offset guard: odrzuca merge gdy odległość prostopadła do
            //    kierunku toru > perpThresholdM. Prawdziwy shared OSM node ma distance ≈ 0
            //    więc zawsze przechodzi. Równoległe tory ~4.5m apart → zablokowane.
            //
            // 2. Track-ref guard: odrzuca merge gdy oba vertexy mają różny non-empty
            //    railway:track_ref. Stosowany TYLKO do endpoint↔endpoint (nie junction,
            //    bo prawdziwy rozjazd łączy tory z różnym track_ref).
            const float perpThresholdM = 1.0f;
            var uf = new UnionFind(rawPositions.Count);
            var cellMap = new Dictionary<long, List<int>>();
            float toleranceSq = cellSizeM * cellSizeM;
            int mergesByJunction = 0;
            int mergesByEndpoint = 0;
            int blockedByTrackRef = 0;
            int blockedByPerp = 0;

            for (int i = 0; i < rawPositions.Count; i++)
            {
                // Dodaj do spatial hash (dla lookup)
                var pos = rawPositions[i];
                int cx = Mathf.FloorToInt(pos.x / cellSizeM);
                int cy = Mathf.FloorToInt(pos.y / cellSizeM);
                long selfKey = ((long)(uint)cx << 32) | (uint)cy;
                if (!cellMap.TryGetValue(selfKey, out var list))
                {
                    list = new List<int>();
                    cellMap[selfKey] = list;
                }
                list.Add(i);

                bool iMergeable = !junctionOnlyMerge || rawIsJunction[i] || rawIsEndpoint[i];
                if (!iMergeable) continue;

                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    long key = ((long)(uint)(cx + dx) << 32) | (uint)(cy + dy);
                    if (!cellMap.TryGetValue(key, out var candidates)) continue;
                    foreach (int j in candidates)
                    {
                        if (j == i) continue;
                        bool jMergeable = !junctionOnlyMerge || rawIsJunction[j] || rawIsEndpoint[j];
                        if (!jMergeable) continue;
                        if ((rawPositions[j] - pos).sqrMagnitude <= toleranceSq)
                        {
                            // Guard 1: Perpendicular offset — blokuj merge vertexów na równoległych
                            // torach. Odległość prostopadła do kierunku toru i > próg = inny tor.
                            // Stosowane do WSZYSTKICH typów merge (junction, endpoint).
                            // Prawdziwy shared OSM node: distance ≈ 0, perp ≈ 0 → przechodzi.
                            Vector2 offset = rawPositions[j] - pos;
                            Vector2 perpI = new Vector2(-rawDirection[i].y, rawDirection[i].x);
                            float perpDist = Mathf.Abs(Vector2.Dot(offset, perpI));
                            if (perpDist > perpThresholdM)
                            {
                                blockedByPerp++;
                                continue;
                            }

                            // Guard 2: Track-ref — blokuj endpoint↔endpoint gdy track_ref się różni.
                            // NIE dla junction — prawdziwy rozjazd łączy tory z różnym track_ref.
                            if (!rawIsJunction[i] && !rawIsJunction[j])
                            {
                                var trI = rawTrackRef[i];
                                var trJ = rawTrackRef[j];
                                if (!string.IsNullOrEmpty(trI) && !string.IsNullOrEmpty(trJ) && trI != trJ)
                                {
                                    blockedByTrackRef++;
                                    continue;
                                }
                            }

                            // Liczenie statystyk PRZED Union (po Union root może się zmienić)
                            if (uf.Find(i) != uf.Find(j))
                            {
                                if (rawIsJunction[i] || rawIsJunction[j]) mergesByJunction++;
                                else mergesByEndpoint++;
                            }
                            uf.Union(i, j);
                        }
                    }
                }
            }

            Log.Info($"[PathfindingGraph] Step 2 (Union-Find): {Time.realtimeSinceStartup - t2:F2}s, "
                     + $"merges: {mergesByJunction} junction, {mergesByEndpoint} endpoint | "
                     + $"blocked: {blockedByPerp} perp, {blockedByTrackRef} track_ref");
            float t3 = Time.realtimeSinceStartup;

            // Step 2.5: Rescue merge — pair raw vertices które są w RÓŻNYCH komponentach
            // i fizycznie bardzo blisko (<2.5m), niezależnie od junction/endpoint flagi.
            // Naprawia T-junctions gdzie OSM dało vertex w środku way'a bez markera.
            // Guardy: perpendicular offset + track-ref (jak w Step 2).
            const float rescueRadiusM = 2.5f;
            float rescueRadiusSq = rescueRadiusM * rescueRadiusM;
            int rescueMerges = 0;
            int rescueBlockedByPerp = 0;
            int rescueBlockedByTrackRef = 0;
            for (int i = 0; i < rawPositions.Count; i++)
            {
                var pos = rawPositions[i];
                int cx = Mathf.FloorToInt(pos.x / cellSizeM);
                int cy = Mathf.FloorToInt(pos.y / cellSizeM);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    long key = ((long)(uint)(cx + dx) << 32) | (uint)(cy + dy);
                    if (!cellMap.TryGetValue(key, out var candidates)) continue;
                    foreach (int j in candidates)
                    {
                        if (j <= i) continue;
                        if ((rawPositions[j] - pos).sqrMagnitude > rescueRadiusSq) continue;
                        if (uf.Find(i) == uf.Find(j)) continue; // już w tym samym komponencie

                        // Guard 1: Perpendicular offset
                        Vector2 offset = rawPositions[j] - pos;
                        Vector2 perpI = new Vector2(-rawDirection[i].y, rawDirection[i].x);
                        float perpDist = Mathf.Abs(Vector2.Dot(offset, perpI));
                        if (perpDist > perpThresholdM)
                        {
                            rescueBlockedByPerp++;
                            continue;
                        }

                        // Guard 2: Track-ref
                        var trI = rawTrackRef[i];
                        var trJ = rawTrackRef[j];
                        if (!string.IsNullOrEmpty(trI) && !string.IsNullOrEmpty(trJ) && trI != trJ)
                        {
                            rescueBlockedByTrackRef++;
                            continue;
                        }

                        uf.Union(i, j);
                        rescueMerges++;
                    }
                }
            }
            Log.Info($"[PathfindingGraph] Step 2.5 (rescue merge): {Time.realtimeSinceStartup - t3:F2}s, "
                     + $"merges: {rescueMerges} | blocked: {rescueBlockedByPerp} perp, {rescueBlockedByTrackRef} track_ref");
            float t4 = Time.realtimeSinceStartup;

            // Step 3: Centroid pozycji per UF component + tworzenie PathfindingGraph nodes
            // Zamiast rawPositions[root] (arbitralny UF root) → centroid wszystkich
            // zmergowanych vertexów → lepsze pozycje junction node'ów.
            JunctionNodeIds = new HashSet<int>();
            var componentToNode = new Dictionary<int, int>();
            var componentSum = new Dictionary<int, Vector2>();
            var componentCount = new Dictionary<int, int>();

            // Pass 1: zbierz sumy pozycji per component
            for (int i = 0; i < rawPositions.Count; i++)
            {
                int root = uf.Find(i);
                if (!componentSum.ContainsKey(root))
                {
                    componentSum[root] = rawPositions[i];
                    componentCount[root] = 1;
                }
                else
                {
                    componentSum[root] += rawPositions[i];
                    componentCount[root]++;
                }
            }

            // Pass 2: stwórz nodes z centroidem + spatial grid
            for (int i = 0; i < rawPositions.Count; i++)
            {
                int root = uf.Find(i);
                if (!componentToNode.ContainsKey(root))
                {
                    int nodeId = _nodes.Count;
                    var pos = componentCount[root] > 1
                        ? componentSum[root] / componentCount[root]
                        : rawPositions[root];

                    _nodes.Add(new Node
                    {
                        id = nodeId,
                        position = pos,
                        // M-PL: pre-alloc capacity 4 — średnio 2 edges per node, capacity 4 unika
                        // realloc dla większości. Eliminuje 600k+ List<int> growth reallocs w Step 4.
                        edgeIds = new List<int>(4)
                    });
                    componentToNode[root] = nodeId;

                    // Register in spatial grid for later FindNodeAt/FindNearestNode
                    long spatialKey = CellKey(pos);
                    if (!_spatialGrid.TryGetValue(spatialKey, out var gridList))
                    {
                        gridList = new List<int>();
                        _spatialGrid[spatialKey] = gridList;
                    }
                    gridList.Add(nodeId);
                }

                // Jeśli ten raw vertex jest junction, oznacz finalny node jako junction
                if (rawIsJunction[i])
                    JunctionNodeIds.Add(componentToNode[root]);
            }

            Log.Info($"[PathfindingGraph] Step 3 (build nodes): {Time.realtimeSinceStartup - t4:F2}s, "
                     + $"junction nodes: {JunctionNodeIds.Count}");
            float t5 = Time.realtimeSinceStartup;

            // Step 4: Build edges z feature chain.
            //
            // M-PL: SKIP geometry storage całkowicie dla MVP — 100k+ List<Vector2>
            // allocations powodowały GC pause hangs (godziny w Mono Editor).
            // Pathfinding nie używa intermediate vertices (tylko fromNode/toNode/length).
            // Geometria może być odbudowana z prostej linii dla wizualizacji trasy.
            var edgeKey = new HashSet<long>();
            int totalGeomPoints = 0;
            int progressInterval = System.Math.Max(1, railwayFeatures.Count / 20); // 20 progress logs

            // M-PL: pre-allocate _edges capacity — z pomiarów 1.24M edges dla pełnej PL.
            // Bez tego List<Edge> grows 16 → 32 → ... → 1.5M = 17 reallocations × O(n) copy.
            int estimatedEdges = railwayFeatures.Count * 30; // ~13 transitions per feature × 2 dir
            if (_edges.Capacity < estimatedEdges) _edges.Capacity = estimatedEdges;

            for (int fi = 0; fi < railwayFeatures.Count; fi++)
            {
                if (fi > 0 && fi % progressInterval == 0)
                    Log.Info($"[PathfindingGraph] Step 4 progress: {fi}/{railwayFeatures.Count} features, {_edges.Count} edges so far");

                var f = railwayFeatures[fi];
                var ids = featureVertexIds[fi];
                if (ids == null) continue;

                int maxSpeed = SegmentSpeedResolver.GetMaxSpeedKmh(f.Metadata);

                float lengthAccum = 0f;
                int prevNode = componentToNode[uf.Find(ids[0])];
                int prevVi = 0;

                for (int vi = 1; vi < ids.Length; vi++)
                {
                    lengthAccum += Vector2.Distance(f.Vertices[vi - 1], f.Vertices[vi]);

                    int currNode = componentToNode[uf.Find(ids[vi])];
                    if (currNode == prevNode) continue; // akumuluj dalej

                    long forwardKey = ((long)(uint)prevNode << 32) | (uint)currNode;
                    if (edgeKey.Add(forwardKey))
                    {
                        long reverseKey = ((long)(uint)currNode << 32) | (uint)prevNode;
                        edgeKey.Add(reverseKey);

                        int segId = (f.SegmentIds != null && prevVi < f.SegmentIds.Count)
                            ? f.SegmentIds[prevVi] : 0;

                        // null geometry — MVP, no allocations. Wizualizacja trasy później rebuilds.
                        AddEdge(prevNode, currNode, segId, lengthAccum, maxSpeed, f.Metadata, null, isOsmForward: true);
                        AddEdge(currNode, prevNode, segId, lengthAccum, maxSpeed, f.Metadata, null, isOsmForward: false);
                    }

                    lengthAccum = 0f;
                    prevNode = currNode;
                    prevVi = vi;
                }
            }

            Log.Info($"[PathfindingGraph] Step 4 (build edges): {Time.realtimeSinceStartup - t5:F2}s, "
                     + $"edges: {_edges.Count}, geometry points: {totalGeomPoints}, "
                     + $"avg {(totalGeomPoints > 0 && _edges.Count > 0 ? totalGeomPoints * 2f / _edges.Count : 0f):F1}/edge");
            Log.Info($"[PathfindingGraph] BuildFromFeaturesUnionFind TOTAL: {Time.realtimeSinceStartup - t0:F2}s");
        }


        private int AddEdge(int from, int to, int segmentId, float length, int maxSpeed,
                            Dictionary<string, string> metadata, List<Vector2> geometry,
                            bool isOsmForward = true)
        {
            int edgeId = _edges.Count;
            _edges.Add(new Edge
            {
                id = edgeId,
                fromNodeId = from,
                toNodeId = to,
                segmentId = segmentId,
                lengthM = length,
                maxSpeedKmh = maxSpeed,
                metadata = metadata,
                geometry = geometry,
                isOsmForward = isOsmForward
            });

            // M-PL: write-back niepotrzebny — edgeIds to List<int> reference type, modyfikacja
            // przez struct copy modyfikuje wspólną listę. Eliminuje 1.24M struct copies.
            _nodes[from].edgeIds.Add(edgeId);
            return edgeId;
        }

        // ─────────────────────────────────────────────
        //  Edge helpers
        // ─────────────────────────────────────────────

        /// <summary>Znajduje edge ID z fromNodeId do toNodeId. Zwraca -1 jeśli brak.</summary>
        public int FindEdgeBetween(int fromNodeId, int toNodeId)
        {
            if (fromNodeId < 0 || fromNodeId >= _nodes.Count) return -1;
            var node = _nodes[fromNodeId];
            if (node.edgeIds == null) return -1;
            foreach (int eid in node.edgeIds)
            {
                if (_edges[eid].toNodeId == toNodeId)
                    return eid;
            }
            return -1;
        }

        /// <summary>
        /// Znajduje node na konkretnym torze stacyjnym (wg railway:track_ref w metadata krawędzi).
        /// Skanuje nodes w promieniu radiusM od stationNodeId, zwraca najbliższy node
        /// który ma przynajmniej jedną krawędź z pasującym track_ref.
        /// </summary>
        // Reuse buffer dla FindNodeOnTrack — eliminuje alokacje per call
        // (wywoływane tysiące razy w RefreshStopsList Filter/Rank).
        private readonly List<int> _nodesInRadiusBuffer = new();

        public int FindNodeOnTrack(int stationNodeId, string trackRef, float radiusM = 300f)
        {
            if (stationNodeId < 0 || stationNodeId >= _nodes.Count || string.IsNullOrEmpty(trackRef))
                return -1;

            var stationPos = _nodes[stationNodeId].position;
            int bestNode = -1;
            float bestDistSq = float.MaxValue;

            // Spatial grid lookup zamiast O(N) brute-force scan
            FindNodesInRadius(stationPos, radiusM, _nodesInRadiusBuffer);

            foreach (int n in _nodesInRadiusBuffer)
            {
                float distSq = (_nodes[n].position - stationPos).sqrMagnitude;
                if (distSq >= bestDistSq) continue;

                var node = _nodes[n];
                if (node.edgeIds == null) continue;

                foreach (int eid in node.edgeIds)
                {
                    var edge = _edges[eid];
                    if (edge.metadata != null
                        && edge.metadata.TryGetValue("railway:track_ref", out var tr)
                        && tr == trackRef)
                    {
                        bestDistSq = distSq;
                        bestNode = n;
                        break;
                    }
                }
            }
            return bestNode;
        }

        /// <summary>
        /// M-TimetableUX 2026-05-11 bug fix: znajdź ENTRY i EXIT nodes na konkretnym torze stacyjnym.
        /// Entry = node na track closest to prevPos (kierunek wjazdu).
        /// Exit = node na track closest to nextPos (kierunek wyjazdu).
        /// Używane przez RebuildRouteViaTrackWaypoints jako PARA waypoints (zamiast pojedynczego)
        /// — wymusza linear traversal toru w direction jazdy, eliminuje "zawijasy" gdzie pathfinder
        /// wchodzi na tor i wraca przez inny.
        ///
        /// Returns (-1, -1) jeśli żaden node z trackRef w radius. Returns (n, n) jeśli single node match.
        /// </summary>
        public (int entry, int exit) FindEntryExitNodesOnTrack(
            int stationNodeId, string trackRef,
            Vector2 prevPos, Vector2 nextPos,
            float radiusM = 500f)
        {
            if (stationNodeId < 0 || stationNodeId >= _nodes.Count || string.IsNullOrEmpty(trackRef))
                return (-1, -1);

            var stationPos = _nodes[stationNodeId].position;
            FindNodesInRadius(stationPos, radiusM, _nodesInRadiusBuffer);

            int entry = -1, exit = -1;
            float bestEntryDist = float.MaxValue, bestExitDist = float.MaxValue;
            int matchCount = 0;

            foreach (int n in _nodesInRadiusBuffer)
            {
                var node = _nodes[n];
                if (node.edgeIds == null) continue;

                bool matches = false;
                foreach (int eid in node.edgeIds)
                {
                    var edge = _edges[eid];
                    if (edge.metadata != null
                        && edge.metadata.TryGetValue("railway:track_ref", out var tr)
                        && tr == trackRef)
                    {
                        matches = true;
                        break;
                    }
                }
                if (!matches) continue;
                matchCount++;

                var pos = node.position;
                float dEntry = (pos - prevPos).sqrMagnitude;
                float dExit = (pos - nextPos).sqrMagnitude;
                if (dEntry < bestEntryDist) { bestEntryDist = dEntry; entry = n; }
                if (dExit < bestExitDist) { bestExitDist = dExit; exit = n; }
            }
            return (entry, exit);
        }

        /// <summary>
        /// Buduje szczegółową polyline trasy z geometrii krawędzi (oryginalne vertexy OSM).
        /// Zwraca listę pozycji + mapping nodeIds index → polyline index.
        /// </summary>
        public List<Vector2> BuildRoutePolyline(List<int> nodeIds, out int[] nodeIdxToPolyIdx)
        {
            var polyline = new List<Vector2>();
            nodeIdxToPolyIdx = nodeIds != null ? new int[nodeIds.Count] : System.Array.Empty<int>();

            if (nodeIds == null || nodeIds.Count < 2)
            {
                if (nodeIds != null && nodeIds.Count == 1)
                {
                    polyline.Add(_nodes[nodeIds[0]].position);
                    nodeIdxToPolyIdx[0] = 0;
                }
                return polyline;
            }

            // Pierwszy node
            nodeIdxToPolyIdx[0] = 0;

            for (int i = 0; i < nodeIds.Count - 1; i++)
            {
                int fromId = nodeIds[i];
                int toId = nodeIds[i + 1];
                int eid = FindEdgeBetween(fromId, toId);

                if (eid >= 0 && _edges[eid].geometry != null && _edges[eid].geometry.Count >= 2)
                {
                    var geom = _edges[eid].geometry;
                    if (polyline.Count == 0)
                    {
                        // Pierwszy edge — dodaj pełną geometrię
                        polyline.AddRange(geom);
                    }
                    else
                    {
                        // Kolejne edgi — pomijamy pierwszy punkt (duplikat z końca poprzedniego)
                        for (int g = 1; g < geom.Count; g++)
                            polyline.Add(geom[g]);
                    }
                }
                else
                {
                    // Fallback: prosta linia między node'ami
                    if (polyline.Count == 0)
                        polyline.Add(_nodes[fromId].position);
                    polyline.Add(_nodes[toId].position);
                }

                // Mapping: node i+1 odpowiada ostatniemu punktowi właśnie dodanemu
                nodeIdxToPolyIdx[i + 1] = polyline.Count - 1;
            }

            return polyline;
        }

        // ─────────────────────────────────────────────
        //  Spatial hashing
        // ─────────────────────────────────────────────

        private long CellKey(Vector2 pos)
        {
            int cx = Mathf.FloorToInt(pos.x / CellSize);
            int cy = Mathf.FloorToInt(pos.y / CellSize);
            return ((long)(uint)cx << 32) | (uint)cy;
        }

        /// <summary>Zwraca istniejący węzeł blisko pozycji (tolerancja = CellSize) albo -1.</summary>
        public int FindNodeAt(Vector2 pos)
        {
            int cx = Mathf.FloorToInt(pos.x / CellSize);
            int cy = Mathf.FloorToInt(pos.y / CellSize);
            float toleranceSq = CellSize * CellSize;

            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                long key = ((long)(uint)(cx + dx) << 32) | (uint)(cy + dy);
                if (!_spatialGrid.TryGetValue(key, out var nodeIds)) continue;
                foreach (int nid in nodeIds)
                {
                    if ((_nodes[nid].position - pos).sqrMagnitude <= toleranceSq)
                        return nid;
                }
            }
            return -1;
        }

        /// <summary>Zwraca istniejący węzeł lub tworzy nowy na podanej pozycji.</summary>
        public int GetOrCreateNode(Vector2 pos)
        {
            int existing = FindNodeAt(pos);
            if (existing >= 0) return existing;

            int id = _nodes.Count;
            _nodes.Add(new Node
            {
                id = id,
                position = pos,
                edgeIds = new List<int>()
            });

            long key = CellKey(pos);
            if (!_spatialGrid.TryGetValue(key, out var list))
            {
                list = new List<int>();
                _spatialGrid[key] = list;
            }
            list.Add(id);
            return id;
        }

        /// <summary>
        /// Znajduje najbliższy węzeł w zadanym promieniu (np. do snap klikanej stacji).
        /// O(k) gdzie k = liczba węzłów w przyległych komórkach.
        /// </summary>
        public int FindNearestNode(Vector2 pos, float maxDistanceM)
        {
            int cx = Mathf.FloorToInt(pos.x / CellSize);
            int cy = Mathf.FloorToInt(pos.y / CellSize);
            int cellRadius = Mathf.CeilToInt(maxDistanceM / CellSize);

            int bestId = -1;
            float bestSqDist = maxDistanceM * maxDistanceM;

            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            for (int dy = -cellRadius; dy <= cellRadius; dy++)
            {
                long key = ((long)(uint)(cx + dx) << 32) | (uint)(cy + dy);
                if (!_spatialGrid.TryGetValue(key, out var nodeIds)) continue;
                foreach (int nid in nodeIds)
                {
                    float sq = (_nodes[nid].position - pos).sqrMagnitude;
                    if (sq < bestSqDist)
                    {
                        bestSqDist = sq;
                        bestId = nid;
                    }
                }
            }
            return bestId;
        }

        /// <summary>
        /// Znajduje WSZYSTKIE węzły w zadanym promieniu — alternatywa dla brute-force
        /// pętli po graph.NodeCount. O(k) gdzie k = liczba węzłów w przyległych komórkach.
        /// Używane np. w StationTrackData.Generate / FindTracksForStop dla lookupu torów
        /// w okolicy stacji (radius ~300m) bez skanowania całego grafu.
        /// </summary>
        public void FindNodesInRadius(Vector2 pos, float radiusM, List<int> output)
        {
            if (output == null) return;
            output.Clear();

            int cx = Mathf.FloorToInt(pos.x / CellSize);
            int cy = Mathf.FloorToInt(pos.y / CellSize);
            int cellRadius = Mathf.CeilToInt(radiusM / CellSize);
            float radiusSq = radiusM * radiusM;

            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            for (int dy = -cellRadius; dy <= cellRadius; dy++)
            {
                long key = ((long)(uint)(cx + dx) << 32) | (uint)(cy + dy);
                if (!_spatialGrid.TryGetValue(key, out var nodeIds)) continue;
                foreach (int nid in nodeIds)
                {
                    if ((_nodes[nid].position - pos).sqrMagnitude <= radiusSq)
                        output.Add(nid);
                }
            }
        }

        public Node GetNode(int id) => _nodes[id];
        public Edge GetEdge(int id) => _edges[id];

        /// <summary>
        /// M-TimetableUX 2026-05-11: propagacja `ref` peronu na `railway:track_ref` najbliższej krawędzi.
        ///
        /// W OSM peron (railway=platform) ma tag `ref` (np. "2") oznaczający numer toru przy którym
        /// jest. Tor (railway=rail) zwykle NIE ma `railway:track_ref` w danych OSM (tag jest opcjonalny,
        /// rzadko wpisywany). Semantyka: peron 2 → znajdujący się obok tor JEST tor 2.
        ///
        /// Po propagacji `FindNodeOnTrack`, `StationTrackData.Generate`, force-route w pathfinderze —
        /// wszystkie widzą spójny `railway:track_ref` na edges.
        ///
        /// Algorytm: dla każdego peronu z platformName (= ref), znajdź najbliższą edge w 30m radius
        /// która jeszcze nie ma `railway:track_ref` lub ma matching. Edge.metadata jest shared między
        /// forward+backward (i czasem między edges tego samego way) — modyfikacja propaguje na cały
        /// fizyczny tor.
        /// </summary>
        public void PropagateTrackRefsFromPlatforms(IReadOnlyList<RailwayManager.Timetable.StationPlatform> platforms)
        {
            if (platforms == null || platforms.Count == 0) return;

            // GLOBAL-OPTIMAL ASSIGNMENT (2026-05-11 v3):
            // Stara propagacja per-peron z "skip if has track_ref" zawodziła gdy OSM way miał
            // `railway:track_ref` ustawiony na całym way (np. Królewo Malborskie: OSM ma "2" na
            // wszystkich 4 edges; peron 1 nie mógł nadpisać). Realnie OSM data ma błędy/braki —
            // peron geometrycznie BLIŻEJ edge powinien definiować ref toru, niezależnie od OSM.
            //
            // Algorytm:
            // 1. Per platform z numeric ref (single, nie multi-ref "1;2"), znajdź edges w 30m
            //    od centroid peronu. Notuj perpendicular distance per (edge, ref).
            // 2. Edge → najbliższy peron-ref (perpDist minimum) wygrywa global assignment.
            // 3. Apply: ustaw edge.metadata["railway:track_ref"] = ref najbliższego peronu.
            //    OVERRIDE existing OSM track_ref (peron geographically bliżej = ground truth).
            // 4. Lazy copy metadata per (segmentId + originalMetadataRef) żeby nie kollidować
            //    z sąsiednimi segmentami tego samego OSM way (gdy way obejmuje kilka physical edges).

            // Step 1: per-edge zbieraj kandydatów (najbliższy peron-ref)
            var edgeToBestPlat = new Dictionary<int, (string trackRef, float distSq, Vector2 platPos)>();
            var nearbyNodes = new List<int>();

            foreach (var plat in platforms)
            {
                if (plat == null) continue;
                if (string.IsNullOrEmpty(plat.platformName) || plat.platformName == "?") continue;
                if (plat.stationNodeId < 0 || plat.stationNodeId >= _nodes.Count) continue;

                var platPos = plat.position;
                if (platPos == Vector2.zero) platPos = _nodes[plat.stationNodeId].position;

                // Multi-ref peron (np. "1;2" wyspowy): traktuj jako single peron z pierwszym ref.
                // Multi-ref propagation jest niejasna geometrycznie (jeden centroid, dwa tory) —
                // best effort assignment dla pierwszego ref tylko.
                foreach (var refRaw in plat.platformName.Split(';'))
                {
                    var trackRef = refRaw.Trim();
                    if (string.IsNullOrEmpty(trackRef)) continue;

                    FindNodesInRadius(platPos, 30f, nearbyNodes);
                    var seenEdges = new HashSet<int>();

                    foreach (int nid in nearbyNodes)
                    {
                        var n = _nodes[nid];
                        if (n.edgeIds == null) continue;
                        foreach (int eid in n.edgeIds)
                        {
                            if (eid < 0 || eid >= _edges.Count) continue;
                            if (!seenEdges.Add(eid)) continue;

                            var edge = _edges[eid];
                            var fromPos = _nodes[edge.fromNodeId].position;
                            var toPos = _nodes[edge.toNodeId].position;
                            float distSq = PointToSegmentDistanceSq(platPos, fromPos, toPos);

                            // Edge → wygrywa peron z minimum perpDist
                            if (!edgeToBestPlat.TryGetValue(eid, out var existing) || distSq < existing.distSq)
                            {
                                edgeToBestPlat[eid] = (trackRef, distSq, platPos);
                            }
                        }
                    }

                    break; // tylko pierwszy ref z multi-ref peronu
                }
            }

            // Step 2: apply track_ref per edge. Group by (segmentId, originalMetaRef) — siblings
            // dostają tę samą lazy copy metadata.
            int assigned = 0;
            int overridden = 0;

            // Pre-compute: edge → (segId, origMeta) → set of edge IDs (siblings)
            // żeby lazy copy raz per group.
            var groupKey = new Dictionary<int, (int segId, Dictionary<string, string> origMeta)>(edgeToBestPlat.Count);
            foreach (var (eid, _) in edgeToBestPlat)
            {
                groupKey[eid] = (_edges[eid].segmentId, _edges[eid].metadata);
            }

            // Per edge: lazy copy + assign. Multiple edges with same (segId, origMeta) share new copy
            // tylko jeśli mają ten sam target trackRef. Inaczej osobne copies.
            var groupMetaCache = new Dictionary<(int segId, Dictionary<string, string> origMeta, string trackRef), Dictionary<string, string>>();

            foreach (var (eid, info) in edgeToBestPlat)
            {
                var (segId, origMeta) = groupKey[eid];
                string trackRef = info.trackRef;

                // Sprawdź czy już ma matching track_ref (no-op).
                if (origMeta != null
                    && origMeta.TryGetValue("railway:track_ref", out var existing)
                    && existing == trackRef
                    && _edges[eid].metadata == origMeta)
                {
                    // Already correct (and metadata reference unchanged) — skip.
                    continue;
                }

                // Get-or-create lazy copy for this (segId, origMeta, trackRef) trio.
                var cacheKey = (segId, origMeta, trackRef);
                if (!groupMetaCache.TryGetValue(cacheKey, out var newMeta))
                {
                    newMeta = origMeta != null
                        ? new Dictionary<string, string>(origMeta)
                        : new Dictionary<string, string>();
                    if (newMeta.ContainsKey("railway:track_ref") && newMeta["railway:track_ref"] != trackRef)
                        overridden++;
                    newMeta["railway:track_ref"] = trackRef;
                    groupMetaCache[cacheKey] = newMeta;
                }

                var e = _edges[eid];
                e.metadata = newMeta;
                _edges[eid] = e;
                assigned++;

                // Sibling (same segmentId, same originalMetaRef): apply też.
                for (int dx = -3; dx <= 3; dx++)
                {
                    if (dx == 0) continue;
                    int sibEid = eid + dx;
                    if (sibEid < 0 || sibEid >= _edges.Count) continue;
                    if (_edges[sibEid].segmentId != segId) continue;
                    if (_edges[sibEid].metadata != origMeta) continue;
                    var es = _edges[sibEid];
                    es.metadata = newMeta;
                    _edges[sibEid] = es;
                }
            }

            Log.Info($"[PathfindingGraph] PropagateTrackRefsFromPlatforms v3: assigned={assigned} edges "
                   + $"from {platforms.Count} platforms (overrode OSM track_ref on {overridden} edges, "
                   + $"groups={groupMetaCache.Count})");
        }

        /// <summary>
        /// Squared distance od punktu p do segment line (a, b). Standard perpendicular projection.
        /// </summary>
        private static float PointToSegmentDistanceSq(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float abLenSq = ab.sqrMagnitude;
            if (abLenSq < 1e-9f) return (p - a).sqrMagnitude; // degenerate: a == b
            float t = Vector2.Dot(p - a, ab) / abLenSq;
            t = Mathf.Clamp01(t);
            var closest = a + t * ab;
            return (p - closest).sqrMagnitude;
        }

        // ─────────────────────────────────────────────
        //  Bulk-load (M-PL B2: pre-built init-state.bin)
        // ─────────────────────────────────────────────

        /// <summary>
        /// Bulk-load PathfindingGraph state z pre-built danych. Wywoływana przez
        /// <see cref="GraphDataUnityAdapter"/> po deserializacji init-state-{country}.bin.
        ///
        /// Mirror metody w bibliotece RailwayManager.GraphData.GraphPathfindingGraph
        /// — interpretuje surowe pola bez ponownego budowania (omija Step 1-4 z BuildFromFeaturesUnionFind).
        ///
        /// Per-vertex pozycje, EdgeIds i metadane przekazywane gotowe — z zachowaniem
        /// kontraktów wewnętrznych (spatial hash rebuilds, JunctionNodeIds preserved).
        /// </summary>
        public void LoadFromSerializedData(
            float cellSize,
            IReadOnlyList<Node> nodes,
            IReadOnlyList<Edge> edges,
            HashSet<int> junctionNodeIds)
        {
            CellSize = cellSize > 0 ? cellSize : 1.0f;
            _nodes.Clear();
            _edges.Clear();
            _spatialGrid.Clear();
            JunctionNodeIds = junctionNodeIds ?? new HashSet<int>();

            if (nodes != null)
            {
                if (_nodes.Capacity < nodes.Count) _nodes.Capacity = nodes.Count;
                for (int i = 0; i < nodes.Count; i++)
                {
                    var n = nodes[i];
                    _nodes.Add(n);

                    long key = CellKey(n.position);
                    if (!_spatialGrid.TryGetValue(key, out var list))
                    {
                        list = new List<int>();
                        _spatialGrid[key] = list;
                    }
                    list.Add(n.id);
                }
            }

            if (edges != null)
            {
                if (_edges.Capacity < edges.Count) _edges.Capacity = edges.Count;
                for (int i = 0; i < edges.Count; i++)
                    _edges.Add(edges[i]);
            }
        }
    }
}
