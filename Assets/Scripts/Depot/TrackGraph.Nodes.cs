using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DepotSystem
{
    public partial class TrackGraph
    {
        // ═══════════════════════════════════════════
        //  WĘZŁY (NODES)
        // ═══════════════════════════════════════════

        /// <summary>Dodaje nowy węzeł</summary>
        public int AddNode(Vector3 position)
        {
            int id = nextNodeId++;
            nodes[id] = new TrackNode
            {
                Id = id,
                Position = position,
                EdgeIds = new List<int>(),
                Type = NodeType.Endpoint,
                Direction = Vector3.forward
            };
            return id;
        }

        /// <summary>
        /// Szuka istniejącego node'a w promieniu tolerance.
        /// Zwraca id lub -1 jeśli nie znaleziono.
        /// </summary>
        public int FindNodeAtPosition(Vector3 position, float tolerance = -1f)
        {
            if (tolerance < 0) tolerance = snapTolerance;

            int nearest = -1;
            float minDist = tolerance;

            foreach (var kvp in nodes)
            {
                float dist = Vector3.Distance(position, kvp.Value.Position);
                if (dist <= minDist)
                {
                    minDist = dist;
                    nearest = kvp.Key;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Zwraca istniejący node w promieniu tolerance lub tworzy nowy.
        /// KLUCZOWA METODA - dzięki niej tory współdzielą node'y i tworzą rozjazdy.
        /// </summary>
        public int GetOrCreateNode(Vector3 position, float tolerance = -1f)
        {
            int existing = FindNodeAtPosition(position, tolerance);
            if (existing >= 0) return existing;
            return AddNode(position);
        }

        /// <summary>Znajduje najbliższy węzeł do podanej pozycji</summary>
        public int GetNearestNode(Vector3 position, float maxDistance = float.MaxValue)
        {
            int nearest = -1;
            float minDist = maxDistance;

            foreach (var kvp in nodes)
            {
                float dist = Vector3.Distance(position, kvp.Value.Position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = kvp.Key;
                }
            }

            return nearest;
        }

        /// <summary>Przelicza typ node'a na podstawie liczby krawędzi</summary>
        private void UpdateNodeType(int nodeId)
        {
            if (!nodes.ContainsKey(nodeId)) return;
            var node = nodes[nodeId];
            if (node.EdgeIds.Count <= 1)
            {
                node.Type = NodeType.Endpoint;
                return;
            }
            if (node.EdgeIds.Count >= 3)
            {
                node.Type = NodeType.Junction;
                return;
            }
            // 2 krawędzie: Throughput tylko jeśli tworzą gładkie przejście (kolinearność)
            // W przeciwnym razie Junction (np. koniec łuku + koniec przekątnej w krzyżowym)
            node.Type = AreTwoEdgesCollinear(nodeId) ? NodeType.Throughput : NodeType.Junction;
        }

        /// <summary>
        /// Sprawdza czy 2 krawędzie w node są kolinearne (gładkie przejście, kąt bliski 180°).
        /// </summary>
        private bool AreTwoEdgesCollinear(int nodeId)
        {
            var node = nodes[nodeId];
            if (node.EdgeIds.Count != 2) return false;

            Vector3[] dirs = new Vector3[2];
            int idx = 0;
            foreach (int edgeId in node.EdgeIds)
            {
                if (!edges.ContainsKey(edgeId)) return false;
                var edge = edges[edgeId];
                if (edge.Polyline != null && edge.Polyline.Count >= 2)
                {
                    // Kierunek krawędzi "odchodzący" od node'a
                    if (edge.FromNodeId == nodeId)
                        dirs[idx] = TrackGeometry.GetStartTangent(edge.Polyline);
                    else
                        dirs[idx] = -TrackGeometry.GetEndTangent(edge.Polyline);
                }
                else
                {
                    int otherNode = edge.FromNodeId == nodeId ? edge.ToNodeId : edge.FromNodeId;
                    if (!nodes.ContainsKey(otherNode)) return false;
                    dirs[idx] = (nodes[otherNode].Position - node.Position).normalized;
                }
                idx++;
            }

            // Kąt między kierunkami odchodzącymi od node'a — powinien być ~180° (dot ~-1)
            // Jeśli kąt jest mniejszy (np. 170° = dot -0.98), to przejście łagodne = Throughput
            // Threshold: 30° odchylenia od prostej (cos(150°) ≈ -0.866)
            float dot = Vector3.Dot(dirs[0].normalized, dirs[1].normalized);
            return dot < -0.866f; // kąt > 150° = gładkie przejście
        }

        /// <summary>
        /// Oblicza kierunek w node na podstawie podłączonych krawędzi.
        /// Dla Endpoint: kierunek jedynej krawędzi (od node na zewnątrz).
        /// Dla Junction: uśredniony kierunek.
        /// </summary>
        public Vector3 GetNodeDirection(int nodeId)
        {
            if (!nodes.ContainsKey(nodeId)) return Vector3.forward;
            var node = nodes[nodeId];

            if (node.EdgeIds.Count == 0) return Vector3.forward;

            // Dla każdej krawędzi oblicz kierunek "na zewnątrz" od node'a
            Vector3 avgDir = Vector3.zero;
            foreach (int edgeId in node.EdgeIds)
            {
                if (!edges.ContainsKey(edgeId)) continue;
                var edge = edges[edgeId];

                // Kierunek od tego node'a do drugiego końca krawędzi
                int otherNode = edge.FromNodeId == nodeId ? edge.ToNodeId : edge.FromNodeId;
                if (!nodes.ContainsKey(otherNode)) continue;

                // Kierunek kontynuacji: "gdyby tor jechał dalej za node, w którą stronę"
                // FromNode: polyline idzie OD node'a, kontynuacja = w przeciwną stronę
                // ToNode: polyline DOCHODZI do node'a, kontynuacja = tą samą stronę
                if (edge.Polyline != null && edge.Polyline.Count >= 2)
                {
                    if (edge.FromNodeId == nodeId)
                        avgDir += -TrackGeometry.GetStartTangent(edge.Polyline);
                    else
                        avgDir += TrackGeometry.GetEndTangent(edge.Polyline);
                }
                else
                {
                    // Kierunek od node'a NA ZEWNĄTRZ (od krawędzi)
                    avgDir += (node.Position - nodes[otherNode].Position).normalized;
                }
            }

            return avgDir.magnitude > 0.01f ? avgDir.normalized : Vector3.forward;
        }

        /// <summary>Zwraca wszystkie node'y typu Endpoint</summary>
        public List<TrackNode> GetEndpointNodes()
        {
            return nodes.Values.Where(n => n.Type == NodeType.Endpoint && n.EdgeIds.Count > 0).ToList();
        }

        /// <summary>Zwraca wszystkie node'y typu Junction</summary>
        public List<TrackNode> GetJunctionNodes()
        {
            return nodes.Values.Where(n => n.Type == NodeType.Junction).ToList();
        }

        /// <summary>Zwraca node po ID.</summary>
        public TrackNode GetNode(int nodeId)
        {
            return nodes.ContainsKey(nodeId) ? nodes[nodeId] : null;
        }
    }
}
