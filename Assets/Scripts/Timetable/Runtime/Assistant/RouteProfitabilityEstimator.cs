using UnityEngine;
using RailwayManager.Fleet;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Timetable.Assistant
{
    /// <summary>Szacunek dzienny opłacalności relacji — breakdown do preview plannera.</summary>
    public struct ProfitEstimate
    {
        public int ticketPriceGroszy;       // bilet 2. klasy na pełną relację
        public int dailyPaxServed;          // min(popyt, pojemność dzienna kursów)
        public long dailyRevenueGroszy;
        public long dailyTractionCostGroszy;   // op-cost pojazdu × km × kursy
        public long dailyTrackAccessGroszy;    // opłata PLK-like × km × kursy
        public long DailyNetGroszy => dailyRevenueGroszy - dailyTractionCostGroszy - dailyTrackAccessGroszy;

        /// <summary>Spełnia warunki dotacji wojewódzkiej (≥MinRunsPerDay kursów, bilet ≤ MaxAvgPrice).
        /// Kwota zależy od województwa/punktualności (runtime) — preview pokazuje tylko kwalifikację.</summary>
        public bool subsidyEligible;
    }

    /// <summary>
    /// M11 AS-5a: estymator opłacalności relacji PRZED uruchomieniem kursów — agregacja
    /// istniejących API (TicketSystem ceny / track access z EconomyConstants / warunki
    /// dotacji z SubsidyCalculator), zero nowego modelu ekonomii.
    ///
    /// Świadome uproszczenia MVP (preview, nie księgowość):
    /// - przychód liczony stawką 2. klasy end-to-end (bez przesiadek/klas premium),
    /// - pax obcięte do dziennej pojemności kursów (nie sprzedajemy ponad miejsca),
    /// - bez opłat postojowych per stacja (wymagają obiektów RailwayStation ze sceny)
    ///   i bez kosztów załogi (PayrollService liczy per pracownik, nie per kurs).
    /// </summary>
    public static class RouteProfitabilityEstimator
    {
        public static ProfitEstimate Estimate(in RelationFacts facts, VehicleCandidate candidate,
            int runsPerDay, CommercialCategory category)
        {
            var result = new ProfitEstimate();
            if (candidate == null || category == null || runsPerDay <= 0 || facts.routeLengthKm <= 0f)
                return result;

            result.ticketPriceGroszy = TicketSystem.CalculatePriceGroszy(
                category, SeatZoneType.SecondClassOpen, facts.routeLengthKm);

            int dailyCapacity = runsPerDay * Mathf.Max(0, candidate.seats);
            result.dailyPaxServed = Mathf.Min(Mathf.RoundToInt(facts.estimatedDailyDemand), dailyCapacity);

            result.dailyRevenueGroszy = (long)result.dailyPaxServed * result.ticketPriceGroszy;
            result.dailyTractionCostGroszy =
                (long)Mathf.RoundToInt(runsPerDay * facts.routeLengthKm * candidate.operationalCostPerKmGroszy);
            result.dailyTrackAccessGroszy =
                (long)Mathf.RoundToInt(runsPerDay * facts.routeLengthKm * EconomyConstants.TuiPasazerskiSredniaGroszy);

            result.subsidyEligible = runsPerDay >= SubsidyCalculator.MinRunsPerDay
                                     && result.ticketPriceGroszy <= SubsidyCalculator.MaxAvgPriceGroszy;
            return result;
        }
    }
}
