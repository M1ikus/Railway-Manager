using System;
using NUnit.Framework;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M6-5: testy DemandModifiers — mnożniki popytu (rush hour, weekend, sezon, święta, commute,
    /// offer frequency). Czysta deterministyczna logika, EditMode. Testuje RELACJE + progi
    /// (nie zamraża dokładnych wartości placeholder — M6.5 rebalans może je zmienić, relacje muszą zostać).
    /// </summary>
    public class DemandModifiersTests
    {
        // ── Godzina dnia ─────────────────────────────────────────────

        [Test]
        public void HourModifier_MorningRush_HighestPeak()
        {
            float rushAm = DemandModifiers.GetHourOfDayModifier(8);   // 7-9
            float rushPm = DemandModifiers.GetHourOfDayModifier(17);  // 16-18
            float midday = DemandModifiers.GetHourOfDayModifier(12);  // 10-20
            float night = DemandModifiers.GetHourOfDayModifier(3);    // noc

            Assert.That(rushAm, Is.GreaterThan(rushPm), "Poranny rush > popołudniowy.");
            Assert.That(rushPm, Is.GreaterThan(midday), "Rush > dzień.");
            Assert.That(midday, Is.GreaterThan(night), "Dzień > noc.");
        }

        [Test]
        public void HourModifier_NightIsLowest()
        {
            Assert.That(DemandModifiers.GetHourOfDayModifier(3), Is.LessThan(1f), "Noc < 1.0 (mało pax).");
            Assert.That(DemandModifiers.GetHourOfDayModifier(23), Is.LessThan(1f));
        }

        // ── Dzień tygodnia ───────────────────────────────────────────

        [Test]
        public void DayModifier_WeekdayHigherThanWeekend()
        {
            float mon = DemandModifiers.GetDayOfWeekModifier(DayOfWeek.Monday);
            float fri = DemandModifiers.GetDayOfWeekModifier(DayOfWeek.Friday);
            float sat = DemandModifiers.GetDayOfWeekModifier(DayOfWeek.Saturday);
            float sun = DemandModifiers.GetDayOfWeekModifier(DayOfWeek.Sunday);

            Assert.That(mon, Is.GreaterThan(fri), "Pn > Pt (część już na weekend).");
            Assert.That(fri, Is.GreaterThan(sat), "Pt > sobota.");
            Assert.That(sat, Is.GreaterThan(sun), "Sobota > niedziela (minimum).");
        }

        // ── Sezonowość ───────────────────────────────────────────────

        [Test]
        public void SeasonModifier_SpringFullDemand_WinterLower()
        {
            float spring = DemandModifiers.GetSeasonModifier(new DateTime(2026, 4, 15));
            float winter = DemandModifiers.GetSeasonModifier(new DateTime(2026, 1, 15));
            float summer = DemandModifiers.GetSeasonModifier(new DateTime(2026, 7, 15));

            Assert.That(spring, Is.EqualTo(1.0f), "Wiosna = pełen popyt.");
            Assert.That(winter, Is.LessThan(spring), "Zima < wiosna.");
            Assert.That(summer, Is.LessThan(spring), "Wakacje < pełen (mniejszy commute).");
        }

        // ── Święta ───────────────────────────────────────────────────

        [Test]
        public void Holiday_AllSaints_IsHighestPeak()
        {
            // 1 XI — wizyty na cmentarzach, mega ruch.
            float allSaints = DemandModifiers.GetHolidayModifier(new DateTime(2026, 11, 1));
            float ordinary = DemandModifiers.GetHolidayModifier(new DateTime(2026, 6, 15));
            Assert.That(allSaints, Is.GreaterThan(ordinary), "Wszystkich Świętych > zwykły dzień.");
            Assert.That(allSaints, Is.GreaterThan(1.5f), "1 XI to bardzo wysoki peak.");
        }

        [Test]
        public void Holiday_ChristmasDay_IsLow()
        {
            // 25 XII — wszyscy już są gdzie trzeba, cisza.
            float christmas = DemandModifiers.GetHolidayModifier(new DateTime(2026, 12, 25));
            float dayBefore = DemandModifiers.GetHolidayModifier(new DateTime(2026, 12, 23));
            Assert.That(christmas, Is.LessThan(1f), "Same święta = cisza.");
            Assert.That(dayBefore, Is.GreaterThan(christmas), "Dzień przed świętami = maks ruch > same święta.");
        }

        [Test]
        public void Holiday_OrdinaryDay_IsNeutral()
        {
            Assert.That(DemandModifiers.GetHolidayModifier(new DateTime(2026, 6, 15)), Is.EqualTo(1.0f));
        }

        // ── Commute ──────────────────────────────────────────────────

        [Test]
        public void Commute_MorningSmallToBig_IsPeak()
        {
            // Rano małe→duże (do pracy) = peak commute. fromImp=1, toImp=5 (diff=4 > 2).
            float toWork = DemandModifiers.GetCommuteModifier(1f, 5f, hour: 8, DayOfWeek.Monday);
            float antiWork = DemandModifiers.GetCommuteModifier(5f, 1f, hour: 8, DayOfWeek.Monday);
            Assert.That(toWork, Is.GreaterThan(1f), "Rano małe→duże = peak commute.");
            Assert.That(antiWork, Is.LessThan(1f), "Rano duże→małe = anti-commute.");
        }

        [Test]
        public void Commute_AfternoonBigToSmall_IsReturn()
        {
            // Popołudnie duże→małe (powroty) = peak.
            float returns = DemandModifiers.GetCommuteModifier(5f, 1f, hour: 17, DayOfWeek.Monday);
            Assert.That(returns, Is.GreaterThan(1f), "Popołudnie duże→małe = powroty.");
        }

        [Test]
        public void Commute_Weekend_AlwaysNeutral()
        {
            float sat = DemandModifiers.GetCommuteModifier(1f, 5f, hour: 8, DayOfWeek.Saturday);
            float sun = DemandModifiers.GetCommuteModifier(5f, 1f, hour: 17, DayOfWeek.Sunday);
            Assert.That(sat, Is.EqualTo(1.0f), "Weekend commute neutralny.");
            Assert.That(sun, Is.EqualTo(1.0f));
        }

        // ── Offer frequency ──────────────────────────────────────────

        [Test]
        public void OfferFrequency_ZeroRuns_NoDemand()
        {
            Assert.That(DemandModifiers.GetOfferFrequencyModifier(0), Is.EqualTo(0f),
                "Brak oferty = brak pax (demand-driven).");
        }

        [Test]
        public void OfferFrequency_MonotonicallyIncreasing()
        {
            // Więcej kursów = większy (lub równy) modyfikator — monotoniczność.
            float one = DemandModifiers.GetOfferFrequencyModifier(1);
            float three = DemandModifiers.GetOfferFrequencyModifier(3);
            float eight = DemandModifiers.GetOfferFrequencyModifier(8);
            float many = DemandModifiers.GetOfferFrequencyModifier(20);

            Assert.That(one, Is.LessThan(three));
            Assert.That(three, Is.LessThan(eight));
            Assert.That(eight, Is.LessThan(many));
            Assert.That(one, Is.GreaterThan(0f), "1 kurs dziennie > 0 (jest oferta).");
        }

        // ── Combined ─────────────────────────────────────────────────

        [Test]
        public void TimeCombined_IsProductOfAllTimeModifiers()
        {
            // Weekday południe wiosna zwykły dzień — iloczyn 4 modyfikatorów.
            var dt = new DateTime(2026, 4, 15, 12, 0, 0); // środa, 12:00, kwiecień
            float expected = DemandModifiers.GetHourOfDayModifier(12)
                           * DemandModifiers.GetDayOfWeekModifier(dt.DayOfWeek)
                           * DemandModifiers.GetSeasonModifier(dt)
                           * DemandModifiers.GetHolidayModifier(dt);
            Assert.That(DemandModifiers.GetTimeCombined(dt), Is.EqualTo(expected).Within(0.0001f),
                "GetTimeCombined = iloczyn hour×day×season×holiday.");
        }
    }
}
