using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1 / D28: Runtime snapshot obciazenia dyzurnych ruchu per depot.
    /// Liczony przez <see cref="Runtime.TrafficControlService"/> co tick.
    ///
    /// Capacity: suma <c>10 + 5×(skill-1)</c> per aktywny TrafficController w tym depocie.
    /// Active tasks: <c>DepotMovementSimulator.ActiveTasks.Count</c> dla tego depotu.
    ///
    /// Statusy:
    /// - Normal: tasks ≤ capacity → pelne priority scheduling
    /// - Queued: 1.0–1.5× → PendingTasks rosnie, delays widoczne
    /// - Critical: &gt;1.5× → random +50% czas wykonania dla 25% taskow
    ///
    /// Brak controllera w firmie = <see cref="noControllerFallback"/>=true → DepotMovementSim dziala FCFS + random ±20% delay (D28).
    /// </summary>
    [Serializable]
    public class TrafficControllerWorkload
    {
        /// <summary>ID depot (-1 = globalne, single depot EA). Post-M2.6: per depot separately.</summary>
        public int depotId = -1;

        /// <summary>Suma capacity aktywnych controllerow w tym depocie.</summary>
        public int totalCapacity;

        /// <summary>Liczba aktywnych controllerow w depocie (wszystkie zmiany sumowane, avg 1 na shift).</summary>
        public int activeControllerCount;

        /// <summary>Liczba aktywnych DepotMoveTask w tym depocie.</summary>
        public int activeTasksCount;

        /// <summary>Liczba PendingTasks (kolejka oczekujaca).</summary>
        public int pendingTasksCount;

        public TrafficControllerStatus status = TrafficControllerStatus.Normal;

        /// <summary>True gdy brak controllera w firmie → default FCFS behavior (D28).</summary>
        public bool noControllerFallback;

        // ── Helpery ───────────────────────────────────

        public float CapacityRatio => totalCapacity > 0 ? (float)activeTasksCount / totalCapacity : 0f;
    }
}
