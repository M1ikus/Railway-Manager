using System;
using RailwayManager.Core;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// M-TimetableUX F1.17: Onboarding progress tracking dla unlock UI modes (Basic→Advanced→Expert).
    ///
    /// **Unlock conditions** (per spec):
    /// - First save → forced <see cref="UIMode.Basic"/>
    /// - After <see cref="AdvancedUnlockTimetableCount"/> timetables created → unlock Advanced toggle
    /// - After <see cref="ExpertUnlockHours"/> hours play OR M11 tutorial completion → unlock Expert
    ///
    /// **Persistence:** stan (counters + selected mode + unlock notifications) persistowany
    /// przez `SaveLoad/Modules/SharedUISavable.cs` (module ID "shared_ui"). Snapshot/Restore
    /// API niżej (<see cref="Snapshot"/> / <see cref="RestoreFromSave"/>). New game wywołuje
    /// <see cref="Reset"/>.
    ///
    /// **Player explicit choice** (Settings option) overrides auto-unlock — gracz może
    /// wybrać Advanced przed unlock count, lub pozostać na Basic mimo unlock'a.
    /// </summary>
    public static class PlayerProgressService
    {
        /// <summary>Default unlock threshold dla Advanced mode (timetables created).</summary>
        public const int AdvancedUnlockTimetableCount = 5;
        /// <summary>Default unlock threshold dla Expert mode (game-time hours).</summary>
        public const float ExpertUnlockHours = 10f;

        // ── State (persistable) ──
        private static int _timetablesCreated = 0;
        private static bool _tutorialCompleted = false;
        private static UIMode _playerSelectedMode = UIMode.Basic;
        private static bool _playerHasExplicitlySelected = false;

        // Unlock event tracking — fired raz per unlock transition (false→true)
        private static bool _advancedUnlockNotified = false;
        private static bool _expertUnlockNotified = false;

        /// <summary>
        /// Event invokowany gdy nowy UIMode zostaje odblokowany (Advanced lub Expert).
        /// UI subscribuje dla rebuild dropdown lub show unlock toast.
        /// </summary>
        public static event System.Action<UIMode> OnModeUnlocked;

        // ── Public state accessors (read-only dla external) ──

        public static int TimetablesCreated => _timetablesCreated;
        public static bool TutorialCompleted => _tutorialCompleted;

        /// <summary>
        /// Czy Advanced mode został odblokowany (unlock != active selection).
        /// </summary>
        public static bool IsAdvancedUnlocked => _timetablesCreated >= AdvancedUnlockTimetableCount;

        /// <summary>
        /// Czy Expert mode został odblokowany.
        /// Conditions: (game time ≥ ExpertUnlockHours) OR tutorial completed.
        /// </summary>
        public static bool IsExpertUnlocked
        {
            get
            {
                if (_tutorialCompleted) return true;
                float hoursPlayed = GameState.GameTimeSeconds / 3600f;
                return hoursPlayed >= ExpertUnlockHours;
            }
        }

        /// <summary>
        /// Effective UIMode: respect player's explicit selection if unlocked, else fallback do najlepszego unlocked.
        /// </summary>
        public static UIMode GetEffectiveMode()
        {
            if (_playerHasExplicitlySelected)
            {
                // Validate that selected mode is unlocked
                if (_playerSelectedMode == UIMode.Expert && !IsExpertUnlocked) return GetMaxUnlockedMode();
                if (_playerSelectedMode == UIMode.Advanced && !IsAdvancedUnlocked) return UIMode.Basic;
                return _playerSelectedMode;
            }
            return GetMaxUnlockedMode();
        }

        /// <summary>
        /// Najwyższy odblokowany mode (default jeśli player nie wybrał explicit).
        /// Note: pre-F1.17 default = Basic — auto-promocja w F1.17 polish (gdy player gets used to UX).
        /// </summary>
        public static UIMode GetMaxUnlockedMode()
        {
            if (IsExpertUnlocked) return UIMode.Expert;
            if (IsAdvancedUnlocked) return UIMode.Advanced;
            return UIMode.Basic;
        }

        // ── Mutators ──

        /// <summary>
        /// Increment counter timetables created. Wywołać po każdym <c>TimetableService.AddTimetable</c>.
        /// </summary>
        public static void RecordTimetableCreated()
        {
            _timetablesCreated++;
            if (_timetablesCreated == AdvancedUnlockTimetableCount && !_advancedUnlockNotified)
            {
                _advancedUnlockNotified = true;
                Log.Info($"[PlayerProgress] Advanced mode UNLOCKED ({_timetablesCreated} timetables created)");
                OnModeUnlocked?.Invoke(UIMode.Advanced);
            }
        }

        public static void RecordTutorialCompletion()
        {
            if (_tutorialCompleted) return;
            _tutorialCompleted = true;
            Log.Info("[PlayerProgress] Tutorial completed — Expert mode UNLOCKED");
            if (!_expertUnlockNotified)
            {
                _expertUnlockNotified = true;
                OnModeUnlocked?.Invoke(UIMode.Expert);
            }
        }

        /// <summary>
        /// Manual check dla Expert unlock przez time progress — wywołać periodically
        /// (np. raz dziennie z game scheduler). Idempotent — fires event raz.
        /// </summary>
        public static void CheckExpertTimeUnlock()
        {
            if (_expertUnlockNotified) return;
            float hoursPlayed = GameState.GameTimeSeconds / 3600f;
            if (hoursPlayed >= ExpertUnlockHours)
            {
                _expertUnlockNotified = true;
                Log.Info($"[PlayerProgress] Expert mode UNLOCKED (game time {hoursPlayed:F1}h)");
                OnModeUnlocked?.Invoke(UIMode.Expert);
            }
        }

        /// <summary>
        /// Player explicitly selects mode w Settings UI. Validation w GetEffectiveMode.
        /// </summary>
        public static void SetPlayerMode(UIMode mode)
        {
            _playerSelectedMode = mode;
            _playerHasExplicitlySelected = true;
            Log.Info($"[PlayerProgress] Player selected mode: {mode}");
        }

        /// <summary>
        /// Reset explicit selection — fall back to auto-unlock logic.
        /// </summary>
        public static void ClearPlayerSelection()
        {
            _playerHasExplicitlySelected = false;
        }

        /// <summary>
        /// Reset all progress — new game lub explicit settings reset.
        /// </summary>
        public static void Reset()
        {
            _timetablesCreated = 0;
            _tutorialCompleted = false;
            _playerSelectedMode = UIMode.Basic;
            _playerHasExplicitlySelected = false;
            _advancedUnlockNotified = false;
            _expertUnlockNotified = false;
        }

        // ── Save/Load integration helpers (konsumowane przez SaveLoad/Modules/SharedUISavable.cs) ──

        public static (int ttCreated, bool tutorialDone, UIMode selected, bool hasExplicit,
                       bool advancedUnlockNotified, bool expertUnlockNotified) Snapshot() =>
            (_timetablesCreated, _tutorialCompleted, _playerSelectedMode, _playerHasExplicitlySelected,
             _advancedUnlockNotified, _expertUnlockNotified);

        public static void RestoreFromSave(int ttCreated, bool tutorialDone, UIMode selected, bool hasExplicit,
                                           bool advancedUnlockNotified, bool expertUnlockNotified)
        {
            _timetablesCreated = Math.Max(0, ttCreated);
            _tutorialCompleted = tutorialDone;
            _playerSelectedMode = Enum.IsDefined(typeof(UIMode), selected) ? selected : UIMode.Basic;
            _playerHasExplicitlySelected = hasExplicit;
            _advancedUnlockNotified = advancedUnlockNotified;
            _expertUnlockNotified = expertUnlockNotified;
        }
    }
}
