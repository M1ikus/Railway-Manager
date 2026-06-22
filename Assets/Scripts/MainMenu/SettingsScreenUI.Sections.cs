using System.Collections.Generic;
using UnityEngine.InputSystem;
using RailwayManager.Core;
using RailwayManager.Core.Settings;
using RailwayManager.SharedUI.Localization;

namespace MainMenu
{
    /// <summary>
    /// SettingsScreenUI — partial: populate metody per zakładka (5 zakładek per D34).
    /// Każda metoda buduje kontrolki używając row builders z partial RowBuilders.cs.
    /// Wszystkie labele i opcje używają i18n keys (M13-4e rollout LocalizationService).
    /// </summary>
    public partial class SettingsScreenUI
    {
        // ─── Section 0: Sterowanie ─────────────────────

        private void PopulateControl()
        {
            HeadingRow("settings.tabs.controls");

            SliderRow("MouseSensitivity", "settings.controls.mouse_sensitivity",
                0.1f, 3.0f,
                () => _working.Control.MouseSensitivity,
                v  => _working.Control.MouseSensitivity = v);

            SliderRow("ScrollSensitivity", "settings.controls.scroll_sensitivity",
                0.1f, 3.0f,
                () => _working.Control.ScrollSensitivity,
                v  => _working.Control.ScrollSensitivity = v);

            SliderRow("PanSpeed", "settings.controls.pan_speed",
                0.1f, 3.0f,
                () => _working.Control.PanSpeed,
                v  => _working.Control.PanSpeed = v);

            AddSpacer(6f);

            ToggleRow("InvertCameraScroll", "settings.controls.invert_camera_scroll",
                () => _working.Control.InvertCameraScroll,
                v  => _working.Control.InvertCameraScroll = v);

            ToggleRow("InvertMouseY", "settings.controls.invert_mouse_y",
                () => _working.Control.InvertMouseY,
                v  => _working.Control.InvertMouseY = v);

            // ── Rebindowanie klawiszy (M13-2b) ──
            AddSpacer(10f);
            BuildRebindingsList();
        }

        // ─── Rebinding sub-section in Sterowanie ───────

        /// <summary>
        /// Buduje listę wszystkich rebindowalnych akcji z 7 action map'ów.
        /// Per binding: row z labelem + display string + "Zmień..." + reset.
        /// Skip composite parents (typu "WSAD 2DVector") — pokazujemy tylko parts (W, S, A, D).
        /// </summary>
        private void BuildRebindingsList()
        {
            if (_rebindActions == null)
            {
                Log.Warn("[SettingsScreenUI] BuildRebindingsList — _rebindActions is null");
                return;
            }

            HeadingRow("settings.headings.key_bindings");

            InfoRow("settings.info.key_rebind_restart");

            // Mapping action map name → i18n key dla heading
            var mapKeys = new Dictionary<string, string>
            {
                { "Camera.Depot",  "settings.headings.rebind_maps.camera_depot"  },
                { "Camera.Map",    "settings.headings.rebind_maps.camera_map"    },
                { "Tool.Build",    "settings.headings.rebind_maps.tool_build"    },
                { "Tool.Turnout",  "settings.headings.rebind_maps.tool_turnout"  },
                { "Vehicle",       "settings.headings.rebind_maps.vehicle"       },
                { "UI.Popup",      "settings.headings.rebind_maps.ui_popup"      },
                { "UI.PauseMenu",  "settings.headings.rebind_maps.ui_pause"      },
            };

            foreach (var map in _rebindActions.asset.actionMaps)
            {
                if (mapKeys.TryGetValue(map.name, out var headingKey))
                    HeadingRow(headingKey);
                else
                    HeadingRow(map.name, isI18nKey: false); // fallback dla nieznanych map (raw name)

                foreach (var action in map.actions)
                {
                    BuildActionBindings(action);
                }
            }
        }

        /// <summary>
        /// Buduje rows dla wszystkich bindings danej akcji (filtruje composite parents).
        /// </summary>
        private void BuildActionBindings(InputAction action)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var binding = action.bindings[i];

