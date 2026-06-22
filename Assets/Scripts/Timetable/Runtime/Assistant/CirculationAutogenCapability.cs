using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Assistant;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable.Assistant
{
    /// <summary>
    /// M11 AS-2: pierwszy adapter delegacji — cienki wrapper nad CirculationAutoGenerator
    /// w kontrakcie IAssistantCapability (Plan→akceptuj→Apply, AS-D3). Dowód koncepcji
    /// szkieletu AS-1: mózg istniał (M5), asystent tylko go opakowuje.
    ///
    /// Plan() = Generate() (czysty — generator nie dotyka CirculationService), payload
    /// niesie GenerationResult z powrotem do Apply(). Apply waliduje ŚWIEŻOŚĆ każdej
    /// propozycji (gracz mógł w międzyczasie skasować rozkład / przypisać go do obiegu
    /// ręcznie) — nieaktualne pomija z Log.Warn zamiast tworzyć obieg z martwym ID.
    /// </summary>
    public class CirculationAutogenCapability : IAssistantCapability
    {
        public const string CapabilityId = "circulation.autogen";

        public string Id => CapabilityId;
        public AssistantCapabilityCategory Category => AssistantCapabilityCategory.Timetable;
        public bool CanAutoExecute => true;

        /// <summary>AS-D6: addytywne (obiegi Draft usuwalne w UI obiegów) + plan bez kosztu → auto-mode OK.</summary>
        public bool AutoModeAllowed => true;

        /// <summary>
        /// Tanio (gating panelu/monitora): są aktywne rozkłady. Czy faktycznie jest CO
        /// wygenerować, odpowiada dopiero Plan() (pełny chain-building).
        /// </summary>
        public bool CanExecute()
        {
            foreach (var t in TimetableService.Timetables)
            {
                if (t != null && t.status == TimetableStatus.Active) return true;
            }
            return false;
        }

        public AssistantGuidance GetGuidance() => new AssistantGuidance
        {
            steps =
            {
                new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.circulation_autogen.step1",
                    highlightTargetId = "depot.tab.Circulations"
                },
                new AssistantGuidanceStep { messageKey = "assistant.guidance.circulation_autogen.step2" }
            }
        };

        public AssistantPlan Plan()
        {
            // Default settings (jak [Wygeneruj auto] w CirculationListUI bez modala).
            var result = CirculationAutoGenerator.Generate();
            if (result == null || result.proposedCirculations.Count == 0) return null;

            var plan = new AssistantPlan
            {
                capabilityId = CapabilityId,
                title = string.Format(
                    LocalizationService.Get("assistant.plan.circulation_autogen.title_format"),
                    result.proposedCirculations.Count),
                effectSummary = LocalizationService.Get("assistant.plan.circulation_autogen.effect"),
                costGroszy = 0,
                payload = result,
                createdAtGameSec = (long)GameState.GameTimeSeconds
            };

            foreach (var p in result.proposedCirculations)
            {
                string vehicle = string.IsNullOrEmpty(p.vehicleAssignmentInfo) ? "" : $" — {p.vehicleAssignmentInfo}";
                plan.previewLines.Add($"{p.suggestedName} · {FormatDuration(p.totalDurationMinutes)}{vehicle}");
            }
            if (result.orphanTimetableIds.Count > 0)
            {
                plan.previewLines.Add(string.Format(
                    LocalizationService.Get("assistant.plan.circulation_autogen.orphans_format"),
                    result.orphanTimetableIds.Count));
            }
            return plan;
        }

        public bool Apply(AssistantPlan plan)
        {
            if (plan == null || plan.capabilityId != CapabilityId) return false;
            if (!(plan.payload is CirculationAutoGenerator.GenerationResult result)) return false;

            int created = 0, skipped = 0;
            foreach (var proposed in result.proposedCirculations)
            {
                if (!ProposalStillValid(proposed)) { skipped++; continue; }
                if (CirculationAutoGenerator.ApplyProposal(proposed) != null) created++;
            }

            if (skipped > 0)
            {
                Log.Warn($"[CirculationAutogenCapability] Pominięto {skipped} nieaktualnych propozycji "
                         + "(stan gry zmienił się między Plan() a Apply())");
            }
            if (created > 0)
            {
                Log.Info($"[CirculationAutogenCapability] Asystent utworzył {created} obiegów (Draft)");
            }
            return created > 0;
        }

        /// <summary>
        /// Świeżość propozycji: wszystkie rozkłady nadal istnieją, są Active i nie wpadły
        /// w międzyczasie do innego obiegu (ręcznie albo przez drugi Apply).
        /// </summary>
        private static bool ProposalStillValid(CirculationAutoGenerator.ProposedCirculation proposed)
        {
            if (proposed == null || proposed.timetableIds.Count == 0) return false;

            foreach (var tid in proposed.timetableIds)
            {
                var tt = TimetableService.GetTimetable(tid);
                if (tt == null || tt.status != TimetableStatus.Active) return false;
            }
            foreach (var c in CirculationService.Circulations)
            {
                if (c?.steps == null) continue;
                foreach (var s in c.steps)
                {
                    if (proposed.timetableIds.Contains(s.timetableId)) return false;
                }
            }
            return true;
        }

        private static string FormatDuration(int minutes)
        {
            if (minutes < 60) return $"{minutes} min";
            return $"{minutes / 60}h {minutes % 60:00}m";
        }
    }

    /// <summary>
    /// Rejestracja capability Timetable w Core registry (wzorzec SaveActionsHook —
    /// moduł rejestruje się sam, asystent nie zna Timetable). Idempotentne: enter-play
    /// bez domain reload = statics przeżywają, metoda strzela ponownie.
    /// </summary>
    public static class TimetableAssistantBootstrap
    {
        static bool _signalsBridged;

        /// <summary>Hook testowy (RuntimeInitializeOnLoadMethod nie strzela w EditMode).</summary>
        public static void EnsureRegistered() => Register();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            // AS-6: bridges sygnałów — sugestie Timetable, które dotąd emitowały eventy
            // W PRÓŻNIĘ (zero konsumentów UI), trafiają do asystenta jako szepty advisora.
            if (!_signalsBridged)
            {
                _signalsBridged = true;
                Suggestions.CirculationSuggestionService.OnSuggestionAvailable += s =>
                    AssistantSignals.Emit(AssistantSignalKind.Suggestion, "CirculationSuggestionService",
                        contextKey: s.contextKey, payload: s.description);
                Suggestions.MijankaSuggestionService.OnSuggestionAvailable += s =>
                    AssistantSignals.Emit(AssistantSignalKind.Suggestion, "MijankaSuggestionService",
                        contextKey: s.contextKey, payload: s.description);
                Notifications.TimetableNotificationService.OnAdded += rec =>
                {
                    // Tylko problemy (Warning/Error) — Info to szum, nie advisor.
                    if (rec.severity == Notifications.NotificationSeverity.Warning
                        || rec.severity == Notifications.NotificationSeverity.Error)
                    {
                        AssistantSignals.Emit(AssistantSignalKind.Suggestion, "TimetableNotificationService",
                            contextKey: $"ttnotif:{rec.sourceTimetableId}:{rec.type}",
                            payload: rec.message);
                    }
                };
            }

            if (AssistantCapabilityRegistry.Get(CirculationAutogenCapability.CapabilityId) == null)
            {
                AssistantCapabilityRegistry.Register(new CirculationAutogenCapability());
                AssistantCapabilityRegistry.Register(new TimetableCreateCapability());
                AssistantCapabilityRegistry.Register(new EconomyReviewCapability());
            }

            // AS-3: reguły onboardingu Timetable (sekwencja 970/960 — patrz spec „Reguły MVP").
            if (AssistantRuleRegistry.Get("ob.timetable") == null)
            {
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "ob.timetable",
                    kind = AssistantRuleKind.Onboarding,
                    priority = 970,
                    isActive = TimetableAssistantRules.NoActiveTimetables,
                    capabilityId = TimetableCreateCapability.CapabilityId,
                    messageKey = "assistant.rule.ob_timetable"
                });
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "ob.circulation",
                    kind = AssistantRuleKind.Onboarding,
                    priority = 960,
                    isActive = TimetableAssistantRules.ActiveTimetablesButNoCirculations,
                    capabilityId = CirculationAutogenCapability.CapabilityId,
                    messageKey = "assistant.rule.ob_circulation"
                });

                // AS-6: reguły reaktywne advisora („Reguły MVP" — orphan/idle/nierentowna/nuda).
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "reactive.orphanTimetable",
                    kind = AssistantRuleKind.Reactive,
                    priority = 350,
                    isActive = TimetableAssistantRules.AnyOrphanTimetable,
                    capabilityId = CirculationAutogenCapability.CapabilityId,
                    messageKey = "assistant.rule.orphan_timetable"
                });
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "reactive.vehicleIdle",
                    kind = AssistantRuleKind.Reactive,
                    priority = 300,
                    isActive = TimetableAssistantRules.AnyTractionVehicleIdle,
                    capabilityId = CirculationAutogenCapability.CapabilityId,
                    messageKey = "assistant.rule.vehicle_idle"
                });
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "reactive.unprofitable",
                    kind = AssistantRuleKind.Reactive,
                    priority = 250,
                    isActive = TimetableAssistantRules.AnyLineUnprofitable,
                    capabilityId = EconomyReviewCapability.CapabilityId,
                    messageKey = "assistant.rule.line_unprofitable"
                });
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "reactive.bored",
                    kind = AssistantRuleKind.Reactive,
                    priority = 50, // najniższy — wygrywa tylko gdy nic innego nie gra
                    isActive = TimetableAssistantRules.BoredomActive,
                    capabilityId = TimetableCreateCapability.CapabilityId, // planner = „nowa linia"
                    messageKey = "assistant.rule.bored"
                });
            }
        }
    }
}
