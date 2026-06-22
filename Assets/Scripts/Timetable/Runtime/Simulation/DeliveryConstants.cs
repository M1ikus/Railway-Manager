namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M9c-D: stałe dostawy taboru (magic numbers zakazane w logice — patrz docs/conventions.md).
    /// Tuning balansowy w M-Balance.
    /// </summary>
    public static class DeliveryConstants
    {
        // ── Produkcja / odbiór ──────────────────────────────
        /// <summary>Czas produkcji nowego pojazdu [sekundy gry] zanim pojawi się w fabryce. 30 dni.</summary>
        public const long ProductionTimeSec = 30L * 24 * 3600;

        // ── Dostawa ekspresowa (płatna, abstrakcyjny transport) ──
        /// <summary>Bazowy koszt dostawy ekspresowej [PLN] niezależny od dystansu.</summary>
        public const int ExpressBaseCostZl = 2000;

        /// <summary>Koszt ekspresu [PLN] za każdy km dystansu punkt zakupu → home depot.</summary>
        public const float ExpressCostPerKmZl = 12f;

        /// <summary>Czas dostawy ekspresowej [sekundy gry] na każdy km dystansu.</summary>
        public const float ExpressTimeSecPerKm = 600f;   // 10 min gry / km

        /// <summary>Minimalny czas dostawy ekspresowej [sekundy gry] (nawet dla bliskiego punktu).</summary>
        public const long ExpressMinTimeSec = 4L * 3600;  // 4h gry

        /// <summary>Fallback dystans [km] gdy nie da się policzyć (brak pozycji) — średnia trasa krajowa.</summary>
        public const float FallbackDistanceKm = 150f;
    }
}
