using System;
using System.Collections.Generic;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// M-PaxV2 Faza A: cennik per KLASA (<see cref="SeatZoneType"/>) w kategorii handlowej.
    /// Pełna granularność — każda klasa (2kl open/przedział, 1kl open/przedział, sypialny,
    /// kuszetka, manager, rower, rodzinny, wózek) może mieć własny cennik. Catering
    /// (Small/LargeCatering) nie jest klasą biletową (brak wpisu → nieobsługiwane).
    ///
    /// Cennik jak w kategorii: <see cref="pricingTiers"/> (preferowane, step pricing) lub legacy
    /// <see cref="basePriceZl"/> + <see cref="pricePerKmZl"/> × km. Brak wpisu dla danej klasy →
    /// TicketSystem fallbackuje do stawki domyślnej kategorii (2. klasa / legacy).
    /// </summary>
    [Serializable]
    public class ClassFare
    {
        public SeatZoneType zone;
        public List<PricingTier> pricingTiers = new();
        public float basePriceZl;
        public float pricePerKmZl;
    }
}
