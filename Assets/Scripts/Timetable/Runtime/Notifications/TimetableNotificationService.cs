using System;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.SharedUI;

namespace RailwayManager.Timetable.Notifications
{
    /// <summary>
    /// M-TimetableUX F1.18: severity dla notifications w Basic/Advanced/Expert modes.
    /// Mode-gating per `memory/timetable_ux_milestone_design.md`:
    /// - Basic: Info + Warning + Error (no Hint)
    /// - Advanced: all severities
    /// - Expert: all + diagnostic details
    /// </summary>
    public enum NotificationSeverity
    {
        /// <summary>Informacyjne — informują gracza, nie wymagają reakcji (mijanka, regulacja, alt platform).</summary>
        Info,
        /// <summary>Ostrzeżenie — wymagają uwagi gracza (cannot fit z auto-recovery options).</summary>
        Warning,
        /// <summary>Błąd — blokuje save (path infeasible, disconnected graph).</summary>
        Error,
        /// <summary>Sugestia (Advanced+ only) — opportunity bez wymaganej akcji (crew swap available).</summary>
        Hint
    }

    /// <summary>
    /// Typ notyfikacji per spec'u Defaults Table — używane do filtrów + style ikon.
    /// </summary>
    public enum NotificationType
    {
        /// <summary>Mijanka detected (synchronized lub unsync).</summary>
        MijankaDetected,
        /// <summary>Block conflict auto-resolved (pociąg czeka N min na zwolnienie bloku).</summary>
        BlockConflictResolved,
        /// <summary>Platform conflict auto-resolved (Strategy A — użyto alternate peronu).</summary>
        PlatformConflictResolved,
        /// <summary>Dwell extended (postój wydłużony — regulacja ruchu).</summary>
        DwellExtended,
        /// <summary>Cannot fit — strategy A+B failed, suggested options dla user.</summary>
        CannotFit,
        /// <summary>Path infeasible — disconnected graph lub blokady.</summary>
        PathInfeasible,
        /// <summary>Suggestion available — proactive opportunity (Advanced mode only).</summary>
        SuggestionAvailable
    }

    /// <summary>
    /// Pojedynczy record notyfikacji — immutable po Add.
    /// </summary>
    [Serializable]
    public struct NotificationRecord
    {
        public NotificationSeverity severity;
        public NotificationType type;
        /// <summary>Polski user-facing message — pre-formatted z context'em (stop name, time, etc.).</summary>
        public string message;
        /// <summary>Index stopu w timetable.stops (-1 jeśli notification jest global/route-level).</summary>
        public int stopIndex;
        /// <summary>Time of day w sekundach od midnight — dla chronological sort'u.</summary>
        public int timeOfDaySec;
        /// <summary>Source timetable ID (-1 dla global notifications).</summary>
        public int sourceTimetableId;
        /// <summary>Real time (game) gdy notification dodany — diagnostic.</summary>
        public long createdAtGameSec;
        /// <summary>
        /// M-TimetableUX F1.16 Expert polish: diagnostic details (blockKey, time window, alternate platforms).
        /// Widoczne tylko w Expert mode (per spec'a F1.18 mode-gating). Null/empty w Basic/Advanced.
        /// </summary>
        public string diagnosticDetails;
    }

    /// <summary>
    /// M-TimetableUX F1.18: Service dla informacyjnych notifications generowanych przy
    /// timetable creation/save (CheckCollisions / AutoAssignPlatforms / ResolveBlockConflicts).
    ///
    /// **One per conflict event** (NOT aggregated) — jeśli timetable ma 5 block conflicts,
    /// 5 notifications. Pozwala graczowi zobaczyć każdy resolved conflict osobno.
    ///
    /// **Persistent until next timetable edit re-computes** — Clear() przy nowym CheckCollisions
    /// run dla danego timetable. UI subscribuje OnAdded event dla incremental refresh.
    ///
    /// **Mode-gating:** caller (UI) filtruje per <see cref="NotificationSeverity"/> wg current
    /// UI mode (Basic / Advanced / Expert — F1.16). Service sam nie filtruje.
    ///
    /// **Critical contract:** Add nigdy nie blokuje calling code. Notifications są
    /// post-fact informational. Player może zignorować, save proceeds.
    /// </summary>
    public static class TimetableNotificationService
    {
        private static readonly List<NotificationRecord> _notifications = new();

