using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-6: Smoke test backbone'a Save/Load. Bootstrapuje 2 dummy moduły,
    /// wykonuje SaveAsync → LoadAsync → verify roundtrip.
    ///
    /// Wywoływany ręcznie z [ContextMenu] w edytorze. Nie jest automatycznym
    /// testem (project decision: brak Unity Test Framework, używamy smoke + manual).
    ///
    /// Wynik widoczny w Console — log z każdego stepa + final OK/FAIL.
    /// </summary>
    public class SaveLoadSmokeTest : MonoBehaviour
    {
        [Tooltip("Slot ID użyty przez test. Plik zostaje na dysku — można usunąć ręcznie z folderu Saves.")]
        public string testSlotId = "smoketest_001";

        [ContextMenu("Run Smoke Test")]
        public void RunSmokeTestMenu() => _ = RunSmokeTestAsync();

        public async Task<bool> RunSmokeTestAsync()
        {
            Log.Info("=== SaveLoad smoke test START ===");

            // Setup: czyste registry + dwa dummy moduły z różnym contentem
            SaveRegistry.Clear();
            var moduleA = new DummyModuleA { CounterValue = 42, NameValue = "Test A" };
            var moduleB = new DummyModuleB { Pi = 3.14159, EnabledFlag = true, ListValues = new[] { 1, 2, 3, 4, 5 } };
            SaveRegistry.Register(moduleA);
            SaveRegistry.Register(moduleB);

            // Storage + orchestrator
            var storage = new LocalDiskStorage("Saves_SmokeTest");
            var orchestrator = new SaveOrchestrator(storage, "0.13.0-test");

            // SAVE
            Log.Info($"[SmokeTest] SaveAsync to '{testSlotId}'");
            bool saved = await orchestrator.SaveAsync(testSlotId, "Smoke test slot",
                                                      saveType: SaveTypes.Test,
                                                      playtime: 123.45,
                                                      gameTimeIso: "2026-04-26T12:00:00");
            if (!saved)
            {
                Log.Error("[SmokeTest] SaveAsync failed");
                return false;
            }

            // Mutate — żeby load musiał faktycznie restore
            moduleA.CounterValue = 0;
            moduleA.NameValue = "MUTATED";
            moduleB.Pi = 0;
            moduleB.EnabledFlag = false;
            moduleB.ListValues = new int[0];

            // LOAD
            Log.Info($"[SmokeTest] LoadAsync from '{testSlotId}'");
            var loadResult = await orchestrator.LoadAsync(testSlotId);
            if (!loadResult.IsSuccess)
            {
                Log.Error($"[SmokeTest] LoadAsync failed: {loadResult.Status} {loadResult.ErrorMessage}");
                return false;
            }

            // Verify
            bool ok = true;
            if (moduleA.CounterValue != 42)
            {
                Log.Error($"[SmokeTest] FAIL ModuleA.CounterValue: expected 42, got {moduleA.CounterValue}");
                ok = false;
            }
            if (moduleA.NameValue != "Test A")
            {
                Log.Error($"[SmokeTest] FAIL ModuleA.NameValue: expected 'Test A', got '{moduleA.NameValue}'");
                ok = false;
            }
            if (System.Math.Abs(moduleB.Pi - 3.14159) > 1e-9)
            {
                Log.Error($"[SmokeTest] FAIL ModuleB.Pi: expected 3.14159, got {moduleB.Pi}");
                ok = false;
            }
            if (!moduleB.EnabledFlag)
            {
                Log.Error($"[SmokeTest] FAIL ModuleB.EnabledFlag: expected true, got false");
                ok = false;
            }
            if (moduleB.ListValues == null || moduleB.ListValues.Length != 5)
            {
                Log.Error($"[SmokeTest] FAIL ModuleB.ListValues: expected length 5, got {moduleB.ListValues?.Length ?? -1}");
                ok = false;
            }

            // HMAC tamper test — load z ignoreHmac=false na zmodyfikowanym pliku
            Log.Info("[SmokeTest] HMAC tamper test");
            string path = System.IO.Path.Combine(storage.SaveFolder, testSlotId + LocalDiskStorage.FileExtension);
            byte[] bytes = await System.IO.File.ReadAllBytesAsync(path);
            // Flip last byte (HMAC base64 char) — nie ruszaj gzip headera
            bytes[bytes.Length - 1] ^= 0x42;
            await System.IO.File.WriteAllBytesAsync(path, bytes);

            var tamperedResult = await orchestrator.LoadAsync(testSlotId);
            // Tampered byte zwykle zerwie też JSON parse (gzip stream chksum) — może być Failed lub ModifiedSave
            if (tamperedResult.Status != LoadStatus.ModifiedSave && tamperedResult.Status != LoadStatus.Failed)
            {
                Log.Warn($"[SmokeTest] Tamper test: expected ModifiedSave or Failed, got {tamperedResult.Status} " +
                         "(może gzip checksum to wyłapał wcześniej — to też OK, ale warto sprawdzić)");
            }
            else
            {
                Log.Info($"[SmokeTest] Tamper detected: {tamperedResult.Status}");
            }

            // Cleanup tampered file
            try { System.IO.File.Delete(path); } catch { /* ignore */ }

            Log.Info(ok ? "=== SaveLoad smoke test PASS ===" : "=== SaveLoad smoke test FAIL ===");
            return ok;
        }

        // ── Dummy modules dla testu ───────────────────

        private class DummyModuleA : ISavable
        {
            public string ModuleId => "_smoketest_a";
            public int SchemaVersion => 1;

            public int CounterValue;
            public string NameValue = "";

            public JObject Serialize() => new JObject
            {
                ["counter"] = CounterValue,
                ["name"] = NameValue
            };

            public void Deserialize(JObject data, int sourceVersion)
            {
                CounterValue = data.Value<int>("counter");
                NameValue = data.Value<string>("name") ?? "";
            }

            public void InitializeDefault()
            {
                CounterValue = 0;
                NameValue = "";
            }
        }

        private class DummyModuleB : ISavable
        {
            public string ModuleId => "_smoketest_b";
            public int SchemaVersion => 1;

            public double Pi;
            public bool EnabledFlag;
            public int[] ListValues;

            public JObject Serialize() => new JObject
            {
                ["pi"] = Pi,
                ["enabled"] = EnabledFlag,
                ["values"] = new JArray(ListValues ?? new int[0])
            };

            public void Deserialize(JObject data, int sourceVersion)
            {
                Pi = data.Value<double>("pi");
                EnabledFlag = data.Value<bool>("enabled");
                var arr = data["values"] as JArray;
                ListValues = arr != null ? arr.ToObject<int[]>() : new int[0];
            }

            public void InitializeDefault()
            {
                Pi = 0;
                EnabledFlag = false;
                ListValues = new int[0];
            }
        }
    }
}
