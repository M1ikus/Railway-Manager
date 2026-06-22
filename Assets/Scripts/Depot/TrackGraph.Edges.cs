using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class TrackGraph
    {
        // ═══════════════════════════════════════════
        //  KRAWĘDZIE (EDGES) + krzywizna + wstawki
        // ═══════════════════════════════════════════

        /// <summary>Dodaje krawędź (prostą) między dwoma węzłami</summary>
        public int AddEdge(int fromNodeId, int toNodeId, bool hasCatenary = false)
        {
            if (!nodes.ContainsKey(fromNodeId) || !nodes.ContainsKey(toNodeId))
            {
                Log.Error($"[TrackGraph] Cannot add edge: node {fromNodeId} or {toNodeId} not found");
                return -1;
            }

            // Prosta polyline: start → end
            List<Vector3> polyline = TrackGeometry.GenerateStraightLine(
                nodes[fromNodeId].Position, nodes[toNodeId].Position);

            return AddEdgeWithPolyline(fromNodeId, toNodeId, polyline, hasCatenary);
        }

        /// <summary>Dodaje krawędź z podaną polyline (krzywą)</summary>
        public int AddEdgeWithPolyline(int fromNodeId, int toNodeId, List<Vector3> polyline, bool hasCatenary = false)
        {
            if (!nodes.ContainsKey(fromNodeId) || !nodes.ContainsKey(toNodeId))
            {
                Log.Error($"[TrackGraph] Cannot add edge: node {fromNodeId} or {toNodeId} not found");
                return -1;
            }

            int id = nextEdgeId++;
            float length = TrackGeometry.CalculatePolylineLength(polyline);

            edges[id] = new TrackEdge
            {
                Id = id,
                FromNodeId = fromNodeId,
                ToNodeId = toNodeId,
                Length = length,
                HasCatenary = hasCatenary,
                Polyline = polyline != null ? new List<Vector3>(polyline) : null
            };

            nodes[fromNodeId].EdgeIds.Add(id);
            nodes[toNodeId].EdgeIds.Add(id);

            UpdateNodeType(fromNodeId);
            UpdateNodeType(toNodeId);

            // Aktualizuj kierunki node'ów
            nodes[fromNodeId].Direction = GetNodeDirection(fromNodeId);
            nodes[toNodeId].Direction = GetNodeDirection(toNodeId);

            return id;
        }

        /// <summary>Znajduje krawędź łączącą dwa węzły</summary>
        public TrackEdge FindEdgeBetween(int nodeA, int nodeB)
        {
            if (!nodes.ContainsKey(nodeA)) return null;

            foreach (int edgeId in nodes[nodeA].EdgeIds)
            {
                if (!edges.ContainsKey(edgeId)) continue;
                var edge = edges[edgeId];
                if ((edge.FromNodeId == nodeA && edge.ToNodeId == nodeB) ||
                    (edge.FromNodeId == nodeB && edge.ToNodeId == nodeA))
                    return edge;
            }

            return null;
        }

        /// <summary>Zwraca krawędź po ID.</summary>
        public TrackEdge GetEdge(int edgeId)
        {
            return edges.ContainsKey(edgeId) ? edges[edgeId] : null;
        }

        // ═══════════════════════════════════════════
        //  ROZJAZDY — części (turnout parts)
        // ═══════════════════════════════════════════

        /// <summary>
        /// Oznacza krawędź jako część rozjazdu (prosta/odnoga).
        /// Wywołaj po AddEdge/AddEdgeWithPolyline.
        /// </summary>
        public void SetEdgeTurnoutPart(int edgeId, TurnoutPart part, int turnoutEntityId)
        {
            if (!edges.ContainsKey(edgeId)) return;
            edges[edgeId].TurnoutPart = part;
            edges[edgeId].TurnoutEntityId = turnoutEntityId;
        }

        // ═══════════════════════════════════════════
        //  KRZYWIZNA (CURVE DATA)
        // ═══════════════════════════════════════════

        /// <summary>Ustawia dane krzywizny na krawędzi</summary>
        public void SetEdgeCurve(int edgeId, float radius, float angle, bool isLeftCurve)
        {
            if (!edges.ContainsKey(edgeId)) return;

            edges[edgeId].Curve = new CurveData
            {
                Radius = radius,
                Angle = angle,
                ArcLength = radius * Mathf.Abs(angle),
                IsLeftCurve = isLeftCurve
            };
        }

        /// <summary>Oznacza krawędź jako prostą (R=0)</summary>
        public void SetEdgeStraight(int edgeId)
        {
            if (!edges.ContainsKey(edgeId)) return;
            edges[edgeId].Curve = new CurveData
            {
                Radius = 0f,
                Angle = 0f,
                ArcLength = edges[edgeId].Length,
                IsLeftCurve = false
            };
        }

        /// <summary>
        /// Automatycznie oblicza CurveData z polyline krawędzi.
        /// Używa GetMinimumRadius z TrackGeometry + heurystykę kierunku.
        /// </summary>
        public void ComputeEdgeCurveFromPolyline(int edgeId)
        {
            if (!edges.ContainsKey(edgeId)) return;
            var edge = edges[edgeId];

            if (edge.Polyline == null || edge.Polyline.Count < 3)
            {
                SetEdgeStraight(edgeId);
                return;
            }

            if (TrackGeometry.IsStraightPolyline(edge.Polyline))
            {
                SetEdgeStraight(edgeId);
                return;
            }

            float radius = TrackGeometry.GetMinimumRadius(edge.Polyline);

            // Kąt = arcLength / radius (przybliżenie z polyline)
            float arcLength = edge.Length;
            float angle = radius > 0.01f ? arcLength / radius : 0f;

            // Kierunek łuku: cross product startu i środka
            Vector3 startDir = TrackGeometry.GetStartTangent(edge.Polyline);
            Vector3 toMid = (edge.Polyline[edge.Polyline.Count / 2] - edge.Polyline[0]).normalized;
            float cross = startDir.x * toMid.z - startDir.z * toMid.x;
            bool isLeft = cross > 0f;

            edge.Curve = new CurveData
            {
                Radius = radius,
                Angle = angle,
                ArcLength = arcLength,
                IsLeftCurve = isLeft
            };
        }

        /// <summary>Zwraca wszystkie krawędzie z łukiem (R > 0)</summary>
        public List<TrackEdge> GetCurvedEdges()
        {
            return edges.Values.Where(e => e.Curve != null && !e.Curve.IsStraight).ToList();
        }

        // ═══════════════════════════════════════════
        //  WSTAWKI MIĘDZY ROZJAZDAMI
        // ═══════════════════════════════════════════

        /// <summary>
        /// Oznacza krawędź jako wstawkę między parą rozjazdów.
        /// turnoutIdA/B — id TurnoutEntity po obu stronach.
        /// </summary>
        public void SetEdgeInsert(int edgeId, InsertType type, int turnoutIdA, int turnoutIdB)
        {
            if (!edges.ContainsKey(edgeId)) return;
            edges[edgeId].Insert = new InsertData
            {
                Type = type,
                TurnoutIdA = turnoutIdA,
                TurnoutIdB = turnoutIdB
            };
        }

        /// <summary>Zwraca wszystkie wstawki danego typu</summary>
        public List<TrackEdge> GetInsertEdges(InsertType type = InsertType.None)
        {
            if (type == InsertType.None)
                return edges.Values.Where(e => e.Insert != null && e.Insert.Type != InsertType.None).ToList();

            return edges.Values.Where(e => e.Insert != null && e.Insert.Type == type).ToList();
        }

        /// <summary>
        /// Automatycznie wykrywa wstawki: krawędzie proste między dwoma node'ami Junction z iglicami.
        /// </summary>
        public void DetectInserts()
        {
            foreach (var edge in edges.Values)
            {
                if (edge.TurnoutPart != TurnoutPart.None) continue; // Pomijaj krawędzie będące częścią rozjazdu

                var fromNode = nodes.ContainsKey(edge.FromNodeId) ? nodes[edge.FromNodeId] : null;
                var toNode = nodes.ContainsKey(edge.ToNodeId) ? nodes[edge.ToNodeId] : null;

                if (fromNode == null || toNode == null) continue;
                if (fromNode.Blade == null && toNode.Blade == null) continue;

                // Oba końce to Junction z iglicami = wstawka
                if (fromNode.Blade != null && toNode.Blade != null)
                {
                    // Sprawdź orientację iglic: czy zwrócone do siebie czy w tym samym kierunku
                    bool facingEachOther = AreBladesOpposing(fromNode, toNode, edge.Id);

                    edge.Insert = new InsertData
                    {
                        Type = facingEachOther ? InsertType.BetweenFacingBlades : InsertType.BetweenPair,
                        TurnoutIdA = fromNode.Blade.TurnoutEntityId,
                        TurnoutIdB = toNode.Blade.TurnoutEntityId
                    };
                }
                else
                {
                    // Jeden koniec to rozjazd — wstawka między parą (pre/post segmentu)
                    var bladeNode = fromNode.Blade != null ? fromNode : toNode;
                    edge.Insert = new InsertData
                    {
                        Type = InsertType.BetweenPair,
                        TurnoutIdA = bladeNode.Blade.TurnoutEntityId,
                        TurnoutIdB = -1
                    };
                }
            }
        }

        /// <summary>
        /// Sprawdza czy iglice dwóch node'ów są zwrócone do siebie
        /// (iglica A patrzy w stronę B i odwrotnie).
        /// </summary>
        private bool AreBladesOpposing(TrackNode nodeA, TrackNode nodeB, int connectingEdgeId)
        {
            // Iglice są "zwrócone do siebie" gdy krawędź łącząca je
            // jest po stronie "trzeciej nogi" (body) obu rozjazdów,
            // tzn. to nie jest ani straightEdge ani divergingEdge dla żadnej iglicy.
            // W praktyce: iglice na obu końcach patrzą na siebie.
            var bladeA = nodeA.Blade;
            var bladeB = nodeB.Blade;

            bool aPointsToB = connectingEdgeId != bladeA.StraightEdgeId && connectingEdgeId != bladeA.DivergingEdgeId;
            bool bPointsToA = connectingEdgeId != bladeB.StraightEdgeId && connectingEdgeId != bladeB.DivergingEdgeId;

            // Obie iglice "patrzą" w stronę łączącej krawędzi = zwrócone do siebie
            return aPointsToB && bPointsToA;
        }
    }
}
