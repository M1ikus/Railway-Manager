using System;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-2: Definicja typu pudła wagonu w konfiguratorze. Ładowane z
    /// <c>StreamingAssets/Fleet/wagon_bodies.json</c>. Przykłady: UIC-X (24.5m,
    /// klasyczny PKP), UIC-Z (26.4m, międzynarodowy).
    /// </summary>
    [Serializable]
    public class WagonBodyDef
    {
        public string id;                       // np. "UIC-X-24.5m"
        public string displayName;              // np. "Pudło krótkie 24.5m"
        public string description;              // tooltip

        public float lengthM;
        public float emptyMassTons;             // pudło bez wózków
        public int maxSpeedKmhCap;              // limit narzucony przez konstrukcję pudła (160/200)
        public long basePrice;                  // cena pudła [zł]

        public string defaultDoorType;          // "SwingFolding" / "SlidingPlugDoor" (preferred)
    }
}
