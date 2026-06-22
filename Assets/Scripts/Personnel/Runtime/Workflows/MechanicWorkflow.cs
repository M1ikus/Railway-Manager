using DepotSystem.Furniture.Placement;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Maintenance;

namespace RailwayManager.Personnel.Workflows
{
    /// <summary>
    /// TD-025: Mechanik workflow.
    ///
    /// <para><b>Flow:</b></para>
    /// <list type="number">
    /// <item><b>OffShift</b> → spawn at gate → <b>ComingToDepot</b> → idzie do dyspozytora</item>
    /// <item><b>ReportingToDispatcher</b> (8s)</item>
    /// <item>Lookup pierwszy <see cref="WorkshopSlot"/> z <c>state == Inspecting</c>
    /// gdzie ten mechanik jest assigned (przez <see cref="WorkshopAssignmentService"/>) →
    /// <b>GoingToWorkstation</b> → walk do ServicePit position</item>
    /// <item><b>WorkingAtStation</b> → stoi przy slocie aż state != Inspecting</item>
    /// <item>Gdy slot Complete → lookup następnego przypisanego slotu z Inspecting.
    /// Brak → idle w Social room (<b>WorkingMobile</b> placeholder, brak active slotu).</item>
    /// </list>
    /// </summary>
    public class MechanicWorkflow : IEmployeeWorkflow
    {
        public bool HandlesRole(EmployeeRole role) => role == EmployeeRole.Mechanic;

        public void Tick(Employee e, EmployeeWalkSimulator sim, long currentGameTime)
        {
            // TD-034: trwająca czynność osobista (przebranie/łazienka/przerwa) ma priorytet
            if (PersonalActivities.Tick(e, sim, currentGameTime,
                    afterArrivalLocker: () => ProceedAfterArrival(e, sim),
                    afterMidShift: () => GoToActiveSlot(e, sim)))
                return;

            // TD-034 G: meldunek/kolejka u dyspozytora
            if (MeldunekFlow.Tick(e, sim, currentGameTime, afterMeldunek: () => GoToActiveSlot(e, sim)))
                return;

            if (e.workflowState == EmployeeWorkflowState.OffShift)
            {
                StartComing(e, sim);
                return;
            }

            if (e.workflowState == EmployeeWorkflowState.ComingToDepot
                || e.workflowState == EmployeeWorkflowState.GoingToWorkstation
                || e.workflowState == EmployeeWorkflowState.GoingHome
                || e.workflowState == EmployeeWorkflowState.WorkingMobile)
            {
                return; // wait for walk callback
            }

            if (e.workflowState == EmployeeWorkflowState.WorkingAtStation)
            {
                // Sprawdź czy slot jeszcze ma state == Inspecting; jeśli nie → następny lub idle
                var slot = FindSlotById(e.workflowTargetId);
                if (slot == null || slot.state != WorkshopSlotState.Inspecting)
                {
                    GoToActiveSlot(e, sim);
                    return;
                }
                if (PersonalActivities.MaybeStartMidShift(e, sim, currentGameTime)) return; // TD-034 łazienka/przerwa
                var visual = sim.GetVisual(e.employeeId);
                if (visual != null) visual.SetWorkingAnim(true);
                return;
            }
        }

        void StartComing(Employee e, EmployeeWalkSimulator sim)
        {
            Vector3 gatePos = DepotGateMarker.GetPosition();
            sim.SpawnEmployee(e.employeeId, gatePos);

            // TD-034: najpierw przebranie w robocze przy szafce (rola operacyjna)
            if (PersonalActivities.TryBeginArrivalLocker(e, sim)) return;
            ProceedAfterArrival(e, sim);
        }

        void ProceedAfterArrival(Employee e, EmployeeWalkSimulator sim)
        {
            // TD-034 G: meldunek u dyspozytora (skip+notyfikacja gdy brak; kolejka gdy zajęte) → potem slot
            MeldunekFlow.Begin(e, sim, afterMeldunek: () => GoToActiveSlot(e, sim));
        }

        void GoToActiveSlot(Employee e, EmployeeWalkSimulator sim)
        {
            var visual = sim.GetVisual(e.employeeId);
            if (visual != null) visual.SetWorkingAnim(false);

            // Znajdź pierwszy slot z state==Inspecting do którego mechanik jest assigned
            var slot = FindFirstAssignedInspectingSlot(e);
            if (slot != null)
            {
                Vector3? slotPos = GetSlotPosition(slot);
                if (slotPos.HasValue)
                {
                    e.workflowState = EmployeeWorkflowState.GoingToWorkstation;
                    e.workflowTargetId = slot.slotId;
                    sim.EnqueueTask(new EmployeeWalkTask
                    {
                        employeeId = e.employeeId,
                        destination = slotPos.Value,
                        purpose = $"Mechanic→Slot#{slot.slotId}",
                        onArrive = () => StartWorking(e, sim)
                    });
                    return;
                }
            }

            // Brak active slotu → idle w Social room
            e.workflowTargetId = -1;
            Vector3? idlePos = WorkflowLocations.GetSocialRoomIdlePosition(e.employeeId);
            if (!idlePos.HasValue) idlePos = DepotGateMarker.GetPosition();

            e.workflowState = EmployeeWorkflowState.WorkingMobile; // mechanik chodzi/czeka
            sim.EnqueueTask(new EmployeeWalkTask
            {
                employeeId = e.employeeId,
                destination = idlePos.Value,
                purpose = "Mechanic→Social(idle)",
                onArrive = () =>
                {
                    e.workflowState = EmployeeWorkflowState.WorkingAtStation;
                    var v = sim.GetVisual(e.employeeId);
                    if (v != null) v.SetWorkingAnim(false); // idle nie working anim
                }
            });
        }

        void StartWorking(Employee e, EmployeeWalkSimulator sim)
        {
            e.workflowState = EmployeeWorkflowState.WorkingAtStation;
            e.workflowStateFinishGameTime = 0L;
            var visual = sim.GetVisual(e.employeeId);
            if (visual != null) visual.SetWorkingAnim(true);
        }

        static WorkshopSlot FindFirstAssignedInspectingSlot(Employee e)
        {
            var wm = WorkshopManager.Instance;
            if (wm == null) return null;
            if (e.assignedWorkshopSlotIds == null || e.assignedWorkshopSlotIds.Count == 0) return null;

            foreach (var slot in wm.Slots)
            {
                if (slot.state != WorkshopSlotState.Inspecting) continue;
                if (!e.assignedWorkshopSlotIds.Contains(slot.slotId)) continue;
                return slot;
            }
            return null;
        }

        static WorkshopSlot FindSlotById(int slotId)
        {
            var wm = WorkshopManager.Instance;
            if (wm == null || slotId < 0) return null;
            foreach (var slot in wm.Slots)
                if (slot.slotId == slotId) return slot;
            return null;
        }

        static Vector3? GetSlotPosition(WorkshopSlot slot)
        {
            var placer = FurniturePlacer.Instance;
            if (placer == null) return null;
            var instance = placer.GetInstance(slot.servicePitInstanceId);
            if (instance == null) return null;
            return instance.position;
        }
    }
}
