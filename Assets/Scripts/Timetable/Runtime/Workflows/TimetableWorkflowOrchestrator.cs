using System;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Timetable.Suggestions;
using RailwayManager.Timetable.Notifications;

namespace RailwayManager.Timetable.Workflows
{
    /// <summary>
    /// M-TimetableUX F1.15 (minimal scope): Coordinator dla suggestion services trigger po save.
    /// Foundation dla full pipeline view (rozkład → obieg → vehicle → crew unified UX) — pełen
    /// orchestrator UI deferred do F1.15 polish post-EA.
    ///
    /// **Pre-F1.15 polish behavior:**
    /// - Subscribe na <see cref="TimetableService.OnTimetablesChanged"/>
    /// - Detect new timetable via delta (Count comparison)
    /// - Auto-trigger F1.12 CirculationSuggestionService.GenerateSuggestions(newTtId)
    /// - Auto-trigger F1.13b MijankaSuggestionService.GenerateSuggestions(newTtId)
    /// - Notify TimetableNotificationService gdy suggestions available (Hint severity, Advanced+ visible)
    ///
    /// **F1.13a CrewSwap** w Personnel namespace — Personnel module subscribuje samodzielnie
    /// (cross-namespace dependency, Timetable nie zna Personnel asmdef).
    ///
    /// **Full F1.15 scope (deferred do polish post-EA):**
    /// - Pipeline UI z step indicators (Stwórz rozkład → Sugestia obiegu → Vehicle → Crew)
    /// - Pre-fill each step intelligently (vehicle suggestion z FleetService, crew z CrewCirculationService)
    /// - Modal stages z back/forward navigation
    /// - Replace 4 osobne panele unified workflow
    /// </summary>
    /// <summary>
    /// F1.15 polish step state machine — pipeline view progress.
    /// </summary>
    public enum WorkflowStep
    {
        Idle,
        CreatingTimetable,
        ReviewingSuggestions, // F1.12+F1.13a+F1.13b modals
        AssigningVehicle,
        AssigningCrew,
        Done
    }

    public static class TimetableWorkflowOrchestrator
    {
        private static int _lastKnownTimetableCount = 0;
        private static bool _subscribed = false;

        /// <summary>F1.15 polish: aktualny step w pipeline workflow. Default Idle (player używa istniejącego UI bezpośrednio).</summary>
        public static WorkflowStep CurrentStep { get; private set; } = WorkflowStep.Idle;

        /// <summary>F1.15 polish: ID rozkładu w aktywnym pipeline workflow (-1 = brak active).</summary>
        public static int ActiveWorkflowTimetableId { get; private set; } = -1;

        /// <summary>F1.15 polish: invokowany przy każdej zmianie CurrentStep — UI subscribe dla step indicators refresh.</summary>
        public static event Action<WorkflowStep> OnStepChanged;

        /// <summary>
        /// Cross-asmdef hook dla suggestion services które są poza Timetable namespace
        /// (np. Personnel <c>CrewSwapSuggestionService</c>). Invokowany z
        /// <see cref="TriggerSuggestionsForNewTimetable"/> po own Timetable suggestions.
        /// </summary>
        public static event Action<int> OnNewTimetableSuggestionsRequested;

        /// <summary>
        /// Subscribe na TimetableService events. Idempotent — multiple calls safe.
        /// Wywołać raz w bootstrap (TimetableInitializer init flow).
        /// </summary>
        public static void Bootstrap()
        {
            if (_subscribed) return;
            _lastKnownTimetableCount = TimetableService.Timetables.Count;
            TimetableService.OnTimetablesChanged += HandleTimetablesChanged;
            _subscribed = true;
            Log.Info("[F1.15] TimetableWorkflowOrchestrator bootstrapped — suggestion auto-trigger active");
        }

        /// <summary>
        /// Detect new timetable via Count delta. Trigger suggestion services dla nowego ID.
        /// Note: nie używamy precyzyjnego event'u (`OnTimetableAdded(Timetable)`) — pre-F1.12
        /// recon foundation gap. Future: gdy F1.12 doda granular event, switch to it.
        /// </summary>
        private static void HandleTimetablesChanged()
        {
            var ttList = TimetableService.Timetables;
            int currentCount = ttList.Count;

            if (currentCount > _lastKnownTimetableCount && currentCount > 0)
            {
                // New timetable added — assume last is newest (TimetableService.AddTimetable appends)
                var newTt = ttList[currentCount - 1];
                if (newTt != null && newTt.id > 0)
                    TriggerSuggestionsForNewTimetable(newTt.id);
            }

            _lastKnownTimetableCount = currentCount;
        }

