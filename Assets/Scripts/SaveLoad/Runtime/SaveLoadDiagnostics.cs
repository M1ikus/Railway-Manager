using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-14: Diagnostyczny MonoBehaviour do **manualnego testowania save/load** w trakcie gameplay.
    ///
    /// Nie clear'uje SaveRegistry — używa **prawdziwego** stack'a modułów zarejestrowanych
    /// przez bootstrappers (FleetSavable, TimetableSavable, PersonnelSavable, etc.).
    ///
    /// Workflow gameplay edge-case checklist:
    /// 1. Załaduj scenę Depot lub MapScene, stwórz nową grę z presetem Hard
    /// 2. Doprowadź stan do interesującej sytuacji (pociąg w ruchu, pojazd w warsztacie, breakdown)
    /// 3. Right-click na ten komponent w Inspectorze → "Save to test_slot"
    /// 4. Sprawdź log: ile modułów OK / ile failed / total bytes
    /// 5. Mutate state ręcznie (przesuń pociąg, zmień balans, itp.)
    /// 6. Right-click → "Load test_slot"
    /// 7. Verify state się przywrócił (pociąg w pozycji save'a, kasa zresetowana, itp.)
    ///
    /// Print Save Folder Path → wyciąga absolutną ścieżkę do folderu Saves (do ręcznego inspect).
    /// </summary>
    public class SaveLoadDiagnostics : MonoBehaviour
    {
        [Tooltip("Slot ID dla manualnych testów save/load")]
        public string testSlotId = "diag_test";

        [Tooltip("Słowo do nazwy save'a (game time itp. dolicza orchestrator)")]
        public string testSlotName = "Diagnostic Test";

        [Tooltip("Override gameVersion (puste = Application.version)")]
        public string overrideGameVersion = "";

        // ── Save/Load operations ────────────────────

        [ContextMenu("Save to test slot")]
        public void SaveMenu() => _ = SaveAsync();

        public async Task SaveAsync()
        {
            Log.Info($"=== Diagnostics: SaveAsync('{testSlotId}') ===");
            var orch = GetOrchestrator();
            bool ok = await orch.SaveAsync(testSlotId, testSlotName,
                                           saveType: SaveTypes.Diagnostic,
                                           playtime: 0,
                                           gameTimeIso: GameState.CurrentDateIso);
            Log.Info($"=== Diagnostics: Save {(ok ? "OK" : "FAILED")} ({SaveRegistry.Count} modules) ===");
        }

        [ContextMenu("Load test slot")]
        public void LoadMenu() => _ = LoadAsync();

        public async Task LoadAsync()
        {
            Log.Info($"=== Diagnostics: LoadAsync('{testSlotId}') ===");
            var orch = GetOrchestrator();
            var result = await orch.LoadAsync(testSlotId);
            Log.Info($"=== Diagnostics: Load status={result.Status}, " +
                     $"failedModules=[{string.Join(",", result.FailedModules)}] ===");
        }

        [ContextMenu("Delete test slot")]
        public void DeleteMenu() => _ = DeleteAsync();

        public async Task DeleteAsync()
        {
            var storage = GetStorage();
            bool ok = await storage.DeleteAsync(testSlotId);
            Log.Info($"=== Diagnostics: Delete '{testSlotId}' {(ok ? "OK" : "FAILED")} ===");
        }

        [ContextMenu("List all save slots")]
        public void ListMenu() => _ = ListAsync();

        public async Task ListAsync()
        {
            var storage = GetStorage();
            var list = await storage.ListAsync();
            Log.Info($"=== Diagnostics: {list.Count} slot(s) on disk ===");
            foreach (var s in list)
            {
                Log.Info($"  - {s.SlotId} | name='{s.SlotName}' | type={s.SaveType} | " +
                         $"version={s.GameVersion} | savedAt={s.SavedAt} | size={s.FileSizeBytes}b");
            }
        }

        // ── Inspection ──────────────────────────────

        [ContextMenu("Print save folder path")]
        public void PrintFolderPath()
        {
            var storage = new LocalDiskStorage();
            Log.Info($"=== Save folder: {storage.SaveFolder} ===");
            Log.Info($"Quick access: AppPaths.PersistentRoot = {AppPaths.PersistentRoot}");
        }

        [ContextMenu("List registered modules")]
        public void ListModules()
        {
            Log.Info($"=== SaveRegistry: {SaveRegistry.Count} module(s) registered ===");
            foreach (var m in SaveRegistry.All)
                Log.Info($"  - '{m.ModuleId}' (v{m.SchemaVersion}) [{m.GetType().FullName}]");
        }

        [ContextMenu("Print GameState snapshot")]
        public void PrintGameState()
        {
            Log.Info($"=== GameState snapshot ===");
            Log.Info($"  Time:     {GameState.GameTimeSeconds:F0}s ({GameState.GameDay} day)");
            Log.Info($"  Money:    {GameState.Money}");
            Log.Info($"  TimeScale: {GameState.TimeScale}x");
            Log.Info($"  Depot:    '{GameState.DepotName}' @ stationId={GameState.HomeDepotStationId}");
            Log.Info($"  Reputation: {GameState.GlobalReputation}");
        }

        [ContextMenu("Mutate GameState (smoke test)")]
        public void MutateGameState()
        {
            GameState.Money = 999999;
            GameState.GlobalReputation = 1;
            GameState.GameTimeSeconds = 12 * 3600f; // 12:00
            Log.Info("[Diagnostics] GameState mutated. Now save → load → verify restored.");
        }

        // ── Helpers ─────────────────────────────────

        private SaveOrchestrator GetOrchestrator()
        {
            var storage = GetStorage();
            string ver = string.IsNullOrEmpty(overrideGameVersion) ? null : overrideGameVersion;
            return new SaveOrchestrator(storage, ver);
        }

        private static LocalDiskStorage GetStorage() => new LocalDiskStorage();
    }
}
