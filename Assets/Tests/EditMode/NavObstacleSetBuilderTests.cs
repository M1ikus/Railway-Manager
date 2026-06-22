using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using DepotSystem;
using DepotSystem.Nav;
using DepotSystem.OutdoorEquipment;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-033 Etap C: konwersja danych zajezdni → przeszkody nav. Kluczowe: ściana z drzwiami dzieli
    /// się na 2 solid części + waypoint światła (przejście wolne), okno zostaje solid (parapet).
    /// </summary>
    public class NavObstacleSetBuilderTests
    {
        static WallSegment Wall(Vector2 a, Vector2 b, params WallOpening[] ops) =>
            new WallSegment
            {
                startPos = new Vector3(a.x, 0f, a.y),
                endPos = new Vector3(b.x, 0f, b.y),
                openings = new List<WallOpening>(ops)
            };

        [Test]
        public void Wall_NoOpenings_SingleSolid()
        {
            var obs = new List<NavRect>(); var wp = new List<Vector2>();
            NavObstacleSetBuilder.AddWall(Wall(new Vector2(0, 0), new Vector2(10, 0)), 0.2f, 0.35f, t => 1.2f, obs, wp);
            Assert.AreEqual(1, obs.Count);
            Assert.AreEqual(0, wp.Count);
        }

        [Test]
        public void Wall_WithDoor_SplitsAndWaypoint()
        {
            var door = new WallOpening { type = OpeningType.Door, distanceOnWall = 5f };
            var obs = new List<NavRect>(); var wp = new List<Vector2>();
            NavObstacleSetBuilder.AddWall(Wall(new Vector2(0, 0), new Vector2(10, 0), door), 0.2f, 0.35f, t => 1.2f, obs, wp);

            Assert.AreEqual(2, obs.Count, "Ściana z drzwiami = 2 solid części.");
            Assert.AreEqual(1, wp.Count);
            Assert.AreEqual(5f, wp[0].x, 1e-3f);
            Assert.AreEqual(0f, wp[0].y, 1e-3f);
            Assert.IsTrue(NavObstacles.SegmentClear(new Vector2(5, -2), new Vector2(5, 2), obs), "Przejście przez światło drzwi wolne.");
            Assert.IsFalse(NavObstacles.SegmentClear(new Vector2(2, -2), new Vector2(2, 2), obs), "Przez solid część zablokowane.");
        }

        [Test]
        public void Wall_WithWindow_StaysSolid()
        {
            var win = new WallOpening { type = OpeningType.Window, distanceOnWall = 5f };
            var obs = new List<NavRect>(); var wp = new List<Vector2>();
            NavObstacleSetBuilder.AddWall(Wall(new Vector2(0, 0), new Vector2(10, 0), win), 0.2f, 0.35f, t => 1.2f, obs, wp);

            Assert.AreEqual(1, obs.Count, "Okno = parapet = solid, brak splitu.");
            Assert.AreEqual(0, wp.Count);
            Assert.IsFalse(NavObstacles.SegmentClear(new Vector2(5, -2), new Vector2(5, 2), obs), "Przez okno zablokowane.");
        }

        [Test]
        public void Equipment_Aabb()
        {
            var e = new PlacedOutdoorEquipment { cornerA = new Vector3(10f, 0f, 10f), cornerB = new Vector3(18f, 0f, 16f) };
            var obs = new List<NavRect>();
            NavObstacleSetBuilder.AddEquipment(e, 0.35f, obs);
            Assert.AreEqual(1, obs.Count);
            Assert.IsTrue(obs[0].ContainsPoint(new Vector2(14f, 13f)), "Środek equipment w przeszkodzie.");
            Assert.IsFalse(obs[0].ContainsPoint(new Vector2(25f, 13f)), "Punkt z dala poza.");
        }

        [Test]
        public void Furniture_FootprintAabb()
        {
            var obs = new List<NavRect>();
            NavObstacleSetBuilder.AddFurnitureFootprint(new Vector3(10f, 0f, 10f), 2, 1, 0, 0.35f, obs);
            Assert.AreEqual(1, obs.Count, "Footprint mebla → 1 przeszkoda.");
            Assert.IsTrue(obs[0].HalfExtents.x > 0.35f && obs[0].HalfExtents.y > 0.35f, "Rozmiar > sama inflacja.");
        }
    }
}
