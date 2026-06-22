using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Timetable.UI;
using formap;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// Etap A RouteMapPreview: czysta matematyka mini-mapy OSM (bounds / pokrycie kafli /
    /// fit ortho / pan / zoom / adaptacyjna szerokość). Bez sceny i GPU.
    /// </summary>
    public class RouteMapPreviewMathTests
    {
        private const float Eps = 0.01f;

        // ---------- TryGetBounds (pojedyncza polyline) ----------

        [Test]
        public void TryGetBounds_EmptyOrNull_False()
        {
            Assert.IsFalse(RouteMapPreviewMath.TryGetBounds((IReadOnlyList<Vector2>)null, out _));
            Assert.IsFalse(RouteMapPreviewMath.TryGetBounds(new List<Vector2>(), out _));
        }

        [Test]
        public void TryGetBounds_ComputesMinMax()
        {
            var pts = new List<Vector2>
            {
                new Vector2(100f, 200f),
                new Vector2(-50f, 800f),
                new Vector2(300f, 50f),
            };
            Assert.IsTrue(RouteMapPreviewMath.TryGetBounds(pts, out BBox b));
            Assert.AreEqual(-50f, b.MinX, Eps);
            Assert.AreEqual(50f, b.MinY, Eps);
            Assert.AreEqual(300f, b.MaxX, Eps);
            Assert.AreEqual(800f, b.MaxY, Eps);
        }

        [Test]
        public void TryGetBounds_AppliesPadding()
        {
            var pts = new List<Vector2> { new Vector2(0f, 0f), new Vector2(1000f, 1000f) };
            Assert.IsTrue(RouteMapPreviewMath.TryGetBounds(pts, out BBox b, paddingM: 250f));
            Assert.AreEqual(-250f, b.MinX, Eps);
            Assert.AreEqual(-250f, b.MinY, Eps);
            Assert.AreEqual(1250f, b.MaxX, Eps);
            Assert.AreEqual(1250f, b.MaxY, Eps);
        }

        // ---------- TryGetBounds (wiele polilinii) ----------

        [Test]
        public void TryGetBounds_MultiPolyline_UnionAndSkipsNull()
        {
            var polys = new List<IReadOnlyList<Vector2>>
            {
                new List<Vector2> { new Vector2(0f, 0f), new Vector2(100f, 100f) },
                null, // pomijane
                new List<Vector2> { new Vector2(-200f, 500f) },
            };
            Assert.IsTrue(RouteMapPreviewMath.TryGetBounds(polys, out BBox b));
            Assert.AreEqual(-200f, b.MinX, Eps);
            Assert.AreEqual(0f, b.MinY, Eps);
            Assert.AreEqual(100f, b.MaxX, Eps);
            Assert.AreEqual(500f, b.MaxY, Eps);
        }

        [Test]
        public void TryGetBounds_MultiPolyline_AllEmpty_False()
        {
            var polys = new List<IReadOnlyList<Vector2>> { null, new List<Vector2>() };
            Assert.IsFalse(RouteMapPreviewMath.TryGetBounds(polys, out _));
        }

        // ---------- TilesCovering / TileCount ----------

        [Test]
        public void TilesCovering_SingleTile()
        {
            // bbox w obrębie jednego kafla (TILE_SIZE = 10000)
            var b = new BBox { MinX = 1000f, MinY = 1000f, MaxX = 2000f, MaxY = 2000f };
            var tiles = RouteMapPreviewMath.TilesCovering(b);
            Assert.AreEqual(1, tiles.Count);
            Assert.AreEqual(TileGrid.GetTileID(0, 0), tiles[0]);
            Assert.AreEqual(1, RouteMapPreviewMath.TileCount(b));
        }

        [Test]
        public void TilesCovering_TwoByTwo()
        {
            // od grid (0,0) do grid (1,1) → 4 kafle
            var b = new BBox { MinX = 5000f, MinY = 5000f, MaxX = 15000f, MaxY = 15000f };
            var tiles = RouteMapPreviewMath.TilesCovering(b);
            Assert.AreEqual(4, tiles.Count);
            Assert.AreEqual(4, RouteMapPreviewMath.TileCount(b));
            CollectionAssert.Contains(tiles, TileGrid.GetTileID(0, 0));
            CollectionAssert.Contains(tiles, TileGrid.GetTileID(1, 0));
            CollectionAssert.Contains(tiles, TileGrid.GetTileID(0, 1));
            CollectionAssert.Contains(tiles, TileGrid.GetTileID(1, 1));
        }

        [Test]
        public void TilesCovering_NegativeCoords()
        {
            var b = new BBox { MinX = -15000f, MinY = -5000f, MaxX = -5000f, MaxY = 5000f };
            // gridX: floor(-15000/10000)=-2 .. floor(-5000/10000)=-1 → 2 kolumny
            // gridY: floor(-5000/10000)=-1 .. floor(5000/10000)=0 → 2 wiersze → 4 kafle
            Assert.AreEqual(4, RouteMapPreviewMath.TileCount(b));
            Assert.AreEqual(4, RouteMapPreviewMath.TilesCovering(b).Count);
        }

        [Test]
        public void TilesCovering_DegenerateBBox_Empty()
        {
            var b = new BBox { MinX = 100f, MinY = 100f, MaxX = -100f, MaxY = -100f };
            Assert.AreEqual(0, RouteMapPreviewMath.TilesCovering(b).Count);
            Assert.AreEqual(0, RouteMapPreviewMath.TileCount(b));
        }

        // ---------- FitOrtho ----------

        [Test]
        public void FitOrtho_SquareBounds_AspectOne()
        {
            var b = new BBox { MinX = 0f, MinY = 0f, MaxX = 10000f, MaxY = 10000f };
            RouteMapPreviewMath.FitOrtho(b, rtAspect: 1f, out Vector2 center, out float ortho, marginFrac: 0.12f);
            Assert.AreEqual(5000f, center.x, Eps);
            Assert.AreEqual(5000f, center.y, Eps);
            // max(5000, 5000) * 1.12 = 5600
            Assert.AreEqual(5600f, ortho, 1f);
        }

        [Test]
        public void FitOrtho_WideBounds_GovernedByWidth_WhenAspectOne()
        {
            var b = new BBox { MinX = 0f, MinY = 0f, MaxX = 20000f, MaxY = 10000f };
            RouteMapPreviewMath.FitOrtho(b, rtAspect: 1f, out _, out float ortho, marginFrac: 0.12f);
            // needByWidth = 10000/1 = 10000 > needByHeight 5000 → 10000*1.12 = 11200
            Assert.AreEqual(11200f, ortho, 1f);
        }

        [Test]
        public void FitOrtho_WideBounds_WideAspect_FitsExactly()
        {
            var b = new BBox { MinX = 0f, MinY = 0f, MaxX = 20000f, MaxY = 10000f };
            RouteMapPreviewMath.FitOrtho(b, rtAspect: 2f, out _, out float ortho, marginFrac: 0f);
            // needByWidth = 10000/2 = 5000 == needByHeight 5000 → 5000 (bez marginu)
            Assert.AreEqual(5000f, ortho, 1f);
        }

        [Test]
        public void FitOrtho_ClampsToMin()
        {
            var b = new BBox { MinX = 0f, MinY = 0f, MaxX = 10f, MaxY = 10f };
            RouteMapPreviewMath.FitOrtho(b, rtAspect: 1f, out _, out float ortho);
            Assert.AreEqual(RouteMapPreviewMath.MinOrthoSizeM, ortho, Eps);
        }

        // ---------- PanScreenDeltaToWorld ----------

        [Test]
        public void Pan_ConvertsAndInverts()
        {
            // ortho 5000, RT 500px → worldPerPixel = 2*5000/500 = 20
            Vector2 world = RouteMapPreviewMath.PanScreenDeltaToWorld(
                new Vector2(10f, -5f), orthoSize: 5000f, rtPixelSize: new Vector2(500f, 500f));
            Assert.AreEqual(-200f, world.x, Eps); // 10 * 20, zanegowane
            Assert.AreEqual(100f, world.y, Eps);  // -5 * 20, zanegowane
        }

        [Test]
        public void Pan_ZeroRtSize_Safe()
        {
            Vector2 world = RouteMapPreviewMath.PanScreenDeltaToWorld(
                new Vector2(10f, 10f), 5000f, new Vector2(0f, 0f));
            Assert.AreEqual(Vector2.zero, world);
        }

        // ---------- ZoomStep ----------

        [Test]
        public void Zoom_PositiveScroll_ZoomsIn()
        {
            // mult = 1 - 1*1.5*0.1 = 0.85 → 5000*0.85 = 4250
            float ortho = RouteMapPreviewMath.ZoomStep(5000f, scrollDelta: 1f);
            Assert.AreEqual(4250f, ortho, Eps);
        }

        [Test]
        public void Zoom_NegativeScroll_ZoomsOut()
        {
            // mult = 1 + 0.15 = 1.15 → 5750
            float ortho = RouteMapPreviewMath.ZoomStep(5000f, scrollDelta: -1f);
            Assert.AreEqual(5750f, ortho, Eps);
        }

        [Test]
        public void Zoom_ClampsMinAndMax()
        {
            float atMin = RouteMapPreviewMath.ZoomStep(300f, scrollDelta: 50f); // ekstremalny zoom in
            Assert.AreEqual(RouteMapPreviewMath.MinOrthoSizeM, atMin, Eps);

            float atMax = RouteMapPreviewMath.ZoomStep(590000f, scrollDelta: -50f); // ekstremalny zoom out
            Assert.AreEqual(RouteMapPreviewMath.MaxOrthoSizeM, atMax, Eps);
        }

        // ---------- AdaptiveWidth ----------

        [Test]
        public void AdaptiveWidth_ClampsLow()
        {
            // 100*0.01 = 1 → clamp do min 2.5
            Assert.AreEqual(RouteMapPreviewMath.MinWidthM, RouteMapPreviewMath.AdaptiveWidth(100f), Eps);
        }

        [Test]
        public void AdaptiveWidth_ClampsHigh()
        {
            // 50000*0.01 = 500 → clamp do max 250
            Assert.AreEqual(RouteMapPreviewMath.MaxWidthM, RouteMapPreviewMath.AdaptiveWidth(50000f), Eps);
        }

        [Test]
        public void AdaptiveWidth_LinearMidRange()
        {
            // 10000*0.01 = 100 (w [2.5, 250])
            Assert.AreEqual(100f, RouteMapPreviewMath.AdaptiveWidth(10000f), Eps);
        }
    }
}
