using System;
using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>Pojazd dostępny na rynku wtórnym.</summary>
    [Serializable]
    public class FleetMarketVehicle
    {
        // ── Identyfikacja ────────────────────────────────
        public int id;
        public string seriesId;             // M-Fleet-1: stable key, np. "EN57", "EU07"
        public string series;               // legacy — human-readable "EN57", "EU07-085"
        public string number;               // taborowy numer (EP07-1052)
        public string evn;                  // European Vehicle Number
        public string family;               // M-Fleet-1: wspólny model 3D (np. "EN57_family")
        public string country = "PL";
        public FleetVehicleType type;

        public int productionYear;          // rok produkcji (rzeczywisty)
        public float mileageKm;
        public float conditionPercent;
        public float cleanlinessPercent;    // czystość 0-100
        public int passengerSeats;       // suma wszystkich miejsc (denormalizowane z seatBreakdown)

        // Rozklad miejsc per typ strefy (dla pojazdow pasazerskich)
        public List<SeatCount> seatBreakdown;

        // Harmonogram przegladow (z mozliwoscia zdefiniowania hintow w JSON)
        public InspectionSchedule inspections = new();
        // Relatywne hinty uzywane przy ladowaniu JSON (kompatybilnosc + czytelnosc)
        public float ins_hoursSinceP1;
        public float ins_daysSinceP2;
        public float ins_kmSinceP3;
        public float ins_kmSinceP4;
        public float ins_yearsSinceP4;
        public float ins_kmSinceP5;
        public float ins_yearsSinceP5;

        // ── Trakcja (M-Fleet-1) ──────────────────────────
        public List<TractionType> supportedTractions = new();

        // Parametry techniczne
        public int maxSpeedKmh;
        public int powerKw;
        public string wheelbase;
        public int coachCount;              // M-Fleet-1: dla EMU/DMU
        public int maxCoachesInTrain;       // M-Fleet-1: dla loko
        public float accelerationMps2 = 0.8f;
        public float decelerationMps2 = 0.9f;

        // Parametry fizyczne
        public float lengthM;                // długość całkowita [m]
        public float emptyMassTons;          // masa własna [t]
        public float maxLoadedMassTons;      // masa z pełnym obciążeniem [t]
        public float brakingMassTons;        // masa hamująca wg UIC [t]
        public BrakeRegime brakeRegime;      // nastawa hamulca (G/P/R/R+Mg/R+E)

        // Ekonomia
        public long price;              // cena w PLN
        public string location;         // miejsce odbioru
        public int operationalCostPerKmGroszy;  // M-Fleet-1: docelowo z catalog

        // AS-P1 (M11): paliwo (tylko spalinowe; 0 = default typu z FleetBalanceConstants)
        public int fuelTankCapacityLitres;
        public int fuelConsumptionLper100km;

        // M-Fleet-3: market refresh tracking
        public long addedGameTime;      // czas gry (sec) kiedy dodany do rynku (0 = seed)

        // Wyposażenie
        public List<string> safetySystemsInstalled;
        public List<string> voltages;
        public List<string> comfortFeatures;
        public int comfortClass = 3;             // M-Fleet-1: 1-5
        public string paintScheme;               // legacy free-form (M-FC-8: deprecated, używamy paintDefinitionResolved)

        // ── Paint (M-FC-1, generator livery w M-FC-8) ──
        /// <summary>Deterministic seed do generatora livery. Jeśli paintDefinitionResolved == null,
        /// system generuje na fly z paintSeed + palety + presetów. Po zakupie resolved kopiowany do FleetVehicleData.</summary>
        public int paintSeed;
        public PaintDefinition paintDefinitionResolved; // nullable — może być generowane on-the-fly

        /// <summary>M-FC-8: Lazy resolve livery z paintSeed. Cache'uje w paintDefinitionResolved.</summary>
        public PaintDefinition GetOrResolvePaint()
        {
            if (paintDefinitionResolved != null) return paintDefinitionResolved;
            int seed = paintSeed != 0 ? paintSeed : MarketLiveryGenerator.FallbackSeedForVehicle(this);
            paintDefinitionResolved = MarketLiveryGenerator.Generate(seed, type, coachCount > 0 ? coachCount : 1);
            return paintDefinitionResolved;
        }

        // ── M7 Niezawodność (M-Fleet-1 — pola, runtime M7-2+) ──
        public int reliabilityScore = 50;        // starsze/używane = niższe
        public float breakdownRiskFactor = 1.2f;
        public float maintenanceCostFactor = 1.3f;
        /// <summary>M7-2: Mnożniki ryzyka per-komponent.</summary>
        public ComponentRiskFactors componentRisk = new();

        // ── Wymagania infrastruktury ─────────────────────
        public int minPlatformLengthM;
        public List<string> requiresMaintenanceCapabilities = new();

        // ── Historia i gameplay ──────────────────────────
        public VehicleProductionStatus status = VehicleProductionStatus.Retired;
        public List<string> suggestedCategoryGroups = new();
        public bool canBePulledByDiesel;
        public bool isShuntingLocomotive;
        public string historicalFactoid;
    }
}
