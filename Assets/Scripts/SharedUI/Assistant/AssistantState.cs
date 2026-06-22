using System;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Core.Assistant;
using RailwayManager.Core.GameRules;
using RailwayManager.SharedUI.Suggestions;

namespace RailwayManager.SharedUI.Assistant
{
    /// <summary>Wpis historii działań asystenta (log „co zrobiłem" — AS-D6).</summary>
    [Serializable]
    public struct AssistantHistoryEntry
    {
        public string text;
        public long gameTimeSec;
    }

    /// <summary>Snapshot stanu asystenta do save/load (SharedUISavable). Proaktywność NIE jest tu —
    /// żyje jako GameRule.AssistantProactivity (persist przez WorldSavable z GameRulesConfig).</summary>
    [Serializable]
    public class AssistantStateSnapshot
    {
        public string displayName;
        public bool introShown;
        public bool onboardingSkipped;
        public List<AssistantHistoryEntry> history = new List<AssistantHistoryEntry>();
        public List<string> autoModeCapabilityIds = new List<string>();
    }

    /// <summary>
    /// M11 AS-1c/1d: per-save stan persony asystenta (imię, historia działań).
    /// Static — żyje cross-scene; persist przez SharedUISavable, reset przy nowej grze.
    ///
    /// <see cref="ProactivityEnabled"/> = AS-D2/D7: czyta GameRule.AssistantProactivity
    /// (per-save toggle z GameCreatora, default Easy/Normal ON / Hard/Realistic OFF,
    /// niemutowalny mid-game per D36). JEDNO źródło prawdy — bez mirrora w stanie asystenta.
    /// Wyłączona proaktywność = brak szeptów onboarding/advisor; delegacja (PULL) działa zawsze.
    /// </summary>
    public static class AssistantState
    {
        public const string DefaultName = "pan Tadeusz";

        private static string _displayName = DefaultName;

        /// <summary>Imię persony (AS-D8). Puste/whitespace → default. Setter emituje OnDisplayNameChanged.</summary>
        public static string DisplayName
        {
            get => _displayName;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? DefaultName : value.Trim();
                if (normalized == _displayName) return;
                _displayName = normalized;
                OnDisplayNameChanged?.Invoke();
            }
        }

        /// <summary>Konsument: AssistantAvatarUI (inicjał + tooltip).</summary>
        public static event Action OnDisplayNameChanged;

        /// <summary>AS-D7: czy asystent sam zaczepia (onboarding/advisor szept). Delegacja działa niezależnie.</summary>
        public static bool ProactivityEnabled => GameRulesService.IsEnabled(GameRule.AssistantProactivity);

        /// <summary>AS-3: czy intro (powitanie persony) już poszło w tym save — one-shot per nowa gra.</summary>
        public static bool IntroShown = false;

        // ── AS-7: skip/restart onboardingu (Settings → Ogólne) ──

        /// <summary>
        /// AS-7: gracz pominął ścieżkę onboardingu — reguły kind=Onboarding znikają
        /// z wyboru kandydata (przez <see cref="OfferFilterAllows"/>). Advisor (reaktywne)
        /// i delegacja (PULL) działają dalej. Persist per-save.
        /// </summary>
        public static bool OnboardingSkipped = false;

        /// <summary>
        /// AS-7: filtr ofert monitora (orchestrator wpina w NextStepMonitor.SetOfferFilter).
        /// Skip onboardingu + pamięć sugestii (snooze/dismiss) w jednym, testowalnym miejscu.
        /// </summary>
        public static bool OfferFilterAllows(AssistantRule rule)
        {
            if (rule == null) return false;
            if (rule.kind == AssistantRuleKind.Onboarding && OnboardingSkipped) return false;
            return SuggestionMemoryService.ShouldShow(SuggestionType.Assistant, rule.contextKey ?? rule.id);
        }

        /// <summary>
        /// AS-7: restart onboardingu — reset stanu DRIVERA (intro persony + skip + stan
        /// runtime monitora: kandydat/liczniki stuck). SuggestionMemory celowo NIETKNIĘTE
        /// (decyzje snooze/dismiss gracza przeżywają restart — spec AS-7). Auto-mode zostaje.
        /// </summary>
        public static void RestartOnboarding()
        {
            IntroShown = false;
            OnboardingSkipped = false;
            NextStepMonitor.Reset();
            Log.Info("[AssistantState] Onboarding zrestartowany (driver reset; SuggestionMemory nietknięte)");
        }

