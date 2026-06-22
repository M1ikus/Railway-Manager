using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Assistant;
using RailwayManager.Fleet;

namespace DepotSystem.Assistant
{
    /// <summary>
    /// M11 AS-3: guidance-only capability — budowa toru. Sufit eskalacji na [1] (AS-D5):
    /// layout zajezdni to creative fun gracza, asystent uczy, NIE buduje.
    /// </summary>
    public class DepotBuildTrackCapability : IAssistantCapability
    {
        public const string CapabilityId = "depot.buildTrack";

        public string Id => CapabilityId;
        public AssistantCapabilityCategory Category => AssistantCapabilityCategory.Depot;
        public bool CanAutoExecute => false;
        public bool CanExecute() => true; // budować można zawsze

        public AssistantGuidance GetGuidance() => new AssistantGuidance
        {
            steps =
            {
                new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.depot_buildtrack.step1",
                    highlightTargetId = "depot.tab.Build"
                },
                new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.depot_buildtrack.step2",
                    highlightTargetId = "depot.tool.BuildTrack"
                }
            }
        };

        public AssistantPlan Plan() => null;
        public bool Apply(AssistantPlan plan) => false;
    }

    /// <summary>
    /// M11 AS-3: guidance-only capability — zakup taboru, DIAGNOSTYCZNA (czyta DepotReadiness):
    /// przy pułapce EMU-bez-sieci pierwszy krok to ostrzeżenie (kup spalinowy albo elektryfikuj),
    /// zanim gracz wyda pieniądze. Cross-system trap ze specu („Introspekcja stanu zajezdni").
    /// </summary>
    public class FleetBuyCapability : IAssistantCapability
    {
        public const string CapabilityId = "fleet.buy";

        public string Id => CapabilityId;
        public AssistantCapabilityCategory Category => AssistantCapabilityCategory.Fleet;
        public bool CanAutoExecute => false;
        public bool CanExecute() => true;

        public AssistantGuidance GetGuidance()
        {
            var guidance = new AssistantGuidance();
            var readiness = DepotReadinessService.Current;

            // Diagnoza PRZED zakupem: zajezdnia bez sieci → ostrzeż zanim gracz kupi elektryka.
            if (readiness.evaluated && !readiness.hasElectrifiedTrack)
            {
                guidance.steps.Add(new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.fleet_buy.warn_no_catenary"
                });
            }

            guidance.steps.Add(new AssistantGuidanceStep
            {
                messageKey = "assistant.guidance.fleet_buy.step1",
                highlightTargetId = "depot.tab.Fleet"
            });
            guidance.steps.Add(new AssistantGuidanceStep
            {
                messageKey = "assistant.guidance.fleet_buy.step2"
            });
            return guidance;
        }

        public AssistantPlan Plan() => null;
        public bool Apply(AssistantPlan plan) => false;
    }

    /// <summary>M11 AS-3: guidance-only — dostawa kupionego pojazdu (status w panelu Tabor).</summary>
    public class FleetDeliverCapability : IAssistantCapability
    {
        public const string CapabilityId = "fleet.deliver";

        public string Id => CapabilityId;
        public AssistantCapabilityCategory Category => AssistantCapabilityCategory.Fleet;
        public bool CanAutoExecute => false;
        public bool CanExecute() => DepotAssistantRules.AnyVehicleInTransit();

        public AssistantGuidance GetGuidance() => new AssistantGuidance
        {
            steps =
            {
                new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.fleet_deliver.step1",
                    highlightTargetId = "depot.tab.Fleet"
                }
            }
        };

        public AssistantPlan Plan() => null;
        public bool Apply(AssistantPlan plan) => false;
    }

    /// <summary>
    /// M11 AS-6: guidance-only — stanowiska pracy (biurka) dla personelu. Reguła
    /// reactive.noDesk (Personnel) wskazuje tutaj; meblowanie to creative fun gracza
    /// (sufit AS-D5), asystent pokazuje GDZIE w UI się je stawia.
    /// </summary>
    public class DepotPlaceDeskCapability : IAssistantCapability
    {
        public const string CapabilityId = "depot.placeDesk";

        public string Id => CapabilityId;
        public AssistantCapabilityCategory Category => AssistantCapabilityCategory.Depot;
        public bool CanAutoExecute => false;
        public bool CanExecute() => true;

        public AssistantGuidance GetGuidance() => new AssistantGuidance
        {
            steps =
            {
                new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.depot_placedesk.step1",
                    highlightTargetId = "depot.tab.Build"
                },
                new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.depot_placedesk.step2",
                    highlightTargetId = "depot.tool.BuildRoom"
                }
            }
        };

        public AssistantPlan Plan() => null;
        public bool Apply(AssistantPlan plan) => false;
    }

    /// <summary>
    /// Predykaty reguł Depot — public static dla testów EditMode (lambdy w regułach
    /// delegują tutaj). Wszystkie czytają stan, NIE mutują (kontrakt AssistantRule).
    /// </summary>
    public static class DepotAssistantRules
    {
        /// <summary>Stan zajezdni ZNANY i brak jakiegokolwiek toru (preset/default layout → false od startu).</summary>
        public static bool NoTrackInKnownDepot()
        {
            var r = DepotReadinessService.Current;
            return r.evaluated && !r.hasAnyTrack;
        }

        public static bool NoVehiclesOwned() => FleetService.OwnedVehicles.Count == 0;

        public static bool AnyVehicleInTransit()
        {
            var svc = VehicleLocationService.Instance;
            if (svc == null) return false;
            return svc.GetByType(VehicleLocationType.InTransit).Count > 0;
        }

        public static bool EmuTrapActive() => DepotReadinessService.Current.EmuTrapActive;
    }

    /// <summary>
    /// Rejestracja capabilities + reguł onboardingu Depot (priorytety 1000/990/980 —
    /// sekwencja przez malejące priorytety, patrz spec „Reguły MVP") + reaktywna pułapka
    /// EMU-bez-sieci (450). Idempotentne.
    /// </summary>
    public static class DepotAssistantBootstrap
    {
        static bool _toolSignalsBridged;

        /// <summary>Hook testowy (RuntimeInitializeOnLoadMethod nie strzela w EditMode).</summary>
        public static void EnsureRegistered() => Register();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            // AS-6: most ToolActive/ToolIdle — NextStepMonitor konsumował te sygnały od AS-1b,
            // ale nikt ich nie emitował. Aktywne narzędzie (≠ Select) = cisza szeptów.
            if (!_toolSignalsBridged)
            {
                _toolSignalsBridged = true;
                DepotUIManager.OnReady += HookToolSignals;
                if (DepotUIManager.Instance != null) HookToolSignals();
            }

            if (AssistantCapabilityRegistry.Get(DepotBuildTrackCapability.CapabilityId) == null)
            {
                AssistantCapabilityRegistry.Register(new DepotBuildTrackCapability());
                AssistantCapabilityRegistry.Register(new FleetBuyCapability());
                AssistantCapabilityRegistry.Register(new FleetDeliverCapability());
                AssistantCapabilityRegistry.Register(new DepotPlaceDeskCapability());
            }

            if (AssistantRuleRegistry.Get("ob.track") == null)
            {
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "ob.track",
                    kind = AssistantRuleKind.Onboarding,
                    priority = 1000,
                    isActive = DepotAssistantRules.NoTrackInKnownDepot,
                    capabilityId = DepotBuildTrackCapability.CapabilityId,
                    messageKey = "assistant.rule.ob_track"
                });
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "ob.vehicle",
                    kind = AssistantRuleKind.Onboarding,
                    priority = 990,
                    isActive = DepotAssistantRules.NoVehiclesOwned,
                    capabilityId = FleetBuyCapability.CapabilityId,
                    messageKey = "assistant.rule.ob_vehicle"
                });
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "ob.deliver",
                    kind = AssistantRuleKind.Onboarding,
                    priority = 980,
                    isActive = DepotAssistantRules.AnyVehicleInTransit,
                    capabilityId = FleetDeliverCapability.CapabilityId,
                    messageKey = "assistant.rule.ob_deliver"
                });
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "reactive.emuTrap",
                    kind = AssistantRuleKind.Reactive,
                    priority = 450,
                    isActive = DepotAssistantRules.EmuTrapActive,
                    capabilityId = FleetBuyCapability.CapabilityId,
                    messageKey = "assistant.rule.emu_trap"
                });
            }
        }

        /// <summary>
        /// Per instancja DepotUIManager (scene reload = nowa instancja, OnReady strzela
        /// ponownie). -=/+= zapewnia idempotencję subskrypcji.
        /// </summary>
        static void HookToolSignals()
        {
            var ui = DepotUIManager.Instance;
            if (ui == null) return;
            ui.OnToolChanged -= EmitToolSignal;
            ui.OnToolChanged += EmitToolSignal;
        }

        static void EmitToolSignal(ToolMode mode)
        {
            AssistantSignals.Emit(
                mode != ToolMode.Select ? AssistantSignalKind.ToolActive : AssistantSignalKind.ToolIdle,
                "DepotUIManager");
        }
    }
}
