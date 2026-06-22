using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Fleet;
using DepotSystem;
using DepotSystem.Furniture.Placement;
using DepotSystem.OutdoorEquipment;

namespace RailwayManager.Maintenance.Movement
{
    /// <summary>
    /// MM-18 — bridge między <see cref="OutdoorEquipmentJobService"/> (Fleet asmdef)
    /// a <see cref="DepotMovementSimulator"/> + <see cref="AccessTrackResolver"/>
    /// (Depot asmdef).
    ///
    /// Asymmetria asmdef: Fleet NIE widzi Depot. Bridge ląduje w Timetable
    /// (Maintenance subnamespace), żeby spinać oba światy. Wzór z
    /// <c>ServicePitFurnitureBridge</c> + <c>PartInventoryFurnitureBridge</c>.
    ///
    /// Workflow:
    /// <list type="number">
    /// <item>Bootstrap: instaluje <c>OutdoorEquipmentJobService.RequestMovementHook</c></item>
    /// <item>Hook wywoływany per ScheduleX → resolve target equipment + access track,
    /// resolve origin track, EnqueueMove, subskrybuje <c>task.onCompleted</c></item>
    /// <item>Callback: state=Completed → <c>OutdoorEquipmentJobService.MarkMovementCompleted</c>,
    /// state=Failed → <c>MarkMovementFailed</c></item>
    /// </list>
    ///
    /// Brak bridge'a (np. testy w Editor bez DepotMovementSimulator instance) →
    /// hook null → <see cref="OutdoorEquipmentJobService.ScheduleInternal"/> robi
    /// fallback do legacy immediate Servicing. Backward-compatible.
    /// </summary>
    public class OutdoorEquipmentMovementBridge : MonoBehaviour
    {
        public static OutdoorEquipmentMovementBridge Instance { get; private set; }

        bool _hookInstalled;

        /// <summary>
        /// MM-18f: hook walidacji obecności maszynisty dla service movement.
        /// Set'owany przez <c>RailwayManager.Personnel.CrewAssignmentService</c>.
        /// Func args: <c>vehicleId</c>. Returns true gdy maszynista dostępny.
        /// Null = brak walidacji (movement zawsze przepuszczamy → manual dispatch fallback).
        /// </summary>
        public static System.Func<int, bool> DriverAvailableHook;

        /// <summary>
        /// MM-18g: hook TC capacity check + workload accounting. Set przez
        /// <c>RailwayManager.Personnel.DispatchActionService</c>. Func zwraca true gdy
        /// akcja serwisowa zaakceptowana przez TC (slot dostępny). Null = pass.
        /// </summary>
        public static System.Func<int, string, bool> TrafficControllerAcceptHook;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var existing = FindAnyObjectByType<OutdoorEquipmentMovementBridge>();
            if (existing != null) return;

            var go = new GameObject("OutdoorEquipmentMovementBridge (auto-spawn)");
            go.AddComponent<OutdoorEquipmentMovementBridge>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            InstallHook();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            if (_hookInstalled)
            {
                if (OutdoorEquipmentJobService.RequestMovementHook == HandleMovementRequest)
                    OutdoorEquipmentJobService.RequestMovementHook = null;
                if (ModernizationJobService.RequestMovementHook == HandleModernizationMovementRequest)
                    ModernizationJobService.RequestMovementHook = null;
                if (VehicleModificationJobService.RequestMovementHook == HandleModificationMovementRequest)
                    VehicleModificationJobService.RequestMovementHook = null;
                if (SelfPaintingService.RequestMovementHook == HandlePaintMovementRequest)
                    SelfPaintingService.RequestMovementHook = null;
                Log.Info("[OutdoorEquipmentMovementBridge] Hooks uninstalled");
            }
            Instance = null;
        }