        /// <summary>
        /// Triggers all suggestion services dla świeżo zapisanego timetable.
        /// Generates Hint severity notifications (Advanced+ mode visible per F1.16).
        /// </summary>
        public static void TriggerSuggestionsForNewTimetable(int newTimetableId)
        {
            var tt = TimetableService.GetTimetable(newTimetableId);
            if (tt == null) return;

            int totalSuggestions = 0;

            // F1.12: circulation connection
            var circulations = CirculationSuggestionService.GenerateSuggestions(newTimetableId);
            totalSuggestions += circulations.Count;
            foreach (var s in circulations)
            {
                TimetableNotificationService.Add(
                    NotificationSeverity.Hint,
                    NotificationType.SuggestionAvailable,
                    s.description,
                    stopIndex: -1,
                    timeOfDaySec: 0,
                    sourceTimetableId: newTimetableId);
            }

            // F1.13b: mijanka synchronization
            var mijankas = MijankaSuggestionService.GenerateSuggestions(newTimetableId);
            totalSuggestions += mijankas.Count;
            foreach (var m in mijankas)
            {
                TimetableNotificationService.Add(
                    NotificationSeverity.Hint,
                    NotificationType.SuggestionAvailable,
                    m.description,
                    stopIndex: -1,
                    timeOfDaySec: m.originalWindowStartSec,
                    sourceTimetableId: newTimetableId);
            }

            // F1.13a CrewSwap → Personnel namespace via event hook (cross-asmdef).
            OnNewTimetableSuggestionsRequested?.Invoke(newTimetableId);

            if (totalSuggestions > 0)
                Log.Info($"[F1.15] Workflow: TR#{newTimetableId} → {totalSuggestions} suggestions queued (Circulation + Mijanka). Cross-asmdef event fired dla Personnel CrewSwap.");
        }

        /// <summary>
        /// Reset state (np. po ResetAll w SaveLoad). Zachowuje subscribed flag — tylko counter reset.
        /// </summary>
        public static void Reset()
        {
            _lastKnownTimetableCount = TimetableService.Timetables?.Count ?? 0;
            SetStep(WorkflowStep.Idle);
            ActiveWorkflowTimetableId = -1;
        }

        // ─── F1.15 pipeline workflow chain ───

        /// <summary>
        /// F1.15 polish: kick off full pipeline workflow Stwórz rozkład → Sugestia obiegu →
        /// Vehicle assignment → Crew assignment. Bazuje na <c>CreatorPreset.onConfirmed</c>
        /// callback dla chain'owania kroków.
        ///
        /// Wywołać z UI button "Stwórz pełny rozkład" w TimetableListUI (zamiast standard
        /// "Nowy" który tylko otwiera TimetableCreatorUI).
        /// </summary>
        /// <param name="kickoff">Akcja otwierająca TimetableCreatorUI z preset onConfirmed wrapping
        /// w workflow continuation. UI subscribes do tego eventu (cross-namespace dependency
        /// inversion — Workflow nie zna konkretnych UI typów).</param>
        public static void StartFullWorkflow(Action<Action<Timetable>> kickoff)
        {
            if (CurrentStep != WorkflowStep.Idle)
            {
                Log.Warn($"[F1.15] StartFullWorkflow: pipeline already w {CurrentStep}, cancel pierwszego");
                return;
            }

            SetStep(WorkflowStep.CreatingTimetable);
            ActiveWorkflowTimetableId = -1;

            // Caller (UI) opens TimetableCreatorUI z onConfirmed = AdvanceAfterTimetableSaved
            kickoff?.Invoke(AdvanceAfterTimetableSaved);
        }

        private static void AdvanceAfterTimetableSaved(Timetable savedTimetable)
        {
            if (savedTimetable == null)
            {
                Log.Warn("[F1.15] AdvanceAfterTimetableSaved: null timetable, cancel workflow");
                CancelWorkflow();
                return;
            }

            ActiveWorkflowTimetableId = savedTimetable.id;
            SetStep(WorkflowStep.ReviewingSuggestions);

            // F1.12/F1.13a/F1.13b suggestions auto-triggered przez HandleTimetablesChanged.
            // F1.15 polish back/forward: NIE auto-advance. Player używa Next button w
            // WorkflowStepIndicatorWidget żeby przejść do AssigningVehicle (Review suggestions
            // → Accept/Dismiss/Skip → Next). Real UI modal z suggestion options TBD.
        }

