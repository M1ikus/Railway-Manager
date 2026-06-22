namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-Fleet-1: Stałe związane ze schema taboru — defaulty przy ładowaniu JSON,
    /// parametry generatora rynku wtórnego, interwały przeglądów.
    ///
    /// Używane jako fallback gdy JSON nie zawiera konkretnego pola (backward compat
    /// ze starymi new_models.json / initial_market.json).
    ///
    /// Wartości placeholder — finalna kalibracja w M6.5 Rebalance (post-M13 Save/Load).
    /// </summary>
    public static class FleetBalanceConstants
    {
        // ── Default inspection intervals (P1-P5) ────────
        // M7-1: używane jako fallback gdy inspection_intervals.json nie ma wpisu dla seriesId.
        // D4 (2026-05-13): single source of truth — wcześniej duplikat w InspectionSchedule
        // (P*_LIMIT_*) usunięty.

        public const int DefaultInspectionHoursP1 = 72;         // P1: co 72h czasu gry
        public const int DefaultInspectionDaysP2 = 28;          // P2: co 28 dni
        public const int DefaultInspectionKmP3 = 250_000;       // P3: co 250k km
        public const int DefaultInspectionKmP4 = 500_000;       // P4: co 500k km
        public const int DefaultInspectionYearsP4 = 5;          // P4: co 5 lat (alternatywnie)
        public const int DefaultInspectionKmP5 = 3_000_000;     // P5: co 3M km
        public const int DefaultInspectionYearsP5 = 30;         // P5: co 30 lat

        // Legacy constants (zachowane dla kompat — NewVehicleModel używa tych nazw)
        public const int DefaultInspectionKmP1 = 20_000;        // legacy: per km P1 (nieużywane)
        public const int DefaultInspectionKmP2 = 80_000;        // legacy: per km P2 (nieużywane)

        // ── Default fizyka ──────────────────────────────

        public const float DefaultAccelerationMps2 = 0.8f;
        public const float DefaultDecelerationMps2 = 0.9f;

        // ── M7 Reliability defaults ─────────────────────

        public const int DefaultReliabilityScoreNew = 85;        // świeżo produkowane
        public const int DefaultReliabilityScoreUsed = 55;       // rynek wtórny standardowy
        public const float DefaultBreakdownRiskFactorNew = 1.0f;
        public const float DefaultBreakdownRiskFactorUsed = 1.4f;
        public const float DefaultMaintenanceCostFactorNew = 1.0f;
        public const float DefaultMaintenanceCostFactorUsed = 1.3f;

        // ── Comfort class defaults (1-5 scale) ──────────

        public const int DefaultComfortClassBasic = 2;           // EN57, EU07
        public const int DefaultComfortClassStandard = 3;        // FLIRT L-4268, SA138
        public const int DefaultComfortClassPremium = 4;         // FLIRT ED160, EU160 + wagony 1. kl

        // ── Rynek wtórny generator (M-Fleet-3) ──────────

        /// <summary>Co ile dni gry auto-refresh rynku wtórnego (remove najstarsze + add losowe).</summary>
        public const int MarketRefreshIntervalDays = 30;
        /// <summary>Ile najstarszych niekupionych pozycji usunąć przy refresh.</summary>
        public const int MarketRefreshRemoveCount = 3;
        /// <summary>Ile nowych losowych pozycji dodać przy refresh.</summary>
        public const int MarketRefreshAddCount = 5;
        /// <summary>Max pozycji na rynku (cap).</summary>
        public const int MarketMaxSize = 20;

        // ── Depreciation (M6.5 Rebalance tune) ──────────

        /// <summary>Roczna deprecjacja ceny rynku wtórnego (2% = 0.02).</summary>
        public const float DepreciationRatePerYear = 0.02f;

        // ── Default operational cost per km (fallback) ──

        /// <summary>
        /// Default koszt operacyjny [gr/km] jeśli pojazd nie ma wartości w catalog.
        /// Ostateczne wartości per-pojazd w fleet_catalog_ea.json (M-Fleet-2).
        /// </summary>
        public const int DefaultOperationalCostPerKmGroszyElectric = 250;  // 2.50 zł/km
        public const int DefaultOperationalCostPerKmGroszyDiesel = 400;    // 4.00 zł/km
        public const int DefaultOperationalCostPerKmGroszyPassive = 50;    // wagony (tylko utrzymanie)

        // ── M7-3 Breakdown rate (per komponent per sekunda) ─────
        // ~4% szansy awarii w ciągu godziny jazdy przy 50% health × 1.0 risk.
        public const float BreakdownBaseRatePerSecond = 0.00002f;

        // ── MM-9/MM-17 Outdoor equipment job durations (in-game seconds) ──
        public const long OutdoorWashDurationSec         = 30 * 60;       // 30 min
        public const long OutdoorRotateDurationSec       = 5 * 60;        // 5 min
        public const long OutdoorPitLiftMaintDurationSec = 2 * 3600;      // 2h
        public const long OutdoorRefuelDurationSec       = 15 * 60;       // 15 min
        public const long OutdoorWaterServiceDurationSec = 20 * 60;       // 20 min (MM-17)

        // ── MM-9/MM-17 Outdoor equipment job costs (groszy) ──
        public const int OutdoorWashCostGr            = 20_000;           // 200 zł
        public const int OutdoorRotateCostGr          = 5_000;            // 50 zł
        public const int OutdoorPitLiftMaintCostGr    = 200_000;          // 2000 zł
        public const int OutdoorWaterServiceCostGr    = 10_000;           // 100 zł

        // ── AS-P1 (M11): realny model paliwa DMU/spalinowozów ──
        // Defaulty typu, gdy katalog nie podaje per-seria (pole = 0). Koszt tankowania
        // FuelStation = brakujące litry × cena (zastąpiło proxy 1 zł × lengthM).
        // TODO (M-Balance): wartości baków/spalania/ceny ON do tuningu + rozstrzygnięcie
        // nakładania z komponentem paliwowym operationalCostPerKmGroszy.
        public const int DefaultFuelTankLitresDmu                   = 1200; // SA-class ~2×600 l
        public const int DefaultFuelTankLitresDieselLoco            = 3000;
        public const int DefaultFuelConsumptionDmuLper100km         = 95;
        public const int DefaultFuelConsumptionDieselLocoLper100km  = 220;
        public const int FuelPricePerLitreGroszy                    = 620;  // ~6,20 zł/l ON

        // Odsprzedaż taboru z floty — ułamek (× kondycja) ceny zakupu jaki gracz odzyskuje.
        // < 1.0 = strata na dealer margin/deprecjacji → brak arbitrażu kup→sprzedaj.
        // TODO (M-Balance): wytuningować.
        public const float ResaleValueHaircut = 0.85f;

        // ── M6 Maintenance monthly cost (per FleetVehicleType, PLN) — FALLBACK ──
        // B6 (2026-05-13): preferowana ścieżka to formuła per-pojazd z `operationalCostPerKmGroszy`
        // (catalog) × avgMonthlyMileage. Te wartości są stosowane gdy pole pojazdu = 0.
        public const long MaintenanceMonthlyBaseElectricLoco = 15_000;
        public const long MaintenanceMonthlyBaseDieselLoco   = 18_000;
        public const long MaintenanceMonthlyBaseEmu          = 12_000;
        public const long MaintenanceMonthlyBaseDmu          = 13_000;
        public const long MaintenanceMonthlyBasePassengerCar = 3_000;
        public const long MaintenanceMonthlyBaseOther        = 5_000;
        /// <summary>+1.5% kosztu utrzymania per rok wieku, cap +50%.</summary>
        public const float MaintenanceAgeMultiplierPerYear = 0.015f;
        public const float MaintenanceAgeMultiplierMax     = 1.5f;
        /// <summary>100% condition = 1.0×, 0% condition = 2.0×.</summary>
        public const float MaintenanceConditionMultiplierMax = 2.0f;
        /// <summary>
        /// B6: estymata miesięcznego przebiegu pojazdu (aktywne ~50% czasu). Używana w formule
        /// monthlyCost = avgMileagePerMonth × operationalCostPerKmGroszy / 100 × ageMult × condMult.
        /// M-Balance tune.
        /// </summary>
        public const int MaintenanceAvgMonthlyMileageKm = 5000;

        // ── MM-12 SelfPainting (Hall lvl 2+) ─────────────────
        public const int SelfPaintingMinHallLevel = 2;
        public const long SelfPaintingBaseCostPln = 50_000;               // ~30-40% ZNTK
        /// <summary>Dni malowania per Hall lvl (index = Hall lvl, 0..5). 0 = niedostępne.</summary>
        public static int GetSelfPaintingDaysForHallLvl(int hallLvl) => hallLvl switch
        {
            2 => 7,
            3 => 5,
            4 => 3,
            5 => 2,
            _ => 0,
        };

        // ── M7-2 Degradation per km (baseline, 1.0 risk, 1.0 rel multiplier) ──
        // % spadku per km. 0.00008 = 100% → 0% za 1.25M km przy baseline.

        public const float DegradeEnginePerKm     = 0.00008f;
        public const float DegradeBrakePerKm      = 0.00012f;
        public const float DegradeDoorsPerKm      = 0.00006f;
        public const float DegradeAcPerKm         = 0.00003f;
        public const float DegradeBodyPerKm       = 0.00001f;
        public const float DegradeWheelsPerKm     = 0.00020f;
        public const float DegradeElectricalPerKm = 0.00005f;
        public const float DegradeInteriorPerKm   = 0.00004f;
        public const float DegradeLightsPerKm     = 0.00008f;
        public const float DegradeToiletsPerKm    = 0.00005f;
        public const float DegradePantographPerKm = 0.00007f;
        public const float DegradeCouplingPerKm   = 0.00004f;
    }
}