        void InstallHook()
        {
            if (_hookInstalled) return;
            OutdoorEquipmentJobService.RequestMovementHook = HandleMovementRequest;
            // MM-18d: bridge obsługuje też 3 dodatkowe services (Modernization/Modification/SelfPaint Internal)
            ModernizationJobService.RequestMovementHook = HandleModernizationMovementRequest;
            VehicleModificationJobService.RequestMovementHook = HandleModificationMovementRequest;
            SelfPaintingService.RequestMovementHook = HandlePaintMovementRequest;
            _hookInstalled = true;
            Log.Info("[OutdoorEquipmentMovementBridge] Hooks installed (Outdoor + Modernization + Modification + SelfPaint)");
        }

        // ── Hook implementation ────────────────────────────────────

        /// <summary>
        /// MM-18: wywoływany przez <c>OutdoorEquipmentJobService.ScheduleInternal</c>
        /// po dorzuceniu joba do kolejki. Zwraca true gdy real movement zaschedulowane;
        /// false gdy fallback do immediate Servicing (np. brak DepotMovementSimulator,
        /// brak access track, niereachable).
        /// </summary>
        bool HandleMovementRequest(OutdoorJob job)
        {
            // ── 0. MM-18f: walidacja prerekwizytów ──
            if (!ValidatePrereqs(job.vehicleId, job.type.ToString()))
                return false;

            // ── 1. DepotMovementSimulator availability ──
            var sim = DepotMovementSimulator.Instance;
            if (sim == null)
            {
                Log.Warn($"[OutdoorEquipmentMovementBridge] No DepotMovementSimulator — fallback");
                return false;
            }

            // ── 2. Target track (equipment → access track) ──
            var targetResolve = ResolveTargetAccessTrack(job.equipmentInstanceId, job.type);
            if (!targetResolve.isReachable)
            {
                Log.Warn($"[OutdoorEquipmentMovementBridge] Brak access track dla equipment#{job.equipmentInstanceId} " +
                         $"({job.type}) — distance={targetResolve.distanceMeters:F1}m. Fallback do legacy.");
                return false;
            }

            // ── 3. Origin track (vehicle → depotTrackId) ──
            int originTrackId = ResolveOriginTrack(job.vehicleId);
            if (originTrackId < 0)
            {
                Log.Warn($"[OutdoorEquipmentMovementBridge] Brak origin track dla vehicle#{job.vehicleId} " +
                         "(VehicleLocationService.depotTrackId=-1, brak fallbacku). Fallback do legacy.");
                return false;
            }

            // Same track? — pojazd już stoi na docelowym, omijamy movement
            if (originTrackId == targetResolve.trackId)
            {
                Log.Info($"[OutdoorEquipmentMovementBridge] Vehicle#{job.vehicleId} już na target track#{originTrackId} " +
                         "— skip movement, bezpośrednio Servicing");
                return false; // fallback = immediate Servicing
            }

            // ── 4. Enqueue movement ──
            int consistId = sim.GenerateConsistId();
            job.consistId = consistId;
            job.targetTrackId = targetResolve.trackId;
            job.originTrackId = originTrackId;

            var vehicleIds = new List<int> { job.vehicleId };
            bool ok = sim.EnqueueMove(consistId, vehicleIds, originTrackId, targetResolve.trackId, targetResolve.accessPos);
            if (!ok)
            {
                Log.Warn($"[OutdoorEquipmentMovementBridge] EnqueueMove failed dla consist#{consistId} " +
                         $"(track#{originTrackId} → #{targetResolve.trackId}). Fallback do legacy.");
                return false;
            }

            // ── 5. Subscribe task.onCompleted ──
            DepotMoveTask task = null;
            foreach (var t in sim.ActiveTasks)
            {
                if (t.consistId == consistId) { task = t; break; }
            }
            if (task == null)
            {
                Log.Warn($"[OutdoorEquipmentMovementBridge] Task dla consist#{consistId} nie znaleziony po EnqueueMove " +
                         "— callback nie zarejestrowany. Job zostaje EnRoute aż timeout/manual cancel.");
                return true; // movement zaschedulowane, ale callback nie ma → user musi sam cancellnąć
            }

            int capturedJobId = job.jobId;
            task.onCompleted = (DepotMoveState state) => HandleMovementFinished(capturedJobId, state, task);

            Log.Info($"[OutdoorEquipmentMovementBridge] Movement scheduled job#{job.jobId} " +
                     $"vehicle#{job.vehicleId} {job.type}: track#{originTrackId} → #{targetResolve.trackId} " +
                     $"(consist#{consistId}, dist={targetResolve.distanceMeters:F1}m)");
            return true;
        }

