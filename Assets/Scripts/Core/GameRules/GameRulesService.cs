using Newtonsoft.Json.Linq;

namespace RailwayManager.Core.GameRules
{
    /// <summary>
    /// M13-13 / D36: Statyczna fasada do <see cref="GameRulesConfig"/>.
    /// Per-save, niemodyfikowalna mid-game (D36).
    ///
    /// Lifecycle (analogiczny do <c>DifficultyService</c>):
    /// - Nowa gra: GameCreator wywołuje <see cref="ApplyNewGameConfig"/>.
    /// - Save: <see cref="WorldSavable"/> serializuje przez <see cref="ToJson"/>.
    /// - Load: <see cref="WorldSavable"/> deserializuje przez <see cref="ApplyFromJson"/>.
    ///
    /// Sprawdzanie w runtime (post-M13, w M-Balance):
    /// <code>
    /// if (GameRulesService.IsEnabled(GameRule.VehicleBreakdowns))
    ///     RollBreakdownChance(...);
    /// </code>
    ///
    /// Default przed pierwszym ApplyNewGameConfig: pusty config = wszystko ON.
    /// </summary>
    public static class GameRulesService
    {
        private static GameRulesConfig _config = new GameRulesConfig();

        /// <summary>Aktualna konfiguracja (read-only access przez Config.IsEnabled / Config.All).</summary>
        public static GameRulesConfig Config => _config;

        /// <summary>Czy reguła jest włączona (skrót zamiast Config.IsEnabled). Pierwszy port wywołań
        /// w gameplay code — stąd statyczna fasada.</summary>
        public static bool IsEnabled(GameRule rule) => _config.IsEnabled(rule);

        // ── New game ────────────────────────────────

        /// <summary>Ustawia config przy starcie nowej gry (wywoływane przez GameCreator).
        /// Nieprzekazany config → reset do defaultów (wszystko ON).</summary>
        public static void ApplyNewGameConfig(GameRulesConfig config)
        {
            _config = config ?? new GameRulesConfig();
            int offCount = 0;
            foreach (var kv in _config.All) if (!kv.Value) offCount++;
            Log.Info($"[GameRulesService] New game: {offCount} reguł(a) wyłączonych.");
        }

        // ── Save/Load (M13-7 / WorldSavable) ────────

        /// <summary>Serializuje aktualny config do JSON. Wstawiane do <see cref="WorldSavable"/>.</summary>
        public static JObject ToJson() => _config.ToJson();

        /// <summary>Wczytuje config z JSON. Brak → wszystko ON.</summary>
        public static void ApplyFromJson(JObject json)
        {
            _config = GameRulesConfig.FromJson(json);
            int offCount = 0;
            foreach (var kv in _config.All) if (!kv.Value) offCount++;
            Log.Info($"[GameRulesService] Load: {offCount} reguł(a) wyłączonych.");
        }

        /// <summary>Reset do defaultów (wszystko ON). InitializeDefault w SaveOrchestrator.</summary>
        public static void ResetToDefault()
        {
            _config = new GameRulesConfig();
        }
    }
}
