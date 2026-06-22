using System;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-2: Definicja typu wózka wagonu w konfiguratorze. Ładowane z
    /// <c>StreamingAssets/Fleet/wagon_bogies.json</c>. Wagon zawsze ma 2 wózki
    /// (cena finalna = bodyPrice + 2× bogiePricePair).
    /// </summary>
    [Serializable]
    public class WagonBogieDef
    {
        public string id;                       // np. "klockowy", "tarczowy", "tarczowy-szynowy"
        public string displayName;              // np. "Wózek klasyczny (klockowy)"
        public string description;              // tooltip

        public int maxSpeedKmh;                 // limit prędkości narzucony przez konstrukcję wózka
        public float emptyMassTonsPair;         // masa pary wózków
        public BrakeRegime brakeRegime;
        public long basePricePair;              // cena za parę wózków [zł]
    }
}
