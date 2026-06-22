using System;
using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>Model pojazdu dostępny w konfiguratorze nowego taboru.</summary>
    [Serializable]
    public class NewVehicleModel
    {
        // ── Identyfikacja ────────────────────────────────
        public string seriesId;              // stable key, np. "FLIRT_LM4268", "EU160"
        public string modelName;             // display name, np. "Newag Griffin EU160"
        public string manufacturer;
        public string country = "PL";        // ISO code (PL default, CH dla Stadler, CZ dla Brejlovec)
        public string family;                // np. "FLIRT", "EU07_family" — wspólny model 3D w M-Models
        public FleetVehicleType type;

        // ── Trakcja (M-Fleet-1) ──────────────────────────
        public List<TractionType> supportedTractions = new() { TractionType.Electric };
        public List<string> voltages = new();            // "3kV DC", "15kV AC 16.7Hz", "25kV AC 50Hz"
        public List<string> safetySystemsInstalled = new(); // "CA", "SHP", "ETCS L1", "GSM-R"
        public List<string> comfortFeatures = new();     // "Klimatyzacja", "Wi-Fi", "Toalety"...
        public int comfortClass = 3;                     // 1-5

        // ── Parametry techniczne ─────────────────────────
        public int maxSpeedKmh;
        public int powerKw;
        public string wheelbase;
        public int passengerSeats;           // suma (denorm)
        public List<SeatCount> seatBreakdown = new();
        public int coachCount;
        public int maxCoachesInTrain;        // dla loko: ile wagonów może pociągnąć
        public float lengthM;
        public float emptyMassTons;
        public float maxLoadedMassTons;
        public float brakingMassTons;
        public BrakeRegime brakeRegime;
        public float accelerationMps2 = 0.8f;
        public float decelerationMps2 = 0.9f;

        // ── Ekonomia ─────────────────────────────────────
        public long basePrice;                   // new z konfiguratora [zł]
        public int operationalCostPerKmGroszy;   // M6-4 — zastępuje heurystykę CostCalculator

        // AS-P1 (M11): paliwo (tylko spalinowe; 0 = default typu z FleetBalanceConstants)
        public int fuelTankCapacityLitres;
        public int fuelConsumptionLper100km;

        // ── M7 Niezawodność (M-Fleet-1 — pola, runtime M7-2+) ──
        public int reliabilityScore = 75;        // 0-100 (nowoczesne wyższe)
        public float breakdownRiskFactor = 1.0f; // 1.0 = default
        public float maintenanceCostFactor = 1.0f;
        /// <summary>M7-2: Mnożniki ryzyka per-komponent (EN57 drzwi 3.0, EU160 wszystko 0.7).</summary>
        public ComponentRiskFactors componentRisk = new();
        public int inspectionIntervalKmP1 = 20000;
        public int inspectionIntervalKmP2 = 80000;
        public int inspectionIntervalKmP3 = 300000;
        public int inspectionIntervalYearsP4 = 6;
        public int inspectionIntervalYearsP5 = 20;

        // ── Historia ─────────────────────────────────────
        public int inProductionFromYear;
        public int inProductionToYear;           // 0 = wciąż produkowany
        public int introducedToPolandYear;
        public VehicleProductionStatus status = VehicleProductionStatus.InProduction;

        // ── Wymagania infrastruktury ─────────────────────
        public int minPlatformLengthM;
        public List<string> requiresMaintenanceCapabilities = new(); // "ElectricWorkshop", "DieselWorkshop", "UndergroundInspection", "WheelLathe"

        // ── Gameplay hints ───────────────────────────────
        public List<string> suggestedCategoryGroups = new(); // nazwy IrjGroup enum (np. "RegionalLocal", "RegionalFast")
        public bool canBePulledByDiesel;         // EMU może być ciągnięty przez SM42
        public bool isShuntingLocomotive;        // SM42 flag
        public string historicalFactoid;         // flavor text dla UI

        // ── Metadata ─────────────────────────────────────
        public string description;
        public string factoryLocation;
    }
}
