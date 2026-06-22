using System;

namespace RailwayManager.Core.Assistant
{
    /// <summary>
    /// M11 AS-1b: silnik next-step monitora — serce „co robić dalej" (guidance) i advisora.
    /// Czysta logika (testowalna EditMode); host (AssistantOrchestrator w SharedUI) woła
    /// <see cref="Tick"/> co ~<see cref="AssistantConstants.EvaluateIntervalRealSec"/>.
    ///
    /// Odpowiedzialności:
    /// - wybór kandydata: najwyższy priorytet wśród aktywnych reguł (remis = kolejność
    ///   rejestracji w AssistantRuleRegistry — determinizm),
    /// - filtr ofert: <see cref="SetOfferFilter"/> — SharedUI wpina SuggestionMemoryService.ShouldShow
    ///   (Core NIE widzi SharedUI, stąd delegat — wzorzec hook jak SaveActionsHook),
    /// - stuck-detector (trigger eskalacji AS-D4 [1]→[2]): czas bezczynności na kroku
    ///   + sygnały PanelAbandoned/ActionCanceled z AssistantSignals (liczone tylko gdy
    ///   contextKey sygnału == capabilityId kandydata),
    /// - flaga ToolActive (zasada UX „nie szepcz, gdy gracz aktywnie coś robi") — gdy aktywna,
    ///   timer stuck przesuwa się (gracz działa, nie utknął).
    ///
    /// Szept/cooldown/gating proaktywności to odpowiedzialność DRIVERA (SharedUI) —
    /// monitor tylko wskazuje kandydata i wykrywa „utknął".
    /// </summary>
    public static class NextStepMonitor
    {
        /// <summary>Aktualny kandydat (najwyższy aktywny priorytet po filtrze). Null = nic do zaoferowania.</summary>
        public static AssistantRule CurrentCandidate { get; private set; }

        /// <summary>True między sygnałami ToolActive a ToolIdle — gracz aktywnie używa narzędzia.</summary>
        public static bool IsToolActive { get; private set; }

        /// <summary>Czy stuck został już zgłoszony dla bieżącego kandydata (jednorazowo per kandydat).</summary>
        public static bool StuckFiredForCurrent { get; private set; }

        /// <summary>Kandydat się zmienił (może być null = brak). Konsument: driver (szept/badge).</summary>
        public static event Action<AssistantRule> OnCandidateChanged;

        /// <summary>Gracz utknął na bieżącym kroku — trigger eskalacji [1]→[2] (AS-D4).</summary>
        public static event Action<AssistantRule> OnStuckDetected;

        private static Func<AssistantRule, bool> _offerFilter;
        private static bool _initialized;
        private static double _candidateSinceRealSec;
        private static int _panelAbandonedCount;
        private static int _actionCanceledCount;

        /// <summary>
        /// Wpina filtr ofert (SharedUI: SuggestionMemoryService.ShouldShow — dedup/snooze).
        /// Null = brak filtra. False z filtra = reguła pomijana przy wyborze kandydata.
        /// </summary>
        public static void SetOfferFilter(Func<AssistantRule, bool> filter) => _offerFilter = filter;

        /// <summary>Subskrybuje AssistantSignals (idempotentne). Woła host przy starcie.</summary>
        public static void Initialize()
        {
            if (_initialized) return;
            AssistantSignals.OnSignal += HandleSignal;
            _initialized = true;
        }

        /// <summary>Odpina sygnały (teardown / testy).</summary>
        public static void Shutdown()
        {
            if (!_initialized) return;
            AssistantSignals.OnSignal -= HandleSignal;
            _initialized = false;
        }

        /// <summary>
        /// Nowa gra / powrót do MainMenu / testy — czyści stan runtime (kandydat, liczniki,
        /// flagi). Reguł NIE czyści (to AssistantRuleRegistry.Clear).
        /// </summary>
        public static void Reset()
        {
            CurrentCandidate = null;
            IsToolActive = false;
            StuckFiredForCurrent = false;
            _candidateSinceRealSec = 0;
            _panelAbandonedCount = 0;
            _actionCanceledCount = 0;
        }

        /// <summary>
        /// Ewaluacja reguł + detekcja stuck. <paramref name="nowRealSec"/> = czas realny
        /// (host: Time.realtimeSinceStartupAsDouble) — wstrzykiwany dla testowalności.
        /// </summary>
        public static void Tick(double nowRealSec)
        {
            // 1. Wybór kandydata: najwyższy priorytet wśród aktywnych (strictly-greater
            //    zachowuje "pierwsza zarejestrowana wygrywa" przy remisie).
            AssistantRule top = null;
            var rules = AssistantRuleRegistry.All;
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (top != null && rule.priority <= top.priority) continue;
                if (!rule.isActive()) continue;
                if (_offerFilter != null && !_offerFilter(rule)) continue;
                top = rule;
            }

            // 2. Zmiana kandydata → reset stanu stuck + event.
            if (!ReferenceEquals(top, CurrentCandidate))
            {
                CurrentCandidate = top;
                _candidateSinceRealSec = nowRealSec;
                _panelAbandonedCount = 0;
                _actionCanceledCount = 0;
                StuckFiredForCurrent = false;
                OnCandidateChanged?.Invoke(top);
            }

            if (CurrentCandidate == null) return;

            // 3. Gracz aktywnie działa → przesuń timer (mierzymy czas OD OSTATNIEJ aktywności).
            if (IsToolActive)
            {
                _candidateSinceRealSec = nowRealSec;
                return;
            }

            // 4. Stuck po czasie bezczynności na kroku.
            if (!StuckFiredForCurrent &&
                nowRealSec - _candidateSinceRealSec >= AssistantConstants.StuckTimeOnStepRealSec)
            {
                FireStuck();
            }
        }

        private static void HandleSignal(AssistantSignal signal)
        {
            switch (signal.kind)
            {
                case AssistantSignalKind.ToolActive:
                    IsToolActive = true;
                    break;

                case AssistantSignalKind.ToolIdle:
                    IsToolActive = false;
                    break;

                case AssistantSignalKind.PanelAbandoned:
                    if (MatchesCurrent(signal) &&
                        ++_panelAbandonedCount >= AssistantConstants.StuckPanelAbandonedThreshold)
                    {
                        FireStuck();
                    }
                    break;

                case AssistantSignalKind.ActionCanceled:
                    if (MatchesCurrent(signal) &&
                        ++_actionCanceledCount >= AssistantConstants.StuckActionCanceledThreshold)
                    {
                        FireStuck();
                    }
                    break;
            }
        }

        /// <summary>
        /// Sygnał liczy się do stuck tylko gdy dotyczy bieżącego kroku — moduł emituje
        /// contextKey = capabilityId (np. FleetPanel zamknięty bez zakupu → "fleet.buy").
        /// </summary>
        private static bool MatchesCurrent(in AssistantSignal signal)
        {
            return CurrentCandidate != null
                && !StuckFiredForCurrent
                && signal.contextKey != null
                && signal.contextKey == CurrentCandidate.capabilityId;
        }

        private static void FireStuck()
        {
            if (StuckFiredForCurrent || CurrentCandidate == null) return;
            StuckFiredForCurrent = true;
            Log.Info($"[NextStepMonitor] Stuck detected na kroku '{CurrentCandidate.id}' (capability '{CurrentCandidate.capabilityId}')");
            OnStuckDetected?.Invoke(CurrentCandidate);
        }
    }
}
