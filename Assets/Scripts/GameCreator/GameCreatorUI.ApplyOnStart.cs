using UnityEngine;
using MainMenu;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;
using RailwayManager.Core.GameRules;
// 2026-05-10: usunięte `using RailwayManager.SaveLoad;` — cyclic dependency fix.
// GameCreator nie referuje SaveLoad asmdef. Hook pattern via SaveActionsHook (Core).

namespace RailwayManager.GameCreator
{
    public partial class GameCreatorUI
    {
        // ═════════════════════════════════════════════
        //  TD-022 — APPLY ON START
        //  ═════════════════════════════════════════════
        //
        //  Wcześniej: tylko ApplyDifficultyAndRulesOnStart aplikował (Difficulty + Rules + Money).
        //  Reszta kontrolek (GameName, Speed, PauseOnStart, Autosave, Server config) była
        //  label-only — wartości giną przy LoadScene("Depot"). TD-022 fix:
        //  - GameName → GameState.DepotName
        //  - Speed slider → GameState.TimeScale (initial speed; gracz może zmienić w grze)
        //  - PauseOnStart toggle → GameState.IsPaused (Depot scene Awake stworzy pausowane)
        //  - Autosave toggle → AutoSaveService.IsAutoSaveEnabled
        //  - Autosave interval dropdown (5/10/15/30 min) → AutoSaveIntervalGameSec
        //  - Server fields (MP only) → GameCreatorContext.Server* (M10 Mirror konsumuje później)
        //
        //  Wzorzec: każda kontrolka ma null-check (handle może być null jeśli sekcja nie była
        //  populated). Difficulty/Rules + Money apply pozostaje bez zmian (delegate do
        //  istniejącego ApplyDifficultyAndRulesOnStart).
        //
        // ═════════════════════════════════════════════

        /// <summary>TD-022: aggregate apply method — wywoływane przez StartBtn click handler.</summary>
        private void ApplyOnStart()
        {
            CaptureActiveSectionState();
            ResetRuntimeForNewGame();
            // Seed pierwszy po Reset — WorldSavable.InitializeDefault ustawił Seed=0,
            // tutaj nadpisujemy user'em (RandomRegistry.GetRng używa GameState.Seed lazy
            // przy pierwszym wywołaniu, więc ustawienie przed Apply* jest wystarczające).
            ApplySeedOnStart();
            ApplyGeneralOnStart();          // GameName
            ApplyGameplayOnStart();         // Pause / Autosave (SP only)
            ApplyDispatchPolicyOnStart();   // M-Dispatch Faza 4b (SP i MP — host-authoritative)
            // 2026-05-14: difficulty + rules aplikujemy także w MP (host-authoritative).
            // PopulateSerwer renderuje Difficulty + GameRules sekcję pod Server fields.
            ApplyDifficultyAndRulesOnStart(); // Difficulty preset + 10 modifiers + 6 rules + Money (M13-13)
            ApplyServerOnStart();            // MP fields → GameCreatorContext (M10 placeholder)

            Log.Info("[GameCreatorUI] ApplyOnStart done — GameState ready for Depot scene transition.");
        }

        private void ApplySeedOnStart()
        {
            GameState.Seed = _seedValue;
            Log.Info($"[GameCreatorUI] GameState.Seed = {GameState.Seed} " +
                     $"({(_seedValue == 0 ? "deterministyczne baseline" : "user-chosen")})");
        }

        private void InitializeFormState()
        {
            _gameNameValue = string.IsNullOrWhiteSpace(GameState.DepotName) ? "" : GameState.DepotName;
            _assistantNameValue = ""; // puste = zostaje default po reset modułów (AS-1d)
            _pauseOnStartValue = false;
            _autosaveEnabledValue = true;
            _autosaveIntervalIndex = 0;
            _seedValue = 0; // 0 = deterministyczne baseline (RandomRegistry start state)
            _dispatchPolicyValue = DispatchPolicy.Balanced; // M-Dispatch Faza 4b default
            _difficultyPresetValue = DifficultyPreset.Normal;
            _difficultyModifiersValue = DifficultyPresetCatalog.Get(_difficultyPresetValue);
            _gameRuleValues.Clear();
            foreach (var def in _ruleDefs)
                _gameRuleValues[def.rule] = def.defaultOn;
            _serverNameValue = GameCreatorContext.ServerName ?? "";
            _serverMaxPlayersIndex = CountToMaxPlayersDropdownIndex(GameCreatorContext.ServerMaxPlayers);
            _serverPasswordValue = GameCreatorContext.ServerPassword ?? "";
            _serverVisibilityIndex = (int)GameCreatorContext.ServerVisibilityValue;
        }

