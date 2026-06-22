using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    /// <summary>
    /// Etap 1 pipeline'u sieci trakcyjnej: klasyfikacja torów na strefy.
    /// Priorytet: SwitchHead > Curve > ParallelStation > Straight.
    /// </summary>
    public static class ZoneClassifier
    {
        private const float ParallelDistThreshold = 6f;   // max odl. między torami dla ParallelStation
        private const float SwitchHeadMergeRadius = 50f;   // max odl. między rozjazdami w jednej głowicy
        private const float CurveRadiusThreshold = 2000f;  // poniżej = strefa Curve

        private static int nextZoneId = 0;

        /// <summary>
        /// Klasyfikuje zelektryfikowane tory na strefy trakcyjne.
        /// </summary>
        public static List<CatenaryZone> ClassifyZones(
            TrackGraph graph,
            PrefabTrackBuilder trackBuilder,
            List<int> electrifiedTrackIds)
        {
            nextZoneId = 0;
            var allZones = new List<CatenaryZone>();
            var claimedEdges = new HashSet<int>(); // krawędzie już przypisane do stref

            // 1. Głowice rozjazdowe (najwyższy priorytet)
            var switchHeads = DetectSwitchHeads(graph, trackBuilder, electrifiedTrackIds);
            foreach (var zone in switchHeads)
            {
                foreach (int eid in zone.EdgeIds) claimedEdges.Add(eid);
                allZones.Add(zone);
            }

            // 2. Łuki
            var curves = DetectCurveZones(graph, electrifiedTrackIds, claimedEdges);
            foreach (var zone in curves)
            {
                foreach (int eid in zone.EdgeIds) claimedEdges.Add(eid);
                allZones.Add(zone);
            }

            // 3. Tory równoległe stacyjne
            var parallel = DetectParallelStationZones(graph, electrifiedTrackIds, claimedEdges);
            foreach (var zone in parallel)
            {
                foreach (int eid in zone.EdgeIds) claimedEdges.Add(eid);
                allZones.Add(zone);
            }

            // 4. Reszta = Straight
            var straight = AssignRemainingToStraight(graph, electrifiedTrackIds, claimedEdges);
            allZones.AddRange(straight);

            // Cache polyline per tor w strefie
            foreach (var zone in allZones)
                CacheZonePolylines(zone, graph);

            Log.Info($"[ZoneClassifier] {allZones.Count} zones: " +
                      $"{switchHeads.Count} SwitchHead, {curves.Count} Curve, " +
                      $"{parallel.Count} ParallelStation, {straight.Count} Straight");

            return allZones;
        }

        // ═══════════════════════════════════════════
        //  SWITCH HEAD — głowice rozjazdowe
        // ═══════════════════════════════════════════

        private static List<CatenaryZone> DetectSwitchHeads(
            TrackGraph graph,
            PrefabTrackBuilder trackBuilder,
            List<int> electrifiedTrackIds)
        {
            var zones = new List<CatenaryZone>();
            if (trackBuilder == null) return zones;

            var electrifiedSet = new HashSet<int>(electrifiedTrackIds);

            // Zbierz rozjazdy na zelektryfikowanych torach
            var relevantTurnouts = new List<TurnoutEntity>();
            foreach (var kvp in trackBuilder.TurnoutEntities)
            {
                var entity = kvp.Value;
                bool hasElectrified = entity.MemberTrackIds.Any(tid => electrifiedSet.Contains(tid));
                if (hasElectrified)
                    relevantTurnouts.Add(entity);
            }

            if (relevantTurnouts.Count == 0) return zones;

            // Union-Find: grupuj rozjazdy dzielące node'y lub połączone wstawkami
            var parent = new Dictionary<int, int>();
            foreach (var t in relevantTurnouts) parent[t.TurnoutId] = t.TurnoutId;

            for (int i = 0; i < relevantTurnouts.Count; i++)
            {
                for (int j = i + 1; j < relevantTurnouts.Count; j++)
                {
                    if (ShouldGroupTurnouts(graph, relevantTurnouts[i], relevantTurnouts[j]))
                    {
                        Union(parent, relevantTurnouts[i].TurnoutId, relevantTurnouts[j].TurnoutId);
                    }
                }
            }

            // Grupuj rozjazdy po root
            var groups = new Dictionary<int, List<TurnoutEntity>>();
            foreach (var t in relevantTurnouts)
            {
                int root = Find(parent, t.TurnoutId);
                if (!groups.ContainsKey(root)) groups[root] = new List<TurnoutEntity>();
                groups[root].Add(t);
            }

            // Twórz strefę per grupa
            foreach (var group in groups.Values)
            {
                var zone = new CatenaryZone
                {
                    ZoneId = nextZoneId++,
                    Type = ZoneType.SwitchHead
                };

                var edgeSet = new HashSet<int>();
                var trackSet = new HashSet<int>();

                foreach (var turnout in group)
                {
                    zone.TurnoutEntityIds.Add(turnout.TurnoutId);
                    foreach (int trackId in turnout.MemberTrackIds)
                    {
                        if (!electrifiedSet.Contains(trackId)) continue;
                        trackSet.Add(trackId);
                        foreach (int eid in graph.GetTrack(trackId)?.EdgeIds ?? new List<int>())
                            edgeSet.Add(eid);
                    }

                    // Dodaj też krawędzie sąsiednie (wstawki, krótkie odcinki przed/za rozjazdem)
                    AddAdjacentInsertEdges(graph, turnout, electrifiedSet, edgeSet, trackSet);
                }

                zone.TrackIds = trackSet.ToList();
                zone.EdgeIds = edgeSet.ToList();
                zone.RecommendedSpacing = 15f; // gęste podpory w głowicach

                zones.Add(zone);
            }

            return zones;
        }

        private static bool ShouldGroupTurnouts(TrackGraph graph, TurnoutEntity a, TurnoutEntity b)
        {
            // Sprawdź czy dzielą node
            var nodesA = GetTurnoutNodeIds(graph, a);
            var nodesB = GetTurnoutNodeIds(graph, b);
            if (nodesA.Overlaps(nodesB)) return true;

            // Sprawdź odległość geometryczną
            float dist = Vector3.Distance(a.Origin, b.Origin);
            if (dist < SwitchHeadMergeRadius)
            {
                // Sprawdź czy są połączone (dzielą sąsiedni tor)
                var tracksA = new HashSet<int>(a.MemberTrackIds);
                var tracksB = new HashSet<int>(b.MemberTrackIds);

                foreach (int tA in tracksA)
                {
                    var adjA = graph.GetAdjacentTrackIds(tA);
                    foreach (int adj in adjA)
                        if (tracksB.Contains(adj)) return true;
                }
            }

            return false;
        }

        private static HashSet<int> GetTurnoutNodeIds(TrackGraph graph, TurnoutEntity turnout)
        {
            var nodeIds = new HashSet<int>();
            foreach (int trackId in turnout.MemberTrackIds)
            {
                var edges = graph.GetEdgesForTrack(trackId);
                foreach (var edge in edges)
                {
                    nodeIds.Add(edge.FromNodeId);
                    nodeIds.Add(edge.ToNodeId);
                }
            }
            return nodeIds;
        }

        private static void AddAdjacentInsertEdges(
            TrackGraph graph, TurnoutEntity turnout,
            HashSet<int> electrifiedSet, HashSet<int> edgeSet, HashSet<int> trackSet)
        {
            // Znajdź node'y rozjazdu i sprawdź ich krawędzie — wstawki dodaj do strefy
            var turnoutNodes = GetTurnoutNodeIds(graph, turnout);
            foreach (int nodeId in turnoutNodes)
            {
                var node = graph.GetNode(nodeId);
                if (node == null) continue;
                foreach (int eid in node.EdgeIds)
                {
                    var edge = graph.GetEdge(eid);
                    if (edge == null) continue;
                    if (edge.Insert != null && edge.Insert.Type != InsertType.None)
                    {
                        edgeSet.Add(eid);
                        // Znajdź tor tej krawędzi
                        foreach (var track in graph.Tracks.Values)
                        {
                            if (!electrifiedSet.Contains(track.TrackId)) continue;
                            if (track.EdgeIds.Contains(eid))
                            {
                                trackSet.Add(track.TrackId);
                                // Dodaj wszystkie krawędzie tego toru (wstawka jest częścią toru)
                                foreach (int teid in track.EdgeIds)
                                    edgeSet.Add(teid);
                                break;
                            }
                        }
                    }
                }
            }
        }

        // ═══════════════════════════════════════════
        //  CURVE — łuki
        // ═══════════════════════════════════════════

        private static List<CatenaryZone> DetectCurveZones(
            TrackGraph graph, List<int> electrifiedTrackIds, HashSet<int> claimedEdges)
        {
            var zones = new List<CatenaryZone>();

            foreach (int trackId in electrifiedTrackIds)
            {
                var trackEdges = graph.GetEdgesForTrack(trackId);

                foreach (var edge in trackEdges)
                {
                    if (claimedEdges.Contains(edge.Id)) continue;

                    bool isCurve = false;
                    float radius = float.MaxValue;

                    if (edge.Curve != null && !edge.Curve.IsStraight)
                    {
                        isCurve = true;
                        radius = edge.Curve.Radius;
                    }
                    else if (edge.Polyline != null && edge.Polyline.Count >= 3)
                    {
                        // Sprawdź promień z polyline
                        float len = TrackGeometry.CalculatePolylineLength(edge.Polyline);
                        float midRadius = CatenarySpacing.ComputeLocalRadius(edge.Polyline, len / 2f);
                        if (midRadius < CurveRadiusThreshold)
                        {
                            isCurve = true;
                            radius = midRadius;
                        }
                    }

                    if (!isCurve) continue;

                    // Sprawdź czy dołączyć do istniejącej strefy Curve tego toru
                    CatenaryZone existingZone = null;
                    foreach (var z in zones)
                    {
                        if (z.TrackIds.Contains(trackId) && z.EdgeIds.Count > 0)
                        {
                            // Sąsiednia krawędź?
                            var lastEdge = graph.GetEdge(z.EdgeIds[z.EdgeIds.Count - 1]);
                            if (lastEdge != null &&
                                (lastEdge.ToNodeId == edge.FromNodeId ||
                                 lastEdge.FromNodeId == edge.ToNodeId ||
                                 lastEdge.ToNodeId == edge.ToNodeId ||
                                 lastEdge.FromNodeId == edge.FromNodeId))
                            {
                                existingZone = z;
                                break;
                            }
                        }
                    }

                    if (existingZone != null)
                    {
                        existingZone.EdgeIds.Add(edge.Id);
                        existingZone.RecommendedSpacing = Mathf.Min(
                            existingZone.RecommendedSpacing,
                            CatenarySpacing.GetSpacing(radius));
                    }
                    else
                    {
                        var zone = new CatenaryZone
                        {
                            ZoneId = nextZoneId++,
                            Type = ZoneType.Curve,
                            RecommendedSpacing = CatenarySpacing.GetSpacing(radius)
                        };
                        zone.TrackIds.Add(trackId);
                        zone.EdgeIds.Add(edge.Id);
                        zones.Add(zone);
                    }
                }
            }

            return zones;
        }

        // ═══════════════════════════════════════════
        //  PARALLEL STATION — tory równoległe
        // ═══════════════════════════════════════════

        private static List<CatenaryZone> DetectParallelStationZones(
            TrackGraph graph, List<int> electrifiedTrackIds, HashSet<int> claimedEdges)
        {
            var zones = new List<CatenaryZone>();

            // Zbierz tory z niezajętymi krawędziami
            var unclaimed = new List<int>();
            foreach (int trackId in electrifiedTrackIds)
            {
                var edges = graph.GetEdgesForTrack(trackId);
                bool hasUnclaimed = edges.Any(e => !claimedEdges.Contains(e.Id));
                if (hasUnclaimed) unclaimed.Add(trackId);
            }

            if (unclaimed.Count < 2) return zones;

            // Znajdź pary równoległych torów
            var used = new HashSet<int>();
            var groups = new List<List<int>>();

            for (int i = 0; i < unclaimed.Count; i++)
            {
                if (used.Contains(unclaimed[i])) continue;
                var group = new List<int> { unclaimed[i] };
                used.Add(unclaimed[i]);

                var polyA = graph.GetTrackPolyline(unclaimed[i]);
                if (polyA == null || polyA.Count < 2) continue;

                for (int j = i + 1; j < unclaimed.Count; j++)
                {
                    if (used.Contains(unclaimed[j])) continue;
                    var polyB = graph.GetTrackPolyline(unclaimed[j]);
                    if (polyB == null || polyB.Count < 2) continue;

                    // Tory dzielące rozjazd = nie równoległe
                    if (TracksShareJunction(polyA, polyB)) continue;

                    float medDist = ComputeMedianDistance(polyA, polyB);
                    if (medDist < ParallelDistThreshold)
                    {
                        group.Add(unclaimed[j]);
                        used.Add(unclaimed[j]);
                    }
                }

                if (group.Count >= 2)
                    groups.Add(group);
            }

            foreach (var group in groups)
            {
                var zone = new CatenaryZone
                {
                    ZoneId = nextZoneId++,
                    Type = ZoneType.ParallelStation,
                    TrackIds = group,
                    RecommendedSpacing = CatenarySpacing.StraightSpacing
                };

                // Zbierz niezajęte krawędzie torów grupy
                foreach (int trackId in group)
                {
                    var edges = graph.GetEdgesForTrack(trackId);
                    foreach (var edge in edges)
                    {
                        if (!claimedEdges.Contains(edge.Id))
                            zone.EdgeIds.Add(edge.Id);
                    }
                }

                zones.Add(zone);
            }

            return zones;
        }

        // ═══════════════════════════════════════════
        //  STRAIGHT — reszta
        // ═══════════════════════════════════════════

        private static List<CatenaryZone> AssignRemainingToStraight(
            TrackGraph graph, List<int> electrifiedTrackIds, HashSet<int> claimedEdges)
        {
            var zones = new List<CatenaryZone>();

            foreach (int trackId in electrifiedTrackIds)
            {
                var edges = graph.GetEdgesForTrack(trackId);
                var unclaimedEdgeIds = new List<int>();

                foreach (var edge in edges)
                    if (!claimedEdges.Contains(edge.Id))
                        unclaimedEdgeIds.Add(edge.Id);

                if (unclaimedEdgeIds.Count == 0) continue;

                // Szukaj istniejącej strefy Straight dla tego toru (merge sąsiednich)
                CatenaryZone existingZone = null;
                foreach (var z in zones)
                {
                    if (z.TrackIds.Contains(trackId))
                    {
                        existingZone = z;
                        break;
                    }
                }

                if (existingZone != null)
                {
                    existingZone.EdgeIds.AddRange(unclaimedEdgeIds);
                }
                else
                {
                    var zone = new CatenaryZone
                    {
                        ZoneId = nextZoneId++,
                        Type = ZoneType.Straight,
                        RecommendedSpacing = CatenarySpacing.StraightSpacing
                    };
                    zone.TrackIds.Add(trackId);
                    zone.EdgeIds.AddRange(unclaimedEdgeIds);
                    zones.Add(zone);
                }
            }

            return zones;
        }

        // ═══════════════════════════════════════════
        //  CACHE POLYLINES
        // ═══════════════════════════════════════════

        private static void CacheZonePolylines(CatenaryZone zone, TrackGraph graph)
        {
            foreach (int trackId in zone.TrackIds)
            {
                if (zone.TrackPolylines.ContainsKey(trackId)) continue;
                var poly = graph.GetTrackPolyline(trackId);
                if (poly != null && poly.Count >= 2)
                    zone.TrackPolylines[trackId] = poly;
            }
        }

        // ═══════════════════════════════════════════
        //  UTILITY (port z PolePlacer)
        // ═══════════════════════════════════════════

        private static bool TracksShareJunction(List<Vector3> polyA, List<Vector3> polyB, float threshold = 5f)
        {
            Vector3 aStart = polyA[0], aEnd = polyA[polyA.Count - 1];
            Vector3 bStart = polyB[0], bEnd = polyB[polyB.Count - 1];

            float d1 = Vector2.Distance(new Vector2(aStart.x, aStart.z), new Vector2(bStart.x, bStart.z));
            float d2 = Vector2.Distance(new Vector2(aStart.x, aStart.z), new Vector2(bEnd.x, bEnd.z));
            float d3 = Vector2.Distance(new Vector2(aEnd.x, aEnd.z), new Vector2(bStart.x, bStart.z));
            float d4 = Vector2.Distance(new Vector2(aEnd.x, aEnd.z), new Vector2(bEnd.x, bEnd.z));

            return d1 < threshold || d2 < threshold || d3 < threshold || d4 < threshold;
        }

        private static float ComputeMedianDistance(List<Vector3> polyA, List<Vector3> polyB)
        {
            float lenA = TrackGeometry.CalculatePolylineLength(polyA);
            float lenB = TrackGeometry.CalculatePolylineLength(polyB);

            var shorter = lenA <= lenB ? polyA : polyB;
            var longer = lenA <= lenB ? polyB : polyA;
            float shorterLen = Mathf.Min(lenA, lenB);

            var distances = new List<float>();
            int samples = 5;
            for (int i = 0; i < samples; i++)
            {
                float d = shorterLen * (i + 1) / (samples + 1);
                var (pos, _) = TrackGeometry.GetPointAtDistance(shorter, d);
                float projDist = TrackGeometry.ProjectPointOnPolyline(longer, pos);
                var (closestPos, _) = TrackGeometry.GetPointAtDistance(longer, projDist);
                float dist2D = Vector2.Distance(
                    new Vector2(pos.x, pos.z),
                    new Vector2(closestPos.x, closestPos.z));
                distances.Add(dist2D);
            }

            distances.Sort();
            return distances[distances.Count / 2];
        }

        // Union-Find
        private static int Find(Dictionary<int, int> parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]]; // path compression
                x = parent[x];
            }
            return x;
        }

        private static void Union(Dictionary<int, int> parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra != rb) parent[rb] = ra;
        }
    }
}
