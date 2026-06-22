using DepotSystem;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Personnel.Workflows
{
    /// <summary>
    /// TD-025: Cleaner workflow — bez meldunku u dyspozytora (user decision 2026-05-11 pkt 7).
    ///
    /// <para><b>Flow:</b></para>
    /// <list type="number">
    /// <item>Spawn at gate → <b>ComingToDepot</b> → walk do pierwszego brudnego pojazdu</item>
    /// <item><b>WorkingMobile</b> (timer <see cref="PersonnelBalanceConstants.CleaningSecondsPerVehicle"/>)
    /// → po końcu: <see cref="CleaningService.CompleteCleaning"/> → <c>cleanlinessPercent = 100</c></item>
    /// <item>Lookup następny brudny pojazd → walk → clean…</item>
    /// <item>Brak brudnych pojazdów → idle w Social room (WorkingAtStation z idle anim)</item>
    /// </list>
    ///
    /// <para>"Brudny pojazd" = <c>cleanlinessPercent &lt; CleaningService.CleanThreshold</c> (50%).</para>
    /// </summary>
    public class CleanerWorkflow : IEmployeeWorkflow
    {
        public bool HandlesRole(EmployeeRole role) => role == EmployeeRole.Cleaner;

        public void Tick(Employee e, EmployeeWalkSimulator sim, long currentGameTime)
        {
            // TD-034: trwająca czynność osobista (przebranie/łazienka/przerwa) ma priorytet
            if (PersonalActivities.Tick(e, sim, currentGameTime,
                    afterArrivalLocker: () => GoToNextDirtyVehicle(e, sim),
                    afterMidShift: () => GoToNextDirtyVehicle(e, sim)))
                return;

            if (e.workflowState == EmployeeWorkflowState.OffShift)
            {
                StartComing(e, sim);
                return;
            }

            if (e.workflowState == EmployeeWorkflowState.ComingToDepot
                || e.workflowState == EmployeeWorkflowState.GoingToWorkstation
                || e.workflowState == EmployeeWorkflowState.GoingHome)
            {
                return;
            }

            if (e.workflowState == EmployeeWorkflowState.WorkingMobile)
            {
                // Timer cleaning
                if (currentGameTime >= e.workflowStateFinishGameTime
                    && e.workflowStateFinishGameTime > 0L)
                {
                    // Complete cleaning na bieżącym vehicleId (workflowTargetId)
                    CleaningService.CompleteCleaning(e.workflowTargetId);
                    GoToNextDirtyVehicle(e, sim);
                }
                return;
            }

            if (e.workflowState == EmployeeWorkflowState.WorkingAtStation)
            {
                // Idle w Social — sprawdź czy jest nowy brudny pojazd
                if (FindFirstDirtyVehicle() != null)
                {
                    GoToNextDirtyVehicle(e, sim);
                    return;
                }
                if (PersonalActivities.MaybeStartMidShift(e, sim, currentGameTime)) return; // TD-034 łazienka/przerwa
                var visual = sim.GetVisual(e.employeeId);
                if (visual != null) visual.SetWorkingAnim(false);
                return;
            }
        }

        void StartComing(Employee e, EmployeeWalkSimulator sim)
        {
            Vector3 gatePos = DepotGateMarker.GetPosition();
            sim.SpawnEmployee(e.employeeId, gatePos);
            // TD-034: najpierw przebranie w robocze przy szafce (rola operacyjna)
            if (PersonalActivities.TryBeginArrivalLocker(e, sim)) return;
            e.workflowState = EmployeeWorkflowState.ComingToDepot;
            GoToNextDirtyVehicle(e, sim);
        }

        void GoToNextDirtyVehicle(Employee e, EmployeeWalkSimulator sim)
        {
            var visual = sim.GetVisual(e.employeeId);
            if (visual != null) visual.SetWorkingAnim(false);

            var dirty = FindFirstDirtyVehicle();
            if (dirty != null)
            {
                Vector3? vehiclePos = GetVehiclePosition(dirty);
                if (vehiclePos.HasValue)
                {
                    e.workflowTargetId = dirty.vehicleId;
                    e.workflowState = EmployeeWorkflowState.GoingToWorkstation;
                    sim.EnqueueTask(new EmployeeWalkTask
                    {
                        employeeId = e.employeeId,
                        destination = vehiclePos.Value,
                        purpose = $"Cleaner→Vehicle#{dirty.vehicleId}",
                        onArrive = () => StartCleaning(e, sim)
                    });
                    return;
                }
            }

            // Brak brudnych → idle w Social
            e.workflowTargetId = -1;
            Vector3? idlePos = WorkflowLocations.GetSocialRoomIdlePosition(e.employeeId);
            if (!idlePos.HasValue) idlePos = DepotGateMarker.GetPosition();

            e.workflowState = EmployeeWorkflowState.GoingToWorkstation;
            sim.EnqueueTask(new EmployeeWalkTask
            {
                employeeId = e.employeeId,
                destination = idlePos.Value,
                purpose = "Cleaner→Social(idle)",
                onArrive = () =>
                {
                    e.workflowState = EmployeeWorkflowState.WorkingAtStation;
                    var v = sim.GetVisual(e.employeeId);
                    if (v != null) v.SetWorkingAnim(false);
                }
            });
        }

        void StartCleaning(Employee e, EmployeeWalkSimulator sim)
        {
            e.workflowState = EmployeeWorkflowState.WorkingMobile;
            e.workflowStateFinishGameTime = (long)GameState.GameTimeSeconds
                + GameState.GameDay * 86400L
                + (long)PersonnelBalanceConstants.CleaningSecondsPerVehicle;
            var visual = sim.GetVisual(e.employeeId);
            if (visual != null) visual.SetWorkingAnim(true);
        }

        static VehicleLocationRecord FindFirstDirtyVehicle()
        {
            var locSvc = VehicleLocationService.Instance;
            if (locSvc == null) return null;
            foreach (var rec in locSvc.GetInDepot())
            {
                var v = FleetService.GetOwnedById(rec.vehicleId);
                if (v == null) continue;
                if (v.cleanlinessPercent < CleaningService.CleanThreshold)
                    return rec;
            }
            return null;
        }

        static Vector3? GetVehiclePosition(VehicleLocationRecord rec)
        {
            if (rec.depotTrackId < 0) return null;
            var tg = Object.FindAnyObjectByType<TrackGraph>();
            if (tg == null) return null;
            var poly = tg.GetTrackPolyline(rec.depotTrackId);
            if (poly == null || poly.Count == 0) return null;
            // Środek polyline jako "obok pojazdu"
            return poly[poly.Count / 2];
        }
    }
}
