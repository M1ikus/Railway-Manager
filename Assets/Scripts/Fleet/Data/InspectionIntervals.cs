using System;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M7-1: Interwały przeglądów P1-P5 dla konkretnej serii pojazdu.
    ///
    /// Ładowane z <c>Assets/StreamingAssets/Fleet/inspection_intervals.json</c>
    /// (keyed by seriesId). Fallback na <see cref="FleetBalanceConstants"/>.Default*
    /// gdy brak wpisu dla danej serii.
    ///
    /// Stare pojazdy (EN57, EU07) mają krótsze interwały (częstsze przeglądy),
    /// nowoczesne (FLIRT, EU160 Griffin) dłuższe.
    /// </summary>
    [Serializable]
    public class InspectionIntervals
    {
        /// <summary>P1 — co ile godzin gry (default 72h).</summary>
        public int p1LimitHours = FleetBalanceConstants.DefaultInspectionHoursP1;
        /// <summary>P2 — co ile dni gry (default 28d).</summary>
        public int p2LimitDays = FleetBalanceConstants.DefaultInspectionDaysP2;
        /// <summary>P3 — co ile km (default 250k).</summary>
        public int p3LimitKm = FleetBalanceConstants.DefaultInspectionKmP3;
        /// <summary>P4 — co ile km (default 500k).</summary>
        public int p4LimitKm = FleetBalanceConstants.DefaultInspectionKmP4;
        /// <summary>P4 — co ile lat (default 5).</summary>
        public int p4LimitYears = FleetBalanceConstants.DefaultInspectionYearsP4;
        /// <summary>P5 — co ile km (default 3M).</summary>
        public int p5LimitKm = FleetBalanceConstants.DefaultInspectionKmP5;
        /// <summary>P5 — co ile lat (default 30).</summary>
        public int p5LimitYears = FleetBalanceConstants.DefaultInspectionYearsP5;

        /// <summary>Zwraca nowy obiekt z domyślnymi wartościami (z BalanceConstants).</summary>
        public static InspectionIntervals CreateDefault() => new();
    }
}
