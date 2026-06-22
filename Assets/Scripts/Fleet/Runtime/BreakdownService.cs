using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;
using RailwayManager.Core.GameRules;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// Typ komponentu który się zepsuł (enum → index w VehicleComponents).
    /// </summary>
    public enum ComponentType
    {
        Engine = 0,
        Brake = 1,
        Doors = 2,
        AC = 3,
        Body = 4,
        Wheels = 5,
        Electrical = 6,
        Interior = 7,
        Lights = 8,
        Toilets = 9,
        Pantograph = 10,
        Coupling = 11
    }

    /// <summary>
    /// Jak krytyczna jest awaria (wpływa na flow M7-3).
    /// </summary>
    public enum BreakdownSeverity
    {
        /// <summary>Pociąg staje na szlaku — engine, pantograph.</summary>
        Critical,
        /// <summary>Pociąg jedzie do najbliższej stacji i kończy — brake.</summary>
        Safety,
        /// <summary>Niedogodność — dwell +60s, ograniczona prędkość — doors, wheels, lights.</summary>
        Inconvenience,
        /// <summary>Komfort — revenue/reputation penalty — AC, interior, toilets.</summary>
        Comfort,
        /// <summary>Kosmetyczne — tylko wizualne, slow degradation w tle — body.</summary>
        Cosmetic
    }

    /// <summary>
    /// M7-3: Serwis detekcji awarii per-tick per-komponent per-pojazd w trasie.
    ///
    /// Prawdopodobieństwo awarii = <c>(1 - health/100)² × componentRisk × BaseRate</c>.
    /// Baseline: BaseRate 0.0001 per second → ~4% szansy awarii w ciągu godziny
    /// jazdy przy 50% component health × 1.0 risk.
    ///
    /// Wywoływany przez TrainRunSimulator (po degradation check), per-pojazd w trasie.
    /// </summary>
    public static class BreakdownService
    {
        // Base rate awarii per sekundę — patrz FleetBalanceConstants.BreakdownBaseRatePerSecond.

        /// <summary>Częstotliwość check'u awarii — co N sekund symulacji (0.5 = 2x na sec).</summary>
        public const float CheckIntervalSec = 1.0f;

        /// <summary>
        /// Sprawdza wszystkie pojazdy w consist'cie na awarie. Zwraca pierwszą wykrytą
        /// (vehicleId, componentType, severity) lub null gdy brak.
        /// </summary>
        public static BreakdownEvent? CheckForBreakdown(
            System.Collections.Generic.List<int> vehicleIds,
            float deltaSec)
        {
            if (vehicleIds == null || vehicleIds.Count == 0 || deltaSec <= 0f) return null;

            // MB-1 Phase B: GameRule check — gracz wybral casual mode bez awarii
            if (!GameRulesService.IsEnabled(GameRule.VehicleBreakdowns)) return null;

            foreach (var v in FleetService.OwnedVehicles)
            {
                if (!vehicleIds.Contains(v.id)) continue;
                var c = v.components;
                if (c == null) continue;
                var risks = v.componentRisk ?? new ComponentRiskFactors();

                // Check każdy komponent
                if (c.engineCondition >= 0f && Roll(c.engineCondition, risks.engine, deltaSec))
                    return new BreakdownEvent(v.id, ComponentType.Engine, BreakdownSeverity.Critical);
                if (c.pantographCondition >= 0f && Roll(c.pantographCondition, risks.pantograph, deltaSec))
                    return new BreakdownEvent(v.id, ComponentType.Pantograph, BreakdownSeverity.Critical);
                if (c.brakeCondition >= 0f && Roll(c.brakeCondition, risks.brake, deltaSec))
                    return new BreakdownEvent(v.id, ComponentType.Brake, BreakdownSeverity.Safety);
                if (c.wheelsCondition >= 0f && Roll(c.wheelsCondition, risks.wheels, deltaSec))
                    return new BreakdownEvent(v.id, ComponentType.Wheels, BreakdownSeverity.Inconvenience);
                if (c.doorsCondition >= 0f && Roll(c.doorsCondition, risks.doors, deltaSec))
                    return new BreakdownEvent(v.id, ComponentType.Doors, BreakdownSeverity.Inconvenience);
                if (c.lightsCondition >= 0f && Roll(c.lightsCondition, risks.lights, deltaSec))
                    return new BreakdownEvent(v.id, ComponentType.Lights, BreakdownSeverity.Inconvenience);
                if (c.couplingCondition >= 0f && Roll(c.couplingCondition, risks.coupling, deltaSec))
                    return new BreakdownEvent(v.id, ComponentType.Coupling, BreakdownSeverity.Inconvenience);
                if (c.acCondition >= 0f && Roll(c.acCondition, risks.ac, deltaSec))
                    return new BreakdownEvent(v.id, ComponentType.AC, BreakdownSeverity.Comfort);
                if (c.toiletsCondition >= 0f && Roll(c.toiletsCondition, risks.toilets, deltaSec))
                    return new BreakdownEvent(v.id, ComponentType.Toilets, BreakdownSeverity.Comfort);
                if (c.interiorCondition >= 0f && Roll(c.interiorCondition, risks.interior, deltaSec))
                    return new BreakdownEvent(v.id, ComponentType.Interior, BreakdownSeverity.Comfort);
                if (c.electricalCondition >= 0f && Roll(c.electricalCondition, risks.electrical, deltaSec))
                    return new BreakdownEvent(v.id, ComponentType.Electrical, BreakdownSeverity.Inconvenience);
                // Body rzadko się "psuje" — Cosmetic, pomijamy per-tick check
            }
            return null;
        }

        // MP-9: deterministic RNG per-service (seed pochodny od GameState.Seed XOR "BreakdownService")
        static readonly DeterministicRng s_rng = RandomRegistry.GetRng("BreakdownService");

        static bool Roll(float health, float risk, float deltaSec)
        {
            if (health >= 99f) return false; // health ≥99% → ignore (no failures for healthy)
            float failureThreshold = Mathf.Max(0f, (100f - health) / 100f);
            // MB-1 Phase B: difficulty multiplier (0.0 = soft-off, 3.0 = realistic worst)
            float diffMult = DifficultyService.Modifiers.BreakdownChanceMultiplier;
            float probabilityPerSec = failureThreshold * failureThreshold * risk * FleetBalanceConstants.BreakdownBaseRatePerSecond * diffMult;
            float probPerDelta = probabilityPerSec * deltaSec;
            // MP-9: seedowane RNG zamiast UnityEngine.Random.value (~55k rolls/s przy 1000 trains × 11 components × 1Hz)
            return s_rng.Value < probPerDelta;
        }

        /// <summary>
        /// M8-12 / D2: Hook Personnel — modyfikuje self-repair chance wg skilla mechanika
        /// z najblizszego depotu. Input: (vehicleId, baseChance). Output: modified chance.
        /// Null = default (brak bonusu).
        /// Set by <c>RailwayManager.Personnel.WorkshopAssignmentService</c>.
        /// </summary>
        public static System.Func<int, float, float> SelfRepairBonusHook;

        /// <summary>
        /// Self-repair success probability dla komponentu.
        /// M8-12: opcjonalny <paramref name="vehicleId"/> aktywuje hook Personnel (skill mechanika).
        /// </summary>
        public static float SelfRepairSuccessChance(float componentHealth, int vehicleId = -1)
        {
            float baseChance = 0.5f * Mathf.Clamp(componentHealth, 0f, 100f) / 100f;
            if (SelfRepairBonusHook != null && vehicleId >= 0)
                return Mathf.Clamp01(SelfRepairBonusHook(vehicleId, baseChance));
            return baseChance;
        }
    }

    /// <summary>M7-3: Event awarii.</summary>
    public readonly struct BreakdownEvent
    {
        public readonly int vehicleId;
        public readonly ComponentType component;
        public readonly BreakdownSeverity severity;

        public BreakdownEvent(int vehicleId, ComponentType component, BreakdownSeverity severity)
        {
            this.vehicleId = vehicleId;
            this.component = component;
            this.severity = severity;
        }

        public override string ToString() =>
            $"Vehicle#{vehicleId} {component} {severity}";
    }
}
