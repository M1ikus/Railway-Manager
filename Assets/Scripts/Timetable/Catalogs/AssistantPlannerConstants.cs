namespace RailwayManager.Timetable.Assistant
{
    /// <summary>
    /// M11 AS-5: stałe plannera rozkład-od-relacji. Scoring 3-warstwowy (spec
    /// memory/tutorial_m11_design.md „Planner rozkładu (AS-5)"): W1 twarde filtry →
    /// W2 archetyp → W3 tie-breakery. Realna przestrzeń tuningu = TYLKO wagi W3
    /// per-archetyp + progi (zwężona z „4 globalnych wag" — AS-OQ2).
    ///
    /// TODO (M-Balance): wszystkie wartości poniżej do tuningu w playtestach.
    /// </summary>
    public static class AssistantPlannerConstants
    {
        // ── W2: progi archetypu relacji (km trasy) ──
        public const float AgglomerationMaxKm = 40f;
        public const float RegionalMaxKm = 120f;

        // ── W1: filtr zasięgu DMU — zasięg ≥ trasa × factor (powrót do tankowania w zajezdni) ──
        public const float DmuRangeRoundTripFactor = 2f;

        // ── W3: bonus dopasowania archetypu (suggestedCategoryGroups z katalogu) ──
        public const float ArchetypeMatchBonus = 0.25f;

        // ── W3: wagi tie-breakerów per archetyp [capacityFit, costEfficiency, speed, comfort, reliability] ──
        public static readonly float[] WeightsAgglomeration = { 0.40f, 0.30f, 0.05f, 0.05f, 0.20f };
        public static readonly float[] WeightsRegional      = { 0.30f, 0.25f, 0.15f, 0.10f, 0.20f };
        public static readonly float[] WeightsInterregional = { 0.15f, 0.15f, 0.30f, 0.25f, 0.15f };

        // ── W3: normalizacje metryk ──
        public const float SpeedNormKmh = 160f;          // EA top speed (EU160/FLIRT)
        public const float CostNormGroszyPerKm = 900f;   // górne realne op-cost pasażerskie

        // ── Warianty częstotliwości (target load factor → liczba kursów) ──
        public const float LoadFactorEconomic = 0.9f;
        public const float LoadFactorBalanced = 0.7f;
        public const float LoadFactorComfort  = 0.5f;
        public const int MinRunsPerDay = 2;              // minimum sensowne (tam i z powrotem)
        public const int MaxRunsPerDay = 32;
        public const int ServiceWindowHours = 18;        // okno kursowania ~5:00-23:00 → takt = okno/kursy
    }
}
