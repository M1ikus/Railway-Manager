using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Assistant;
using RailwayManager.SharedUI.Localization;
using RailwayManager.SharedUI.Suggestions;

namespace RailwayManager.SharedUI.Assistant
{
    /// <summary>
    /// M11 AS-1c: driver asystenta — spina Core (NextStepMonitor/registry/signals) z UI persony.
    /// Odpowiedzialności:
    /// - tick monitora co AssistantConstants.EvaluateIntervalRealSec (czas realny),
    /// - polityka szeptu: gating proaktywności (AS-D7), cooldown, cisza gdy ToolActive,
    ///   snooze przez SuggestionMemoryService (typ Assistant),
    /// - eskalacja stuck [1]→[2] (AS-D4): capability z mózgiem → oferta planu, bez → guidance,
    /// - scene-aware visibility (Depot/MapScene — wzorzec MaintenanceAlertsUI),
    /// - filtr ofert monitora = SuggestionMemoryService.ShouldShow (wpięty przez SetOfferFilter,
    ///   bo Core nie widzi SharedUI).
    /// </summary>
    public class AssistantOrchestrator : MonoBehaviour
    {
        public static AssistantOrchestrator Instance { get; private set; }

        AssistantAvatarUI _avatar;
        AssistantWhisperUI _whisper;
        AssistantPanelUI _panel;

        float _tickTimer;
        double _lastWhisperRealSec = double.MinValue;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            if (FindAnyObjectByType<AssistantOrchestrator>() != null) return;
            EnsureExists();
        }

        public static AssistantOrchestrator EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("AssistantOrchestrator");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AssistantOrchestrator>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            NextStepMonitor.Initialize();
            // AS-7: filtr = skip onboardingu + pamięć sugestii (logika w AssistantState — testowalna).
            NextStepMonitor.SetOfferFilter(AssistantState.OfferFilterAllows);

            _avatar = AssistantAvatarUI.EnsureExists();
            _whisper = AssistantWhisperUI.EnsureExists(_avatar);
            _panel = AssistantPanelUI.EnsureExists(_avatar);

            _avatar.OnAvatarClicked += TogglePanel;
            NextStepMonitor.OnCandidateChanged += HandleCandidateChanged;
            NextStepMonitor.OnStuckDetected += HandleStuckDetected;
            AssistantSignals.OnSignal += HandleAssistantSignal; // AS-6: sugestie z bridges

