using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M9a Etap 1+2: Symulacja ruchu pociągów na mapie 2D OSM.
    /// FixedUpdate 50 Hz, iteracja po aktywnych TrainRun, interpolacja pozycji
    /// po polyline trasy, uproszczona fizyka (rozpęd/cruise/hamowanie).
    ///
    /// Podpięcie: GameObject w MapScene (child "Map" root lub osobny "TrainSimulation").
    /// Assembly: Timetable (dostęp do TrainRun, Route, TimetableService + Map types).
    ///
    /// Klasa rozbita na partial files:
    /// - <c>TrainRunSimulator.cs</c>           — pola, lifecycle, FixedUpdate dispatcher (ten plik)
    /// - <c>TrainRunSimulator.Spawn.cs</c>     — Check/Spawn/Despawn lifecycle TrainRun
    /// - <c>TrainRunSimulator.Blocks.cs</c>    — block + platform occupancy maps
    /// - <c>TrainRunSimulator.Priority.cs</c>  — IRJ priority + rush hour + tie-break
    /// - <c>TrainRunSimulator.Movement.cs</c>  — Advance (rozped/cruise/hamowanie) + polyline
    /// - <c>TrainRunSimulator.Visuals.cs</c>   — CreateVisual / UpdateVisualPosition
    /// - <c>TrainRunSimulator.Breakdowns.cs</c>— M7-3 awarie + self-repair
    /// - <c>TrainRunSimulator.Delays.cs</c>    — M6-5 reputation + M5 cascade do nastepnego kursu
    /// </summary>
    public partial class TrainRunSimulator : MonoBehaviour
    {
        // ── MP-2: Profiler markers ──────────────────────────────
        static readonly ProfilerMarker s_FixedUpdate = new("TrainRunSimulator.FixedUpdate");
        static readonly ProfilerMarker s_AdvanceLoop = new("TrainRunSimulator.AdvanceLoop");

        /// <summary>
        /// Scene-scoped singleton (nie DontDestroyOnLoad — żyje w MapScene).
        /// Ustawiany w Awake, czyszczony w OnDestroy. Dostęp z innych systemów
        /// (RescueService, WorkshopManager, RescueDispatchUI itd.) eliminuje
        /// O(scena) FindAnyObjectByType per call.
        /// </summary>
        public static TrainRunSimulator Instance { get; private set; }

        // ── Stałe wizualne ──────────────────────────────────────────

        /// <summary>Wysokość Y pociągów na mapie (ponad railways=8, POI=10).</summary>
        const float TrainYHeight = 12f;

        /// <summary>
        /// Rozmiar kwadratu pociągu na mapie [m].
        /// Celowo duży żeby był widoczny przy zoom out na poziomie województwa.
        /// Docelowo skalowany dynamicznie wg orthographicSize (M12c).
        /// </summary>
        const float TrainVisualSize = 300f;

        // ── State ───────────────────────────────────────────────────

        readonly Dictionary<int, SimulatedTrain> _activeTrains = new();

        // BUG-033: rate limit log spam — log Advance exception tylko raz per train per session.
        // 60Hz × 1000 trains z persistent error = ~60k log warns/s → console freeze.
        readonly HashSet<int> _alreadyWarnedTrains = new();
        readonly List<int> _despawnBuffer = new();

        /// <summary>Container GO pod który parentujemy wizualizacje pociągów.</summary>
        Transform _trainContainer;

        /// <summary>Shared material dla prostokątów pociągów.</summary>
        Material _trainMaterial;

        /// <summary>Shared mesh (quad) wielokrotnie używany.</summary>
        Mesh _trainMesh;

        /// <summary>Cache referencji do grafu pathfindingowego (lazy init).</summary>
        PathfindingGraph _graph;

        /// <summary>Publiczny dostęp do pathfinding graph (lazy init). M7-3c: rescue pathfinding.</summary>
        public PathfindingGraph Graph => GetGraph();

        /// <summary>Timer dla periodic UI refresh (co 5s realnych — MapTrainListUI status update).</summary>
        float _debugLogTimer;

        // ── Block occupancy (Etap 3) ────────────────────────────────

        /// <summary>
        /// Runtime block occupancy: blockKey → trainRunId.
        /// Jeśli blockKey jest w dictionary = sekcja zajęta przez dany pociąg.
        /// Brak wpisu = sekcja wolna.
        /// </summary>
        readonly Dictionary<int, int> _blockOccupancy = new();

        /// <summary>
        /// Runtime platform occupancy: platformId → trainRunId.
        /// Tor peronowy zajęty przez pociąg stojący na stacji (+ margines wjazdu/wyjazdu).
        /// </summary>
        readonly Dictionary<int, int> _platformOccupancy = new();

        /// <summary>
        /// Odroczone release platform: platformId → gameTimeSec kiedy zwolnić.
        /// Margines wyjazdu — tor jest jeszcze "zajęty" przez ~30s po odjeździe.
        /// </summary>
        readonly Dictionary<int, float> _platformReleaseTimers = new();

        // ── TD-013: spatial index pociągów czekających na blok (BlockedBySignal) ──
        /// <summary>
        /// blockKey → set trainIds aktualnie czekających na ten blok (state=BlockedBySignal).
        /// Lookup w `HasHigherPriorityWaiting` zamiast O(N) skanu wszystkich pociągów.
        /// Synchronizowany lazy w <see cref="SyncBlockWaitIndex"/> wywoływanym z `Advance`.
        /// </summary>
        readonly Dictionary<int, HashSet<int>> _trainsWaitingForBlock = new();

        /// <summary>
        /// trainId → aktualnie zarejestrowany blockKey (lub -1 gdy nie czeka). Backing index
        /// dla lazy sync — żeby wiedzieć skąd unregister gdy state lub block się zmienia.
        /// </summary>
        readonly Dictionary<int, int> _currentlyWaitingForBlock = new();

        // ── Publiczny dostęp (do debug/UI) ──────────────────────────

        /// <summary>Liczba aktualnie aktywnych pociągów na mapie.</summary>
        public int ActiveTrainCount => _activeTrains.Count;

        /// <summary>Enumeracja aktywnych pociągów (read-only, do debug overlay i UI).</summary>
        public IReadOnlyDictionary<int, SimulatedTrain> ActiveTrains => _activeTrains;

        /// <summary>TD-025: czy podany TrainRun jest aktualnie spawnowany na mapie.</summary>
        public bool IsActive(int trainRunId) => _activeTrains.ContainsKey(trainRunId);

        /// <summary>Mapa occupancy bloków (read-only, do debug overlay Etap 4).</summary>
        public IReadOnlyDictionary<int, int> BlockOccupancy => _blockOccupancy;

        /// <summary>Mapa occupancy torów peronowych (read-only, do debug overlay).</summary>
        public IReadOnlyDictionary<int, int> PlatformOccupancy => _platformOccupancy;

        // ── M9c handshake events ────────────────────────────────────

        /// <summary>Event: pociąg wystartował (pojawił się na mapie). Args: TrainRun.
        /// Static — subskrybują handshake services z dowolnego assembly bez reference do instancji.</summary>
        public static event System.Action<TrainRun> OnRunSpawned;

        /// <summary>
        /// M8-13 / §7.6: Hook Personnel do sprawdzania obsady zalogi przed Spawn.
        /// Input: (trainRunId, runDateIso). Output: true = mozna startowac, false = brak zalogi → odroczenie.
        /// Null = default (backward compat — zawsze startuj bez check).
        /// Set by <c>RailwayManager.Personnel.CrewAssignmentService</c>.
        /// </summary>
        public static System.Func<int /*trainRunId*/, string /*dateIso*/, bool> CrewCheckHook;

        /// <summary>Event: pociąg zakończył kurs i został usunięty z mapy. Args: TrainRun.
        /// W momencie emit'u runningVehicleIds jest jeszcze wypełnione (clear następuje po).</summary>
        public static event System.Action<TrainRun> OnRunDespawned;

        /// <summary>M6-2: Pociąg zatrzymał się na stacji (przejście Running → StoppedAtStation).
        /// Args: (TrainRun, stopIndex w timetable.stops, stationNodeId z grafu).
        /// Używane przez PassengerManager do alight + board.</summary>
        public static event System.Action<TrainRun, int, int> OnTrainArrivingAtStop;

        /// <summary>M6-2: Pociąg rusza z postoju (przejście StoppedAtStation → Running).
        /// Args: (TrainRun, stopIndex właśnie opuszczonego stopu, stationNodeId).</summary>
        public static event System.Action<TrainRun, int, int> OnTrainDepartingFromStop;

        /// <summary>
        /// M-TimetableUX F1.2: Pociąg zatrzymał się na stopie typu ZD (zmiana drużyny).
        /// Fired immediately AFTER <see cref="OnTrainArrivingAtStop"/> dla stopów z
        /// <see cref="StopType.ZD"/>. Personnel system (M8 CrewAssignmentService) subscribuje
        /// dla crew handover analytics + future crew swap workflow (F1.13a suggestions).
        /// Args: (TrainRun, stopIndex w timetable.stops, stationNodeId z grafu).
        /// </summary>
        public static event System.Action<TrainRun, int, int> OnCrewSwap;

        /// <summary>
        /// M-Maintenance asmdef split (2026-05-15): emit na końcu Awake po bootstrap'ie
        /// wszystkich wewnętrznych singletonów (handshake services, Economy, popup UI).
        /// Maintenance asmdef subskrybuje przez <c>MaintenanceBootstrapper</c> żeby zainstalować
        /// swoje singletony (WorkshopManager, RescueService, PartInventoryService,
        /// WorkshopsPanelUI, PartsPanelUI, MaintenanceAlertsUI, RescueDispatchUI) bez tworzenia
        /// cyklu Timetable→Maintenance asmdef references — Maintenance referuje Timetable,
        /// nie odwrotnie.
        /// </summary>
        public static event System.Action OnSimulatorBootstrapped;

        /// <summary>
        /// M9c handshake: spawn pociągu z podaniem listy vehicleIds oraz startowej pozycji 2D.
        /// Używane przez handshake z depot'u (gdy consist wyjedzie za bramę):
        /// consist otrzymuje pozycję home station, pociąg pojawia się na mapie.
        ///
        /// Różnice vs automatyczny SpawnTrain:
        /// - Ignoruje <see cref="ShouldStart"/> (handshake wymusza spawn nawet przed departure time)
        /// - Preset'uje tr.runningVehicleIds przed emit OnRunSpawned
        /// - worldStartPos na razie tylko zalogowane — M9c-2 doda override'owanie currentPositionOnRouteM.
        /// </summary>
        /// <returns>true gdy spawn się udał (TrainRun dodany do _activeTrains).</returns>
        public bool SpawnTrainFromVehicles(TrainRun tr, List<int> vehicleIds, Vector2 worldStartPos)
        {
            if (tr == null) return false;
            if (_activeTrains.ContainsKey(tr.id))
            {
                Log.Warn($"[TrainRunSimulator] SpawnTrainFromVehicles: run#{tr.id} już aktywny");
                return false;
            }

            tr.runningVehicleIds = vehicleIds != null ? new List<int>(vehicleIds) : new List<int>();
            SpawnTrain(tr);

            if (_activeTrains.ContainsKey(tr.id))
            {
                Log.Info($"[TrainRunSimulator] SpawnTrainFromVehicles: run#{tr.id} spawned " +
                         $"with {tr.runningVehicleIds.Count} vehicles at {worldStartPos}");
                return true;
            }
            return false;
        }

        // ── Unity lifecycle ─────────────────────────────────────────

        void Awake()
        {
            // Singleton guard — duplikat z poprzedniej sceny / spawnu
            if (Instance != null && Instance != this)
            {
                Log.Warn($"[TrainRunSimulator] Duplicate Awake — destroying second instance on '{gameObject.name}'");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // M9c: bootstrap handshake services (DontDestroyOnLoad singletony)
            VehicleLocationService.EnsureExists();
            DispatchService.EnsureExists();
            DepotMapHandshakeService.EnsureExists();
            DeliveryService.EnsureExists();
            TrainNotificationUI.EnsureExists();

            // M9c-5: idle vehicle visualizer — jako component na tym samym GO co simulator
            // (żyje w MapScene, widoczny na MapLayer 31). Nie DontDestroyOnLoad — scene-scoped.
            if (GetComponent<MapSystem.IdleVehicleVisualizer>() == null)
                gameObject.AddComponent<MapSystem.IdleVehicleVisualizer>();

            // DepotLocationPicker — nakładka wyboru depot (pokazuje się jeśli HomeDepotStationId == -1)
            DepotLocationPickerUI.EnsureExists();

            // M6-1: PassengerManager — symulacja agentów-pasażerów (OD matrix, spawning, boarding M6-2)
            RailwayManager.Timetable.Economy.PassengerManager.EnsureExists();

            // M6-3: EconomyManager — revenue tracking (bilety przy boardingu) + GameState.Money
            RailwayManager.Timetable.Economy.EconomyManager.EnsureExists();

            // M6-4: FinancePanelUI — zakładka Finanse w Depot (UIIntent.OpenFinancesPanel)
            RailwayManager.Timetable.Economy.FinancePanelUI.EnsureExists();

            // M6-5: ReputationManager — multi-level reputation (global/woj/station), events od delays
            RailwayManager.Timetable.Economy.ReputationManager.EnsureExists();

            // Container na wizualizacje pociągów
            var containerGo = new GameObject("ActiveTrains");
            containerGo.layer = 31; // MapLayer
            containerGo.transform.SetParent(transform);
            _trainContainer = containerGo.transform;

            // Shared material — MaterialFactory wybiera pipeline-aware shader (URP/Lit ?? Standard)
            // z wewnętrznym fallbackiem na error shader.
            _trainMaterial = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(_trainMaterial, new Color(0.1f, 0.8f, 0.1f, 1f));

            Log.Info($"[TrainRunSimulator] Shader: {(_trainMaterial.shader != null ? _trainMaterial.shader.name : "NULL")}");

            // Shared mesh — prostokąt (quad) widziany z góry
            // _trainMesh nie jest potrzebny — używamy CreatePrimitive(Cube) w CreateVisual

            // Debug overlay bloków (Etap 4) — toggle F9
            if (GetComponent<BlockDebugOverlay>() == null)
                gameObject.AddComponent<BlockDebugOverlay>();

            // Popup UI (Etap 10) — klik na pociąg / stację / tor
            if (GetComponent<TrainPopupUI>() == null)
                gameObject.AddComponent<TrainPopupUI>();
            if (GetComponent<StationPopupUI>() == null)
                gameObject.AddComponent<StationPopupUI>();
            if (GetComponent<TrackPopupUI>() == null)
                gameObject.AddComponent<TrackPopupUI>();

            // Map click handler — manual raycast (New Input System nie wspiera OnMouseDown)
            if (GetComponent<MapClickHandler>() == null)
                gameObject.AddComponent<MapClickHandler>();

            // M-Maintenance asmdef split (2026-05-15): emit event, MaintenanceBootstrapper
            // (Maintenance asmdef) subskrybuje aby zainstalować swoje singletony.
            OnSimulatorBootstrapped?.Invoke();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void FixedUpdate()
        {
            if (GameState.IsPaused) return;

            using var _profMarker = s_FixedUpdate.Auto();

            // Timer dla periodic UI refresh listy pociagow (co 5s realnych — patrz nizej).
            _debugLogTimer += Time.fixedDeltaTime;

            float deltaGameSec = Time.fixedDeltaTime * GameState.TimeScale;
            string todayIso = GameState.CurrentDateIso;

            // 0. TD-037: wznowienie aktywnych kursów z save (pending czeka aż graf async się zbuduje).
            //    PRZED CheckForNewTrains — wznowione runy nie mogą zostać uznane za "nowe do spawnu".
            TryConsumePendingRestore(Time.fixedDeltaTime);

            // 1. Sprawdź nowe TrainRun'y do spawnu
            CheckForNewTrains(todayIso);

            // 2. Advance aktywnych — substepping przy x150/x500
            //    Max 1s gry per substep żeby pociąg nie przeskoczył bloku/stacji.
            //    x1:   0.02s → 1 substep (brak overhead)
            //    x25:  0.5s  → 1 substep
            //    x150: 3s    → 3 substepy po 1s
            //    x500: 10s   → 10 substepów po 1s
            const float maxStepSec = 1f;
            int substeps = deltaGameSec > maxStepSec
                ? Mathf.CeilToInt(deltaGameSec / maxStepSec)
                : 1;
            float stepSec = deltaGameSec / substeps;

            using (s_AdvanceLoop.Auto())
            {
                foreach (var kvp in _activeTrains)
                {
                    try
                    {
                        for (int sub = 0; sub < substeps; sub++)
                            Advance(kvp.Value, stepSec);
                    }
                    catch (System.Exception ex)
                    {
                        // BUG-033: log tylko raz per-train (60Hz × N trains = log spam).
                        // Reset _alreadyWarnedTrains przy despawn (CollectAndDespawnCompleted).
                        if (_alreadyWarnedTrains.Add(kvp.Key))
                        {
                            Log.Warn($"[TrainRunSimulator] Exception w Advance dla Train #{kvp.Key} " +
                                     $"(suppressed dla kolejnych klatek): {ex.Message}\n{ex.StackTrace}");
                        }
                    }
                }
            }

            // 3. Despawn zakończonych (buffered — nie modyfikujemy dict w trakcie iteracji)
            CollectAndDespawnCompleted();

            // 4. Release timery platform (margines wyjazdu)
            ProcessPlatformReleaseTimers();

            // Periodic UI refresh — co 5s realnych aktualizujemy MapTrainListUI ze statusami.
            if (_debugLogTimer >= 5f)
            {
                _debugLogTimer = 0f;
                if (_activeTrains.Count > 0)
                {
                    var listUi = MapSystem.MapTrainListUI.Instance;
                    if (listUi != null)
                    {
                        foreach (var kvp in _activeTrains)
                        {
                            var s = kvp.Value;
                            if (!listUi.HasTrain(s.trainRun.id)) continue;

                            float kmDone = s.trainRun.currentPositionOnRouteM / 1000f;
                            float kmTotal = s.route.totalLengthM / 1000f;
                            float speedKmh = s.currentSpeedMps * 3.6f;
                            var cat = IrjCategoryCatalog.GetCode(s.timetable.irjCategory);
                            string nm = $"{s.trainRun.trainNumberSnapshot} ({cat})";
                            string status = $"{kmDone:F1}/{kmTotal:F1}km · {speedKmh:F0}km/h"
                                + (s.trainRun.currentDelaySec > 0 ? $" +{s.trainRun.currentDelaySec}s" : "");
                            listUi.UpdateTrainStatus(s.trainRun.id, nm, status,
                                GetCategoryColor(s.timetable.irjCategory.group));
                        }
                    }
                }
            }
        }

        /// <summary>Debug: wypisuje stan simulatora i wszystkich TrainRun do Console.</summary>
        [ContextMenu("Debug: Simulator Status")]
        public void DebugSimulatorStatus()
        {
            string todayIso = GameState.CurrentDateIso;
            int h = Mathf.FloorToInt(GameState.GameTimeSeconds / 3600f);
            int m = Mathf.FloorToInt((GameState.GameTimeSeconds % 3600f) / 60f);

            Log.Info($"[TrainRunSimulator] === DEBUG STATUS ===");
            Log.Info($"  GameDay={GameState.GameDay}, today={todayIso}, " +
                     $"gameTime={h:D2}:{m:D2} ({GameState.GameTimeSeconds:F0}s), " +
                     $"timeScale=x{GameState.TimeScale}, paused={GameState.IsPaused}");
            Log.Info($"  Total TrainRuns: {TimetableService.TrainRuns.Count}, " +
                     $"Active trains: {_activeTrains.Count}");

            var runs = TimetableService.TrainRuns;
            for (int i = 0; i < Mathf.Min(runs.Count, 20); i++)
            {
                var tr = runs[i];
                int depH = tr.startMinutesFromMidnight / 60;
                int depM = tr.startMinutesFromMidnight % 60;
                string match = tr.runDateIso == todayIso ? "MATCH" : "no-match";
                Log.Info($"  TR#{tr.id}: date={tr.runDateIso} ({match}), " +
                         $"dep={depH:D2}:{depM:D2}, circ=#{tr.circulationId}, " +
                         $"completed={tr.isCompleted}, cancelled={tr.isCancelled}, " +
                         $"train='{tr.trainNumberSnapshot}'");
            }

            if (runs.Count > 20)
                Log.Info($"  ... i {runs.Count - 20} więcej");
        }

        // ── Helpers ─────────────────────────────────────────────────

        PathfindingGraph GetGraph()
        {
            if (_graph != null) return _graph;

            var init = TimetableInitializer.Instance;
            if (init == null) return null;

            _graph = init.Graph;
            return _graph;
        }
    }
}
