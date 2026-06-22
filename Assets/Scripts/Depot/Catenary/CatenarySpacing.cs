using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// Lookup table rozstawu słupów trakcyjnych w zależności od promienia łuku.
    /// Dane oficjalne PKP + ekstrapolacja do R75.
    /// Interpolacja liniowa między punktami tabeli.
    /// </summary>
    public static class CatenarySpacing
    {
        // Tabela: (promień w metrach, rozstaw słupów w metrach)
        // Posortowana rosnąco po promieniu.
        private static readonly (float radius, float spacing)[] Table =
        {
            (75f,   15f),
            (80f,   16f),
            (90f,   17f),
            (100f,  18f),
            (120f,  19f),
            (130f,  20f),
            (150f,  22f),
            (180f,  24f),
            (200f,  25f),
            (250f,  28f),
            (300f,  31f),
            (350f,  33f),
            (400f,  36f),
            (500f,  40f),
            (600f,  44f),
            (700f,  47f),
            (800f,  51f),
            (1000f, 57f),
            (1200f, 61f),
            (1500f, 69f),
            (1800f, 75f),
            (2000f, 80f),
        };

        /// <summary>Stały rozstaw na prostej (R > 2000m).</summary>
        public const float StraightSpacing = 65f;

        /// <summary>Minimalny dopuszczalny promień łuku w grze.</summary>
        public const float MinRadius = 75f;

        /// <summary>
        /// Zwraca rozstaw słupów dla danego promienia łuku.
        /// Dla prostej (R > 2000 lub float.MaxValue) zwraca StraightSpacing.
        /// Między punktami tabeli — interpolacja liniowa.
        /// </summary>
        public static float GetSpacing(float radius)
        {
            // Prosta lub bardzo duży promień
            if (radius <= 0f || radius > 2000f || float.IsInfinity(radius) || float.IsNaN(radius))
                return StraightSpacing;

            // Poniżej minimalnego promienia — clamp do R75
            if (radius < Table[0].radius)
                return Table[0].spacing;

            // Powyżej ostatniego wpisu tabeli
            if (radius >= Table[Table.Length - 1].radius)
                return Table[Table.Length - 1].spacing;

            // Znajdź przedział i interpoluj liniowo
            for (int i = 0; i < Table.Length - 1; i++)
            {
                if (radius >= Table[i].radius && radius <= Table[i + 1].radius)
                {
                    float t = (radius - Table[i].radius) /
                              (Table[i + 1].radius - Table[i].radius);
                    return Mathf.Lerp(Table[i].spacing, Table[i + 1].spacing, t);
                }
            }

            return StraightSpacing;
        }

        /// <summary>
        /// Oblicza promień krzywizny w punkcie polyline na podstawie trzech punktów.
        /// Używa wielu próbek (sampleOffsets) dla lepszej detekcji łagodnych łuków.
        /// Zwraca float.MaxValue dla prostej.
        /// </summary>
        public static float ComputeLocalRadius(
            System.Collections.Generic.List<Vector3> polyline,
            float distance)
        {
            if (polyline == null || polyline.Count < 3)
                return float.MaxValue;

            float totalLen = TrackGeometry.CalculatePolylineLength(polyline);
            if (totalLen < 4f)
                return float.MaxValue;

            // Próbuj kilka sampleOffsets — mniejsze łapią ciasne łuki,
            // większe łapią łagodne łuki (R > 1000)
            float bestRadius = float.MaxValue;
            float[] offsets = { 5f, 15f, 30f };

            foreach (float sampleOffset in offsets)
            {
                if (totalLen < sampleOffset * 2f) continue;

                float d0 = Mathf.Max(0f, distance - sampleOffset);
                float d2 = Mathf.Min(totalLen, distance + sampleOffset);
                float d1 = (d0 + d2) / 2f; // środek między próbkami

                if (d2 - d0 < 2f) continue;

                var (p0, _) = TrackGeometry.GetPointAtDistance(polyline, d0);
                var (p1, _) = TrackGeometry.GetPointAtDistance(polyline, d1);
                var (p2, _) = TrackGeometry.GetPointAtDistance(polyline, d2);

                float r = CircumscribedRadius2D(p0, p1, p2);
                if (r < bestRadius)
                    bestRadius = r;
            }

            return bestRadius;
        }

        /// <summary>
        /// Promień okręgu przez 3 punkty w płaszczyźnie XZ.
        /// </summary>
        private static float CircumscribedRadius2D(Vector3 a, Vector3 b, Vector3 c)
        {
            float ax = a.x, az = a.z;
            float bx = b.x, bz = b.z;
            float cx = c.x, cz = c.z;

            float ab = Mathf.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            float bc = Mathf.Sqrt((cx - bx) * (cx - bx) + (cz - bz) * (cz - bz));
            float ca = Mathf.Sqrt((ax - cx) * (ax - cx) + (az - cz) * (az - cz));

            float s = (ab + bc + ca) / 2f;
            float areaSquared = s * (s - ab) * (s - bc) * (s - ca);

            // Dla łagodnych łuków area jest bardzo mała — zmniejszony próg
            if (areaSquared < 1e-10f) return float.MaxValue;

            float area = Mathf.Sqrt(areaSquared);
            return (ab * bc * ca) / (4f * area);
        }
    }
}
