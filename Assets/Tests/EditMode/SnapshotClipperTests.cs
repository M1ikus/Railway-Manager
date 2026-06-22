using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using DepotSystem.Schemas.Snapshot;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-004: clipping polyline torów do prostokąta selekcji. Weryfikuje że tor w całości wewnątrz
    /// jest nietknięty, tor wystający jest przycięty na granicy (nowy endpoint na linii cięcia),
    /// a tor wchodzący-wychodzący-wchodzący daje wiele fragmentów. Czysta matematyka XZ (Y zachowane).
    /// </summary>
    public class SnapshotClipperTests
    {
        // Prostokąt 10×10 wokół origin: x[-5,5], z[-5,5].
        private static Bounds Rect10() => new Bounds(Vector3.zero, new Vector3(10f, 0.1f, 10f));

        private static void AssertApprox(Vector3 a, Vector3 b, string msg)
        {
            Assert.Less(Vector3.Distance(a, b), 1e-3f, $"{msg}: {a} != {b}");
        }

        [Test]
        public void FullyInside_ReturnsUnchangedCopy()
        {
            var poly = new List<Vector3> { new Vector3(-2, 0, 0), new Vector3(2, 0, 0), new Vector3(2, 0, 3) };
            var frags = SnapshotClipper.ClipPolylineToRectXZ(poly, Rect10());

            Assert.AreEqual(1, frags.Count, "tor w całości wewnątrz = 1 fragment");
            Assert.AreEqual(poly.Count, frags[0].Count, "fragment ma tyle samo punktów co oryginał");
            for (int i = 0; i < poly.Count; i++)
                AssertApprox(poly[i], frags[0][i], $"punkt {i} nietknięty");
        }

        [Test]
        public void CrossesOneEdge_ClipsAtBoundary()
        {
            // Z (-2,0,0) wewnątrz do (10,0,0) na zewnątrz — przecina x=5.
            var poly = new List<Vector3> { new Vector3(-2, 0, 0), new Vector3(10, 0, 0) };
            var frags = SnapshotClipper.ClipPolylineToRectXZ(poly, Rect10());

            Assert.AreEqual(1, frags.Count);
            Assert.AreEqual(2, frags[0].Count);
            AssertApprox(new Vector3(-2, 0, 0), frags[0][0], "start nietknięty");
            AssertApprox(new Vector3(5, 0, 0), frags[0][1], "koniec przycięty na x=5 (nowy endpoint)");
        }

        [Test]
        public void EnterExitEnter_ProducesTwoFragments()
        {
            // Wchodzi (z<=5), wychodzi górą (z=10), wraca w dół do wewnątrz.
            var poly = new List<Vector3>
            {
                new Vector3(-4, 0, 0),   // wewnątrz
                new Vector3(-4, 0, 10),  // na zewnątrz (z>5)
                new Vector3( 4, 0, 10),  // na zewnątrz
                new Vector3( 4, 0, 0),   // wewnątrz
            };
            var frags = SnapshotClipper.ClipPolylineToRectXZ(poly, Rect10());

            Assert.AreEqual(2, frags.Count, "wejście-wyjście-wejście = 2 fragmenty");
            AssertApprox(new Vector3(-4, 0, 0), frags[0][0], "frag0 start");
            AssertApprox(new Vector3(-4, 0, 5), frags[0][frags[0].Count - 1], "frag0 cięcie na z=5");
            AssertApprox(new Vector3(4, 0, 5), frags[1][0], "frag1 cięcie na z=5");
            AssertApprox(new Vector3(4, 0, 0), frags[1][frags[1].Count - 1], "frag1 koniec");
        }

        [Test]
        public void FullyOutside_ReturnsNoFragments()
        {
            var poly = new List<Vector3> { new Vector3(10, 0, 0), new Vector3(20, 0, 0) };
            var frags = SnapshotClipper.ClipPolylineToRectXZ(poly, Rect10());
            Assert.AreEqual(0, frags.Count);
        }

        [Test]
        public void DegenerateRect_ReturnsNoFragments()
        {
            var poly = new List<Vector3> { new Vector3(-2, 0, 0), new Vector3(2, 0, 0) };
            var degenerate = new Bounds(Vector3.zero, new Vector3(0f, 0.1f, 10f)); // szerokość 0
            var frags = SnapshotClipper.ClipPolylineToRectXZ(poly, degenerate);
            Assert.AreEqual(0, frags.Count);
        }

        [Test]
        public void PreservesYByInterpolation()
        {
            // Y rośnie liniowo 0→12 wzdłuż X od -2 do 10; cięcie na x=5 (t=0.583) → Y≈7.
            var poly = new List<Vector3> { new Vector3(-2, 0, 0), new Vector3(10, 12, 0) };
            var frags = SnapshotClipper.ClipPolylineToRectXZ(poly, Rect10());

            Assert.AreEqual(1, frags.Count);
            Vector3 cut = frags[0][frags[0].Count - 1];
            Assert.AreEqual(5f, cut.x, 1e-3f, "cięcie na x=5");
            Assert.AreEqual(7f, cut.y, 1e-2f, "Y zachowane przez interpolację (t=7/12*12=7)");
        }
    }
}
