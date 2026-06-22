using DepotSystem.OutdoorEquipment;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Personnel.Workflows
{
    /// <summary>
    /// TD-025: WashBay worker workflow.
    ///
    /// <para><b>Flow:</b></para>
    /// <list type="number">
    /// <item>Spawn at gate → ComingToDepot → meldunek u dyspozytora (8s)</item>
    /// <item>Lookup pierwszy <see cref="OutdoorJob"/> typu Wash w stanie
    /// <see cref="OutdoorJobState.Servicing"/> → walk do WashZone (equipment centroid)</item>
    /// <item><b>WorkingAtStation</b> dopóki job state == Servicing</item>
    /// <item>Gdy job Complete → następny job lub idle w Social room</item>
    /// </list>
    /// </summary>
    public class WashBayWorkflow : IEmployeeWorkflow
    {
        public bool HandlesRole(EmployeeRole role) => role == EmployeeRole.WashBay;

        public void Tick(Employee e, EmployeeWalkSimulator sim, long currentGameTime)
        {
            // TD-034: trwająca czynność osobista (przebranie/łazienka/przerwa) ma priorytet
            if (PersonalActivities.Tick(e, sim, currentGameTime,
                    afterArrivalLocker: () => ProceedAfterArrival(e, sim),
                    afterMidShift: () => GoToActiveJob(e, sim)))
                return;

            // TD-034 G: meldunek/kolejka u dyspozytora
            if (MeldunekFlow.Tick(e, sim, currentGameTime, afterMeldunek: () => GoToActiveJob(e, sim)))
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
                return;
            }

            if (e.workflowState == EmployeeWorkflowState.WorkingAtStation)
            {
                var job = FindJobById(e.workflowTargetId);
                if (job == null || job.state != OutdoorJobState.Servicing)
                {
                    GoToActiveJob(e, sim);
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
            // TD-034 G: meldunek u dyspozytora (skip+notyfikacja gdy brak; kolejka gdy zajęte) → potem job
            MeldunekFlow.Begin(e, sim, afterMeldunek: () => GoToActiveJob(e, sim));
        }

        void GoToActiveJob(Employee e, EmployeeWalkSimulator sim)
        {
            var visual = sim.GetVisual(e.employeeId);
            if (visual != null) visual.SetWorkingAnim(false);

            var job = FindFirstWashServicingJob();
            if (job != null)
            {
                Vector3? jobPos = GetJobPosition(job);
                if (jobPos.HasValue)
                {
                    e.workflowState = EmployeeWorkflowState.GoingToWorkstation;
                    e.workflowTargetId = job.jobId;
                    sim.EnqueueTask(new EmployeeWalkTask
                    {
                        employeeId = e.employeeId,
                        destination = jobPos.Value,
                        purpose = $"WashBay→WashZone#{job.equipmentInstanceId}",
                        onArrive = () => StartWorking(e, sim)
                    });
                    return;
                }
            }

            // Idle w Social room
            e.workflowTargetId = -1;
            Vector3? idlePos = WorkflowLocations.GetSocialRoomIdlePosition(e.employeeId);
            if (!idlePos.HasValue) idlePos = DepotGateMarker.GetPosition();

            e.workflowState = EmployeeWorkflowState.WorkingMobile;
            sim.EnqueueTask(new EmployeeWalkTask
            {
                employeeId = e.employeeId,
                destination = idlePos.Value,
                purpose = "WashBay→Social(idle)",
                onArrive = () =>
                {
                    e.workflowState = EmployeeWorkflowState.WorkingAtStation;
                    var v = sim.GetVisual(e.employeeId);
                    if (v != null) v.SetWorkingAnim(false);
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

        static OutdoorJob FindFirstWashServicingJob()
        {
            foreach (var job in OutdoorEquipmentJobService.ActiveJobs)
            {
                if (job == null) continue;
                if (job.type != OutdoorJobType.Wash) continue;
                if (job.state != OutdoorJobState.Servicing) continue;
                return job;
            }
            return null;
        }

        static OutdoorJob FindJobById(int jobId)
        {
            if (jobId < 0) return null;
            foreach (var job in OutdoorEquipmentJobService.ActiveJobs)
                if (job != null && job.jobId == jobId) return job;
            return null;
        }

        static Vector3? GetJobPosition(OutdoorJob job)
        {
            var placer = OutdoorEquipmentPlacer.Instance;
            if (placer == null) return null;
            foreach (var p in placer.Placed)
            {
                if (p.instanceId != job.equipmentInstanceId) continue;
                return (p.cornerA + p.cornerB) * 0.5f;
            }
            return null;
        }
    }
}