        // ── Resolvers ──────────────────────────────────────────────

        AccessTrackResolver.ResolveResult ResolveTargetAccessTrack(int equipmentInstanceId, OutdoorJobType type)
        {
            var graph = AccessTrackResolver.FindGraph();
            if (graph == null) return default;

            // Try outdoor first
            if (OutdoorEquipmentPlacer.Instance != null)
            {
                foreach (var p in OutdoorEquipmentPlacer.Instance.Placed)
                {
                    if (p == null) continue;
                    if (equipmentInstanceId >= 0 && p.instanceId != equipmentInstanceId) continue;
                    // Match instance lub fallback any-of-type
                    var resolved = AccessTrackResolver.FindAccessTrackFor(p, graph);
                    if (resolved.isReachable) return resolved;
                }
            }

            // Try indoor furniture (wash_gate / fuel_pump / water_service / paint_bay)
            if (FurniturePlacer.Instance != null && equipmentInstanceId >= 0)
            {
                var f = FurniturePlacer.Instance.GetInstance(equipmentInstanceId);
                if (f != null)
                {
                    var resolved = AccessTrackResolver.FindAccessTrackFor(f, graph);
                    if (resolved.isReachable) return resolved;
                }
            }

            return default;
        }

        int ResolveOriginTrack(int vehicleId)
        {
            // Primary: VehicleLocationService.depotTrackId
            var loc = VehicleLocationService.Instance?.Get(vehicleId);
            if (loc != null && loc.depotTrackId >= 0)
                return loc.depotTrackId;

            // Fallback: pierwszy track w grafie (świeżo dostarczony pojazd bez znanego parking)
            var graph = AccessTrackResolver.FindGraph();
            if (graph != null)
            {
                foreach (var kvp in graph.Tracks)
                    return kvp.Key; // pierwszy
            }

            return -1;
        }

        // ── Callback after movement ────────────────────────────────

        void HandleMovementFinished(int jobId, DepotMoveState state, DepotMoveTask task)
        {
            long now = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;

            if (state == DepotMoveState.Completed)
            {
                OutdoorEquipmentJobService.MarkMovementCompleted(jobId, now);
                UpdateLocationOnArrival(task);
            }
            else
            {
                OutdoorEquipmentJobService.MarkMovementFailed(jobId, now,
                    task?.failureReason ?? "movement aborted");
            }
        }

        // ════════════════════════════════════════════════════════════
        //  MM-18d: handlers dla Modernization / Modification / SelfPaint Internal
        // ════════════════════════════════════════════════════════════

        bool HandleModernizationMovementRequest(ModernizationJob job)
        {
            if (!ValidatePrereqs(job.vehicleId, "Modernization")) return false;
            var ctx = ResolveServicePitMovement(job.vehicleId, job.internalServicePitInstanceId);
            if (!ctx.success) return false;
            job.consistId = ctx.consistId;
            job.targetTrackId = ctx.targetTrackId;
            job.originTrackId = ctx.originTrackId;
            int capturedJobId = job.jobId;
            ctx.task.onCompleted = (state) => HandleModernizationMovementFinished(capturedJobId, state, ctx.task);
            Log.Info($"[Bridge] Modernization movement scheduled job#{job.jobId} vehicle#{job.vehicleId} " +
                     $"track#{ctx.originTrackId} → #{ctx.targetTrackId} (consist#{ctx.consistId})");
            return true;
        }

