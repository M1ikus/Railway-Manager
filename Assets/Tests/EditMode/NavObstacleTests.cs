using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using DepotSystem.Nav;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-033 Etap A: czysta geometria przeszkód nav (NavRect). Weryfikuje intersekcję odcinek×rect,
    /// inflację, i KLUCZOWE: wąska luka drzwi (1 m) przetrwa inflację o promień kapsuły (0.3 m) →
    /// przejście przez światło pozostaje wolne (inaczej A* uznałby drzwi za zamknięte).
    /// </summary>
    public class NavObstacleTests
    {
        [Test]
        public void Aabb_ContainsAndIntersect()
        {
            var r = NavRect.FromAabb(new Vector2(10f, 10f), new Vector2(18f, 16f)); // WashZone 8×6
            Assert.IsTrue(r.ContainsPoint(new Vector2(14f, 13f)), "Środek w środku.");
            Assert.IsFalse(r.ContainsPoint(new Vector2(20f, 13f)), "Punkt poza.");
            Assert.IsTrue(r.IntersectsSegment(new Vector2(0f, 13f), new Vector2(30f, 13f)), "Odcinek przez środek przecina.");
            Assert.IsFalse(r.IntersectsSegment(new Vector2(0f, 30f), new Vector2(30f, 30f)), "Odcinek ponad nie przecina.");
        }

        [Test]
        public void Inflate_GrowsHalfExtents()
        {
            var r = new NavRect(Vector2.zero, new Vector2(2f, 1f), 0f); // center origin, he (2,1)
            var inf = r.Inflate(0.5f);
            Assert.AreEqual(2.5f, inf.HalfExtents.x, 1e-4f);
            Assert.AreEqual(1.5f, inf.HalfExtents.y, 1e-4f);
            Assert.IsTrue(inf.ContainsPoint(new Vector2(2.4f, 0f)), "Punkt w napompowanym...");
            Assert.IsFalse(r.ContainsPoint(new Vector2(2.4f, 0f)), "...ale poza oryginałem.");
        }

        [Test]
        public void WallSegment_BlocksThrough_ClearAlong()
        {
            var w = NavRect.FromSegment(new Vector2(0f, 0f), new Vector2(5f, 0f), 0.2f);
            Assert.IsTrue(w.IntersectsSegment(new Vector2(2f, -1f), new Vector2(2f, 1f)), "W poprzek ściany = blok.");
            Assert.IsFalse(w.IntersectsSegment(new Vector2(2f, 2f), new Vector2(2f, 3f)), "Obok ściany = wolne.");
        }

        [Test]
        public void NarrowDoorGap_StaysPassableAfterInflation()
        {
            // Dwie ściany w jednej linii z luką 1 m (drzwi), inflacja 0.3 m → luka 0.4 m.
            var a = NavRect.FromSegment(new Vector2(0f, 0f), new Vector2(5f, 0f), 0.2f).Inflate(0.3f);
            var b = NavRect.FromSegment(new Vector2(6f, 0f), new Vector2(11f, 0f), 0.2f).Inflate(0.3f);
            var rects = new List<NavRect> { a, b };

            Assert.IsTrue(
                NavObstacles.SegmentClear(new Vector2(5.5f, -2f), new Vector2(5.5f, 2f), rects),
                "Przejście przez światło drzwi (0.4 m) musi pozostać wolne mimo inflacji.");
            Assert.IsFalse(
                NavObstacles.SegmentClear(new Vector2(2f, -2f), new Vector2(2f, 2f), rects),
                "Przejście przez ścianę A musi być zablokowane.");
        }

        [Test]
        public void PointInAny_DetectsInside()
        {
            var rects = new List<NavRect> { NavRect.FromAabb(new Vector2(0f, 0f), new Vector2(2f, 2f)) };
            Assert.IsTrue(NavObstacles.PointInAny(new Vector2(1f, 1f), rects));
            Assert.IsFalse(NavObstacles.PointInAny(new Vector2(5f, 5f), rects));
        }

        [Test]
        public void RotatedWall_BlocksAcross()
        {
            // Ściana pod 45° z (0,0) do (5,5).
            var w = NavRect.FromSegment(new Vector2(0f, 0f), new Vector2(5f, 5f), 0.4f);
            Assert.IsTrue(w.ContainsPoint(new Vector2(2.5f, 2.5f)), "Środek ukośnej ściany.");
            Assert.IsTrue(w.IntersectsSegment(new Vector2(3f, 2f), new Vector2(2f, 3f)), "W poprzek ukośnej = blok.");
            Assert.IsFalse(w.IntersectsSegment(new Vector2(0f, 4f), new Vector2(1f, 5f)), "Obok ukośnej = wolne.");
        }
    }
}