        // ── AS-6 / AS-D6: opt-in auto-mode per capability ──

        private static readonly HashSet<string> _autoMode = new HashSet<string>();

        /// <summary>Konsument: AssistantPanelUI (refresh toggle).</summary>
        public static event Action OnAutoModeChanged;

        public static bool IsAutoModeEnabled(string capabilityId)
            => !string.IsNullOrEmpty(capabilityId) && _autoMode.Contains(capabilityId);

        public static void SetAutoMode(string capabilityId, bool enabled)
        {
            if (string.IsNullOrEmpty(capabilityId)) return;
            bool changed = enabled ? _autoMode.Add(capabilityId) : _autoMode.Remove(capabilityId);
            if (changed) OnAutoModeChanged?.Invoke();
        }

        /// <summary>
        /// AS-D6: brama auto-wykonania. Capability musi mieć mózg + flagę addytywności +
        /// świadomy opt-in gracza, a plan ZEROWY koszt (kasa nigdy auto — twarda zasada,
        /// niezależna od flag). Pure przy danym stanie — testowalne EditMode.
        /// </summary>
        public static bool AutoExecuteEligible(Core.Assistant.IAssistantCapability cap,
            Core.Assistant.AssistantPlan plan)
        {
            if (cap == null || plan == null) return false;
            if (!cap.CanAutoExecute || !cap.AutoModeAllowed) return false;
            if (!IsAutoModeEnabled(cap.Id)) return false;
            if (plan.costGroszy > 0) return false;
            return true;
        }

        private static readonly List<AssistantHistoryEntry> _history = new List<AssistantHistoryEntry>();

        /// <summary>Najnowsze wpisy na początku listy. Cap: AssistantConstants.HistoryMaxEntries.</summary>
        public static IReadOnlyList<AssistantHistoryEntry> History => _history;

        public static event Action OnHistoryChanged;

        /// <summary>Dopisuje wpis „co zrobiłem" (np. po Apply planu). Najnowszy na górze.</summary>
        public static void AddHistory(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _history.Insert(0, new AssistantHistoryEntry
            {
                text = text,
                gameTimeSec = (long)GameState.GameTimeSeconds
            });
            while (_history.Count > AssistantConstants.HistoryMaxEntries)
            {
                _history.RemoveAt(_history.Count - 1);
            }
            OnHistoryChanged?.Invoke();
        }

        /// <summary>Nowa gra — reset do defaultów (GameCreator nadpisze imię w ApplyGeneralOnStart).</summary>
        public static void ResetForNewGame()
        {
            DisplayName = DefaultName;
            IntroShown = false;
            OnboardingSkipped = false;
            _history.Clear();
            _autoMode.Clear();
            OnHistoryChanged?.Invoke();
            OnAutoModeChanged?.Invoke();
        }

        // ── Save/Load (konsumowane przez SharedUISavable) ──

        public static AssistantStateSnapshot Snapshot()
        {
            return new AssistantStateSnapshot
            {
                displayName = _displayName,
                introShown = IntroShown,
                onboardingSkipped = OnboardingSkipped,
                history = new List<AssistantHistoryEntry>(_history),
                autoModeCapabilityIds = new List<string>(_autoMode)
            };
        }

        public static void RestoreFromSave(AssistantStateSnapshot snapshot)
        {
            if (snapshot == null) { ResetForNewGame(); return; }
            DisplayName = snapshot.displayName;
            IntroShown = snapshot.introShown;
            OnboardingSkipped = snapshot.onboardingSkipped;
            _history.Clear();
            if (snapshot.history != null) _history.AddRange(snapshot.history);
            _autoMode.Clear();
            if (snapshot.autoModeCapabilityIds != null)
            {
                foreach (var id in snapshot.autoModeCapabilityIds)
                {
                    if (!string.IsNullOrEmpty(id)) _autoMode.Add(id);
                }
            }
            OnHistoryChanged?.Invoke();
            OnAutoModeChanged?.Invoke();
        }
    }
}
