using System;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-PaxV2 Faza B: testy modelu celu podróży (PassengerPurposeModel). Czyste, deterministyczne
    /// (seedowany DeterministicRng). Pora → rozkład celów; cel → klasa + budżet.
    /// </summary>
    public class PassengerPurposeModelTests
    {
        [Test]
        public void Pick_RushWeekday_CommuteDominant()
        {
            var rng = new DeterministicRng(12345);
            var c = new int[4];
            for (int i = 0; i < 2000; i++) c[(int)PassengerPurposeModel.Pick(rng, 7, DayOfWeek.Monday)]++;

            Assert.That(c[(int)TripPurpose.Commute], Is.GreaterThan(c[(int)TripPurpose.Business]));
            Assert.That(c[(int)TripPurpose.Commute], Is.GreaterThan(c[(int)TripPurpose.Leisure]));
            Assert.That(c[(int)TripPurpose.Commute], Is.GreaterThan(c[(int)TripPurpose.Tourism]),
                "Szczyt pn-pt → dominują dojazdy do pracy.");
        }

        [Test]
        public void Pick_Weekend_LeisureTourismDominant()
        {
            var rng = new DeterministicRng(999);
            var c = new int[4];
            for (int i = 0; i < 2000; i++) c[(int)PassengerPurposeModel.Pick(rng, 12, DayOfWeek.Saturday)]++;

            int leisureTourism = c[(int)TripPurpose.Leisure] + c[(int)TripPurpose.Tourism];
            int commuteBusiness = c[(int)TripPurpose.Commute] + c[(int)TripPurpose.Business];
            Assert.That(leisureTourism, Is.GreaterThan(commuteBusiness),
                "Weekend → dominują wypoczynek/turystyka.");
        }

        [Test]
        public void PreferredClass_Commute_AlwaysSecond()
        {
            var rng = new DeterministicRng(7);
            for (int i = 0; i < 500; i++)
                Assert.That(PassengerPurposeModel.PreferredClass(rng, TripPurpose.Commute),
                    Is.EqualTo(SeatZoneType.SecondClassOpen), "Dojazd zawsze 2. klasa.");
        }

        [Test]
        public void PreferredClass_Business_MostlyFirst()
        {
            var rng = new DeterministicRng(7);
            int first = 0, second = 0;
            for (int i = 0; i < 2000; i++)
            {
                if (PassengerPurposeModel.PreferredClass(rng, TripPurpose.Business) == SeatZoneType.FirstClassOpen) first++;
                else second++;
            }
            Assert.That(first, Is.GreaterThan(second), "Biznes — większość 1. klasa (share 0.7).");
        }

        [Test]
        public void Willingness_BusinessExceedsCommute()
        {
            var rng = new DeterministicRng(3);
            // jitter ±25%: business [12000,20000] gr, commute [3750,6250] gr — zakresy rozłączne.
            for (int i = 0; i < 200; i++)
            {
                int biz = PassengerPurposeModel.WillingnessGroszy(rng, TripPurpose.Business);
                int com = PassengerPurposeModel.WillingnessGroszy(rng, TripPurpose.Commute);
                Assert.That(biz, Is.GreaterThan(com), "Biznes płaci wyraźnie więcej niż dojazd.");
            }
        }
    }
}
