using NUnit.Framework;
using UnityEngine;
using DepotSystem;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>M-Windows P1: czysta matematyka układu okien (kaskada + clamp paska tytułu).</summary>
    public class WindowLayoutMathTests
    {
        // ── Kaskada ──────────────────────────────────────────────

        [Test]
        public void Cascade_FirstWindow_NoOffset()
        {
            Assert.AreEqual(Vector2.zero, WindowLayoutMath.CascadeOffset(0, 28f, 8));
        }

        [Test]
        public void Cascade_SecondWindow_StepsRightAndDown()
        {
            var o = WindowLayoutMath.CascadeOffset(1, 28f, 8);
            Assert.AreEqual(28f, o.x, 1e-4f);
            Assert.AreEqual(-28f, o.y, 1e-4f, "Y w dół = ujemne.");
        }

        [Test]
        public void Cascade_WrapsAtWrapCount()
        {
            var first = WindowLayoutMath.CascadeOffset(0, 28f, 8);
            var wrapped = WindowLayoutMath.CascadeOffset(8, 28f, 8);
            Assert.AreEqual(first, wrapped, "Po `wrap` oknach kaskada wraca na start.");
        }

        // ── Clamp paska tytułu ───────────────────────────────────

        [Test]
        public void Clamp_InsideBounds_Unchanged()
        {
            var p = new Vector2(10f, 20f);
            var r = WindowLayoutMath.ClampTitleBarOnScreen(p, new Vector2(400f, 500f), new Vector2(1920f, 1080f), 56f);
            Assert.AreEqual(p.x, r.x, 1e-3f);
            Assert.AreEqual(p.y, r.y, 1e-3f);
        }

        [Test]
        public void Clamp_DraggedTooHigh_TopStaysAtTopEdge()
        {
            var win = new Vector2(400f, 500f);
            var parent = new Vector2(1920f, 1080f);
            var r = WindowLayoutMath.ClampTitleBarOnScreen(new Vector2(0f, 99999f), win, parent, 56f);
            float top = r.y + win.y * 0.5f;
            Assert.AreEqual(parent.y * 0.5f, top, 1e-3f, "Góra okna nie wychodzi nad górną krawędź.");
        }

        [Test]
        public void Clamp_DraggedTooLow_TitleStaysReachable()
        {
            var win = new Vector2(400f, 500f);
            var parent = new Vector2(1920f, 1080f);
            float keep = 56f;
            var r = WindowLayoutMath.ClampTitleBarOnScreen(new Vector2(0f, -99999f), win, parent, keep);
            float top = r.y + win.y * 0.5f;
            Assert.AreEqual(-parent.y * 0.5f + keep, top, 1e-3f, "Pasek tytułu zostaje min keep od dołu.");
        }

        [Test]
        public void Clamp_DraggedTooFarRight_KeepsMarginVisible()
        {
            var win = new Vector2(400f, 500f);
            var parent = new Vector2(1920f, 1080f);
            float keep = 56f;
            var r = WindowLayoutMath.ClampTitleBarOnScreen(new Vector2(99999f, 0f), win, parent, keep);
            float expectedMaxX = parent.x * 0.5f + win.x * 0.5f - keep;
            Assert.AreEqual(expectedMaxX, r.x, 1e-3f);
        }

        // ── Resize ───────────────────────────────────────────────

        static readonly Vector2 RMin = new Vector2(300f, 220f);
        static readonly Vector2 RMax = new Vector2(1100f, 900f);

        [Test]
        public void Resize_RightEdge_GrowsWidth_MovesCenterRight()
        {
            WindowLayoutMath.Resize(Vector2.zero, new Vector2(400f, 500f), new Vector2(50f, 0f),
                1, 0, RMin, RMax, out var pos, out var size);
            Assert.AreEqual(new Vector2(450f, 500f), size);
            Assert.AreEqual(new Vector2(25f, 0f), pos);
        }

        [Test]
        public void Resize_LeftEdge_DragLeft_GrowsWidth_MovesCenterLeft()
        {
            WindowLayoutMath.Resize(Vector2.zero, new Vector2(400f, 500f), new Vector2(-50f, 0f),
                -1, 0, RMin, RMax, out var pos, out var size);
            Assert.AreEqual(new Vector2(450f, 500f), size);
            Assert.AreEqual(new Vector2(-25f, 0f), pos);
        }

        [Test]
        public void Resize_BottomEdge_DragDown_GrowsHeight_MovesCenterDown()
        {
            WindowLayoutMath.Resize(Vector2.zero, new Vector2(400f, 500f), new Vector2(0f, -50f),
                0, -1, RMin, RMax, out var pos, out var size);
            Assert.AreEqual(new Vector2(400f, 550f), size);
            Assert.AreEqual(new Vector2(0f, -25f), pos);
        }

        [Test]
        public void Resize_ClampsToMin()
        {
            WindowLayoutMath.Resize(Vector2.zero, RMin, new Vector2(-100f, 0f),
                1, 0, RMin, RMax, out var pos, out var size);
            Assert.AreEqual(RMin.x, size.x, 1e-3f, "Szerokość nie schodzi poniżej min.");
            Assert.AreEqual(0f, pos.x, 1e-3f, "Brak zmiany rozmiaru → środek bez ruchu.");
        }

        [Test]
        public void Resize_ClampsToMax_CenterUsesActualDelta()
        {
            WindowLayoutMath.Resize(Vector2.zero, new Vector2(1090f, 500f), new Vector2(50f, 0f),
                1, 0, RMin, RMax, out var pos, out var size);
            Assert.AreEqual(RMax.x, size.x, 1e-3f);
            Assert.AreEqual((RMax.x - 1090f) * 0.5f, pos.x, 1e-3f, "Środek o połowę FAKTYCZNej zmiany (clamped).");
        }

        [Test]
        public void Resize_Corner_BottomRight_BothAxes()
        {
            WindowLayoutMath.Resize(Vector2.zero, new Vector2(400f, 500f), new Vector2(50f, -30f),
                1, -1, RMin, RMax, out var pos, out var size);
            Assert.AreEqual(new Vector2(450f, 530f), size);
            Assert.AreEqual(new Vector2(25f, -15f), pos);
        }
    }
}
