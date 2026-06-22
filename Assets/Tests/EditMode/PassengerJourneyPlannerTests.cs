using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-PaxV2 Faza C.1: testy planera podróży (PassengerJourneyPlanner) na grafie osiągalności.
    /// Czyste. Direct / 1 / 2 przesiadki / brak trasy / preferencja mniej przesiadek / limit 2.
    /// </summary>
    public class PassengerJourneyPlannerTests
    {
        const int A = 1, B = 2, T1 = 3, T2 = 4, T3 = 5, X = 9;

        static Dictionary<int, List<DirectEdge>> Graph(params (int from, int to, float km, string cat)[] edges)
        {
            var g = new Dictionary<int, List<DirectEdge>>();
            foreach (var e in edges)
            {
                if (!g.TryGetValue(e.from, out var list)) { list = new List<DirectEdge>(); g[e.from] = list; }
                list.Add(new DirectEdge(e.to, e.km, e.cat));
            }
            return g;
        }

        [Test]
        public void Direct_ZeroTransfers()
        {
            var g = Graph((A, B, 50f, "os"));
            var j = PassengerJourneyPlanner.FindJourney(A, B, g);

            Assert.That(j, Is.Not.Null);
            Assert.That(j.TransferCount, Is.EqualTo(0));
            Assert.That(j.legs.Count, Is.EqualTo(1));
            Assert.That(j.legs[0].boardStationId, Is.EqualTo(A));
            Assert.That(j.legs[0].alightStationId, Is.EqualTo(B));
            Assert.That(j.legs[0].distanceKm, Is.EqualTo(50f));
            Assert.That(j.legs[0].commercialCategoryId, Is.EqualTo("os"));
        }

        [Test]
        public void OneTransfer()
        {
            var g = Graph((A, T1, 30f, "os"), (T1, B, 40f, "ic"));
            var j = PassengerJourneyPlanner.FindJourney(A, B, g);

            Assert.That(j, Is.Not.Null);
            Assert.That(j.TransferCount, Is.EqualTo(1));
            Assert.That(j.legs[0].alightStationId, Is.EqualTo(T1), "1. odcinek do węzła przesiadkowego.");
            Assert.That(j.legs[1].boardStationId, Is.EqualTo(T1));
            Assert.That(j.legs[1].alightStationId, Is.EqualTo(B));
            Assert.That(j.TotalDistanceKm, Is.EqualTo(70f));
        }

        [Test]
        public void TwoTransfers()
        {
            var g = Graph((A, T1, 10f, "os"), (T1, T2, 20f, "os"), (T2, B, 30f, "os"));
            var j = PassengerJourneyPlanner.FindJourney(A, B, g);

            Assert.That(j, Is.Not.Null);
            Assert.That(j.TransferCount, Is.EqualTo(2));
            Assert.That(j.legs.Count, Is.EqualTo(3));
            Assert.That(j.legs[2].alightStationId, Is.EqualTo(B));
        }

        [Test]
        public void NoPath_ReturnsNull()
        {
            var g = Graph((A, X, 10f, "os")); // X nie prowadzi do B
            Assert.That(PassengerJourneyPlanner.FindJourney(A, B, g), Is.Null);
        }

        [Test]
        public void PrefersDirect_OverTransfer()
        {
            var g = Graph((A, B, 60f, "ic"), (A, T1, 20f, "os"), (T1, B, 45f, "os"));
            var j = PassengerJourneyPlanner.FindJourney(A, B, g);

            Assert.That(j.TransferCount, Is.EqualTo(0), "Bezpośredni preferowany nad przesiadką.");
            Assert.That(j.legs[0].commercialCategoryId, Is.EqualTo("ic"));
        }

        [Test]
        public void ExceedsTwoTransfers_ReturnsNull()
        {
            // A→T1→T2→T3→B = 3 przesiadki > limit 2.
            var g = Graph((A, T1, 10f, "os"), (T1, T2, 10f, "os"), (T2, T3, 10f, "os"), (T3, B, 10f, "os"));
            Assert.That(PassengerJourneyPlanner.FindJourney(A, B, g), Is.Null,
                "Trasa wymagajaca 3 przesiadek odrzucona (limit 2).");
        }

        [Test]
        public void SameOriginDest_ReturnsNull()
        {
            var g = Graph((A, B, 50f, "os"));
            Assert.That(PassengerJourneyPlanner.FindJourney(A, A, g), Is.Null);
        }
    }
}
