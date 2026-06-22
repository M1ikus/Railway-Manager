using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Assistant;
using RailwayManager.Fleet;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Maintenance.Assistant
{
    /// <summary>
    /// M11 AS-6: adapter delegacji — odstawienie niesprawnego/zaległego taboru do warsztatu.
    /// „Deleguj naprawę zamiast dublować badge": MaintenanceAlertsUI już INFORMUJE o problemie,
    /// asystent dokłada wykonanie chore'u (dobór poziomu przeglądu + wolnego stanowiska +
    /// AssignVehicle z ruchem MM-18 po torach zajezdni).
    ///
    /// Plan() czysty (AS-D3): dobór kandydatów i stanowisk bez mutacji. Apply() deleguje do
    /// <see cref="WorkshopManager.AssignVehicle"/>, który sam re-waliduje świeżość (slot
    /// zajęty / pojazd zniknął / brak części między Plan a Apply → odmowa per pojazd, zero
    /// złamanego stanu). AutoModeAllowed zostaje false (DIM): zajęcie stanowiska wyłącza
    /// pojazd z ruchu na godziny — to nie jest akcja swobodnie odwracalna, gracz akceptuje preview.
    /// </summary>
    public class WorkshopSendCapability : IAssistantCapability
    {
        public const string CapabilityId = "maintenance.workshop";

        /// <summary>Para pojazd→stanowisko w payload planu (Apply odczytuje z powrotem).</summary>
        public class PlannedAssignment
        {
            public int vehicleId;
            public InspectionLevel level;
            public int slotId;
        }

        struct Candidate
        {
            public FleetVehicleData vehicle;
            public InspectionLevel level;
            public bool broken;
            public float progress;
        }

        public string Id => CapabilityId;
        public AssistantCapabilityCategory Category => AssistantCapabilityCategory.Fleet;
        public bool CanAutoExecute => true;

        public bool CanExecute() => MaintenanceAssistantRules.AnyVehicleNeedsWorkshop();

        public AssistantGuidance GetGuidance() => new AssistantGuidance
        {
            steps =
            {
                new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.maintenance_workshop.step1",
                    uiIntent = UIIntent.OpenWorkshopsPanel
                },
                new AssistantGuidanceStep { messageKey = "assistant.guidance.maintenance_workshop.step2" }
            }
        };

        public AssistantPlan Plan()
        {
            var wm = WorkshopManager.Instance;
            if (wm == null) return null;

            var candidates = CollectCandidates();
            if (candidates.Count == 0) return null;

            var assignments = new List<PlannedAssignment>();
            var rejectLines = new List<string>();
            var usedSlots = new HashSet<int>();

            foreach (var c in candidates)
            {
                WorkshopSlot picked = null;
                foreach (var s in wm.GetAvailableSlots(c.level))
                {
                    if (usedSlots.Contains(s.slotId)) continue;
                    if (s.maxVehicleLength > 0f && c.vehicle.lengthM > s.maxVehicleLength + 0.01f) continue;
                    picked = s;
                    break;
                }

                if (picked == null)
                {
                    rejectLines.Add(string.Format(
                        LocalizationService.Get("assistant.plan.workshop.no_slot_format"),
                        c.vehicle.seriesId, c.vehicle.evn, c.level));
                    continue;
                }

                usedSlots.Add(picked.slotId);
                assignments.Add(new PlannedAssignment
                {
                    vehicleId = c.vehicle.id,
                    level = c.level,
                    slotId = picked.slotId
                });
            }

            // Brak jakiegokolwiek wolnego stanowiska → nie ma czego delegować;
            // guidance prowadzi gracza (panel Warsztaty / hala / ZNTK).
            if (assignments.Count == 0) return null;

            var plan = new AssistantPlan
            {
                capabilityId = CapabilityId,
                title = string.Format(
                    LocalizationService.Get("assistant.plan.workshop.title_format"), assignments.Count),
                effectSummary = LocalizationService.Get("assistant.plan.workshop.effect"),
                costGroszy = 0,
                payload = assignments,
                createdAtGameSec = (long)GameState.GameTimeSeconds
            };

            foreach (var a in assignments)
            {
                var v = FleetService.GetOwnedById(a.vehicleId);
                plan.previewLines.Add(string.Format(
                    LocalizationService.Get("assistant.plan.workshop.line_format"),
                    v?.seriesId, v?.evn, a.level, a.slotId));
            }
            foreach (var line in rejectLines) plan.previewLines.Add(line);
            return plan;
        }

        public bool Apply(AssistantPlan plan)
        {
            if (plan == null || plan.capabilityId != CapabilityId) return false;
            if (!(plan.payload is List<PlannedAssignment> assignments) || assignments.Count == 0) return false;

            var wm = WorkshopManager.Instance;
            if (wm == null) return false;

            int done = 0, skipped = 0;
            foreach (var a in assignments)
            {
                if (a == null) { skipped++; continue; }
                if (wm.AssignVehicle(a.vehicleId, a.level, a.slotId)) done++;
                else skipped++;
            }

            if (skipped > 0)
            {
                Log.Warn($"[WorkshopSendCapability] Pominięto {skipped} przydziałów "
                         + "(stan zmienił się między Plan() a Apply() — AssignVehicle odmówił)");
            }
            if (done > 0)
            {
                Log.Info($"[WorkshopSendCapability] Asystent odstawił {done} pojazdów do warsztatu");
            }
            return done > 0;
        }

        /// <summary>
        /// Kandydaci do warsztatu: pojazdy STOJĄCE w zajezdni, niesprawne (próg parytetowy
        /// z CirculationService) albo z zaległym przeglądem. Poziom = najpilniejszy z
        /// harmonogramu przeglądów (niesprawny bez harmonogramu → P1). Sort: awarie przed
        /// zaległościami, dalej wg pilności.
        /// </summary>
        static List<Candidate> CollectCandidates()
        {
            var result = new List<Candidate>();
            long now = (long)GameState.GameTimeSeconds;

            foreach (var v in FleetService.OwnedVehicles)
            {
                if (v == null || v.status != FleetVehicleStatus.StoppedInDepot) continue;

                bool broken = v.conditionPercent < AssistantConstants.BrokenConditionPercent;
                var level = InspectionLevel.P1;
                float progress = 0f;
                if (v.inspections != null)
                {
                    var urgent = v.inspections.GetMostUrgent(
                        InspectionCatalog.GetForSeries(v.seriesId), now, v.mileageKm);
                    level = urgent.level;
                    progress = urgent.progress;
                }

                if (!broken && progress < 1f) continue;
                result.Add(new Candidate { vehicle = v, level = level, broken = broken, progress = progress });
            }

            result.Sort((a, b) => a.broken != b.broken
                ? (a.broken ? -1 : 1)
                : b.progress.CompareTo(a.progress));
            return result;
        }
    }

    /// <summary>Predykaty reguł Maintenance — public static dla testów EditMode.</summary>
    public static class MaintenanceAssistantRules
    {
        /// <summary>
        /// Pojazd w zajezdni „woła o warsztat": niesprawny (cond &lt; próg) ALBO zaległy
        /// przegląd (progress ≥ 1). InRepair/OutOfService wykluczone przez status
        /// (już obsłużone / w drodze na stanowisko).
        /// </summary>
        public static bool AnyVehicleNeedsWorkshop()
        {
            long now = (long)GameState.GameTimeSeconds;
            foreach (var v in FleetService.OwnedVehicles)
            {
                if (v == null || v.status != FleetVehicleStatus.StoppedInDepot) continue;
                if (v.conditionPercent < AssistantConstants.BrokenConditionPercent) return true;
                if (v.inspections == null) continue;
                var urgent = v.inspections.GetMostUrgent(
                    InspectionCatalog.GetForSeries(v.seriesId), now, v.mileageKm);
                if (urgent.progress >= 1f) return true;
            }
            return false;
        }
    }

    /// <summary>Rejestracja capability + reguły reaktywnej Maintenance (500). Idempotentne.</summary>
    public static class MaintenanceAssistantBootstrap
    {
        /// <summary>Hook testowy (RuntimeInitializeOnLoadMethod nie strzela w EditMode).</summary>
        public static void EnsureRegistered() => Register();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            if (AssistantCapabilityRegistry.Get(WorkshopSendCapability.CapabilityId) == null)
            {
                AssistantCapabilityRegistry.Register(new WorkshopSendCapability());
            }

            if (AssistantRuleRegistry.Get("reactive.brokenDown") == null)
            {
                // AS-6: najwyższa reguła reaktywna — niesprawny tabor blokuje gameplay.
                AssistantRuleRegistry.Register(new AssistantRule
                {
                    id = "reactive.brokenDown",
                    kind = AssistantRuleKind.Reactive,
                    priority = 500,
                    isActive = MaintenanceAssistantRules.AnyVehicleNeedsWorkshop,
                    capabilityId = WorkshopSendCapability.CapabilityId,
                    messageKey = "assistant.rule.broken_down"
                });
            }
        }
    }
}
