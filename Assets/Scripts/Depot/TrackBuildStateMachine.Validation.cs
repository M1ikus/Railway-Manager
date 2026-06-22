using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public partial class TrackBuildStateMachine
    {
        // ═══════════════════════════════════════════
        //  WALIDACJA KOLIZJI — track overlap + buildings
        // ═══════════════════════════════════════════

        /// <summary>
        /// Zwraca true jeśli środkowa część polyline nakłada się geometrycznie na istniejący tor.
        /// Endpoints (pierwsze/ostatnie 1.5m) są ignorowane — tam łączymy tory.
        /// Zoptymalizowane: rzadsze próbkowanie, early-out z AABB, max 3 sample pointy.
        /// </summary>
        private bool IsPolylineOverlapping(List<Vector3> polyline, float overlapDist = 0.9f,
            float endpointExclude = 1.5f)
        {
            if (trackBuilder == null) return false;
            float totalLen = TrackGeometry.CalculatePolylineLength(polyline);
            if (totalLen <= endpointExclude * 2f) return false;

            // BUG-067 fix: dynamic sample count proporcjonalny do długości toru, nie cap na 3.
            // 1 sample na ~5m → tor 200m dostaje ~40 samples zamiast 3. Wcześniej dla tego samego
            // toru próbki były co ~50m, kolizja przez środek istniejącego toru niewykryta.
            // Cap 50 dla bardzo długich torów (>250m) żeby nie zabić perf.
            float usableLen = totalLen - endpointExclude * 2f;
            int sampleCount = Mathf.Clamp(Mathf.FloorToInt(usableLen / 5f), 3, 50);
            float step = usableLen / (sampleCount + 1);

            var samplePoints = new Vector2[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = endpointExclude + step * (i + 1);
                var (pt, _) = TrackGeometry.GetPointAtDistance(polyline, t);
                samplePoints[i] = new Vector2(pt.x, pt.z);
            }

            // AABB nowej polyline do szybkiego odrzucenia
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var sp in samplePoints)
            {
                if (sp.x < minX) minX = sp.x; if (sp.x > maxX) maxX = sp.x;
                if (sp.y < minZ) minZ = sp.y; if (sp.y > maxZ) maxZ = sp.y;
            }
            float margin = overlapDist + 5f;
            minX -= margin; maxX += margin;
            minZ -= margin; maxZ += margin;

            foreach (var placed in trackBuilder.PlacedTracks)
            {
                if (placed.Polyline == null || placed.Polyline.Count < 2) continue;

                // AABB check na istniejący tor
                if (!PolylineIntersectsAABB(placed.Polyline, minX, maxX, minZ, maxZ))
                    continue;

                foreach (var sp in samplePoints)
                {
                    if (IsPointNearPolyline(sp, placed.Polyline, overlapDist, endpointExclude))
                        return true;
                }
            }
            return false;
        }

        private static bool PolylineIntersectsAABB(List<Vector3> poly,
            float minX, float maxX, float minZ, float maxZ)
        {
            for (int i = 0; i < poly.Count; i++)
            {
                float x = poly[i].x, z = poly[i].z;
                if (x >= minX && x <= maxX && z >= minZ && z <= maxZ)
                    return true;
            }
            return false;
        }

        private bool IsPolylineOverlappingBuildings(List<Vector3> polyline)
        {
            float totalLen = TrackGeometry.CalculatePolylineLength(polyline);
            if (totalLen < 1f) return false;

            // Próbkuj punkty co 2m wzdłuż polyline
            float step = 2f;
            for (float d = 0f; d <= totalLen; d += step)
            {
                var (pt, _) = TrackGeometry.GetPointAtDistance(polyline, d);
                Vector3 center = new Vector3(pt.x, 1.5f, pt.z);
                Vector3 halfExtents = new Vector3(1.2f, 2f, 1.2f);

                Collider[] hits = Physics.OverlapBox(center, halfExtents, Quaternion.identity);
                foreach (var hit in hits)
                {
                    if (hit.CompareTag("Wall"))
                        return true;
                }
            }
            return false;
        }

        private static bool IsPointNearPolyline(Vector2 pt, List<Vector3> poly,
            float threshold, float endExclude)
        {
            float threshSq = threshold * threshold;

            for (int i = 1; i < poly.Count; i++)
            {
                Vector2 a = new Vector2(poly[i-1].x, poly[i-1].z);
                Vector2 b = new Vector2(poly[i].x,   poly[i].z);

                float distSq = DistSqPointSegment2D(pt, a, b);
                if (distSq < threshSq) return true;
            }
            return false;
        }

        private static float DistSqPointSegment2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return (p - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            Vector2 closest = a + t * ab;
            return (p - closest).sqrMagnitude;
        }
    }
}
