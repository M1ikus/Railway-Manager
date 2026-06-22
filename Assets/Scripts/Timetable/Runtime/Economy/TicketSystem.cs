using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M6-3: Kalkulator cen biletów według kategorii handlowej + dystansu.
    ///
    /// Priorytet:
    /// 1. <see cref="CommercialCategory.pricingTiers"/> (step pricing — D4, preferowany)
    /// 2. Legacy: <see cref="CommercialCategory.basePriceZl"/> + <see cref="CommercialCategory.pricePerKmZl"/>
    ///    (fallback gdy tiers puste — dla starych kategorii)
    ///
    /// Per odcinek (D2/D11): każdy segment podróży ma swoją cenę. Przesiadki
    /// nie dają rabatu — agent płaci osobno za każdy pociąg.
    /// </summary>
    public static class TicketSystem
    {
        /// <summary>
        /// Cena biletu [gr] dla kategorii handlowej i dystansu [km].
        /// Zwraca 0 jeśli kategoria == null lub dystans &lt; 1km (za krótki na bilet).
        /// </summary>
        public static int CalculatePriceGroszy(CommercialCategory category, float kmDistance)
        {
            if (category == null) return 0;
            if (kmDistance < 1f) return 0;

            // Priorytet 1: step pricing z tiers
            if (category.pricingTiers != null && category.pricingTiers.Count > 0)
                return CalculateFromTiers(category.pricingTiers, kmDistance);

            // Priorytet 2: legacy base + per km (zł → gr)
            float zl = category.basePriceZl + category.pricePerKmZl * kmDistance;
            return Mathf.RoundToInt(zl * 100f);
        }

        /// <summary>
        /// Cena biletu dla TrainRun (wyszukuje kategorię z Timetable.commercialCategoryId).
        /// </summary>
        public static int CalculatePriceGroszy(TrainRun run, float kmDistance)
        {
            var tt = TimetableService.GetTimetable(run.timetableId);
            if (tt == null) return 0;
            var cat = FindCategory(tt.commercialCategoryId);
            return CalculatePriceGroszy(cat, kmDistance);
        }

        // ── M-PaxV2 Faza A: cena per KLASA biletowa (SeatZoneType) ──

        /// <summary>
        /// Cena biletu [gr] dla kategorii, konkretnej KLASY (<paramref name="zone"/>) i dystansu.
        /// Gdy kategoria ma wpis <see cref="ClassFare"/> dla tej klasy → liczy z niego (tiers lub
        /// base+perKm). Brak wpisu → fallback do stawki domyślnej (2. klasa / legacy).
        /// </summary>
        public static int CalculatePriceGroszy(CommercialCategory category, SeatZoneType zone, float kmDistance)
        {
            if (category == null) return 0;
            if (kmDistance < 1f) return 0;

            var cf = FindClassFare(category, zone);
            if (cf != null)
            {
                if (cf.pricingTiers != null && cf.pricingTiers.Count > 0)
                    return CalculateFromTiers(cf.pricingTiers, kmDistance);
                return Mathf.RoundToInt((cf.basePriceZl + cf.pricePerKmZl * kmDistance) * 100f);
            }
            // Brak per-class wpisu → stawka domyślna kategorii.
            return CalculatePriceGroszy(category, kmDistance);
        }

        /// <summary>Cena per klasa dla TrainRun (kategoria z Timetable.commercialCategoryId).</summary>
        public static int CalculatePriceGroszy(TrainRun run, SeatZoneType zone, float kmDistance)
        {
            var tt = TimetableService.GetTimetable(run.timetableId);
            if (tt == null) return 0;
            return CalculatePriceGroszy(FindCategory(tt.commercialCategoryId), zone, kmDistance);
        }

        static ClassFare FindClassFare(CommercialCategory category, SeatZoneType zone)
        {
            if (category.classFares == null) return null;
            foreach (var cf in category.classFares)
                if (cf != null && cf.zone == zone) return cf;
            return null;
        }

        /// <summary>
        /// M-PaxV2 Faza C: stawka base + per-km [zł] dla (kategoria, klasa) — do through-fare przy
        /// przesiadkach (base raz, per-km per odcinek). Z ClassFare klasy lub fallback do stawki
        /// domyślnej kategorii. UWAGA: ignoruje pricingTiers (through-fare używa base+perKm).
        /// </summary>
        public static void GetClassRate(CommercialCategory category, SeatZoneType zone,
                                        out float baseZl, out float perKmZl)
        {
            baseZl = 0f; perKmZl = 0f;
            if (category == null) return;

            var cf = FindClassFare(category, zone);
            if (cf != null) { baseZl = cf.basePriceZl; perKmZl = cf.pricePerKmZl; }
            else { baseZl = category.basePriceZl; perKmZl = category.pricePerKmZl; }
        }

        static CommercialCategory FindCategory(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var c in TimetableService.CommercialCategories)
                if (c.id == id) return c;
            return null;
        }

        static int CalculateFromTiers(List<PricingTier> tiers, float kmDistance)
        {
            // Znajdź tier pokrywający dystans
            foreach (var tier in tiers)
            {
                if (kmDistance >= tier.fromKm && kmDistance < tier.toKm)
                {
                    int price = tier.priceGroszy;
                    if (tier.perKmAboveGroszy > 0)
                    {
                        int kmAbove = Mathf.Max(0, Mathf.FloorToInt(kmDistance) - tier.fromKm);
                        price += kmAbove * tier.perKmAboveGroszy;
                    }
                    return price;
                }
            }

            // Fallback: ostatni tier (gdy dystans przekroczył wszystkie)
            if (tiers.Count > 0)
            {
                var last = tiers[tiers.Count - 1];
                int price = last.priceGroszy;
                if (last.perKmAboveGroszy > 0)
                {
                    int kmAbove = Mathf.Max(0, Mathf.FloorToInt(kmDistance) - last.fromKm);
                    price += kmAbove * last.perKmAboveGroszy;
                }
                return price;
            }

            return 0;
        }
    }
}