        public static IReadOnlyList<NotificationRecord> All => _notifications;
        public static int Count => _notifications.Count;

        public static event Action<NotificationRecord> OnAdded;
        public static event Action OnCleared;

        /// <summary>
        /// Dodaj notification. Returns assigned record dla ewentualnego logging w callerze.
        /// </summary>
        public static NotificationRecord Add(
            NotificationSeverity severity,
            NotificationType type,
            string message,
            int stopIndex = -1,
            int timeOfDaySec = 0,
            int sourceTimetableId = -1,
            string diagnosticDetails = null)
        {
            var rec = new NotificationRecord
            {
                severity = severity,
                type = type,
                message = message ?? "",
                stopIndex = stopIndex,
                timeOfDaySec = timeOfDaySec,
                sourceTimetableId = sourceTimetableId,
                createdAtGameSec = (long)GameState.GameTimeSeconds,
                diagnosticDetails = diagnosticDetails
            };
            _notifications.Add(rec);
            OnAdded?.Invoke(rec);
            return rec;
        }

        /// <summary>
        /// M-TimetableUX F1.16 Expert polish: Format notification message dla danej UIMode.
        /// Basic/Advanced: tylko message. Expert: message + " [diagnostic: X]" suffix gdy details set.
        /// </summary>
        public static string FormatMessageForMode(NotificationRecord rec, UIMode mode)
        {
            if (mode != UIMode.Expert) return rec.message;
            if (string.IsNullOrEmpty(rec.diagnosticDetails)) return rec.message;
            return $"{rec.message} [diag: {rec.diagnosticDetails}]";
        }

        /// <summary>
        /// Wyczyść wszystkie notifications. Wywołać przy re-compute (nowy CheckCollisions run)
        /// żeby stare notifications nie zaśmiecały UI.
        /// </summary>
        public static void Clear()
        {
            if (_notifications.Count == 0) return;
            _notifications.Clear();
            OnCleared?.Invoke();
        }

        /// <summary>
        /// Wyczyść notifications dla konkretnego timetable (np. re-edit pojedynczego rozkładu).
        /// </summary>
        public static void ClearForTimetable(int timetableId)
        {
            int removed = _notifications.RemoveAll(n => n.sourceTimetableId == timetableId);
            if (removed > 0)
                OnCleared?.Invoke();
        }

        /// <summary>
        /// Filtr: notifications dla danego timetable, sorted by timeOfDaySec.
        /// </summary>
        public static List<NotificationRecord> GetForTimetable(int timetableId)
        {
            var result = new List<NotificationRecord>();
            foreach (var n in _notifications)
                if (n.sourceTimetableId == timetableId)
                    result.Add(n);
            result.Sort((a, b) => a.timeOfDaySec.CompareTo(b.timeOfDaySec));
            return result;
        }

        /// <summary>
        /// Filtr per severity (UI mode-gating helper).
        /// Basic mode: GetVisibleForMode(uiMode=Basic) → Info/Warning/Error (no Hint).
        /// </summary>
        public static List<NotificationRecord> GetVisibleForMode(UIMode mode)
        {
            var result = new List<NotificationRecord>();
            foreach (var n in _notifications)
                if (IsVisibleInMode(n.severity, mode))
                    result.Add(n);
            return result;
        }

        public static bool IsVisibleInMode(NotificationSeverity severity, UIMode mode) => mode switch
        {
            UIMode.Basic => severity != NotificationSeverity.Hint,
            UIMode.Advanced => true,
            UIMode.Expert => true,
            _ => true
        };

        /// <summary>
        /// Polski human-readable severity label dla UI badges.
        /// </summary>
        public static string SeverityLabel(NotificationSeverity severity) => severity switch
        {
            NotificationSeverity.Info => "Info",
            NotificationSeverity.Warning => "Ostrzeżenie",
            NotificationSeverity.Error => "Błąd",
            NotificationSeverity.Hint => "Sugestia",
            _ => severity.ToString()
        };
    }

    // UIMode moved do SharedUI/UIMode.cs (M-TimetableUX F1.16/F1.17 prep) — cross-cutting concept.
}