                // Skip composite parent (sam typ jak "2DVector" / "1DAxis" — bez własnego klawisza)
                if (binding.isComposite) continue;

                // For non-composite or composite parts: show row
                string partLabel = binding.isPartOfComposite ? binding.name : "";
                int bindingIdx = i; // capture for lambda

                RebindRow(
                    actionLabel: action.name,
                    bindingPartLabel: partLabel,
                    action: action,
                    bindingIndex: bindingIdx,
                    onRebindClick: () => OnRebindClick(action, bindingIdx),
                    onResetClick:  () => OnResetBindingClick(action, bindingIdx));
            }
        }

        // ─── Rebind handlers ───────────────────────────

        private void OnRebindClick(InputAction action, int bindingIndex)
        {
            if (_rebindModal == null || _rebindActions == null) return;

            string actionDisplay = action.name;
            var binding = action.bindings[bindingIndex];
            if (binding.isPartOfComposite)
                actionDisplay = $"{action.name} / {binding.name}";

            _rebindModal.Show(actionDisplay, action, bindingIndex,
                onComplete: success =>
                {
                    if (success)
                    {
                        // Save overrides do PlayerPrefs
                        RebindingService.SaveOverrides(_rebindActions);
                        MarkRebindingsDirty();

                        // Conflict detection (warning log, brak auto-swap w M13-2b)
                        DetectAndWarnConflicts(action, bindingIndex);
                    }
                    // Refresh UI żeby pokazać nowy display string
                    PopulateSection(_activeSection);
                    RefreshRowLabels();
                });
        }

        private void OnResetBindingClick(InputAction action, int bindingIndex)
        {
            if (_rebindActions == null) return;

            RebindingService.ResetBinding(action, bindingIndex);
            RebindingService.SaveOverrides(_rebindActions);
            MarkRebindingsDirty();

            PopulateSection(_activeSection);
            RefreshRowLabels();
            Log.Info($"[SettingsScreenUI] Reset binding {action.name}[{bindingIndex}]");
        }

        /// <summary>
        /// Sprawdza czy nowo przypisany klawisz nie duplikuje się w innej akcji
        /// w obrębie tej samej action map. Tylko warning log w M13-2b — auto-swap
        /// post-EA jeśli user'zy poproszą.
        /// </summary>
        private void DetectAndWarnConflicts(InputAction newAction, int newBindingIndex)
        {
            if (_rebindActions == null) return;

            var newBinding = newAction.bindings[newBindingIndex];
            string newPath = newBinding.effectivePath;
            if (string.IsNullOrEmpty(newPath)) return;

            // Same action map only (cross-map konflikty są OK — różne mapy enable/disable osobno)
            var sameMap = newAction.actionMap;
            if (sameMap == null) return;

            foreach (var otherAction in sameMap.actions)
            {
                for (int j = 0; j < otherAction.bindings.Count; j++)
                {
                    var otherBinding = otherAction.bindings[j];
                    if (otherBinding.isComposite) continue;

                    // Skip ourselves
                    if (otherAction == newAction && j == newBindingIndex) continue;

                    if (otherBinding.effectivePath == newPath)
                    {
                        Log.Warn($"[SettingsScreenUI] Konflikt klawiszy w mapie '{sameMap.name}': "
                            + $"'{newPath}' używany w '{newAction.name}' i '{otherAction.name}'. "
                            + "Auto-swap nie zaimplementowany w M13-2b — ręcznie popraw jeśli potrzeba.");
                    }
                }
            }
        }

        // ─── Section 1: Grafika (FPS limit przeniesione do Ogólne→Rozgrywka per D34) ────

        private void PopulateGraphics()
        {
            HeadingRow("settings.tabs.graphics");

            DropdownRow("Window", "settings.graphics.window_mode",
                new[] { "settings.options.window.fullscreen", "settings.options.window.borderless", "settings.options.window.windowed" },
                () => (int)_working.Graphics.Window,
                v  => _working.Graphics.Window = (WindowMode)v);

            DropdownRow("VSync", "settings.graphics.vsync",
                new[] { "settings.options.vsync.off", "settings.options.vsync.on" },
                () => _working.Graphics.VSync ? 1 : 0,
                v  => _working.Graphics.VSync = (v == 1));

            AddSpacer(6f);

            DropdownRow("Quality", "settings.graphics.quality_preset",
                new[] { "settings.options.quality.low", "settings.options.quality.medium", "settings.options.quality.high", "settings.options.quality.ultra" },
                () => (int)_working.Graphics.Quality,
                v  => _working.Graphics.Quality = (QualityPreset)v);

            DropdownRow("Shadows", "settings.graphics.shadows",
                new[] { "settings.options.shadows.off", "settings.options.shadows.low", "settings.options.shadows.medium", "settings.options.shadows.high" },
                () => (int)_working.Graphics.Shadows,
                v  => _working.Graphics.Shadows = (ShadowLevel)v);

            DropdownRow("Textures", "settings.graphics.textures",
                new[] { "settings.options.textures.low", "settings.options.textures.medium", "settings.options.textures.high", "settings.options.textures.ultra" },
                () => (int)_working.Graphics.Textures,
                v  => _working.Graphics.Textures = (TextureLevel)v);

            SliderRow("LodBias", "settings.graphics.lod_distance",
                0.5f, 2.0f,
                () => _working.Graphics.LodBias,
                v  => _working.Graphics.LodBias = v);

            DropdownRow("AntiAliasing", "settings.graphics.antialiasing",
                new[] { "settings.options.antialiasing.off", "settings.options.antialiasing.fxaa", "settings.options.antialiasing.smaa", "settings.options.antialiasing.taa" },
                () => (int)_working.Graphics.AntiAliasing,
                v  => _working.Graphics.AntiAliasing = (AntiAliasing)v);

            AddSpacer(10f);
            ToggleRow("PostProcessing", "settings.graphics.post_processing",
                () => _working.Graphics.PostProcessing,
                v  => _working.Graphics.PostProcessing = v);
        }

        // ─── Section 2: Dźwięk ─────────────────────────

        private void PopulateAudio()
        {
            HeadingRow("settings.tabs.audio");

            SliderRow("MasterVolume", "settings.audio.master_volume",
                0f, 1f,
                () => _working.Audio.MasterVolume,
                v  => _working.Audio.MasterVolume = v);

            SliderRow("MusicVolume", "settings.audio.music_volume",
                0f, 1f,
                () => _working.Audio.MusicVolume,
                v  => _working.Audio.MusicVolume = v);

            AddSpacer(6f);

            SliderRow("SfxVolume", "settings.audio.sfx_volume",
                0f, 1f,
                () => _working.Audio.SfxVolume,
                v  => _working.Audio.SfxVolume = v);

            SliderRow("VoiceVolume", "settings.audio.voice_volume",
                0f, 1f,
                () => _working.Audio.VoiceVolume,
                v  => _working.Audio.VoiceVolume = v);

            ToggleRow("MuteWhenUnfocused", "settings.audio.mute_when_unfocused",
                () => _working.Audio.MuteWhenUnfocused,
                v  => _working.Audio.MuteWhenUnfocused = v);

            AddSpacer(8f);
            InfoRow("settings.info.audio_mixer_pending");
        }

        // ─── Section 3: Język (osobna zakładka per D34) ────

        private void PopulateLanguage()
        {
            HeadingRow("settings.tabs.language");

            DropdownRow("LanguagePreference", "settings.language.preference",
                new[] {
                    "settings.options.language.auto_steam",
                    "settings.options.language.pl",
                    "settings.options.language.en",
                    "settings.options.language.de",
                    "settings.options.language.cz",
                    "settings.options.language.jp",
                    "settings.options.language.ru_alpha",
                    "settings.options.language.uk_alpha"
                },
                () => (int)_working.Language.Preference,
                v  => _working.Language.Preference = (LanguagePreference)v);

            // M-Economy Faza 2b: waluta wyświetlania kwot (PLN/EUR/USD/CZK). Index dropdownu == wartość
            // enuma Currency (PLN=0/EUR=1/USD=2/CZK=3). Baza gry zawsze PLN — to tylko prezentacja.
            DropdownRow("DisplayCurrency", "settings.language.currency",
                new[] {
                    "settings.options.currency.pln",
                    "settings.options.currency.eur",
                    "settings.options.currency.usd",
                    "settings.options.currency.czk"
                },
                () => (int)_working.Language.DisplayCurrency,
                v  => _working.Language.DisplayCurrency = (Currency)v);

            AddSpacer(8f);
            InfoRow("settings.info.i18n_partial");
            InfoRow("settings.info.language_autodetect");
        }

        // ─── Section 4: Ogólne (2 podsekcje: Rozgrywka / Interfejs) ────

        private void PopulateGeneral()
        {
            // ── Rozgrywka ──
            HeadingRow("settings.headings.gameplay");

            DropdownRow("DefaultTimeSpeed", "settings.general.default_time_speed",
                new[] { "settings.options.time_speed.x1", "settings.options.time_speed.x5", "settings.options.time_speed.x25", "settings.options.time_speed.x150", "settings.options.time_speed.x500" },
                () => (int)_working.General.Gameplay.DefaultTimeSpeed,
                v  => _working.General.Gameplay.DefaultTimeSpeed = (TimeSpeed)v);

            // FPS limit przeniesione z Grafiki per D34 (perf/feel, nie estetyka)
            DropdownRow("FpsLimit", "settings.general.fps_limit",
                new[] { "settings.options.fps_limit.unlimited", "settings.options.fps_limit.30", "settings.options.fps_limit.60", "settings.options.fps_limit.120", "settings.options.fps_limit.144" },
                () => FpsToIndex(_working.General.Gameplay.FpsLimit),
                v  => _working.General.Gameplay.FpsLimit = IndexToFps(v));

            ToggleRow("AutoPauseBreakdown", "settings.general.auto_pause.breakdown",
                () => _working.General.Gameplay.AutoPauseOnBreakdown,
                v  => _working.General.Gameplay.AutoPauseOnBreakdown = v);

            ToggleRow("AutoPauseInfra", "settings.general.auto_pause.infrastructure",
                () => _working.General.Gameplay.AutoPauseOnInfrastructure,
                v  => _working.General.Gameplay.AutoPauseOnInfrastructure = v);

            ToggleRow("AutoPauseDecision", "settings.general.auto_pause.critical_decision",
                () => _working.General.Gameplay.AutoPauseOnCriticalDecision,
                v  => _working.General.Gameplay.AutoPauseOnCriticalDecision = v);

            ToggleRow("AutoPauseCollision", "settings.general.auto_pause.collision",
                () => _working.General.Gameplay.AutoPauseOnCollision,
                v  => _working.General.Gameplay.AutoPauseOnCollision = v);

            ToggleRow("ShowTutorial", "settings.general.show_tutorial",
                () => _working.General.Gameplay.ShowTutorialOnNewGame,
                v  => _working.General.Gameplay.ShowTutorialOnNewGame = v);

            // M-Dispatch Faza 4b: polityka autonomicznego dispatchera mapy OSM. Per-gra (GameState),
            // NIE aplikacyjna — pokazujemy tylko in-game (poza MainMenu). Efekt natychmiastowy (jak
            // rebindy), zapisywana z grą przez WorldSavable. Index dropdownu == wartość enuma.
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "MainMenu")
            {
                DropdownRow("DispatchPolicy", "dispatch.policy.label",
                    new[] { "dispatch.policy.off", "dispatch.policy.balanced", "dispatch.policy.punctuality" },
                    () => (int)GameState.MapDispatchPolicy,
                    v  => GameState.MapDispatchPolicy = (DispatchPolicy)v);
            }

            AddSpacer(6f);

            DropdownRow("EventFrequency", "settings.general.random_event_frequency",
                new[] { "settings.options.event_frequency.off", "settings.options.event_frequency.rare", "settings.options.event_frequency.normal", "settings.options.event_frequency.often" },
                () => (int)_working.General.Gameplay.RandomEventFrequency,
                v  => _working.General.Gameplay.RandomEventFrequency = (EventFrequency)v);

            ToggleRow("NotifyDelays", "settings.general.notify.delays",
                () => _working.General.Gameplay.NotifyDelays,
                v  => _working.General.Gameplay.NotifyDelays = v);

            ToggleRow("NotifyMoney", "settings.general.notify.money",
                () => _working.General.Gameplay.NotifyMoney,
                v  => _working.General.Gameplay.NotifyMoney = v);

            ToggleRow("NotifyPersonnel", "settings.general.notify.personnel",
                () => _working.General.Gameplay.NotifyPersonnel,
                v  => _working.General.Gameplay.NotifyPersonnel = v);

            ToggleRow("NotifyOther", "settings.general.notify.other",
                () => _working.General.Gameplay.NotifyOther,
                v  => _working.General.Gameplay.NotifyOther = v);

            AddSpacer(6f);

            IntSliderRow("MaxUndos", "settings.general.max_undos",
                UndoSettings.MIN, UndoSettings.MAX,
                () => _working.General.Gameplay.MaxUndos,
                v  => _working.General.Gameplay.MaxUndos = v);

            // ── Interfejs ──
            AddSpacer(12f);
            HeadingRow("settings.headings.interface");

            // UI Scale: tylko 100% w EA — dropdown z jedną opcją był UX-marnotrawstwem
            // (no-op setter, klik na strzałkę pokazywał listę z jednym wpisem). Zastąpione
            // info row'em. Pełne skalowanie wejdzie w M-UIPolish 1.0 (responsive scaling).
            InfoRow("settings.info.ui_scale_pending");

            SliderRow("TooltipsDelay", "settings.interface_section.tooltips_delay",
                0f, 2f,
                () => _working.General.Interface.TooltipsDelaySeconds,
                v  => _working.General.Interface.TooltipsDelaySeconds = v);

            ToggleRow("ShowKeybindsInTooltips", "settings.interface_section.show_keybinds_in_tooltips",
                () => _working.General.Interface.ShowKeybindsInTooltips,
                v  => _working.General.Interface.ShowKeybindsInTooltips = v);

            DropdownRow("ColorBlind", "settings.interface_section.color_blind_mode",
                new[] { "settings.options.color_blind.none", "settings.options.color_blind.protanopia", "settings.options.color_blind.deuteranopia", "settings.options.color_blind.tritanopia" },
                () => (int)_working.General.Interface.ColorBlindMode,
                v  => _working.General.Interface.ColorBlindMode = (ColorBlindMode)v);

            InfoRow("settings.info.color_blind_pending");

            // ── Asystent (M11 AS-7) — per-gra (AssistantState), tylko in-game (wzorzec
            // DispatchPolicy). Efekt natychmiastowy, poza Apply/Cancel; skip persistuje
            // z save'em (SharedUISavable), restart = reset drivera, NIE SuggestionMemory.
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "MainMenu")
            {
                AddSpacer(12f);
                HeadingRow("settings.headings.assistant");
                InfoRow("settings.assistant.info");

                ToggleRow("AssistantSkipOnboarding", "settings.assistant.skip_onboarding",
                    () => RailwayManager.SharedUI.Assistant.AssistantState.OnboardingSkipped,
                    v  => RailwayManager.SharedUI.Assistant.AssistantState.OnboardingSkipped = v);

                ActionRow("AssistantRestartOnboarding", "settings.assistant.restart_onboarding",
                    "settings.assistant.restart_button",
                    () => RailwayManager.SharedUI.Assistant.AssistantState.RestartOnboarding());
            }
        }

        // ─── helpers FPS ↔ index ────────────────────────
        // Kolejność musi pokrywać się z DropdownRow optionKeys w PopulateGeneral.
        // 0 = unlimited (vsync) — semantycznie value w SettingsData.
        private static readonly int[] FpsOptions = { 0, 30, 60, 120, 144 };

        private static int FpsToIndex(int fps)
        {
            int idx = System.Array.IndexOf(FpsOptions, fps);
            return idx < 0 ? 0 : idx;
        }

        private static int IndexToFps(int idx) =>
            (idx >= 0 && idx < FpsOptions.Length) ? FpsOptions[idx] : 0;
    }
}