        void HandleModernizationMovementFinished(int jobId, DepotMoveState state, DepotMoveTask task)
        {
            long now = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;
            if (state == DepotMoveState.Completed)
            {
                ModernizationJobService.MarkMovementCompleted(jobId, now);
                UpdateLocationOnArrival(task);
            }
            else
            {
                ModernizationJobService.MarkMovementFailed(jobId, now,
                    task?.failureReason ?? "movement aborted");
            }
        }

        bool HandleModificationMovementRequest(VehicleModificationJob job)
        {
            if (!ValidatePrereqs(job.vehicleId, "VehicleModification")) return false;
            var ctx = ResolveServicePitMovement(job.vehicleId, job.internalServicePitInstanceId);
            if (!ctx.success) return false;
            job.consistId = ctx.consistId;
            job.targetTrackId = ctx.targetTrackId;
            job.originTrackId = ctx.originTrackId;
            int capturedJobId = job.jobId;
            ctx.task.onCompleted = (state) => HandleModificationMovementFinished(capturedJobId, state, ctx.task);
            Log.Info($"[Bridge] VehicleModification movement scheduled job#{job.jobId} vehicle#{job.vehicleId} " +
                     $"track#{ctx.originTrackId} → #{ctx.targetTrackId} (consist#{ctx.consistId})");
            return true;
        }

        void HandleModificationMovementFinished(int jobId, DepotMoveState state, DepotMoveTask task)
        {
            long now = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;
            if (state == DepotMoveState.Completed)
            {
                VehicleModificationJobService.MarkMovementCompleted(jobId, now);
                UpdateLocationOnArrival(task);
            }
            else
            {
                VehicleModificationJobService.MarkMovementFailed(jobId, now,
                    task?.failureReason ?? "movement aborted");
            }
        }

        bool HandlePaintMovementRequest(SelfPaintingJob job)
        {
            if (!ValidatePrereqs(job.vehicleId, "SelfPaint")) return false;
            var ctx = ResolveServicePitMovement(job.vehicleId, job.paintBayInstanceId);
            if (!ctx.success) return false;
            job.consistId = ctx.consistId;
            job.targetTrackId = ctx.targetTrackId;
            job.originTrackId = ctx.originTrackId;
            int capturedJobId = job.jobId;
            ctx.task.onCompleted = (state) => HandlePaintMovementFinished(capturedJobId, state, ctx.task);
            Log.Info($"[Bridge] SelfPaint movement scheduled job#{job.jobId} vehicle#{job.vehicleId} " +
                     $"track#{ctx.originTrackId} → #{ctx.targetTrackId} (consist#{ctx.consistId})");
            return true;
        }

        void HandlePaintMovementFinished(int jobId, DepotMoveState state, DepotMoveTask task)
        {
            long now = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;
            if (state == DepotMoveState.Completed)
            {
                SelfPaintingService.MarkMovementCompleted(jobId, now);
                UpdateLocationOnArrival(task);
            }
            else
            {
                SelfPaintingService.MarkMovementFailed(jobId, now,
                    task?.failureReason ?? "movement aborted");
            }
        }

