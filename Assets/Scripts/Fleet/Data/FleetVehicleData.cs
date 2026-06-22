using System;
using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>Pojazd posiadany przez gracza.</summary>
    [Serializable]
    public class FleetVehicleData
    {
        // ── Identyfikacja ──────────────────────────────
        public int id;                     // wewnętrzne ID w grze
        public string seriesId;            // M-Fleet-1: stable key, np. "EN57", "FLIRT_LM4268"
        public string series;               // np. "EP07" (human display)
        public string number;               // taborowy numer inwentarzowy (np. "EP07-1052")
        public string evn;                   // European Vehicle Number (12-cyfrowy, generowany)
        public string family;               // M-Fleet-1: wspólny model 3D (np. "FLIRT")
        public string country = "PL";       // M-Fleet-1
        public FleetVehicleType type;

        // ── Stan operacyjny ────────────────────────────
        public FleetVehicleStatus status;
        public string currentTask;          // np. "Linia R1: Warszawa→Kraków"
        public string assignedConsist;      // nazwa składu lub null (legacy, pre-M5 placeholder)

        /// <summary>
        /// ID obiegu (z CirculationService.Circulations) w którym pojazd obecnie pracuje.
        /// -1 = pojazd wolny (nie w żadnym obiegu). Dodane w M5 żeby runtime ruchu (M9)
        /// wiedział do jakiej sekwencji kursów ten pojazd należy.
        /// Pojazd może być w wielu obiegach jednocześnie jeśli mają rozłączne kalendarze
        /// (np. pn-pt w obiegu A, weekend w obiegu B) — w tym polu trzymamy tylko
        /// PRIMARY obieg (pierwszy dodany). Pełna lista jest po stronie CirculationService
        /// przez GetCirculationsForVehicle.
        /// </summary>
        public int assignedCirculationId = -1;

        // ── Trakcja (M-Fleet-1) ──────────────────────────
        public List<TractionType> supportedTractions = new();

        // ── Parametry techniczne ────────────────────────
        public int maxSpeedKmh;
        public int powerKw;                  // 0 dla wagonów
        public string wheelbase;             // np. "Bo'Bo'"
        public int passengerSeats;           // suma wszystkich miejsc (z seatBreakdown)
        public List<SeatCount> seatBreakdown = new();  // rozkład per typ strefy
        public int coachCount;               // M-Fleet-1: dla EMU/DMU liczba członów
        public int maxCoachesInTrain;        // M-Fleet-1: dla loko
        public float accelerationMps2 = 0.8f; // M-Fleet-1 (default z BalanceConstants)
        public float decelerationMps2 = 0.9f; // M-Fleet-1

        // ── Parametry fizyczne ──────────────────────────
        public float lengthM;                // długość całkowita [m]
        public float emptyMassTons;          // masa własna (pusty pojazd) [t]
        public float maxLoadedMassTons;      // masa z pełnym obciążeniem pasażerami [t]
        public float brakingMassTons;        // masa hamująca wg UIC [t]
        public BrakeRegime brakeRegime;      // nastawa hamulca (G/P/R/R+Mg/R+E)

        // ── Wyposażenie ─────────────────────────────────
        public List<string> safetySystemsInstalled = new();  // CA, SHP, ETCS
        public List<string> voltages = new();                // 3kV DC, 15kV AC, ...
        public List<string> comfortFeatures = new();         // klimatyzacja, Wi-Fi, gniazdka, info
        public int comfortClass = 3;                         // M-Fleet-1: 1-5
        public string paintScheme;                           // legacy free-form (M-FC-8: deprecated, używamy paintDefinition)

        // ── Paint (M-FC-1, paint editor w M-FC-7) ──
        /// <summary>Pełna definicja malowania pojazdu. Po zakupie z konfiguratora kopiowane z VehicleConfiguration.paint.
        /// Po zakupie z rynku wtórnego — resolved z FleetMarketVehicle.paintDefinitionResolved (lub generated z paintSeed).
        /// Może być modyfikowane przez paint editor w ZNTK (M-FC-9) lub w warsztacie (M-Modernization).</summary>
        public PaintDefinition paintDefinition = new();

        /// <summary>M-FC-1: Konfiguracja źródłowa (gdy pojazd kupiony z konfiguratora). Null dla używanych z rynku.
        /// Pozwala na "kup taki sam ponownie" + przyszłe modyfikacje (M-Modernization).</summary>
        public VehicleConfiguration sourceConfiguration;

        // ── Eksploatacja ────────────────────────────────
        public float mileageKm;
        public float conditionPercent;       // 0-100, ogólny
        public float cleanlinessPercent;     // 0-100, czystość
        public VehicleComponents components = new();  // stan per komponent

        /// <summary>
        /// MM-9 / MM-D14: poziom paliwa 0-100% dla DMU spalinowych i dieslowych lokomotyw
        /// (SA134/SA138/SM42/SM48/SU45/6Dg). Pojazdy elektryczne (EMU/EU07/Griffin/itp.)
        /// ignorują (energia z catenary przez pantograf).
        ///
        /// Degradacja per km dla diesli (tankowanie: <see cref="FuelStationService"/> /
        /// <see cref="WorkshopManager"/> przez fuel_pump). Brak paliwa (≤0%) → Critical
        /// breakdown z <see cref="BreakdownService.BreakdownEvent"/>.
        ///
        /// Wymaga <see cref="RequiresFuel"/> helper (sprawdza supportedTractions).
        /// Default 100% — nowy pojazd zatankowany, używane tylko gdy RequiresFuel=true.
        /// </summary>
        public float fuelLevelPercent = 100f;

        // AS-P1 (M11): realny model paliwa — bak + spalanie kopiowane z katalogu przy
        // zakupie (0 = użyj defaultu typu z FleetBalanceConstants). Matematyka: FleetFuelMath.
        public int fuelTankCapacityLitres;
        public int fuelConsumptionLper100km;

        /// <summary>MM-9 / MM-D14: czy pojazd potrzebuje tankowania (DMU spalinowe / lokomotywy diesle).</summary>
        public bool RequiresFuel
        {
            get
            {
                if (supportedTractions == null) return false;
                foreach (var t in supportedTractions)
                    if (t == TractionType.Diesel) return true;
                return false;
            }
        }

        /// <summary>
        /// MM-17: poziom wody w zbiorniku pasażerskim (toalety, umywalki). 0-100%.
        /// Dotyczy pojazdów z toaletami: EMU/DMU/PassengerCar. Lokomotywy luzem ignorują.
        ///
        /// Uzupełniane przez wodowanie (<see cref="OutdoorEquipmentJobService.ScheduleWaterService"/>)
        /// na stanowisku WaterService outdoor lub mebel water_service indoor.
        /// Brak wody (≤0%) → toalety nieczynne, comfort penalty.
        ///
        /// Default 100% (nowy pojazd ma pełne zbiorniki).
        /// </summary>
        public float waterLevelPercent = 100f;

        /// <summary>
        /// MM-17: poziom zbiornika fekaliów (WC retencyjne). 0-100%.
        /// 0 = pusty/dobry stan, 100 = pełny/wymaga opróżnienia.
        ///
        /// Opróżniany przez wodowanie razem z uzupełnieniem wody (jedno stanowisko obsługuje
        /// oba zbiorniki). Pełny zbiornik (≥90%) → toalety nieczynne, comfort penalty.
        ///
        /// Default 0% (nowy pojazd ma pusty zbiornik fekaliów).
        /// </summary>
        public float wasteTankLevelPercent = 0f;

        /// <summary>
        /// MM-17: czy pojazd ma toalety i wymaga wodowania (uzupełnienie wody + opróżnienie
        /// zbiornika fekaliów). True dla pasażerskich (EMU/DMU/PassengerCar). False dla
        /// lokomotyw luzem (ElectricLocomotive/DieselLocomotive — brak pasażerów, brak toalet).
        ///
        /// User feedback 2026-05-06: "każdy pojazd oprócz lokomotyw luzem powinien mieć
        /// zbiornik na wodę i fekalia".
        /// </summary>
        public bool RequiresWaterService
        {
            get
            {
                return type == FleetVehicleType.EMU
                    || type == FleetVehicleType.DMU
                    || type == FleetVehicleType.PassengerCar;
            }
        }

        // ── Wiek i historia ─────────────────────────────
        public int productionYear;           // rok wyprodukowania (rzeczywisty rok kalendarzowy)
        public long purchaseGameTime;        // czas w grze (sekundy) kiedy kupiono
        public List<MaintenanceRecord> history = new();  // log napraw/przeglądów/zmian

        // ── Przeglądy ───────────────────────────────────
        public InspectionSchedule inspections = new();

        // ── Pozycja fizyczna ────────────────────────────
        public VehiclePosition position = new();

        // ── Czas oczekiwania (dla InProduction/InTransit/AwaitingPickup) ──
        public long estimatedCompletionGameTime;  // czas w grze (sekundy) kiedy pojazd będzie gotowy

        /// <summary>
        /// M9c-D F7: pojazd jest w trakcie dostawy własnym rozkładem (delivery TrainRun na mapie).
        /// Persystowany — delivery run NIE jest (runtime), więc po wczytaniu save DeliveryService
        /// wykrywa osierocony stan (MovingOnMap + brak aktywnego runu) i kończy dostawę do depot.
        /// </summary>
        public bool deliveryInProgress;

        // ── Koszty ──────────────────────────────────────
        public long monthlyMaintenanceCost;   // PLN/miesiąc (aktualizowany okresowo)
        public int operationalCostPerKmGroszy; // M-Fleet-1: docelowo z catalog (zastępuje heurystykę CostCalculator w M-Fleet-4)

        // ── M7 Niezawodność (M-Fleet-1, runtime w M7-2+) ──
        public int reliabilityScore = 75;
        public float breakdownRiskFactor = 1.0f;
        public float maintenanceCostFactor = 1.0f;
        /// <summary>M7-2: Mnożniki ryzyka per-komponent — kopiowane z catalog przy zakupie.</summary>
        public ComponentRiskFactors componentRisk = new();

        /// <summary>M7-2: Ostatni mileage przy evaluacji degradacji (do obliczenia delta km per tick).</summary>
        public float lastDegradationMileageKm;

        // ── Wymagania infrastruktury ─────────────────────
        public int minPlatformLengthM;
        public List<string> requiresMaintenanceCapabilities = new();

        // ── Gameplay hints ───────────────────────────────
        public VehicleProductionStatus productionStatus = VehicleProductionStatus.Retired;
        public List<string> suggestedCategoryGroups = new();
        public bool canBePulledByDiesel;
        public bool isShuntingLocomotive;
        public string historicalFactoid;
    }
}