        private void CaptureActiveSectionState()
        {
            if (_fieldGameName != null)
                _gameNameValue = _fieldGameName.text ?? "";
            if (_fieldAssistantName != null)
                _assistantNameValue = _fieldAssistantName.text ?? "";

            if (_togglePauseOnStart != null)
                _pauseOnStartValue = _togglePauseOnStart.isOn;
            if (_toggleAutosave != null)
                _autosaveEnabledValue = _toggleAutosave.isOn;
            if (_ddAutosaveInterval != null)
                _autosaveIntervalIndex = _ddAutosaveInterval.value;
            if (_fieldSeed != null)
            {
                // Puste pole = seed 0 (deterministyczne baseline). int.TryParse skip = zostawiamy
                // poprzednią wartość (defensywne: gdy gracz wpisał śmieci, nie chcemy crash'a).
                if (string.IsNullOrWhiteSpace(_fieldSeed.text))
                    _seedValue = 0;
                else if (int.TryParse(_fieldSeed.text, out var s))
                    _seedValue = s;
            }

            if (_ddDispatchPolicy != null)
                _dispatchPolicyValue = (DispatchPolicy)Mathf.Clamp(_ddDispatchPolicy.value, 0, 2);

            if (_ddDifficultyPreset != null)
            {
                _difficultyPresetValue = DropdownIndexToPreset(_ddDifficultyPreset.value);
                _difficultyModifiersValue = ReadDifficultyModifiersFromControls();
            }

            foreach (var def in _ruleDefs)
            {
                if (_ruleToggles.TryGetValue(def.rule, out var toggle) && toggle != null)
                    _gameRuleValues[def.rule] = toggle.isOn;
            }

            if (_fieldSrvName != null)
                _serverNameValue = _fieldSrvName.text ?? "";
            if (_ddMaxPlayers != null)
                _serverMaxPlayersIndex = _ddMaxPlayers.value;
            if (_fieldPassword != null)
                _serverPasswordValue = _fieldPassword.text ?? "";
            if (_ddVisibility != null)
                _serverVisibilityIndex = _ddVisibility.value;
        }

        private DifficultyModifiers ReadDifficultyModifiersFromControls()
        {
            var mods = new DifficultyModifiers();
            for (int i = 0; i < _modifierDefs.Length; i++)
                _modifierDefs[i].setter(mods, ReadModifierValue(i));
            return mods;
        }

        private void ResetRuntimeForNewGame()
        {
            if (SaveActionsHook.ResetRuntimeForNewGame != null)
            {
                SaveActionsHook.ResetRuntimeForNewGame.Invoke();
                return;
            }

            Log.Warn("[GameCreatorUI] SaveActionsHook.ResetRuntimeForNewGame = null - runtime state not reset before new game");
        }

        private void ApplyDispatchPolicyOnStart()
        {
            // Polityka dispatchera mapy OSM — żyje w GameState (Core), persistowana przez WorldSavable,
            // zmienialna potem w ustawieniach ogólnych. Kontrolka jest w sekcji Rozgrywka (SP);
            // w MP zostaje default Balanced (dropdown nie renderowany → _dispatchPolicyValue domyślne).
            GameState.MapDispatchPolicy = _dispatchPolicyValue;
            Log.Info($"[GameCreatorUI] GameState.MapDispatchPolicy = {GameState.MapDispatchPolicy}");
        }

        private void ApplyGeneralOnStart()
        {
            if (!string.IsNullOrWhiteSpace(_gameNameValue))
            {
                GameState.DepotName = _gameNameValue.Trim();
                Log.Info($"[GameCreatorUI] GameState.DepotName = '{GameState.DepotName}'");
            }

            // M11 AS-1d: imię asystenta (AS-D8). Puste pole = default ("pan Tadeusz")
            // ustawiony przez AssistantState.ResetForNewGame w resecie modułów.
            if (!string.IsNullOrWhiteSpace(_assistantNameValue))
            {
                SharedUI.Assistant.AssistantState.DisplayName = _assistantNameValue.Trim();
                Log.Info($"[GameCreatorUI] AssistantState.DisplayName = '{SharedUI.Assistant.AssistantState.DisplayName}'");
            }
        }

