using UnityEngine;
using RailwayManager.Core;
using DepotSystem.Furniture.Placement;

namespace RailwayManager.Personnel.Workflows
{
    /// <summary>
    /// TD-034 G: urealniony meldunek u dyspozytora — obecność dyspozytora + kolejka (1 meldujący naraz).
    ///
    /// <para><b>Skip (degraded, nie cichy bypass):</b> brak meldunku dla roli / brak biurka dyspozytora /
    /// brak dyspozytora na zmianie → meldunek pominięty + jednorazowa notyfikacja (Log.Warn/dzień),
    /// pracownik idzie prosto do pracy.</para>
    ///
    /// <para><b>Kolejka:</b> biurko dyspozytora to token w <see cref="FurnitureOccupancyService"/> — kto
    /// pierwszy zajmie, ten melduje 8 s; reszta czeka w <see cref="EmployeeWorkflowState.QueuingForDispatcher"/>
    /// (pozycje za biurkiem) i re-próbuje co tick. Po meldunku token zwolniony → kolejny rusza.</para>
    ///
    /// Workflowy wołają <see cref="Begin"/> (po przyjściu/przebraniu) i <see cref="Tick"/> (na górze Tick'a),
    /// przekazując kontynuację <c>afterMeldunek</c> (przejście do swojej pracy). Wzór jak PersonalActivities.
    /// </summary>
    public static class MeldunekFlow
    {
        static int _lastNoDispatcherWarnDay = -1;

        /// <summary>
        /// Rozpoczyna meldunek. Skip → <paramref name="afterMeldunek"/> od razu. Inaczej walk do biurka →
        /// onArrive zajmuje token (meldunek) lub wchodzi do kolejki.
        /// </summary>
        public static void Begin(Employee e, EmployeeWalkSimulator sim, System.Action afterMeldunek)
        {
            if (!WorkflowLocations.RequiresDispatcherMeldunek(e.role)) { afterMeldunek?.Invoke(); return; }

            var deskPos = WorkflowLocations.GetDispatcherDeskPosition();
            int deskInst = WorkflowLocations.GetDispatcherDeskInstanceId();
            if (!deskPos.HasValue || deskInst < 0) { afterMeldunek?.Invoke(); return; } // brak biurka → pomiń

            if (!WorkflowLocations.IsDispatcherAvailable())
            {
                NotifyNoDispatcherOnce();   // degraded — pomiń meldunek, nie blokuj startu pracy
                afterMeldunek?.Invoke();
                return;
            }

            e.workflowState = EmployeeWorkflowState.ComingToDepot;
            sim.EnqueueTask(new EmployeeWalkTask
            {
                employeeId = e.employeeId,
                destination = deskPos.Value,
                purpose = "→Dispatcher(meldunek)",
                onArrive = () => ArriveAtDesk(e, sim)
            });
        }

        /// <summary>
        /// Obsługa stanów meldunku (na górze workflow.Tick). Zwraca true gdy w trakcie (workflow ma return).
        /// QueuingForDispatcher → re-próba zajęcia tokena; ReportingToDispatcher → timer → release + afterMeldunek.
        /// </summary>
        public static bool Tick(Employee e, EmployeeWalkSimulator sim, long now, System.Action afterMeldunek)
        {
            if (e.workflowState == EmployeeWorkflowState.QueuingForDispatcher)
            {
                var occ = FurnitureOccupancyService.Instance;
                int deskInst = WorkflowLocations.GetDispatcherDeskInstanceId();
                if (occ != null && deskInst >= 0 && occ.TryReserve(deskInst, e.employeeId))
                {
                    // Wolne → podejdź do biurka i zamelduj się
                    var deskPos = WorkflowLocations.GetDispatcherDeskPosition();
                    e.workflowState = EmployeeWorkflowState.ComingToDepot;
                    sim.EnqueueTask(new EmployeeWalkTask
                    {
                        employeeId = e.employeeId,
                        destination = deskPos ?? Vector3.zero,
                        purpose = "Kolejka→Dispatcher",
                        onArrive = () => StartMeldunek(e, sim)
                    });
                }
                return true;
            }

            if (e.workflowState == EmployeeWorkflowState.ReportingToDispatcher)
            {
                if (now >= e.workflowStateFinishGameTime)
                {
                    FurnitureOccupancyService.Instance?.Release(e.employeeId); // zwolnij token kolejki
                    afterMeldunek?.Invoke();
                }
                return true;
            }

            return false;
        }

        static void ArriveAtDesk(Employee e, EmployeeWalkSimulator sim)
        {
            // Anti-zombie: meldunek anulowany w trakcie dojścia (koniec zmiany → stan ≠ ComingToDepot).
            if (e.workflowState != EmployeeWorkflowState.ComingToDepot) return;
            var occ = FurnitureOccupancyService.Instance;
            int deskInst = WorkflowLocations.GetDispatcherDeskInstanceId();
            if (occ != null && deskInst >= 0 && occ.TryReserve(deskInst, e.employeeId))
                StartMeldunek(e, sim);
            else
                EnterQueue(e, sim);
        }

        static void StartMeldunek(Employee e, EmployeeWalkSimulator sim)
        {
            // Anti-zombie: stan zmieniony w trakcie dojścia (koniec zmiany) → zwolnij token i wyjdź.
            if (e.workflowState != EmployeeWorkflowState.ComingToDepot)
            {
                FurnitureOccupancyService.Instance?.Release(e.employeeId);
                return;
            }
            e.workflowState = EmployeeWorkflowState.ReportingToDispatcher;
            e.workflowStateFinishGameTime = PersonalActivities.NowAbs() + (long)PersonnelBalanceConstants.MeldunekDurationSec;
            var v = sim.GetVisual(e.employeeId);
            if (v != null) v.SetWorkingAnim(true);
        }

        static void EnterQueue(Employee e, EmployeeWalkSimulator sim)
        {
            e.workflowState = EmployeeWorkflowState.QueuingForDispatcher;
            var v = sim.GetVisual(e.employeeId);
            if (v != null) v.SetWorkingAnim(false);

            Vector3? qp = WorkflowLocations.GetDispatcherQueuePosition(QueueIndexOf(e));
            if (qp.HasValue)
                sim.EnqueueTask(new EmployeeWalkTask
                {
                    employeeId = e.employeeId,
                    destination = qp.Value,
                    purpose = "→Kolejka dyspozytor"
                });
        }

        /// <summary>Deterministyczna pozycja w kolejce = liczba czekających z niższym employeeId.</summary>
        static int QueueIndexOf(Employee e)
        {
            int idx = 0;
            foreach (var other in PersonnelService.Employees)
                if (other != null && other.employeeId != e.employeeId
                    && other.workflowState == EmployeeWorkflowState.QueuingForDispatcher
                    && other.employeeId < e.employeeId)
                    idx++;
            return idx;
        }

        static void NotifyNoDispatcherOnce()
        {
            int day = GameState.GameDay;
            if (day == _lastNoDispatcherWarnDay) return;
            _lastNoDispatcherWarnDay = day;
            Log.Warn("[MeldunekFlow] Brak dyspozytora na zmianie — meldunek pominięty " +
                     "(pracownicy idą prosto do pracy). Zatrudnij/zaplanuj dyżur dyspozytora.");
        }
    }
}
