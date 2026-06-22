using UnityEngine;
using RailwayManager.Core.GameRules;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M7-2: Serwis degradacji komponentów pojazdu per km jazdy.
    ///
    /// Wywoływany przez <c>TrainRunSimulator.UpdateVisualPosition</c> (M9 hook) —
    /// w tym samym miejscu gdzie obliczane są koszty operacyjne per delta km.
    ///
    /// Każdy komponent zużywa się innym tempem. Tempo pomnożone przez:
    /// - <see cref="FleetVehicleData.componentRisk"/> — per-seria multiplikator
    /// - <c>(2 - reliabilityScore/100)</c> — niższy reliability = szybsze zużycie
    ///
    /// Aktualizuje też <see cref="FleetVehicleData.mileageKm"/> i
    /// <see cref="FleetVehicleData.conditionPercent"/> (agregat z components).
    /// </summary>
    public static class DegradationService
    {
        // Tempo degradacji per km — patrz FleetBalanceConstants.Degrade*PerKm.

        /// <summary>
        /// Stosuje degradację per-komponent dla consist'u (lista vehicleIds) po przejechaniu deltaKm.
        /// </summary>
        public static void ApplyDegradation(System.Collections.Generic.List<int> vehicleIds, float deltaKm)
        {
            if (vehicleIds == null || vehicleIds.Count == 0 || deltaKm <= 0f) return;

            // MB-1 Phase B: GameRule check — gracz wybral casual mode bez degradacji komponentow.
            // AS-P1: spalanie paliwa NIE podlega tej regule (paliwo to nie zużycie komponentów) —
            // dlatego liczone per pojazd PRZED gate'em, w tym samym per-km hooku.
            bool decayEnabled = GameRulesService.IsEnabled(GameRule.MaintenanceComponentDecay);

            foreach (var v in FleetService.OwnedVehicles)
            {
                if (!vehicleIds.Contains(v.id)) continue;
                FleetFuelMath.ConsumeForDistance(v, deltaKm);
                if (decayEnabled) ApplyToVehicle(v, deltaKm);
            }
        }

        static void ApplyToVehicle(FleetVehicleData v, float deltaKm)
        {
            var risks = v.componentRisk ?? new ComponentRiskFactors();
            // Reliability factor: 0-100 → 2.0 (rel 0) do 1.0 (rel 100)
            float relFactor = 2f - Mathf.Clamp(v.reliabilityScore, 0, 100) / 100f;

            var c = v.components;
            if (c == null)
            {
                c = VehicleComponents.New(100f);
                v.components = c;
            }

            // BUG-053: usunięty mnożnik × 100f który powodował 100× za szybką degradację.
            // Stale są w jednostkach % per km (np. 0.00008 = 0.00008% per km).
            // 100% → 0%: engine 1.25M km / wheels 500k km / body 10M km — sensible real-world.
            // Wcześniej z × 100f: engine 12.5k km / wheels 5k km / body 100k km — 100× za szybko.
            if (c.engineCondition >= 0f)
                c.engineCondition = Mathf.Max(0f, c.engineCondition - deltaKm * FleetBalanceConstants.DegradeEnginePerKm * risks.engine * relFactor);
            if (c.brakeCondition >= 0f)
                c.brakeCondition = Mathf.Max(0f, c.brakeCondition - deltaKm * FleetBalanceConstants.DegradeBrakePerKm * risks.brake * relFactor);
            if (c.doorsCondition >= 0f)
                c.doorsCondition = Mathf.Max(0f, c.doorsCondition - deltaKm * FleetBalanceConstants.DegradeDoorsPerKm * risks.doors * relFactor);
            if (c.acCondition >= 0f)
                c.acCondition = Mathf.Max(0f, c.acCondition - deltaKm * FleetBalanceConstants.DegradeAcPerKm * risks.ac * relFactor);
            if (c.bodyCondition >= 0f)
                c.bodyCondition = Mathf.Max(0f, c.bodyCondition - deltaKm * FleetBalanceConstants.DegradeBodyPerKm * risks.body * relFactor);
            if (c.wheelsCondition >= 0f)
                c.wheelsCondition = Mathf.Max(0f, c.wheelsCondition - deltaKm * FleetBalanceConstants.DegradeWheelsPerKm * risks.wheels * relFactor);
            if (c.electricalCondition >= 0f)
                c.electricalCondition = Mathf.Max(0f, c.electricalCondition - deltaKm * FleetBalanceConstants.DegradeElectricalPerKm * risks.electrical * relFactor);
            if (c.interiorCondition >= 0f)
                c.interiorCondition = Mathf.Max(0f, c.interiorCondition - deltaKm * FleetBalanceConstants.DegradeInteriorPerKm * risks.interior * relFactor);
            if (c.lightsCondition >= 0f)
                c.lightsCondition = Mathf.Max(0f, c.lightsCondition - deltaKm * FleetBalanceConstants.DegradeLightsPerKm * risks.lights * relFactor);
            if (c.toiletsCondition >= 0f)
                c.toiletsCondition = Mathf.Max(0f, c.toiletsCondition - deltaKm * FleetBalanceConstants.DegradeToiletsPerKm * risks.toilets * relFactor);
            if (c.pantographCondition >= 0f)
                c.pantographCondition = Mathf.Max(0f, c.pantographCondition - deltaKm * FleetBalanceConstants.DegradePantographPerKm * risks.pantograph * relFactor);
            if (c.couplingCondition >= 0f)
                c.couplingCondition = Mathf.Max(0f, c.couplingCondition - deltaKm * FleetBalanceConstants.DegradeCouplingPerKm * risks.coupling * relFactor);

            // Update aggregaty
            v.mileageKm += deltaKm;
            v.conditionPercent = c.Average();
            v.lastDegradationMileageKm = v.mileageKm;
        }
    }
}
