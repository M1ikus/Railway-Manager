using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Personnel.Furniture;
using RailwayManager.Personnel.Workflows;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// TD-025: Orchestrator state machine 3D workflow pracownikow w Depot.
    ///
    /// <para><b>Trzy poziomy aktualizacji stanu:</b></para>
    /// <list type="bullet">
    /// <item><c>OnDayEnded</c> → resolve initial state per pracownik (OnShift → spawn,
    /// Resting/Fired/Sick → despawn). To jest "kiedy zmiana sie zaczyna/konczy".</item>
    /// <item><c>FixedUpdate</c> (sub-sampled co <see cref="PersonnelBalanceConstants.WorkflowTickIntervalSec"/>)
    /// → workflow.Tick per pracownik OnShift. To jest "co fizycznie robi w sekcie".</item>
    /// <item><c>OnRunSpawned/OnRunDespawned</c> → embed/unembed Driver i Conductor
    /// (kapsula znika gdy wsiada do pociagu, reappear gdy wraca).</item>
    /// </list>
    ///
    /// <para><b>Routing rola → workflow:</b> registr List&lt;IEmployeeWorkflow&gt;,
    /// iter z first-match. Latwo dorzucic kolejna role.</para>
    /// </summary>
    public class PersonnelDispatcher3D : MonoBehaviour
    {
        public static PersonnelDispatcher3D Instance { get; private set; }

        // ── Workflows ──
        readonly List<IEmployeeWorkflow> _workflows = new();
        float _tickAccumulator;

        /// <summary>
        /// trainRunId → (driverId, conductorId) — kapsuły hidden podczas DrivingTrain.
        /// Push przy <see cref="EmbedInTrain"/>, pop przy <see cref="OnRunDespawned"/>.
        /// Eliminuje O(n) scan PersonnelService.Employees per train despawn (200+ TR/dzień
        /// × ~50 Driver/Conductor = 10k ops/dzień zamiast 2 lookup/dzień).
        /// </summary>
        readonly Dictionary<int, EmbeddedCrew> _embeddedCrewByTrainRun = new();

        struct EmbeddedCrew
        {
            public int driverId;     // -1 = brak
            public int conductorId;  // -1 = brak
        }

        public static PersonnelDispatcher3D EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PersonnelDispatcher3D");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PersonnelDispatcher3D>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // TD-025: register workflows (kolejnosc nie ma znaczenia — first-match po HandlesRole)
            _workflows.Add(new DriverConductorWorkflow());
            _workflows.Add(new MechanicWorkflow());
            _workflows.Add(new CleanerWorkflow());
            _workflows.Add(new WashBayWorkflow());
            _workflows.Add(new StaticDeskWorkflow());
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            PersonnelService.OnEmployeeStatusChanged += OnEmployeeStatusChanged;
            PersonnelService.OnEmployeeFired += OnEmployeeLost;
            PersonnelEvents.OnEmployeeRetired += OnEmployeeLost;

            // TD-025: embed/unembed Driver-Conductor przy spawn/despawn TrainRun
            TrainRunSimulator.OnRunSpawned += OnRunSpawned;
            TrainRunSimulator.OnRunDespawned += OnRunDespawned;
        }

        void OnDisable()
        {
            PersonnelService.OnEmployeeStatusChanged -= OnEmployeeStatusChanged;
            PersonnelService.OnEmployeeFired -= OnEmployeeLost;
            PersonnelEvents.OnEmployeeRetired -= OnEmployeeLost;
            TrainRunSimulator.OnRunSpawned -= OnRunSpawned;
            TrainRunSimulator.OnRunDespawned -= OnRunDespawned;
        }

        void FixedUpdate()
        {
            // TD-025: workflow tick (sub-sampled na WorkflowTickIntervalSec)
            _tickAccumulator += Time.fixedDeltaTime;
            if (_tickAccumulator < PersonnelBalanceConstants.WorkflowTickIntervalSec) return;
            _tickAccumulator = 0f;

            var sim = EmployeeWalkSimulator.Instance;
            if (sim == null) return;

            long currentGameTime = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;

            // Iteruje cache OnShift agentów zamiast pełnej listy Employees (zawiera dziesiątki/setki
            // Fired+Retired+Resting). Cache maintained event-driven przez NotifyStatusChanged
            // i raz po ShiftManager.ApplyDailyTick (PersonnelDailyScheduler).
            var agents = PersonnelService.OnShiftAgents;
            for (int i = 0; i < agents.Count; i++)
            {
                var e = agents[i];
                if (e == null) continue;
                var workflow = FindWorkflow(e.role);
                workflow?.Tick(e, sim, currentGameTime);
            }
        }

        IEmployeeWorkflow FindWorkflow(EmployeeRole role)
        {
            for (int i = 0; i < _workflows.Count; i++)
                if (_workflows[i].HandlesRole(role)) return _workflows[i];
            return null;
        }

        // ═══ Event subscribers ═══

        void OnEmployeeStatusChanged(Employee e)
        {
            if (e == null) return;
            ResolveEmployee(e);
        }

        void OnEmployeeLost(Employee e)
        {
            if (e == null) return;
            // MF-10: release przypisane biurko + FIFO reassign do innego pracownika bez biurka
            FurnitureAssignmentService.ReleaseAndReassignFifo(e);
            // TD-034: zwolnij mebel czynności osobistej (jeśli w trakcie) + reset przebrania
            Workflows.PersonalActivities.AbortAndRelease(e);
            e.wearingWorkClothes = false;
            EmployeeWalkSimulator.Instance?.DespawnEmployee(e.employeeId);
            e.workflowState = EmployeeWorkflowState.OffShift;
            e.workflowStateFinishGameTime = 0L;
            e.workflowTargetId = -1;
        }

        /// <summary>TD-025: Driver/Conductor wsiada do pociagu — visual hidden.</summary>
        void OnRunSpawned(TrainRun tr)
        {
            if (tr == null) return;
            var sim = EmployeeWalkSimulator.Instance;
            if (sim == null) return;

            // Driver — IsActive guard żeby nie embed'ować Fired/Retired (stale crew assignment
            // może przeżyć fire jeśli CrewCirculationService.ClearAssignmentsForEmployee
            // jeszcze nie zdążył strzelić, np. fire w tym samym frame'ie co OnRunSpawned).
            var driver = CrewAssignmentService.GetDriverForTrainRun(tr.id, tr.runDateIso);
            if (driver != null && driver.IsActive) EmbedInTrain(driver, tr.id, sim);

            // Conductor
            var conductor = CrewAssignmentService.GetConductorForTrainRun(tr.id, tr.runDateIso);
            if (conductor != null && conductor.IsActive) EmbedInTrain(conductor, tr.id, sim);
        }

        /// <summary>TD-025: Pociag wrocil do depot — Driver/Conductor reappear + GoingHome.</summary>
        void OnRunDespawned(TrainRun tr)
        {
            if (tr == null) return;
            var sim = EmployeeWalkSimulator.Instance;
            if (sim == null) return;

            if (!_embeddedCrewByTrainRun.TryGetValue(tr.id, out var crew)) return;
            _embeddedCrewByTrainRun.Remove(tr.id);

            TryUnembed(crew.driverId, tr.id, sim);
            TryUnembed(crew.conductorId, tr.id, sim);
        }

        void TryUnembed(int employeeId, int trainRunId, EmployeeWalkSimulator sim)
        {
            if (employeeId <= 0) return;  // 0 = default(EmbeddedCrew), -1 = explicit "brak"
            var e = PersonnelService.GetById(employeeId);
            if (e == null) return;
            // Guard: między embed a despawn pracownik mógł zostać Fired/Retired
            // (OnEmployeeLost ustawia workflowState=OffShift + despawn).
            if (e.workflowState != EmployeeWorkflowState.DrivingTrain) return;
            if (e.workflowTargetId != trainRunId) return;
            UnembedFromTrain(e, sim);
        }

        void EmbedInTrain(Employee e, int trainRunId, EmployeeWalkSimulator sim)
        {
            e.workflowState = EmployeeWorkflowState.DrivingTrain;
            e.workflowTargetId = trainRunId;
            var visual = sim.GetVisual(e.employeeId);
            if (visual != null) visual.SetHidden(true);

            // Maintain trainRunId → (driver, conductor) mapping dla O(1) OnRunDespawned.
            _embeddedCrewByTrainRun.TryGetValue(trainRunId, out var crew);
            if (e.role == EmployeeRole.Driver) crew.driverId = e.employeeId;
            else if (e.role == EmployeeRole.Conductor) crew.conductorId = e.employeeId;
            else return; // inna rola embedded — niespodziewane, ignore
            _embeddedCrewByTrainRun[trainRunId] = crew;
        }

        void UnembedFromTrain(Employee e, EmployeeWalkSimulator sim)
        {
            // Reappear przy bramie depot (uproszczone — pojazd po wjezdzie jest w depot,
            // ale jego nowa pozycja zalezy od DepotMovementSimulator handshake; gate
            // wystarczy bo pracownik i tak idzie do bramy zeby wyjsc do domu).
            Vector3 reappearPos = DepotGateMarker.GetPosition();

            // Spawn lub re-show visual
            var visual = sim.GetVisual(e.employeeId);
            if (visual == null)
            {
                // Visual byl despawned podczas embed (Sick/Fired itp) — spawn na nowo
                sim.SpawnEmployee(e.employeeId, reappearPos);
            }
            else
            {
                visual.SetHidden(false);
                visual.transform.position = new Vector3(reappearPos.x, 0.9f, reappearPos.z);
            }

            // Workflow: po powrocie pojazdu pracownik idzie do bramy (GoingHome).
            // Jesli ma kolejny imminent duty, Workflow.Tick przelaczy go z powrotem
            // do ComingToDepot przy nastepnym tick'u.
            e.workflowState = EmployeeWorkflowState.GoingHome;
            e.workflowTargetId = -1;
            Vector3 gate = DepotGateMarker.GetPosition();

            // TD-034: po przejeździe drużyna pociągowa też przebiera się przy szafce wychodząc
            if (Workflows.PersonalActivities.TryRouteDepartureViaLocker(e, sim, gate,
                    () => { e.workflowState = EmployeeWorkflowState.OffShift; sim.DespawnEmployee(e.employeeId); }))
                return;

            sim.EnqueueTask(new EmployeeWalkTask
            {
                employeeId = e.employeeId,
                destination = gate,
                purpose = "Driver/Conductor→Home(post-run)",
                onArrive = () =>
                {
                    e.workflowState = EmployeeWorkflowState.OffShift;
                    sim.DespawnEmployee(e.employeeId);
                }
            });
        }

        // ═══ Resolve on daily tick ═══

        /// <summary>
        /// Wywolywane z <see cref="PersonnelDailyScheduler.OnDayEnded"/>.
        /// Resolves initial state (spawn dla nowo OnShift, despawn dla Resting/Fired/etc).
        /// </summary>
        public void ResolveAllForDay()
        {
            var sim = EmployeeWalkSimulator.Instance ?? EmployeeWalkSimulator.EnsureExists();
            foreach (var e in PersonnelService.Employees)
                ResolveEmployee(e);
        }

        void ResolveEmployee(Employee e)
        {
            if (e == null) return;
            var sim = EmployeeWalkSimulator.Instance;
            if (sim == null) return;

            switch (e.status)
            {
                case EmployeeStatus.OnShift:
                    HandleOnShift(e, sim);
                    break;

                case EmployeeStatus.Available:
                case EmployeeStatus.Resting:
                case EmployeeStatus.Sick:
                case EmployeeStatus.LongSick:
                case EmployeeStatus.Training:
                case EmployeeStatus.Fired:
                case EmployeeStatus.Retired:
                    HandleEndOfShift(e, sim);
                    break;
            }
        }

        void HandleOnShift(Employee e, EmployeeWalkSimulator sim)
        {
            // Kasjer — nie spawnuje sie w Depot (D19)
            if (!RoleDefinitions.SpawnsAsAgentInDepot(e.role))
            {
                sim.DespawnEmployee(e.employeeId);
                e.workflowState = EmployeeWorkflowState.OffShift;
                return;
            }

            // MF-10: auto-assign do furniture (jesli rola wymaga i jeszcze brak)
            if (e.assignedFurnitureId < 0)
                FurnitureAssignmentService.AssignBestFurniture(e);

            var existing = sim.GetVisual(e.employeeId);
            if (existing != null)
            {
                // Pracownik juz w depot — refresh label, kontynuuj biezacy workflow
                existing.RefreshLabel();
                return;
            }

            // Inicjalizacja: workflow.Tick w FixedUpdate wykryje OffShift state i wszczyna
            // ComingToDepot. Tu tylko zapewniamy startowy stan.
            e.workflowState = EmployeeWorkflowState.OffShift;
            e.workflowTargetId = -1;
            e.workflowStateFinishGameTime = 0L;
        }

        /// <summary>
        /// TD-025: koniec zmiany — pracownik idzie do bramy i znika.
        /// Jesli juz idzie do domu (GoingHome) lub jest OffShift — no-op.
        /// </summary>
        void HandleEndOfShift(Employee e, EmployeeWalkSimulator sim)
        {
            var visual = sim.GetVisual(e.employeeId);
            if (visual == null)
            {
                // Juz despawned
                e.workflowState = EmployeeWorkflowState.OffShift;
                return;
            }

            if (e.workflowState == EmployeeWorkflowState.GoingHome
                || e.workflowState == EmployeeWorkflowState.OffShift)
                return;

            // DrivingTrain — czeka na OnRunDespawned, nie ingerujemy
            if (e.workflowState == EmployeeWorkflowState.DrivingTrain)
                return;

            // TD-034: gdyby zmiana skończyła się w trakcie czynności osobistej — zwolnij mebel
            Workflows.PersonalActivities.AbortAndRelease(e);

            e.workflowState = EmployeeWorkflowState.GoingHome;
            e.workflowTargetId = -1;
            Vector3 gate = DepotGateMarker.GetPosition();

            // TD-034: operacyjni przebierają się z powrotem przy szafce po drodze do bramy
            if (Workflows.PersonalActivities.TryRouteDepartureViaLocker(e, sim, gate,
                    () => { e.workflowState = EmployeeWorkflowState.OffShift; sim.DespawnEmployee(e.employeeId); }))
                return;

            // Walk do bramy → despawn (biurowi / brak szafki)
            sim.EnqueueTask(new EmployeeWalkTask
            {
                employeeId = e.employeeId,
                destination = gate,
                purpose = "EndOfShift→Home",
                onArrive = () =>
                {
                    e.workflowState = EmployeeWorkflowState.OffShift;
                    sim.DespawnEmployee(e.employeeId);
                }
            });
        }

        // ═══ Debug ═══

        [ContextMenu("Debug: Force resolve all")]
        public void DebugForceResolve() => ResolveAllForDay();

        [ContextMenu("Debug: Report workflow states")]
        public void DebugReportWorkflows()
        {
            int total = 0;
            var counts = new Dictionary<EmployeeWorkflowState, int>();
            foreach (var e in PersonnelService.Employees)
            {
                if (e == null || !RoleDefinitions.SpawnsAsAgentInDepot(e.role)) continue;
                total++;
                counts.TryGetValue(e.workflowState, out var c);
                counts[e.workflowState] = c + 1;
            }
            Log.Info($"[PersonnelDispatcher3D] Workflow report ({total} agents):");
            foreach (var kv in counts)
                Log.Info($"  {kv.Key}: {kv.Value}");
        }
    }
}
