using UnityEngine;
using RailwayManager.Core.Assistant;
using RailwayManager.Fleet;

namespace RailwayManager.Personnel.Assistant
{
    /// <summary>
    /// M11 AS-3: guidance-only capability — zatrudnienie maszynisty (panel Personel →
    /// Rekrutacja). Sufit na [1]; auto-rekrutacja to nie chore tylko decyzja kadrowa gracza.
    /// </summary>
    public class PersonnelHireCapability : IAssistantCapability
    {
        public const string CapabilityId = "personnel.hire";

        public string Id => CapabilityId;
        public AssistantCapabilityCategory Category => AssistantCapabilityCategory.Personnel;
        public bool CanAutoExecute => false;
        public bool CanExecute() => true;

        public AssistantGuidance GetGuidance() => new AssistantGuidance
        {
            steps =
            {
                new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.personnel_hire.step1",
                    highlightTargetId = "depot.tab.Staff"
                },
                new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.personnel_hire.step2"
                }
            }
        };

        public AssistantPlan Plan() => null;
        public bool Apply(AssistantPlan plan) => false;
    }

    /// <summary>Predykaty reguł Personnel — public static dla testów EditMode.</summary>
    public static class PersonnelAssistantRules
    {
        /// <summary>Jest tabor, ale zero aktywnych maszynistów — pociąg nie pojedzie bez załogi.</summary>
        public static bool FleetButNoDrivers()
        {
            if (FleetService.OwnedVehicles.Count == 0) return false;
            return PersonnelService.CountActiveByRole(EmployeeRole.Driver) == 0;
        }

        /// <summary>Obiegi taboru są, ale zero turnusów załóg — kursy pojadą bez grafików (AS-4).</summary>
        public static bool CirculationsButNoCrewSchedules()
        {
            return RailwayManager.Timetable.CirculationService.Circulations.Count > 0
                && CrewCirculationService.All.Count == 0;
        }

        /// <summary>
        /// AS-6: aktywny pracownik wymagający stanowiska bez przypisanego mebla
        /// (ten sam predykat co alert „⚠ Brak biurka" w MyStaff — MF-12).
        /// </summary>
        public static bool AnyEmployeeWithoutRequiredDesk()
        {
            foreach (var e in PersonnelService.Employees)
            {
                if (e != null && e.IsActive
                    && Furniture.FurnitureAssignmentService.IsIdleWithoutFurniture(e))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>Rejestracja capability + reguły onboardingu Personnel (950). Idempotentne.</summary>
    public static class PersonnelAssistantBootstrap
    {
        static bool _signalsBridged;

        /// <summary>Hook testowy (RuntimeInitializeOnLoadMethod nie strzela w EditMode).</summary>
        public static void EnsureRegistered() => Register();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            // AS-6: bridge sugestii zamiany załóg do asystenta (eventy dotąd w próżnię).
            if (!_signalsBridged)
            {
                _signalsBridged = true;
                Suggestions.CrewSwapSuggestionService.OnSuggestionAvailable += s =>
                    AssistantSignals.Emit(AssistantSignalKind.Suggestion, "CrewSwapSuggestionService",
                        contextKey: s.contextKey, payload: s.description);
            }

            if (AssistantCapabilityRegistry.Get(PersonnelHireCapability.CapabilityId) == null)
            {
                AssistantCapabilityRegistry.Register(new PersonnelHireCapability());
                AssistantCapabilityRegistry.Register(new CrewAutogenCapability());
            }

            if (AssistantRuleRegistry.Get("ob.crew") == null)
            {
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "ob.crew",
                    kind = AssistantRuleKind.Onboarding,
                    priority = 950,
                    isActive = PersonnelAssistantRules.FleetButNoDrivers,
                    capabilityId = PersonnelHireCapability.CapabilityId,
                    messageKey = "assistant.rule.ob_crew"
                });
                // AS-4: reaktywna — obiegi bez grafików załóg (capability z mózgiem:
                // eskalacja stuck oferuje gotowy plan turnusów).
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "reactive.crewless",
                    kind = AssistantRuleKind.Reactive,
                    priority = 400,
                    isActive = PersonnelAssistantRules.CirculationsButNoCrewSchedules,
                    capabilityId = CrewAutogenCapability.CapabilityId,
                    messageKey = "assistant.rule.crew_missing"
                });
                // AS-6: „dołóż akcję nad alertem Brak biurka" — guidance prowadzi do
                // meblowania (capability Depot; Personnel tylko diagnozuje brak).
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "reactive.noDesk",
                    kind = AssistantRuleKind.Reactive,
                    priority = 200,
                    isActive = PersonnelAssistantRules.AnyEmployeeWithoutRequiredDesk,
                    capabilityId = DepotSystem.Assistant.DepotPlaceDeskCapability.CapabilityId,
                    messageKey = "assistant.rule.no_desk"
                });
            }
        }
    }
}
