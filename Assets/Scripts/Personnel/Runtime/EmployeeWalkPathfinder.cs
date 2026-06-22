using System.Collections.Generic;
using DepotSystem;
using DepotSystem.Nav;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// TD-025: Wrapper na <see cref="PathGraph"/> dla pracownika idącego po zajezdni.
    ///
    /// <para><b>Strategia (user decision 2026-05-11 pkt 4):</b></para>
    /// <list type="number">
    /// <item>Znajdź najbliższy node startowy (gate) i najbliższy node końcowy do destination.</item>
    /// <item>BFS po grafie → polyline węzłów.</item>
    /// <item>Jeśli koniec grafu jest dalej niż <see cref="GraphSnapToleranceM"/> od destination,
    /// dopisz straight-line segment od ostatniego node'a do realnego destination
    /// ("z końca chodnika do celu").</item>
    /// <item>Brak PathGraph w scenie LUB BFS zwraca null → pełen straight-line + warning.</item>
    /// </list>
    ///
    /// <para><b>Lookup:</b> singleton lookup raz w <see cref="EnsureGraph"/>, cache'owany
    /// dopóki nie zmieni się scena. Topology change event <c>PathGraph.OnTopologyChanged</c>
    /// nie inwaliduje cache'a (pracownik trzyma już zbudowany polyline i go nie re-routuje
    /// mid-walk — re-route przy kolejnym task'u).</para>
    /// </summary>
    public static class EmployeeWalkPathfinder
    {
        /// <summary>Threshold "blisko węzła" — gdy destination jest dalej niż X m od najbliższego
        /// node'a, dopisujemy straight-line do realnego destination.</summary>
        public const float GraphSnapToleranceM = 1.5f;

        /// <summary>Max odległość lookup'u dla GetNearestNode (m). Zapobiega routingowi
        /// przez cały graf gdy pracownik startuje daleko od jakiegokolwiek chodnika.</summary>
        public const float MaxNodeLookupDistanceM = 200f;

        static PathGraph _cachedGraph;
        static readonly HashSet<int> _warnedEmployees = new(); // log fallback raz per pracownik

        /// <summary>
        /// Buduje polyline waypoints od <paramref name="start"/> do <paramref name="destination"/>.
        /// Zawsze zwraca co najmniej 2 punkty: start + destination (straight-line fallback).
        /// Y koordynaty są zachowane z PathNode.Position (Y=0 dla większości grafu depot).
        /// </summary>
        /// <param name="employeeId">Pracownik (do throttle warning logów).</param>
        public static List<Vector3> BuildPolyline(int employeeId, Vector3 start, Vector3 destination)
        {
            // TD-033: pełna nawigacja (visibility graph + drzwi + omijanie przeszkód, deterministyczna)
            // przez DepotNavService. Zastępuje 4 straight-line fallbacki + konektory (przenikały ściany).
            var nav = DepotNavService.Instance;
            if (nav != null)
            {
                var route = nav.BuildRoute(start, destination);
                if (route != null && route.Count >= 2) return route;
            }
            // Fallback gdy brak DepotNavService (scena bez bootstrapu / test headless): legacy PathGraph + straight-line.
            return BuildPolylineLegacy(employeeId, start, destination);
        }

        static List<Vector3> BuildPolylineLegacy(int employeeId, Vector3 start, Vector3 destination)
        {
            var result = new List<Vector3>(8);
            result.Add(start);

            var graph = EnsureGraph();
            if (graph == null)
            {
                if (_warnedEmployees.Add(employeeId))
                    Log.Warn($"[EmployeeWalkPathfinder] No PathGraph in scene for employee #{employeeId} — using straight-line.");
                result.Add(destination);
                return result;
            }

            int startNodeId = graph.GetNearestNode(start, MaxNodeLookupDistanceM);
            int endNodeId = graph.GetNearestNode(destination, MaxNodeLookupDistanceM);

            if (startNodeId < 0 || endNodeId < 0)
            {
                if (_warnedEmployees.Add(employeeId))
                    Log.Warn($"[EmployeeWalkPathfinder] Employee #{employeeId}: no nearby PathGraph node " +
                             $"(start={(startNodeId<0?"miss":"ok")} end={(endNodeId<0?"miss":"ok")}) — straight-line.");
                result.Add(destination);
                return result;
            }

            // BFS path of nodeIds
            var nodePath = graph.FindPath(startNodeId, endNodeId);
            if (nodePath == null || nodePath.Count == 0)
            {
                if (_warnedEmployees.Add(employeeId))
                    Log.Warn($"[EmployeeWalkPathfinder] Employee #{employeeId}: BFS fail #{startNodeId}→#{endNodeId} — straight-line.");
                result.Add(destination);
                return result;
            }

            // Konwertuj nodeIds na world positions
            for (int i = 0; i < nodePath.Count; i++)
            {
                var node = graph.GetNode(nodePath[i]);
                if (node == null) continue;
                result.Add(node.Position);
            }

            // "Z końca chodnika do celu" — jeśli ostatni node grafu jest dalej niż tolerance
            // od realnego destination, dopisz finalny straight-line segment.
            var endNode = graph.GetNode(endNodeId);
            if (endNode != null && Vector3.Distance(endNode.Position, destination) > GraphSnapToleranceM)
            {
                result.Add(destination);
            }

            return result;
        }

        /// <summary>Zwraca cached PathGraph lub <c>FindAnyObjectByType</c> jeśli pierwsze
        /// wywołanie / poprzedni graf został zniszczony (scena unloaded).
        ///
        /// <para>Unity '==' operator overload zwraca true dla zniszczonych MonoBehaviour —
        /// nie potrzebujemy track'ować sceneHandle, samo null-check wystarcza.</para>
        /// </summary>
        static PathGraph EnsureGraph()
        {
            if (_cachedGraph != null) return _cachedGraph;
            _cachedGraph = Object.FindAnyObjectByType<PathGraph>();
            return _cachedGraph;
        }

        /// <summary>Reset cache (np. po wyładowaniu sceny). Wywołane z EmployeeWalkSimulator.</summary>
        public static void Reset()
        {
            _cachedGraph = null;
            _warnedEmployees.Clear();
        }
    }
}
