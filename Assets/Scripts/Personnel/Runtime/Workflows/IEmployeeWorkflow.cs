using UnityEngine;

namespace RailwayManager.Personnel.Workflows
{
    /// <summary>
    /// TD-025: Interfejs workflow per rola. <see cref="PersonnelDispatcher3D"/>
    /// dispatchuje tick per pracownik do odpowiedniego workflow'a wg roli.
    ///
    /// <para>Implementacje:</para>
    /// <list type="bullet">
    /// <item><see cref="DriverConductorWorkflow"/> — Driver, Conductor (pre-duty + embed)</item>
    /// <item><see cref="MechanicWorkflow"/> — Mechanic (slot Inspecting trigger)</item>
    /// <item><see cref="CleanerWorkflow"/> — Cleaner (dirty queue, brak meldunku)</item>
    /// <item><see cref="WashBayWorkflow"/> — WashBay (OutdoorJob Wash trigger)</item>
    /// <item><see cref="StaticDeskWorkflow"/> — Office/Research/Dispatcher/TrafficController</item>
    /// </list>
    /// </summary>
    public interface IEmployeeWorkflow
    {
        /// <summary>Czy ten workflow obsługuje daną rolę.</summary>
        bool HandlesRole(EmployeeRole role);

        /// <summary>
        /// Per-tick re-evaluation. Wywoływane co
        /// <see cref="PersonnelBalanceConstants.WorkflowTickIntervalSec"/>
        /// dla każdego pracownika OnShift z tą rolą.
        ///
        /// Workflow może:
        /// <list type="bullet">
        /// <item>Zmienić <c>e.workflowState</c> (state transition)</item>
        /// <item>Wywołać <c>sim.EnqueueTask(...)</c> żeby kazać pracownikowi iść do nowego celu</item>
        /// <item>Ustawić <c>e.workflowStateFinishGameTime</c> (timer dla WorkingAt/Reporting)</item>
        /// <item>No-op gdy state jest stale (np. WalkSimulator jeszcze nie skończył)</item>
        /// </list>
        /// </summary>
        void Tick(Employee e, EmployeeWalkSimulator sim, long currentGameTime);
    }
}
