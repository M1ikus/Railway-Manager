using System;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M7-1: Mnożniki ryzyka awarii per-komponent, specyficzne dla serii pojazdu.
    ///
    /// 1.0 = baseline (standardowe pojazdy 1990-2010).
    /// &lt;1.0 = niezawodniejsze (nowoczesne po 2010).
    /// &gt;1.0 = bardziej awaryjne (stare, wysłużone).
    ///
    /// Przykłady z fleet_catalog_ea.json:
    /// - EN57 stara: doors=3.0, engine=1.5, toilets=2.5, interior=2.0
    /// - EU160 Griffin: wszystko 0.6-0.8 (nowoczesne, niezawodne)
    /// - EU07 klasyk: mostly 1.3-1.5 (stare ale proste w naprawie)
    ///
    /// Kalibrowane w M6.5 Rebalance (post-M13).
    /// </summary>
    [Serializable]
    public class ComponentRiskFactors
    {
        public float engine = 1.0f;
        public float brake = 1.0f;
        public float doors = 1.0f;
        public float ac = 1.0f;
        public float body = 1.0f;
        public float wheels = 1.0f;
        public float electrical = 1.0f;
        public float interior = 1.0f;
        public float lights = 1.0f;
        public float toilets = 1.0f;
        public float pantograph = 1.0f;
        public float coupling = 1.0f;

        /// <summary>Zwraca neutralne mnożniki (wszystkie 1.0).</summary>
        public static ComponentRiskFactors CreateBaseline() => new();
    }
}
