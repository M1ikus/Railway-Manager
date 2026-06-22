using System;
using System.Collections.Generic;
using UnityEngine;
using DepotSystem;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;
using DepotSystem.RoomLevel;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;
using RailwayManager.Maintenance.Movement;

namespace RailwayManager.Maintenance
{
    /// <summary>
    /// M7-4 / MM-8: Slot warsztatowy — pozycja w hali gdzie może być wykonywany
    /// przegląd konkretnego pojazdu.
    ///
    /// MM-8 refactor (2026-05-05): slot **per ServicePit instance** (PlacedFurnitureItem
    /// z funkcją <see cref="DepotSystem.Furniture.ObjectFunction.ServicePit"/>) zamiast
    /// "slot per Hall room" (gdzie sztucznie tworzono N slotów wg <see cref="WorkshopLevel.MaxSlots"/>).
    ///
    /// Source of truth: <see cref="DepotSystem.Furniture.Placement.FurniturePlacer.PlacedInstances"/>
    /// + <see cref="DepotSystem.DetectedRoom.level"/> (Hall lvl). Gracz stawia pit_small (18m) /
    /// pit_medium (25m) / pit_large (35m) → każdy daje osobny slot z <see cref="maxVehicleLength"/>.
    ///
    /// Walidacja per slot: <c>vehicle.lengthM &lt;= slot.maxVehicleLength</c>.
    /// </summary>
    /// <summary>
    /// MM-18: faza przegladu w slocie warsztatowym (analog OutdoorJobState).
    ///
    /// <list type="bullet">
    /// <item><b>Idle</b> — slot wolny (occupyingVehicleId=-1)</item>
    /// <item><b>EnRoute</b> — slot zarezerwowany, pojazd jedzie po torze do ServicePit</item>
    /// <item><b>Inspecting</b> — pojazd na slocie, count-down przegladu</item>
    /// <item><b>Completed</b> — finishesGameTime minął, czeka na ReleaseSlot (auto-pickup do
    /// Idle przy zwolnieniu)</item>
    /// </list>
    /// </summary>
    public enum WorkshopSlotState
    {
        Idle,
        EnRoute,
        Inspecting,
        Completed,
    }

    [Serializable]
    public class WorkshopSlot
    {
        public int slotId;                       // auto-incrementing wewnętrzny
        public int roomId;                       // DetectedRoom.roomId (Hall) — context
        public int servicePitInstanceId;         // MM-8: PlacedFurnitureItem.instanceId (primary key)
        public WorkshopLevel level;              // dziedziczy z Hall.level (MM-4)
        public float maxVehicleLength;           // MM-D16: z FurnitureItem.maxVehicleLength

        // State
        public int occupyingVehicleId = -1;      // -1 = wolny
        public InspectionLevel currentInspection;
        public long startedGameTime;
        public long finishesGameTime;
        public bool isExternalZNTK;              // M7-5 placeholder

        // ── MM-18: phased movement ────────────────────────────────────

        /// <summary>MM-18: faza slota (Idle/EnRoute/Inspecting/Completed).</summary>
        public WorkshopSlotState state = WorkshopSlotState.Idle;

        /// <summary>MM-18: tor docelowy (resolved przez AccessTrackResolver dla ServicePit).</summary>
        public int targetTrackId = -1;

        /// <summary>MM-18: tor pochodzenia pojazdu (audit).</summary>
        public int originTrackId = -1;

        /// <summary>MM-18: consistId DepotMoveTask'a w fazie EnRoute.</summary>
        public int movementConsistId = -1;

        /// <summary>MM-18: pełny duration inspekcji (sekundy gry, recompute po dotarciu).</summary>
        public long inspectionDurationSec;
    }

    /// <summary>M7-5: Faza external job — dostawa out → inspection → dostawa back.</summary>
    public enum ExternalJobPhase
    {
        DeliveringOut,
        InInspection,
        DeliveringBack,
    }

    /// <summary>M7-5: Aktywne zadanie w zewnętrznym zakładzie (ZNTK/NEWAG/…).</summary>
    [Serializable]
    public class OngoingExternalJob
    {
        public int vehicleId;
        public InspectionLevel level;
        public string workshopId;
        public long startedGameTime;
        public long deliveryOutFinishGT;
        public long inspectionFinishGT;
        public long deliveryBackFinishGT;
        public float pathLengthM;
        public int totalCostGroszy;
        public ExternalJobPhase phase;
    }

    /// <summary>
    /// M7-4: Menedżer slotów warsztatowych w depotcie + wykonawca przeglądów.
    ///
    /// - Skanuje RoomDetectionSystem co X sekund → update listy slotów
    /// - API: AssignVehicle(vehicleId, level, slotId) — rezerwuje slot
    /// - Per-tick advance: gdy finishesGameTime minął → Complete przegląd
    /// - Complete: reset InspectionSchedule + component restore + TriggerCosts
    /// </summary>
    public class WorkshopManager : MonoBehaviour
    {
        public static WorkshopManager Instance { get; private set; }

        /// <summary>
        /// M8-12 / D2: Hook Personnel — mnoznik czasu przegladu wg avg skill mechanikow w slocie.
        /// Input: slotId. Output: multiplier (0.6-1.66, wzor <c>1/(0.5+skill/5)</c>).
        /// Null = default 1.0 (brak mechanikow → standardowy czas).
        /// Set by <c>RailwayManager.Personnel.WorkshopAssignmentService</c>.
        /// </summary>
        public static Func<int, float> SlotSpeedMultiplierHook;

        readonly List<WorkshopSlot> _slots = new();
        readonly List<OngoingExternalJob> _externalJobs = new();
        int _nextSlotId = 1;
        float _scanTimer;
        const float RoomScanInterval = 5f; // co 5 sec realtime

        // TD-031: watchdog wznowienia osieroconych manewrów serwisowych po load (analog DeliveryService).
        float _recoveryTimer;
        const float RecoveryCheckInterval = 1f;

        public IReadOnlyList<WorkshopSlot> Slots => _slots;
        public IReadOnlyList<OngoingExternalJob> ExternalJobs => _externalJobs;
        public event Action OnSlotsChanged;

        public int GetNextSlotId() => _nextSlotId;

        public void RestoreFromSave(IList<WorkshopSlot> slots, IList<OngoingExternalJob> externalJobs, int nextSlotId)
        {
            _slots.Clear();
            if (slots != null) _slots.AddRange(slots);

            _externalJobs.Clear();
            if (externalJobs != null) _externalJobs.AddRange(externalJobs);

            RecoverPostLoadState(nextSlotId);
        }