        private void ApplyGameplayOnStart()
        {
            // Sekcja Rozgrywka tylko dla SP — w MP toggle*/dropdown* zostają null.
            // Speed wyrzucony 2026-05-14 — GameState.TimeScale zostaje default 1f (z InitializeDefault).
            // TopBarUI steruje speedem (1/5/25/150/500x) po wejściu do Depot.
            if (_isMP) return;

            GameState.IsPaused = _pauseOnStartValue;
            if (GameState.IsPaused)
                Log.Info("[GameCreatorUI] GameState.IsPaused = true (gracz wybral pauseOnStart)");

            // Auto-save config via Core hooks (cyclic dep fix 2026-05-10).
            // GameCreator NIE referuje SaveLoad asmdef — używamy SaveActionsHook delegates.
            if (SaveActionsHook.SetAutoSaveEnabled != null)
            {
                SaveActionsHook.SetAutoSaveEnabled.Invoke(_autosaveEnabledValue);
                Log.Info($"[GameCreatorUI] AutoSave enabled = {_autosaveEnabledValue}");
            }
            else
            {
                Log.Warn("[GameCreatorUI] SaveActionsHook.SetAutoSaveEnabled = null — autosave enabled nie zaaplikowane (bootstrap problem)");
            }

            if (SaveActionsHook.SetAutoSaveIntervalSec != null)
            {
                // Dropdown index 0..3 → 5/10/15/30 min
                int intervalMin = AutosaveDropdownIndexToMinutes(_autosaveIntervalIndex);
                SaveActionsHook.SetAutoSaveIntervalSec.Invoke(intervalMin * 60f);
                Log.Info($"[GameCreatorUI] AutoSaveIntervalGameSec = {intervalMin * 60}s ({intervalMin} min)");
            }
            else
            {
                Log.Warn("[GameCreatorUI] SaveActionsHook.SetAutoSaveIntervalSec = null — autosave interval nie zaaplikowane");
            }
        }

        private void ApplyServerOnStart()
        {
            // MP fields tylko jak _isMP (sekcja Server była populated)
            if (!_isMP) return;

            GameCreatorContext.ServerName = _serverNameValue?.Trim() ?? "";
            GameCreatorContext.ServerMaxPlayers = MaxPlayersDropdownIndexToCount(_serverMaxPlayersIndex);
            GameCreatorContext.ServerPassword = _serverPasswordValue ?? "";
            // Clamp do zakresu enum'a żeby rozszerzenie dropdown'a o nowe opcje (bez aktualizacji enum'a)
            // nie skutkowało nielegalną wartością — fallback na Public.
            int visibilityIdx = Mathf.Clamp(_serverVisibilityIndex, 0, 2);
            GameCreatorContext.ServerVisibilityValue = (GameCreatorContext.ServerVisibility)visibilityIdx;

            Log.Info($"[GameCreatorUI] MP placeholder zachowany w GameCreatorContext: " +
                     $"name='{GameCreatorContext.ServerName}', maxPlayers={GameCreatorContext.ServerMaxPlayers}, " +
                     $"hasPassword={!string.IsNullOrEmpty(GameCreatorContext.ServerPassword)}, " +
                     $"visibility={GameCreatorContext.ServerVisibilityValue}. " +
                     $"M10 Mirror integration konsumuje przy host bootstrap.");
        }

        // ── Helper conversions ──
        // `internal static` zamiast `private` — wymagane dla GameCreatorSmokeTest który testuje
        // mapping invariants. Konwencja smoke testów (CoreSmokeTest/DepotSmokeTest) używa publicznych
        // API; tutaj `internal` bo helpers żyją w tym samym asmdef.

        /// <summary>Dropdown idx (0..3) → autosave interval w minutach (5/10/15/30).</summary>
        internal static int AutosaveDropdownIndexToMinutes(int idx)
        {
            switch (idx)
            {
                case 0: return 5;
                case 1: return 10;
                case 2: return 15;
                case 3: return 30;
                default: return 5;
            }
        }

        /// <summary>Dropdown idx (0..5) → server max players (2/4/6/8/12/16).</summary>
        internal static int MaxPlayersDropdownIndexToCount(int idx)
        {
            switch (idx)
            {
                case 0: return 2;
                case 1: return 4;
                case 2: return 6;
                case 3: return 8;
                case 4: return 12;
                case 5: return 16;
                default: return 4;
            }
        }

        internal static int CountToMaxPlayersDropdownIndex(int count)
        {
            switch (count)
            {
                case 2: return 0;
                case 4: return 1;
                case 6: return 2;
                case 8: return 3;
                case 12: return 4;
                case 16: return 5;
                default: return 1;
            }
        }

        /// <summary>Logika formatowania pola seed: 0 = pusty placeholder (UX), inne = liczba.</summary>
        internal static string FormatSeedFieldText(int seedValue) =>
            seedValue == 0 ? "" : seedValue.ToString();
    }
}
