using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable.Assistant
{
    /// <summary>Archetyp relacji (W2) — mapuje się na suggestedCategoryGroups z katalogu taboru.</summary>
    public enum RelationArchetype
    {
        Agglomeration,
        Regional,
        Interregional
    }

    /// <summary>
    /// Fakty o relacji zebrane przez warstwę scenową (AS-5c: pathfinder + DemandQuery).
    /// Czysty input dla rdzenia — dzięki temu scoring jest w pełni EditMode-testowalny.
    /// </summary>
    public struct RelationFacts
    {
        public int fromStationId;
        public int toStationId;
        public string fromName;
        public string toName;
        public bool routeFound;
        public float routeLengthKm;
        public bool fullyElectrified;       // wszystkie segmenty trasy z siecią
        public float estimatedDailyDemand;  // DemandQuery.EstimatedDailyDemand [pax/dzień]
    }

    /// <summary>Wyciąg z FleetVehicleData potrzebny scoringowi (czysty POCO).</summary>
    public class VehicleCandidate
    {
        public int vehicleId;
        public string label;
        public FleetVehicleType type;
        public bool needsCatenary;          // Electric bez opcji Diesel (multi-system jedzie dieslem)
        public bool requiresFuel;
        public float rangeKmFullTank;       // FleetFuelMath.RangeKmOnFullTank
        public int seats;
        public int maxSpeedKmh;
        public int comfortClass;            // 1-5
        public int operationalCostPerKmGroszy;
        public List<string> suggestedCategoryGroups = new();
        public float conditionPercent;
        public float breakdownRiskFactor;
        public bool available;              // status zdatny (nie InRepair/OutOfService, kondycja ≥10%)

        /// <summary>Buduje kandydata z pojazdu floty (jedyne miejsce dotykające FleetVehicleData).</summary>
        public static VehicleCandidate FromVehicle(FleetVehicleData v)
        {
            if (v == null) return null;
            bool hasElectric = false, hasDiesel = false;
            if (v.supportedTractions != null)
            {
                foreach (var t in v.supportedTractions)
                {
                    if (t == TractionType.Electric) hasElectric = true;
                    if (t == TractionType.Diesel) hasDiesel = true;
                }
            }
            return new VehicleCandidate
            {
                vehicleId = v.id,
                label = $"{v.series} {v.number}",
                type = v.type,
                needsCatenary = hasElectric && !hasDiesel,
                requiresFuel = v.RequiresFuel,
                rangeKmFullTank = FleetFuelMath.RangeKmOnFullTank(v),
                seats = v.passengerSeats,
                maxSpeedKmh = v.maxSpeedKmh,
                comfortClass = v.comfortClass,
                operationalCostPerKmGroszy = v.operationalCostPerKmGroszy,
                suggestedCategoryGroups = v.suggestedCategoryGroups != null
                    ? new List<string>(v.suggestedCategoryGroups)
                    : new List<string>(),
                conditionPercent = v.conditionPercent,
                breakdownRiskFactor = v.breakdownRiskFactor,
                available = v.status != FleetVehicleStatus.InRepair
                            && v.status != FleetVehicleStatus.OutOfService
                            && v.conditionPercent >= 10f
            };
        }
    }

    /// <summary>Werdykt W1+W3 dla kandydata — odrzucony z powodem (i18n key) albo z punktacją.</summary>
    public class CandidateVerdict
    {
        public VehicleCandidate candidate;
        public bool accepted;
        public string rejectReasonKey;      // assistant.planner.reject.* (transparentny preview)
        public float score;                 // tylko gdy accepted
        public bool archetypeMatch;
    }

    /// <summary>Wariant częstotliwości — deterministyczna arytmetyka, nie scoring.</summary>
    public struct FrequencyVariant
    {
        public string variantKey;           // "economic" / "balanced" / "comfort"
        public int runsPerDay;
        public int taktMinutes;             // okno kursowania / liczba kursów
        public float occupancyPercent;      // popyt / (kursy × miejsca)
    }

    /// <summary>
    /// M11 AS-5b: czysty rdzeń plannera rozkład-od-relacji. Trzy warstwy (AS-OQ2):
    /// W1 twarde filtry (eliminacja, zero wag) → W2 archetyp z długości trasy →
    /// W3 tie-breakery ważone per-archetyp. Plus arytmetyka wariantów częstotliwości.
    /// Wszystko deterministyczne i bez dotykania sceny — fakty wstrzykuje AS-5c.
    ///
    /// Scope MVP (uczciwy): kandydaci = zespoły trakcyjne (EMU/DMU). Lokomotywa+wagony
    /// wymaga komponowania składu — poza MVP plannera (gracz składa ręcznie), odrzucane
    /// z jawnym powodem w preview.
    /// </summary>
    public static class RelationPlannerCore
    {
        static readonly string[] GroupsAgglomeration = { "RegionalAgglomeration" };
        static readonly string[] GroupsRegional = { "RegionalLocal", "RegionalFast" };
        static readonly string[] GroupsInterregional = { "InterregionalFast", "ExpressDomestic", "InterregionalFastNight" };

        // ── W2: archetyp ──

        public static RelationArchetype ClassifyArchetype(in RelationFacts facts)
        {
            if (facts.routeLengthKm <= AssistantPlannerConstants.AgglomerationMaxKm)
                return RelationArchetype.Agglomeration;
            if (facts.routeLengthKm <= AssistantPlannerConstants.RegionalMaxKm)
                return RelationArchetype.Regional;
            return RelationArchetype.Interregional;
        }

        // ── W1 + W3: filtr i ranking ──

        /// <summary>
        /// Filtruje (W1) i punktuje (W3) kandydatów. Zwraca WSZYSTKIE werdykty
        /// (odrzuceni z powodem — preview pokazuje graczowi DLACZEGO), zaakceptowani
        /// posortowani malejąco po score na początku listy.
        /// </summary>
        public static List<CandidateVerdict> FilterAndScore(in RelationFacts facts, List<VehicleCandidate> candidates)
        {
            var verdicts = new List<CandidateVerdict>();
            if (candidates == null) return verdicts;

            var archetype = ClassifyArchetype(facts);

            foreach (var c in candidates)
            {
                if (c == null) continue;
                var verdict = new CandidateVerdict { candidate = c };
                verdicts.Add(verdict);

                if (!facts.routeFound)
                {
                    verdict.rejectReasonKey = "assistant.planner.reject.no_route";
                    continue;
                }
                if (c.type != FleetVehicleType.EMU && c.type != FleetVehicleType.DMU)
                {
                    verdict.rejectReasonKey = "assistant.planner.reject.composition_unsupported";
                    continue;
                }
                if (!c.available)
                {
                    verdict.rejectReasonKey = "assistant.planner.reject.unavailable";
                    continue;
                }
                if (c.needsCatenary && !facts.fullyElectrified)
                {
                    verdict.rejectReasonKey = "assistant.planner.reject.not_electrified";
                    continue;
                }
                if (c.requiresFuel
                    && c.rangeKmFullTank < facts.routeLengthKm * AssistantPlannerConstants.DmuRangeRoundTripFactor)
                {
                    verdict.rejectReasonKey = "assistant.planner.reject.range";
                    continue;
                }
                if (c.seats <= 0)
                {
                    verdict.rejectReasonKey = "assistant.planner.reject.no_seats";
                    continue;
                }

                verdict.accepted = true;
                verdict.archetypeMatch = MatchesArchetype(c, archetype);
                verdict.score = Score(facts, archetype, c, verdict.archetypeMatch);
            }

            // Zaakceptowani malejąco po score, potem odrzuceni (stabilnie po vehicleId — determinizm).
            verdicts.Sort((a, b) =>
            {
                if (a.accepted != b.accepted) return a.accepted ? -1 : 1;
                int byScore = b.score.CompareTo(a.score);
                if (byScore != 0) return byScore;
                return a.candidate.vehicleId.CompareTo(b.candidate.vehicleId);
            });
            return verdicts;
        }

        public static bool MatchesArchetype(VehicleCandidate c, RelationArchetype archetype)
        {
            if (c?.suggestedCategoryGroups == null) return false;
            var groups = archetype switch
            {
                RelationArchetype.Agglomeration => GroupsAgglomeration,
                RelationArchetype.Regional => GroupsRegional,
                _ => GroupsInterregional
            };
            foreach (var g in groups)
            {
                if (c.suggestedCategoryGroups.Contains(g)) return true;
            }
            return false;
        }

        static float Score(in RelationFacts facts, RelationArchetype archetype, VehicleCandidate c, bool match)
        {
            var w = archetype switch
            {
                RelationArchetype.Agglomeration => AssistantPlannerConstants.WeightsAgglomeration,
                RelationArchetype.Regional => AssistantPlannerConstants.WeightsRegional,
                _ => AssistantPlannerConstants.WeightsInterregional
            };

            // capacityFit: obłożenie wariantu zbalansowanego blisko targetu = sweet spot
            // (nie pusty wagon, nie tłok) — kara proporcjonalna do odchyłki.
            var balanced = ComputeVariant(facts.estimatedDailyDemand, c.seats,
                AssistantPlannerConstants.LoadFactorBalanced, "balanced");
            float target = AssistantPlannerConstants.LoadFactorBalanced * 100f;
            float capacityFit = Mathf.Clamp01(1f - Mathf.Abs(balanced.occupancyPercent - target) / target);

            float costEfficiency = Mathf.Clamp01(1f - c.operationalCostPerKmGroszy / AssistantPlannerConstants.CostNormGroszyPerKm);
            float speed = Mathf.Clamp01(c.maxSpeedKmh / AssistantPlannerConstants.SpeedNormKmh);
            float comfort = Mathf.Clamp01((c.comfortClass - 1) / 4f);
            float reliability = Mathf.Clamp01(c.conditionPercent / 100f) * Mathf.Clamp01(2f - c.breakdownRiskFactor);

            float score = w[0] * capacityFit + w[1] * costEfficiency + w[2] * speed
                          + w[3] * comfort + w[4] * reliability;
            if (match) score += AssistantPlannerConstants.ArchetypeMatchBonus;
            return score;
        }

        // ── Warianty częstotliwości (arytmetyka, nie scoring) ──

        /// <summary>Trzy warianty: oszczędny / zbalansowany / komfort (różny target load factor).</summary>
        public static List<FrequencyVariant> ComputeFrequencyVariants(in RelationFacts facts, VehicleCandidate c)
        {
            return new List<FrequencyVariant>
            {
                ComputeVariant(facts.estimatedDailyDemand, c.seats, AssistantPlannerConstants.LoadFactorEconomic, "economic"),
                ComputeVariant(facts.estimatedDailyDemand, c.seats, AssistantPlannerConstants.LoadFactorBalanced, "balanced"),
                ComputeVariant(facts.estimatedDailyDemand, c.seats, AssistantPlannerConstants.LoadFactorComfort, "comfort")
            };
        }

        public static FrequencyVariant ComputeVariant(float dailyDemand, int seats, float targetLoadFactor, string key)
        {
            int runs;
            if (seats <= 0 || dailyDemand <= 0f)
            {
                runs = AssistantPlannerConstants.MinRunsPerDay;
            }
            else
            {
                runs = Mathf.CeilToInt(dailyDemand / (seats * targetLoadFactor));
                runs = Mathf.Clamp(runs, AssistantPlannerConstants.MinRunsPerDay, AssistantPlannerConstants.MaxRunsPerDay);
            }

            float occupancy = seats > 0 && runs > 0
                ? dailyDemand / (runs * (float)seats) * 100f
                : 0f;

            return new FrequencyVariant
            {
                variantKey = key,
                runsPerDay = runs,
                taktMinutes = Mathf.RoundToInt(AssistantPlannerConstants.ServiceWindowHours * 60f / runs),
                occupancyPercent = occupancy
            };
        }
    }
}
