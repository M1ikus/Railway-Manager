using System;
using System.Collections.Generic;

namespace RailwayManager.Core.Assistant
{
    /// <summary>
    /// M11 AS-1b: rejestr reguł next-step monitora. Moduły rejestrują reguły w swoich
    /// bootstrapach (jak capability w AssistantCapabilityRegistry), Clear() przy nowej grze.
    ///
    /// Kolejność rejestracji jest ZACHOWANA (lista, nie dict-values) — remis priorytetów
    /// w NextStepMonitor rozstrzyga „pierwsza zarejestrowana wygrywa" (determinizm, MP-9 kultura).
    /// </summary>
    public static class AssistantRuleRegistry
    {
        private static readonly List<AssistantRule> _ordered = new List<AssistantRule>();
        private static readonly Dictionary<string, AssistantRule> _byId = new Dictionary<string, AssistantRule>();

        /// <summary>Emitowane po każdej zmianie zawartości (Register/Unregister/Clear).</summary>
        public static event Action OnChanged;

        public static int Count => _ordered.Count;

        /// <summary>Widok w kolejności rejestracji (zero-alokacji). NIE mutować podczas iteracji.</summary>
        public static IReadOnlyList<AssistantRule> All => _ordered;

        /// <summary>False + Log.Warn przy null / pustym id / null predicate / duplikacie.</summary>
        public static bool Register(AssistantRule rule)
        {
            if (rule == null)
            {
                Log.Warn("[AssistantRules] Register(null) — ignored");
                return false;
            }
            if (string.IsNullOrEmpty(rule.id))
            {
                Log.Warn("[AssistantRules] Register z pustym id — ignored");
                return false;
            }
            if (rule.isActive == null)
            {
                Log.Warn($"[AssistantRules] Reguła '{rule.id}' bez predykatu isActive — ignored");
                return false;
            }
            if (_byId.ContainsKey(rule.id))
            {
                Log.Warn($"[AssistantRules] Duplikat reguły '{rule.id}' — pierwsza rejestracja wygrywa");
                return false;
            }

            _ordered.Add(rule);
            _byId[rule.id] = rule;
            Log.Info($"[AssistantRules] Registered '{rule.id}' ({rule.kind}, prio={rule.priority}) — total {_ordered.Count}");
            OnChanged?.Invoke();
            return true;
        }

        public static bool Unregister(string id)
        {
            if (string.IsNullOrEmpty(id) || !_byId.TryGetValue(id, out var rule)) return false;
            _byId.Remove(id);
            _ordered.Remove(rule);
            Log.Info($"[AssistantRules] Unregistered '{id}' — total {_ordered.Count}");
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Null gdy nieznane id.</summary>
        public static AssistantRule Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var rule) ? rule : null;
        }

        /// <summary>Nowa gra / powrót do MainMenu / testy.</summary>
        public static void Clear()
        {
            if (_ordered.Count == 0) return;
            int n = _ordered.Count;
            _ordered.Clear();
            _byId.Clear();
            Log.Info($"[AssistantRules] Cleared {n} rules");
            OnChanged?.Invoke();
        }
    }
}
