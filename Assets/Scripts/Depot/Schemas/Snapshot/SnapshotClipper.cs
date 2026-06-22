using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem.Schemas.Snapshot
{
    /// <summary>
    /// TD-004 — clipping polyline torów do prostokąta selekcji (Wariant A z `memory/depot_tools_design.md`).
    ///
    /// MD-8 MVP zachowywał całe tory verbatim (all-or-nothing), więc tory zaznaczone przez ">50%
    /// punktów w prostokącie" potrafiły wystawać poza rectangle. Ten clipper tnie polyline na
    /// granicy prostokąta XZ i tworzy nowy endpoint na linii cięcia — snapshot ma czyste granice.
    ///
    /// Czysta matematyka (Liang-Barsky per segment w płaszczyźnie XZ, Y zachowane przez interpolację),
    /// EditMode-testowalne — wzór z `TrackOccupancyMath`.
    /// </summary>
    public static class SnapshotClipper
    {
        /// <summary>Tolerance numeryczna (m) — boundary-inclusive + dedup zerowych segmentów.</summary>
        public const float Epsilon = 1e-4f;

        /// <summary>
        /// Tnie polyline (world coords) do osiowego prostokąta XZ (z `bounds.min/max` x,z).
        /// Zwraca 0..N fragmentów (każdy ≥2 punkty) — tor wchodzący i wychodzący z prostokąta daje
        /// więcej niż 1 fragment. Y zachowane przez interpolację.
        ///
        /// Fast-path: jeśli CAŁA polyline jest wewnątrz prostokąta → zwraca jej niezmienioną kopię
        /// (gwarancja: tor w całości wewnątrz pozostaje nietknięty).
        /// </summary>
        public static List<List<Vector3>> ClipPolylineToRectXZ(IReadOnlyList<Vector3> polyline, Bounds rectXZ)
        {
            var result = new List<List<Vector3>>();
            if (polyline == null || polyline.Count < 2) return result;

            float xMin = rectXZ.min.x, xMax = rectXZ.max.x;
            float zMin = rectXZ.min.z, zMax = rectXZ.max.z;
            if (xMax - xMin < Epsilon || zMax - zMin < Epsilon) return result; // zdegenerowany prostokąt

            // Fast-path: wszystkie punkty wewnątrz → segmenty też (prostokąt wypukły) → zwróć kopię.
            bool allInside = true;
            for (int i = 0; i < polyline.Count; i++)
            {
                if (!InsideXZ(polyline[i], xMin, xMax, zMin, zMax)) { allInside = false; break; }
            }
            if (allInside)
            {
                result.Add(new List<Vector3>(polyline));
                return result;
            }

            List<Vector3> current = null;
            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector3 a = polyline[i];
                Vector3 b = polyline[i + 1];

                if (!ClipSegmentXZ(a, b, xMin, xMax, zMin, zMax, out float t0, out float t1))
                {
                    CloseFragment(result, ref current); // segment całkowicie poza prostokątem
                    continue;
                }

                Vector3 entry = Vector3.LerpUnclamped(a, b, t0);
                Vector3 exit = Vector3.LerpUnclamped(a, b, t1);
                bool startsInside = t0 <= Epsilon;
                bool endsInside = t1 >= 1f - Epsilon;

                if (current == null)
                {
                    current = new List<Vector3> { entry };
                }
                else if (!startsInside)
                {
                    // re-wejście po przerwie → zamknij poprzedni fragment, zacznij nowy
                    CloseFragment(result, ref current);
                    current = new List<Vector3> { entry };
                }
                // gdy current != null && startsInside: entry == ostatni punkt (poprzedni exit) → pomiń

                AddIfDistinct(current, exit);

                if (!endsInside)
                {
                    CloseFragment(result, ref current); // segment wychodzi przed swoim końcem
                }
            }
            CloseFragment(result, ref current);

            return result;
        }

        private static bool InsideXZ(Vector3 p, float xMin, float xMax, float zMin, float zMax)
        {
            return p.x >= xMin - Epsilon && p.x <= xMax + Epsilon
                && p.z >= zMin - Epsilon && p.z <= zMax + Epsilon;
        }

        /// <summary>Liang-Barsky: portion [t0,t1] segmentu a→b wewnątrz prostokąta XZ. false = brak.</summary>
        private static bool ClipSegmentXZ(Vector3 a, Vector3 b,
            float xMin, float xMax, float zMin, float zMax,
            out float t0, out float t1)
        {
            t0 = 0f; t1 = 1f;
            float dx = b.x - a.x;
            float dz = b.z - a.z;

            if (!ClipTest(-dx, a.x - xMin, ref t0, ref t1)) return false; // lewa
            if (!ClipTest(dx, xMax - a.x, ref t0, ref t1)) return false;  // prawa
            if (!ClipTest(-dz, a.z - zMin, ref t0, ref t1)) return false; // dół
            if (!ClipTest(dz, zMax - a.z, ref t0, ref t1)) return false;  // góra
            return t0 <= t1;
        }

        private static bool ClipTest(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 1e-7f) return q >= 0f; // równoległy do krawędzi: wewnątrz iff q>=0
            float r = q / p;
            if (p < 0f)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }

        private static void AddIfDistinct(List<Vector3> list, Vector3 p)
        {
            if (list.Count == 0 || Vector3.Distance(list[list.Count - 1], p) > Epsilon)
                list.Add(p);
        }

        private static void CloseFragment(List<List<Vector3>> result, ref List<Vector3> current)
        {
            if (current != null && current.Count >= 2)
                result.Add(current);
            current = null;
        }
    }
}
