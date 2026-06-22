using System;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// Fizyczna pozycja pojazdu — na mapie (trasa) lub na torze w zajezdni.
    /// </summary>
    [Serializable]
    public class VehiclePosition
    {
        public VehicleLocationKind kind;

        // ── Na mapie ─────────────────────────────────
        public double mapLat;            // szerokość geograficzna
        public double mapLon;            // długość geograficzna
        public string currentLineId;     // ID linii (np. "R1")
        public string nextStationName;   // najbliższa stacja
        public float speedKmh;           // aktualna prędkość

        // ── W zajezdni ───────────────────────────────
        public int depotId;              // ID zajezdni gracza
        public string depotTrackName;    // nazwa toru (np. "Tor 3A")
        public int depotTrackSlot;       // pozycja na torze (0 = pierwszy)

        // ── Nieistniejący (InProduction/InTransit) ────
        public string externalLocation;  // np. "Fabryka Newag, Nowy S\u0105cz"
    }

    public enum VehicleLocationKind
    {
        None,           // nieistniejący fizycznie (InProduction, InTransit, AwaitingPickup)
        OnMap,          // na trasie/stacji na mapie
        InDepot         // na torze w zajezdni gracza
    }
}
