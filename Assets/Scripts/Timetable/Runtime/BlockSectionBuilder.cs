using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Buduje odcinki blokowe z PathfindingGraph.
    /// Granice odcinków: rozjazdy (node z 3+ edge), semafory (railway=signal),
    /// końce linii (dead-end, 1 edge). Na dwutorowej każdy tor ma osobne sekcje.
    /// </summary>
    public static class BlockSectionBuilder
    {
        public struct BuildResult
        {
            public List<BlockSection> sections;
            /// <summary>edgeId → sectionId (-1 = nieprzypisany). Tablica indeksowana po edgeId.</summary>
            public int[] edgeToSection;
        }

        /// <param name="boundaryNodeIds">Node IDs granic odcinków (stacje, semafory itp.).</param>
        public static BuildResult Build(PathfindingGraph graph, HashSet<int> boundaryNodeIds)
        {
            var result = new BuildResult
            {
                sections = new List<BlockSection>(),
                edgeToSection = new int[graph != null ? graph.EdgeCount : 0]
            };

            if (graph == null || graph.EdgeCount == 0)
                return result;

            // Init edge mapping to -1
            for (int i = 0; i < result.edgeToSection.Length; i++)
                result.edgeToSection[i] = -1;

            // 1) Oznacz boundary nodes — stacje jako granice odcinków
            var boundaryType = new Dictionary<int, BoundaryType>();

            // Stacje/semafory jako granice (nadpisują junction jeśli nakładają się)
            if (boundaryNodeIds != null)
            {
                foreach (int sn in boundaryNodeIds)
                {
                    if (sn >= 0 && sn < graph.NodeCount)
                        boundaryType[sn] = BoundaryType.Station;
                }
            }

            // 2) Buduj sekcje
            int nextId = 0;

            for (int e = 0; e < graph.EdgeCount; e++)
            {
                if (result.edgeToSection[e] >= 0) continue; // already assigned

                var startEdge = graph.GetEdge(e);
                string trackRef = GetTrackRef(startEdge);

                // Rozciągnij łańcuch w obu kierunkach
                var chain = BuildChain(graph, e, trackRef, boundaryType, result.edgeToSection);
                if (chain.edgeIds.Count == 0) continue;

                int sectionId = nextId++;

                // Przypisz edge→section i oblicz właściwości
                float totalLength = 0f;
                int minSpeed = int.MaxValue;

                foreach (int eid in chain.edgeIds)
                {
                    result.edgeToSection[eid] = sectionId;
                    var edge = graph.GetEdge(eid);
                    totalLength += edge.lengthM;
                    if (edge.maxSpeedKmh > 0 && edge.maxSpeedKmh < minSpeed)
                        minSpeed = edge.maxSpeedKmh;
                }
                if (minSpeed == int.MaxValue) minSpeed = 80;

                boundaryType.TryGetValue(chain.startNodeId, out var startBnd);
                boundaryType.TryGetValue(chain.endNodeId, out var endBnd);

                result.sections.Add(new BlockSection
                {
                    id = sectionId,
                    startNodeId = chain.startNodeId,
                    endNodeId = chain.endNodeId,
                    lengthM = totalLength,
                    maxSpeedKmh = minSpeed,
                    edgeCount = chain.edgeIds.Count,
                    startBoundary = startBnd,
                    endBoundary = endBnd
                });
            }

            // Stats
            int junctions = 0, signals = 0, lineEnds = 0;
            foreach (var kv in boundaryType)
            {
                switch (kv.Value)
                {
                    case BoundaryType.Junction: junctions++; break;
                    case BoundaryType.Signal: signals++; break;
                    case BoundaryType.LineEnd: lineEnds++; break;
                }
            }

            float avgLen = 0;
            if (result.sections.Count > 0)
            {
                float sum = 0;
                foreach (var s in result.sections) sum += s.lengthM;
                avgLen = sum / result.sections.Count;
            }

            Log.Info($"[BlockSectionBuilder] Built {result.sections.Count} sections " +
                $"({junctions} junctions, {signals} signals, {lineEnds} line-ends), " +
                $"avg length {avgLen:F0}m, boundary nodes {boundaryType.Count}");

            return result;
        }

        struct ChainResult
        {
            public List<int> edgeIds; // tymczasowa lista — nie przechowywana w BlockSection
            public int startNodeId;
            public int endNodeId;
        }

        static ChainResult BuildChain(
            PathfindingGraph graph, int seedEdgeId, string trackRef,
            Dictionary<int, BoundaryType> boundaries, int[] edgeToSection)
        {
            var result = new ChainResult { edgeIds = new List<int>() };
            var seedEdge = graph.GetEdge(seedEdgeId);

            var forwardEdges = new List<int>();
            int forwardEndNode = Extend(graph, seedEdge.toNodeId, seedEdgeId, trackRef,
                boundaries, edgeToSection, forwardEdges);

            var backwardEdges = new List<int>();
            int backwardEndNode = Extend(graph, seedEdge.fromNodeId, seedEdgeId, trackRef,
                boundaries, edgeToSection, backwardEdges);

            backwardEdges.Reverse();
            result.edgeIds.AddRange(backwardEdges);
            result.edgeIds.Add(seedEdgeId);
            result.edgeIds.AddRange(forwardEdges);

            result.startNodeId = backwardEndNode;
            result.endNodeId = forwardEndNode;

            return result;
        }

        static int Extend(
            PathfindingGraph graph, int currentNode, int prevEdgeId,
            string trackRef, Dictionary<int, BoundaryType> boundaries,
            int[] edgeToSection, List<int> collectedEdges)
        {
            if (boundaries.ContainsKey(currentNode))
                return currentNode;

            int maxSteps = 50000;
            int prevNode = -1;

            // Ustal skąd przyszliśmy (żeby nie wracać reverse edge)
            var seedEdge = graph.GetEdge(prevEdgeId);
            prevNode = (seedEdge.toNodeId == currentNode) ? seedEdge.fromNodeId : seedEdge.toNodeId;

            while (maxSteps-- > 0)
            {
                var node = graph.GetNode(currentNode);
                if (node.edgeIds == null) break;

                int nextEdge = -1;
                int nextNode = -1;

                foreach (int eid in node.edgeIds)
                {
                    if (edgeToSection[eid] >= 0) continue;

                    var edge = graph.GetEdge(eid);
                    int other = edge.fromNodeId == currentNode ? edge.toNodeId : edge.fromNodeId;

                    // Nie wracaj do node'a z którego przyszliśmy (blokuje reverse edge)
                    if (other == prevNode) continue;

                    string edgeTrack = GetTrackRef(edge);
                    if (!string.IsNullOrEmpty(trackRef) && !string.IsNullOrEmpty(edgeTrack)
                        && edgeTrack != trackRef)
                        continue;

                    nextEdge = eid;
                    nextNode = other;
                    break;
                }

                if (nextEdge < 0) break;

                collectedEdges.Add(nextEdge);
                prevNode = currentNode;
                currentNode = nextNode;

                if (boundaries.ContainsKey(currentNode))
                    return currentNode;
            }

            return currentNode;
        }

        static string GetTrackRef(PathfindingGraph.Edge edge)
        {
            if (edge.metadata == null) return "";
            edge.metadata.TryGetValue("railway:track_ref", out var tr);
            return tr ?? "";
        }
    }
}
