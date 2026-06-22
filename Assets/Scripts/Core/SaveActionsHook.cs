using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RailwayManager.Core
{
    /// <summary>
    /// Kontrakt provider'a save/load actions. Implementowany przez SaveLoad asmdef
    /// (`AutoSaveService` w EA, `MockSaveActionsProvider` w testach).
    ///
    /// SaveLoad to najwyższa warstwa hierarchii asmdef, więc niższe warstwy
    /// (Core/Depot/MainMenu/GameCreator) NIE widzą jej typów. Provider w Core
    /// + atomic <see cref="SaveActionsHook.Register"/> daje cross-asmdef wywołania
    /// save/load bez cyclic dependency.
    /// </summary>
    public interface ISaveActionsProvider
    {
        void QuickSave();
        void QuickLoad();
        void SaveAndExitToMainMenu();
        void SaveAndQuitApplication();
        void ShowSaveSlotPicker();
        void ShowLoadSlotPicker();
        Task<List<SaveSlotSummary>> EnumerateSavesAsync();
        Task<LoadOutcome> LoadSaveByIdAsync(string slotId, bool ignoreHmac);
        void ShowLoadingScreen(string title);
        void HideLoadingScreen();
        void SetAutoSaveEnabled(bool enabled);
        void SetAutoSaveIntervalSec(float sec);
        void ResetRuntimeForNewGame();
    }

    /// <summary>
    /// 2026-05-08 — hooks DI dla manualnych akcji save/load wywoływanych z UI niższych
    /// warstw (PauseMenuUI w Depot, MainMenuUI itd.).
    ///
    /// SaveLoad asmdef jest najwyższą warstwą (referuje wszystkich), więc niższe
    /// asmdefy (Core/Depot/MainMenu) NIE widzą bezpośrednio <c>AutoSaveService</c>
    /// ani <c>SaveOrchestrator</c>. Wcześniej (2026-05-08..05-13) były to publiczne
    /// mutowalne static delegaty — każdy callsite mógł je nullować, brak atomic
    /// install. Refactor 2026-05-13: provider pattern z <see cref="ISaveActionsProvider"/>,
    /// atomic <see cref="Register"/>/<see cref="Unregister"/>. Publiczny API
    /// (12 properties typu Action) zachowuje backwards-compat — callsites nadal
    /// piszą <c>SaveActionsHook.QuickSave?.Invoke()</c>.
    ///
    /// Bootstrap: <c>RailwayManager.SaveLoad.AutoSaveService.EnsureExists</c>
    /// wywołuje <see cref="Register"/> z własną implementacją <see cref="ISaveActionsProvider"/>.
    /// Brak provider'a (np. testy bez SaveLoad scene) → wszystkie properties null → no-op.
    /// </summary>
    public static class SaveActionsHook
    {
        private static ISaveActionsProvider _provider;

        /// <summary>True gdy <see cref="Register"/> zainstalował provider. Diagnostic.</summary>
        public static bool IsRegistered => _provider != null;

        /// <summary>
        /// Atomic install — wszystkie 12 hooków ustawione razem z jednej implementacji
        /// <see cref="ISaveActionsProvider"/>. Brak ryzyka partial bootstrap (poprzednio
        /// AutoSaveService musiało setować 12 osobnych delegatów; jeśli rzucił między
        /// nimi, część hooków pozostawała null silently).
        /// </summary>
        public static void Register(ISaveActionsProvider provider)
        {
            if (provider == null) { Unregister(); return; }
            _provider = provider;
            QuickSave                = provider.QuickSave;
            QuickLoad                = provider.QuickLoad;
            SaveAndExitToMainMenu    = provider.SaveAndExitToMainMenu;
            SaveAndQuitApplication   = provider.SaveAndQuitApplication;
            ShowSaveSlotPicker       = provider.ShowSaveSlotPicker;
            ShowLoadSlotPicker       = provider.ShowLoadSlotPicker;
            EnumerateSavesAsync      = provider.EnumerateSavesAsync;
            LoadSaveByIdAsync        = provider.LoadSaveByIdAsync;
            ShowLoadingScreen        = provider.ShowLoadingScreen;
            HideLoadingScreen        = provider.HideLoadingScreen;
            SetAutoSaveEnabled       = provider.SetAutoSaveEnabled;
            SetAutoSaveIntervalSec   = provider.SetAutoSaveIntervalSec;
            ResetRuntimeForNewGame   = provider.ResetRuntimeForNewGame;
        }

        /// <summary>
        /// Czysci provider + wszystkie hook'i. Wywołać przed scene transition do
        /// MainMenu albo przy teardown'ie SaveLoad podsystemu (testy).
        /// </summary>
        public static void Unregister()
        {
            _provider = null;
            QuickSave              = null;
            QuickLoad              = null;
            SaveAndExitToMainMenu  = null;
            SaveAndQuitApplication = null;
            ShowSaveSlotPicker     = null;
            ShowLoadSlotPicker     = null;
            EnumerateSavesAsync    = null;
            LoadSaveByIdAsync      = null;
            ShowLoadingScreen      = null;
            HideLoadingScreen      = null;
            SetAutoSaveEnabled     = null;
            SetAutoSaveIntervalSec = null;
            ResetRuntimeForNewGame = null;
        }

        // ──────────────────────────────────────────────────────────────
        // Public read-only API — callsites wywołują przez `?.Invoke()`.
        // Settery prywatne — tylko Register może je modyfikować. Wcześniej
        // każdy konsument mógł `SaveActionsHook.QuickSave = null;` i wywalić
        // system — BUG-pattern wymieniony w audycie Core.
        // ──────────────────────────────────────────────────────────────

        /// <summary>Quick-save (slot "quicksave"). Fire-and-forget.</summary>
        public static Action QuickSave { get; private set; }

        /// <summary>Quick-load (slot "quicksave"). Fire-and-forget.</summary>
        public static Action QuickLoad { get; private set; }

        /// <summary>
        /// Save & quit do Main Menu — wywoływane z PauseMenu "Wyjdź do menu (z zapisem)".
        /// Implementacja: synchronous save (timeout 5s) + scene transition.
        /// </summary>
        public static Action SaveAndExitToMainMenu { get; private set; }

        /// <summary>
        /// Save & quit aplikacji — wywoływane z PauseMenu "Wyjdź z gry (z zapisem)".
        /// Implementacja: synchronous save (timeout 5s) + Application.Quit.
        /// </summary>
        public static Action SaveAndQuitApplication { get; private set; }

        /// <summary>
        /// TD-006: otwiera SaveLoadUI w trybie Save (slot picker — gracz wybiera nazwę
        /// + slot do nadpisania lub utworzenia nowego). Wywoływane z PauseMenu "Zapisz stan".
        /// Fallback gdy hook null → QuickSave (quick-save bez UI).
        /// </summary>
        public static Action ShowSaveSlotPicker { get; private set; }

        /// <summary>
        /// TD-006: otwiera SaveLoadUI w trybie Load (lista zapisów do wczytania). Wywoływane
        /// z PauseMenu "Załaduj stan". Fallback gdy hook null → QuickLoad.
        /// </summary>
        public static Action ShowLoadSlotPicker { get; private set; }

        // ──────────────────────────────────────────────────────────────
        // 2026-05-10 — TD-021/TD-022 cyclic dependency fix
        //
        // MainMenu i GameCreator nie mogą referować SaveLoad asmdef
        // (Depot → MainMenu existing, więc MainMenu → SaveLoad → Depot = cycle).
        // Hooks zastępują direct ref — Core DTO (SaveSlotSummary, LoadOutcome)
        // na hook boundary. Mapping w AutoSaveService.
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// TD-021: enumerate save slots (dla LoadGameScreenUI w MainMenu).
        /// Zwraca Task z listą SaveSlotSummary, sortowane wg SavedAt desc.
        /// </summary>
        public static Func<Task<List<SaveSlotSummary>>> EnumerateSavesAsync { get; private set; }

        /// <summary>
        /// TD-021: load save by slotId (dla LoadGameScreenUI). Zwraca Task z
        /// LoadOutcome enum. Caller robi SceneManager.LoadScene("Depot")
        /// na Success/PartialLoad.
        /// </summary>
        public static Func<string, bool, Task<LoadOutcome>> LoadSaveByIdAsync { get; private set; }

        /// <summary>
        /// TD-022: configure auto-save enabled (dla GameCreator ApplyOnStart).
        /// Wywoła AutoSaveService.IsAutoSaveEnabled = value.
        /// </summary>
        public static Action<bool> SetAutoSaveEnabled { get; private set; }

        /// <summary>
        /// TD-022: configure auto-save interval (dla GameCreator ApplyOnStart).
        /// Wartość w SECUNDACH GRY (z 6s granularity, np. 5 min × 60 = 300s).
        /// Wywoła AutoSaveService.AutoSaveIntervalGameSec = value.
        /// </summary>
        public static Action<float> SetAutoSaveIntervalSec { get; private set; }

        /// <summary>
        /// Resetuje zarejestrowane runtime moduły przed startem nowej gry z GameCreator.
        /// Hook jest instalowany przez SaveLoad, żeby GameCreator nie referował SaveLoad asmdef.
        /// </summary>
        public static Action ResetRuntimeForNewGame { get; private set; }

        /// <summary>
        /// TD-021: show LoadingScreen overlay (dla LoadGameScreenUI podczas LoadAsync).
        /// String = title text. Wywoła LoadingScreenManager.Show.
        /// </summary>
        public static Action<string> ShowLoadingScreen { get; private set; }

        /// <summary>
        /// TD-021: hide LoadingScreen overlay.
        /// </summary>
        public static Action HideLoadingScreen { get; private set; }
    }
}
