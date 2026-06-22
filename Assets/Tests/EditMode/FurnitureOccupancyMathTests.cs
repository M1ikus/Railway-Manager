using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using DepotSystem.Furniture;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>TD-034 Etap A: czysty wybór najbliższego wolnego mebla (deterministyczny, tie-break po id).</summary>
    public class FurnitureOccupancyMathTests
    {
        static (int, Vector2, bool) C(int id, float x, float z, bool occupied) => (id, new Vector2(x, z), occupied);

        [Test]
        public void PicksNearestFree()
        {
            var cands = new List<(int, Vector2, bool)>
            {
                C(1, 10f, 0f, false),
                C(2, 2f, 0f, false),
                C(3, 5f, 0f, false),
            };
            int id = FurnitureOccupancyMath.PickNearestFree(cands, Vector2.zero);
            Assert.AreEqual(2, id, "Najbliższy wolny (dist 2) wygrywa.");
        }

        [Test]
        public void SkipsOccupied()
        {
            var cands = new List<(int, Vector2, bool)>
            {
                C(1, 1f, 0f, true),   // najbliższy ale zajęty
                C(2, 4f, 0f, false),
                C(3, 9f, 0f, false),
            };
            int id = FurnitureOccupancyMath.PickNearestFree(cands, Vector2.zero);
            Assert.AreEqual(2, id, "Zajęty pomijany — wybrany najbliższy WOLNY.");
        }

        [Test]
        public void NoneFree_ReturnsMinusOne()
        {
            var cands = new List<(int, Vector2, bool)>
            {
                C(1, 1f, 0f, true),
                C(2, 2f, 0f, true),
            };
            Assert.AreEqual(-1, FurnitureOccupancyMath.PickNearestFree(cands, Vector2.zero), "Wszystkie zajęte → -1.");
        }

        [Test]
        public void EmptyAndNull_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, FurnitureOccupancyMath.PickNearestFree(new List<(int, Vector2, bool)>(), Vector2.zero));
            Assert.AreEqual(-1, FurnitureOccupancyMath.PickNearestFree(null, Vector2.zero));
        }

        [Test]
        public void TieBreak_LowerIdWins()
        {
            // Dwa równo-odległe wolne krzesła → niższe instanceId.
            var cands = new List<(int, Vector2, bool)>
            {
                C(7, 3f, 0f, false),
                C(4, 0f, 3f, false), // ta sama odległość (3) od origin
            };
            Assert.AreEqual(4, FurnitureOccupancyMath.PickNearestFree(cands, Vector2.zero), "Remis dystansu → niższe id (4).");
        }

        [Test]
        public void Deterministic_OrderIndependent()
        {
            var a = new List<(int, Vector2, bool)> { C(7, 3f, 0f, false), C(4, 0f, 3f, false), C(9, 1f, 0f, true) };
            var b = new List<(int, Vector2, bool)> { C(9, 1f, 0f, true), C(4, 0f, 3f, false), C(7, 3f, 0f, false) };
            int ra = FurnitureOccupancyMath.PickNearestFree(a, Vector2.zero);
            int rb = FurnitureOccupancyMath.PickNearestFree(b, Vector2.zero);
            Assert.AreEqual(ra, rb, "Ten sam zbiór w innej kolejności → ten sam wybór.");
            Assert.AreEqual(4, ra, "Remis → niższe id niezależnie od kolejności.");
        }
    }
}
