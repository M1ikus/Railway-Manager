using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public enum PathEdgeType { Path, Road, Parking }
    public enum PathNodeType { Endpoint, Throughput, Junction }

    [System.Serializable]
    public class PathNode
    {
        public int Id;
        public Vector3 Position;
        public List<int> EdgeIds = new();
        public PathNodeType Type = PathNodeType.Endpoint;
    }

    [System.Serializable]
    public class PathEdge
    {
        public int Id;
        public int FromNodeId;
        public int ToNodeId;
        public float Length;
        public PathEdgeType EdgeType;
        public List<Vector3> Polyline = new();
        public float Width;
    }

    /// <summary>
    /// Graf ścieżek, dróg i parkingów dla pathfindingu pracowników.
    /// Uproszczony TrackGraph (bez blade/catenary/turnout).
    /// </summary>
    public class PathGraph : MonoBehaviour
    {
        public float snapTolerance = 0.5f;

        private Dictionary<int, PathNode> nodes = new();
        private Dictionary<int, PathEdge> edges = new();
        private int nextNodeId = 0;
        private int nextEdgeId = 0;

        public IReadOnlyDictionary<int, PathNode> Nodes => nodes;
        public IReadOnlyDictionary<int, PathEdge> Edges => edges;

        public event System.Action OnTopologyChanged;

        // ─── Save/Load API (zamiast reflection w DepotSavable) ───

        public void RestoreFromSave(IEnumerable<PathNode> nodesIn,
                                    IEnumerable<PathEdge> edgesIn,
                                    int nextNodeIdIn, int nextEdgeIdIn)
        {
            nodes.Clear();
            int maxNodeId = -1;
            if (nodesIn != null)
            {
                foreach (var n in nodesIn)
                {
                    if (n == null) continue;
                    nodes[n.Id] = n;
                    if (n.Id > maxNodeId) maxNodeId = n.Id;
                }
            }

            edges.Clear();
            int maxEdgeId = -1;
            if (edgesIn != null)
            {
                foreach (var e in edgesIn)
                {
                    if (e == null) continue;
                    edges[e.Id] = e;
                    if (e.Id > maxEdgeId) maxEdgeId = e.Id;
                }
            }

            nextNodeId = nextNodeIdIn > 0 ? nextNodeIdIn : maxNodeId + 1;
            nextEdgeId = nextEdgeIdIn > 0 ? nextEdgeIdIn : maxEdgeId + 1;

            OnTopologyChanged?.Invoke();
        }

        public void ClearAllForReset()
        {
            nodes.Clear();
            edges.Clear();
            nextNodeId = 0;
            nextEdgeId = 0;
            OnTopologyChanged?.Invoke();
        }

        // ─── Nodes ───

        public int AddNode(Vector3 position)
        {
            int id = nextNodeId++;
            nodes[id] = new PathNode { Id = id, Position = position };
            return id;
        }

        public int FindNodeAtPosition(Vector3 position, float tolerance = -1f)
        {
            if (tolerance < 0) tolerance = snapTolerance;
            float bestDist = tolerance;
            int bestId = -1;
            foreach (var kvp in nodes)
            {
                float dist = Vector3.Distance(kvp.Value.Position, position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestId = kvp.Key;
                }
            }
            return bestId;
        }

        public int GetOrCreateNode(Vector3 position, float tolerance = -1f)
        {
            int existing = FindNodeAtPosition(position, tolerance);
            return existing >= 0 ? existing : AddNode(position);
        }

        public int GetNearestNode(Vector3 position, float maxDistance = float.MaxValue)
        {
            float bestDist = maxDistance;
            int bestId = -1;
            foreach (var kvp in nodes)
            {
                float dist = Vector3.Distance(kvp.Value.Position, position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestId = kvp.Key;
                }
            }
            return bestId;
        }

        private void UpdateNodeType(int nodeId)
        {
            if (!nodes.TryGetValue(nodeId, out var node)) return;
            int edgeCount = node.EdgeIds.Count;
            node.Type = edgeCount switch
            {
                0 or 1 => PathNodeType.Endpoint,
                2 => PathNodeType.Throughput,
                _ => PathNodeType.Junction
            };
        }

        // ─── Edges ───

        public int AddEdge(int fromNodeId, int toNodeId, PathEdgeType type, float width)
        {
            if (!nodes.ContainsKey(fromNodeId) || !nodes.ContainsKey(toNodeId)) return -1;

            int id = nextEdgeId++;
            var fromPos = nodes[fromNodeId].Position;
            var toPos = nodes[toNodeId].Position;

            edges[id] = new PathEdge
            {
                Id = id,
                FromNodeId = fromNodeId,
                ToNodeId = toNodeId,
                Length = Vector3.Distance(fromPos, toPos),
                EdgeType = type,
                Polyline = new List<Vector3> { fromPos, toPos },
                Width = width
            };

            nodes[fromNodeId].EdgeIds.Add(id);
            nodes[toNodeId].EdgeIds.Add(id);
            UpdateNodeType(fromNodeId);
            UpdateNodeType(toNodeId);

            OnTopologyChanged?.Invoke();
            return id;
        }

        public void RemoveEdge(int edgeId)
        {
            if (!edges.TryGetValue(edgeId, out var edge)) return;

            if (nodes.TryGetValue(edge.FromNodeId, out var fromNode))
            {
                fromNode.EdgeIds.Remove(edgeId);
                UpdateNodeType(edge.FromNodeId);
            }
            if (nodes.TryGetValue(edge.ToNodeId, out var toNode))
            {
                toNode.EdgeIds.Remove(edgeId);
                UpdateNodeType(edge.ToNodeId);
            }

            edges.Remove(edgeId);

            // Usuń osierocone węzły
            if (fromNode != null && fromNode.EdgeIds.Count == 0) nodes.Remove(edge.FromNodeId);
            if (toNode != null && toNode.EdgeIds.Count == 0) nodes.Remove(edge.ToNodeId);

            OnTopologyChanged?.Invoke();
        }

        // ─── Query ───

        public PathEdge GetEdge(int edgeId) => edges.TryGetValue(edgeId, out var e) ? e : null;
        public PathNode GetNode(int nodeId) => nodes.TryGetValue(nodeId, out var n) ? n : null;

        public PathEdge FindNearestEdge(Vector3 position, float maxDistance = 5f)
        {
            float bestDist = maxDistance;
            PathEdge bestEdge = null;
            foreach (var edge in edges.Values)
            {
                Vector3 closest = ClosestPointOnSegment(position, edge.Polyline[0], edge.Polyline[1]);
                float dist = Vector3.Distance(position, closest);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestEdge = edge;
                }
            }
            return bestEdge;
        }

        public List<PathEdge> GetEdgesByType(PathEdgeType type)
        {
            var result = new List<PathEdge>();
            foreach (var edge in edges.Values)
                if (edge.EdgeType == type) result.Add(edge);
            return result;
        }

        // ─── Pathfinding (BFS) ───

        public List<int> FindPath(int startNodeId, int endNodeId)
        {
            if (!nodes.ContainsKey(startNodeId) || !nodes.ContainsKey(endNodeId)) return null;
            if (startNodeId == endNodeId) return new List<int> { startNodeId };

            var queue = new Queue<int>();
            var cameFrom = new Dictionary<int, int>();
            queue.Enqueue(startNodeId);
            cameFrom[startNodeId] = -1;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (current == endNodeId)
                {
                    // Odtwórz ścieżkę
                    var path = new List<int>();
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
                    var edge = edges[edgeId];
                    int neighbor = edge.FromNodeId == current ? edge.ToNodeId : edge.FromNodeId;
                    if (!cameFrom.ContainsKey(neighbor))
                    {
                        cameFrom[neighbor] = current;
                        queue.Enqueue(neighbor);
                    }
                }
            }
            return null; // Brak ścieżki
        }

        // ─── Helpers ───

        private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude);
            return a + ab * t;
        }

        // ─── Debug ───

        void OnDrawGizmos()
        {
            foreach (var edge in edges.Values)
            {
                Gizmos.color = edge.EdgeType switch
                {
                    PathEdgeType.Path => new Color(0.9f, 0.8f, 0.5f),
                    PathEdgeType.Road => Color.gray,
                    PathEdgeType.Parking => new Color(0.3f, 0.3f, 0.8f),
                    _ => Color.white
                };
                if (edge.Polyline.Count >= 2)
                    Gizmos.DrawLine(edge.Polyline[0] + Vector3.up * 0.1f, edge.Polyline[1] + Vector3.up * 0.1f);
            }

            foreach (var node in nodes.Values)
            {
                Gizmos.color = node.Type switch
                {
                    PathNodeType.Junction => Color.yellow,
                    PathNodeType.Throughput => Color.cyan,
                    _ => Color.green
                };
                Gizmos.DrawSphere(node.Position + Vector3.up * 0.1f, 0.2f);
            }
        }
    }
}
