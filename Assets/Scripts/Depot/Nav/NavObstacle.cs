using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem.Nav
{
    /// <summary>
    /// TD-033: zorientowany prostokąt-przeszkoda w płaszczyźnie XZ (x = world X, y = world Z).
    /// Pokrywa AABB (rotation = 0 → outdoor equipment / meble) oraz ścianę (oriented rect z
    /// startPos→endPos + grubość). Czysta geometria, bez stanu Unity scene → testowalne w EditMode
    /// (wzór `TrackOccupancyMath` / `ConsistCouplingMath`). Bazowy klocek visibility-graph nav (TD-033).
    /// </summary>
    public readonly struct NavRect
    {
        public readonly Vector2 Center;
        public readonly Vector2 HalfExtents;   // half (x = szerokość, y = długość) w lokalnym frame
        public readonly float RotationRad;     // 0 = axis-aligned

        public NavRect(Vector2 center, Vector2 halfExtents, float rotationRad)
        {
            Center = center;
            HalfExtents = new Vector2(Mathf.Abs(halfExtents.x), Mathf.Abs(halfExtents.y));
            RotationRad = rotationRad;
        }

        /// <summary>AABB z min/max (world XZ).</summary>
        public static NavRect FromAabb(Vector2 min, Vector2 max)
            => new NavRect((min + max) * 0.5f, (max - min) * 0.5f, 0f);

        /// <summary>Ściana: odcinek a→b z grubością (oriented rect). Lokalne x = wzdłuż, y = w poprzek.</summary>
        public static NavRect FromSegment(Vector2 a, Vector2 b, float thickness)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            float rot = len > 1e-6f ? Mathf.Atan2(d.y, d.x) : 0f;
            return new NavRect((a + b) * 0.5f, new Vector2(len * 0.5f, thickness * 0.5f), rot);
        }

        /// <summary>Napompowanie o promień kapsuły (Minkowski dla rect = +r na każdą półoś).</summary>
        public NavRect Inflate(float radius)
            => new NavRect(Center, HalfExtents + new Vector2(radius, radius), RotationRad);

        Vector2 ToLocal(Vector2 worldPt)
        {
            Vector2 rel = worldPt - Center;
            float c = Mathf.Cos(-RotationRad), s = Mathf.Sin(-RotationRad);
            return new Vector2(rel.x * c - rel.y * s, rel.x * s + rel.y * c);
        }

        Vector2 ToWorld(Vector2 localPt)
        {
            float c = Mathf.Cos(RotationRad), s = Mathf.Sin(RotationRad);
            return Center + new Vector2(localPt.x * c - localPt.y * s, localPt.x * s + localPt.y * c);
        }

        /// <summary>4 rogi CCW w stałej, deterministycznej kolejności (BL, BR, TR, TL w lokalnym frame).</summary>
        public void GetCorners(Vector2[] outCorners)
        {
            float hx = HalfExtents.x, hy = HalfExtents.y;
            outCorners[0] = ToWorld(new Vector2(-hx, -hy));
            outCorners[1] = ToWorld(new Vector2(hx, -hy));
            outCorners[2] = ToWorld(new Vector2(hx, hy));
            outCorners[3] = ToWorld(new Vector2(-hx, hy));
        }

        public Vector2[] Corners()
        {
            var c = new Vector2[4];
            GetCorners(c);
            return c;
        }

        public bool ContainsPoint(Vector2 worldPt)
        {
            Vector2 l = ToLocal(worldPt);
            return Mathf.Abs(l.x) <= HalfExtents.x && Mathf.Abs(l.y) <= HalfExtents.y;
        }

        /// <summary>Ściśle wewnątrz (z marginesem) — punkt „pogrzebany". Rogi na krawędzi → false (zostają węzłami).</summary>
        public bool ContainsPointStrict(Vector2 worldPt, float margin)
        {
            Vector2 l = ToLocal(worldPt);
            return Mathf.Abs(l.x) < HalfExtents.x - margin && Mathf.Abs(l.y) < HalfExtents.y - margin;
        }

        /// <summary>
        /// Czy odcinek a→b PENETRUJE wnętrze prostokąta (slab method w lokalnym frame). Grazing wzdłuż
        /// krawędzi NIE blokuje — po inflacji krawędź = promień kapsuły od realnej przeszkody, więc
        /// dozwolone (i konieczne, by visibility-graph łączył sąsiednie rogi wokół przeszkody).
        /// </summary>
        public bool IntersectsSegment(Vector2 a, Vector2 b)
        {
            Vector2 la = ToLocal(a), lb = ToLocal(b);
            Vector2 d = lb - la;
            float t0 = 0f, t1 = 1f;
            if (!ClipSlab(la.x, d.x, -HalfExtents.x, HalfExtents.x, ref t0, ref t1)) return false;
            if (!ClipSlab(la.y, d.y, -HalfExtents.y, HalfExtents.y, ref t0, ref t1)) return false;
            if (t1 <= t0 + 1e-6f) return false; // tylko dotyk rogu/krawędzi lub zero-length
            float tm = (t0 + t1) * 0.5f;
            Vector2 mid = la + d * tm;
            const float m = 1e-4f;
            return Mathf.Abs(mid.x) < HalfExtents.x - m && Mathf.Abs(mid.y) < HalfExtents.y - m;
        }

        static bool ClipSlab(float origin, float dir, float min, float max, ref float t0, ref float t1)
        {
            const float eps = 1e-7f;
            if (Mathf.Abs(dir) < eps)
                return origin >= min && origin <= max; // równoległy do osi: tylko jeśli w paśmie
            float inv = 1f / dir;
            float tA = (min - origin) * inv;
            float tB = (max - origin) * inv;
            if (tA > tB) { var tmp = tA; tA = tB; tB = tmp; }
            if (tA > t0) t0 = tA;
            if (tB < t1) t1 = tB;
            return t0 <= t1;
        }
    }

    /// <summary>TD-033: helpery zbioru przeszkód dla visibility-graph routingu.</summary>
    public static class NavObstacles
    {
        /// <summary>Czy odcinek a→b jest wolny od WSZYSTKICH przeszkód (visibility-edge test).</summary>
        public static bool SegmentClear(Vector2 a, Vector2 b, IReadOnlyList<NavRect> rects)
        {
            for (int i = 0; i < rects.Count; i++)
                if (rects[i].IntersectsSegment(a, b)) return false;
            return true;
        }

        /// <summary>Czy punkt leży w którejkolwiek przeszkodzie (filtr nieprawidłowych węzłów grafu).</summary>
        public static bool PointInAny(Vector2 p, IReadOnlyList<NavRect> rects)
        {
            for (int i = 0; i < rects.Count; i++)
                if (rects[i].ContainsPoint(p)) return true;
            return false;
        }
    }
}