        // ════════════════════════════════════════════════════════════
        //  TD-031: recovery osieroconych manewrów serwisowych po load
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// TD-031: wznawia ruch zajezdni dla jobów serwisowych które po wczytaniu save'a zostały
        /// w fazie EnRoute bez aktywnego <c>DepotMoveTask</c> (delegat <c>onCompleted</c> nie jest
        /// serializowany, więc DepotSavable pomija te manewry). Wzór z <c>DeliveryService</c> —
        /// wykrycie orphan stanu w tick loop i domknięcie ruchu.
        ///
        /// Wywoływane z <c>WorkshopManager.Update</c> (throttled) — to centralny, DontDestroyOnLoad
        /// tick który już orkiestruje <c>CheckCompletions</c> 4 service'ów. Bridge dostarcza tu
        /// logikę resolve (Fleet nie widzi Depot/AccessTrackResolver).
        ///
        /// Per orphan: re-issue manewr z TYM SAMYM consistId (occupancy + visual są już odtworzone
        /// pod nim), re-resolve target (access pos nie jest w save) i podpięcie <c>onCompleted</c>
        /// → przeskok EnRoute→Servicing po dotarciu. Gdy ruch strukturalnie niemożliwy (target
        /// nieosiągalny / brak napędu) — fallback do natychmiastowego Servicing (<c>MarkMovementCompleted</c>),
        /// żeby nie zawiesić joba.
        /// </summary>
        public void RecoverInterruptedServiceMovements()
        {
            var sim = DepotMovementSimulator.Instance;
            if (sim == null || !sim.IsGraphReady) return;

            long now = NowGameTime();
            RecoverOutdoorJobs(sim, now);
            RecoverModernizationJobs(sim, now);
            RecoverModificationJobs(sim, now);
            RecoverSelfPaintJobs(sim, now);
        }

        void RecoverOutdoorJobs(DepotMovementSimulator sim, long now)
        {
            foreach (var job in OutdoorEquipmentJobService.ActiveJobs)
            {
                if (job == null || job.state != OutdoorJobState.EnRoute) continue;
                if (!IsOrphanedMovement(sim, job.consistId)) continue;

                var target = ResolveTargetAccessTrack(job.equipmentInstanceId, job.type);
                int capturedJobId = job.jobId;
                bool ok = TryReissueServiceMove(sim, job.consistId, job.vehicleId, target,
                    t => (state) => HandleMovementFinished(capturedJobId, state, t));

                if (ok)
                    Log.Info($"[Bridge] Wznowiono ruch serwisowy Outdoor job#{job.jobId} consist#{job.consistId} → track#{target.trackId}");
                else
                {
                    Log.Warn($"[Bridge] Nie można wznowić ruchu Outdoor job#{job.jobId} consist#{job.consistId} " +
                             $"(reachable={target.isReachable}) — fallback: serwis startuje na bieżącej pozycji");
                    OutdoorEquipmentJobService.MarkMovementCompleted(job.jobId, now);
                }
            }
        }

        void RecoverModernizationJobs(DepotMovementSimulator sim, long now)
        {
            foreach (var job in ModernizationJobService.ActiveJobs)
            {
                if (job == null || job.state != ServiceJobState.EnRoute) continue;
                if (!IsOrphanedMovement(sim, job.consistId)) continue;

                var target = ResolveFurnitureAccessTrack(job.internalServicePitInstanceId);
                int capturedJobId = job.jobId;
                bool ok = TryReissueServiceMove(sim, job.consistId, job.vehicleId, target,
                    t => (state) => HandleModernizationMovementFinished(capturedJobId, state, t));

                if (ok)
                    Log.Info($"[Bridge] Wznowiono ruch serwisowy Modernization job#{job.jobId} consist#{job.consistId} → track#{target.trackId}");
                else
                {
                    Log.Warn($"[Bridge] Nie można wznowić ruchu Modernization job#{job.jobId} consist#{job.consistId} " +
                             $"(reachable={target.isReachable}) — fallback: serwis startuje na bieżącej pozycji");
                    ModernizationJobService.MarkMovementCompleted(job.jobId, now);
                }
            }
        }

