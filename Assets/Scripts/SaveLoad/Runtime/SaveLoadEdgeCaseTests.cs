using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-14: Edge case smoke tests dla Save/Load backbone.
    ///
    /// Każdy [ContextMenu] uruchamia osobny scenariusz — wyniki w Console.
    /// Wszystkie testy są **destrukcyjne** dla SaveRegistry (wywołują Clear()),
    /// więc nie odpalać podczas normalnej rozgrywki.
    ///
    /// Scenariusze:
    /// 1. <see cref="TestMigrationChain"/> — save v1, register module v2 z migrator v1→v2, load, verify migrator ran
    /// 2. <see cref="TestNewerVersionRejection"/> — save z gameVersion przyszłości, load → NewerVersion
    /// 3. <see cref="TestNotFound"/> — load nieistniejący slot → NotFound
    /// 4. <see cref="TestPerModuleIsolation"/> — moduł failuje na Deserialize, inne OK → PartialLoad
    /// 5. <see cref="TestMigrationGap"/> — brak migrator w chain → PartialLoad + InitializeDefault
    /// 6. <see cref="TestEmptyBundle"/> — registry pusty, save/load no-op → Success
    /// 7. <see cref="TestStorageList"/> — wiele save'ów → ListSlotsAsync zwraca poprawnie
    /// 8. <see cref="TestSerializeFailureDoesNotOverwrite"/> — błąd Serialize abortuje save i nie nadpisuje slotu
    /// 9. <see cref="TestRegistryOrdering"/> — SaveRegistry.All zwraca deterministyczną kolejność modułów
    ///
    /// Manualne testy gameplay (do odhaczenia w playtest):
    /// - mid-flight save: F5 podczas pociągu w ruchu → load → pociąg powinien być w pozycji startowej rejsu (Phase 2 sims gated)
    /// - manewr save: F5 podczas manewru w 3D → load → manewr powinien się powtórzyć od początku
    /// - workshop save: F5 z pojazdem w warsztacie → load → pojazd nadal w warsztacie z tym samym progress
    /// - breakdown save: F5 z aktywnym breakdown → load → breakdown nadal aktywny
    /// - autosave w trakcie pauzy: Time.timeScale=0 → autosave SKIP'owany (AutoSaveService.ShouldThrottle)
    /// </summary>
    public class SaveLoadEdgeCaseTests : MonoBehaviour
    {
        private const string TestStorageFolder = "Saves_EdgeCases";
        private const string TestSlot = "edgecase_001";

        // ── 1. Migration chain ──────────────────────

        [ContextMenu("Test 1: Migration chain v1 -> v2")]
        public void RunMigrationChainMenu() => _ = TestMigrationChain();

        public async Task<bool> TestMigrationChain()
        {
            Log.Info("=== EdgeCase 1: Migration chain v1->v2 ===");
            var storage = new LocalDiskStorage(TestStorageFolder);
            var orchestrator = new SaveOrchestrator(storage, "0.13.0-test");
            MigrationRunner.Reset();

            // Step A: register v1 module, save
            SaveRegistry.Clear();
            var v1Module = new MigratableModule_v1 { LegacyValue = 100 };
            SaveRegistry.Register(v1Module);
            await orchestrator.SaveAsync(TestSlot, "MigrationTest", SaveTypes.Test, 0, "");

            // Step B: switch to v2 module + register migrator (auto-discovery picks _MigratableModule_v1_v2)
            SaveRegistry.Clear();
            var v2Module = new MigratableModule_v2 { NewValue = -1 }; // -1 = not initialized
            SaveRegistry.Register(v2Module);

            // Step C: load — should trigger migrator
            var result = await orchestrator.LoadAsync(TestSlot);

            bool ok = result.IsSuccess && v2Module.NewValue == 200; // migrator multiplies by 2
            Log.Info(ok ? $"[EdgeCase 1] PASS — migrator ran, NewValue={v2Module.NewValue}"
                       : $"[EdgeCase 1] FAIL — status={result.Status}, NewValue={v2Module.NewValue} (expected 200)");
            return ok;
        }

        // ── 2. Newer version rejection ──────────────

        [ContextMenu("Test 2: Newer version rejection")]
        public void RunNewerVersionMenu() => _ = TestNewerVersionRejection();

        public async Task<bool> TestNewerVersionRejection()
        {
            Log.Info("=== EdgeCase 2: Newer version rejection ===");
            var storage = new LocalDiskStorage(TestStorageFolder);

            // Save z gameVersion z przyszłości
            var futureOrchestrator = new SaveOrchestrator(storage, "9.99.99");
            SaveRegistry.Clear();
            SaveRegistry.Register(new SimpleDummy { Value = 1 });
            await futureOrchestrator.SaveAsync(TestSlot, "FromFuture", SaveTypes.Test, 0, "");

            // Load aktualną wersją — powinno odrzucić
            var currentOrchestrator = new SaveOrchestrator(storage, "0.13.0-test");
            var result = await currentOrchestrator.LoadAsync(TestSlot);
            bool ok = result.Status == LoadStatus.NewerVersion;
            Log.Info(ok ? $"[EdgeCase 2] PASS — NewerVersion detected ({result.ErrorMessage})"
                       : $"[EdgeCase 2] FAIL — expected NewerVersion, got {result.Status}");
            return ok;
        }

        // ── 3. NotFound ──────────────────────────────

        [ContextMenu("Test 3: NotFound for nonexistent slot")]
        public void RunNotFoundMenu() => _ = TestNotFound();

        public async Task<bool> TestNotFound()
        {
            Log.Info("=== EdgeCase 3: NotFound ===");
            var storage = new LocalDiskStorage(TestStorageFolder);
            var orchestrator = new SaveOrchestrator(storage, "0.13.0-test");

            // Upewnij się że slotu nie ma
            await storage.DeleteAsync("nonexistent_slot_xyz");

            var result = await orchestrator.LoadAsync("nonexistent_slot_xyz");
            bool ok = result.Status == LoadStatus.NotFound;
            Log.Info(ok ? "[EdgeCase 3] PASS — NotFound for missing slot"
                       : $"[EdgeCase 3] FAIL — expected NotFound, got {result.Status}");
            return ok;
        }

        // ── 4. Per-module isolation ─────────────────

        [ContextMenu("Test 4: Per-module isolation (one fails, others OK)")]
        public void RunIsolationMenu() => _ = TestPerModuleIsolation();

        public async Task<bool> TestPerModuleIsolation()
        {
            Log.Info("=== EdgeCase 4: Per-module isolation ===");
            var storage = new LocalDiskStorage(TestStorageFolder);
            var orchestrator = new SaveOrchestrator(storage, "0.13.0-test");

            // Save 2 modułów (alpha + beta) — oba zdrowe na save
            SaveRegistry.Clear();
            var alphaSrc = new IsolationAlpha { Value = 42 };
            var betaSrc  = new IsolationBeta  { Value = 99 };
            SaveRegistry.Register(alphaSrc);
            SaveRegistry.Register(betaSrc);
            await orchestrator.SaveAsync(TestSlot, "IsolationTest", SaveTypes.Test, 0, "");

            // Switch: alpha zdrowy, beta podmieniony na ThrowingBeta (ten sam ModuleId, ale Deserialize throws)
            SaveRegistry.Clear();
            var alphaTarget = new IsolationAlpha { Value = -1 }; // -1 = "should be overwritten by load"
            var betaThrow   = new ThrowingBeta();
            SaveRegistry.Register(alphaTarget);
            SaveRegistry.Register(betaThrow);

            var result = await orchestrator.LoadAsync(TestSlot);

            bool isolationOk = result.Status == LoadStatus.PartialLoad
                               && result.FailedModules != null
                               && result.FailedModules.Contains("_iso_beta")
                               && !result.FailedModules.Contains("_iso_alpha")
                               && alphaTarget.Value == 42; // alpha succeeded mimo że beta failed

            Log.Info(isolationOk
                ? $"[EdgeCase 4] PASS — beta failed (in FailedModules), alpha restored to {alphaTarget.Value}"
                : $"[EdgeCase 4] FAIL — status={result.Status}, failed=[{(result.FailedModules == null ? "null" : string.Join(",", result.FailedModules))}], alpha.Value={alphaTarget.Value}");
            return isolationOk;
        }

        // ── 5. Migration gap ─────────────────────────

        [ContextMenu("Test 5: Migration gap (no migrator)")]
        public void RunMigrationGapMenu() => _ = TestMigrationGap();

        public async Task<bool> TestMigrationGap()
        {
            Log.Info("=== EdgeCase 5: Migration gap ===");
            var storage = new LocalDiskStorage(TestStorageFolder);
            var orchestrator = new SaveOrchestrator(storage, "0.13.0-test");
            MigrationRunner.Reset();

            // Save modułu w wersji v1
            SaveRegistry.Clear();
            SaveRegistry.Register(new GapTestModule_v1 { Value = 100 });
            await orchestrator.SaveAsync(TestSlot, "GapTest", SaveTypes.Test, 0, "");

            // Switch do v3 (skip v2 — brak migratora v1→v2 ani v2→v3 w testowanej klasie)
            SaveRegistry.Clear();
            var v3Module = new GapTestModule_v3 { Value = -1 };
            SaveRegistry.Register(v3Module);

            var result = await orchestrator.LoadAsync(TestSlot);
            // PartialLoad z modułem w FailedModules + InitializeDefault wywołane (Value=0)
            bool ok = result.Status == LoadStatus.PartialLoad
                      && result.FailedModules != null
                      && result.FailedModules.Contains("_gaptest")
                      && v3Module.Value == 0; // InitializeDefault
            Log.Info(ok ? "[EdgeCase 5] PASS — gap detected, PartialLoad + InitializeDefault"
                       : $"[EdgeCase 5] FAIL — status={result.Status}, failed=[{(result.FailedModules == null ? "null" : string.Join(",", result.FailedModules))}], v3.Value={v3Module.Value}");
            return ok;
        }

        // ── 6. Empty bundle ─────────────────────────

        [ContextMenu("Test 6: Empty registry roundtrip")]
        public void RunEmptyMenu() => _ = TestEmptyBundle();

        public async Task<bool> TestEmptyBundle()
        {
            Log.Info("=== EdgeCase 6: Empty bundle ===");
            var storage = new LocalDiskStorage(TestStorageFolder);
            var orchestrator = new SaveOrchestrator(storage, "0.13.0-test");

            SaveRegistry.Clear();
            bool saved = await orchestrator.SaveAsync(TestSlot, "EmptyTest", SaveTypes.Test, 0, "");
            var result = await orchestrator.LoadAsync(TestSlot);

            bool ok = saved && result.IsSuccess;
            Log.Info(ok ? "[EdgeCase 6] PASS — empty registry save/load OK (Success)"
                       : $"[EdgeCase 6] FAIL — saved={saved}, status={result.Status}");
            return ok;
        }

        // ── 7. Storage list ─────────────────────────

        [ContextMenu("Test 7: ListSlotsAsync with multiple slots")]
        public void RunListMenu() => _ = TestStorageList();

        public async Task<bool> TestStorageList()
        {
            Log.Info("=== EdgeCase 7: ListSlots ===");
            var storage = new LocalDiskStorage(TestStorageFolder);
            var orchestrator = new SaveOrchestrator(storage, "0.13.0-test");

            SaveRegistry.Clear();
            SaveRegistry.Register(new SimpleDummy { Value = 1 });

            // Stwórz 3 sloty
            await orchestrator.SaveAsync("listtest_001", "Slot 1", SaveTypes.Test, 0, "");
            await orchestrator.SaveAsync("listtest_002", "Slot 2", SaveTypes.Test, 0, "");
            await orchestrator.SaveAsync("listtest_003", "Slot 3", SaveTypes.Test, 0, "");

            var slots = await storage.ListAsync();
            int count = 0;
            bool foundOne = false, foundTwo = false, foundThree = false;
            foreach (var s in slots)
            {
                count++;
                if (s.SlotId == "listtest_001") foundOne = true;
                if (s.SlotId == "listtest_002") foundTwo = true;
                if (s.SlotId == "listtest_003") foundThree = true;
            }

            bool ok = count >= 3 && foundOne && foundTwo && foundThree;
            Log.Info(ok ? $"[EdgeCase 7] PASS — found {count} slots, all 3 listtest_* present"
                       : $"[EdgeCase 7] FAIL — found {count} slots, foundOne={foundOne}, foundTwo={foundTwo}, foundThree={foundThree}");

            // Cleanup
            await storage.DeleteAsync("listtest_001");
            await storage.DeleteAsync("listtest_002");
            await storage.DeleteAsync("listtest_003");
            return ok;
        }

        // ── 8. Serialize failure does not overwrite ─────────────────────────

        [ContextMenu("Test 8: Serialize failure does not overwrite existing slot")]
        public void RunSerializeFailureMenu() => _ = TestSerializeFailureDoesNotOverwrite();

        public async Task<bool> TestSerializeFailureDoesNotOverwrite()
        {
            Log.Info("=== EdgeCase 8: Serialize failure aborts save ===");
            var storage = new LocalDiskStorage(TestStorageFolder);
            var orchestrator = new SaveOrchestrator(storage, "0.13.0-test");
            const string slotId = "serialize_abort_test";

            SaveRegistry.Clear();
            SaveRegistry.Register(new SimpleDummy { Value = 7 });
            bool initialSaved = await orchestrator.SaveAsync(slotId, "SerializeAbortBaseline", SaveTypes.Test, 0, "");

            SaveRegistry.Clear();
            SaveRegistry.Register(new SimpleDummy { Value = 99 });
            SaveRegistry.Register(new ThrowingSerializeModule());
            bool failedSaveResult = await orchestrator.SaveAsync(slotId, "SerializeAbortShouldFail", SaveTypes.Test, 0, "");

            SaveRegistry.Clear();
            var restored = new SimpleDummy { Value = -1 };
            SaveRegistry.Register(restored);
            var result = await orchestrator.LoadAsync(slotId);

            bool ok = initialSaved
                      && !failedSaveResult
                      && result.IsSuccess
                      && restored.Value == 7;

            Log.Info(ok
                ? "[EdgeCase 8] PASS — failed Serialize returned false and previous slot content survived"
                : $"[EdgeCase 8] FAIL — initialSaved={initialSaved}, failedSaveResult={failedSaveResult}, status={result.Status}, restored.Value={restored.Value}");

            await storage.DeleteAsync(slotId);
            return ok;
        }

        // ── 9. Registry ordering ─────────────────────────────────────────────

        [ContextMenu("Test 9: Registry deterministic module order")]
        public void RunRegistryOrderingMenu() => TestRegistryOrdering();

        public bool TestRegistryOrdering()
        {
            Log.Info("=== EdgeCase 9: Registry ordering ===");
            SaveRegistry.Clear();
            SaveRegistry.Register(new OrderedModule("personnel"));
            SaveRegistry.Register(new OrderedModule("_z_custom"));
            SaveRegistry.Register(new OrderedModule("world"));
            SaveRegistry.Register(new OrderedModule("maintenanceJobs"));
            SaveRegistry.Register(new OrderedModule("_a_custom"));
            SaveRegistry.Register(new OrderedModule("fleet"));

            var ids = new List<string>();
            foreach (var module in SaveRegistry.All)
                ids.Add(module.ModuleId);

            bool ok = ids.Count == 6
                      && ids[0] == "world"
                      && ids[1] == "fleet"
                      && ids[2] == "maintenanceJobs"
                      && ids[3] == "personnel"
                      && ids[4] == "_a_custom"
                      && ids[5] == "_z_custom";

            Log.Info(ok
                ? $"[EdgeCase 9] PASS — order=[{string.Join(",", ids)}]"
                : $"[EdgeCase 9] FAIL — order=[{string.Join(",", ids)}]");

            return ok;
        }

        // ── Run all ──────────────────────────────────

        [ContextMenu("Run ALL edge case tests")]
        public void RunAllMenu() => _ = RunAll();

        public async Task RunAll()
        {
            Log.Info("######### M13-14 EDGE CASE SUITE START #########");
            int passed = 0, failed = 0;
            if (await TestMigrationChain()) passed++; else failed++;
            if (await TestNewerVersionRejection()) passed++; else failed++;
            if (await TestNotFound()) passed++; else failed++;
            if (await TestPerModuleIsolation()) passed++; else failed++;
            if (await TestMigrationGap()) passed++; else failed++;
            if (await TestEmptyBundle()) passed++; else failed++;
            if (await TestStorageList()) passed++; else failed++;
            if (await TestSerializeFailureDoesNotOverwrite()) passed++; else failed++;
            if (TestRegistryOrdering()) passed++; else failed++;
            Log.Info($"######### M13-14 SUITE: {passed} passed, {failed} failed #########");
        }

        // ── Test moduły ──────────────────────────────

        // Module v1 — przed migracją
        private class MigratableModule_v1 : ISavable
        {
            public string ModuleId => "_migratable";
            public int SchemaVersion => 1;
            public int LegacyValue;
            public JObject Serialize() => new JObject { ["legacyValue"] = LegacyValue };
            public void Deserialize(JObject data, int sourceVersion) => LegacyValue = data.Value<int>("legacyValue");
            public void InitializeDefault() => LegacyValue = 0;
        }

        // Module v2 — po migracji
        private class MigratableModule_v2 : ISavable
        {
            public string ModuleId => "_migratable";
            public int SchemaVersion => 2;
            public int NewValue;
            public JObject Serialize() => new JObject { ["newValue"] = NewValue };
            public void Deserialize(JObject data, int sourceVersion) => NewValue = data.Value<int>("newValue");
            public void InitializeDefault() => NewValue = 0;
        }

        // Module gap test v1 (nie ma migrator do v3)
        private class GapTestModule_v1 : ISavable
        {
            public string ModuleId => "_gaptest";
            public int SchemaVersion => 1;
            public int Value;
            public JObject Serialize() => new JObject { ["value"] = Value };
            public void Deserialize(JObject data, int sourceVersion) => Value = data.Value<int>("value");
            public void InitializeDefault() => Value = 0;
        }

        private class GapTestModule_v3 : ISavable
        {
            public string ModuleId => "_gaptest";
            public int SchemaVersion => 3;
            public int Value;
            public JObject Serialize() => new JObject { ["value"] = Value };
            public void Deserialize(JObject data, int sourceVersion) => Value = data.Value<int>("value");
            public void InitializeDefault() => Value = 0;
        }

        // Isolation test — alpha + beta osobne moduły. Beta po podmianie ThrowingBeta wyrzuca przy Deserialize.
        private class IsolationAlpha : ISavable
        {
            public string ModuleId => "_iso_alpha";
            public int SchemaVersion => 1;
            public int Value;
            public JObject Serialize() => new JObject { ["value"] = Value };
            public void Deserialize(JObject data, int sourceVersion) => Value = data.Value<int>("value");
            public void InitializeDefault() => Value = 0;
        }

        private class IsolationBeta : ISavable
        {
            public string ModuleId => "_iso_beta";
            public int SchemaVersion => 1;
            public int Value;
            public JObject Serialize() => new JObject { ["value"] = Value };
            public void Deserialize(JObject data, int sourceVersion) => Value = data.Value<int>("value");
            public void InitializeDefault() => Value = 0;
        }

        // Beta replacement that throws on Deserialize — używany do testowania isolation.
        private class ThrowingBeta : ISavable
        {
            public string ModuleId => "_iso_beta";
            public int SchemaVersion => 1;
            public JObject Serialize() => new JObject();
            public void Deserialize(JObject data, int sourceVersion) => throw new InvalidOperationException("test throw on Deserialize");
            public void InitializeDefault() { /* no-op */ }
        }

        private class SimpleDummy : ISavable
        {
            public string ModuleId => "_simple";
            public int SchemaVersion => 1;
            public int Value;
            public JObject Serialize() => new JObject { ["value"] = Value };
            public void Deserialize(JObject data, int sourceVersion) => Value = data.Value<int>("value");
            public void InitializeDefault() => Value = 0;
        }

        private class ThrowingSerializeModule : ISavable
        {
            public string ModuleId => "_throwing_serialize";
            public int SchemaVersion => 1;
            public JObject Serialize() => throw new InvalidOperationException("test throw on Serialize");
            public void Deserialize(JObject data, int sourceVersion) { }
            public void InitializeDefault() { }
        }

        private class OrderedModule : ISavable
        {
            public OrderedModule(string moduleId)
            {
                ModuleId = moduleId;
            }

            public string ModuleId { get; }
            public int SchemaVersion => 1;
            public JObject Serialize() => new JObject();
            public void Deserialize(JObject data, int sourceVersion) { }
            public void InitializeDefault() { }
        }
    }

    /// <summary>Migrator v1 → v2 dla MigratableModule, użyty w EdgeCase 1.
    /// Auto-discovery przez MigrationRunner (klasa publiczna z parameterless ctor).</summary>
    public class _MigratableModule_v1_v2 : IMigrator
    {
        public string ModuleId => "_migratable";
        public int SourceVersion => 1;
        public int TargetVersion => 2;

        public JObject Migrate(JObject input)
        {
            // Migracja: legacyValue * 2 → newValue
            var legacy = input.Value<int>("legacyValue");
            return new JObject { ["newValue"] = legacy * 2 };
        }
    }
}
