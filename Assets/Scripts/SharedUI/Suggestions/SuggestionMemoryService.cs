using System;
using System.Collections.Generic;
using RailwayManager.Core;

namespace RailwayManager.SharedUI.Suggestions
{
    /// <summary>
    /// Typ sugestii proaktywnej (M-TimetableUX F1.20).
    /// </summary>
    public enum SuggestionType
    {
        Circulation,    // F1.12: nowy timetable pasuje do istniejącego obiegu
        CrewSwap,       // F1.13a: zmiana drużyny przy ZD/PH stop
        Mijanka,        // F1.13b: synchronizacja PT mijanki z opposite-direction train
        Conflict,       // F1.18: conflict resolution suggestion (Strategy A/B options)
        Assistant       // M11 AS-1c: szepty asystenta gracza (snooze/dismiss per reguła monitora)
    }

    /// <summary>
    /// Decyzja gracza odnośnie sugestii. Tracked dla analytics + future ML personalizacji.
    /// </summary>
    public enum SuggestionChoice
    {
        /// <summary>Permanent dismiss — nie pokazuj więcej dla tego context'u w tym save'cie.</summary>
        Dismiss,
        /// <summary>Timed snooze — re-show po `snoozeDurationSec` w grze.</summary>
        Snooze,
        /// <summary>Akceptacja — sugestia wykonała się (np. utworzono obieg). Re-prompt dla nowych kontekstów.</summary>
        Accept
    }

    /// <summary>Record dismissed/accepted suggestion — internal storage struct.</summary>
    [Serializable]
    public struct SuggestionRecord
    {
        public SuggestionType type;
        public string contextKey;
        public long dismissedAtGameSec;
        public long snoozedUntilGameSec;  // 0 = permanent dismiss
        public SuggestionChoice choice;
    }

    /// <summary>
    /// M-TimetableUX F1.20: Suggestion memory dla proaktywnych sugestii (F1.12 Circulation /
    /// F1.13a CrewSwap / F1.13b Mijanka / F1.18 Conflict). Tracks dismissed/accepted/snoozed
    /// per-(type, contextKey) żeby zapobiec re-prompt'owi tej samej sugestii dla tego samego kontekstu.
    ///
    /// **Per-save state:** memory wraz z save/load (TBD: integration via SaveLoad module).
    /// Nowa gra = fresh memory. Dismissed w save A nie wpływa na save B.
    ///
    /// **Snooze semantics:** snooze record re-aktywuje się po przekroczeniu `snoozedUntilGameSec`
    /// (GameState.GameTimeSeconds porównanie). Snooze 0 = permanent dismiss.
    ///
    /// **Throttling:** max 1 prompt per major user action (save, add stop). Implementacja
    /// throttling'u w callerach (np. <c>CirculationSuggestionService</c>); ten service tylko
    /// memory layer.
    ///
    /// **Critical contract:** suggestions cannot block save flow. Player może zignorować/odrzucić
    /// każdą — ten service tylko track'uje co już dismissed, NIE blokuje user actions.
    /// </summary>
    public static class SuggestionMemoryService
    {
        // Storage: keyed by "{type}:{contextKey}".
        private static readonly Dictionary<string, SuggestionRecord> _records = new();

        /// <summary>Total count records (debug + diagnostic).</summary>
        public static int RecordCount => _records.Count;

        /// <summary>
        /// Czy pokazać sugestię dla danego (type, contextKey).
        /// True = pokaż (no record OR snooze expired OR previously accepted).
        /// False = ukryj (dismiss permanent OR snooze active).
        /// </summary>
        public static bool ShouldShow(SuggestionType type, string contextKey)
        {
            if (string.IsNullOrEmpty(contextKey)) return true;

            var key = MakeKey(type, contextKey);
            if (!_records.TryGetValue(key, out var rec)) return true;

            switch (rec.choice)
            {
                case SuggestionChoice.Accept:
                    // Accepted suggestion — context "consumed", nie re-prompt'uj dla tego samego.
                    // Caller może utworzyć nowy contextKey jeśli inny scenariusz.
                    return false;
                case SuggestionChoice.Dismiss:
                    return false;
                case SuggestionChoice.Snooze:
                    // Re-aktywuj po snooze deadline.
                    long now = (long)GameState.GameTimeSeconds;
                    return now >= rec.snoozedUntilGameSec;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Zapisz dismiss/accept/snooze decyzję gracza.
        /// </summary>
        /// <param name="snoozeDurationSec">Tylko dla <see cref="SuggestionChoice.Snooze"/>: ile sekund game-time zanim re-aktywować. Default 1h game time.</param>
        public static void RecordChoice(SuggestionType type, string contextKey, SuggestionChoice choice, long snoozeDurationSec = 3600)
        {
            if (string.IsNullOrEmpty(contextKey))
            {
                Log.Warn($"[SuggestionMemory] RecordChoice with empty contextKey (type={type}, choice={choice}) — ignored");
                return;
            }

            var key = MakeKey(type, contextKey);
            long now = (long)GameState.GameTimeSeconds;
            long snoozedUntil = choice == SuggestionChoice.Snooze ? now + snoozeDurationSec : 0;

            _records[key] = new SuggestionRecord
            {
                type = type,
                contextKey = contextKey,
                dismissedAtGameSec = now,
                snoozedUntilGameSec = snoozedUntil,
                choice = choice
            };
        }

        /// <summary>
        /// Settings option "show all suggestions again" — wyczyść wszystkie records.
        /// Wywoływane przez Settings UI (M13) lub diagnostic ContextMenu.
        /// </summary>
        public static void Reset()
        {
            int n = _records.Count;
            _records.Clear();
            Log.Info($"[SuggestionMemory] Reset — cleared {n} records");
        }

        /// <summary>
        /// Save/Load integration helper — eksport wszystkich records dla persistence.
        /// </summary>
        public static IReadOnlyCollection<SuggestionRecord> GetAllRecords() => _records.Values;

        /// <summary>
        /// Save/Load integration helper — restore records z save (clear + bulk add).
        /// </summary>
        public static void RestoreFromSave(IEnumerable<SuggestionRecord> records)
        {
            _records.Clear();
            if (records == null) return;
            foreach (var rec in records)
            {
                if (string.IsNullOrEmpty(rec.contextKey)) continue;
                _records[MakeKey(rec.type, rec.contextKey)] = rec;
            }
        }

        private static string MakeKey(SuggestionType type, string contextKey) => $"{type}:{contextKey}";
    }
}