            Log.Info("[AssistantOrchestrator] Bootstrap OK (monitor + persona UI)");
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            if (_avatar != null) _avatar.OnAvatarClicked -= TogglePanel;
            NextStepMonitor.OnCandidateChanged -= HandleCandidateChanged;
            NextStepMonitor.OnStuckDetected -= HandleStuckDetected;
            AssistantSignals.OnSignal -= HandleAssistantSignal;
            NextStepMonitor.Shutdown();
        }

        void Update()
        {
            // Scene-aware (wzorzec MaintenanceAlertsUI): companion tylko w gameplayu.
            var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool inGameplay = sceneName == "Depot" || sceneName == "MapScene";
            // Ustąp pełnoekranowym popupom (kreator rozkładów / pickery) — inaczej awatar i szept
            // przeświecają spod panelu (z-order: popup sortingOrder 200 > avatar 175).
            bool popupOpen = SceneController.TimetablePopupOpen || SceneController.FullscreenOverlayOpen;
            bool visible = inGameplay && !popupOpen;
            _avatar?.SetVisible(visible);
            if (!visible)
            {
                if (_panel != null && _panel.IsOpen) _panel.Hide();
                _whisper?.HideSilent();
                return;
            }

            _tickTimer += Time.unscaledDeltaTime;
            if (_tickTimer >= AssistantConstants.EvaluateIntervalRealSec)
            {
                _tickTimer = 0f;
                NextStepMonitor.Tick(Time.realtimeSinceStartupAsDouble);
            }
        }

        // ────────────────────────── Monitor → UI ──────────────────────────

        void HandleCandidateChanged(AssistantRule rule)
        {
            // AS-D7: badge to też PUSH — przy wyłączonej proaktywności sygnalizujemy tylko w panelu (PULL).
            _avatar?.SetBadge(rule != null && AssistantState.ProactivityEnabled);
            if (rule == null)
            {
                _whisper?.HideSilent();
                return;
            }

            // AS-3: intro persony — one-shot per save (powitanie + pierwszy realny brak z diagnozy).
            if (!AssistantState.IntroShown && AssistantState.ProactivityEnabled)
            {
                AssistantState.IntroShown = true;
                string intro = string.Format(LocalizationService.Get("assistant.whisper.intro_format"),
                                   AssistantState.DisplayName)
                               + " " + LocalizationService.Get(rule.messageKey);
                TryWhisper(intro, rule.contextKey ?? rule.id, bypassCooldown: true, onClick: TogglePanel);
                return;
            }

            // AS-6 / AS-D6: opt-in auto-mode — capability addytywna z włączonym auto
            // wykonuje się w tle (zero-koszt twardo), log w historii zamiast preview.
            if (TryAutoExecute(rule)) return;

            TryWhisper(LocalizationService.Get(rule.messageKey), rule.contextKey ?? rule.id,
                bypassCooldown: false, onClick: TogglePanel);
        }

        /// <summary>AS-6: konsumpcja sugestii z bridges modułów (Circulation/Mijanka/CrewSwap/TimetableNotif).</summary>
        void HandleAssistantSignal(AssistantSignal signal)
        {
            if (signal.kind != AssistantSignalKind.Suggestion) return;

            // Payload-string = gotowy opis z serwisu; messageKey = wariant i18n.
            string text = signal.payload as string;
            if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(signal.messageKey))
            {
                text = LocalizationService.Get(signal.messageKey);
            }
            if (string.IsNullOrEmpty(text)) return;

            TryWhisper(text, signal.contextKey ?? signal.sourceId, bypassCooldown: false,
                onClick: TogglePanel);
        }

        /// <summary>AS-D6: próba auto-wykonania kandydata. True = wykonane (bez szeptu propozycji).</summary>
        bool TryAutoExecute(AssistantRule rule)
        {
            var cap = AssistantCapabilityRegistry.Get(rule.capabilityId);
            if (cap == null || !cap.CanAutoExecute || !cap.AutoModeAllowed) return false;
            if (!AssistantState.IsAutoModeEnabled(cap.Id)) return false;

            var plan = cap.Plan();
            if (!AssistantState.AutoExecuteEligible(cap, plan)) return false;

            if (!cap.Apply(plan)) return false;

            AssistantState.AddHistory("auto: " + plan.title);
            TryWhisper(string.Format(LocalizationService.Get("assistant.whisper.auto_done_format"), plan.title),
                "auto:" + cap.Id, bypassCooldown: true, onClick: TogglePanel);
            Log.Info($"[AssistantOrchestrator] Auto-mode wykonał '{cap.Id}': {plan.title}");
            return true;
        }

        void HandleStuckDetected(AssistantRule rule)
        {
            // Eskalacja AS-D4 [1]→[2]: mózg → zaproponuj plan; guidance-only → pokaż mocniej.
            var cap = AssistantCapabilityRegistry.Get(rule.capabilityId);
            if (cap != null && cap.CanAutoExecute)
            {
                string capId = cap.Id;
                TryWhisper(LocalizationService.Get("assistant.stuck.offer_help"),
                    rule.contextKey ?? rule.id, bypassCooldown: true, onClick: () => ShowPlanFor(capId));
            }
            else
            {
                TryWhisper(LocalizationService.Get(rule.messageKey), rule.contextKey ?? rule.id,
                    bypassCooldown: true, onClick: TogglePanel);
            }
        }

        void TryWhisper(string text, string memoryKey, bool bypassCooldown, System.Action onClick)
        {
            if (!AssistantState.ProactivityEnabled) return;          // AS-D7: toggle wyłącza PUSH
            if (NextStepMonitor.IsToolActive) return;                 // cisza gdy gracz aktywny
            if (_panel != null && _panel.IsOpen) return;              // panel otwarty = gracz już patrzy
            if (_whisper == null) return;

            // AS-6: memory filter także dla sugestii z bridges (snooze/dismiss respektowane).
            if (!SuggestionMemoryService.ShouldShow(SuggestionType.Assistant, memoryKey)) return;

            double now = Time.realtimeSinceStartupAsDouble;
            if (!bypassCooldown && now - _lastWhisperRealSec < AssistantConstants.WhisperCooldownRealSec) return;

            _whisper.Show(text, onClick, onDismissedByX: () =>
                SuggestionMemoryService.RecordChoice(SuggestionType.Assistant, memoryKey, SuggestionChoice.Snooze));
            _lastWhisperRealSec = now;
        }

        // ────────────────────────── Akcje (panel/whisper → capability) ──────────────────────────

        void TogglePanel() => _panel?.Toggle();

        /// <summary>Poziom [2]: policz plan i pokaż preview (Plan→akceptuj→Apply, AS-D3).</summary>
        public void ShowPlanFor(string capabilityId)
        {
            var cap = AssistantCapabilityRegistry.Get(capabilityId);
            var plan = cap?.Plan();
            if (plan == null)
            {
                _whisper?.Show(LocalizationService.Get("assistant.plan.nothing"), TogglePanel, null);
                return;
            }
            AssistantPlanPreviewUI.Show(cap, plan);
        }

        /// <summary>
        /// Poziom [1]: pokaż pierwszy krok guidance — szept + pulsująca poświata na celu
        /// (AS-3: moduł zarejestrował RectTransform w AssistantHighlightTargets).
        /// </summary>
        public void ShowGuidanceFor(string capabilityId)
        {
            var cap = AssistantCapabilityRegistry.Get(capabilityId);
            var guidance = cap?.GetGuidance();
            string text = (guidance != null && guidance.steps.Count > 0)
                ? LocalizationService.Get(guidance.steps[0].messageKey)
                : LocalizationService.Get("assistant.guidance.fallback");
            _whisper?.Show(text, TogglePanel, null);

            AssistantHighlight.ClearAll();
            if (guidance != null && guidance.steps.Count > 0)
            {
                var step = guidance.steps[0];
                if (!string.IsNullOrEmpty(step.highlightTargetId)
                    && AssistantHighlightTargets.TryGet(step.highlightTargetId, out var target))
                {
                    AssistantHighlight.Show(target);
                }
                // AS-5c: krok może otwierać panel (np. planner połączeń) — generyczny mechanizm.
                if (step.uiIntent.HasValue)
                {
                    UIIntents.Emit(step.uiIntent.Value);
                }
            }
        }

        // ────────────────────────── Diagnostyka ──────────────────────────

        [ContextMenu("Assistant: testowy szept")]
        void DebugTestWhisper()
        {
            _whisper?.Show(LocalizationService.Get("assistant.whisper.test"), TogglePanel, null);
        }

        [ContextMenu("Assistant: otwórz panel")]
        void DebugOpenPanel() => _panel?.Show();
    }
}