        public void RecoverPostLoadState(int nextSlotId)
        {
            _nextSlotId = nextSlotId > 0 ? nextSlotId : ComputeNextSlotId(_slots);
            RecoverInterruptedMovementAfterLoad();
            OnSlotsChanged?.Invoke();
        }

        public void ResetRuntimeState()
        {
            _slots.Clear();
            _externalJobs.Clear();
            _nextSlotId = 1;
            OnSlotsChanged?.Invoke();
        }

        static int ComputeNextSlotId(IList<WorkshopSlot> slots)
        {
            int maxId = 0;
            if (slots != null)
            {
                foreach (var slot in slots)
                    if (slot != null && slot.slotId > maxId) maxId = slot.slotId;
            }
            return maxId + 1;
        }

        void RecoverInterruptedMovementAfterLoad()
        {
            int pending = 0;
            foreach (var slot in _slots)
            {
                if (slot == null || slot.state != WorkshopSlotState.EnRoute) continue;
                if (slot.occupyingVehicleId < 0)
                {
                    ResetSlot(slot); // EnRoute bez pojazdu = niespójny stan → zwolnij slot
                    continue;
                }

                // TD-031: NIE teleportujemy slotu na Inspecting (stary RecoverEnRouteAsServicing —
                // pojazd „dojeżdżał" magicznie mimo że visual stoi w połowie drogi). Zostawiamy slot
                // w fazie EnRoute; faktyczny ruch wznowi watchdog (RecoverOrphanedSlotMovements) gdy
                // graf torów + visual consistu będą gotowe (depot_3d deserializuje się PO maintenance
                // w ModuleOrder). Tu tylko oznaczamy pojazd jako „wznawianie po load".
                var v = FleetService.GetOwnedById(slot.occupyingVehicleId);
                if (v != null)
                {
                    v.status = FleetVehicleStatus.OutOfService;
                    v.currentTask = $"Przeglad {slot.currentInspection} — wznawianie po load (slot #{slot.slotId})";
                    v.estimatedCompletionGameTime = slot.finishesGameTime;
                }

                pending++;
            }

            if (pending > 0)
            {
                Log.Info($"[Workshop] {pending} EnRoute slot(s) oczekuje na wznowienie ruchu po load " +
                         "(watchdog re-issue DepotMoveTask gdy scena zajezdni gotowa)");
                FleetService.NotifyOwnedChanged();
            }
        }

