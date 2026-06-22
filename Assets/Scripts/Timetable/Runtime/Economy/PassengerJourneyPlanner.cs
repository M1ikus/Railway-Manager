using System.Collections.Generic;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M-PaxV2 Faza C: planer podróży pasażera. Na grafie bezpośredniej osiągalności
    /// (stacja → krawędzie <see cref="DirectEdge"/>, każda = przejazd jednym kursem) szuka trasy
    /// origin→dest z MINIMALNĄ liczbą przesiadek, ≤2 (≤3 odcinki). Czysty + deterministyczny
    /// (kolejność krawędzi w grafie ustala wynik). Graf buduje PassengerManager z rozkładów (C.2).
    ///
    /// Bounded search: direct → 1 przesiadka → 2 przesiadki; zwraca pierwszą znalezioną na
    /// najniższym poziomie (preferuje mniej przesiadek). Worst case O(deg1×deg2) na hubie.
    /// </summary>
    public static class PassengerJourneyPlanner
    {
        public static PassengerJourney FindJourney(int origin, int dest,
                                                   IReadOnlyDictionary<int, List<DirectEdge>> graph)
        {
            if (graph == null || origin == dest) return null;

            // 0 przesiadek — bezpośredni kurs.
            if (TryEdge(graph, origin, dest, out var d0))
                return One(new JourneyLeg(origin, dest, d0.distanceKm, d0.commercialCategoryId));

            if (!graph.TryGetValue(origin, out var e1) || e1 == null) return null;

            // 1 przesiadka — origin → T1 → dest.
            for (int i = 0; i < e1.Count; i++)
            {
                int t1 = e1[i].toStationId;
                if (t1 == origin || t1 == dest) continue;
                if (TryEdge(graph, t1, dest, out var d2))
                    return Two(
                        new JourneyLeg(origin, t1, e1[i].distanceKm, e1[i].commercialCategoryId),
                        new JourneyLeg(t1, dest, d2.distanceKm, d2.commercialCategoryId));
            }

            // 2 przesiadki — origin → T1 → T2 → dest.
            for (int i = 0; i < e1.Count; i++)
            {
                int t1 = e1[i].toStationId;
                if (t1 == origin || t1 == dest) continue;
                if (!graph.TryGetValue(t1, out var e2) || e2 == null) continue;

                for (int j = 0; j < e2.Count; j++)
                {
                    int t2 = e2[j].toStationId;
                    if (t2 == origin || t2 == dest || t2 == t1) continue;
                    if (TryEdge(graph, t2, dest, out var d3))
                        return Three(
                            new JourneyLeg(origin, t1, e1[i].distanceKm, e1[i].commercialCategoryId),
                            new JourneyLeg(t1, t2, e2[j].distanceKm, e2[j].commercialCategoryId),
                            new JourneyLeg(t2, dest, d3.distanceKm, d3.commercialCategoryId));
                }
            }

            return null; // brak trasy w ≤2 przesiadkach
        }

        static bool TryEdge(IReadOnlyDictionary<int, List<DirectEdge>> graph, int from, int to, out DirectEdge edge)
        {
            edge = default;
            if (!graph.TryGetValue(from, out var edges) || edges == null) return false;
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].toStationId == to) { edge = edges[i]; return true; }
            }
            return false;
        }

        static PassengerJourney One(JourneyLeg a) => new PassengerJourney(new List<JourneyLeg> { a });
        static PassengerJourney Two(JourneyLeg a, JourneyLeg b) => new PassengerJourney(new List<JourneyLeg> { a, b });
        static PassengerJourney Three(JourneyLeg a, JourneyLeg b, JourneyLeg c) => new PassengerJourney(new List<JourneyLeg> { a, b, c });
    }
}