        /// <summary>
        /// F1.15 polish: advance forward — Next button. Validate że current step jest completable.
        /// </summary>
        public static bool AdvanceToNextStep()
        {
            switch (CurrentStep)
            {
                case WorkflowStep.CreatingTimetable:
                    Log.Warn("[F1.15] AdvanceToNextStep: still CreatingTimetable, nothing to advance to. Save timetable w kreator first.");
                    return false;
                case WorkflowStep.ReviewingSuggestions:
                    SetStep(WorkflowStep.AssigningVehicle);
                    return true;
                case WorkflowStep.AssigningVehicle:
                    Log.Warn("[F1.15] AdvanceToNextStep: AssigningVehicle wymaga explicit NotifyVehicleAssigned z UI");
                    return false;
                case WorkflowStep.AssigningCrew:
                    Log.Warn("[F1.15] AdvanceToNextStep: AssigningCrew wymaga explicit NotifyCrewAssigned z UI");
                    return false;
                default:
                    return false;
            }
        }

        /// <summary>
        /// F1.15 polish: go back — Back button. Anuluje current step, wraca do previous.
        /// </summary>
        public static bool GoBackToPreviousStep()
        {
            switch (CurrentStep)
            {
                case WorkflowStep.ReviewingSuggestions:
                    // Wraca do CreatingTimetable — player może edit kreator
                    SetStep(WorkflowStep.CreatingTimetable);
                    return true;
                case WorkflowStep.AssigningVehicle:
                    SetStep(WorkflowStep.ReviewingSuggestions);
                    return true;
                case WorkflowStep.AssigningCrew:
                    SetStep(WorkflowStep.AssigningVehicle);
                    return true;
                case WorkflowStep.CreatingTimetable:
                case WorkflowStep.Idle:
                case WorkflowStep.Done:
                default:
                    Log.Warn($"[F1.15] GoBackToPreviousStep: brak previous step dla {CurrentStep}");
                    return false;
            }
        }

        /// <summary>
        /// F1.15 polish: czy Next button enabled (step completable manually z UI).
        /// </summary>
        public static bool CanAdvanceToNextStep() => CurrentStep == WorkflowStep.ReviewingSuggestions;

        /// <summary>
        /// F1.15 polish: czy Back button enabled.
        /// </summary>
        public static bool CanGoBackToPreviousStep() => CurrentStep != WorkflowStep.Idle
            && CurrentStep != WorkflowStep.CreatingTimetable
            && CurrentStep != WorkflowStep.Done;

        /// <summary>F1.15 polish: caller (UI) zaktualizuje step po vehicle assignment complete.</summary>
        public static void NotifyVehicleAssigned()
        {
            if (CurrentStep != WorkflowStep.AssigningVehicle)
            {
                Log.Warn($"[F1.15] NotifyVehicleAssigned called w step {CurrentStep} (expected AssigningVehicle)");
                return;
            }
            SetStep(WorkflowStep.AssigningCrew);
        }

        /// <summary>F1.15 polish: caller (UI) zaktualizuje step po crew assignment complete.</summary>
        public static void NotifyCrewAssigned()
        {
            if (CurrentStep != WorkflowStep.AssigningCrew)
            {
                Log.Warn($"[F1.15] NotifyCrewAssigned called w step {CurrentStep} (expected AssigningCrew)");
                return;
            }
            SetStep(WorkflowStep.Done);
            Log.Info($"[F1.15] Full pipeline workflow complete dla TR#{ActiveWorkflowTimetableId}");
            // Reset to Idle po done — gracz może uruchomić kolejny workflow
            ActiveWorkflowTimetableId = -1;
            SetStep(WorkflowStep.Idle);
        }

        public static void CancelWorkflow()
        {
            Log.Info($"[F1.15] Cancel workflow (was at {CurrentStep})");
            ActiveWorkflowTimetableId = -1;
            SetStep(WorkflowStep.Idle);
        }

        private static void SetStep(WorkflowStep step)
        {
            if (CurrentStep == step) return;
            CurrentStep = step;
            OnStepChanged?.Invoke(step);
        }

        // ─── Helpers ───

        /// <summary>Human-readable label dla step w UI indicator.</summary>
        public static string StepLabel(WorkflowStep step) => step switch
        {
            WorkflowStep.Idle => "—",
            WorkflowStep.CreatingTimetable => "1. Tworzenie rozkładu",
            WorkflowStep.ReviewingSuggestions => "2. Sugestia obiegu",
            WorkflowStep.AssigningVehicle => "3. Przypisanie pojazdu",
            WorkflowStep.AssigningCrew => "4. Przypisanie drużyny",
            WorkflowStep.Done => "✓ Zakończono",
            _ => step.ToString()
        };
    }
}
