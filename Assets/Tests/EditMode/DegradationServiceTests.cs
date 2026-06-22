using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Core.GameRules;
using RailwayManager.Fleet;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M7-2: testy DegradationService — degradacja komponentów per km (czysta arytmetyka, zero RNG).
    /// EditMode. Testuje RELACJE + obserwowalne zachowanie (nie zamraża stałych balansu — M6.5):
    /// degradacja proporcjonalna do km, wheels szybciej niż engine, niski reliability = szybsze
    /// zużycie, N/A komponenty (-1) nietknięte, mileage++, casual mode OFF, clamp [0].
    /// </summary>
    public class DegradationServiceTests
    {
        readonly List<int> _ids = new();
        int _nextId = 910000;

        [SetUp]
        public void SetUp() => GameRulesService.ApplyNewGameConfig(new GameRulesConfig()); // wszystko ON

        [TearDown]
        public void TearDown()
        {
            foreach (int id in _ids) FleetService.RemoveOwnedVehicle(id);
            _ids.Clear();
            GameRulesService.ApplyNewGameConfig(new GameRulesConfig());
        }

        FleetVehicleData Add(float health = 100f, int reliability = 100, VehicleComponents comps = null)
        {
            int id = _nextId++;
            var v = new FleetVehicleData
            {
                id = id, type = FleetVehicleType.EMU,
                components = comps ?? VehicleComponents.New(health),
                componentRisk = new ComponentRiskFactors(),
                reliabilityScore = reliability
            };
            FleetService.AddOwnedVehicle(v);
            _ids.Add(id);
            return v;
        }

        void Degrade(int id, float km) => DegradationService.ApplyDegradation(new List<int> { id }, km);

        // ── Podstawowa degradacja ────────────────────────────────────

        [Test]
        public void Degradation_ReducesComponentsAfterLongDistance()
        {
            var v = Add(health: 100f);
            Degrade(v.id, 500000f); // 500k km — mierzalne przy stałych ~0.0001%/km

            Assert.That(v.components.engineCondition, Is.LessThan(100f), "Silnik degraduje po długim dystansie.");
            Assert.That(v.components.wheelsCondition, Is.LessThan(100f), "Koła degradują.");
        }

        [Test]
        public void Degradation_ProportionalToDistance()
        {
            var vShort = Add(health: 100f);
            var vLong = Add(health: 100f);
            Degrade(vShort.id, 100000f);
            Degrade(vLong.id, 400000f);

            float wearShort = 100f - vShort.components.engineCondition;
            float wearLong = 100f - vLong.components.engineCondition;
            Assert.That(wearLong, Is.GreaterThan(wearShort), "Dłuższy dystans → większe zużycie.");
        }

        [Test]
        public void Degradation_WheelsWearFasterThanEngine()
        {
            // DegradeWheelsPerKm (0.0002) > DegradeEnginePerKm (0.00008) → koła zużywają się szybciej.
            var v = Add(health: 100f);
            Degrade(v.id, 200000f);
            float engineWear = 100f - v.components.engineCondition;
            float wheelsWear = 100f - v.components.wheelsCondition;
            Assert.That(wheelsWear, Is.GreaterThan(engineWear), "Koła zużywają się szybciej niż silnik.");
        }

        [Test]
        public void Degradation_LowReliabilityWearsFaster()
        {
            // relFactor = 2 - rel/100. rel=0 → 2.0 (2× szybciej), rel=100 → 1.0.
            var reliable = Add(health: 100f, reliability: 100);
            var unreliable = Add(health: 100f, reliability: 0);
            Degrade(reliable.id, 300000f);
            Degrade(unreliable.id, 300000f);

            float reliableWear = 100f - reliable.components.engineCondition;
            float unreliableWear = 100f - unreliable.components.engineCondition;
            Assert.That(unreliableWear, Is.GreaterThan(reliableWear),
                "Niski reliability → szybsze zużycie (relFactor 2.0 vs 1.0).");
        }

        // ── N/A komponenty + agregaty ────────────────────────────────

        [Test]
        public void Degradation_SkipsNotApplicableComponents()
        {
            // Pantograf -1 (N/A, np. diesel) → nietknięty (zostaje -1, nie idzie w minus).
            var comps = VehicleComponents.New(100f);
            comps.pantographCondition = -1f;
            var v = Add(comps: comps);
            Degrade(v.id, 500000f);

            Assert.That(v.components.pantographCondition, Is.EqualTo(-1f),
                "Komponent N/A (-1) pozostaje -1, nie degraduje.");
            Assert.That(v.components.engineCondition, Is.LessThan(100f), "Zainstalowane degradują normalnie.");
        }

        [Test]
        public void Degradation_UpdatesMileageAndCondition()
        {
            var v = Add(health: 100f);
            float startMileage = v.mileageKm;
            Degrade(v.id, 50000f);

            Assert.That(v.mileageKm, Is.EqualTo(startMileage + 50000f), "Mileage rośnie o deltaKm.");
            Assert.That(v.conditionPercent, Is.LessThan(100f), "conditionPercent = agregat zdegradowanych komponentów.");
            Assert.That(v.conditionPercent, Is.GreaterThan(0f));
        }

        [Test]
        public void Degradation_ClampsAtZero()
        {
            // Ekstremalny dystans → komponenty nie schodzą poniżej 0.
            var v = Add(health: 5f);
            Degrade(v.id, 100_000_000f); // 100M km
            Assert.That(v.components.engineCondition, Is.GreaterThanOrEqualTo(0f), "Nie schodzi poniżej 0.");
            Assert.That(v.components.wheelsCondition, Is.GreaterThanOrEqualTo(0f));
        }

        // ── Guardy ───────────────────────────────────────────────────

        [Test]
        public void Degradation_Disabled_NoChange()
        {
            var config = new GameRulesConfig();
            config.Set(GameRule.MaintenanceComponentDecay, false);
            GameRulesService.ApplyNewGameConfig(config);

            var v = Add(health: 100f);
            Degrade(v.id, 1_000_000f);
            Assert.That(v.components.engineCondition, Is.EqualTo(100f), "Casual mode (decay OFF) → brak degradacji.");
            Assert.That(v.mileageKm, Is.EqualTo(0f), "Mileage też nie rośnie gdy decay OFF.");
        }

        [Test]
        public void Degradation_NonPositiveKm_NoChange()
        {
            var v = Add(health: 100f);
            Degrade(v.id, 0f);
            Degrade(v.id, -100f);
            Assert.That(v.components.engineCondition, Is.EqualTo(100f), "deltaKm≤0 → no-op.");
        }

        [Test]
        public void Degradation_EmptyOrNullList_NoCrash()
        {
            Assert.DoesNotThrow(() => DegradationService.ApplyDegradation(new List<int>(), 1000f));
            Assert.DoesNotThrow(() => DegradationService.ApplyDegradation(null, 1000f));
        }
    }
}
