using System.Collections.Generic;
using UnityEngine;
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M-PaxV2 Faza C.2a: buduje graf bezpośredniej osiągalności (stacja → <see cref="DirectEdge"/>)
    /// z rozkładów. Krawędź A→B istnieje, gdy jakiś rozkład ma przystanki PH A przed B (przejazd
    /// jednym kursem). Niesie dystans (różnica distanceFromStartM) + kategorię handlową rozkładu.
    ///
    /// Czysty + deterministyczny. Buduje PassengerManager raz (cache, rebuild przy zmianie rozkładów —
    /// jak OD matrix). O(rozkłady × stopy²) — dla pełnej Polski ciężkie, dlatego cache'owane.
    /// Przy wielu rozkładach A→B trzymamy krawędź o NAJKRÓTSZYM dystansie (deterministyczny wybór).
    /// </summary>
    public static class JourneyGraphBuilder
    {
        public static Dictionary<int, List<DirectEdge>> Build(
            IReadOnlyList<TimetableObj> timetables, IReadOnlyDictionary<int, int> nodeToStation)
        {
            var graph = new Dictionary<int, List<DirectEdge>>();
            if (timetables == null || nodeToStation == null) return graph;

            for (int t = 0; t < timetables.Count; t++)
            {
                var tt = timetables[t];
                if (tt == null || tt.isDeliveryTimetable || tt.stops == null) continue;
                var stops = tt.stops;

                for (int i = 0; i < stops.Count; i++)
                {
                    if (stops[i].stopType != StopType.PH) continue;
                    if (!nodeToStation.TryGetValue(stops[i].stationNodeId, out int fromSt)) continue;

                    for (int j = i + 1; j < stops.Count; j++)
                    {
                        if (stops[j].stopType != StopType.PH) continue;
                        if (!nodeToStation.TryGetValue(stops[j].stationNodeId, out int toSt)) continue;
                        if (fromSt == toSt) continue;

                        float distKm = Mathf.Max(0f, (stops[j].distanceFromStartM - stops[i].distanceFromStartM) / 1000f);
                        AddEdge(graph, fromSt, toSt, distKm, tt.commercialCategoryId);
                    }
                }
            }
            return graph;
        }

        static void AddEdge(Dictionary<int, List<DirectEdge>> graph, int from, int to, float distKm, string categoryId)
        {
            if (!graph.TryGetValue(from, out var list))
            {
                list = new List<DirectEdge>();
                graph[from] = list;
            }
            for (int k = 0; k < list.Count; k++)
            {
                if (list[k].toStationId == to)
                {
                    // Trzymaj najkrótszy dystans (deterministyczny, bez duplikatów).
                    if (distKm < list[k].distanceKm)
                        list[k] = new DirectEdge(to, distKm, categoryId);
                    return;
                }
            }
            list.Add(new DirectEdge(to, distKm, categoryId));
        }
    }
}
