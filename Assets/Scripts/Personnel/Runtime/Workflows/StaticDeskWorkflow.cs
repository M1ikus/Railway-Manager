using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Personnel.Furniture;

namespace RailwayManager.Personnel.Workflows
{
    /// <summary>
    /// TD-025: Workflow dla ról stacjonarnych z biurkiem —
    /// Office, Research, Dispatcher, TrafficController.
    ///
    /// <para><b>Flow:</b></para>
    /// <list type="number">
    /// <item><b>OffShift</b> → status zmieniony na OnShift przez ShiftManager →
    /// <b>ComingToDepot</b> (spawn at gate, walk do dispatcher's desk lub
    /// bezpośrednio do biurka jeśli to sam dyspozytor)</item>
    /// <item><b>ReportingToDispatcher</b> → stoi 8s u dyspozytora (poza Dispatcher self)</item>
    /// <item><b>GoingToWorkstation</b> → idzie do swojego biurka (z furniture assignment)</item>
    /// <item><b>WorkingAtStation</b> → stoi przy biurku z working anim do końca zmiany</item>
    /// </list>
    ///
    /// <para>Idle bez biurka: stoi w Social room (jeśli istnieje), inaczej przy bramie.</para>
    /// </summary>
    public class StaticDeskWorkflow : IEmployeeWorkflow
    {
        public bool HandlesRole(EmployeeRole role)
        {
            return role == EmployeeRole.Office
                || role == EmployeeRole.Research
                || role == EmployeeRole.Dispatcher
                || role == EmployeeRole.TrafficController;
        }

        public void Tick(Employee e, EmployeeWalkSimulator sim, long currentGameTime)
        {
            // TD-034: trwająca czynność osobista (łazienka/przerwa) ma priorytet
            if (PersonalActivities.Tick(e, sim, currentGameTime,
                    afterArrivalLocker: null, afterMidShift: () => GoToOwnDesk(e, sim)))
                return;

            // TD-034 G: meldunek/kolejka u dyspozytora
            if (MeldunekFlow.Tick(e, sim, currentGameTime, afterMeldunek: () => GoToOwnDesk(e, sim)))
                return;

            // FIRST CALL — pracownik dopiero co dostał OnShift, jeszcze nie ma workflow
            if (e.workflowState == EmployeeWorkflowState.OffShift)
            {
                StartComing(e, sim);
                return;
            }

            // ComingToDepot — czekamy aż dotrze (callback w EnqueueTask zmieni state)
            // GoingToWorkstation — j.w.
            // GoingHome — j.w.
            if (e.workflowState == EmployeeWorkflowState.ComingToDepot
                || e.workflowState == EmployeeWorkflowState.GoingToWorkstation
                || e.workflowState == EmployeeWorkflowState.GoingHome)
            {
                return; // wait for walk callback
            }

            // WorkingAtStation — stoi i pracuje (no-op tick), tylko zapewnij working anim
            if (e.workflowState == EmployeeWorkflowState.WorkingAtStation)
            {
                if (PersonalActivities.MaybeStartMidShift(e, sim, currentGameTime)) return; // TD-034 łazienka/przerwa
                var visual = sim.GetVisual(e.employeeId);
                if (visual != null) visual.SetWorkingAnim(true);
                return;
            }
        }

        /// <summary>Spawn at gate + walk do meldunku (lub bezpośrednio do biurka dla Dispatcher).</summary>
        void StartComing(Employee e, EmployeeWalkSimulator sim)
        {
            Vector3 gatePos = DepotGateMarker.GetPosition();
            sim.SpawnEmployee(e.employeeId, gatePos);

            // TD-034 G: meldunek u dyspozytora (Dispatcher self skip; skip+notyfikacja gdy brak dyspozytora;
            // kolejka gdy zajęte) → potem własne biurko. Begin sam decyduje czy meldować (RequiresDispatcherMeldunek).
            MeldunekFlow.Begin(e, sim, afterMeldunek: () => GoToOwnDesk(e, sim));
        }

        void GoToOwnDesk(Employee e, EmployeeWalkSimulator sim)
        {
            GoToOwnDeskInternal(e, sim, fromStart: false);
        }

        void GoToOwnDeskInternal(Employee e, EmployeeWalkSimulator sim, bool fromStart)
        {
            var visual = sim.GetVisual(e.employeeId);
            if (visual != null) visual.SetWorkingAnim(false);

            // Auto-assign furniture jeśli nie ma
            if (e.assignedFurnitureId < 0)
                FurnitureAssignmentService.AssignBestFurniture(e);

            Vector3? deskPos = null;
            if (e.assignedFurnitureId >= 0)
                deskPos = FurnitureAssignmentService.GetAccessSideWorldPosition(e.assignedFurnitureId);

            // Idle bez biurka — Social room
            if (!deskPos.HasValue)
                deskPos = WorkflowLocations.GetSocialRoomIdlePosition(e.employeeId);

            if (!deskPos.HasValue)
            {
                // Ostateczny fallback: gate
                deskPos = DepotGateMarker.GetPosition();
            }

            e.workflowState = EmployeeWorkflowState.GoingToWorkstation;
            sim.EnqueueTask(new EmployeeWalkTask
            {
                employeeId = e.employeeId,
                destination = deskPos.Value,
                targetFurnitureId = e.assignedFurnitureId,
                purpose = "GoToOwnDesk",
                onArrive = () => StartWorking(e, sim)
            });
        }

        void StartWorking(Employee e, EmployeeWalkSimulator sim)
        {
            e.workflowState = EmployeeWorkflowState.WorkingAtStation;
            e.workflowStateFinishGameTime = 0L; // brak timera — pracuje aż zmiana się skończy
            var visual = sim.GetVisual(e.employeeId);
            if (visual != null) visual.SetWorkingAnim(true);
        }
    }
}
