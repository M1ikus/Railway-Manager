using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using DepotSystem.Nav;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-033 Etap B: visibility-graph router (A*). Przeszkody podawane JUŻ napompowane.
    /// Weryfikuje: linia prosta gdy wolno, objazd przeszkody (legi wolne + długość), wejście do
    /// pokoju przez drzwi (luka), brak ścieżki → fallback+flag, determinizm.
    /// </summary>
    public class VisibilityGraphRouterTests
    {
        static float PathLen(List<Vector2> p)
        {
            float s = 0f;
            for (int i = 1; i < p.Count; i++) s += Vector2.Distance(p[i - 1], p[i]);
            return s;
        }

        static void AssertLegsClear(List<Vector2> p, List<NavRect> obs)
        {
            for (int i = 1; i < p.Count; i++)
                Assert.IsTrue(NavObstacles.SegmentClear(p[i - 1], p[i], obs),
                    $"Leg {i} ({p[i - 1]}→{p[i]}) musi być wolny od przeszkód.");
        }

        [Test]
        public void DirectClear_TwoPointPath()
        {
            var obs = new List<NavRect> { new NavRect(new Vector2(5f, 20f), new Vector2(1f, 1f), 0f) }; // z boku
            var res = VisibilityGraphRouter.Route(new Vector2(0f, 0f), new Vector2(10f, 0f), obs);
            Assert.IsFalse(res.ViaFallback);
            Assert.AreEqual(2, res.Path.Count, "Wolna linia prosta = 2 punkty.");
        }

        [Test]
        public void AroundSingleBox_RoutesAround_LegsClear()
        {
            var box = new NavRect(new Vector2(5f, 0f), new Vector2(1f, 1f), 0f); // x[4,6] z[-1,1]
            var obs = new List<NavRect> { box };
            var start = new Vector2(0f, 0f);
            var dest = new Vector2(10f, 0f);

            var res = VisibilityGraphRouter.Route(start, dest, obs);

            Assert.IsFalse(res.ViaFallback, "Box do ominięcia, nie fallback.");
            Assert.GreaterOrEqual(res.Path.Count, 3, "Objazd = ≥3 punkty.");
            Assert.AreEqual(start, res.Path[0]);
            Assert.AreEqual(dest, res.Path[res.Path.Count - 1]);
            AssertLegsClear(res.Path, obs);
            // Ręcznie: 0→(4,±1)→(6,±1)→10 = √17 + 2 + √17 ≈ 10.246.
            float len = PathLen(res.Path);
            Assert.That(len, Is.GreaterThan(10f), "Dłuższe niż prosta (objazd).");
            Assert.That(len, Is.LessThan(10.5f), "≈ optymalny objazd po rogach.");
        }

        [Test]
        public void IntoRoom_ThroughDoorGap()
        {
            // Pokój 0..10 ze ścianą dolną z drzwiami x[4,6]; reszta solidna. dest w środku, start poza.
            float th = 0.2f, infl = 0.3f;
            var obs = new List<NavRect>
            {
                NavRect.FromSegment(new Vector2(-1f, 0f), new Vector2(4f, 0f), th).Inflate(infl),   // bottom A
                NavRect.FromSegment(new Vector2(6f, 0f), new Vector2(11f, 0f), th).Inflate(infl),   // bottom B (luka x[4,6])
                NavRect.FromSegment(new Vector2(-1f, 10f), new Vector2(11f, 10f), th).Inflate(infl),// top
                NavRect.FromSegment(new Vector2(0f, -1f), new Vector2(0f, 11f), th).Inflate(infl),  // left
                NavRect.FromSegment(new Vector2(10f, -1f), new Vector2(10f, 11f), th).Inflate(infl),// right
            };
            var doorLight = new List<Vector2> { new Vector2(5f, 0f) }; // światło drzwi
            var start = new Vector2(2f, -5f);
            var dest = new Vector2(5f, 5f);

            var res = VisibilityGraphRouter.Route(start, dest, obs, doorLight);

            Assert.IsFalse(res.ViaFallback, "Drzwi są — musi wejść, nie fallback przez ścianę.");
            Assert.Greater(res.Path.Count, 2, "Trasa do drzwi = nie prosta.");
            Assert.AreEqual(dest, res.Path[res.Path.Count - 1]);
            AssertLegsClear(res.Path, obs);
            // Trasa musi przejść przez światło drzwi (jakiś wierzchołek w pasie x[4,6] blisko z=0).
            bool throughDoor = res.Path.Exists(v => v.x > 4f && v.x < 6f && Mathf.Abs(v.y) < 0.6f);
            Assert.IsTrue(throughDoor, "Trasa musi prowadzić przez światło drzwi.");
        }

        [Test]
        public void EnclosedRoom_NoDoor_Fallback()
        {
            // Zamknięty pokój (4 solidne ściany, nachodzące w rogach), dest w środku → brak ścieżki.
            float th = 0.2f, infl = 0.3f;
            var obs = new List<NavRect>
            {
                NavRect.FromSegment(new Vector2(-1f, 0f), new Vector2(11f, 0f), th).Inflate(infl),
                NavRect.FromSegment(new Vector2(-1f, 10f), new Vector2(11f, 10f), th).Inflate(infl),
                NavRect.FromSegment(new Vector2(0f, -1f), new Vector2(0f, 11f), th).Inflate(infl),
                NavRect.FromSegment(new Vector2(10f, -1f), new Vector2(10f, 11f), th).Inflate(infl),
            };
            var res = VisibilityGraphRouter.Route(new Vector2(5f, -5f), new Vector2(5f, 5f), obs);
            Assert.IsTrue(res.ViaFallback, "Zamknięty pokój → brak ścieżki → fallback (guard upstream).");
        }

        [Test]
        public void Deterministic_SameInputSamePath()
        {
            var obs = new List<NavRect> { new NavRect(new Vector2(5f, 0f), new Vector2(1f, 1f), 0f) };
            var a = VisibilityGraphRouter.Route(new Vector2(0f, 0f), new Vector2(10f, 0f), obs).Path;
            var b = VisibilityGraphRouter.Route(new Vector2(0f, 0f), new Vector2(10f, 0f), obs).Path;
            Assert.AreEqual(a.Count, b.Count, "Ta sama liczba punktów.");
            for (int i = 0; i < a.Count; i++)
                Assert.AreEqual(a[i], b[i], $"Punkt {i} identyczny (determinizm).");
        }
    }
}