        void RecoverModificationJobs(DepotMovementSimulator sim, long now)
        {
            foreach (var job in VehicleModificationJobService.ActiveJobs)
            {
                if (job == null || job.state != ServiceJobState.EnRoute) continue;
                if (!IsOrphanedMovement(sim, job.consistId)) continue;

                var target = ResolveFurnitureAccessTrack(job.internalServicePitInstanceId);
                int capturedJobId = job.jobId;
                bool ok = TryReissueServiceMove(sim, job.consistId, job.vehicleId, target,
                    t => (state) => HandleModificationMovementFinished(capturedJobId, state, t));

                if (ok)
                    Log.Info($"[Bridge] Wznowiono ruch serwisowy Modification job#{job.jobId} consist#{job.consistId} → track#{target.trackId}");
                else
                {
                    Log.Warn($"[Bridge] Nie można wznowić ruchu Modification job#{job.jobId} consist#{job.consistId} " +
                             $"(reachable={target.isReachable}) — fallback: serwis startuje na bieżącej pozycji");
                    VehicleModificationJobService.MarkMovementCompleted(job.jobId, now);
                }
            }
        }

        void RecoverSelfPaintJobs(DepotMovementSimulator sim, long now)
        {
            foreach (var job in SelfPaintingService.ActiveJobs)
            {
                if (job == null || job.state != ServiceJobState.EnRoute) continue;
                if (!IsOrphanedMovement(sim, job.consistId)) continue;

                var target = ResolveFurnitureAccessTrack(job.paintBayInstanceId);
                int capturedJobId = job.jobId;
                bool ok = TryReissueServiceMove(sim, job.consistId, job.vehicleId, target,
                    t => (state) => HandlePaintMovementFinished(capturedJobId, state, t));

                if (ok)
                    Log.Info($"[Bridge] Wznowiono ruch serwisowy SelfPaint job#{job.jobId} consist#{job.consistId} → track#{target.trackId}");
                else
                {
                    Log.Warn($"[Bridge] Nie można wznowić ruchu SelfPaint job#{job.jobId} consist#{job.consistId} " +
                             $"(reachable={target.isReachable}) — fallback: serwis startuje na bieżącej pozycji");
                    SelfPaintingService.MarkMovementCompleted(job.jobId, now);
                }
            }
        }

        /// <summary>
        /// TD-031: czy consist serwisowy jest osierocony — visual odtworzony (consist fizycznie
        /// w zajezdni) ale brak aktywnego DepotMoveTask. Brak visualu (graf gotowy ale
        /// RestoreParkedVisualsFromGraph jeszcze nie spawnął) → czekamy następny tick (nie orphan).
        /// </summary>
        static bool IsOrphanedMovement(DepotMovementSimulator sim, int consistId)
            => consistId >= 0 && sim.HasConsistVisual(consistId) && !sim.HasTaskForConsist(consistId);

        /// <summary>
        /// TD-031: re-issue manewr serwisowy (RestoreActiveMove z reuse consistId) + podpięcie
        /// onCompleted na świeży task. <paramref name="onCompletedFactory"/> dostaje task i zwraca
        /// delegat (kura-jajko: callback potrzebuje ref na task który powstaje dopiero w EnqueueMove).
        /// Returns false gdy target nieosiągalny / EnqueueMove fail (caller robi fallback teleport).
        /// </summary>
        bool TryReissueServiceMove(DepotMovementSimulator sim, int consistId, int vehicleId,
            AccessTrackResolver.ResolveResult target,
            System.Func<DepotMoveTask, System.Action<DepotMoveState>> onCompletedFactory)
        {
            if (!target.isReachable) return false;

            var vehicleIds = new List<int> { vehicleId };
            if (!sim.RestoreActiveMove(consistId, vehicleIds, target.trackId, target.accessPos, false, false))
                return false;

            var task = sim.GetActiveTask(consistId);
            if (task == null) return false; // EnqueueMove zgłosił sukces, ale brak taska — defensywnie fallback

            task.onCompleted = onCompletedFactory(task);
            return true;
        }

