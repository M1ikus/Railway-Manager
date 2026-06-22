using NUnit.Framework;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M6-6 + TD-007: testy SubsidyCalculator — dotacje wojewódzkie + punctuality KPI.
    /// Czysta logika (zależności GameRules/Difficulty mają bezpieczne defaulty: wszystko ON,
    /// Normal 1.0; brak catalogu → fallback constants). EditMode.
    ///
    /// Reguły kwalifikacji: status Active, ≥MinRunsPerDay kursów, śr. cena ≤MaxAvgPrice, są pax.
    /// Punctuality: <60% on-time → 0×, 60-80% → 0.5×, >80% → 1×, brak danych → 1× (nie karze nowych).
    /// </summary>
    public class SubsidyCalculatorTests
    {
        static LineBalance Balance(int runs, long revenue, int pax, int onTime = 0, int late = 0)
            => new LineBalance
            {
                circulationId = -1, // brak voivodeship → fallback subsidyPerRun
                runsCompletedToday = runs,
                revenueGroszy = revenue,
                passengerCount = pax,
                punctualOnTimeToday = onTime,
                punctualLateToday = late
            };

        // ── PunctualityMultiplier ────────────────────────────────────

        [Test]
        public void Punctuality_NoData_FullMultiplier()
        {
            // 0 ukończonych runów → 1.0 (nie penalizujemy nowo aktywowanych obiegów).
            Assert.That(SubsidyCalculator.PunctualityMultiplier(Balance(0, 0, 0)), Is.EqualTo(1f));
        }

        [Test]
        public void Punctuality_Null_FullMultiplier()
        {
            Assert.That(SubsidyCalculator.PunctualityMultiplier(null), Is.EqualTo(1f));
        }

        [Test]
        public void Punctuality_Above80Percent_FullMultiplier()
        {
            // 9/10 on-time = 90% > 80% → 1.0.
            Assert.That(SubsidyCalculator.PunctualityMultiplier(Balance(10, 0, 0, onTime: 9, late: 1)),
                Is.EqualTo(1f));
        }

        [Test]
        public void Punctuality_Between60And80_HalfMultiplier()
        {
            // 7/10 = 70% → 0.5×.
            Assert.That(SubsidyCalculator.PunctualityMultiplier(Balance(10, 0, 0, onTime: 7, late: 3)),
                Is.EqualTo(0.5f));
        }

        [Test]
        public void Punctuality_Below60_ZeroMultiplier()
        {
            // 5/10 = 50% < 60% → 0× (gracz traci dotację za niską jakość).
            Assert.That(SubsidyCalculator.PunctualityMultiplier(Balance(10, 0, 0, onTime: 5, late: 5)),
                Is.EqualTo(0f));
        }

        // ── CalculateDailySubsidy: kwalifikacja ──────────────────────

        [Test]
        public void Subsidy_NullBalance_Zero()
        {
            Assert.That(SubsidyCalculator.CalculateDailySubsidy(null), Is.EqualTo(0));
        }

        [Test]
        public void Subsidy_TooFewRuns_Zero()
        {
            // < MinRunsPerDay (4) → 0.
            Assert.That(SubsidyCalculator.CalculateDailySubsidy(Balance(2, 10000, 100)), Is.EqualTo(0),
                "Za mało kursów → brak dotacji.");
        }

        [Test]
        public void Subsidy_NoPassengers_Zero()
        {
            Assert.That(SubsidyCalculator.CalculateDailySubsidy(Balance(10, 0, 0)), Is.EqualTo(0),
                "Brak pasażerów → brak dotacji.");
        }

        [Test]
        public void Subsidy_AvgTicketTooExpensive_Zero()
        {
            // Śr. cena = revenue/pax. 10 pax × bardzo drogi bilet > MaxAvgPrice (4000gr=40zł) → 0.
            // revenue=1_000_000gr / 10 pax = 100000gr = 1000zł/bilet >> 40zł limit.
            Assert.That(SubsidyCalculator.CalculateDailySubsidy(Balance(10, 1_000_000, 10)), Is.EqualTo(0),
                "Średnia cena biletu > limit (kategoria nie-regionalna) → brak dotacji.");
        }

        [Test]
        public void Subsidy_QualifyingRegional_ReturnsPositive()
        {
            // 10 kursów, tani bilet (20zł = 2000gr śr.), są pax → kwalifikuje się.
            // revenue = 100 pax × 2000gr = 200000gr; śr = 2000gr < 4000 limit.
            int subsidy = SubsidyCalculator.CalculateDailySubsidy(Balance(10, 200000, 100));
            Assert.That(subsidy, Is.GreaterThan(0), "Kwalifikujący się obieg regionalny → dotacja > 0.");
        }

        [Test]
        public void Subsidy_ScalesWithRunCount()
        {
            // Więcej kursów (przy zachowaniu kwalifikacji) = większa dotacja.
            int few = SubsidyCalculator.CalculateDailySubsidy(Balance(5, 100000, 50));
            int many = SubsidyCalculator.CalculateDailySubsidy(Balance(20, 400000, 200)); // śr. cena ta sama (2000gr)
            Assert.That(many, Is.GreaterThan(few), "Dotacja skaluje się z liczbą kursów.");
        }

        [Test]
        public void Subsidy_LowPunctuality_ReducesToZero()
        {
            // Kwalifikujący się obieg, ale punctuality < 60% → multiplier 0 → dotacja 0.
            var poorPunctuality = Balance(10, 200000, 100, onTime: 4, late: 6); // 40%
            Assert.That(SubsidyCalculator.CalculateDailySubsidy(poorPunctuality), Is.EqualTo(0),
                "Niska punktualność (<60%) zeruje dotację mimo kwalifikacji.");
        }

        [Test]
        public void Explain_TooFewRuns_MentionsRunCount()
        {
            string txt = SubsidyCalculator.Explain(Balance(2, 10000, 100));
            Assert.That(txt, Does.Contain("2").And.Contain("4"), "Wyjaśnienie pokazuje kursy aktualne/wymagane.");
        }
    }
}
