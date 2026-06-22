using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem.Nav
{
    /// <summary>
    /// TD-033: routing po visibility graph (rogi napompowanych przeszkód + dodatkowe waypointy =
    /// światła drzwi) + A*. Zwraca polyline obstacle-free w XZ. Czysty, deterministyczny
    /// (skan po indeksach, tie-break = najniższy indeks, brak iteracji po HashSet/Dict order) →
    /// MP/EditMode-friendly. Przeszkody muszą być JUŻ napompowane o promień (robi to builder).
    ///
    /// Perf: A* O(n²) bez kopca (Depot nie widzi Timetable.MinHeap), visibility liczona on-demand.
    /// n = 2 + 4×przeszkody + waypointy (~100-200) → OK per zapytanie; cache adjacency w DepotNavService (D/I).
    /// </summary>
    public static class VisibilityGraphRouter
    {
        public struct RouteResult
        {
            public List<Vector2> Path;   // start..dest (≥2 punkty)
            public bool ViaFallback;     // true = brak ścieżki w grafie → bezpośredni odcinek (guard upstream)
        }

        const float NodeMargin = 1e-3f;  // margines „ściśle wewnątrz" przy filtrze węzłów
        const float ImproveEps = 1e-4f;  // próg poprawy g[] (anty-jitter, determinizm)

        /// <param name="obstacles">przeszkody JUŻ napompowane o promień kapsuły</param>
        /// <param name="extraWaypoints">opcjonalne dodatkowe węzły (światła drzwi / jamb-y)</param>
        public static RouteResult Route(Vector2 start, Vector2 dest,
            IReadOnlyList<NavRect> obstacles, IReadOnlyList<Vector2> extraWaypoints = null)
        {
            // 1. Linia prosta wolna → najkrótsza.
            if (NavObstacles.SegmentClear(start, dest, obstacles))
                return new RouteResult { Path = new List<Vector2> { start, dest }, ViaFallback = false };

            // 2. Węzły: [0]=start, [1]=dest, potem rogi przeszkód + waypointy (pomiń pogrzebane).
            var nodes = new List<Vector2>(8 + obstacles.Count * 4) { start, dest };
            var corners = new Vector2[4];
            for (int i = 0; i < obstacles.Count; i++)
            {
                obstacles[i].GetCorners(corners);
                for (int k = 0; k < 4; k++)
                    if (!StrictlyInsideAny(corners[k], obstacles)) nodes.Add(corners[k]);
            }
            if (extraWaypoints != null)
                for (int i = 0; i < extraWaypoints.Count; i++)
                    if (!StrictlyInsideAny(extraWaypoints[i], obstacles)) nodes.Add(extraWaypoints[i]);

            var pathIdx = AStar(nodes, obstacles);
            if (pathIdx == null)
                return new RouteResult { Path = new List<Vector2> { start, dest }, ViaFallback = true };

            var path = new List<Vector2>(pathIdx.Count);
            for (int i = 0; i < pathIdx.Count; i++) path.Add(nodes[pathIdx[i]]);
            return new RouteResult { Path = path, ViaFallback = false };
        }

        static bool StrictlyInsideAny(Vector2 p, IReadOnlyList<NavRect> rects)
        {
            for (int i = 0; i < rects.Count; i++)
                if (rects[i].ContainsPointStrict(p, NodeMargin)) return true;
            return false;
        }

        /// <summary>Deterministyczny A* na implicit visibility graph. nodes[0]=start, nodes[1]=dest.</summary>
        static List<int> AStar(List<Vector2> nodes, IReadOnlyList<NavRect> obstacles)
        {
            int n = nodes.Count;
            var g = new float[n];
            var f = new float[n];
            var came = new int[n];
            var open = new bool[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.PositiveInfinity; f[i] = float.PositiveInfinity; came[i] = -1; }

            Vector2 goal = nodes[1];
            g[0] = 0f;
            f[0] = Vector2.Distance(nodes[0], goal);
            open[0] = true;

            while (true)
            {
                int cur = -1;
                float best = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                    if (open[i] && f[i] < best) { best = f[i]; cur = i; } // tie → najniższy indeks (strict <)
                if (cur < 0) return null; // brak ścieżki

                if (cur == 1) return Reconstruct(came, 1);

                open[cur] = false;
                closed[cur] = true;
                Vector2 pc = nodes[cur];

                for (int j = 0; j < n; j++)
                {
                    if (j == cur || closed[j]) continue;
                    if (!NavObstacles.SegmentClear(pc, nodes[j], obstacles)) continue;
                    float tentative = g[cur] + Vector2.Distance(pc, nodes[j]);
                    if (tentative < g[j] - ImproveEps)
                    {
                        came[j] = cur;
                        g[j] = tentative;
                        f[j] = tentative + Vector2.Distance(nodes[j], goal);
                        open[j] = true;
                    }
                }
            }
        }

        static List<int> Reconstruct(int[] came, int goalIdx)
        {
            var rev = new List<int>();
            int c = goalIdx;
            while (c >= 0) { rev.Add(c); c = came[c]; }
            rev.Reverse();
            return rev;
        }
    }
}
