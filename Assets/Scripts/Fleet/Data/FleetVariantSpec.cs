using System;
using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-1: Pojedynczy wpis matrix'a wariantów. Kombinacja
    /// (memberCount, voltageConfigId) daje konkretne parametry techniczne i cenę.
    /// Dla pojedynczego SKU (np. EU07) rodzina ma 1-elementową listę wariantów.
    /// </summary>
    [Serializable]
    public class FleetVariantSpec
    {
        // ── Klucz wariantu ──
        public int memberCount = 1;            // liczba członów (1 dla loko)
        public string voltageConfigId;          // "3kV", "3kV+25kV", "multi4", "diesel"
        public string variantLabel;             // do wyświetlenia w UI ("3 człony, 3kV DC")

        // ── Parametry techniczne ──
        public int maxSpeedKmh;
        public int powerKw;
        public string wheelbase;                // np. "Bo'2'2'Bo'"
        public float lengthM;
        public float emptyMassTons;
        public float maxLoadedMassTons;
        public float brakingMassTons;
        public BrakeRegime brakeRegime;
        public int passengerSeatsBase;          // przed customizacją siedzeń
        public List<SeatCount> seatBreakdownBase = new();
        public float accelerationMps2 = 0.8f;
        public float decelerationMps2 = 0.9f;
        public int comfortClassBase = 3;

        // ── Trakcja ──
        public List<TractionType> supportedTractions = new();
        public List<string> voltages = new();   // konkretne napięcia tego wariantu

        // ── Default options (sugestie, gracz może override w VehicleConfiguration) ──
        public List<string> defaultSafetySystems = new();
        public List<string> defaultComfortFeatures = new();

        // ── Ekonomia ──
        public long basePrice;
        public int operationalCostPerKmGroszy;

        // ── AS-P1 (M11): paliwo — tylko spalinowe; 0 = default typu z FleetBalanceConstants ──
        public int fuelTankCapacityLitres;
        public int fuelConsumptionLper100km;

        // ── M7 Niezawodność ──
        public int reliabilityScore = 75;
        public float breakdownRiskFactor = 1.0f;
        public float maintenanceCostFactor = 1.0f;
        public ComponentRiskFactors componentRisk = new();
        public int inspectionIntervalKmP1 = 20000;
        public int inspectionIntervalKmP2 = 80000;
        public int inspectionIntervalKmP3 = 300000;
        public int inspectionIntervalYearsP4 = 6;
        public int inspectionIntervalYearsP5 = 20;

        // ── UX hints ──
        public string defaultPurpose = "regional"; // longDistance | regional | agglomeration
        public List<string> suggestedCategoryGroups = new();

        // ── Wymagania infrastruktury ──
        public int minPlatformLengthM;
        public List<string> requiresMaintenanceCapabilities = new();
        public bool canBePulledByDiesel;
        public bool isShuntingLocomotive;

        // ── Variant flavor (override family-level) ──
        public string variantDescription;       // np. "Krótki 2-człon dla aglomeracji"
        public string variantFactoid;           // gdy specyficzny dla wariantu
    }
}
