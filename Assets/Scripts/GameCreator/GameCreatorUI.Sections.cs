using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.GameCreator
{
    public partial class GameCreatorUI
    {
        // ═══════════════════════════════════════════
        //  SECTION POPULATE — dispatcher + 3 sekcje (Ogólnie / Rozgrywka SP / Serwer MP)
        // ═══════════════════════════════════════════

        private void PopulateSection(int idx)
        {
            CaptureActiveSectionState();

            foreach (Transform ch in _contentParent)
                Destroy(ch.gameObject);
            _activeLabels.Clear();

            _lblGameName = null;
            _lblAssistantName = null;
            _lblPauseOnStart = _lblAutosave = _lblAutosaveInt = _lblSeed = null;
            _lblSrvName = _lblMaxPlayers = _lblPassword = _lblVisibility = null;

            _fieldGameName = null;
            _fieldAssistantName = null;
            _togglePauseOnStart = null; _toggleAutosave = null; _ddAutosaveInterval = null; _fieldSeed = null;
            _fieldSrvName = null; _ddMaxPlayers = null; _fieldPassword = null; _ddVisibility = null;

            // M13-13: difficulty + game rules state reset (dropdown/sliders/toggles destroyed razem z _contentParent children)
            _ddDifficultyPreset = null;
            _customSlidersContainer = null;
            _lblPresetDescription = null;
            for (int i = 0; i < _customSliders.Length; i++) _customSliders[i] = null;
            _ruleToggles.Clear();

            switch (idx)
            {
                case 0: PopulateOgolnie();  break;
                case 1: if (_isMP) PopulateSerwer(); else PopulateRozgrywka(); break;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRT);
        }

        // ── Ogólnie ──────────────────────────────────

        private void PopulateOgolnie()
        {
            // TD-022: tylko GameName w Ogólnie. Difficulty + Funds USUNIĘTE jako duplikaty
            // z sekcji Rozgrywka (gdzie pełen DifficultyService preset selector + 10 modifierów +
            // 6 game rules toggles są źródłem prawdy). Wcześniej Ogólnie miało 3 dropdowny które
            // nigdy się nie aplikowały (label-only — TD-022).
            _lblGameName = InputRow("GameName", "game_creator.general.game_name",
                                    "game_creator.placeholder.game_name", false,
                                    out _fieldGameName);
            if (_fieldGameName != null)
            {
                _fieldGameName.characterLimit = MaxGameNameLength;
                _fieldGameName.text = _gameNameValue ?? "";
            }

            // M11 AS-1d: imię asystenta-zastępcy (AS-D8). Puste = default "pan Tadeusz".
            AddSpacer(8f);
            _lblAssistantName = InputRow("AssistantName", "game_creator.general.assistant_name",
                                         "game_creator.placeholder.assistant_name", false,
                                         out _fieldAssistantName);
            if (_fieldAssistantName != null)
            {
                _fieldAssistantName.characterLimit = MaxGameNameLength;
                _fieldAssistantName.text = _assistantNameValue ?? "";
            }
        }

        // ── Rozgrywka (SP only) ───────────────────────

        private void PopulateRozgrywka()
        {
            // Speed slider wyrzucony 2026-05-14 — TopBarUI steruje speedem dyskretnymi przyciskami x1/x5/x25/x150/x500.
            _lblPauseOnStart = ToggleRow ("PauseOnStart","game_creator.gameplay.pause_on_start", _pauseOnStartValue,
                                          out _togglePauseOnStart);
            AddSpacer(8f);
            _lblAutosave     = ToggleRow ("Autosave",    "game_creator.gameplay.autosave",     _autosaveEnabledValue,
                                          out _toggleAutosave);
            _lblAutosaveInt  = DropdownRow("AutosaveInt","game_creator.gameplay.autosave_interval",
                new[] { "game_creator.options.autosave_interval.5min", "game_creator.options.autosave_interval.10min", "game_creator.options.autosave_interval.15min", "game_creator.options.autosave_interval.30min" },
                out _ddAutosaveInterval);
            if (_ddAutosaveInterval != null)
            {
                _ddAutosaveInterval.SetValueWithoutNotify(Mathf.Clamp(_autosaveIntervalIndex, 0, 3));
                _ddAutosaveInterval.RefreshShownValue();
            }

            // Seed row — deterministic RNG dla debugowania crashów + MP-9 determinizm.
            _lblSeed = InputRow("Seed", "game_creator.gameplay.seed",
                                "game_creator.placeholder.seed", false,
                                out _fieldSeed);
            ConfigureSeedField();

            // M-Dispatch Faza 4b: polityka autonomicznego dispatchera mapy OSM (zmienialna in-game).
            AddSpacer(8f);
            _lblDispatchPolicy = DropdownRow("DispatchPolicy", "dispatch.policy.label",
                new[] { "dispatch.policy.off", "dispatch.policy.balanced", "dispatch.policy.punctuality" },
                out _ddDispatchPolicy);
            if (_ddDispatchPolicy != null)
            {
                _ddDispatchPolicy.SetValueWithoutNotify(Mathf.Clamp((int)_dispatchPolicyValue, 0, 2));
                _ddDispatchPolicy.RefreshShownValue();
            }

            // M13-13: difficulty selector + Custom editor + game rules toggles
            AddSpacer(16f);
            PopulateDifficultySection();
            ApplyStoredDifficultyToControls();
            AddSpacer(16f);
            PopulateGameRulesSection();
        }

        private void ConfigureSeedField()
        {
            if (_fieldSeed == null) return;
            _fieldSeed.contentType    = TMP_InputField.ContentType.IntegerNumber;
            _fieldSeed.characterLimit = MaxSeedDigits;
            _fieldSeed.text           = FormatSeedFieldText(_seedValue);
        }

        // ── Serwer (MP only) ─────────────────────────

        private void PopulateSerwer()
        {
            // TD-022: handles capture'owane do GameCreatorContext (M10 Mirror integration konsumuje
            // później). Aktualnie M10 nie ready — wartości zachowane w context, brak crash bo
            // ApplyOnStart loguje "MP placeholder".
            _lblSrvName    = InputRow   ("SrvName",    "game_creator.general.server_name",
                                         "game_creator.placeholder.server_name", false,
                                         out _fieldSrvName);
            if (_fieldSrvName != null)
            {
                _fieldSrvName.characterLimit = MaxServerNameLength;
                _fieldSrvName.text = _serverNameValue ?? "";
            }
            _lblMaxPlayers = DropdownRow("MaxPlayers", "game_creator.general.max_players",
                new[] { "game_creator.options.max_players.2", "game_creator.options.max_players.4", "game_creator.options.max_players.6", "game_creator.options.max_players.8", "game_creator.options.max_players.12", "game_creator.options.max_players.16" },
                out _ddMaxPlayers);
            if (_ddMaxPlayers != null)
            {
                _ddMaxPlayers.SetValueWithoutNotify(Mathf.Clamp(_serverMaxPlayersIndex, 0, 5));
                _ddMaxPlayers.RefreshShownValue();
            }
            AddSpacer(8f);
            _lblPassword   = InputRow   ("Password",   "game_creator.general.password", "", true,
                                         out _fieldPassword);
            if (_fieldPassword != null)
            {
                _fieldPassword.characterLimit = MaxServerPasswordLength;
                _fieldPassword.text = _serverPasswordValue ?? "";
            }
            _lblVisibility = DropdownRow("Visibility", "game_creator.general.visibility",
                new[] { "game_creator.options.visibility.public", "game_creator.options.visibility.private", "game_creator.options.visibility.hidden" },
                out _ddVisibility);
            if (_ddVisibility != null)
            {
                _ddVisibility.SetValueWithoutNotify(Mathf.Clamp(_serverVisibilityIndex, 0, 2));
                _ddVisibility.RefreshShownValue();
            }

            // Seed row — host MP musi ustawić seed żeby klienci mieli ten sam (MP-9 determinizm).
            AddSpacer(8f);
            _lblSeed = InputRow("Seed", "game_creator.gameplay.seed",
                                "game_creator.placeholder.seed", false,
                                out _fieldSeed);
            ConfigureSeedField();

            // 2026-05-14: host MP wybiera difficulty + game rules dla całej sesji (host-authoritative).
            // Wcześniej MP pomijał ApplyDifficultyAndRulesOnStart — Money/Rules zostawały z InitializeDefault.
            AddSpacer(16f);
            PopulateDifficultySection();
            ApplyStoredDifficultyToControls();
            AddSpacer(16f);
            PopulateGameRulesSection();
        }

    }
}
