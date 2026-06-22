namespace RailwayManager.Fleet
{
    public enum FleetVehicleType
    {
        ElectricLocomotive,
        DieselLocomotive,
        EMU,            // Elektryczny Zespol Trakcyjny
        DMU,            // Autobus szynowy / spalinowy ZT
        PassengerCar
    }

    public enum FleetVehicleStatus
    {
        MovingOnMap,
        StoppedOnMap,
        StoppedInDepot,
        MovingInDepot,
        InRepair,
        OutOfService,
        InProduction,       // zamówiony nowy, w produkcji
        InTransit,          // w dostawie do zajezdni
        AwaitingPickup      // czeka na odbiór własny
    }

    /// <summary>
    /// Poziom przeglądu technicznego (P1 najlżejszy — P5 rewizja główna).
    /// </summary>
    public enum InspectionLevel { P1, P2, P3, P4, P5 }

    public enum SeatZoneType
    {
        SecondClassOpen, SecondClassCompartment,
        FirstClassOpen, FirstClassCompartment,
        Bicycle, SmallCatering, LargeCatering,
        Sleeping, Reclining, Family,
        WheelchairAccessible, ManagerCompartment
    }

    public enum ValidationSeverity { Error, Warning, Ok }

    public enum CartDeliveryMode { SelfPickup, DeliverToDepot }

    /// <summary>
    /// Nastawa hamulca wg UIC.
    /// G = towarowy (wolne napełnianie), P = osobowy, R = pospieszny,
    /// R_Mg = R + hamulec szynowy magnetyczny, R_E = R + hamulec elektrodynamiczny.
    /// </summary>
    public enum BrakeRegime { G, P, R, R_Mg, R_E }

    /// <summary>
    /// M-Fleet-1: Rodzaj trakcji. Pojazd może obsługiwać kilka (hybryda/bimode post-1.0).
    /// <c>Electric</c> — pantograf + sieć trakcyjna (Vectron, EU160, FLIRT, EN57)
    /// <c>Diesel</c> — silnik spalinowy (754, SM42, SA134-138)
    /// <c>None</c> — pojazd pasywny (wagon, ciągnięty przez lokomotywę)
    /// </summary>
    public enum TractionType { Electric, Diesel, None }

    /// <summary>
    /// M-Fleet-1: Status produkcji pojazdu. Wpływa na dostępność w konfiguratorze
    /// (nowy zakup) i generatorze rynku wtórnego.
    /// <c>InProduction</c> — dostępny jako zamówienie (Griffin, FLIRT, SA137/138)
    /// <c>Retired</c> — produkcja zakończona, tylko rynek wtórny (EU07, EN57, EP08)
    /// <c>InModernization</c> — można kupić starą + zmodernizować (EU07 → EU07A)
    /// <c>Modernized</c> — zmodernizowana wersja (EN57 Cebula/Ryba, EP07A)
    /// </summary>
    public enum VehicleProductionStatus { InProduction, Retired, InModernization, Modernized }
}