        public static WorkshopManager EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("WorkshopManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<WorkshopManager>();
            Log.Info("[WorkshopManager] Bootstrapped");
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnEnable()
        {
            // MM-4: instant rescan przy awansie pokoju Hall (zamiast czekać 5s scan tick)
            RoomLevelService.OnLevelChanged += HandleRoomLevelChanged;
        }

        void OnDisable()
        {
            RoomLevelService.OnLevelChanged -= HandleRoomLevelChanged;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void HandleRoomLevelChanged(int roomId, int oldLvl, int newLvl)
        {
            // Force immediate rescan — gracz właśnie awansował pokój, slot.level powinien
            // odświeżyć się natychmiast. RescanDepotRooms preserve'uje busy slots.
            RescanDepotRooms();
        }

        void Update()
        {
            _scanTimer += Time.deltaTime;
            if (_scanTimer >= RoomScanInterval)
            {
                _scanTimer = 0f;
                RescanDepotRooms();
            }

            AdvanceInspections();
            AdvanceExternalJobs();

            // MM-9: outdoor equipment jobs lifecycle (Wash/Rotate/PitLiftMaint/Refuel).
            // Tick wpięty w istniejący per-Update pattern WorkshopManager (analog do AdvanceInspections).
            OutdoorEquipmentJobService.CheckCompletions(CurrentGameTime());

            // MM-10: modernizacje pojazdów (External ZNTK + Internal Hall lvl5).
            ModernizationJobService.CheckCompletions(CurrentGameTime());

            // MM-11: modyfikacje posiadanych pojazdów (External + Internal symetrycznie,
            // wymiana wózków/wyposażenie/zmiana funkcji wagonu).
            VehicleModificationJobService.CheckCompletions(CurrentGameTime());

            // MM-12: self-paint w paint_bay mebel (Hall lvl ≥ 2).
            SelfPaintingService.CheckCompletions(CurrentGameTime());

            // TD-031: watchdog wznowienia osieroconych manewrów serwisowych po load (throttled).
            // Sloty przeglądów (inline) + 4 service'y (deleguj do bridge, który widzi resolve logic).
            _recoveryTimer += Time.deltaTime;
            if (_recoveryTimer >= RecoveryCheckInterval)
            {
                _recoveryTimer = 0f;
                RecoverOrphanedSlotMovements();
                OutdoorEquipmentMovementBridge.Instance?.RecoverInterruptedServiceMovements();
            }
        }

        /// <summary>
        /// MM-8 refactor: skan PlacedFurnitureItem z funkcją ServicePit. Per ServicePit
        /// instance tworzy <see cref="WorkshopSlot"/> z poziomem z Hall.level i maxLength
        /// z FurnitureItem.maxVehicleLength.
        ///
        /// Skip warunki:
        /// <list type="bullet">
        /// <item>ServicePit poza pokojem typu Hall (cell w bounds Hall room)</item>
        /// <item>Hall.level = 0 (przed SetRoomType)</item>
        /// <item>ServicePit functional state nie-Active (brak dojścia accessSide, MF-8)</item>
        /// </list>
        ///
        /// Preserve busy slots: jeśli slot ma <c>occupyingVehicleId &gt;= 0</c>, zachowuje
        /// progres inspection'u (slot match po servicePitInstanceId, nie indexie).
        ///
        /// Wywoływane:
        /// - co 5s (RoomScanInterval tick)
        /// - on RoomLevelService.OnLevelChanged (instant rescan po awansie)
        /// - on FurniturePlacer.OnPlacementStateChanged (przez ServicePitFurnitureBridge)
        /// </summary>
        public void RescanDepotRooms()
        {
            var rds = FindAnyObjectByType<DepotSystem.RoomDetectionSystem>();
            if (rds == null) return;

            var placer = DepotSystem.Furniture.Placement.FurniturePlacer.Instance;
            if (placer == null) placer = FindAnyObjectByType<DepotSystem.Furniture.Placement.FurniturePlacer>();

            // Index existing slots po servicePitInstanceId żeby preserve busy slots
            var existingByInstance = new Dictionary<int, WorkshopSlot>();
            foreach (var s in _slots)
                if (s.servicePitInstanceId > 0) existingByInstance[s.servicePitInstanceId] = s;

            var newSlots = new List<WorkshopSlot>();

            // Iteruj placed furniture; gdy null (gracz jeszcze nie wszedł w Furniture tool)
            // — _slots jest pusty (no ServicePits to scan).
            if (placer != null)
            {
                foreach (var inst in placer.PlacedInstances)
                {
                    if (inst == null) continue;
                    var item = DepotSystem.Furniture.FurnitureCatalog.FindById(inst.itemId);
                    if (item == null) continue;
                    if (!item.HasFunction(DepotSystem.Furniture.ObjectFunction.ServicePit)) continue;

                    // Functional state — kanał z brakiem dojścia (no accessSide free) nie liczy się
                    var fnState = placer.GetFunctionalState(inst.instanceId);
                    if (!fnState.IsActive) continue;

                    // Lookup pokoju (cell w bounds Hall)
                    var room = FindRoomContainingCell(rds, inst.position);
                    if (room == null) continue;
                    if (room.roomType != DepotSystem.RoomType.Hall) continue;

                    var level = WorkshopLevelExtensions.FromHallLevel(room.level);
                    if (level == WorkshopLevel.None) continue;

                    // Preserve busy slot lub stwórz nowy
                    if (existingByInstance.TryGetValue(inst.instanceId, out var existing))
                    {
                        existing.roomId = room.roomId;
                        existing.level = level;
                        existing.maxVehicleLength = item.maxVehicleLength;
                        newSlots.Add(existing);
                    }
                    else
                    {
                        newSlots.Add(new WorkshopSlot
                        {
                            slotId = _nextSlotId++,
                            servicePitInstanceId = inst.instanceId,
                            roomId = room.roomId,
                            level = level,
                            maxVehicleLength = item.maxVehicleLength,
                        });
                    }
                }
            }

            // Compare — jeśli zmiana, notify
            bool changed = newSlots.Count != _slots.Count;
            if (!changed)
            {
                // Sprawdź czy któryś istniejący slot zniknął (np. usunięty ServicePit)
                foreach (var s in _slots)
                {
                    bool found = false;
                    foreach (var n in newSlots)
                        if (n.slotId == s.slotId) { found = true; break; }
                    if (!found) { changed = true; break; }
                }
            }

            _slots.Clear();
            _slots.AddRange(newSlots);
            if (changed) OnSlotsChanged?.Invoke();
        }

        /// <summary>MM-8 helper: pokój zawierający cell na podstawie world position.</summary>
        static DepotSystem.DetectedRoom FindRoomContainingCell(DepotSystem.RoomDetectionSystem rds, Vector3 worldPos)
        {
            int cellX = Mathf.FloorToInt(worldPos.x);
            int cellZ = Mathf.FloorToInt(worldPos.z);
            var cell = new Vector2Int(cellX, cellZ);
            foreach (var r in rds.Rooms)
                if (r != null && r.bounds.Contains(cell)) return r;
            return null;
        }

        /// <summary>Zwraca wolne sloty które obsługują dany poziom przeglądu.</summary>
        public List<WorkshopSlot> GetAvailableSlots(InspectionLevel level)
        {
            var result = new List<WorkshopSlot>();
            foreach (var s in _slots)
                if (s.occupyingVehicleId < 0 && s.level.CanPerform(level))
                    result.Add(s);
            return result;
        }

        /// <summary>
        /// Przydziela pojazd do slotu i rozpoczyna przegląd. Zwraca true gdy ok.
        /// </summary>
        public bool AssignVehicle(int vehicleId, InspectionLevel inspLevel, int slotId)
        {
            var slot = _slots.Find(s => s.slotId == slotId);
            if (slot == null) { Log.Warn($"[Workshop] Slot#{slotId} nie istnieje"); return false; }
            if (slot.occupyingVehicleId >= 0) { Log.Warn($"[Workshop] Slot#{slotId} zajęty"); return false; }
            if (!slot.level.CanPerform(inspLevel))
            {
                Log.Warn($"[Workshop] Slot#{slotId} ({slot.level}) nie obsługuje {inspLevel}"); return false;
            }

            var v = FleetService.GetOwnedById(vehicleId);
            if (v == null) { Log.Warn($"[Workshop] Vehicle#{vehicleId} nie istnieje"); return false; }

            // M7-7: walidacja części (P3-P5 wymagają zapasu) — blokada gdy brak
            string missing = GetMissingPartsLabel(inspLevel);
            if (missing != null)
            {
                Log.Warn($"[Workshop] Brak części dla {inspLevel}: {missing}. Zamów w Magazynie.");
                return false;
            }

            // MM-8 / MM-D16: walidacja długości pojazdu per slot (slot zna konkretny
            // ServicePit dzięki refactorowi MM-8). Brak maxVehicleLength = 0 → pomijamy
            // walidację (defensive, np. legacy slot sprzed MM-8 lub niezasynchronizowany).
            if (slot.maxVehicleLength > 0f && v.lengthM > slot.maxVehicleLength + 0.01f)
            {
                Log.Warn($"[Workshop] Vehicle#{vehicleId} ({v.lengthM:F1}m) za długi dla " +
                         $"slot#{slotId} (max {slot.maxVehicleLength:F1}m). " +
                         "Wybierz większy slot (pit_medium=25m / pit_large=35m) lub wyślij do ZNTK.");
                return false;
            }

            // MM-18: 2-fazowy flow — slot zarezerwowany od razu, ale inspekcja zaczyna się
            // dopiero po dotarciu pojazdu do ServicePit.
            slot.occupyingVehicleId = vehicleId;
            slot.currentInspection = inspLevel;

            // M8-12: multiplier czasu wg skill mechanikow w slocie (hook Personnel)
            long baseDuration = GetDurationSeconds(inspLevel);
            float speedMult = SlotSpeedMultiplierHook?.Invoke(slotId) ?? 1.0f;
            slot.inspectionDurationSec = (long)(baseDuration * speedMult);

            // MM-18: spróbuj zaschedulować movement do ServicePit
            bool movementScheduled = TryScheduleMovementToSlot(slot, v);
            if (movementScheduled)
            {
                slot.state = WorkshopSlotState.EnRoute;
                slot.startedGameTime = 0;       // recompute po dotarciu
                slot.finishesGameTime = 0;
                v.status = FleetVehicleStatus.OutOfService;
                v.currentTask = $"Przegląd {inspLevel} — w drodze do slot#{slotId}";

                Log.Info($"[Workshop] Vehicle#{vehicleId} przydzielony do slot#{slotId} ({inspLevel}, EnRoute) " +
                         $"track#{slot.originTrackId} → #{slot.targetTrackId}, skill multiplier={speedMult:F2}");
            }
            else
            {
                // Legacy fallback (brak grafu / brak access track / DepotMovementSim missing)
                slot.state = WorkshopSlotState.Inspecting;
                slot.startedGameTime = CurrentGameTime();
                slot.finishesGameTime = slot.startedGameTime + slot.inspectionDurationSec;

                v.status = FleetVehicleStatus.InRepair;
                v.currentTask = $"Przegląd {inspLevel} w slocie #{slotId}";

                Log.Info($"[Workshop] Vehicle#{vehicleId} przydzielony do slot#{slotId} ({inspLevel}, " +
                         $"LEGACY immediate Inspecting), finish za {slot.inspectionDurationSec / 3600}h, " +
                         $"skill multiplier={speedMult:F2}");
            }

            FleetService.NotifyOwnedChanged();
            OnSlotsChanged?.Invoke();
            return true;
        }

        // ════════════════════════════════════════════════════════════════
        //  MM-18: Movement integration (DepotMovementSimulator + AccessTrackResolver)
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// MM-18: schedule movement do ServicePit (target = ServicePit furniture access track,
        /// origin = vehicle's depotTrackId). Returns true gdy ruch zaschedulowany;
        /// false gdy fallback do legacy immediate Inspecting.
        /// </summary>
        bool TryScheduleMovementToSlot(WorkshopSlot slot, FleetVehicleData vehicle)
        {
            var sim = DepotMovementSimulator.Instance;
            if (sim == null) return false;

            // Resolve target — ServicePit furniture access track
            var placer = DepotSystem.Furniture.Placement.FurniturePlacer.Instance;
            if (placer == null) return false;
            var pit = placer.GetInstance(slot.servicePitInstanceId);
            if (pit == null) return false;

            var graph = AccessTrackResolver.FindGraph();
            if (graph == null) return false;

            var targetResolve = AccessTrackResolver.FindAccessTrackFor(pit, graph);
            if (!targetResolve.isReachable) return false;

            // Resolve origin — vehicle's depotTrackId (fallback: pierwszy tor)
            int originTrackId = ResolveOriginTrackForVehicle(vehicle.id, graph);
            if (originTrackId < 0) return false;

            // Same track? — skip movement (pojazd już na ServicePit)
            if (originTrackId == targetResolve.trackId)
                return false; // legacy fallback = immediate Inspecting

            int consistId = sim.GenerateConsistId();
            var vehicleIds = new List<int> { vehicle.id };
            bool ok = sim.EnqueueMove(consistId, vehicleIds, originTrackId, targetResolve.trackId, targetResolve.accessPos);
            if (!ok) return false;

            DepotMoveTask task = null;
            foreach (var t in sim.ActiveTasks)
                if (t.consistId == consistId) { task = t; break; }

            if (task == null)
            {
                Log.Warn($"[Workshop] EnqueueMove succeeded ale task dla consist#{consistId} nie znaleziony");
                return false;
            }

            slot.movementConsistId = consistId;
            slot.targetTrackId = targetResolve.trackId;
            slot.originTrackId = originTrackId;

            int capturedSlotId = slot.slotId;
            task.onCompleted = (DepotMoveState state) => HandleSlotMovementFinished(capturedSlotId, state, task);
            return true;
        }

        /// <summary>
        /// MM-18: resolve origin track dla pojazdu (depot location → fallback first track).
        /// </summary>
        static int ResolveOriginTrackForVehicle(int vehicleId, TrackGraph graph)
        {
            var loc = VehicleLocationService.Instance?.Get(vehicleId);
            if (loc != null && loc.depotTrackId >= 0) return loc.depotTrackId;

            if (graph != null)
            {
                foreach (var kvp in graph.Tracks)
                    return kvp.Key;
            }
            return -1;
        }

        /// <summary>
        /// MM-18: callback po movement → przejście EnRoute na Inspecting (lub Failed na refund).
        /// </summary>
        void HandleSlotMovementFinished(int slotId, DepotMoveState state, DepotMoveTask task)
        {
            var slot = _slots.Find(s => s.slotId == slotId);
            if (slot == null) return;
            if (slot.state != WorkshopSlotState.EnRoute) return; // ignored — re-fire

            long now = CurrentGameTime();

            if (state == DepotMoveState.Completed)
            {
                slot.state = WorkshopSlotState.Inspecting;
                slot.startedGameTime = now;
                slot.finishesGameTime = now + slot.inspectionDurationSec;

                var v = FleetService.GetOwnedById(slot.occupyingVehicleId);
                if (v != null)
                {
                    v.status = FleetVehicleStatus.InRepair;
                    v.currentTask = $"Przegląd {slot.currentInspection} w slocie #{slotId}";
                }

                if (task?.vehicleIds != null)
                {
                    foreach (int vid in task.vehicleIds)
                        VehicleLocationService.Instance?.SetInDepot(vid, task.toTrackId);
                }

                Log.Info($"[Workshop] Slot#{slotId} EnRoute → Inspecting, finish za " +
                         $"{slot.inspectionDurationSec / 3600}h");
                FleetService.NotifyOwnedChanged();
                OnSlotsChanged?.Invoke();
            }
            else
            {
                // Movement failed — uwolnij slot z refund
                int vid = slot.occupyingVehicleId;
                Log.Warn($"[Workshop] Slot#{slotId} movement FAILED — {task?.failureReason ?? "unknown"}, " +
                         $"slot zwolniony, vehicle#{vid} zwrócony do StoppedInDepot");

                slot.state = WorkshopSlotState.Idle;
                slot.occupyingVehicleId = -1;
                slot.movementConsistId = -1;
                slot.targetTrackId = -1;
                slot.originTrackId = -1;
                slot.startedGameTime = 0;
                slot.finishesGameTime = 0;

                var v = FleetService.GetOwnedById(vid);
                if (v != null)
                {
                    v.status = FleetVehicleStatus.StoppedInDepot;
                    v.currentTask = null;
                }
                FleetService.NotifyOwnedChanged();
                OnSlotsChanged?.Invoke();
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  TD-031: recovery osieroconych manewrów przeglądu po load
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// TD-031: wznawia ruch do ServicePit dla slotów które po load zostały w fazie EnRoute
        /// bez aktywnego DepotMoveTask (delegat onCompleted nieserializowalny → DepotSavable pomija).
        /// Wzór z <c>DeliveryService</c> — wykrycie orphan stanu w tick loop. Wywoływane z
        /// <see cref="Update"/> (throttled). Re-issue manewr z tym samym consistId
        /// (<see cref="WorkshopSlot.movementConsistId"/>) — occupancy + visual już odtworzone pod nim.
        /// Gdy ruch strukturalnie niemożliwy → fallback do natychmiastowego Inspecting (nie zawieś slotu).
        /// </summary>
        void RecoverOrphanedSlotMovements()
        {
            var sim = DepotMovementSimulator.Instance;
            if (sim == null || !sim.IsGraphReady) return;

            long now = CurrentGameTime();
            bool anyChange = false;
            foreach (var slot in _slots)
            {
                if (slot == null || slot.state != WorkshopSlotState.EnRoute) continue;
                if (slot.occupyingVehicleId < 0) continue;

                int consistId = slot.movementConsistId;
                // visual nie odtworzony jeszcze (graf gotowy, ale RestoreParkedVisualsFromGraph
                // jeszcze nie spawnął) → czekaj następny tick; brak orphana gdy task już aktywny.
                if (consistId < 0 || !sim.HasConsistVisual(consistId) || sim.HasTaskForConsist(consistId)) continue;

                if (TryReissueSlotMovement(sim, slot))
                {
                    Log.Info($"[Workshop] Wznowiono ruch przeglądu slot#{slot.slotId} consist#{consistId} → track#{slot.targetTrackId}");
                    continue;
                }

                // Strukturalny fail (target nieosiągalny / brak napędu) → fallback Inspecting in-place.
                Log.Warn($"[Workshop] Nie można wznowić ruchu slot#{slot.slotId} consist#{consistId} " +
                         "— fallback: przegląd startuje na bieżącej pozycji");
                slot.state = WorkshopSlotState.Inspecting;
                slot.startedGameTime = now;
                if (slot.inspectionDurationSec <= 0)
                    slot.inspectionDurationSec = GetDurationSeconds(slot.currentInspection);
                slot.finishesGameTime = now + slot.inspectionDurationSec;
                slot.movementConsistId = -1;

                var v = FleetService.GetOwnedById(slot.occupyingVehicleId);
                if (v != null)
                {
                    v.status = FleetVehicleStatus.InRepair;
                    v.currentTask = $"Przeglad {slot.currentInspection} w slocie #{slot.slotId}";
                    v.estimatedCompletionGameTime = slot.finishesGameTime;
                }
                anyChange = true;
            }

            if (anyChange)
            {
                FleetService.NotifyOwnedChanged();
                OnSlotsChanged?.Invoke();
            }
        }

        /// <summary>
        /// TD-031: re-issue manewr przeglądu (RestoreActiveMove z reuse <see cref="WorkshopSlot.movementConsistId"/>)
        /// + podpięcie onCompleted na świeży task (przeskok EnRoute→Inspecting po dotarciu).
        /// Returns false gdy target nieosiągalny / EnqueueMove fail (caller robi fallback).
        /// </summary>
        bool TryReissueSlotMovement(DepotMovementSimulator sim, WorkshopSlot slot)
        {
            var placer = FurniturePlacer.Instance;
            if (placer == null) return false;
            var pit = placer.GetInstance(slot.servicePitInstanceId);
            if (pit == null) return false;

            var graph = AccessTrackResolver.FindGraph();
            if (graph == null) return false;

            var target = AccessTrackResolver.FindAccessTrackFor(pit, graph);
            if (!target.isReachable) return false;

            var vehicleIds = new List<int> { slot.occupyingVehicleId };
            if (!sim.RestoreActiveMove(slot.movementConsistId, vehicleIds, target.trackId, target.accessPos, false, false))
                return false;

            var task = sim.GetActiveTask(slot.movementConsistId);
            if (task == null) return false; // EnqueueMove zgłosił sukces ale brak taska — fallback

            slot.targetTrackId = target.trackId;
            int capturedSlotId = slot.slotId;
            task.onCompleted = (state) => HandleSlotMovementFinished(capturedSlotId, state, task);
            return true;
        }

        /// <summary>
        /// MM-4 / MM-D16: zwraca max <c>maxVehicleLength</c> wśród ServicePit-funkcyjnych
        /// mebli postawionych w danym pokoju. 0 = brak ServicePit w pokoju
        /// (walidacja długości wtedy pominięta — fallback do mechanizmu sprzed MM-4).
        ///
        /// Lookup: FurniturePlacer.PlacedInstances filter po cells w bounds pokoju
        /// + filter po HasFunction(ServicePit) + max maxVehicleLength.
        /// </summary>
        public static float GetMaxServicePitLengthInRoom(int roomId)
        {
            var rds = FindAnyObjectByType<RoomDetectionSystem>();
            if (rds == null) return 0f;

            DetectedRoom room = null;
            foreach (var r in rds.Rooms)
                if (r != null && r.roomId == roomId) { room = r; break; }
            if (room == null) return 0f;

            var placer = FurniturePlacer.Instance;
            if (placer == null) return 0f;

            float maxLen = 0f;
            foreach (var inst in placer.PlacedInstances)
            {
                if (inst == null) continue;
                int cellX = Mathf.FloorToInt(inst.position.x);
                int cellZ = Mathf.FloorToInt(inst.position.z);
                if (!room.bounds.Contains(new Vector2Int(cellX, cellZ))) continue;

                var item = FurnitureCatalog.FindById(inst.itemId);
                if (item == null) continue;
                if (!item.HasFunction(ObjectFunction.ServicePit)) continue;
                if (item.maxVehicleLength > maxLen) maxLen = item.maxVehicleLength;
            }
            return maxLen;
        }

        /// <summary>
        /// M7-7: sprawdza czy magazyn ma wszystkie wymagane części dla danego poziomu.
        /// Zwraca null gdy wszystko OK, albo string z listą brakujących (dla UI tooltip).
        /// P1/P2 nie wymagają części — zawsze null.
        /// </summary>
        public static string GetMissingPartsLabel(InspectionLevel level)
        {
            var inv = PartInventoryService.Instance;
            if (inv == null) return null; // brak serwisu — nie blokuj

            var missing = new List<string>();
            foreach (var (type, qty) in GetPartsForLevel(level))
            {
                int have = inv.GetStock(type);
                if (have < qty)
                {
                    var info = PartCatalog.Get(type);
                    missing.Add($"{info.displayName} ({have}/{qty})");
                }
            }
            return missing.Count == 0 ? null : string.Join(", ", missing);
        }

        /// <summary>Per-tick check — ukończone przeglądy.</summary>
        void AdvanceInspections()
        {
            long now = CurrentGameTime();
            bool anyChange = false;
            foreach (var s in _slots)
            {
                if (s.occupyingVehicleId < 0) continue;
                // MM-18: pominiemy sloty w EnRoute (DepotMovementSimulator tickuje, callback
                // przejmie state). Timer leci tylko w Inspecting.
                if (s.state == WorkshopSlotState.EnRoute) continue;
                if (s.state == WorkshopSlotState.Completed) continue;
                if (s.finishesGameTime <= 0 || now < s.finishesGameTime) continue;

                CompleteInspection(s);
                s.state = WorkshopSlotState.Completed;
                anyChange = true;
            }
            if (anyChange) OnSlotsChanged?.Invoke();
        }

        // ── M7-5: External ZNTK dispatch ────────────────────────────

        /// <summary>
        /// M7-5: Wyślij pojazd do zewnętrznego zakładu (ZNTK/NEWAG/PESA/Cegielski).
        /// Oblicza A* home depot → workshop station, registers external job z fazami
        /// dostawa→przegląd→powrót. Koszt (fee + dostawa) pobrany od razu przy dispatch.
        /// </summary>
        public bool SendToExternal(int vehicleId, InspectionLevel level, string workshopId)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            if (v == null) { Log.Warn($"[Workshop] Vehicle#{vehicleId} nieznany"); return false; }
            if (v.status == FleetVehicleStatus.InRepair)
            {
                Log.Warn($"[Workshop] Vehicle#{vehicleId} już zajęty (InRepair)"); return false;
            }

            var workshop = ExternalWorkshopCatalog.GetById(workshopId);
            if (workshop == null) { Log.Warn($"[Workshop] Unknown workshop '{workshopId}'"); return false; }
            if (!workshop.CanPerformLevel(level))
            {
                Log.Warn($"[Workshop] {workshop.name} nie obsługuje {level}"); return false;
            }
            if (!workshop.CanServeType(v.type))
            {
                Log.Warn($"[Workshop] {workshop.name} nie specjalizuje się w {v.type}"); return false;
            }

            // Pathfinding — home station → workshop station
            var sim = TrainRunSimulator.Instance;
            var graph = sim != null ? sim.Graph : null;
            int homeNodeId = GameState.HomeDepotStationId;
            if (graph == null || homeNodeId < 0)
            {
                Log.Warn($"[Workshop] Graph/home unavailable — nie można policzyć trasy"); return false;
            }

            var init = FindAnyObjectByType<TimetableInitializer>();
            var station = init != null ? init.FindStation(workshop.locationStationName) : null;
            if (station == null || station.pathNodeId < 0)
            {
                Log.Warn($"[Workshop] Stacja '{workshop.locationStationName}' nieznaleziona na mapie " +
                         "(brak w POIs albo brak węzła w snapRadius)"); return false;
            }

            var res = RailwayPathfinder.FindPath(graph, homeNodeId, station.pathNodeId);
            if (!res.success)
            {
                Log.Warn($"[Workshop] Brak ścieżki home → {workshop.name}"); return false;
            }

            float pathLengthM = res.totalLengthM;
            // BUG-055: per-vehicle maxSpeed (cap 80 km/h dla pasywnej dostawy push/pull).
            // Wcześniej hardcoded 80 km/h dla wszystkich pojazdów — Pendolino/FLIRT 160/Griffin
            // niepotrzebnie ograniczone. Min(80, maxSpeed) bo dostawa zwykle nie idzie na max
            // speed (lokomotywa towarowa pcha lub holuje, ograniczenia trasy).
            float deliverySpeedKmh = Mathf.Min(80f, v.maxSpeedKmh > 0 ? v.maxSpeedKmh : 80f);
            float speedMs = deliverySpeedKmh * 1000f / 3600f;
            long deliveryOutSec = (long)(pathLengthM / speedMs);
            long inspSec = (long)(GetDurationSeconds(level) * workshop.durationMultiplier);
            long deliveryBackSec = deliveryOutSec;

            // Koszt: fee (base × multiplier) + dostawa (2 × km × opCostPerKm w gr)
            int baseFee = GetInternalCostGroszy(level);
            int fee = (int)(baseFee * workshop.priceMultiplier);
            var consist = new List<int> { vehicleId };
            int costPerKmGr = RailwayManager.Timetable.Economy.CostCalculator.GetConsistOperationalCostPerKm(consist);
            int deliveryCost = (int)((pathLengthM / 1000f) * 2f * costPerKmGr); // [gr]
            int totalCost = fee + deliveryCost;

            long now = CurrentGameTime();
            var job = new OngoingExternalJob
            {
                vehicleId = vehicleId,
                level = level,
                workshopId = workshopId,
                startedGameTime = now,
                deliveryOutFinishGT = now + deliveryOutSec,
                inspectionFinishGT = now + deliveryOutSec + inspSec,
                deliveryBackFinishGT = now + deliveryOutSec + inspSec + deliveryBackSec,
                pathLengthM = pathLengthM,
                totalCostGroszy = totalCost,
                phase = ExternalJobPhase.DeliveringOut,
            };
            _externalJobs.Add(job);

            v.status = FleetVehicleStatus.InRepair;
            v.currentTask = $"Transport do {workshop.name}";

            var econ = RailwayManager.Timetable.Economy.EconomyManager.Instance;
            econ?.AddCost(-1, totalCost, "maintenance_external",
                $"{workshop.name} P{(int)level + 1} ({pathLengthM / 1000f:F0}km)");

            FleetService.NotifyOwnedChanged();
            OnSlotsChanged?.Invoke();

            Log.Info($"[Workshop] SendToExternal: #{vehicleId} → {workshop.name}, " +
                     $"path {pathLengthM / 1000f:F1}km, fee {fee / 100f:F0}zł + delivery {deliveryCost / 100f:F0}zł " +
                     $"= total {totalCost / 100f:F0}zł, " +
                     $"ETA done {(deliveryOutSec + inspSec + deliveryBackSec) / 3600}h");
            return true;
        }

        void AdvanceExternalJobs()
        {
            if (_externalJobs.Count == 0) return;

            long now = CurrentGameTime();
            bool anyChange = false;

            for (int i = _externalJobs.Count - 1; i >= 0; i--)
            {
                var j = _externalJobs[i];

                if (j.phase == ExternalJobPhase.DeliveringOut && now >= j.deliveryOutFinishGT)
                {
                    j.phase = ExternalJobPhase.InInspection;
                    var v = FleetService.GetOwnedById(j.vehicleId);
                    var workshop = ExternalWorkshopCatalog.GetById(j.workshopId);
                    if (v != null && workshop != null)
                        v.currentTask = $"Przegląd {j.level} w {workshop.name}";
                    _externalJobs[i] = j;
                    anyChange = true;
                    Log.Info($"[Workshop] External job vehicle#{j.vehicleId} dotarł do zakładu — start przegląd");
                }
                else if (j.phase == ExternalJobPhase.InInspection && now >= j.inspectionFinishGT)
                {
                    // Perform inspection — actual maintenance done
                    var v = FleetService.GetOwnedById(j.vehicleId);
                    var workshop = ExternalWorkshopCatalog.GetById(j.workshopId);
                    if (v != null)
                    {
                        v.inspections.Perform(j.level, now, v.mileageKm);
                        RestoreComponentsForLevel(v, j.level);

                        string workshopName = workshop != null ? workshop.name : j.workshopId;
                        v.currentTask = $"Powrót z {workshopName}";

                        v.history.Add(new MaintenanceRecord
                        {
                            gameTimeSeconds = now,
                            recordType = MaintenanceRecordTypes.Inspection(j.level, external: true),
                            description = $"Wykonany w {workshopName}",
                            cost = j.totalCostGroszy / 100,
                            mileageAtRecord = v.mileageKm,
                        });
                    }

                    j.phase = ExternalJobPhase.DeliveringBack;
                    _externalJobs[i] = j;
                    anyChange = true;
                    Log.Info($"[Workshop] External przegląd ukończony dla #{j.vehicleId} — powrót do depotu");

                    // Reputation bonus jak w internal (P3+)
                    if (j.level >= InspectionLevel.P3)
                    {
                        var rep = RailwayManager.Timetable.Economy.ReputationManager.Instance;
                        rep?.ApplyEvent(RailwayManager.Timetable.Economy.ReputationEventType.VehicleUpgrade,
                            null, null, $"Przegląd {j.level} vehicle#{j.vehicleId} (ZNTK)");
                    }
                }
                else if (j.phase == ExternalJobPhase.DeliveringBack && now >= j.deliveryBackFinishGT)
                {
                    var v = FleetService.GetOwnedById(j.vehicleId);
                    if (v != null)
                    {
                        v.status = FleetVehicleStatus.StoppedInDepot;
                        v.currentTask = null;
                    }
                    _externalJobs.RemoveAt(i);
                    anyChange = true;
                    Log.Info($"[Workshop] External job complete dla #{j.vehicleId} — wrócił do depotu");
                }
            }

            if (anyChange)
            {
                FleetService.NotifyOwnedChanged();
                OnSlotsChanged?.Invoke();
            }
        }

        /// <summary>M7-5: czy pojazd ma aktywne zadanie w external ZNTK.</summary>
        public bool HasExternalJob(int vehicleId)
        {
            foreach (var j in _externalJobs)
                if (j.vehicleId == vehicleId) return true;
            return false;
        }

        public bool TryGetExternalJob(int vehicleId, out OngoingExternalJob job)
        {
            foreach (var j in _externalJobs)
                if (j.vehicleId == vehicleId) { job = j; return true; }
            job = null;
            return false;
        }

        // ────────────────────────────────────────────────────────────

        void CompleteInspection(WorkshopSlot slot)
        {
            var v = FleetService.GetOwnedById(slot.occupyingVehicleId);
            if (v == null) { ResetSlot(slot); return; }

            long now = CurrentGameTime();
            v.inspections.Perform(slot.currentInspection, now, v.mileageKm);

            // Restore components per poziom
            RestoreComponentsForLevel(v, slot.currentInspection);

            // M7-6: Konsumuj części z magazynu (P4/P5 — duże przeglądy wymagają parts).
            // MVP: tylko log warning gdy brak — nie blokuje ukończenia. Pełna blokada w M7-7.
            ConsumePartsForInspection(slot.currentInspection);

            // Koszt wewnętrznego przeglądu (parts + pensje placeholder)
            int costGroszy = GetInternalCostGroszy(slot.currentInspection);
            var econ = RailwayManager.Timetable.Economy.EconomyManager.Instance;
            econ?.AddCost(-1, costGroszy, "maintenance", $"P{(int)slot.currentInspection + 1} internal");

            v.history.Add(new MaintenanceRecord
            {
                gameTimeSeconds = now,
                recordType = MaintenanceRecordTypes.Inspection(slot.currentInspection, external: false),
                description = $"Wykonany w slocie #{slot.slotId}",
                cost = costGroszy / 100,
                mileageAtRecord = v.mileageKm
            });

            v.status = FleetVehicleStatus.StoppedInDepot;
            v.currentTask = null;

            Log.Info($"[Workshop] Slot#{slot.slotId} — przegląd {slot.currentInspection} " +
                     $"na vehicle#{v.id} ukończony, koszt {costGroszy / 100f:F0}zł");

            // Reputation bonus za terminowy
            var rep = RailwayManager.Timetable.Economy.ReputationManager.Instance;
            if (rep != null && slot.currentInspection >= InspectionLevel.P3)
            {
                rep.ApplyEvent(RailwayManager.Timetable.Economy.ReputationEventType.VehicleUpgrade,
                               null, null, $"Przegląd {slot.currentInspection} vehicle#{v.id}");
            }

            ResetSlot(slot);
            FleetService.NotifyOwnedChanged();
        }

        /// <summary>
        /// M7-6: Konsumuj części z magazynu zgodnie z poziomem przeglądu.
        /// MVP: tylko log warning gdy brak. Pełna blokada (require parts) w M7-7.
        ///
        /// Heurystyka ile części per level (placeholder do M6.5 Rebalance):
        /// - P1/P2 — drobne materiały, 0 parts (konsumowalne wliczone w koszt)
        /// - P3 — 1× Wheels (obracanie)
        /// - P4 — 1× Brake, 1× Doors, 1× Wheels (wymiana)
        /// - P5 — 1× Engine, 1× Interior, 1× Electrical, 1× Body, 1× Wheels, 1× Pantograph
        /// </summary>
        static void ConsumePartsForInspection(InspectionLevel level)
        {
            var inv = PartInventoryService.Instance;
            if (inv == null) return;

            var needed = GetPartsForLevel(level);
            foreach (var (type, qty) in needed)
            {
                inv.ConsumePart(type, qty); // loguje warning gdy brak
            }
        }

        static IEnumerable<(ComponentType type, int qty)> GetPartsForLevel(InspectionLevel level)
        {
            switch (level)
            {
                case InspectionLevel.P3:
                    yield return (ComponentType.Wheels, 1);
                    break;
                case InspectionLevel.P4:
                    yield return (ComponentType.Brake, 1);
                    yield return (ComponentType.Doors, 1);
                    yield return (ComponentType.Wheels, 1);
                    break;
                case InspectionLevel.P5:
                    yield return (ComponentType.Engine, 1);
                    yield return (ComponentType.Interior, 1);
                    yield return (ComponentType.Electrical, 1);
                    yield return (ComponentType.Body, 1);
                    yield return (ComponentType.Wheels, 1);
                    yield return (ComponentType.Pantograph, 1);
                    break;
                // P1/P2 — drobne materiały, no parts consumed
            }
        }

        static void RestoreComponentsForLevel(FleetVehicleData v, InspectionLevel level)
        {
            // Im wyższy poziom, tym więcej restore
            float restoreAmount = level switch
            {
                InspectionLevel.P1 => 5f,    // +5%
                InspectionLevel.P2 => 15f,   // +15%
                InspectionLevel.P3 => 35f,   // +35%
                InspectionLevel.P4 => 60f,   // +60%
                InspectionLevel.P5 => 100f,  // full reset = nowy pojazd
                _ => 0f
            };

            var c = v.components;
            if (c == null) return;
            if (level == InspectionLevel.P5)
            {
                c.SetAll(100f);
            }
            else
            {
                if (c.engineCondition >= 0f) c.engineCondition = Mathf.Min(100f, c.engineCondition + restoreAmount);
                if (c.brakeCondition >= 0f) c.brakeCondition = Mathf.Min(100f, c.brakeCondition + restoreAmount);
                if (c.doorsCondition >= 0f) c.doorsCondition = Mathf.Min(100f, c.doorsCondition + restoreAmount);
                if (c.acCondition >= 0f) c.acCondition = Mathf.Min(100f, c.acCondition + restoreAmount);
                if (c.bodyCondition >= 0f) c.bodyCondition = Mathf.Min(100f, c.bodyCondition + restoreAmount);
                if (c.wheelsCondition >= 0f) c.wheelsCondition = Mathf.Min(100f, c.wheelsCondition + restoreAmount);
                if (c.electricalCondition >= 0f) c.electricalCondition = Mathf.Min(100f, c.electricalCondition + restoreAmount);
                if (c.interiorCondition >= 0f) c.interiorCondition = Mathf.Min(100f, c.interiorCondition + restoreAmount);
                if (c.lightsCondition >= 0f) c.lightsCondition = Mathf.Min(100f, c.lightsCondition + restoreAmount);
                if (c.toiletsCondition >= 0f) c.toiletsCondition = Mathf.Min(100f, c.toiletsCondition + restoreAmount);
                if (c.pantographCondition >= 0f) c.pantographCondition = Mathf.Min(100f, c.pantographCondition + restoreAmount);
                if (c.couplingCondition >= 0f) c.couplingCondition = Mathf.Min(100f, c.couplingCondition + restoreAmount);
            }
            v.conditionPercent = c.Average();
        }

        void ResetSlot(WorkshopSlot s)
        {
            s.occupyingVehicleId = -1;
            s.startedGameTime = 0;
            s.finishesGameTime = 0;
            s.isExternalZNTK = false;
            // MM-18: reset state machine
            s.state = WorkshopSlotState.Idle;
            s.movementConsistId = -1;
            s.targetTrackId = -1;
            s.originTrackId = -1;
            s.inspectionDurationSec = 0;
        }

        static long CurrentGameTime()
            => (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;

        /// <summary>Czas trwania przeglądu w sec game time (internal warsztat).</summary>
        static long GetDurationSeconds(InspectionLevel level) => level switch
        {
            InspectionLevel.P1 => 2L * 3600L,             // 2 h
            InspectionLevel.P2 => 8L * 3600L,             // 8 h
            InspectionLevel.P3 => 2L * 86400L,            // 2 dni
            InspectionLevel.P4 => 14L * 86400L,           // 2 tygodnie
            InspectionLevel.P5 => 60L * 86400L,           // 2 miesiące
            _ => 0L
        };

        /// <summary>Koszt przeglądu u siebie [gr] (parts + pensje baseline).</summary>
        static int GetInternalCostGroszy(InspectionLevel level) => level switch
        {
            InspectionLevel.P1 => 200_000,           // 2k zł
            InspectionLevel.P2 => 1_000_000,         // 10k
            InspectionLevel.P3 => 4_000_000,         // 40k
            InspectionLevel.P4 => 30_000_000,        // 300k
            InspectionLevel.P5 => 200_000_000,       // 2M
            _ => 100_000
        };

        /// <summary>
        /// M7-4 helper: Lista pojazdów kwalifikujących się do przeglądu,
        /// posortowana malejąco po urgency (najbardziej pilny na górze).
        ///
        /// threshold=0.8 — ostrzegawczy poziom (80% limitu), 1.0+ = overdue.
        /// Pomija pojazdy które są aktualnie w przeglądzie (InRepair).
        /// </summary>
        public struct OverdueEntry
        {
            public FleetVehicleData vehicle;
            public InspectionLevel level;
            public float progress;
        }

        public List<OverdueEntry> GetOverdueVehicles(float threshold = 0.8f)
        {
            var result = new List<OverdueEntry>();
            long now = CurrentGameTime();

            foreach (var v in FleetService.OwnedVehicles)
            {
                if (v == null || v.inspections == null) continue;
                if (v.status == FleetVehicleStatus.InRepair) continue;

                var intervals = InspectionCatalog.GetForSeries(v.seriesId);
                var urgent = v.inspections.GetMostUrgent(intervals, now, v.mileageKm);
                if (urgent.progress < threshold) continue;

                result.Add(new OverdueEntry
                {
                    vehicle = v,
                    level = urgent.level,
                    progress = urgent.progress,
                });
            }

            result.Sort((a, b) => b.progress.CompareTo(a.progress));
            return result;
        }

        [ContextMenu("Debug: Dump workshop state")]
        public void DebugDump()
        {
            Log.Info($"[Workshop] {_slots.Count} slotów (MM-8 slot per ServicePit):");
            foreach (var s in _slots)
            {
                string status = s.occupyingVehicleId < 0 ? "WOLNY"
                              : $"vehicle#{s.occupyingVehicleId} {s.currentInspection}, ETA {(s.finishesGameTime - CurrentGameTime()) / 3600}h";
                Log.Info($"  Slot#{s.slotId} room#{s.roomId} pit#{s.servicePitInstanceId} " +
                         $"{s.level} (max {s.maxVehicleLength:F0}m): {status}");
            }
        }
    }
}
