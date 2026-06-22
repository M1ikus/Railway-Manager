using System.Collections.Generic;
using UnityEngine;
using formap;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Reprezentuje granicę administracyjną z OSM (country lub voivodeship).
    /// Polygon jest trzymany jako sekwencja trójkątów (triangulowany przez formap/LibTess),
    /// PIP test sprawdza każdy trójkąt — point jest w regionie jeśli leży w dowolnym trójkącie.
    /// </summary>
    public class AdminRegion
    {
        public string name;                   // np. "województwo mazowieckie" albo "Polska"
        public int adminLevel;                 // 2 = country, 4 = województwo
        public string iso3166_1;               // np. "PL" (dla country)
        public string iso3166_2;               // np. "PL-MZ" (dla województwo)
        public BBox boundingBox;               // quick reject przed testem triangli

        /// <summary>
        /// Trójkąty poligonu — lista wierzchołków i indeksów jak w MeshGeometry.
        /// Po LibTess triangulacji każde 3 indeksy tworzą jeden trójkąt.
        /// </summary>
        public List<Vector2> vertices = new();
        public List<int> indices = new();

        /// <summary>Test point-in-polygon — iteruje po trójkątach i sprawdza każdy.</summary>
        public bool ContainsPoint(Vector2 point)
        {
            // Quick reject po bounding box
            if (point.x < boundingBox.MinX || point.x > boundingBox.MaxX
                || point.y < boundingBox.MinY || point.y > boundingBox.MaxY)
                return false;

            // Dla każdego trójkąta (3 kolejne indeksy) sprawdź czy punkt leży wewnątrz
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                var a = vertices[indices[i]];
                var b = vertices[indices[i + 1]];
                var c = vertices[indices[i + 2]];
                if (PointInTriangle(point, a, b, c))
                    return true;
            }
            return false;
        }

        /// <summary>Barycentric coordinates check — klasyczny triangle PIP test.</summary>
        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b);
            float d2 = Sign(p, b, c);
            float d3 = Sign(p, c, a);

            bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
            bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);

            // Punkt w trójkącie jeśli wszystkie znaki są takie same (lub zero na krawędzi)
            return !(hasNeg && hasPos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
            => (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }
}
