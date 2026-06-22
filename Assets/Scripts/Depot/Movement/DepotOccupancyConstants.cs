namespace DepotSystem
{
    /// <summary>
    /// TD-031: stałe zajętości pozycyjnej toru i dojazdu do styku. Balans/tuning — nie magic numbers
    /// (konwencja projektu). Decyzje gracza/usera 2026-06-08: bufor = styk, wolny dojazd (crawl).
    /// </summary>
    public static class DepotOccupancyConstants
    {
        /// <summary>
        /// Styk: minimalny prześwit między dwiema nie-sprzęgniętymi jednostkami [m].
        /// Decyzja user "aż do styku" → mały epsilon przeciw nakładaniu z błędów float / z-fight.
        /// TD-032 (sprzęganie) domknie do zera przy faktycznym połączeniu.
        /// </summary>
        public const float ContactGapM = 0.05f;

        /// <summary>Odstęp przy pakowaniu wielu zaparkowanych składów na jeden tor [m] (czytelność).</summary>
        public const float MinParkingGapM = 0.5f;

        /// <summary>Prędkość pełzania na finiszu dojazdu do stojącej jednostki [m/s] (manewr podczepiania).</summary>
        public const float CouplingApproachSpeedMps = 1.5f;

        /// <summary>Od ilu metrów przed stojącą jednostką włącza się wolny dojazd (crawl) [m].</summary>
        public const float ApproachSlowdownDistM = 5f;
    }
}
