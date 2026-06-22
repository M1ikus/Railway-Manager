using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RailwayManager.Core.GameRules
{
    /// <summary>
    /// M13-13 / D36: Konfiguracja game rules — Dictionary&lt;GameRule, bool&gt; z serializacją.
    ///
    /// Brak reguły w configu = ON (wszystkie features pełne by default).
    /// Save format: lista nazw reguł które są OFF (mniej miejsca + forward-compat: nowe reguły
    /// dodane w patch'u będą domyślnie ON dla starych save'ów).
    ///
    /// Gracz konfiguruje przez UI w GameCreator (sekcja "Reguły gry"), config zapisywany
    /// per-save w world.json bundle. UI w GameCreator → M-Balance.
    /// </summary>
    public class GameRulesConfig
    {
        // Dictionary: GameRule → bool (true = ON). Brak klucza = ON (default).
        private readonly Dictionary<GameRule, bool> _rules = new Dictionary<GameRule, bool>();

        /// <summary>Czy reguła jest włączona. Brak w słowniku → true (default ON).</summary>
        public bool IsEnabled(GameRule rule)
        {
            return !_rules.TryGetValue(rule, out var enabled) || enabled;
        }

        /// <summary>Ustawia regułę (used przez GameCreator UI). Mid-game NIE wywoływać —
        /// difficulty/rules są niemodyfikowalne po starcie gry (D33+D36).</summary>
        public void Set(GameRule rule, bool enabled)
        {
            _rules[rule] = enabled;
        }

        /// <summary>Lista wszystkich aktualnie ustawionych reguł (do iteracji w UI).</summary>
        public IReadOnlyDictionary<GameRule, bool> All => _rules;

        // ── Serializacja ────────────────────────────

        /// <summary>Serializuje do JSON jako tablica nazw OFF reguł (kompaktowo + forward-compat).</summary>
        public JObject ToJson()
        {
            var disabled = new JArray();
            foreach (var kv in _rules)
                if (!kv.Value)
                    disabled.Add(kv.Key.ToString());
            return new JObject
            {
                ["disabled"] = disabled
            };
        }

        /// <summary>Wczytuje z JSON. Tablica "disabled" = lista nazw GameRule które są OFF.
        /// Nieznane nazwy (forward-compat: save z nowszej wersji z ENUM-em rozszerzonym) są ignorowane.</summary>
        public static GameRulesConfig FromJson(JObject json)
        {
            var cfg = new GameRulesConfig();
            if (json == null) return cfg;

            var disabled = json.Value<JArray>("disabled");
            if (disabled == null) return cfg;

            foreach (var token in disabled)
            {
                var name = token?.ToString();
                if (string.IsNullOrEmpty(name)) continue;
                if (Enum.TryParse(name, out GameRule rule))
                    cfg.Set(rule, false);
                // unknown rule name → ignore (forward compat)
            }
            return cfg;
        }
    }
}
