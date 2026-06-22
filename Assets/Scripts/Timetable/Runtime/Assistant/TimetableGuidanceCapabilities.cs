using RailwayManager.Core.Assistant;
using RailwayManager.Fleet;
using Econ = RailwayManager.Timetable.Economy;

namespace RailwayManager.Timetable.Assistant
{
    /// <summary>
    /// M11 AS-3: guidance-only capability — tworzenie rozkładu. CanAutoExecute=false do czasu
    /// AS-5 (planner rozkład-od-relacji da tej capability mózg: Plan() = trasa+tabor+kursy).
    /// </summary>
    public class TimetableCreateCapability : IAssistantCapability
    {
        public const string CapabilityId = "timetable.create";

        public string Id => CapabilityId;
        public AssistantCapabilityCategory Category => AssistantCapabilityCategory.Timetable;
        public bool CanAutoExecute => false; // AS-5 zmieni na true (planner)
        public bool CanExecute() => true;    // kreator zawsze dostępny

        public AssistantGuidance GetGuidance() => new AssistantGuidance
        {
            steps =
            {
                // AS-5c: guidance otwiera planner połączeń (mózg AS-5) — gracz wybiera
                // relację, asystent liczy tabor/warianty/bilans i tworzy rozkład.
                new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.timetable_create.step1",
                    uiIntent = RailwayManager.Core.UIIntent.OpenRelationPlanner
                },
                new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.timetable_create.step2",
                    highlightTargetId = "depot.tab.Schedules"
                }
            }
        };

        public AssistantPlan Plan() => null;
        public bool Apply(AssistantPlan plan) => false;
    }

    /// <summary>
    /// M11 AS-6: guidance-only capability — przegląd rentowności linii (panel Finanse,
    /// per-line breakdown). Sufit na [1]: decyzje ekonomiczne (zawieszenie obiegu, zmiana
    /// taktu, ceny, dotacje) to strategia gracza — asystent pokazuje GDZIE patrzeć, nie decyduje.
    /// </summary>
    public class EconomyReviewCapability : IAssistantCapability
    {
        public const string CapabilityId = "economy.review";

        public string Id => CapabilityId;
        public AssistantCapabilityCategory Category => AssistantCapabilityCategory.Economy;
        public bool CanAutoExecute => false;
        public bool CanExecute() => true;

        public AssistantGuidance GetGuidance() => new AssistantGuidance
        {
            steps =
            {
                new AssistantGuidanceStep
                {
                    messageKey = "assistant.guidance.economy_review.step1",
                    uiIntent = RailwayManager.Core.UIIntent.OpenFinancesPanel
                },
                new AssistantGuidanceStep { messageKey = "assistant.guidance.economy_review.step2" }
            }
        };

        public AssistantPlan Plan() => null;
        public bool Apply(AssistantPlan plan) => false;
    }

    /// <summary>Predykaty reguł Timetable — public static dla testów EditMode.</summary>
    public static class TimetableAssistantRules
    {
        public static bool NoActiveTimetables()
        {
            foreach (var t in TimetableService.Timetables)
            {
                if (t != null && t.status == TimetableStatus.Active) return false;
            }
            return true;
        }

        /// <summary>Są aktywne rozkłady, ale zero obiegów — czas spiąć (capability z mózgiem AS-2).</summary>
        public static bool ActiveTimetablesButNoCirculations()
        {
            return !NoActiveTimetables() && CirculationService.Circulations.Count == 0;
        }

        // ── AS-6: predykaty reaktywne advisora ──

        /// <summary>
        /// Aktywny rozkład poza jakimkolwiek obiegiem, gdy obiegi już istnieją (zero obiegów
        /// pokrywa onboarding ob.circulation). Reużywa CirculationService.GetOrphanedTimetables.
        /// </summary>
        public static bool AnyOrphanTimetable()
        {
            return CirculationService.Circulations.Count > 0
                && CirculationService.GetOrphanedTimetables().Count > 0;
        }

        /// <summary>
        /// Pojazd trakcyjny stoi w zajezdni bez przydziału do żadnego obiegu (idle), gdy
        /// obiegi już istnieją. Wagony (PassengerCar) pominięte — nie jeżdżą same; niesprawne
        /// pokrywa reactive.brokenDown (wyższy priorytet wygrywa u monitora).
        /// </summary>
        public static bool AnyTractionVehicleIdle()
        {
            if (CirculationService.Circulations.Count == 0) return false;

            foreach (var v in FleetService.OwnedVehicles)
            {
                if (v == null || v.type == FleetVehicleType.PassengerCar) continue;
                if (v.status != FleetVehicleStatus.StoppedInDepot) continue;
                if (!IsAssignedToAnyCirculation(v.id)) return true;
            }
            return false;
        }

        static bool IsAssignedToAnyCirculation(int vehicleId)
        {
            foreach (var c in CirculationService.Circulations)
            {
                if (c == null) continue;
                if (c.assignedVehicleIds != null && c.assignedVehicleIds.Contains(vehicleId)) return true;
                if (c.vehicleAssignmentsPerDay != null)
                {
                    foreach (var kvp in c.vehicleAssignmentsPerDay)
                    {
                        if (kvp.Value != null && kvp.Value.Contains(vehicleId)) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Linia (obieg) na minusie przez N OSTATNICH dni z rzędu (N = AssistantConstants.
        /// UnprofitableLineDays). Czyta dzienne archiwum EconomyManager.History per-line.
        /// </summary>
        public static bool AnyLineUnprofitable()
        {
            var em = Econ.EconomyManager.Instance;
            if (em == null) return false;

            var history = em.History;
            int n = AssistantConstants.UnprofitableLineDays;
            if (history == null || history.Count < n) return false;

            var lastDay = history[history.Count - 1];
            if (lastDay?.perLine == null) return false;

            foreach (var kvp in lastDay.perLine)
            {
                if (Net(kvp.Value) >= 0) continue;

                bool allNegative = true;
                for (int i = history.Count - n; i < history.Count - 1 && allNegative; i++)
                {
                    var day = history[i];
                    if (day?.perLine == null
                        || !day.perLine.TryGetValue(kvp.Key, out var lb)
                        || Net(lb) >= 0)
                    {
                        allNegative = false;
                    }
                }
                if (allNegative) return true;
            }
            return false;
        }

        static long Net(Econ.LineBalance lb)
            => lb == null ? 0 : lb.revenueGroszy + lb.subsidiesGroszy - lb.costsGroszy;

        // ── AS-6: nuda („rozważ nową linię") ──

        static double _boredomHeldSinceRealSec = -1;

        /// <summary>
        /// Gra skonfigurowana (rozkłady + obiegi) i toczy się nieprzerwanie od
        /// BoredomHoldRealSec — advisor podsuwa rozwój. Priorytet 50 (najniższy) =
        /// monitor wybierze nudę tylko, gdy ŻADNA inna reguła nie jest aktywna.
        /// MVP proxy: nie mierzy bezczynności inputu (doprecyzowanie → M-Balance).
        /// </summary>
        public static bool BoredomActive()
        {
            bool configured = !NoActiveTimetables() && CirculationService.Circulations.Count > 0;
            return BoredomCore(configured, UnityEngine.Time.realtimeSinceStartupAsDouble,
                ref _boredomHeldSinceRealSec);
        }

        /// <summary>Czysty rdzeń (EditMode): zegar trzymania startuje przy spełnieniu warunków, reset przy zerwaniu.</summary>
        public static bool BoredomCore(bool configured, double nowRealSec, ref double heldSinceRealSec)
        {
            if (!configured)
            {
                heldSinceRealSec = -1;
                return false;
            }
            if (heldSinceRealSec < 0)
            {
                heldSinceRealSec = nowRealSec;
                return false;
            }
            return nowRealSec - heldSinceRealSec >= AssistantConstants.BoredomHoldRealSec;
        }

        /// <summary>Reset zegara nudy (testy / nowa gra).</summary>
        public static void ResetBoredomClock() => _boredomHeldSinceRealSec = -1;
    }
}
