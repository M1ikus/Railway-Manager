using System.Collections.Generic;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Lookup nad odcinkami blokowymi. Minimalny footprint pamięci:
    /// int[] edgeToSection (tablica) zamiast Dictionary.
    /// </summary>
    public class BlockSectionGraph
    {
        readonly BlockSection[] _sections;
        readonly int[] _edgeToSection; // edgeId → sectionId, -1 = brak

        public int SectionCount => _sections.Length;

        public BlockSectionGraph(BlockSectionBuilder.BuildResult buildResult)
        {
            _sections = buildResult.sections != null ? buildResult.sections.ToArray() : new BlockSection[0];
            _edgeToSection = buildResult.edgeToSection ?? new int[0];

            Log.Info($"[BlockSectionGraph] {_sections.Length} sections, " +
                $"{_edgeToSection.Length} edge slots");
        }

        public BlockSection GetSection(int sectionId)
        {
            if (sectionId < 0 || sectionId >= _sections.Length) return default;
            return _sections[sectionId];
        }

        public int GetSectionForEdge(int edgeId)
        {
            if (edgeId < 0 || edgeId >= _edgeToSection.Length) return -1;
            return _edgeToSection[edgeId];
        }

        /// <summary>
        /// Zwraca uporządkowaną listę sekcji wzdłuż trasy z dystansem wejścia.
        /// totalRouteDistance — łączna długość trasy w tych samych jednostkach co entryDistanceM.
        /// </summary>
        public RouteBlockInfo GetSectionsForRoute(
            List<int> routeNodeIds, PathfindingGraph graph)
        {
            var info = new RouteBlockInfo
            {
                sections = new List<SectionOnRoute>(),
                totalRouteDistance = 0f
            };
            if (routeNodeIds == null || routeNodeIds.Count < 2) return info;

            var seen = new HashSet<int>();
            float cumulativeDistance = 0f;

            for (int i = 0; i < routeNodeIds.Count - 1; i++)
            {
                int fromNode = routeNodeIds[i];
                int toNode = routeNodeIds[i + 1];

                // Guard (crash-hunt V1): stale/corrupt node id — np. save po regeneracji mapy
                // albo Route z innego grafu. GetNode to goły indexer (_nodes[id]) → bez tego
                // ArgumentOutOfRangeException wywala symulację. Pomijamy parę, liczymy jako missing.
                if (fromNode < 0 || fromNode >= graph.NodeCount
                    || toNode < 0 || toNode >= graph.NodeCount)
                {
                    info.missingEdges++;
                    continue;
                }

                int foundEdge = -1;
                var node = graph.GetNode(fromNode);
                if (node.edgeIds != null)
                {
                    foreach (int eid in node.edgeIds)
                    {
                        var edge = graph.GetEdge(eid);
                        if ((edge.fromNodeId == fromNode && edge.toNodeId == toNode)
                            || (edge.fromNodeId == toNode && edge.toNodeId == fromNode))
                        {
                            foundEdge = eid;
                            break;
                        }
                    }
                }

                float edgeLen;
                if (foundEdge >= 0)
                {
                    edgeLen = graph.GetEdge(foundEdge).lengthM;
                    int sectionId = GetSectionForEdge(foundEdge);
                    if (sectionId >= 0 && seen.Add(sectionId))
                    {
                        info.sections.Add(new SectionOnRoute
                        {
                            sectionId = sectionId,
                            entryDistanceM = cumulativeDistance
                        });
                    }
                    else if (sectionId < 0)
                    {
                        info.unmappedEdges++;
                    }
                }
                else
                {
                    edgeLen = UnityEngine.Vector2.Distance(
                        graph.GetNode(fromNode).position,
                        graph.GetNode(toNode).position);
                    info.missingEdges++;
                }

                cumulativeDistance += edgeLen;
            }

            info.totalRouteDistance = cumulativeDistance;
            return info;
        }

        public struct RouteBlockInfo
        {
            public List<SectionOnRoute> sections;
            public float totalRouteDistance;
            public int unmappedEdges;  // edge istnieje ale nie ma sekcji
            public int missingEdges;   // edge nie znaleziony w grafie
        }

        public struct SectionOnRoute
        {
            public int sectionId;
            public float entryDistanceM;
        }
    }
}
