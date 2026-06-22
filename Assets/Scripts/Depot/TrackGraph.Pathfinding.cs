using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public partial class TrackGraph
    {
        // ═══════════════════════════════════════════
        //  PATHFINDING (BFS) — bez i z iglicami
        // ═══════════════════════════════════════════

        /// <summary>BFS: Najkrótsza ścieżka między dwoma węzłami (lista nodeId)</summary>
        public List<int> FindPath(int startNodeId, int endNodeId)
        {
            if (!nodes.ContainsKey(startNodeId) || !nodes.ContainsKey(endNodeId))
                return null;

            if (startNodeId == endNodeId)
                return new List<int> { startNodeId };

            Queue<int> queue = new();
            Dictionary<int, int> cameFrom = new();
            HashSet<int> visited = new();

            queue.Enqueue(startNodeId);
            visited.Add(startNodeId);
            cameFrom[startNodeId] = -1;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                if (current == endNodeId)
                {
                    List<int> path = new();
                    int node = endNodeId;
                    while (node != -1)
                    {
                        path.Add(node);
                        node = cameFrom[node];
                    }
                    path.Reverse();
                    return path;
                }

                foreach (int edgeId in nodes[current].EdgeIds)
                {
                    if (!edges.ContainsKey(edgeId)) continue;
                    var edge = edges[edgeId];

                    int neighbor = edge.FromNodeId == current ? edge.ToNodeId : edge.FromNodeId;

                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        cameFrom[neighbor] = current;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// BFS z uwzględnieniem pozycji iglic.
        /// Przechodzi tylko przez krawędzie dozwolone przez aktualny stan iglic.
        /// </summary>
        public List<int> FindPathRespectingBlades(int startNodeId, int endNodeId)
        {
            if (!nodes.ContainsKey(startNodeId) || !nodes.ContainsKey(endNodeId))
                return null;

            if (startNodeId == endNodeId)
                return new List<int> { startNodeId };

            Queue<int> queue = new();
            Dictionary<int, int> cameFrom = new();
            HashSet<int> visited = new();

            queue.Enqueue(startNodeId);
            visited.Add(startNodeId);
            cameFrom[startNodeId] = -1;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                if (current == endNodeId)
                {
                    List<int> path = new();
                    int node = endNodeId;
                    while (node != -1)
                    {
                        path.Add(node);
                        node = cameFrom[node];
                    }
                    path.Reverse();
                    return path;
                }

                foreach (int edgeId in nodes[current].EdgeIds)
                {
                    if (!edges.ContainsKey(edgeId)) continue;
                    var edge = edges[edgeId];

                    int neighbor = edge.FromNodeId == current ? edge.ToNodeId : edge.FromNodeId;

                    // Sprawdź iglicę na obecnym node: czy wolno jechać tą krawędzią?
                    if (!IsRouteAllowedByBlade(current, edgeId)) continue;

                    // Sprawdź iglicę na sąsiednim node: czy wolno wjechać z tej krawędzi?
                    if (!IsRouteAllowedByBlade(neighbor, edgeId)) continue;

                    if (!visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        cameFrom[neighbor] = current;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return null;
        }

        /// <summary>Konwertuje ścieżkę węzłów na pozycje (z polyline)</summary>
        public List<Vector3> PathToWorldPositions(List<int> nodePath)
        {
            if (nodePath == null || nodePath.Count < 2) return null;

            List<Vector3> positions = new();

            for (int i = 0; i < nodePath.Count - 1; i++)
            {
                int fromNode = nodePath[i];
                int toNode = nodePath[i + 1];

                // Znajdź krawędź między tymi node'ami
                TrackEdge edge = FindEdgeBetween(fromNode, toNode);
                if (edge == null)
                {
                    positions.Add(nodes[fromNode].Position);
                    continue;
                }

                if (edge.Polyline != null && edge.Polyline.Count > 0)
                {
                    // Dodaj polyline (w odpowiednim kierunku)
                    if (edge.FromNodeId == fromNode)
                    {
                        for (int j = (i == 0 ? 0 : 1); j < edge.Polyline.Count; j++)
                            positions.Add(edge.Polyline[j]);
                    }
                    else
                    {
                        // Odwrócona polyline
                        for (int j = edge.Polyline.Count - 1 - (i == 0 ? 0 : 1); j >= 0; j--)
                            positions.Add(edge.Polyline[j]);
                    }
                }
                else
                {
                    if (i == 0) positions.Add(nodes[fromNode].Position);
                    positions.Add(nodes[toNode].Position);
                }
            }

            return positions;
        }
    }
}
