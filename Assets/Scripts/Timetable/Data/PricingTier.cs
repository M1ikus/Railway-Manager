using System;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// M6-3: Pojedynczy próg cennika step pricing dla kategorii handlowej.
    ///
    /// Przykład cennika dla kategorii "Osobowy":
    /// <code>
    /// [ new PricingTier { fromKm = 0,   toKm = 25,    priceGroszy = 400 },    // 0-25km = 4zł
    ///   new PricingTier { fromKm = 25,  toKm = 50,    priceGroszy = 700 },    // 25-50km = 7zł
    ///   new PricingTier { fromKm = 50,  toKm = 100,   priceGroszy = 1100 },   // 50-100km = 11zł
    ///   new PricingTier { fromKm = 100, toKm = 99999, priceGroszy = 1500,
    ///                     perKmAboveGroszy = 8 } ]                             // >100km = 15zł + 8gr/km
    /// </code>
    ///
    /// Pasażer jadący 150km w tej kategorii: 1500 + 50×8 = 1900gr = 19zł.
    /// </summary>
    [Serializable]
    public class PricingTier
    {
        /// <summary>Początek przedziału kilometrowego (inclusive).</summary>
        public int fromKm;

        /// <summary>Koniec przedziału kilometrowego (exclusive). Ostatni tier: duża wartość (np. 99999).</summary>
        public int toKm;

        /// <summary>Cena fixed w tym przedziale [gr].</summary>
        public int priceGroszy;

        /// <summary>
        /// Dodatek za każdy km POWYŻEJ <see cref="fromKm"/> w tym przedziale [gr].
        /// Zwykle 0 dla krótkich tier'ów (flat rate), &gt;0 dla ostatniego (linear above).
        /// </summary>
        public int perKmAboveGroszy;
    }
}
