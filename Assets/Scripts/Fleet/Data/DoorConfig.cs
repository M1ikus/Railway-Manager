using System;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-1: Konfiguracja drzwi pojazdu. Część VehicleConfiguration.
    /// W EA drzwi są tylko wizualne — brak wpływu na czas wymiany pasażerów na peronie.
    /// </summary>
    [Serializable]
    public class DoorConfig
    {
        public DoorType type = DoorType.SwingFolding;
        public int pairsPerSegment = 2;
        public DoorPlacement placement = DoorPlacement.AtEnds;
    }

    /// <summary>
    /// M-FC-1: Typ drzwi pojazdu.
    /// <c>SwingFolding</c> — skrzydłowo-łamane (klasyczne PKP, np. Bdhpumn). Retro look.
    /// <c>SlidingPlugDoor</c> — odskokowo-przesuwne (nowoczesne FLIRT/Impuls/PesaCarbo).
    /// </summary>
    public enum DoorType { SwingFolding, SlidingPlugDoor }

    /// <summary>
    /// M-FC-1: Lokalizacja drzwi w obrębie pudła/członu.
    /// <c>AtEnds</c> — domyślnie 2 pary po końcach (klasyczny wagon).
    /// <c>OneAtMiddle</c> — 1 para w środku pudła (zaawansowane, niskie pojemności).
    /// <c>EndsAndMiddle</c> — 3 pary (końce + środek, aglomeracja).
    /// </summary>
    public enum DoorPlacement { AtEnds, OneAtMiddle, EndsAndMiddle }
}
