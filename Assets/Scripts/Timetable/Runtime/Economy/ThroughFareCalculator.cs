using System;
using UnityEngine;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M-PaxV2 Faza C.2b: cena „jednego biletu na całą podróż" z przesiadkami (decyzja user'a):
    /// per-km KAŻDEGO odcinka wg jego kategorii × dystans + opłata bazowa RAZ (najwyższa wśród
    /// odcinków). Tańsze niż suma osobnych biletów (base liczony raz).
    ///
    /// Przychód dzielony per odcinek: <see cref="LegContributionGroszy"/> = per-km tego odcinka
    /// + base (najwyższy) przypisany do odcinka o najwyższym base. <see cref="ComputeTotalGroszy"/>
    /// = suma wkładów (spójność: ile płaci pasażer == ile trafia do obiegów).
    ///
    /// Czysty + deterministyczny. <paramref name="resolveCategory"/> = lookup id→CommercialCategory
    /// (produkcyjnie TimetableService; w testach stub). Używane dla podróży MULTI-leg; bezpośrednie
    /// (1 odcinek) liczy TicketSystem (tiers-aware).
    /// </summary>
    public static class ThroughFareCalculator
    {
        public static int ComputeTotalGroszy(PassengerJourney journey,
            RailwayManager.Fleet.SeatZoneType cls, Func<string, CommercialCategory> resolveCategory)
        {
            if (journey == null || journey.legs.Count == 0 || resolveCategory == null) return 0;
            int total = 0;
            for (int i = 0; i < journey.legs.Count; i++)
                total += LegContributionGroszy(journey, i, cls, resolveCategory);
            return total;
        }

        public static int LegContributionGroszy(PassengerJourney journey, int legIndex,
            RailwayManager.Fleet.SeatZoneType cls, Func<string, CommercialCategory> resolveCategory)
        {
            if (journey == null || resolveCategory == null) return 0;
            if (legIndex < 0 || legIndex >= journey.legs.Count) return 0;

            // Odcinek o najwyższej opłacie bazowej — tam doliczamy base (raz na całą podróż).
            int maxBaseLeg = 0;
            float maxBaseZl = -1f;
            for (int i = 0; i < journey.legs.Count; i++)
            {
                TicketSystem.GetClassRate(resolveCategory(journey.legs[i].commercialCategoryId), cls, out float b, out _);
                if (b > maxBaseZl) { maxBaseZl = b; maxBaseLeg = i; }
            }

            var leg = journey.legs[legIndex];
            TicketSystem.GetClassRate(resolveCategory(leg.commercialCategoryId), cls, out float _, out float perKmZl);
            float contribZl = perKmZl * leg.distanceKm + (legIndex == maxBaseLeg ? Mathf.Max(0f, maxBaseZl) : 0f);
            return Mathf.RoundToInt(contribZl * 100f);
        }
    }
}
