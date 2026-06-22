using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Fleet;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-PaxV2 Faza A: testy cennika per KLASA biletowa (TicketSystem + ClassFare). Czyste.
    /// </summary>
    public class TicketClassPricingTests
    {
        static CommercialCategory MakeCategory()
        {
            return new CommercialCategory
            {
                id = "test_ic",
                displayName = "Test IC",
                basePriceZl = 8f,        // domyslna stawka (2. klasa / fallback)
                pricePerKmZl = 0.25f,
                classFares = new List<ClassFare>
                {
                    new ClassFare { zone = SeatZoneType.SecondClassOpen, basePriceZl = 6f,  pricePerKmZl = 0.1f },
                    new ClassFare { zone = SeatZoneType.FirstClassOpen,  basePriceZl = 12f, pricePerKmZl = 0.2f },
                    new ClassFare { zone = SeatZoneType.Sleeping,        basePriceZl = 40f, pricePerKmZl = 0.5f },
                }
            };
        }

        [Test]
        public void PerClass_UsesClassSpecificFare()
        {
            var cat = MakeCategory();
            // 100 km: base + perKm×100, ×100 gr/zl
            Assert.That(TicketSystem.CalculatePriceGroszy(cat, SeatZoneType.SecondClassOpen, 100f), Is.EqualTo(1600), "2kl: (6+10)zl");
            Assert.That(TicketSystem.CalculatePriceGroszy(cat, SeatZoneType.FirstClassOpen, 100f),  Is.EqualTo(3200), "1kl: (12+20)zl");
            Assert.That(TicketSystem.CalculatePriceGroszy(cat, SeatZoneType.Sleeping, 100f),        Is.EqualTo(9000), "sypialny: (40+50)zl");
        }

        [Test]
        public void PerClass_FallsBackToDefault_WhenClassNotPriced()
        {
            var cat = MakeCategory();
            // Bicycle nie ma wpisu w classFares -> stawka domyslna kategorii (8 + 0.25×100 = 33 zl).
            Assert.That(TicketSystem.CalculatePriceGroszy(cat, SeatZoneType.Bicycle, 100f), Is.EqualTo(3300),
                "Brak per-class wpisu -> fallback do stawki domyslnej kategorii.");
        }

        [Test]
        public void PerClass_RespectsTiers()
        {
            var cat = new CommercialCategory
            {
                id = "test_tiers",
                classFares = new List<ClassFare>
                {
                    new ClassFare
                    {
                        zone = SeatZoneType.SecondClassOpen,
                        pricingTiers = new List<PricingTier>
                        {
                            new PricingTier { fromKm = 0,  toKm = 50,    priceGroszy = 400 },
                            new PricingTier { fromKm = 50, toKm = 99999, priceGroszy = 700, perKmAboveGroszy = 8 },
                        }
                    }
                }
            };
            Assert.That(TicketSystem.CalculatePriceGroszy(cat, SeatZoneType.SecondClassOpen, 30f), Is.EqualTo(400), "tier 0-50");
            Assert.That(TicketSystem.CalculatePriceGroszy(cat, SeatZoneType.SecondClassOpen, 60f), Is.EqualTo(780), "tier 50+: 700 + 10×8");
        }

        [Test]
        public void LegacyOverload_StillWorks()
        {
            var cat = MakeCategory();
            // CalculatePriceGroszy(category, km) bez klasy = stawka domyslna (8 + 0.25×100).
            Assert.That(TicketSystem.CalculatePriceGroszy(cat, 100f), Is.EqualTo(3300));
        }
    }
}
