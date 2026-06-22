using System;
using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-1: Rodzina pojazdów (np. FLIRT, SA, EU07, EU160 Griffin).
    /// Zawiera listę wariantów (FleetVariantSpec) — kombinacji parametryzowanych
    /// (członów × voltage). Pojedynczy model (EU07) ma 1-elementową listę wariantów.
    /// Zastępuje płaski `NewVehicleModel` dla pojazdów dostępnych w konfiguratorze.
    /// </summary>
    [Serializable]
    public class FleetFamily
    {
        public string familyId;           // np. "FLIRT", "SA_new", "EU160_Griffin", "EU07"
        public string displayName;        // np. "Stadler FLIRT", "Pesa SA13x"
        public string manufacturer;       // np. "Stadler", "Pesa", "Newag"
        public string country = "PL";     // ISO (kraj producenta — przyszłe DLC)
        public FleetVehicleType type;     // EMU/DMU/ElectricLocomotive/...

        // Lista wariantów dostępnych w konfiguratorze
        public List<FleetVariantSpec> variants = new();

        // Wspólne metadane rodziny
        public int inProductionFromYear;
        public int inProductionToYear;       // 0 = wciąż produkowany
        public int introducedToPolandYear;
        public VehicleProductionStatus status = VehicleProductionStatus.InProduction;

        public string description;
        public string historicalFactoid;
        public string factoryLocation;
    }
}
