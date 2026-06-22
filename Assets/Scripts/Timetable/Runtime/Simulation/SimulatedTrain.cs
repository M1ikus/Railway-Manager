using System.Collections.Generic;
using UnityEngine;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// Stan runtime aktywnego pociągu na mapie. Companion class do <see cref="TrainRun"/>
    /// — przechowuje dane runtime-only (prędkość, stan, visual GO) które nie powinny
    /// trafiać do save/load.
    /// </summary>
    public class SimulatedTrain
    {
        // ── Referencje (read-only po init) ──────────────────────────

        public readonly TrainRun trainRun;
        public readonly Timetable timetable;
        public readonly Route route;

        // ── Runtime state ───────────────────────────────────────────

        /// <summary>Aktualna prędkość [m/s]. 0 = stoi.</summary>
        public float currentSpeedMps;

        /// <summary>M6-4: ostatnia pozycja na trasie [m] — delta do kosztów operacyjnych per km.</summary>
        public float lastCostDistanceM;

        // ── M7-3 Breakdown state ─────────────────────────────────

        /// <summary>M7-3: Który komponent padł (gdy state=BrokenDown/AwaitingRescue). -1 = brak awarii.</summary>
        public int brokenComponentIndex = -1;

        /// <summary>M7-3: Game time (sec) kiedy pociąg się zepsuł — do timerów self-repair i alertów.</summary>
        public long breakdownStartedGameTime;

        /// <summary>M7-3: Zaplanowany czas self-repair attempt (start + 5-15 min). 0 gdy nie w self-repair.</summary>
        public long selfRepairAttemptGameTime;

        /// <summary>M7-3: ID vehicleId który padł (do znalezienia FleetVehicleData dla self-repair check).</summary>
        public int brokenVehicleId = -1;

        /// <summary>M7-3b: Drzwi zepsute — na kolejnych postojach +60s dwell.</summary>
        public bool doorsBroken;

        /// <summary>M7-3b: Speed limit po awarii kół/świateł [km/h]. 0 = brak limitu.</summary>
        public int wheelsSpeedLimitedKmh;

        /// <summary>Indeks następnego przystanku w timetable.stops (0 = stacja startowa, jedzie do stops[1]).</summary>
        public int currentStopIndex;

        /// <summary>Bieżący stan pociągu w symulacji.</summary>
        public TrainState state;

        // ── Platform-aware re-routing ───────────────────────────────

        /// <summary>Efektywne nodeIds trasy — przeliczone przez waypoint routing
        /// jeśli rozkład ma przypisane tory. Null = brak re-routingu, użyj route.nodeIds.</summary>
        public readonly List<int> effectiveNodeIds;

        // ── Cached detailed polyline (edge geometry) ────────────────

        /// <summary>Szczegółowa polyline z geometrii krawędzi — oryginalne pozycje vertexów OSM.
        /// Używana do interpolacji pozycji pociągu i rysowania overlayów.</summary>
        public readonly Vector2[] cachedPolyline;

        /// <summary>Kumulatywny dystans w każdym punkcie polyline.
        /// cachedPolyline[i] jest w odległości polylineCumulDist[i] metrów od startu.</summary>
        public readonly float[] polylineCumulDist;

        /// <summary>Mapping: route.nodeIds[i] → cachedPolyline[nodeIdxToPolyIdx[i]].
        /// Pozwala konwertować indeksy blokowe (w nodeIds) na indeksy polyline.</summary>
        public readonly int[] nodeIdxToPolyIdx;

        // ── Dystanse przystanków ────────────────────────────────────

        /// <summary>
        /// Dystans od startu trasy [m] per przystanek (indeksowane jak timetable.stops).
        /// Obliczane z Route.stations bo TimetableStop.distanceFromStartM nie jest wypełniane.
        /// </summary>
        public readonly float[] stopDistancesM;

        // ── Profil prędkości trasy ──────────────────────────────────

        /// <summary>Kumulatywny dystans na końcu segmentu i (między nodeIds[i] a nodeIds[i+1]).</summary>
        public readonly float[] segmentEndDistM;

        /// <summary>Vmax [m/s] per segment trasy (z grafu OSM / edge.maxSpeedKmh).</summary>
        public readonly float[] segmentMaxSpeedMps;

        // ── Bloki semaforowe (Etap 3) ───────────────────────────────

        /// <summary>blockKey per blok wzdłuż trasy (te same co planning-time ReservationManager).</summary>
        public readonly int[] routeBlockKeys;

        /// <summary>Dystans [m] na którym pociąg wjeżdża w dany blok.</summary>
        public readonly float[] blockEntryDistM;

        /// <summary>Dystans [m] na którym pociąg wyjeżdża z danego bloku.</summary>
        public readonly float[] blockExitDistM;

        /// <summary>Indeks startu bloku w route.nodeIds (do debug overlay rysowania polyline).</summary>
        public readonly int[] blockStartRouteIdx;

        /// <summary>Indeks końca bloku w route.nodeIds.</summary>
        public readonly int[] blockEndRouteIdx;

        /// <summary>Ile bloków na trasie.</summary>
        public readonly int routeBlockCount;

        /// <summary>Indeks bieżącego bloku (w routeBlockKeys). Aktualizowany runtime.</summary>
        public int currentBlockIndex;

        // ── Timing ──────────────────────────────────────────────────

        /// <summary>Absolutna godzina odjazdu ze stacji startowej [sekundy od midnight].</summary>
        public readonly float departureTimeOfDaySec;

        // ── Visual (Etap 2) ─────────────────────────────────────────

        /// <summary>Reference do GameObject na mapie (prostokąt/sprite). Null w Etap 1.</summary>
        public GameObject visual;

        /// <summary>Cache transform (unikamy GetComponent per tick).</summary>
        public Transform visualTransform;

        // ── Constructor ─────────────────────────────────────────────

        public SimulatedTrain(TrainRun trainRun, Timetable timetable, Route route,
                              PathfindingGraph graph)
        {
            this.trainRun = trainRun;
            this.timetable = timetable;
            this.route = route;

            departureTimeOfDaySec = trainRun.startMinutesFromMidnight * 60f;
            state = TrainState.WaitingToDepart;
            currentStopIndex = 0;
            currentSpeedMps = 0f;

            // ── Platform-aware re-routing: aktywne po naprawie track-ref aware merge ──
            effectiveNodeIds = BuildEffectiveRoute(timetable, route, graph);
            var activeNodeIds = effectiveNodeIds ?? route.nodeIds;

            // ── Detailed polyline z edge geometry ──
            if (graph != null && activeNodeIds != null && activeNodeIds.Count >= 2)
            {
                var polyList = graph.BuildRoutePolyline(activeNodeIds, out int[] mapping);
                cachedPolyline = polyList.ToArray();
                nodeIdxToPolyIdx = mapping;

                polylineCumulDist = new float[cachedPolyline.Length];
                for (int i = 1; i < cachedPolyline.Length; i++)
                    polylineCumulDist[i] = polylineCumulDist[i - 1]
                        + Vector2.Distance(cachedPolyline[i - 1], cachedPolyline[i]);
            }
            else
            {
                cachedPolyline = System.Array.Empty<Vector2>();
                polylineCumulDist = System.Array.Empty<float>();
                nodeIdxToPolyIdx = System.Array.Empty<int>();
            }

            // Oblicz dystanse przystanków z dokładnej polyline (activeNodeIds, nie route.nodeIds)
            stopDistancesM = BuildStopDistances(timetable.stops, activeNodeIds, route, graph, nodeIdxToPolyIdx, polylineCumulDist);

            // Profil prędkości — per segment z grafu OSM
            BuildSpeedProfileFrom(activeNodeIds, graph, out segmentEndDistM, out segmentMaxSpeedMps, route.totalLengthM);

            // Bloki semaforowe
            var init = TimetableInitializer.Instance;
            if (init != null && graph != null && activeNodeIds != null && activeNodeIds.Count >= 2)
            {
                var blocks = ReservationManager.BuildRouteBlocks(timetable.stops, route, init);
                routeBlockCount = blocks.Count;
                routeBlockKeys = new int[routeBlockCount];
                blockEntryDistM = new float[routeBlockCount];
                blockExitDistM = new float[routeBlockCount];
                blockStartRouteIdx = new int[routeBlockCount];
                blockEndRouteIdx = new int[routeBlockCount];

                for (int i = 0; i < routeBlockCount; i++)
                {
                    routeBlockKeys[i] = blocks[i].blockKey;
                    int startIdx = Mathf.Clamp(blocks[i].startRouteIdx, 0, activeNodeIds.Count - 1);
                    int endIdx = Mathf.Clamp(blocks[i].endRouteIdx, 0, activeNodeIds.Count - 1);

                    if (nodeIdxToPolyIdx.Length > 0 && polylineCumulDist.Length > 0)
                    {
                        int polyStart = nodeIdxToPolyIdx[Mathf.Clamp(startIdx, 0, nodeIdxToPolyIdx.Length - 1)];
                        int polyEnd = nodeIdxToPolyIdx[Mathf.Clamp(endIdx, 0, nodeIdxToPolyIdx.Length - 1)];
                        blockEntryDistM[i] = polylineCumulDist[Mathf.Clamp(polyStart, 0, polylineCumulDist.Length - 1)];
                        blockExitDistM[i] = polylineCumulDist[Mathf.Clamp(polyEnd, 0, polylineCumulDist.Length - 1)];
                    }

                    blockStartRouteIdx[i] = blocks[i].startRouteIdx;
                    blockEndRouteIdx[i] = blocks[i].endRouteIdx;
                }

                RailwayManager.Core.Log.Info($"[SimulatedTrain] Route blocks: {routeBlockCount} blocks");
            }
            else
            {
                routeBlockCount = 0;
                routeBlockKeys = System.Array.Empty<int>();
                blockEntryDistM = System.Array.Empty<float>();
                blockExitDistM = System.Array.Empty<float>();
                blockStartRouteIdx = System.Array.Empty<int>();
                blockEndRouteIdx = System.Array.Empty<int>();
                RailwayManager.Core.Log.Warn("[SimulatedTrain] No blocks — TimetableInitializer not ready");
            }
            currentBlockIndex = 0;

            float totalPolyLen = polylineCumulDist.Length > 0 ? polylineCumulDist[polylineCumulDist.Length - 1] : 0f;
            RailwayManager.Core.Log.Info($"[SimulatedTrain] Polyline: {cachedPolyline.Length} pts " +
                $"(from {activeNodeIds?.Count ?? 0} nodes" +
                (effectiveNodeIds != null ? $", re-routed via track waypoints" : "") +
                $"), totalLen={totalPolyLen:F0}m");
        }

        /// <summary>
        /// Buduje efektywną trasę z uwzględnieniem przypisanych torów stacyjnych.
        /// Dla każdego stopu z trackRef, znajduje waypoint na tym torze i routuje przez niego.
        /// Zwraca null jeśli brak track assignments lub re-routing niepotrzebny.
        /// </summary>
        static List<int> BuildEffectiveRoute(Timetable timetable, Route route, PathfindingGraph graph)
        {
            if (graph == null || route.nodeIds == null || route.nodeIds.Count < 2)
                return null;

            // Zbierz waypoints. Pierwsza i ostatnia stacja: ZASTĄP endpoint torem
            // (nie dodawaj obok), inaczej trasa robi zygzak station_center → track → station_center.
            var stops = timetable.stops;
            var waypoints = new List<int>();
            int trackWaypoints = 0;

            // Start: tor pierwszej stacji zamiast route.nodeIds[0], jeśli ma trackRef
            int startNode = route.nodeIds[0];
            if (stops.Count > 0 && !string.IsNullOrEmpty(stops[0].trackRef))
            {
                int tn = graph.FindNodeOnTrack(stops[0].stationNodeId, stops[0].trackRef);
                if (tn >= 0) { startNode = tn; trackWaypoints++; }
            }
            waypoints.Add(startNode);

            // Pośrednie stopy
            for (int i = 1; i < stops.Count - 1; i++)
            {
                var stop = stops[i];
                if (string.IsNullOrEmpty(stop.trackRef)) continue;
                int wp = graph.FindNodeOnTrack(stop.stationNodeId, stop.trackRef);
                if (wp < 0) continue;
                if (wp == waypoints[waypoints.Count - 1]) continue;
                waypoints.Add(wp);
                trackWaypoints++;
            }

            // End: tor ostatniej stacji zamiast route.nodeIds[last], jeśli ma trackRef
            int endNode = route.nodeIds[route.nodeIds.Count - 1];
            if (stops.Count > 1 && !string.IsNullOrEmpty(stops[stops.Count - 1].trackRef))
            {
                int tn = graph.FindNodeOnTrack(stops[stops.Count - 1].stationNodeId,
                                                stops[stops.Count - 1].trackRef);
                if (tn >= 0) { endNode = tn; trackWaypoints++; }
            }
            if (endNode != waypoints[waypoints.Count - 1])
                waypoints.Add(endNode);

            if (trackWaypoints == 0) return null; // brak track assignments — użyj route.nodeIds

            // Deduplikuj kolejne duplikaty
            for (int i = waypoints.Count - 1; i > 0; i--)
            {
                if (waypoints[i] == waypoints[i - 1])
                    waypoints.RemoveAt(i);
            }

            if (waypoints.Count < 2) return null;

            var result = RailwayPathfinder.FindPathViaWaypoints(graph, waypoints);
            if (result.success)
            {
                RailwayManager.Core.Log.Info($"[SimulatedTrain] Re-routed via {trackWaypoints} track waypoints, " +
                    $"{result.nodeIds.Count} nodes, {result.totalLengthM:F0}m " +
                    $"(was {route.nodeIds.Count} nodes, {route.totalLengthM:F0}m)");
                return result.nodeIds;
            }

            RailwayManager.Core.Log.Warn($"[SimulatedTrain] Waypoint re-routing failed — using original route");
            return null;
        }

        /// <summary>
        /// Buduje profil prędkości wzdłuż trasy z podanych nodeIds (effectiveNodeIds lub route.nodeIds).
        /// </summary>
        static void BuildSpeedProfileFrom(List<int> nodeIds, PathfindingGraph graph,
                                          out float[] endDists, out float[] maxSpeeds,
                                          float fallbackTotalLengthM)
        {
            if (graph == null || nodeIds == null || nodeIds.Count < 2)
            {
                endDists = new float[] { fallbackTotalLengthM };
                maxSpeeds = new float[] { 120f / 3.6f };
                return;
            }

            int segCount = nodeIds.Count - 1;
            endDists = new float[segCount];
            maxSpeeds = new float[segCount];

            float cumDist = 0f;
            for (int i = 0; i < segCount; i++)
            {
                int fromId = nodeIds[i];
                int toId = nodeIds[i + 1];

                int edgeId = graph.FindEdgeBetween(fromId, toId);
                float segLen;
                int speedKmh;

                if (edgeId >= 0)
                {
                    var edge = graph.GetEdge(edgeId);
                    segLen = edge.lengthM;
                    speedKmh = edge.maxSpeedKmh > 0 ? edge.maxSpeedKmh : 80;
                }
                else
                {
                    segLen = (graph.GetNode(toId).position - graph.GetNode(fromId).position).magnitude;
                    speedKmh = 80;
                }

                cumDist += segLen;
                endDists[i] = cumDist;
                maxSpeeds[i] = speedKmh / 3.6f;
            }
        }

        /// <summary>Zwraca indeks bloku dla pozycji na trasie. -1 jeśli brak bloków.</summary>
        public int GetBlockIndexAtDistance(float distanceM)
        {
            if (routeBlockCount == 0) return -1;

            // Szukamy ostatniego bloku którego entryDist <= distanceM
            int result = 0;
            for (int i = 1; i < routeBlockCount; i++)
            {
                if (blockEntryDistM[i] <= distanceM)
                    result = i;
                else
                    break;
            }
            return result;
        }

        /// <summary>Binary search: zwraca Vmax [m/s] dla pozycji na trasie.</summary>
        public float GetVmaxAtDistance(float distanceM)
        {
            if (segmentEndDistM == null || segmentEndDistM.Length == 0)
                return 120f / 3.6f;

            // Binary search — segmentEndDistM jest posortowane rosnąco
            int lo = 0, hi = segmentEndDistM.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (segmentEndDistM[mid] < distanceM)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            return segmentMaxSpeedMps[lo];
        }

        /// <summary>
        /// Oblicza dystans każdego przystanku od startu trasy.
        /// Używa dokładnych dystansów z polyline (polylineCumulDist) gdy dostępne.
        /// </summary>
        static float[] BuildStopDistances(List<TimetableStop> stops, List<int> activeNodeIds,
                                          Route route, PathfindingGraph graph,
                                          int[] nodeIdxToPolyIdx, float[] polylineCumulDist)
        {
            var distances = new float[stops.Count];
            if (stops.Count == 0) return distances;

            var nodeIds = activeNodeIds ?? route.nodeIds;
            if (graph == null || nodeIds == null || nodeIds.Count < 2)
            {
                for (int i = 0; i < stops.Count; i++)
                    distances[i] = route.totalLengthM * ((float)i / Mathf.Max(stops.Count - 1, 1));
                return distances;
            }

            // Zbuduj dystanse per node z polyline (dokładne) lub z edge.lengthM (fallback)
            bool hasPolyline = nodeIdxToPolyIdx != null && nodeIdxToPolyIdx.Length == nodeIds.Count
                               && polylineCumulDist != null && polylineCumulDist.Length > 0;

            var nodeDistances = new float[nodeIds.Count];
            if (hasPolyline)
            {
                for (int i = 0; i < nodeIds.Count; i++)
                {
                    int polyIdx = nodeIdxToPolyIdx[i];
                    nodeDistances[i] = polyIdx < polylineCumulDist.Length
                        ? polylineCumulDist[polyIdx] : 0f;
                }
            }
            else
            {
                for (int i = 1; i < nodeIds.Count; i++)
                {
                    int eid = graph.FindEdgeBetween(nodeIds[i - 1], nodeIds[i]);
                    float segLen = eid >= 0
                        ? graph.GetEdge(eid).lengthM
                        : (graph.GetNode(nodeIds[i]).position - graph.GetNode(nodeIds[i - 1]).position).magnitude;
                    nodeDistances[i] = nodeDistances[i - 1] + segLen;
                }
            }

            // Lookup: nodeId → dystans na polyline (pierwszy trafiony)
            var nodeDistLookup = new Dictionary<int, float>(nodeIds.Count);
            for (int i = 0; i < nodeIds.Count; i++)
            {
                if (!nodeDistLookup.ContainsKey(nodeIds[i]))
                    nodeDistLookup[nodeIds[i]] = nodeDistances[i];
            }

            int matched = 0;
            for (int i = 0; i < stops.Count; i++)
            {
                if (nodeDistLookup.TryGetValue(stops[i].stationNodeId, out float dist))
                {
                    distances[i] = dist;
                    matched++;
                }
                else
                {
                    distances[i] = route.totalLengthM * ((float)i / Mathf.Max(stops.Count - 1, 1));
                }
            }

            float totalPolyLen = nodeDistances.Length > 0 ? nodeDistances[nodeDistances.Length - 1] : 0f;
            RailwayManager.Core.Log.Info($"[SimulatedTrain] BuildStopDistances: " +
                $"{matched}/{stops.Count} matched, polyline {nodeIds.Count} nodes, " +
                $"totalLen={totalPolyLen:F0}m vs route.totalLengthM={route.totalLengthM:F0}m");

            return distances;
        }
    }

    /// <summary>
    /// Stan pociągu w symulacji M9a. Kolejność wartości odpowiada
    /// typowym przejściom: WaitingToDepart → Running → StoppedAtStation → ... → Completed.
    /// </summary>
    public enum TrainState
    {
        /// <summary>Na stacji startowej, czeka na godzinę odjazdu.</summary>
        WaitingToDepart,

        /// <summary>W ruchu między stacjami.</summary>
        Running,

        /// <summary>Zatrzymany na stacji (postój pasażerski lub techniczny).</summary>
        StoppedAtStation,

        /// <summary>Etap 3: zatrzymany przed zajętym blokiem (czerwone światło).</summary>
        BlockedBySignal,

        /// <summary>M7-3: Awaria w trasie — pociąg zatrzymany, próba self-repair.</summary>
        BrokenDown,

        /// <summary>M7-3: Czeka na rescue lokomotywę (self-repair fail).</summary>
        AwaitingRescue,

        /// <summary>Dotarł do stacji końcowej — gotowy do despawnu.</summary>
        Completed
    }
}
