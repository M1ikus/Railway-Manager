using System;
using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-1: Konkretna konfiguracja pojazdu wybrana przez gracza w konfiguratorze.
    /// Wkładana do koszyka jako CartItem (z możliwością regulowania ilości).
    /// Po zakupie kopiowana do FleetVehicleData.sourceConfiguration jako referencja.
    ///
    /// configId = hash zawartości — identyfikuje "tę samą konfigurację" (gracz może
    /// zamówić 4× ten sam FLIRT 3-człon z identycznym paint'em).
    /// </summary>
    [Serializable]
    public class VehicleConfiguration
    {
        public string configId;                 // hash z zawartości
        public string familyId;                 // FK → FleetFamily.familyId
        public string variantKey;               // np. "FLIRT|3|3kV+25kV" — wybór z matrix'a

        // ── Wagon-specific (gdy type == PassengerCar) ──
        public string bodyTypeId;               // "UIC-X-24.5m" / "UIC-Z-26.4m"
        public string bogieTypeId;              // "klockowy" / "tarczowy" / "tarczowy-szynowy"
        public DoorConfig doorConfig = new();
        public List<SeatZoneSlot> interiorMix = new(); // miks stref wzdłuż długości

        // ── EZT/SZT-specific ──
        public PantographConfig pantographConfig = new();

        // ── Loko-specific ──
        public List<string> safetySystemsSelected = new(); // "ETCS L1", "ETCS L2", "GSM-R"
        public bool etcsL2 = false;
        public bool gsmR = true;

        // ── Wszystkie typy ──
        public List<string> comfortFeaturesSelected = new(); // klima, Wi-Fi, gniazdka, ...
        public PaintDefinition paint = new();

        // ── Wyliczane runtime (cache, recalc przy zmianie konfiguracji) ──
        public long calculatedPrice;            // suma: variantSpec.basePrice + opcje + paint
        public int calculatedSeats;             // suma z interiorMix lub variantSpec.passengerSeatsBase × członów
        public int calculatedComfortClass;      // 1-5, wyliczane z miksu stref + comfortFeatures
    }
}
