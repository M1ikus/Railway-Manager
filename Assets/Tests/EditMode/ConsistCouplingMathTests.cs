using System.Collections.Generic;
using NUnit.Framework;
using DepotSystem;
using RailwayManager.Fleet;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-032 Etap A: testy czystej logiki łączenia/dzielenia składów (ConsistCouplingMath).
    /// Geometria footprintu (anchor nosa, domknięcie styku), kolejność nos→tył (chasing/head-on),
    /// split, match FleetConsistData, dedup nazwy. EditMode.
    /// </summary>
    public class ConsistCouplingMathTests
    {
        // ── MergeFootprint ──────────────────────────────────────────
        [Test]
        public void MergeFootprint_Forward_AnchorNoseAtMax_ClosesGap()
        {
            var (f, r) = ConsistCouplingMath.MergeFootprint(0f, 100f, 1, 80f);
            Assert.That(r, Is.EqualTo(100f).Within(1e-4), "Nos (max) stały.");
            Assert.That(f, Is.EqualTo(20f).Within(1e-4));
            Assert.That(r - f, Is.EqualTo(80f).Within(1e-4), "Długość = suma (luka styku domknięta).");
        }

        [Test]
        public void MergeFootprint_Reverse_AnchorNoseAtMin()
        {
            var (f, r) = ConsistCouplingMath.MergeFootprint(0f, 100f, -1, 80f);
            Assert.That(f, Is.EqualTo(0f).Within(1e-4), "Nos (min) stały.");
            Assert.That(r, Is.EqualTo(80f).Within(1e-4));
        }

        // ── NoseCoord / IsAFront ────────────────────────────────────
        [Test]
        public void NoseCoord_ByDir()
        {
            Assert.That(ConsistCouplingMath.NoseCoord(10f, 30f, 1), Is.EqualTo(30f));
            Assert.That(ConsistCouplingMath.NoseCoord(10f, 30f, -1), Is.EqualTo(10f));
        }

        [Test]
        public void IsAFront_ForwardAndReverse()
        {
            Assert.That(ConsistCouplingMath.IsAFront(80f, 40f, 1), Is.True);
            Assert.That(ConsistCouplingMath.IsAFront(40f, 80f, 1), Is.False);
            Assert.That(ConsistCouplingMath.IsAFront(20f, 60f, -1), Is.True, "Przy reverse mniejsza współrzędna = bardziej z przodu.");
        }

        // ── OrderInMerged ───────────────────────────────────────────
        [Test]
        public void OrderInMerged_SameDir_Unchanged()
            => CollectionAssert.AreEqual(new[] { 1, 2, 3 },
                ConsistCouplingMath.OrderInMerged(new List<int> { 1, 2, 3 }, 1, 1));

        [Test]
        public void OrderInMerged_OppositeDir_Reversed()
            => CollectionAssert.AreEqual(new[] { 3, 2, 1 },
                ConsistCouplingMath.OrderInMerged(new List<int> { 1, 2, 3 }, -1, 1));

        // ── MergeVehicleOrder ───────────────────────────────────────
        [Test]
        public void MergeVehicleOrder_Chasing_FrontThenRear()
        {
            var merged = ConsistCouplingMath.MergeVehicleOrder(new List<int> { 1, 2 }, 1, new List<int> { 3, 4 }, 1, 1);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, merged);
        }

        [Test]
        public void MergeVehicleOrder_HeadOn_RearReversed()
        {
            var merged = ConsistCouplingMath.MergeVehicleOrder(new List<int> { 1, 2 }, 1, new List<int> { 3, 4 }, -1, 1);
            CollectionAssert.AreEqual(new[] { 1, 2, 4, 3 }, merged, "Tylny skład head-on odwrócony.");
        }

        // ── SplitFootprint ──────────────────────────────────────────
        [Test]
        public void SplitFootprint_Forward()
        {
            var (front, tail) = ConsistCouplingMath.SplitFootprint(0f, 100f, 1, 30f, 70f);
            Assert.That(front.front, Is.EqualTo(70f).Within(1e-4));
            Assert.That(front.rear, Is.EqualTo(100f).Within(1e-4), "Front przy nosie (max).");
            Assert.That(tail.front, Is.EqualTo(0f).Within(1e-4));
            Assert.That(tail.rear, Is.EqualTo(70f).Within(1e-4));
        }

        [Test]
        public void SplitFootprint_Reverse()
        {
            var (front, tail) = ConsistCouplingMath.SplitFootprint(0f, 100f, -1, 30f, 70f);
            Assert.That(front.front, Is.EqualTo(0f).Within(1e-4), "Front przy nosie (min).");
            Assert.That(front.rear, Is.EqualTo(30f).Within(1e-4));
            Assert.That(tail.front, Is.EqualTo(30f).Within(1e-4));
            Assert.That(tail.rear, Is.EqualTo(100f).Within(1e-4));
        }

        [Test]
        public void SplitFootprint_PartsAdjacentAndSumToParent()
        {
            var (front, tail) = ConsistCouplingMath.SplitFootprint(10f, 90f, 1, 50f, 30f);
            Assert.That(front.rear - front.front, Is.EqualTo(50f).Within(1e-4));
            Assert.That(tail.rear - tail.front, Is.EqualTo(30f).Within(1e-4));
            Assert.That(front.front, Is.EqualTo(tail.rear).Within(1e-4), "Części przyległe (styk).");
        }

        // ── FindConsistByVehicleId ──────────────────────────────────
        [Test]
        public void FindConsistByVehicleId_ExactAndNull()
        {
            var a = new FleetConsistData { name = "A", vehicleIds = new List<int> { 1, 2 } };
            var b = new FleetConsistData { name = "B", vehicleIds = new List<int> { 3, 4 } };
            var list = new List<FleetConsistData> { a, b };
            Assert.That(ConsistCouplingMath.FindConsistByVehicleId(list, 3), Is.SameAs(b));
            Assert.That(ConsistCouplingMath.FindConsistByVehicleId(list, 99), Is.Null);
        }

        // ── DedupConsistName ────────────────────────────────────────
        [Test]
        public void DedupConsistName_AddsSuffixWhenTaken()
        {
            var existing = new[] { "IC Krakowiak" };
            Assert.That(ConsistCouplingMath.DedupConsistName(existing, "IC Krakowiak"), Is.EqualTo("IC Krakowiak (2)"));
            Assert.That(ConsistCouplingMath.DedupConsistName(existing, "Nowy"), Is.EqualTo("Nowy"));
        }

        [Test]
        public void DedupConsistName_SkipsTakenSuffix()
        {
            var existing = new[] { "X", "X (2)" };
            Assert.That(ConsistCouplingMath.DedupConsistName(existing, "X"), Is.EqualTo("X (3)"));
        }
    }
}