        /// <summary>TD-031: resolve access track dla furniture-bound service (Modernization/Modification/Paint).</summary>
        AccessTrackResolver.ResolveResult ResolveFurnitureAccessTrack(int furnitureInstanceId)
        {
            var placer = FurniturePlacer.Instance;
            if (placer == null) return default;
            var pit = placer.GetInstance(furnitureInstanceId);
            if (pit == null) return default;
            var graph = AccessTrackResolver.FindGraph();
            if (graph == null) return default;
            return AccessTrackResolver.FindAccessTrackFor(pit, graph);
        }

        static long NowGameTime()
            => (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;

        // ════════════════════════════════════════════════════════════
        //  Shared helpers
        // ════════════════════════════════════════════════════════════

        struct MovementContext
        {
            public bool success;
            public int consistId;
            public int targetTrackId;
            public int originTrackId;
            public DepotMoveTask task;
        }

        /// <summary>
        /// MM-18d: resolve movement context dla furniture-bound services (Modernization/Modification/Paint).
        /// Furniture instance → access track → EnqueueMove → subscribed task.
        /// </summary>
        MovementContext ResolveServicePitMovement(int vehicleId, int furnitureInstanceId)
        {
            var sim = DepotMovementSimulator.Instance;
            if (sim == null) return default;

            var placer = FurniturePlacer.Instance;
            if (placer == null) return default;
            var pit = placer.GetInstance(furnitureInstanceId);
            if (pit == null) return default;

            var graph = AccessTrackResolver.FindGraph();
            if (graph == null) return default;

            var targetResolve = AccessTrackResolver.FindAccessTrackFor(pit, graph);
            if (!targetResolve.isReachable) return default;

            int originTrackId = ResolveOriginTrack(vehicleId);
            if (originTrackId < 0) return default;
            if (originTrackId == targetResolve.trackId) return default; // same track → fallback Servicing

            int consistId = sim.GenerateConsistId();
            var vehicleIds = new List<int> { vehicleId };
            bool ok = sim.EnqueueMove(consistId, vehicleIds, originTrackId, targetResolve.trackId, targetResolve.accessPos);
            if (!ok) return default;

            DepotMoveTask task = null;
            foreach (var t in sim.ActiveTasks)
                if (t.consistId == consistId) { task = t; break; }
            if (task == null) return default;

            return new MovementContext
            {
                success = true,
                consistId = consistId,
                targetTrackId = targetResolve.trackId,
                originTrackId = originTrackId,
                task = task,
            };
        }

        void UpdateLocationOnArrival(DepotMoveTask task)
        {
            if (task?.vehicleIds == null) return;
            foreach (int vid in task.vehicleIds)
                VehicleLocationService.Instance?.SetInDepot(vid, task.toTrackId);
        }

        // ════════════════════════════════════════════════════════════
        //  MM-18f: walidacja prerekwizytów (crew + TC capacity)
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// MM-18f: walidacja crew (DriverAvailableHook) + TC capacity (TrafficControllerAcceptHook).
        /// Returns true gdy oba pass; false → fallback do legacy Servicing (omija ruch ale wykonuje
        /// service synchronicznie — nie blokujemy gameplaya gdy brak personelu).
        /// </summary>
        bool ValidatePrereqs(int vehicleId, string actionTypeName)
        {
            // Driver check
            if (DriverAvailableHook != null && !DriverAvailableHook.Invoke(vehicleId))
            {
                Log.Warn($"[Bridge] Brak maszynisty dla vehicle#{vehicleId} ({actionTypeName}) " +
                         "— fallback do legacy (manual dispatch)");
                return false;
            }

            // TC capacity check (consume slot przy accept)
            if (TrafficControllerAcceptHook != null && !TrafficControllerAcceptHook.Invoke(vehicleId, actionTypeName))
            {
                Log.Warn($"[Bridge] TrafficController odmówił akcji {actionTypeName} dla vehicle#{vehicleId} " +
                         "— brak slotów dyżurnego");
                return false;
            }

            return true;
        }
    }
}
