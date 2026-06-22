using DepotSystem;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Personnel.Workflows
{
    /// <summary>
    /// TD-025: Driver i Conductor workflow z pre-duty trigger + embed w pojezdzie.
    ///
    /// <para><b>Flow:</b></para>
    /// <list type="number">
    /// <item><b>OffShift</b> → szuka aktywnego <see cref="CrewCirculation"/> dla tej daty.
    /// Gdy znaleziony i pierwszy duty jest <c>IsImminent(30 min)</c> → wszczyna
    /// <b>ComingToDepot</b>. Inaczej idle Social room (czeka w depot do końca zmiany).</item>
    /// <item><b>ComingToDepot</b> → walk do dispatcher's desk → <b>ReportingToDispatcher</b> (8s)</item>
    /// <item><b>AwaitingDeparture</b> → idle przy biurku dyspozytora; per tick sprawdza czy
    /// <see cref="TrainRunSimulator.OnRunSpawned"/> dla jego TrainRun już zaszło.</item>
    /// <item>Gdy TrainRun spawned → <b>GoingToVehicle</b> → walk do parking pojazdu</item>
    /// <item>Po dotarciu → <b>DrivingTrain</b> (visual hidden — kapsuła znika "wewnątrz pojazdu")</item>
    /// <item>Po <c>OnRunDespawned</c> → <b>GoingHome</b> (visual reappear przy pojezdzie,
    /// walk do bramy, despawn)</item>
    /// </list>
    /// </summary>
    public class DriverConductorWorkflow : IEmployeeWorkflow
    {
        public bool HandlesRole(EmployeeRole role)
            => role == EmployeeRole.Driver || role == EmployeeRole.Conductor;

        public void Tick(Employee e, EmployeeWalkSimulator sim, long currentGameTime)
        {
            // TD-034: trwająca czynność osobista (przebranie/łazienka/przerwa) ma priorytet
            if (PersonalActivities.Tick(e, sim, currentGameTime,
                    afterArrivalLocker: () => ProceedToDispatcher(e, sim),
                    afterMidShift: () => TryStartPreDuty(e, sim, currentGameTime)))
                return;

            // TD-034 G: meldunek/kolejka u dyspozytora
            if (MeldunekFlow.Tick(e, sim, currentGameTime, afterMeldunek: () => EnterAwaitingDeparture(e, sim)))
                return;

            // Waiting on walk callback
            if (e.workflowState == EmployeeWorkflowState.ComingToDepot
                || e.workflowState == EmployeeWorkflowState.GoingToWorkstation
                || e.workflowState == EmployeeWorkflowState.GoingToVehicle
                || e.workflowState == EmployeeWorkflowState.GoingHome)
            {
                return;
            }

            // OffShift — pracownik na shift'ie ale jeszcze nic nie robi
            if (e.workflowState == EmployeeWorkflowState.OffShift)
            {
                TryStartPreDuty(e, sim, currentGameTime);
                return;
            }

            // AwaitingDeparture — czeka na OnRunSpawned dla swojego trainRun
            if (e.workflowState == EmployeeWorkflowState.AwaitingDeparture)
            {
                var trainRun = FindAssignedActiveTrainRun(e, currentGameTime);
                if (trainRun != null)
                {
                    GoToVehicle(e, sim, trainRun);
                }
                return;
            }

            // DrivingTrain — visual hidden, czeka na OnRunDespawned event
            // (handled w PersonnelDispatcher3D.HandleRunDespawned — workflow tick to no-op)
            if (e.workflowState == EmployeeWorkflowState.DrivingTrain)
            {
                return;
            }

            // WorkingAtStation = idle w Social (czeka na duty). Tu wolno wziąć łazienkę/przerwę
            // (5 min ≪ 30 min lead duty → nie ryzykuje spóźnienia); przy AwaitingDeparture/DrivingTrain NIE.
            if (e.workflowState == EmployeeWorkflowState.WorkingAtStation)
            {
                var duty = FindImminentDuty(e, currentGameTime);
                if (duty != null) { StartComing(e, sim); return; }
                if (PersonalActivities.MaybeStartMidShift(e, sim, currentGameTime)) return; // TD-034 łazienka/przerwa
                return;
            }
        }

        void TryStartPreDuty(Employee e, EmployeeWalkSimulator sim, long currentGameTime)
        {
            var duty = FindImminentDuty(e, currentGameTime);
            if (duty != null)
            {
                StartComing(e, sim);
                return;
            }

            // Nie ma zbliżającego się duty — idle w Social (lub przy gate jeśli brak Social)
            // (tylko jeśli jeszcze nie jest w idle)
            if (e.workflowState != EmployeeWorkflowState.WorkingAtStation)
                EnterIdleSocial(e, sim);
        }

        void StartComing(Employee e, EmployeeWalkSimulator sim)
        {
            Vector3 gatePos = DepotGateMarker.GetPosition();
            sim.SpawnEmployee(e.employeeId, gatePos);

            // TD-034: najpierw przebranie w robocze przy szafce (drużyna pociągowa)
            if (PersonalActivities.TryBeginArrivalLocker(e, sim)) return;
            ProceedToDispatcher(e, sim);
        }

        void ProceedToDispatcher(Employee e, EmployeeWalkSimulator sim)
        {
            // TD-034 G: meldunek u dyspozytora (skip+notyfikacja gdy brak; kolejka gdy zajęte) → AwaitingDeparture
            MeldunekFlow.Begin(e, sim, afterMeldunek: () => EnterAwaitingDeparture(e, sim));
        }

        void EnterAwaitingDeparture(Employee e, EmployeeWalkSimulator sim)
        {
            e.workflowState = EmployeeWorkflowState.AwaitingDeparture;
            // Pozostaje przy biurku dyspozytora z idle animation
            var visual = sim.GetVisual(e.employeeId);
            if (visual != null) visual.SetWorkingAnim(false);
        }

        void EnterIdleSocial(Employee e, EmployeeWalkSimulator sim)
        {
            // Spawn at gate jeśli jeszcze nie ma visualu
            Vector3 gatePos = DepotGateMarker.GetPosition();
            sim.SpawnEmployee(e.employeeId, gatePos);

            Vector3? idlePos = WorkflowLocations.GetSocialRoomIdlePosition(e.employeeId);
            if (!idlePos.HasValue) idlePos = gatePos;

            e.workflowState = EmployeeWorkflowState.GoingToWorkstation;
            sim.EnqueueTask(new EmployeeWalkTask
            {
                employeeId = e.employeeId,
                destination = idlePos.Value,
                purpose = "Driver/Conductor→Social(idle)",
                onArrive = () =>
                {
                    e.workflowState = EmployeeWorkflowState.WorkingAtStation;
                    var v = sim.GetVisual(e.employeeId);
                    if (v != null) v.SetWorkingAnim(false);
                }
            });
        }

        void GoToVehicle(Employee e, EmployeeWalkSimulator sim, TrainRun trainRun)
        {
            // Lookup parking pojazdu wykonującego ten TrainRun
            Vector3? parkingPos = GetVehicleParkingPosition(trainRun);
            if (!parkingPos.HasValue)
            {
                // Pojazd już ruszył (OnRoute) lub niewiadomy depotTrackId →
                // embed natychmiast (skip walk)
                EnterDrivingTrain(e, sim, trainRun.id);
                return;
            }

            e.workflowState = EmployeeWorkflowState.GoingToVehicle;
            e.workflowTargetId = trainRun.id;
            sim.EnqueueTask(new EmployeeWalkTask
            {
                employeeId = e.employeeId,
                destination = parkingPos.Value,
                purpose = $"Driver→Vehicle(TR#{trainRun.id})",
                hurry = true,
                onArrive = () => EnterDrivingTrain(e, sim, trainRun.id)
            });
        }

        void EnterDrivingTrain(Employee e, EmployeeWalkSimulator sim, int trainRunId)
        {
            e.workflowState = EmployeeWorkflowState.DrivingTrain;
            e.workflowTargetId = trainRunId;
            var visual = sim.GetVisual(e.employeeId);
            if (visual != null) visual.SetHidden(true);
        }

        // ── Helpers ───────────────────────────────────────

        static CrewDuty FindImminentDuty(Employee e, long currentGameTime)
        {
            if (e == null || !e.IsActive) return null;
            string today = GameState.CurrentDateIso;
            int leadMin = PersonnelBalanceConstants.CrewReportLeadMinutes;

            foreach (var c in CrewCirculationService.All)
            {
                if (c.status != CirculationStatus.Active) continue;
                if (c.assignedEmployeeId != e.employeeId) continue;
                if (c.role != e.role) continue;
                if (!RunsOnDate(c, today)) continue;

                foreach (var duty in c.duties)
                {
                    if (duty.kind != CrewDutyKind.Service) continue;
                    if (duty.IsImminent(today, currentGameTime, leadMin))
                        return duty;
                }
            }
            return null;
        }

        static TrainRun FindAssignedActiveTrainRun(Employee e, long currentGameTime)
        {
            string today = GameState.CurrentDateIso;

            foreach (var c in CrewCirculationService.All)
            {
                if (c.status != CirculationStatus.Active) continue;
                if (c.assignedEmployeeId != e.employeeId) continue;
                if (c.role != e.role) continue;
                if (!RunsOnDate(c, today)) continue;

                foreach (var duty in c.duties)
                {
                    if (duty.kind != CrewDutyKind.Service) continue;
                    if (duty.referencedTrainRunId < 0) continue;

                    // Czy TR spawnowany w TrainRunSimulator (aktywny)?
                    if (TrainRunSimulator.Instance == null) continue;
                    if (!TrainRunSimulator.Instance.IsActive(duty.referencedTrainRunId)) continue;

                    foreach (var tr in TimetableService.TrainRuns)
                        if (tr.id == duty.referencedTrainRunId) return tr;
                }
            }
            return null;
        }

        static Vector3? GetVehicleParkingPosition(TrainRun trainRun)
        {
            if (trainRun.runningVehicleIds == null || trainRun.runningVehicleIds.Count == 0)
                return null;
            int vehicleId = trainRun.runningVehicleIds[0]; // pierwszy pojazd consist'u
            var locSvc = VehicleLocationService.Instance;
            if (locSvc == null) return null;
            var rec = locSvc.Get(vehicleId);
            if (rec == null) return null;
            if (rec.depotTrackId < 0) return null;
            var tg = Object.FindAnyObjectByType<TrackGraph>();
            if (tg == null) return null;
            var poly = tg.GetTrackPolyline(rec.depotTrackId);
            if (poly == null || poly.Count == 0) return null;
            return poly[poly.Count / 2];
        }

        static bool RunsOnDate(CrewCirculation c, string dateIso)
        {
            if (c.specificDates != null && c.specificDates.Count > 0)
                return c.specificDates.Contains(dateIso);
            try
            {
                var date = IsoTime.ParseDate(dateIso);
                int dayOfWeekMonday0 = ((int)date.DayOfWeek + 6) % 7;
                return c.calendarDays.Runs(dayOfWeekMonday0);
            }
            catch
            {
                return false;
            }
        }
    }
}
