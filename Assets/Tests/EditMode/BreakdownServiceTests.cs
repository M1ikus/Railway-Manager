using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Core.GameRules;
using RailwayManager.Fleet;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M7-3: testy BreakdownService — ścieżki DETERMINISTYCZNE (bez RNG) + czyste funkcje.
    /// RNG (s_rng) jest static readonly (nie re-seedowalny per-test), więc nie testujemy
    /// probabilistycznego "psuje się przy niskim health" — testujemy gwarantowane kontrakty:
    /// zdrowy pojazd (≥99%) NIGDY się nie psuje, casual mode (rule off) wyłącza awarie,
    /// guardy (pusta lista/null/deltaSec≤0), oraz SelfRepairSuccessChance (czysta funkcja).
    /// </summary>
    public class BreakdownServiceTests
    {
        readonly List<int> _ids = new();
        int _nextId = 920000;

        [SetUp]
        public void SetUp()
        {
            // Domyślnie wszystkie reguły ON (w tym VehicleBreakdowns).
            GameRulesService.ApplyNewGameConfig(new GameRulesConfig());
            BreakdownService.SelfRepairBonusHook = null; // higiena — inny test mógł zostawić hook
        }

        [TearDown]
        public void TearDown()
        {
            foreach (int id in _ids) FleetService.RemoveOwnedVehicle(id);
            _ids.Clear();
            GameRulesService.ApplyNewGameConfig(new GameRulesConfig()); // reset reguł
        }

        int AddVehicle(float componentHealth)
        {
            int id = _nextId++;
            FleetService.AddOwnedVehicle(new FleetVehicleData
            {
                id = id, type = FleetVehicleType.EMU,
                components = VehicleComponents.New(componentHealth),
                componentRisk = new ComponentRiskFactors()
            });
            _ids.Add(id);
            return id;
        }

        // ── Deterministyczne ścieżki CheckForBreakdown ───────────────

        [Test]
        public void HealthyVehicle_NeverBreaksDown()
        {
            // Komponenty 100% (≥99% → Roll zawsze false), nawet duży deltaSec.
            int id = AddVehicle(100f);
            var result = BreakdownService.CheckForBreakdown(new List<int> { id }, deltaSec: 3600f);
            Assert.That(result, Is.Null, "Zdrowy pojazd (100%) nie może się zepsuć.");
        }

        [Test]
        public void HealthyVehicle_At99_NeverBreaksDown()
        {
            // Próg: health ≥99 → ignore (early return false w Roll).
            int id = AddVehicle(99f);
            var result = BreakdownService.CheckForBreakdown(new List<int> { id }, deltaSec: 10000f);
            Assert.That(result, Is.Null, "Pojazd 99% nadal w progu 'bez awarii'.");
        }

        [Test]
        public void BreakdownsDisabled_NoBreakdownEvenAtZeroHealth()
        {
            // Casual mode: rule VehicleBreakdowns OFF → zawsze null nawet przy 0% health.
            var config = new GameRulesConfig();
            config.Set(GameRule.VehicleBreakdowns, false);
            GameRulesService.ApplyNewGameConfig(config);

            int id = AddVehicle(0f); // kompletnie zepsuty
            var result = BreakdownService.CheckForBreakdown(new List<int> { id }, deltaSec: 3600f);
            Assert.That(result, Is.Null, "Rule VehicleBreakdowns OFF → brak awarii mimo 0% health.");
        }

        [Test]
        public void EmptyVehicleList_ReturnsNull()
        {
            Assert.That(BreakdownService.CheckForBreakdown(new List<int>(), 100f), Is.Null);
            Assert.That(BreakdownService.CheckForBreakdown(null, 100f), Is.Null);
        }

        [Test]
        public void NonPositiveDelta_ReturnsNull()
        {
            int id = AddVehicle(0f);
            Assert.That(BreakdownService.CheckForBreakdown(new List<int> { id }, deltaSec: 0f), Is.Null);
            Assert.That(BreakdownService.CheckForBreakdown(new List<int> { id }, deltaSec: -5f), Is.Null);
        }

        [Test]
        public void VehicleNotInOwnedFleet_Ignored()
        {
            // vehicleId spoza floty → pętla go nie znajdzie → null (zdrowych nie ma).
            var result = BreakdownService.CheckForBreakdown(new List<int> { 88888888 }, 3600f);
            Assert.That(result, Is.Null);
        }

        // ── SelfRepairSuccessChance (czysta funkcja, zero RNG) ───────

        [Test]
        public void SelfRepair_ScalesWithHealth()
        {
            // baseChance = 0.5 × health/100. 100%→0.5, 50%→0.25, 0%→0.
            Assert.That(BreakdownService.SelfRepairSuccessChance(100f), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(BreakdownService.SelfRepairSuccessChance(50f), Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(BreakdownService.SelfRepairSuccessChance(0f), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void SelfRepair_ClampsHealthRange()
        {
            // Health poza [0,100] clampowane.
            Assert.That(BreakdownService.SelfRepairSuccessChance(150f), Is.EqualTo(0.5f).Within(0.001f),
                "Health >100 clampowane do 100 → max 0.5.");
            Assert.That(BreakdownService.SelfRepairSuccessChance(-20f), Is.EqualTo(0f).Within(0.001f),
                "Health <0 clampowane do 0.");
        }

        [Test]
        public void SelfRepair_NoHook_UsesBaseChance()
        {
            // Bez hooka Personnel (vehicleId=-1) → czysta base chance.
            BreakdownService.SelfRepairBonusHook = null;
            Assert.That(BreakdownService.SelfRepairSuccessChance(80f, vehicleId: -1),
                Is.EqualTo(0.4f).Within(0.001f));
        }

        [Test]
        public void SelfRepair_WithHook_AppliesBonus()
        {
            // Hook podnosi chance (np. skill mechanika). Sprawdzamy że hook jest wywołany.
            try
            {
                BreakdownService.SelfRepairBonusHook = (vid, baseChance) => baseChance + 0.3f;
                float result = BreakdownService.SelfRepairSuccessChance(80f, vehicleId: 5);
                Assert.That(result, Is.EqualTo(0.4f + 0.3f).Within(0.001f), "Hook dodaje bonus do base chance.");
            }
            finally { BreakdownService.SelfRepairBonusHook = null; }
        }

        [Test]
        public void SelfRepair_HookResultClamped01()
        {
            // Hook zwracający >1 → clamp do 1.
            try
            {
                BreakdownService.SelfRepairBonusHook = (vid, baseChance) => 5f;
                Assert.That(BreakdownService.SelfRepairSuccessChance(80f, vehicleId: 5),
                    Is.EqualTo(1f).Within(0.001f), "Hook >1 clampowany do 1.");
            }
            finally { BreakdownService.SelfRepairBonusHook = null; }
        }
    }
}
