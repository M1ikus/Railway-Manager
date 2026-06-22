using System;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// Stan techniczny pojedynczych komponentów pojazdu (0-100%).
    /// Używane przy serwisowaniu i obliczaniu ogólnego conditionPercent.
    /// </summary>
    [Serializable]
    public class VehicleComponents
    {
        // Podstawowe (M7 data layer)
        public float engineCondition = 100f;     // silnik / napęd (dla lok/EZT/SZT; -1 = N/A dla wagonów)
        public float brakeCondition = 100f;      // hamulce (wszędzie)
        public float doorsCondition = 100f;      // drzwi (pasażerskie)
        public float acCondition = 100f;         // klimatyzacja (jeśli zainstalowana; -1 = N/A)
        public float bodyCondition = 100f;       // pudło / nadwozie
        public float wheelsCondition = 100f;     // koła / wózki
        public float electricalCondition = 100f; // instalacja elektryczna
        public float interiorCondition = 100f;   // wnętrze (tapicerka, monitoring)

        // Rozszerzone (M-Fleet + M7)
        public float lightsCondition = 100f;     // reflektory/oświetlenie zewnętrzne + wewnętrzne
        public float toiletsCondition = 100f;    // WC (jeśli zainstalowane; -1 = N/A, np. SM42)
        public float pantographCondition = 100f; // pantograf (tylko Electric; -1 = N/A dla diesel/wagonów)
        public float couplingCondition = 100f;   // sprzęg (UIC/auto)

        /// <summary>
        /// Uśredniony stan ze WSZYSTKICH ZAINSTALOWANYCH komponentów.
        /// Wartość -1 traktowana jest jako "N/A" (komponent niezainstalowany, pomija w średniej).
        /// </summary>
        public float Average()
        {
            int count = 0;
            float sum = 0f;
            AddIfInstalled(engineCondition, ref sum, ref count);
            AddIfInstalled(brakeCondition, ref sum, ref count);
            AddIfInstalled(doorsCondition, ref sum, ref count);
            AddIfInstalled(acCondition, ref sum, ref count);
            AddIfInstalled(bodyCondition, ref sum, ref count);
            AddIfInstalled(wheelsCondition, ref sum, ref count);
            AddIfInstalled(electricalCondition, ref sum, ref count);
            AddIfInstalled(interiorCondition, ref sum, ref count);
            AddIfInstalled(lightsCondition, ref sum, ref count);
            AddIfInstalled(toiletsCondition, ref sum, ref count);
            AddIfInstalled(pantographCondition, ref sum, ref count);
            AddIfInstalled(couplingCondition, ref sum, ref count);
            return count > 0 ? sum / count : 0f;
        }

        static void AddIfInstalled(float v, ref float sum, ref int count)
        {
            if (v >= 0f) { sum += v; count++; }
        }

        /// <summary>Ustawia wszystkie komponenty na tę samą wartość (tylko zainstalowane; -1 pozostaje).</summary>
        public void SetAll(float value)
        {
            if (engineCondition >= 0f) engineCondition = value;
            if (brakeCondition >= 0f) brakeCondition = value;
            if (doorsCondition >= 0f) doorsCondition = value;
            if (acCondition >= 0f) acCondition = value;
            if (bodyCondition >= 0f) bodyCondition = value;
            if (wheelsCondition >= 0f) wheelsCondition = value;
            if (electricalCondition >= 0f) electricalCondition = value;
            if (interiorCondition >= 0f) interiorCondition = value;
            if (lightsCondition >= 0f) lightsCondition = value;
            if (toiletsCondition >= 0f) toiletsCondition = value;
            if (pantographCondition >= 0f) pantographCondition = value;
            if (couplingCondition >= 0f) couplingCondition = value;
        }

        public static VehicleComponents New(float value = 100f)
        {
            var c = new VehicleComponents();
            c.SetAll(value);
            return c;
        }
    }
}
