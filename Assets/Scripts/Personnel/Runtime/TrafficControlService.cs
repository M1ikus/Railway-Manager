using System;
using System.Collections.Generic;
using UnityEngine;
using DepotSystem;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-11 / D28, D33, D34: Serwis dyzurnego ruchu — priorytetyzuje manewry w zajezdni 3D.
    ///
    /// Implementuje <see cref="IDepotTaskPriorityProvider"/> i instaluje siebie jako
    /// <see cref="DepotMovementSimulator.PriorityProvider"/> (auto w Awake bootstrapu).
    ///
    /// Capacity: suma <c>10 + 5×(skill-1)</c> per aktywny TrafficController (1★=10, 5★=30).
    /// Statusy (D28):
    /// - <c>Normal</c>: active tasks ≤ capacity → priority scheduling aktywne
    /// - <c>Queued</c>: 1.0-1.5× → PendingTasks rosna, delays widoczne
    /// - <c>Critical</c>: &gt;1.5× → random +50% czas wykonania dla 25% taskow
    ///
    /// Brak controllera w firmie: <see cref="DepotMovementSimulator.PriorityProvider"/>=null
    /// → simulator uzywa default FCFS (backward compat).
    ///
    /// Priorytety (D34, sliders w UI post-EA):
    /// - WorkshopOverdue = 100 (przeglady overdue — najwyzszy prio)
    /// - ScheduledDeparture = 80 (wyjazdy rozkladowe)
    /// - WashBayPlanned = 60 (myjnia)
    /// - ParkingReshuffle = 40 (default / parking only)
    ///
    /// Rozpoznawanie typu manewru w M8-11 MVP: heurystyka po trackType (WorkshopTrack → Workshop,
    /// ExitTrack → Departure, WashBayTrack → WashBay, else Parking). Post-EA: explicit purpose
    /// field w DepotMoveTask.
    /// </summary>
    public class TrafficControlService : MonoBehaviour, IDepotTaskPriorityProvider
    {
        public static TrafficControlService Instance { get; private set; }

        public static event Action<TrafficControllerWorkload> OnWorkloadChanged;

        // Sliders dla priorytetow (D34) — override defaultow z PersonnelBalanceConstants.
        // UI moze je modyfikowac; persistent w save (M8-15).
        public static int PriorityWorkshopOverdue = PersonnelBalanceConstants.TrafficPriorityWorkshopOverdue;
        public static int PriorityScheduledDeparture = PersonnelBalanceConstants.TrafficPriorityScheduledDeparture;
        public static int PriorityWashBayPlanned = PersonnelBalanceConstants.TrafficPriorityWashBayPlanned;
        public static int PriorityParkingReshuffle = PersonnelBalanceConstants.TrafficPriorityParkingReshuffle;

        public static TrafficControlService EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("TrafficControlService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<TrafficControlService>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            InstallAsProvider();
            Log.Info("[TrafficControlService] Bootstrapped + installed as DepotMovementSimulator.PriorityProvider");
        }

        void OnDestroy()
        {
            // Uninstall provider jesli my
            if (DepotMovementSimulator.PriorityProvider == (IDepotTaskPriorityProvider)this)
                DepotMovementSimulator.PriorityProvider = null;
        }

        void OnEnable()
        {
            PersonnelService.OnEmployeesChanged += OnEmployeesChanged;
        }

        void OnDisable()
        {
            PersonnelService.OnEmployeesChanged -= OnEmployeesChanged;
        }

        void OnEmployeesChanged()
        {
            // Refresh workload gdy controller jest zatrudniony/zwolniony/chory
            var w = GetWorkload();
            InstallAsProvider();
            OnWorkloadChanged?.Invoke(w);
        }

        /// <summary>Jesli sa aktywni controllerzy — ustaw this jako provider. Inaczej null (FCFS).</summary>
        void InstallAsProvider()
        {
            var active = GetActiveControllers();
            DepotMovementSimulator.PriorityProvider = active.Count > 0 ? (IDepotTaskPriorityProvider)this : null;
        }

        // ═══ Active controllers / capacity ═══

        public static List<Employee> GetActiveControllers()
        {
            var result = new List<Employee>();
            foreach (var e in PersonnelService.Employees)
                if (e.role == EmployeeRole.TrafficController && e.IsActive) result.Add(e);
            return result;
        }

        public static int GetTotalCapacity()
        {
            int cap = 0;
            foreach (var e in PersonnelService.Employees)
                if (e.role == EmployeeRole.TrafficController && e.IsActive)
                    cap += RoleDefinitions.GetTrafficControllerCapacity(e.skill);
            return cap;
        }

        public static TrafficControllerWorkload GetWorkload()
        {
            var controllers = GetActiveControllers();
            int capacity = 0;
            foreach (var c in controllers)
                capacity += RoleDefinitions.GetTrafficControllerCapacity(c.skill);

            int activeTasks = 0, pendingTasks = 0;
            var sim = DepotMovementSimulator.Instance;
            if (sim != null && sim.ActiveTasks != null)
            {
                foreach (var t in sim.ActiveTasks)
                {
                    if (t.state == DepotMoveState.Queued) pendingTasks++;
                    else if (t.state == DepotMoveState.Moving || t.state == DepotMoveState.Pathfinding)
                        activeTasks++;
                }
            }

            var w = new TrafficControllerWorkload
            {
                depotId = -1, // single depot EA
                totalCapacity = capacity,
                activeControllerCount = controllers.Count,
                activeTasksCount = activeTasks,
                pendingTasksCount = pendingTasks,
                noControllerFallback = controllers.Count == 0,
                status = ComputeStatus(capacity, activeTasks, controllers.Count)
            };
            return w;
        }

        static TrafficControllerStatus ComputeStatus(int capacity, int activeTasks, int controllerCount)
        {
            if (controllerCount == 0) return TrafficControllerStatus.Critical;
            if (capacity == 0) return TrafficControllerStatus.Critical;
            float ratio = activeTasks / (float)capacity;
            if (ratio <= 1f) return TrafficControllerStatus.Normal;
            if (ratio <= PersonnelBalanceConstants.TrafficControllerCriticalOverThreshold)
                return TrafficControllerStatus.Queued;
            return TrafficControllerStatus.Critical;
        }

        // ═══ IDepotTaskPriorityProvider ═══

        public int ComputePriority(DepotMoveTask task)
        {
            if (task == null) return PriorityParkingReshuffle;
            var type = ClassifyTask(task);
            return type switch
            {
                TaskKind.WorkshopOverdue => PriorityWorkshopOverdue,
                TaskKind.ScheduledDeparture => PriorityScheduledDeparture,
                TaskKind.WashBay => PriorityWashBayPlanned,
                _ => PriorityParkingReshuffle
            };
        }

        public bool CanAdmitNewTask()
        {
            var w = GetWorkload();
            // Critical = 150%+ capacity → block nowych taskow (queue as pending)
            return w.status != TrafficControllerStatus.Critical;
        }

        /// <summary>
        /// M8-11 MVP: heurystyka klasyfikacji taska po trackType target/from.
        /// Post-EA: explicit <c>purpose</c> field na DepotMoveTask.
        /// </summary>
        TaskKind ClassifyTask(DepotMoveTask task)
        {
            // Placeholder M8-11: wszystkie bez rozroznienia → ParkingReshuffle default.
            // Pelna integracja M9b polish: analiza fromTrackId/toTrackId type via TrackGraph metadata.
            if (task.exitAfterComplete) return TaskKind.ScheduledDeparture;
            return TaskKind.ParkingReshuffle;
        }

        enum TaskKind { WorkshopOverdue, ScheduledDeparture, WashBay, ParkingReshuffle }

        // ═══ Public API (do UI) ═══

        public static void UpdatePrioritySliders(int workshopOverdue, int scheduledDeparture, int washBay, int parking)
        {
            PriorityWorkshopOverdue = Mathf.Clamp(workshopOverdue, 0, 200);
            PriorityScheduledDeparture = Mathf.Clamp(scheduledDeparture, 0, 200);
            PriorityWashBayPlanned = Mathf.Clamp(washBay, 0, 200);
            PriorityParkingReshuffle = Mathf.Clamp(parking, 0, 200);
            if (Instance != null) Instance.OnEmployeesChanged();
        }

        public static void ResetPrioritiesToDefault()
        {
            PriorityWorkshopOverdue = PersonnelBalanceConstants.TrafficPriorityWorkshopOverdue;
            PriorityScheduledDeparture = PersonnelBalanceConstants.TrafficPriorityScheduledDeparture;
            PriorityWashBayPlanned = PersonnelBalanceConstants.TrafficPriorityWashBayPlanned;
            PriorityParkingReshuffle = PersonnelBalanceConstants.TrafficPriorityParkingReshuffle;
            if (Instance != null) Instance.OnEmployeesChanged();
        }

        // ═══ Debug ═══

        [ContextMenu("Debug: Report workload")]
        public void DebugReport()
        {
            var w = GetWorkload();
            Log.Info($"[TrafficControlService] Workload: status={w.status}, " +
                     $"controllers={w.activeControllerCount}, capacity={w.totalCapacity}, " +
                     $"activeTasks={w.activeTasksCount}, pending={w.pendingTasksCount}, " +
                     $"noControllerFallback={w.noControllerFallback}");
            Log.Info($"  Priorities: workshop={PriorityWorkshopOverdue}, dep={PriorityScheduledDeparture}, " +
                     $"wash={PriorityWashBayPlanned}, park={PriorityParkingReshuffle}");
        }
    }
}
