using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-9: Auto-save / Quick-save / Exit-save.
    ///
    /// Funkcje:
    /// 1. Auto-save co 5 minut **game time** — rotujący slot (autosave_001..005,
    ///    najstarszy nadpisywany)
    /// 2. Quick-save F5 → slot "quicksave" (natychmiastowo, bez UI)
    /// 3. Quick-load F9 → load slot "quicksave" (z confirmation w SaveLoadUI [TODO M13-11])
    /// 4. Application.wantsToQuit → best-effort save do "exit_save" (5s timeout)
    ///
    /// Throttling: nie save'uj gdy:
    /// - Gracz w pause menu (PauseMenuUI.IsPopupVisible) — czekaj
    /// - Mid-drag (UI drag operation) — czekaj
    /// - SaveOrchestrator zajety poprzednim save'em — pomiń tę iterację
    ///
    /// Bootstrap: <see cref="EnsureExists"/> → MonoBehaviour singleton.
    /// </summary>
    public class AutoSaveService : MonoBehaviour, ISaveActionsProvider
    {
        public static AutoSaveService Instance { get; private set; }

        // ── Konfiguracja (post-EA: load z Settings) ─────────────────

        /// <summary>Interwał auto-save w SECUNDACH GRY (mnoznik czasu uwzgledniony).
        /// Default 5 min × 60s = 300s game time.</summary>
        public float AutoSaveIntervalGameSec { get; set; } = 5f * 60f;

        /// <summary>Liczba slotów auto-save (rotacja). Default 5.</summary>
        public int AutoSaveSlotCount { get; set; } = 5;

        /// <summary>TD-022: czy auto-save aktywny. Konfigurowane w GameCreator (sekcja Rozgrywka)
        /// + ewentualnie Settings panel. False → tick pomija save (gracz polega na manual saves).
        /// Default true (typowy use case).</summary>
        public bool IsAutoSaveEnabled { get; set; } = true;

        /// <summary>Quick-save slot id (F5).</summary>
        public const string QuickSaveSlotId = "quicksave";

        /// <summary>Exit-save slot id (Application.wantsToQuit).</summary>
        public const string ExitSaveSlotId = "exit_save";

        /// <summary>Auto-save slot id format. {0} = rotation index 1..N.</summary>
        public const string AutoSaveSlotFormat = "autosave_{0:D3}";

        // ── Runtime state ────────────────────────────────────────────

        /// <summary>Czy aktualnie trwa save async (anti-reentrancy).</summary>
        public bool IsBusy { get; private set; }

        /// <summary>Czas (game seconds) ostatniego auto-save'a.
        /// Przeliczany jako (GameDay × 86400 + GameTimeSeconds).</summary>
        private double _lastAutoSaveGameTime;

        /// <summary>Index następnego slotu w rotacji (0..N-1).</summary>
        private int _nextSlotIndex;

        /// <summary>Reference na orchestrator — ustawiony w EnsureExists.</summary>
        public SaveOrchestrator Orchestrator { get; set; }

        // ── Bootstrap ────────────────────────────────────────────────

        public static AutoSaveService EnsureExists(SaveOrchestrator orchestrator = null)
        {
            if (Instance != null)
            {
                if (orchestrator != null && Instance.Orchestrator == null)
                    Instance.Orchestrator = orchestrator;
                InstallSaveActionsHooks();
                return Instance;
            }
            var go = new GameObject("AutoSaveService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AutoSaveService>();
            Instance.Orchestrator = orchestrator;
            Application.wantsToQuit += Instance.OnWantsToQuit;
            InstallSaveActionsHooks();
            Log.Info("[AutoSaveService] Bootstrapped");
            return Instance;
        }

        /// <summary>
        /// 2026-05-08: instaluje hooks DI w <see cref="SaveActionsHook"/> żeby UI
        /// niższych warstw (PauseMenuUI w Depot) mogły wywołać manualny save/load
        /// bez referencji asmdef do SaveLoad.
        ///
        /// 2026-05-13 refactor: provider pattern. Single <c>Register</c> call zamiast
        /// 12× setowania publicznych delegatów (anti-pattern). Implementacja
        /// <see cref="ISaveActionsProvider"/> w sekcji ↓.
        /// </summary>
        static void InstallSaveActionsHooks()
        {
            SaveActionsHook.Register(Instance);
        }

        // ── ISaveActionsProvider implementation ────────────────────────

        void ISaveActionsProvider.QuickSave()
        {
            if (Orchestrator != null && !IsBusy)
                _ = QuickSaveAsync();
        }

        void ISaveActionsProvider.QuickLoad()
        {
            if (Orchestrator != null && !IsBusy)
                _ = QuickLoadAsync();
        }

        void ISaveActionsProvider.SaveAndExitToMainMenu()
        {
            if (Orchestrator != null)
                _ = DoSaveAndExitToMainMenuAsync();
            else
                SceneManager.LoadScene("MainMenu");
        }

        void ISaveActionsProvider.SaveAndQuitApplication()
        {
            if (Orchestrator != null)
                _ = DoSaveAndQuitApplicationAsync();
            else
                Application.Quit();
        }

        // TD-006: master-detail SaveLoadUI z pause menu buttons.
        void ISaveActionsProvider.ShowSaveSlotPicker() => SaveLoadUI.EnsureExists().ShowForSave();
        void ISaveActionsProvider.ShowLoadSlotPicker() => SaveLoadUI.EnsureExists().ShowForLoad();

        // TD-021: enumerate saves dla LoadGameScreenUI (MainMenu nie referuje SaveLoad asmdef).
        async Task<List<SaveSlotSummary>> ISaveActionsProvider.EnumerateSavesAsync()
        {
            var result = new List<SaveSlotSummary>();
            if (SaveLoadServiceBootstrap.Storage == null) return result;
            try
            {
                var slots = await SaveLoadServiceBootstrap.Storage.ListAsync();
                if (slots == null) return result;
                foreach (var s in slots)
                {
                    if (s == null) continue;
                    result.Add(new SaveSlotSummary
                    {
                        SlotId = s.SlotId,
                        SlotName = s.SlotName,
                        SaveType = s.SaveType,
                        GameVersion = s.GameVersion,
                        GameTimeIso = s.GameTimeIso,
                        SavedAt = s.SavedAt,
                        Playtime = s.Playtime,
                        FileSizeBytes = s.FileSizeBytes
                    });
                }
            }
            catch (Exception e)
            {
                Log.Error($"[AutoSaveService] EnumerateSavesAsync hook threw: {e.Message}");
            }
            return result;
        }

        // TD-021: load save by ID dla LoadGameScreenUI.
        async Task<LoadOutcome> ISaveActionsProvider.LoadSaveByIdAsync(string slotId, bool ignoreHmac)
        {
            if (SaveLoadServiceBootstrap.Orchestrator == null)
                return LoadOutcome.Failed;
            try
            {
                var result = await SaveLoadServiceBootstrap.Orchestrator.LoadAsync(slotId, ignoreHmac);
                if (result == null) return LoadOutcome.Failed;
                return result.Status switch
                {
                    LoadStatus.Success      => LoadOutcome.Success,
                    LoadStatus.PartialLoad  => LoadOutcome.PartialLoad,
                    LoadStatus.NotFound     => LoadOutcome.NotFound,
                    LoadStatus.ModifiedSave => LoadOutcome.ModifiedSave,
                    LoadStatus.NewerVersion => LoadOutcome.NewerVersion,
                    _                       => LoadOutcome.Failed
                };
            }
            catch (Exception e)
            {
                Log.Error($"[AutoSaveService] LoadSaveByIdAsync hook threw: {e.Message}");
                return LoadOutcome.Failed;
            }
        }

        // TD-021: LoadingScreen control via hook.
        void ISaveActionsProvider.ShowLoadingScreen(string title) => LoadingScreenManager.EnsureExists()?.Show(title);
        void ISaveActionsProvider.HideLoadingScreen() => LoadingScreenManager.Instance?.Hide();

        // TD-022: AutoSaveService config dla GameCreator ApplyOnStart.
        void ISaveActionsProvider.SetAutoSaveEnabled(bool enabled) => IsAutoSaveEnabled = enabled;
        void ISaveActionsProvider.SetAutoSaveIntervalSec(float sec) { if (sec > 0) AutoSaveIntervalGameSec = sec; }
        void ISaveActionsProvider.ResetRuntimeForNewGame()
        {
            SaveLoadServiceBootstrap.ResetRegisteredModulesForNewGame();
            ResetRuntimeForNewGame();
        }

        public void ResetRuntimeForNewGame()
        {
            IsBusy = false;
            _lastAutoSaveGameTime = ComputeGameTimeSec();
            _nextSlotIndex = 0;
        }

        // ── Save & exit (async, fire-and-forget z void hook'a) ────────
        //
        // Wcześniej (do 2026-05-15) używaliśmy `task.Wait(5s)` na main thread —
        // deadlock-prone gdy ktoś dorzuciłby `await foo()` w SaveAsync chain bez
        // ConfigureAwait(false). Tu wszystkie ścieżki zostały przepisane na
        // pełen async pipeline. Hook ISaveActionsProvider definiuje sygnatury
        // `void`, więc fire-and-forget z `_ = ...Async()` w wrapperach.

        async Task DoSaveAndExitToMainMenuAsync()
        {
            await DoExitSaveAsync(SaveTypes.ManualExitToMenu);
            // Po awaicie continuation może być na thread pool (jeśli orch gate był
            // zajęty) — SceneManager wymaga main thread. Unity bridge async/await
            // zwykle wraca na main przez SynchronizationContext.
            SceneManager.LoadScene("MainMenu");
        }

        async Task DoSaveAndQuitApplicationAsync()
        {
            await DoExitSaveAsync(SaveTypes.ManualQuit);
            Application.Quit();
        }

        async Task DoExitSaveAsync(string saveType)
        {
            if (Orchestrator == null) return;
            if (!IsInActiveGameplayScene())
            {
                Log.Info($"[AutoSaveService] Manual save '{saveType}' skipped — not in gameplay scene.");
                return;
            }
            try
            {
                Log.Info($"[AutoSaveService] Manual save '{saveType}' → {ExitSaveSlotId} (async)");
                bool ok = await Orchestrator.SaveAsync(ExitSaveSlotId, $"Manual save {DateTime.Now:yyyy-MM-dd HH:mm}",
                    saveType: saveType,
                    playtime: ComputePlaytime(),
                    gameTimeIso: GameState.CurrentDateIso);
                if (!ok)
                    Log.Warn($"[AutoSaveService] Manual save '{saveType}' returned false");
            }
            catch (Exception e)
            {
                Log.Error($"[AutoSaveService] Manual save '{saveType}' threw: {e.Message}");
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _lastAutoSaveGameTime = ComputeGameTimeSec();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Application.wantsToQuit -= OnWantsToQuit;
                SaveActionsHook.Unregister();
                Instance = null;
            }
        }

        // ── Update tick ─────────────────────────────────────────────

        void Update()
        {
            // Hot-keys (F5 quick-save, F9 quick-load)
            HandleHotkeys();

            // Auto-save timer
            if (Orchestrator == null) return;
            if (!IsAutoSaveEnabled) return; // TD-022: gracz wyłączył auto-save w GameCreator
            if (IsBusy) return;
            if (!IsInActiveGameplayScene()) return; // skip w MainMenu/GameCreator
            if (ShouldThrottle()) return;

            double now = ComputeGameTimeSec();
            if (now - _lastAutoSaveGameTime >= AutoSaveIntervalGameSec)
            {
                _ = AutoSaveAsync();
            }
        }

        // ── Auto-save ───────────────────────────────────────────────

        private async Task AutoSaveAsync()
        {
            IsBusy = true;
            try
            {
                _lastAutoSaveGameTime = ComputeGameTimeSec();
                int slotNum = (_nextSlotIndex % AutoSaveSlotCount) + 1;
                string slotId = string.Format(AutoSaveSlotFormat, slotNum);
                _nextSlotIndex++;

                string slotName = $"Auto-save {DateTime.Now:yyyy-MM-dd HH:mm}";
                Log.Info($"[AutoSaveService] Auto-save → {slotId} ({slotName})");

                bool ok = await Orchestrator.SaveAsync(slotId, slotName,
                    saveType: SaveTypes.Auto,
                    playtime: ComputePlaytime(),
                    gameTimeIso: GameState.CurrentDateIso);
                if (!ok)
                    Log.Warn($"[AutoSaveService] Auto-save '{slotId}' failed");
            }
            catch (Exception e)
            {
                Log.Error($"[AutoSaveService] Auto-save threw: {e.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ── Quick-save / Quick-load (F5/F9) ─────────────────────────

        private void HandleHotkeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (Orchestrator == null) return;
            if (IsBusy) return;
            // F5/F9 ignorowane poza gameplay scenes — bez tego F5 w MainMenu zapisał
            // pusty stan jako "quicksave", nadpisując prawdziwy quicksave gracza.
            if (!IsInActiveGameplayScene()) return;

            // F5 — quick-save
            if (kb.f5Key.wasPressedThisFrame)
            {
                _ = QuickSaveAsync();
            }

            // F9 — quick-load (TODO: confirmation dialog w SaveLoadUI M13-11)
            if (kb.f9Key.wasPressedThisFrame)
            {
                _ = QuickLoadAsync();
            }
        }

        public async Task<bool> QuickSaveAsync()
        {
            if (Orchestrator == null) return false;
            IsBusy = true;
            try
            {
                Log.Info($"[AutoSaveService] Quick-save → {QuickSaveSlotId}");
                bool ok = await Orchestrator.SaveAsync(QuickSaveSlotId, $"Quick-save {DateTime.Now:HH:mm:ss}",
                    saveType: SaveTypes.Quick,
                    playtime: ComputePlaytime(),
                    gameTimeIso: GameState.CurrentDateIso);
                Log.Info($"[AutoSaveService] Quick-save: {(ok ? "OK" : "FAIL")}");
                return ok;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task<LoadResult> QuickLoadAsync()
        {
            if (Orchestrator == null) return LoadResult.Failed("Orchestrator null");
            IsBusy = true;
            try
            {
                Log.Info($"[AutoSaveService] Quick-load ← {QuickSaveSlotId}");
                var result = await Orchestrator.LoadAsync(QuickSaveSlotId);
                Log.Info($"[AutoSaveService] Quick-load: {result.Status}");
                return result;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ── Application quit ─────────────────────────────────────────

        // Non-blocking exit-save pattern. Wcześniej OnWantsToQuit blokował main
        // thread przez `task.Wait(5s)` na `Orchestrator.SaveAsync` — działało dopóki
        // wszystkie await w SaveAsync chain miały ConfigureAwait(false), ale jeden
        // await bez = deadlock i utracony exit-save (CLAUDE.md gotcha).
        //
        // Nowy flow (Unity wantsToQuit re-entrant):
        //   1. Pierwszy wantsToQuit (user kliknął X / Alt+F4):
        //      → start async save w tle, return false (anulujemy quit teraz).
        //   2. Async save kończy → finally wywołuje Application.Quit() ponownie.
        //   3. Drugi wantsToQuit fires → flag _saveOnQuitCompleted == true →
        //      return true → Unity quit naprawdę.
        //
        // Edge case: user kliknie X dwa razy podczas pierwszego save → drugie
        // wywołanie zwraca false (in-progress), Unity nie quit'uje. Czekamy
        // aż save się skończy.
        private bool _saveOnQuitInProgress;
        private bool _saveOnQuitCompleted;

        /// <summary>Handler dla Application.wantsToQuit. Pierwszy fire startuje
        /// async exit-save i anuluje quit; drugi fire (po Application.Quit z continuation
        /// finally) zwraca true i Unity faktycznie quit'uje.</summary>
        private bool OnWantsToQuit()
        {
            if (Orchestrator == null) return true;
            if (_saveOnQuitCompleted) return true; // drugi try po zakończeniu save → pozwól quit
            if (_saveOnQuitInProgress) return false; // czekaj na save (drugi user-trigger ignorowany)
            if (IsBusy) return true; // inny save w trakcie (np. autosave) — skip exit-save, niech tamten dokończy
            if (!IsInActiveGameplayScene()) return true; // quit z MainMenu — brak state do zapisu

            _saveOnQuitInProgress = true;
            Log.Info($"[AutoSaveService] OnWantsToQuit → {ExitSaveSlotId} (async, quit anulowany do czasu finish)");
            _ = SaveOnQuitAsync();
            return false; // anuluj quit, wykonamy go po save w SaveOnQuitAsync.finally
        }

        private async Task SaveOnQuitAsync()
        {
            try
            {
                bool ok = await Orchestrator.SaveAsync(ExitSaveSlotId, $"Exit save {DateTime.Now:yyyy-MM-dd HH:mm}",
                    saveType: SaveTypes.Exit,
                    playtime: ComputePlaytime(),
                    gameTimeIso: GameState.CurrentDateIso);
                if (!ok) Log.Warn("[AutoSaveService] Exit-save returned false");
                else Log.Info("[AutoSaveService] Exit-save OK, ponowny Application.Quit()");
            }
            catch (Exception e)
            {
                Log.Error($"[AutoSaveService] Exit-save threw: {e.Message}");
            }
            finally
            {
                _saveOnQuitCompleted = true;
                Application.Quit(); // re-fire wantsToQuit → flag set → return true → Unity quit
            }
        }

        // ── Throttling helpers ───────────────────────────────────────

        /// <summary>Czy aktualnie powinno się odłożyć save (gracz w menu / drag / itd.).
        /// Pre-EA proste: pomijamy gdy Time.timeScale == 0 (gra zatrzymana = pause).
        /// Post-EA: dodać check'i mid-modal / mid-drag.</summary>
        private bool ShouldThrottle()
        {
            // Hard pause = nie save'uj
            if (Time.timeScale == 0f) return true;
            if (GameState.IsPaused) return true;
            return false;
        }

        /// <summary>Czy gracz jest w aktywnej scenie gameplay (Depot lub MapScene loaded).
        /// SceneController używa additive load — obie sceny mogą być loaded jednocześnie,
        /// którąś z nich aktywną. AutoSaveService jest DontDestroyOnLoad i żyje w
        /// MainMenu/GameCreator gdzie save'owanie nie ma sensu (pusty state) — bez tego
        /// guard'a F5 w MainMenu nadpisał slot "quicksave" pustym save'em.</summary>
        private static bool IsInActiveGameplayScene()
        {
            var depot = SceneManager.GetSceneByName("Depot");
            if (depot.IsValid() && depot.isLoaded) return true;
            var map = SceneManager.GetSceneByName("MapScene");
            if (map.IsValid() && map.isLoaded) return true;
            return false;
        }

        /// <summary>Czas gry w sekundach od startu (GameDay×86400 + GameTimeSeconds).
        /// Używane do trigger'a auto-save co N sekund GAME time.</summary>
        private double ComputeGameTimeSec()
            => (double)GameState.GameDay * 86400.0 + GameState.GameTimeSeconds;

        /// <summary>Sumaryczny playtime (accumulator z poprzednich sesji + aktywna).
        /// Manifest.Playtime używa tego do "Grasz X godzin" w SaveLoadUI. Bez accumulator'a
        /// playtime resetował się przy każdym restarcie gry — load 100h save → next save
        /// zapisywał Time.realtimeSinceStartup (~30min) zamiast 100h30min.</summary>
        private static double ComputePlaytime()
            => GameState.GetTotalPlaytimeSec();
    }
}
