using UnityEngine;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M11 AS-P1: czysta matematyka realnego modelu paliwa (EditMode-testowalna — wzór
    /// TrackOccupancyMath). Do tej pory <c>fuelLevelPercent</c> było martwą mechaniką:
    /// nic go nie obniżało, Refuel ustawiał 100%, a koszt tankowania był proxy z długości
    /// pojazdu. AS-P1 wprowadza: spalanie per km (hook w DegradationService), koszt
    /// tankowania = brakujące litry × cena, zasięg dla filtra W1 plannera (AS-5).
    ///
    /// Per-seria dane z katalogu (fuelTankCapacityLitres/fuelConsumptionLper100km);
    /// 0 = fallback na default typu z FleetBalanceConstants. Pojazdy bez paliwa
    /// (RequiresFuel=false: elektryczne, wagony) → wszystkie metody no-op/0.
    /// </summary>
    public static class FleetFuelMath
    {
        /// <summary>Efektywna pojemność baku [l] — katalog albo default typu. 0 dla nie-spalinowych.</summary>
        public static float EffectiveTankLitres(FleetVehicleData v)
        {
            if (v == null || !v.RequiresFuel) return 0f;
            if (v.fuelTankCapacityLitres > 0) return v.fuelTankCapacityLitres;
            return v.type == FleetVehicleType.DieselLocomotive
                ? FleetBalanceConstants.DefaultFuelTankLitresDieselLoco
                : FleetBalanceConstants.DefaultFuelTankLitresDmu;
        }

        /// <summary>Efektywne spalanie [l/100km] — katalog albo default typu. 0 dla nie-spalinowych.</summary>
        public static float EffectiveConsumptionLper100km(FleetVehicleData v)
        {
            if (v == null || !v.RequiresFuel) return 0f;
            if (v.fuelConsumptionLper100km > 0) return v.fuelConsumptionLper100km;
            return v.type == FleetVehicleType.DieselLocomotive
                ? FleetBalanceConstants.DefaultFuelConsumptionDieselLocoLper100km
                : FleetBalanceConstants.DefaultFuelConsumptionDmuLper100km;
        }

        /// <summary>Spadek poziomu [punkty % per km].</summary>
        public static float PercentPerKm(FleetVehicleData v)
        {
            float tank = EffectiveTankLitres(v);
            if (tank <= 0f) return 0f;
            float litresPerKm = EffectiveConsumptionLper100km(v) / 100f;
            return litresPerKm / tank * 100f;
        }

        /// <summary>
        /// Spalanie za przejechany dystans — mutuje <c>fuelLevelPercent</c> (floor 0; pusty bak
        /// NIE unieruchamia w EA — konsekwencja gameplay → M-Balance/AS-6). No-op dla nie-spalinowych.
        /// </summary>
        public static void ConsumeForDistance(FleetVehicleData v, float deltaKm)
        {
            if (v == null || deltaKm <= 0f || !v.RequiresFuel) return;
            v.fuelLevelPercent = Mathf.Max(0f, v.fuelLevelPercent - PercentPerKm(v) * deltaKm);
        }

        /// <summary>Zasięg na pełnym baku [km] (filtr W1 plannera AS-5). 0 dla nie-spalinowych.</summary>
        public static float RangeKmOnFullTank(FleetVehicleData v)
        {
            float consPerKm = EffectiveConsumptionLper100km(v) / 100f;
            if (consPerKm <= 0f) return 0f;
            return EffectiveTankLitres(v) / consPerKm;
        }

        /// <summary>Ile litrów brakuje do pełna.</summary>
        public static float LitresMissing(FleetVehicleData v)
        {
            float tank = EffectiveTankLitres(v);
            if (tank <= 0f) return 0f;
            float pct = Mathf.Clamp(v.fuelLevelPercent, 0f, 100f);
            return tank * (1f - pct / 100f);
        }

        /// <summary>Koszt dotankowania do pełna [groszy] = brakujące litry × cena.</summary>
        public static int RefuelCostGroszy(FleetVehicleData v)
        {
            return Mathf.RoundToInt(LitresMissing(v) * FleetBalanceConstants.FuelPricePerLitreGroszy);
        }
    }
}
