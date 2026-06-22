using System.Collections.Generic;
using DepotSystem;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.Maintenance;
using RailwayManager.Personnel.Workflows;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// TD-025 — diagnostic + smoke test pełnego loop'a pracownika w 3D depot.
    ///
    /// <para>Komponent w Personnel asmdef (widzi Depot + Timetable + Fleet — wszystkie
    /// systemy z którymi workflow integruje).</para>
    ///
    /// <para><b>Test scenarios via ContextMenu:</b></para>
    /// <list type="number">
    /// <item><b>Setup #1 — Office staff:</b> Hire Office + Dispatcher → meldunek → biurko</item>
    /// <item><b>Setup #2 — Mechanic + slot:</b> Hire Mechanic + symuluj slot Inspecting</item>
    /// <item><b>Setup #3 — Cleaner + dirty fleet:</b> Hire Cleaner + dirty depot → walk loop</item>
    /// <item><b>Setup #4 — Driver + imminent duty:</b> Hire Driver + CrewCirculation z duty
    /// za 30 min → przyjdzie do depot, melduje się, czeka, embed</item>
    /// <item><b>Report:</b> tabela workflowState per pracownik + counts per stan</item>
    /// </list>
    /// </summary>
    public class PersonnelLifeLoopSmokeTest : MonoBehaviour
    {
        [ContextMenu("TD-025 #1: Setup Office + Dispatcher (meldunek test)")]
        public void Setup1_OfficeStaff()
        {
            HireDummy(EmployeeRole.Dispatcher, 3);
            HireDummy(EmployeeRole.Office, 3);
            Log.Info("[TD-025 Test #1] Hired 1 Dispatcher + 1 Office. Trigger 'Force daily tick' → " +
                     "Office should walk to Dispatcher desk → meldunek → biurko.");
        }

        [ContextMenu("TD-025 #2: Setup Mechanic + simulate slot Inspecting")]
        public void Setup2_MechanicSlot()
        {
            HireDummy(EmployeeRole.Mechanic, 3);
            var wm = WorkshopManager.Instance;
            if (wm == null || wm.Slots.Count == 0)
            {
                Log.Warn("[TD-025 Test #2] No WorkshopSlots — build Hall + ServicePit first.");
                return;
            }
            // Find a mechanic + assign to first slot
            Employee mech = null;
            foreach (var e in PersonnelService.Employees)
            {
                if (e.role == EmployeeRole.Mechanic && e.IsActive) { mech = e; break; }
            }
            if (mech == null) { Log.Warn("[TD-025 Test #2] No mechanic — hire one first."); return; }

            int slotId = wm.Slots[0].slotId;
            WorkshopAssignmentService.Assign(mech.employeeId, slotId);

            // Force slot.state = Inspecting (debug — simulate active maintenance)
            wm.Slots[0].state = WorkshopSlotState.Inspecting;
            wm.Slots[0].occupyingVehicleId = 999; // fake

            Log.Info($"[TD-025 Test #2] Assigned mechanic #{mech.employeeId} to slot #{slotId}, " +
                     "forced slot Inspecting. Workflow should walk mechanic to slot.");
        }

        [ContextMenu("TD-025 #3: Setup Cleaner + dirty all depot vehicles")]
        public void Setup3_CleanerDirty()
        {
            HireDummy(EmployeeRole.Cleaner, 3);
            CleaningService.Instance?.DebugDirtyAll();
            Log.Info("[TD-025 Test #3] Hired Cleaner + dirtied all depot vehicles. " +
                     "Workflow should walk cleaner vehicle-to-vehicle 60s each.");
        }

        [ContextMenu("TD-025 #4: Setup Driver + duty 30 min ahead")]
        public void Setup4_DriverImminent()
        {
            HireDummy(EmployeeRole.Driver, 3);
            Employee driver = null;
            foreach (var e in PersonnelService.Employees)
            {
                if (e.role == EmployeeRole.Driver && e.IsActive) { driver = e; break; }
            }
            if (driver == null) { Log.Warn("[TD-025 Test #4] No driver."); return; }

            // Create CrewCirculation z duty za 30 min (od bieżącego game time)
            long currentGameSec = (long)GameState.GameTimeSeconds;
            long dutyStartSec = currentGameSec + 25 * 60; // 25 min ahead (poniżej 30 min lead → IsImminent=true)

            string hhmmss = System.TimeSpan.FromSeconds(dutyStartSec).ToString(@"hh\:mm\:ss");
            string endHhmmss = System.TimeSpan.FromSeconds(dutyStartSec + 3600).ToString(@"hh\:mm\:ss");

            var c = CrewCirculationService.Create("TD-025-Test", EmployeeRole.Driver);
            if (c == null) { Log.Warn("[TD-025 Test #4] Failed to create CrewCirculation."); return; }

            CrewCirculationService.AddDuty(c.crewCirculationId, new CrewDuty
            {
                kind = CrewDutyKind.Service,
                dayOffset = 0,
                startTimeIso = hhmmss,
                endTimeIso = endHhmmss,
                startStationName = "Test Origin",
                endStationName = "Test Dest",
                referencedTrainRunId = -1
            });
            CrewCirculationService.AssignEmployee(c.crewCirculationId, driver.employeeId);
            bool activated = CrewCirculationService.Activate(c.crewCirculationId);
            if (!activated)
                Log.Warn("[TD-025 Test #4] Activate failed — validator errors. Workflow.Tick will not pick this turnus.");

            Log.Info($"[TD-025 Test #4] Driver #{driver.employeeId}, duty start={hhmmss}, " +
                     $"current game time {currentGameSec}s → IsImminent (delta {dutyStartSec - currentGameSec}s, lead {PersonnelBalanceConstants.CrewReportLeadMinutes}min).");
        }

        [ContextMenu("TD-025: Report workflowState distribution")]
        public void ReportWorkflowStates()
        {
            var counts = new Dictionary<EmployeeWorkflowState, int>();
            int total = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (e == null) continue;
                if (!RoleDefinitions.SpawnsAsAgentInDepot(e.role)) continue;
                total++;
                counts.TryGetValue(e.workflowState, out var c);
                counts[e.workflowState] = c + 1;
            }
            Log.Info($"[TD-025] Workflow distribution ({total} agents):");
            foreach (var kv in counts)
                Log.Info($"  {kv.Key}: {kv.Value}");

            Log.Info("[TD-025] Per-employee detail:");
            foreach (var e in PersonnelService.Employees)
            {
                if (e == null) continue;
                if (!RoleDefinitions.SpawnsAsAgentInDepot(e.role)) continue;
                long now = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;
                long finishIn = e.workflowStateFinishGameTime > 0 ? (e.workflowStateFinishGameTime - now) : 0;
                Log.Info($"  #{e.employeeId,-3} {e.DisplayShortName,-20} {e.role,-18} " +
                         $"status={e.status,-12} workflow={e.workflowState,-22} target={e.workflowTargetId} " +
                         $"finishIn={finishIn}s");
            }
        }

        [ContextMenu("TD-025: Force PersonnelDispatcher3D.ResolveAllForDay")]
        public void ForceResolve()
        {
            PersonnelDispatcher3D.Instance?.ResolveAllForDay();
            Log.Info("[TD-025] Forced ResolveAllForDay — pracownicy OnShift powinni dostać workflow start.");
        }

        [ContextMenu("TD-025: Report PathGraph status")]
        public void ReportPathGraph()
        {
            var pg = Object.FindAnyObjectByType<PathGraph>();
            if (pg == null)
            {
                Log.Warn("[TD-025] No PathGraph in active scene — pracownicy będą używać straight-line fallback.");
                return;
            }
            Log.Info($"[TD-025] PathGraph found: {pg.Nodes.Count} nodes, {pg.Edges.Count} edges.");
        }

        void HireDummy(EmployeeRole role, int skill)
        {
            var random = new System.Random();
            var (first, last, _) = PolishNamesCatalog.GetRandomFullName(random);
            int age = 30 + random.Next(20);
            string birthDate;
            try { birthDate = IsoTime.ParseDate(GameState.CurrentDateIso).AddYears(-age).ToString("yyyy-MM-dd"); }
            catch { birthDate = "1990-01-01"; }

            var terms = new HireTerms
            {
                firstName = first, lastName = last, age = age, birthDateIso = birthDate,
                role = role, skill = skill,
                negotiatedSalaryGroszy = RoleDefinitions.GetExpectedSalaryGroszy(role, skill),
                initialShift = ShiftType.Morning,
                initialCycle = WorkCyclePattern.Cycle5_2
            };
            PersonnelService.Hire(terms);
        }
    }
}
