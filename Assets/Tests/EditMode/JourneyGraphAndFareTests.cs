using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Fleet;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Economy;
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-PaxV2 Faza C.2a/b: graf osiągalności z rozkładów (JourneyGraphBuilder) + through-fare
    /// (ThroughFareCalculator). Czyste.
    /// </summary>
    public class JourneyGraphAndFareTests
    {
        // ── JourneyGraphBuilder ──────────────────────────────────────

        [Test]
        public void GraphBuild_DirectEdgesFromTimetables()
        {
            var tt1 = Tt(1, "os", (10, 0f), (20, 30000f));   // st1 -> st2, 30 km, os
            var tt2 = Tt(2, "ic", (20, 0f), (30, 40000f));   // st2 -> st3, 40 km, ic
            var map = new Dictionary<int, int> { { 10, 1 }, { 20, 2 }, { 30, 3 } };

            var g = JourneyGraphBuilder.Build(new[] { tt1, tt2 }, map);

            Assert.That(EdgeTo(g, 1, 2), Is.Not.Null, "Krawedz 1->2 z TT1.");
            Assert.That(EdgeTo(g, 1, 2).Value.distanceKm, Is.EqualTo(30f).Within(0.01f));
            Assert.That(EdgeTo(g, 1, 2).Value.commercialCategoryId, Is.EqualTo("os"));
            Assert.That(EdgeTo(g, 2, 3), Is.Not.Null, "Krawedz 2->3 z TT2.");
            Assert.That(EdgeTo(g, 2, 3).Value.commercialCategoryId, Is.EqualTo("ic"));
            Assert.That(EdgeTo(g, 1, 3), Is.Null, "Brak bezposredniej 1->3 (wymaga przesiadki).");
        }

        [Test]
        public void GraphBuild_KeepsShortestEdge_OnDuplicate()
        {
            var slow = Tt(1, "os", (10, 0f), (20, 60000f)); // 1->2, 60 km
            var fast = Tt(2, "ic", (10, 0f), (20, 25000f)); // 1->2, 25 km
            var map = new Dictionary<int, int> { { 10, 1 }, { 20, 2 } };

            var g = JourneyGraphBuilder.Build(new[] { slow, fast }, map);

            Assert.That(EdgeTo(g, 1, 2).Value.distanceKm, Is.EqualTo(25f).Within(0.01f),
                "Przy duplikacie trzyma krawedz o najkrotszym dystansie.");
        }

        // ── ThroughFareCalculator ────────────────────────────────────

        [Test]
        public void ThroughFare_BaseOnce_PerKmPerLeg()
        {
            // leg1 os (base6, perKm0.1) 30km; leg2 ic (base12, perKm0.2) 40km.
            var journey = new PassengerJourney(new List<JourneyLeg>
            {
                new JourneyLeg(1, 2, 30f, "os"),
                new JourneyLeg(2, 3, 40f, "ic"),
            });
            var cats = new Dictionary<string, CommercialCategory>
            {
                ["os"] = new CommercialCategory { id = "os", basePriceZl = 6f,  pricePerKmZl = 0.1f },
                ["ic"] = new CommercialCategory { id = "ic", basePriceZl = 12f, pricePerKmZl = 0.2f },
            };
            CommercialCategory Resolve(string id) => cats.TryGetValue(id, out var c) ? c : null;

            // base raz = max(6,12)=12 zl (przy ic/leg2). per-km: 0.1×30=3 + 0.2×40=8.
            // leg0 = 3 zl (300gr); leg1 = 8 + 12 = 20 zl (2000gr); total = 2300gr.
            int total = ThroughFareCalculator.ComputeTotalGroszy(journey, SeatZoneType.SecondClassOpen, Resolve);
            int leg0 = ThroughFareCalculator.LegContributionGroszy(journey, 0, SeatZoneType.SecondClassOpen, Resolve);
            int leg1 = ThroughFareCalculator.LegContributionGroszy(journey, 1, SeatZoneType.SecondClassOpen, Resolve);

            Assert.That(total, Is.EqualTo(2300), "base raz (najwyzszy 12) + per-km per leg.");
            Assert.That(leg0, Is.EqualTo(300), "leg1 = tylko per-km (3 zl).");
            Assert.That(leg1, Is.EqualTo(2000), "leg2 = per-km (8) + base (12) = 20 zl.");
            Assert.That(leg0 + leg1, Is.EqualTo(total), "Suma wkladow == total (spojnosc przychodu).");
        }

        // ── Helpers ──────────────────────────────────────────────────
        static TimetableObj Tt(int id, string cat, params (int node, float distM)[] stops)
        {
            var list = new List<TimetableStop>();
            foreach (var s in stops)
                list.Add(new TimetableStop { stationNodeId = s.node, distanceFromStartM = s.distM, stopType = StopType.PH });
            return new TimetableObj { id = id, commercialCategoryId = cat, stops = list };
        }

        static DirectEdge? EdgeTo(Dictionary<int, List<DirectEdge>> g, int from, int to)
        {
            if (!g.TryGetValue(from, out var list)) return null;
            foreach (var e in list) if (e.toStationId == to) return e;
            return null;
        }
    }
}
