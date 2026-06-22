using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Core.Assistant;
using RailwayManager.SharedUI.Localization;
using RailwayManager.Timetable;

namespace RailwayManager.Personnel.Assistant
{
    /// <summary>
    /// M11 AS-4: drugi adapter delegacji — grafiki załóg. Wrapper nad
    /// CrewCirculationAutoGenerator (mózg z M8-9) karmiony realnymi TrainRunami
    /// przez CrewAutoGenInputAdapter (AS-P2).
    ///
    /// Plan() = Generate() (czysty — generator nie dotyka CrewCirculationService do Commit).
    /// Apply() waliduje świeżość: wszystkie duty z referencedTrainRunId muszą nadal istnieć
    /// w TimetableService.TrainRuns (kurs mógł zniknąć między Plan a akceptacją) — inaczej
    /// cały plan odrzucony z Warn (duties w turnusie są splecione czasowo, partial apply
    /// dałby dziurawy grafik).
    ///
    /// Deadhead: placeholder generatora (M8-9) — gdy wygenerowany, preview pokazuje
    /// jawny warning (NIE cichy bypass).
    /// </summary>
    public class CrewAutogenCapability : IAssistantCapability
    {
        public const string CapabilityId = "crew.autogen";

        public string Id => CapabilityId;
        public AssistantCapabilityCategory Category => AssistantCapabilityCategory.Personnel;
        public bool CanAutoExecute => true;

        /// <summary>AS-D6: addytywne (turnusy usuwalne przez CrewCirculationService.Delete) → auto-mode OK.</summary>
        public bool AutoModeAllowed => true;

        /// <summary>Tanio: jest dziś jakikolwiek kurs z załogą do obsadzenia.</summary>
        public bool CanExecute()
        {
            string today = GameState.CurrentDateIso;
            foreach (var run in TimetableService.TrainRuns)
            {
                if (run != null && !run.isDeliveryRun && run.runDateIso == today) return true;
            }
            return false;
        }

        public AssistantGuidance GetGuidance() => new AssistantGuidance
        {
            steps =
            {
                new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.crew_autogen.step1",
                    highlightTargetId = "depot.tab.Staff"
                },
                new AssistantGuidanceStep { messageKey = "assistant.guidance.crew_autogen.step2" }
            }
        };

        public AssistantPlan Plan()
        {
            string today = GameState.CurrentDateIso;
            var inputs = CrewAutoGenInputAdapter.BuildInputsFromTrainRuns(today);
            if (inputs.Count == 0) return null;

            // Default settings jak modal (DriverOnly) — granularność ról w panelu Turnusy.
            var preview = CrewCirculationAutoGenerator.Generate(inputs, new CrewAutoGenSettings());
            if (preview == null || preview.GeneratedCirculations.Count == 0) return null;

            var plan = new AssistantPlan
            {
                capabilityId = CapabilityId,
                title = string.Format(
                    LocalizationService.Get("assistant.plan.crew_autogen.title_format"),
                    preview.GeneratedCirculations.Count),
                effectSummary = LocalizationService.Get("assistant.plan.crew_autogen.effect"),
                costGroszy = 0,
                payload = preview,
                createdAtGameSec = (long)GameState.GameTimeSeconds
            };

            foreach (var circ in preview.GeneratedCirculations)
            {
                int duties = circ.duties?.Count ?? 0;
                plan.previewLines.Add($"{circ.name} — {duties} służb ({circ.role})");
            }
            foreach (var warning in preview.Warnings)
            {
                plan.previewLines.Add($"⚠ {warning}");
            }
            return plan;
        }

        public bool Apply(AssistantPlan plan)
        {
            if (plan == null || plan.capabilityId != CapabilityId) return false;
            if (!(plan.payload is CrewAutoGenPreview preview)) return false;

            // Walidacja świeżości: każdy referowany TrainRun nadal istnieje.
            var existingRunIds = new HashSet<int>();
            foreach (var run in TimetableService.TrainRuns)
            {
                if (run != null) existingRunIds.Add(run.id);
            }
            foreach (var circ in preview.GeneratedCirculations)
            {
                if (circ?.duties == null) continue;
                foreach (var duty in circ.duties)
                {
                    if (duty.referencedTrainRunId > 0 && !existingRunIds.Contains(duty.referencedTrainRunId))
                    {
                        Log.Warn($"[CrewAutogenCapability] Plan nieaktualny — TrainRun #{duty.referencedTrainRunId} "
                                 + "już nie istnieje (kurs zmienił się między Plan() a Apply()). Odrzucam cały plan.");
                        return false;
                    }
                }
            }

            int committed = CrewCirculationAutoGenerator.Commit(preview);
            if (committed > 0)
            {
                Log.Info($"[CrewAutogenCapability] Asystent utworzył {committed} turnusów załóg");
            }
            return committed > 0;
        }
    }
}
