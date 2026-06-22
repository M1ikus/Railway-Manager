using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RailwayManager.Core.Difficulty
{
    /// <summary>
    /// M13-13 / D33+D35: Statyczny serwis trzymający aktualną konfigurację trudności.
    ///
    /// Lifecycle:
    /// - Nowa gra: GameCreator wywołuje <see cref="ApplyNewGameConfig"/> z wybranym presetem
    ///   przed scene switch do Depot/Map.
    /// - Save: <see cref="WorldSavable"/> serializuje przez <see cref="ToJson"/> do bundle.
    /// - Load: <see cref="WorldSavable"/> deserializuje przez <see cref="ApplyFromJson"/>.
    /// - Mid-game: difficulty NIE może się zmienić (D33). Brak public API do zmiany pól runtime.
    ///
    /// Konsumenci (post-M13, w M-Balance):
    /// - <c>EconomyManager</c> czyta <see cref="Modifiers"/>.OperationalCostMultiplier przy obliczaniu kosztów dziennych
    /// - <c>BreakdownService</c> czyta BreakdownChanceMultiplier przy każdym risk roll
    /// - <c>PassengerManager</c> czyta PassengerDemandMultiplier przy generowaniu agentów
    /// - <c>PersonnelService</c> czyta SalaryMultiplier przy wypłacie pensji
    /// - itd.
    ///
    /// Default przed pierwszym ApplyNewGameConfig: Normal (wszystko 1.0).
    /// </summary>
    public static class DifficultyService
    {
        private static DifficultyPreset _preset = DifficultyPreset.Normal;
        private static DifficultyModifiers _modifiers = new DifficultyModifiers();

        /// <summary>Aktualnie wybrany preset (Easy/Normal/Hard/Realistic/Custom).</summary>
        public static DifficultyPreset Preset => _preset;

        /// <summary>Aktualne modyfikatory (per-save, niemodyfikowalne mid-game).
        /// Konsumenci czytają to bezpośrednio (np. <c>DifficultyService.Modifiers.SalaryMultiplier</c>).</summary>
        public static DifficultyModifiers Modifiers => _modifiers;

        // ── UX hints ────────────────────────────────
        // Bool flagi UX zależne od presetu (Easy = uproszczone). Mnożniki idą przez Modifiers,
        // hints idą przez te properties żeby nie zaśmiecać DifficultyModifiers POCO.

        /// <summary>Czy w UI pokazywać kolumnę "Przegląd" w FleetPanelUI. Easy = false
        /// (mniej kolumn = łatwiej zrozumieć dla nowego gracza), pozostałe presety = true.</summary>
        public static bool ShowInspectionColumnHint => _preset != DifficultyPreset.Easy;

        // ── New game ────────────────────────────────

        /// <summary>Ustawia config przy starcie nowej gry. Wywoływane przez GameCreator
        /// przed scene switch. Custom dostaje przekazane modyfikatory; pozostałe presety
        /// biorą wartości z katalogu (modifiersOverride ignorowany).</summary>
        public static void ApplyNewGameConfig(DifficultyPreset preset, DifficultyModifiers modifiersOverride = null)
        {
            _preset = preset;
            if (preset == DifficultyPreset.Custom && modifiersOverride != null)
            {
                _modifiers = modifiersOverride.Clone();
                Log.Info($"[DifficultyService] New game: Custom preset, modifiers applied (startBudget={_modifiers.StartBudgetMultiplier}x).");
            }
            else
            {
                _modifiers = DifficultyPresetCatalog.Get(preset);
                Log.Info($"[DifficultyService] New game: preset={preset}, modifiers from catalog.");
            }
        }

        // ── Save/Load (M13-7 / WorldSavable) ────────

        /// <summary>Serializuje aktualny config do JSON (preset + modifiers). Wstawiane do <see cref="WorldSavable"/>.</summary>
        public static JObject ToJson()
        {
            return new JObject
            {
                ["preset"]    = _preset.ToString(),
                ["modifiers"] = _modifiers.ToJson()
            };
        }

        /// <summary>Wczytuje config z JSON. Brak/błędy → fallback do Normal.</summary>
        public static void ApplyFromJson(JObject json)
        {
            if (json == null)
            {
                _preset = DifficultyPreset.Normal;
                _modifiers = new DifficultyModifiers();
                Log.Info("[DifficultyService] Load: brak danych — fallback Normal.");
                return;
            }

            string presetStr = json.Value<string>("preset") ?? "Normal";
            if (!System.Enum.TryParse(presetStr, out DifficultyPreset parsed))
                parsed = DifficultyPreset.Normal;
            _preset = parsed;

            // Custom czytamy zapisane modifiery; presety regenerujemy z katalogu (przyszłość: jeśli zmienimy
            // wartości presetu w patch'u, save'y starsze będą brały NOWE wartości — celowo dla balansu post-EA).
            if (_preset == DifficultyPreset.Custom)
            {
                _modifiers = DifficultyModifiers.FromJson(json.Value<JObject>("modifiers"));
                Log.Info($"[DifficultyService] Load: Custom preset, modifiers from save (startBudget={_modifiers.StartBudgetMultiplier}x).");
            }
            else
            {
                _modifiers = DifficultyPresetCatalog.Get(_preset);
                Log.Info($"[DifficultyService] Load: preset={_preset}, modifiers regenerated from catalog.");
            }
        }

        /// <summary>Reset do defaultów (Normal). Używane przy nowej grze bez konfiguracji
        /// lub przy InitializeDefault w SaveOrchestrator gdy brak modułu world w bundle.</summary>
        public static void ResetToDefault()
        {
            _preset = DifficultyPreset.Normal;
            _modifiers = new DifficultyModifiers();
        }
    }
}
