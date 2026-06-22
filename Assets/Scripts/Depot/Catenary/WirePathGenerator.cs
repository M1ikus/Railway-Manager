using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    /// <summary>
    /// Etap 2 pipeline'u sieci trakcyjnej: generacja logicznych linii przewodów.
    /// Dla każdego toru/relacji przejazdowej tworzy WirePath z punktami kontrolnymi.
    /// </summary>
    public static class WirePathGenerator
    {
        private const float DefaultContactWireHeight = 5.5f;
        private const float ZigzagAmplitude = 0.3f;  // boczne odsunięcie zygzaka (m)
        private const float SwitchZoneDensity = 3f;   // co ile metrów punkt w strefie rozjazdowej

        private static int nextWirePathId = 0;

        /// <summary>
        /// Generuje logiczne linie przewodów dla wszystkich stref.
        /// </summary>
        public static List<WirePath> GenerateWirePaths(
            TrackGraph graph,
            List<CatenaryZone> zones,
            List<int> electrifiedTrackIds)
        {
            nextWirePathId = 0;
            var allPaths = new List<WirePath>();

            // Tory obsłużone przez strefy specjalne (SwitchHead) — nie generuj podwójnie
            var handledEdges = new HashSet<int>();

            foreach (var zone in zones)
            {
                List<WirePath> zonePaths;

                switch (zone.Type)
                {
                    case ZoneType.SwitchHead:
                        zonePaths = GenerateForSwitchHeadZone(zone, graph);
                        break;
                    case ZoneType.Curve:
                        zonePaths = GenerateForCurveZone(zone, graph);
                        break;
                    case ZoneType.ParallelStation:
                        zonePaths = GenerateForParallelStationZone(zone, graph);
                        break;
                    default: // Straight
                        zonePaths = GenerateForStraightZone(zone, graph);
                        break;
                }

                foreach (var path in zonePaths)
                {
                    path.SourceZoneId = zone.ZoneId;
                    allPaths.Add(path);
                }
            }

            // Post-processing: snapuj endpointy wire paths do wspólnych junction node'ów
            // Gwarantuje idealne połączenie przewodów na rozjazdach (brak ~5cm luk)
            SnapEndpointsToJunctionNodes(allPaths, graph);

            Log.Info($"[WirePathGenerator] {allPaths.Count} wire paths, " +
                      $"{allPaths.Sum(p => p.ControlPoints.Count)} control points");

            return allPaths;
        }

        // ═══════════════════════════════════════════
        //  STRAIGHT — prosty tor
        // ═══════════════════════════════════════════

        private static List<WirePath> GenerateForStraightZone(CatenaryZone zone, TrackGraph graph)
        {
            var paths = new List<WirePath>();

            foreach (int trackId in zone.TrackIds)
            {
                var poly = graph.GetTrackPolyline(trackId);
                if (poly == null || poly.Count < 2) continue;

                float totalLen = TrackGeometry.CalculatePolylineLength(poly);
                if (totalLen < 1f) continue;

                var path = CreateWirePath(trackId);
                var distances = ComputeSupportDistances(poly, totalLen);

                bool longSide = true;
                foreach (float dist in distances)
                {
                    var cp = CreateControlPoint(path.WirePathId, trackId, poly, totalLen, dist, longSide);
                    cp.IsSupportCandidate = true;
                    path.ControlPoints.Add(cp);
                    longSide = !longSide;
                }

                if (path.ControlPoints.Count >= 2)
                    paths.Add(path);
            }

            return paths;
        }

        // ═══════════════════════════════════════════
        //  CURVE — łuk
        // ═══════════════════════════════════════════

        private static List<WirePath> GenerateForCurveZone(CatenaryZone zone, TrackGraph graph)
        {
            // Łuki: gęstsze punkty, brak zygzaka (łuk sam zapewnia zużycie nakładki)
            var paths = new List<WirePath>();

            foreach (int trackId in zone.TrackIds)
            {
                var poly = graph.GetTrackPolyline(trackId);
                if (poly == null || poly.Count < 2) continue;

                float totalLen = TrackGeometry.CalculatePolylineLength(poly);
                if (totalLen < 1f) continue;

                var path = CreateWirePath(trackId);
                var distances = ComputeSupportDistances(poly, totalLen);

                foreach (float dist in distances)
                {
                    var cp = CreateControlPoint(path.WirePathId, trackId, poly, totalLen, dist, true);
                    cp.ZigzagOffset = 0f; // brak zygzaka na łuku
                    cp.IsSupportCandidate = true;
                    path.ControlPoints.Add(cp);
                }

                if (path.ControlPoints.Count >= 2)
                    paths.Add(path);
            }

            return paths;
        }

        // ═══════════════════════════════════════════
        //  PARALLEL STATION — tory równoległe
        // ═══════════════════════════════════════════

        private static List<WirePath> GenerateForParallelStationZone(CatenaryZone zone, TrackGraph graph)
        {
            var paths = new List<WirePath>();
            if (zone.TrackIds.Count < 2) return GenerateForStraightZone(zone, graph);

            // Generuj wire path dla pierwszego (reference) toru
            int refTrackId = zone.TrackIds[0];
            var refPoly = graph.GetTrackPolyline(refTrackId);
            if (refPoly == null || refPoly.Count < 2) return GenerateForStraightZone(zone, graph);

            float refLen = TrackGeometry.CalculatePolylineLength(refPoly);
            var refDistances = ComputeSupportDistances(refPoly, refLen);

            // Reference path
            var refPath = CreateWirePath(refTrackId);
            bool longSide = true;
            foreach (float dist in refDistances)
            {
                var cp = CreateControlPoint(refPath.WirePathId, refTrackId, refPoly, refLen, dist, longSide);
                cp.IsSupportCandidate = true;
                refPath.ControlPoints.Add(cp);
                longSide = !longSide;
            }
            if (refPath.ControlPoints.Count >= 2)
                paths.Add(refPath);

            // Dla pozostałych torów: wyrównaj punkty do reference
            for (int t = 1; t < zone.TrackIds.Count; t++)
            {
                int trackId = zone.TrackIds[t];
                var poly = graph.GetTrackPolyline(trackId);
                if (poly == null || poly.Count < 2) continue;

                float totalLen = TrackGeometry.CalculatePolylineLength(poly);
                var path = CreateWirePath(trackId);
                bool ls = true;

                foreach (var refCp in refPath.ControlPoints)
                {
                    // Rzutuj pozycję reference na ten tor
                    float projDist = TrackGeometry.ProjectPointOnPolyline(poly, refCp.Position);
                    if (projDist < 0f || projDist > totalLen) continue;

                    var cp = CreateControlPoint(path.WirePathId, trackId, poly, totalLen, projDist, ls);
                    cp.IsSupportCandidate = true;
                    path.ControlPoints.Add(cp);
                    ls = !ls;
                }

                // Dodaj brakujące punkty na końcach (jeśli tor jest dłuższy niż reference)
                EnsureEndPoints(path, poly, totalLen);

                if (path.ControlPoints.Count >= 2)
                    paths.Add(path);
            }

            return paths;
        }

        // ═══════════════════════════════════════════
        //  SWITCH HEAD — głowica rozjazdowa
        // ═══════════════════════════════════════════

        private static List<WirePath> GenerateForSwitchHeadZone(CatenaryZone zone, TrackGraph graph)
        {
            // W strefie rozjazdowej generujemy wire path per tor,
            // ale z gęstszymi punktami i bez zygzaka.
            // Punkty stawiane co SwitchZoneDensity metrów.
            var paths = new List<WirePath>();

            foreach (int trackId in zone.TrackIds)
            {
                var poly = graph.GetTrackPolyline(trackId);
                if (poly == null || poly.Count < 2) continue;

                float totalLen = TrackGeometry.CalculatePolylineLength(poly);
                if (totalLen < 0.5f) continue;

                var path = CreateWirePath(trackId);

                // Gęste punkty kontrolne
                float step = Mathf.Min(SwitchZoneDensity, totalLen / 2f);
                int numPoints = Mathf.Max(2, Mathf.CeilToInt(totalLen / step) + 1);
                float actualStep = totalLen / (numPoints - 1);

                for (int i = 0; i < numPoints; i++)
                {
                    float dist = i * actualStep;
                    dist = Mathf.Min(dist, totalLen);

                    var cp = CreateControlPoint(path.WirePathId, trackId, poly, totalLen, dist, true);
                    cp.ZigzagOffset = 0f; // brak zygzaka w rozjazdach
                    // Tylko początek, koniec i co ~15m jako kandydaci na podpora
                    cp.IsSupportCandidate = (i == 0 || i == numPoints - 1 ||
                                             dist % 15f < actualStep);
                    path.ControlPoints.Add(cp);
                }

                if (path.ControlPoints.Count >= 2)
                    paths.Add(path);
            }

            // Połącz wire paths na rozgałęzieniach (tory dzielące node z junction)
            ConnectSwitchPaths(paths, zone, graph);

            return paths;
        }

        private static void ConnectSwitchPaths(List<WirePath> paths, CatenaryZone zone, TrackGraph graph)
        {
            // Dla każdej pary wire paths w tej strefie: jeśli ich tory dzielą Junction node,
            // oznacz je jako połączone
            for (int i = 0; i < paths.Count; i++)
            {
                for (int j = i + 1; j < paths.Count; j++)
                {
                    if (paths[i].TrackId == paths[j].TrackId) continue;

                    // Sprawdź czy tory dzielą node
                    var edgesA = graph.GetEdgesForTrack(paths[i].TrackId);
                    var edgesB = graph.GetEdgesForTrack(paths[j].TrackId);

                    var nodesA = new HashSet<int>();
                    foreach (var e in edgesA) { nodesA.Add(e.FromNodeId); nodesA.Add(e.ToNodeId); }

                    foreach (var e in edgesB)
                    {
                        if (nodesA.Contains(e.FromNodeId) || nodesA.Contains(e.ToNodeId))
                        {
                            paths[i].ConnectedWirePathIds.Add(paths[j].WirePathId);
                            paths[j].ConnectedWirePathIds.Add(paths[i].WirePathId);
                            break;
                        }
                    }
                }
            }
        }

        // ═══════════════════════════════════════════
        //  SPACING LOGIC (port z PolePlacer)
        // ═══════════════════════════════════════════

        /// <summary>
        /// Oblicza odległości wzdłuż toru, gdzie powinny stanąć podpory.
        /// Adaptacyjny spacing z tabeli PKP + wyrównanie ostatniego przęsła.
        /// </summary>
        private static List<float> ComputeSupportDistances(List<Vector3> polyline, float totalLength)
        {
            var distances = new List<float>();
            distances.Add(0f);

            float currentDist = 0f;
            while (true)
            {
                float radius = CatenarySpacing.ComputeLocalRadius(polyline, currentDist);
                float spacing = CatenarySpacing.GetSpacing(radius);

                // Skanuj w przód — najciaśniejszy łuk w następnym przęśle
                float nextSpacing = spacing;
                for (float scan = currentDist + 5f;
                     scan < currentDist + spacing && scan < totalLength;
                     scan += 5f)
                {
                    float scanRadius = CatenarySpacing.ComputeLocalRadius(polyline, scan);
                    float scanSpacing = CatenarySpacing.GetSpacing(scanRadius);
                    nextSpacing = Mathf.Min(nextSpacing, scanSpacing);
                }

                float nextDist = currentDist + nextSpacing;
                if (nextDist >= totalLength) break;

                distances.Add(nextDist);
                currentDist = nextDist;
            }

            // Wyrównanie ostatniego przęsła
            BalanceLastSpan(distances, totalLength, polyline);

            return distances;
        }

        private static void BalanceLastSpan(List<float> distances, float totalLength, List<Vector3> polyline)
        {
            if (distances.Count < 2)
            {
                if (totalLength > 0.1f) distances.Add(totalLength);
                return;
            }

            float lastPlaced = distances[distances.Count - 1];
            float remaining = totalLength - lastPlaced;
            float localRadius = CatenarySpacing.ComputeLocalRadius(polyline, lastPlaced);
            float nominalSpacing = CatenarySpacing.GetSpacing(localRadius);

            if (remaining < nominalSpacing * 0.5f && remaining > 0.1f)
            {
                // Za krótkie — rozdziel
                float redistributeFrom = distances[distances.Count - 2];
                float redistributeTotal = totalLength - redistributeFrom;
                int subdivisions = Mathf.Max(2, Mathf.RoundToInt(redistributeTotal / nominalSpacing));
                float evenSpacing = redistributeTotal / subdivisions;

                distances.RemoveAt(distances.Count - 1);
                for (int i = 1; i <= subdivisions; i++)
                    distances.Add(redistributeFrom + evenSpacing * i);
            }
            else if (remaining > 0.1f)
            {
                distances.Add(totalLength);
            }
        }

        // ═══════════════════════════════════════════
        //  UTILITY
        // ═══════════════════════════════════════════

        private static WirePath CreateWirePath(int trackId)
        {
            return new WirePath
            {
                WirePathId = nextWirePathId++,
                TrackId = trackId
            };
        }

        private static WireControlPoint CreateControlPoint(
            int wirePathId, int trackId, List<Vector3> polyline,
            float totalLength, float dist, bool longSide)
        {
            float clampedDist = Mathf.Clamp(dist, 0f, totalLength);
            var (pos, tangent) = TrackGeometry.GetPointAtDistance(polyline, clampedDist);
            float radius = CatenarySpacing.ComputeLocalRadius(polyline, clampedDist);

            return new WireControlPoint
            {
                WirePathId = wirePathId,
                TrackId = trackId,
                DistAlongTrack = clampedDist,
                Position = new Vector3(pos.x, 0f, pos.z),
                Tangent = tangent.sqrMagnitude > 0.001f ? tangent.normalized : Vector3.forward,
                ZigzagOffset = longSide ? ZigzagAmplitude : -ZigzagAmplitude,
                ContactWireHeight = DefaultContactWireHeight,
                LocalRadius = radius
            };
        }

        private static void EnsureEndPoints(WirePath path, List<Vector3> polyline, float totalLength)
        {
            if (path.ControlPoints.Count == 0) return;

            // Sprawdź czy mamy punkt na początku toru
            if (path.ControlPoints[0].DistAlongTrack > 1f)
            {
                var cp = CreateControlPoint(path.WirePathId, path.TrackId, polyline, totalLength, 0f, true);
                cp.IsSupportCandidate = true;
                path.ControlPoints.Insert(0, cp);
            }

            // Sprawdź czy mamy punkt na końcu toru
            var last = path.ControlPoints[path.ControlPoints.Count - 1];
            if (totalLength - last.DistAlongTrack > 1f)
            {
                var cp = CreateControlPoint(path.WirePathId, path.TrackId, polyline, totalLength, totalLength, true);
                cp.IsSupportCandidate = true;
                path.ControlPoints.Add(cp);
            }
        }

        // ═══════════════════════════════════════════
        //  SNAP ENDPOINTÓW DO JUNCTION NODE'ÓW
        // ═══════════════════════════════════════════

        /// <summary>
        /// Dla każdego wire path: jeśli początek/koniec toru leży na junction node
        /// współdzielonym z innym zelektryfikowanym torem, snapuj endpoint do dokładnej
        /// pozycji tego node'a. Dzięki temu przewody z różnych torów idealnie się łączą
        /// na rozjazdach (bez luk ~5cm).
        /// </summary>
        private static void SnapEndpointsToJunctionNodes(List<WirePath> allPaths, TrackGraph graph)
        {
            // Zbierz mapę: trackId → (startNodeId, endNodeId)
            var trackEndNodes = new Dictionary<int, (int startNodeId, int endNodeId)>();
            foreach (var path in allPaths)
            {
                if (trackEndNodes.ContainsKey(path.TrackId)) continue;
                var edges = graph.GetEdgesForTrack(path.TrackId);
                if (edges.Count == 0) continue;

                // Pierwszy i ostatni node toru
                int startNode = edges[0].FromNodeId;
                int endNode = edges[edges.Count - 1].ToNodeId;

                // Sprawdź czy polyline jest odwrócona
                var poly = graph.GetTrackPolyline(path.TrackId);
                if (poly != null && poly.Count >= 2)
                {
                    var nodeStartPos = graph.GetNode(startNode)?.Position ?? Vector3.zero;
                    var nodeEndPos = graph.GetNode(endNode)?.Position ?? Vector3.zero;
                    float dStartToPolyStart = Vector3.Distance(nodeStartPos, poly[0]);
                    float dEndToPolyStart = Vector3.Distance(nodeEndPos, poly[0]);
                    if (dEndToPolyStart < dStartToPolyStart)
                    {
                        // Polyline jest odwrócona
                        (startNode, endNode) = (endNode, startNode);
                    }
                }

                trackEndNodes[path.TrackId] = (startNode, endNode);
            }

            // Dla każdego junction node'a: zbierz wszystkie wire paths które tu kończą się/zaczynają
            var nodeToPathEndpoints = new Dictionary<int, List<(WirePath path, bool isStart)>>();

            foreach (var path in allPaths)
            {
                if (!trackEndNodes.ContainsKey(path.TrackId)) continue;
                var (startNodeId, endNodeId) = trackEndNodes[path.TrackId];

                var startNode = graph.GetNode(startNodeId);
                var endNode = graph.GetNode(endNodeId);

                // Interesują nas junction node'y (3+ krawędzi)
                if (startNode != null && startNode.Type == NodeType.Junction)
                {
                    if (!nodeToPathEndpoints.ContainsKey(startNodeId))
                        nodeToPathEndpoints[startNodeId] = new List<(WirePath, bool)>();
                    nodeToPathEndpoints[startNodeId].Add((path, true));
                }
                if (endNode != null && endNode.Type == NodeType.Junction)
                {
                    if (!nodeToPathEndpoints.ContainsKey(endNodeId))
                        nodeToPathEndpoints[endNodeId] = new List<(WirePath, bool)>();
                    nodeToPathEndpoints[endNodeId].Add((path, false));
                }
            }

            // Snapuj: dla każdego junction node'a z 2+ wire paths, ustaw ich endpointy
            // na dokładną pozycję node'a
            foreach (var kvp in nodeToPathEndpoints)
            {
                if (kvp.Value.Count < 2) continue; // sam nie potrzebuje snapu

                var node = graph.GetNode(kvp.Key);
                if (node == null) continue;

                Vector3 snapPos = new Vector3(node.Position.x, 0f, node.Position.z);

                foreach (var (path, isStart) in kvp.Value)
                {
                    if (path.ControlPoints.Count == 0) continue;

                    WireControlPoint endpoint;
                    if (isStart)
                        endpoint = path.ControlPoints[0];
                    else
                        endpoint = path.ControlPoints[path.ControlPoints.Count - 1];

                    // Snapuj pozycję do dokładnej pozycji node'a
                    endpoint.Position = snapPos;
                }
            }
        }
    }
}
