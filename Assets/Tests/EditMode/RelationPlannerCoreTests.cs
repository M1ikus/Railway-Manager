using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RailwayManager.Fleet;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Assistant;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M11 AS-5a+5b: czysty rdzeń plannera — archetypy (W2), twarde filtry z powodami (W1),
    /// ranking z bonusem dopasowania (W3), arytmetyka wariantów częstotliwości,
    /// estymator opłacalności (przychód capowany pojemnością + kwalifikacja dotacji).
    /// </summary>
    public class RelationPlannerCoreTests
    {
        static RelationFacts MakeFacts(float lengthKm, bool electrified = true,
            float dailyDemand = 1200f, bool routeFound = true)
        {
            return new RelationFacts
            {
                fromStationId = 1,
                toStationId = 2,
                fromName = "TestAlfa",
                toName = "TestBeta",
                routeFound = routeFound,
                routeLengthKm = lengthKm,
                fullyElectrified = electrified,
                estimatedDailyDemand = dailyDemand
            };
        }

        static VehicleCandidate MakeCandidate(int id, FleetVehicleType type,
            bool needsCatenary, float rangeKm = 0f, int seats = 200,
            int speed = 120, int comfort = 3, int costPerKm = 500,
            string[] groups = null, bool available = true)
        {
            return new VehicleCandidate
            {
                vehicleId = id,
                label = $"Test {id}",
                type = type,
                needsCatenary = needsCatenary,
                requiresFuel = type == FleetVehicleType.DMU || type == FleetVehicleType.DieselLocomotive,
                rangeKmFullTank = rangeKm,
                seats = seats,
                maxSpeedKmh = speed,
                comfortClass = comfort,
                operationalCostPerKmGroszy = costPerKm,
                suggestedCategoryGroups = new List<string>(groups ?? new string[0]),
                conditionPercent = 100f,
                breakdownRiskFactor = 1f,
                available = available
            };
        }

        // ────────────── W2: archetypy ──────────────

        [Test]
        public void Archetype_ClassifiedByRouteLength()
        {
            Assert.That(RelationPlannerCore.ClassifyArchetype(MakeFacts(25f)),
                Is.EqualTo(RelationArchetype.Agglomeration));
            Assert.That(RelationPlannerCore.ClassifyArchetype(MakeFacts(80f)),
                Is.EqualTo(RelationArchetype.Regional));
            Assert.That(RelationPlannerCore.ClassifyArchetype(MakeFacts(300f)),
                Is.EqualTo(RelationArchetype.Interregional));
        }

        // ────────────── W1: twarde filtry ──────────────

        [Test]
        public void Filter_EmuRejectedOnNonElectrifiedRoute()
        {
            var facts = MakeFacts(80f, electrified: false);
            var emu = MakeCandidate(1, FleetVehicleType.EMU, needsCatenary: true);
            var dmu = MakeCandidate(2, FleetVehicleType.DMU, needsCatenary: false, rangeKm: 1200f);

            var verdicts = RelationPlannerCore.FilterAndScore(facts,
                new List<VehicleCandidate> { emu, dmu });

            var emuVerdict = verdicts.First(v => v.candidate.vehicleId == 1);
            Assert.That(emuVerdict.accepted, Is.False);
            Assert.That(emuVerdict.rejectReasonKey, Is.EqualTo("assistant.planner.reject.not_electrified"));
            Assert.That(verdicts.First(v => v.candidate.vehicleId == 2).accepted, Is.True,
                "DMU przechodzi po trasie bez sieci");
        }

        [Test]
        public void Filter_DmuRange_RoundTripToRefuel()
        {
            // Zasięg 1263 km < 800 km × 2 (round-trip do tankowania) → odrzucony.
            var farFacts = MakeFacts(800f);
            var dmu = MakeCandidate(1, FleetVehicleType.DMU, needsCatenary: false, rangeKm: 1263f);

            var far = RelationPlannerCore.FilterAndScore(farFacts, new List<VehicleCandidate> { dmu });
            Assert.That(far[0].accepted, Is.False);
            Assert.That(far[0].rejectReasonKey, Is.EqualTo("assistant.planner.reject.range"));

            var nearFacts = MakeFacts(600f);
            var near = RelationPlannerCore.FilterAndScore(nearFacts, new List<VehicleCandidate> { dmu });
            Assert.That(near[0].accepted, Is.True, "1263 km ≥ 600 × 2 → przechodzi");
        }

        [Test]
        public void Filter_LocoAndUnavailable_RejectedWithReasons()
        {
            var facts = MakeFacts(80f);
            var loco = MakeCandidate(1, FleetVehicleType.ElectricLocomotive, needsCatenary: true);
            var broken = MakeCandidate(2, FleetVehicleType.EMU, needsCatenary: true, available: false);

            var verdicts = RelationPlannerCore.FilterAndScore(facts,
                new List<VehicleCandidate> { loco, broken });

            Assert.That(verdicts.First(v => v.candidate.vehicleId == 1).rejectReasonKey,
                Is.EqualTo("assistant.planner.reject.composition_unsupported"),
                "MVP: lokomotywa+wagony poza plannerem (jawny powód, nie cichy skip)");
            Assert.That(verdicts.First(v => v.candidate.vehicleId == 2).rejectReasonKey,
                Is.EqualTo("assistant.planner.reject.unavailable"));
        }

        // ────────────── W3: ranking ──────────────

        [Test]
        public void Score_ArchetypeMatchBonus_BeatsIdenticalNonMatch()
        {
            var facts = MakeFacts(80f); // Regional
            var matching = MakeCandidate(1, FleetVehicleType.EMU, true,
                groups: new[] { "RegionalLocal" });
            var generic = MakeCandidate(2, FleetVehicleType.EMU, true,
                groups: new[] { "ExpressDomestic" });

            var verdicts = RelationPlannerCore.FilterAndScore(facts,
                new List<VehicleCandidate> { generic, matching });

            Assert.That(verdicts[0].candidate.vehicleId, Is.EqualTo(1),
                "Identyczne parametry → wygrywa dopasowanie suggestedCategoryGroups");
            Assert.That(verdicts[0].score - verdicts[1].score,
                Is.EqualTo(AssistantPlannerConstants.ArchetypeMatchBonus).Within(0.001f));
        }

        [Test]
        public void Score_InterregionalWeightsFavorSpeedAndComfort()
        {
            var facts = MakeFacts(300f, dailyDemand: 2000f); // Interregional
            var fastComfy = MakeCandidate(1, FleetVehicleType.EMU, true, seats: 200, speed: 160, comfort: 5);
            var slowBasic = MakeCandidate(2, FleetVehicleType.EMU, true, seats: 200, speed: 80, comfort: 1);

            var verdicts = RelationPlannerCore.FilterAndScore(facts,
                new List<VehicleCandidate> { slowBasic, fastComfy });

            Assert.That(verdicts[0].candidate.vehicleId, Is.EqualTo(1),
                "Na długiej relacji prędkość+komfort przeważają");
        }

        // ────────────── Warianty częstotliwości (arytmetyka) ──────────────

        [Test]
        public void FrequencyVariants_TargetLoadFactorMath()
        {
            // daily 1200, seats 200: economic ceil(1200/180)=7, balanced ceil(1200/140)=9, comfort ceil(1200/100)=12.
            var facts = MakeFacts(80f, dailyDemand: 1200f);
            var c = MakeCandidate(1, FleetVehicleType.EMU, true, seats: 200);

            var variants = RelationPlannerCore.ComputeFrequencyVariants(facts, c);

            Assert.That(variants[0].runsPerDay, Is.EqualTo(7));
            Assert.That(variants[1].runsPerDay, Is.EqualTo(9));
            Assert.That(variants[2].runsPerDay, Is.EqualTo(12));
            Assert.That(variants[1].occupancyPercent, Is.EqualTo(1200f / (9 * 200f) * 100f).Within(0.1f));
            Assert.That(variants[1].taktMinutes,
                Is.EqualTo(UnityEngine.Mathf.RoundToInt(AssistantPlannerConstants.ServiceWindowHours * 60f / 9)));
        }

        [Test]
        public void FrequencyVariants_ClampedToMinimum()
        {
            var variant = RelationPlannerCore.ComputeVariant(50f, 200, 0.7f, "balanced");
            Assert.That(variant.runsPerDay, Is.EqualTo(AssistantPlannerConstants.MinRunsPerDay),
                "Mały popyt → minimum sensowne (tam i z powrotem), nie 1 kurs");
        }

        // ────────────── Estymator opłacalności (AS-5a) ──────────────

        static CommercialCategory MakeCategory(float baseZl, float perKmZl) => new CommercialCategory
        {
            id = "test.os",
            displayName = "Test Osobowy",
            basePriceZl = baseZl,
            pricePerKmZl = perKmZl
        };

        [Test]
        public void Estimate_RevenueCostsAndNet()
        {
            var facts = MakeFacts(100f, dailyDemand: 1000f);
            var c = MakeCandidate(1, FleetVehicleType.EMU, true, seats: 200, costPerKm: 500);
            var cat = MakeCategory(baseZl: 3f, perKmZl: 0.25f); // 100 km → 28 zł = 2800 gr

            var est = RouteProfitabilityEstimator.Estimate(facts, c, runsPerDay: 6, cat);

            Assert.That(est.ticketPriceGroszy, Is.EqualTo(2800));
            Assert.That(est.dailyPaxServed, Is.EqualTo(1000), "Pojemność 6×200=1200 ≥ popyt 1000");
            Assert.That(est.dailyRevenueGroszy, Is.EqualTo(1000L * 2800));
            Assert.That(est.dailyTractionCostGroszy, Is.EqualTo(6L * 100 * 500));
            Assert.That(est.dailyTrackAccessGroszy,
                Is.EqualTo((long)(6 * 100 * RailwayManager.Timetable.Economy.EconomyConstants.TuiPasazerskiSredniaGroszy)));
            Assert.That(est.DailyNetGroszy,
                Is.EqualTo(est.dailyRevenueGroszy - est.dailyTractionCostGroszy - est.dailyTrackAccessGroszy));
        }

        [Test]
        public void Estimate_PaxCappedByCapacity()
        {
            var facts = MakeFacts(100f, dailyDemand: 5000f);
            var c = MakeCandidate(1, FleetVehicleType.EMU, true, seats: 200);
            var est = RouteProfitabilityEstimator.Estimate(facts, c, runsPerDay: 4, MakeCategory(3f, 0.25f));

            Assert.That(est.dailyPaxServed, Is.EqualTo(800),
                "Nie sprzedajemy ponad miejsca: 4 kursy × 200 = 800 < popyt 5000");
        }

        [Test]
        public void Estimate_SubsidyEligibility()
        {
            var facts = MakeFacts(100f, dailyDemand: 1000f);
            var c = MakeCandidate(1, FleetVehicleType.EMU, true, seats: 200);
            var cheap = MakeCategory(3f, 0.25f);   // 28 zł ≤ 40 zł
            var pricey = MakeCategory(10f, 0.60f); // 70 zł > 40 zł

            Assert.That(RouteProfitabilityEstimator.Estimate(facts, c, 4, cheap).subsidyEligible, Is.True,
                "≥4 kursy + bilet ≤40 zł → kwalifikacja (progi z SubsidyCalculator)");
            Assert.That(RouteProfitabilityEstimator.Estimate(facts, c, 3, cheap).subsidyEligible, Is.False,
                "3 kursy < MinRunsPerDay");
            Assert.That(RouteProfitabilityEstimator.Estimate(facts, c, 6, pricey).subsidyEligible, Is.False,
                "Bilet powyżej progu ceny");
        }
    }
}
