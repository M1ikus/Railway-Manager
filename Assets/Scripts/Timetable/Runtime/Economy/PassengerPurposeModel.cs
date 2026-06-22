using System;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M-PaxV2 Faza B: cel podróży pasażera. Kolejność == indeksy w
    /// TimetableTuningConstants.PurposeWeights* (NIE zmieniać bez aktualizacji wag).
    /// </summary>
    public enum TripPurpose
    {
        Commute = 0,   // dojazd do pracy/szkoły — regularny, cenowo wrażliwy, 2. klasa
        Business = 1,  // służbowy — płaci za czas/komfort, często 1. klasa
        Leisure = 2,   // wypoczynek/odwiedziny
        Tourism = 3,   // turystyka — weekendy, dłuższe trasy
    }

    /// <summary>
    /// M-PaxV2 Faza B: wyprowadza z celu podróży (i pory) preferowaną KLASĘ i skłonność do
    /// zapłaty — zastępuje losowy portfel. Czysta, deterministyczna (DeterministicRng + stałe).
    ///
    /// Pora wpływa na rozkład celów (szczyt → dojazdy, weekend → wypoczynek/turystyka).
    /// Cel wpływa na klasę (biznes → częściej 1. klasa) i budżet (biznes płaci więcej).
    /// Dystans NIE skaluje budżetu wprost — affordability wychodzi przez cenę (dystans×taryfa).
    /// </summary>
    public static class PassengerPurposeModel
    {
        public static TripPurpose Pick(DeterministicRng rng, int hour, DayOfWeek day)
        {
            float[] w;
            if (day == DayOfWeek.Saturday || day == DayOfWeek.Sunday)
                w = TimetableTuningConstants.PurposeWeightsWeekend;
            else if (IsRush(hour))
                w = TimetableTuningConstants.PurposeWeightsRush;
            else
                w = TimetableTuningConstants.PurposeWeightsOffpeak;

            return (TripPurpose)WeightedPick(rng, w);
        }

        public static SeatZoneType PreferredClass(DeterministicRng rng, TripPurpose purpose)
        {
            float firstShare = purpose switch
            {
                TripPurpose.Business => TimetableTuningConstants.PurposeFirstClassShareBusiness,
                TripPurpose.Leisure  => TimetableTuningConstants.PurposeFirstClassShareLeisure,
                TripPurpose.Tourism  => TimetableTuningConstants.PurposeFirstClassShareTourism,
                _ => 0f, // Commute zawsze 2. klasa
            };
            return rng.Value < firstShare ? SeatZoneType.FirstClassOpen : SeatZoneType.SecondClassOpen;
        }

        public static int WillingnessGroszy(DeterministicRng rng, TripPurpose purpose)
        {
            int baseG = purpose switch
            {
                TripPurpose.Commute  => TimetableTuningConstants.PurposeWillingnessCommute,
                TripPurpose.Business => TimetableTuningConstants.PurposeWillingnessBusiness,
                TripPurpose.Leisure  => TimetableTuningConstants.PurposeWillingnessLeisure,
                TripPurpose.Tourism  => TimetableTuningConstants.PurposeWillingnessTourism,
                _ => TimetableTuningConstants.PurposeWillingnessCommute,
            };
            // jitter ± frakcja wokół bazy
            float jitter = 1f + (rng.Value * 2f - 1f) * TimetableTuningConstants.PurposeWillingnessJitter;
            return Mathf.Max(0, Mathf.RoundToInt(baseG * jitter));
        }

        static bool IsRush(int hour) => (hour >= 6 && hour < 9) || (hour >= 13 && hour < 16);

        static int WeightedPick(DeterministicRng rng, float[] weights)
        {
            if (weights == null || weights.Length == 0) return 0;
            float total = 0f;
            for (int i = 0; i < weights.Length; i++) total += weights[i];
            if (total <= 0f) return 0;

            float r = rng.Value * total;
            float acc = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                acc += weights[i];
                if (r < acc) return i;
            }
            return weights.Length - 1;
        }
    }
}
