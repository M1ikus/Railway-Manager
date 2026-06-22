using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    /// <summary>
    /// M9b Etap 1: driver ruchu pociągów w zajezdni 3D.
    ///
    /// Przyjmuje zadania (consist z toru A do toru B), wykonuje pathfinding przez
    /// TrackGraph, auto-ustawia rozjazdy wg ścieżki, konstruuje polyline do interpolacji.
    ///
    /// Etap 1: pathfinding + blade switching + polyline build. Interpolacja pozycji
    /// (ruch visual kubików) dochodzi w Etap 2. Etap 1 kończy zadanie instant
    /// (Moving → Completed bez ruchu).
    ///
    /// Clock: GameState.DepotTimeScale (cap x5 wg decyzji M9 design spec).
    ///
    /// Klasa rozbita na partial files:
    /// - <c>DepotMovementSimulator.cs</c>             — pola, lifecycle, FixedUpdate (ten plik)
    /// - <c>DepotMovementSimulator.Tasks.cs</c>       — public API: EnqueueMove/Exit/SpawnConsistAtEntry
    /// - <c>DepotMovementSimulator.Movement.cs</c>    — ProcessTask, FinalizeTask, AdvanceMovement
    /// - <c>DepotMovementSimulator.Pathfinding.cs</c> — ExecutePathfinding + BuildPolyline + lookup helpers
    /// - <c>DepotMovementSimulator.Visuals.cs</c>     — EnsureVisualForConsist, UpdateVisualPosition
    /// - <c>DepotMovementSimulator.Boundary.cs</c>    — IsOutsideDepot, FindGateCrossing, despawn margin
    /// - <c>DepotMovementSimulator.Debug.cs</c>       — DebugTestMove, DebugTestSpawnEntry
    /// </summary>
    public partial class DepotMovementSimulator : MonoBehaviour
    {
        // ── MP-2: Profiler markers ──────────────────────────────
        static readonly ProfilerMarker s_FixedUpdate = new("DepotMovementSimulator.FixedUpdate");

        public static DepotMovementSimulator Instance { get; private set; }

        /// <summary>
        /// M8-11 / D33: Hook Personnel dla priorytetyzacji manewrów (dyzurny ruchu).
        /// Null = default FCFS (backward compat). Set by <c>RailwayManager.Personnel.TrafficControlService</c>.
        /// Przy Enqueue wywoluje <see cref="IDepotTaskPriorityProvider.ComputePriority"/>.
        /// </summary>
        public static IDepotTaskPriorityProvider PriorityProvider { get; set; }

        /// <summary>Event: consist dotarł do końca Exit track i opuścił depot.
        /// Args: (consistId, vehicleIds). VehicleIds są przekazywane inline bo visual
        /// ConsistMarker jest już zdestroyowany do tego momentu. Subskrybuje M9c handshake
        /// (spawn na mapie 2D na home station).</summary>
        public static event System.Action<int /*consistId*/, System.Collections.Generic.List<int> /*vehicleIds*/> OnConsistExitedDepot;

        /// <summary>Event: consist wjechał do depot i zatrzymał się przed bramą (pień permanentnego toru).
        /// Args: (consistId, vehicleIds). Gotowy do sterowania — gracz może go wybrać i wysłać na parking.
        /// Subskrybuje M9c confirm.</summary>
        public static event System.Action<int /*consistId*/, System.Collections.Generic.List<int> /*vehicleIds*/> OnConsistEnteredDepot;

        /// <summary>TD-032: consist dojechał DO STYKU za innym składem (ruch gracza) — UI pokazuje prompt
        /// „Połączyć X z Y?". Args: (moverConsistId, blockerConsistId, contactWorldPos).</summary>
        public static event System.Action<int /*mover*/, int /*blocker*/, Vector3 /*contactWorldPos*/> OnConsistArrivedAtContact;

        // ── Fizyka manewrów zajezdni (wolniejsze niż na mapie) ──

        /// <summary>Cruising speed przy manewrach [m/s]. ~36 km/h.</summary>
        const float DepotCruiseSpeedMps = 10f;
        /// <summary>Accel [m/s²] — delikatniejsze niż na mapie.</summary>
        const float DepotAccelMps2 = 0.5f;
        /// <summary>Decel [m/s²].</summary>
        const float DepotDecelMps2 = 0.8f;
        /// <summary>Wysokość Y kubika consist'u ponad torem [m].</summary>
        const float VehicleYHeight = 1.5f;
        /// <summary>Fallback scale dla consist'u bez danych fleet (placeholder 20m).</summary>
        static readonly Vector3 VehicleScale = new Vector3(3f, 3f, 20f); // 3×3×20m — fallback

        /// <summary>
        /// M-Fleet-4: oblicza scale cube'a visual'u consist'u z rzeczywistych długości pojazdów.
        /// Suma <see cref="RailwayManager.Fleet.FleetVehicleData.lengthM"/> wszystkich vehicleIds.
        /// Fallback do VehicleScale gdy brak danych (np. Debug consist z fake vehicleIds).
        /// </summary>
        static Vector3 ComputeConsistScale(System.Collections.Generic.List<int> vehicleIds)
        {
            if (vehicleIds == null || vehicleIds.Count == 0) return VehicleScale;

            float totalLength = 0f;
            foreach (var v in RailwayManager.Fleet.FleetService.OwnedVehicles)
            {
                if (vehicleIds.Contains(v.id) && v.lengthM > 0f)
                    totalLength += v.lengthM;
            }
            if (totalLength < 1f) return VehicleScale;

            return new Vector3(VehicleScale.x, VehicleScale.y, totalLength);
        }

        readonly List<DepotMoveTask> _tasks = new();

        /// <summary>Persistent visual GO per consist. Nie niszczymy po task — reused w kolejnych ruchach.</summary>
        readonly Dictionary<int, GameObject> _consistVisuals = new();

        /// <summary>Monotonic counter do generowania unikalnych consistId (M9c handshake).
        /// Używany przez <see cref="GenerateConsistId"/>. Start od 1000 żeby nie kolidować
        /// z testowymi ID (999 w DebugTestMove, 888+ w DebugTestSpawnEntry).</summary>
        int _nextConsistId = 1000;

        /// <summary>Generuje unikalny consistId — dla handshake'u Map→Depot gdy pojazd wraca.</summary>
        public int GenerateConsistId() => _nextConsistId++;

        /// <summary>
        /// Zadania oczekujące na zatrzymanie bieżącego taska tego samego consist'u.
        /// Gdy gracz klika nowy cel mid-move, stary task hamuje, nowy czeka w pending.
        /// Po Completed starego taska pending jest promowany do _tasks.
        /// </summary>
        readonly Dictionary<int, DepotMoveTask> _pendingNextTask = new();

        TrackGraph _graph;
        Transform _visualsContainer;
        Material _visualMaterial;

        /// <summary>Aktualnie aktywne zadania (read-only, do debug/UI).</summary>
        public IReadOnlyList<DepotMoveTask> ActiveTasks => _tasks;

        /// <summary>
        /// TD-031 service recovery: czy consist ma aktywny manewr (w toku lub pending).
        /// Używane przez watchdog recovery jobów serwisowych po load — job/slot w fazie EnRoute
        /// bez aktywnego taska = osierocony manewr (DepotMoveTask runtime-only, nie persystowany)
        /// → trzeba go wznowić.
        /// </summary>
        public bool HasTaskForConsist(int consistId)
        {
            foreach (var t in _tasks)
                if (t.consistId == consistId) return true;
            return _pendingNextTask.ContainsKey(consistId);
        }

        /// <summary>
        /// TD-031 service recovery: zwraca aktywny task danego consistu z głównej kolejki
        /// (do podpięcia <see cref="DepotMoveTask.onCompleted"/> po <see cref="RestoreActiveMove"/>).
        /// Null gdy brak. Pomija pending (callback ma sens dopiero gdy task realnie jedzie).
        /// </summary>
        public DepotMoveTask GetActiveTask(int consistId)
        {
            foreach (var t in _tasks)
                if (t.consistId == consistId) return t;
            return null;
        }

        /// <summary>
        /// TD-031 service recovery: czy istnieje odtworzony visual consistu (po load
        /// <see cref="RestoreParkedVisualsFromGraph"/> spawnuje go z occupancy grafu). Recovery
        /// czeka aż visual będzie gotowy — inaczej <see cref="RestoreActiveMove"/> nie wyznaczy
        /// poprawnego fromTrack z bieżącej pozycji.
        /// </summary>
        public bool HasConsistVisual(int consistId)
            => _consistVisuals.TryGetValue(consistId, out var go) && go != null;

        /// <summary>Shared material dla highlight'u zaznaczonego consist'u (żółty).</summary>
        public Material SelectedMaterial { get; private set; }

        // ── Unity lifecycle ──────────────────────────────────────────

        // Throttle dla "Graph is NULL" Warn — żeby nie spamować 50/s gdy graph rzeczywiscie zniknal.
        bool _graphNullWarned;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Log.Warn($"[DepotMovementSim] Duplicate instance on '{gameObject.name}' — destroying");
                Destroy(gameObject); return;
            }
            Instance = this;

            // Container dla wszystkich visualów consist'ów
            var containerGo = new GameObject("ConsistVisuals");
            containerGo.transform.SetParent(transform);
            _visualsContainer = containerGo.transform;

            // Shared materials (czerwony default + żółty selected)
            _visualMaterial = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(_visualMaterial, new Color(0.9f, 0.2f, 0.2f, 1f)); // czerwony

            SelectedMaterial = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(SelectedMaterial, new Color(1f, 0.9f, 0.1f, 1f)); // żółty
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        static void ApplyColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            else
                mat.color = color;
        }

        void Start()
        {
            EnsureGraph();
        }

        void FixedUpdate()
        {
            if (GameState.IsPaused) return;

            using var _profMarker = s_FixedUpdate.Auto();

            float deltaSec = Time.fixedDeltaTime * GameState.DepotTimeScale;

            EnsureGraph();
            if (_graph == null)
            {
                if (_tasks.Count > 0 && !_graphNullWarned)
                {
                    Log.Warn("[DepotMovementSim] Graph is NULL — tasks waiting indefinitely");
                    _graphNullWarned = true;
                }
                return;
            }
            _graphNullWarned = false;

            // TD-031 pass 1: przelicz dynamiczne capy ze snapshotu interwałów z początku ticku
            // (determinizm — wynik niezależny od kolejności przetwarzania tasków w pass 2).
            RecomputeAllDynamicStopCaps();

            // pass 2: iteruj po tasks od końca (żeby safely usuwać completed/failed)
            for (int i = _tasks.Count - 1; i >= 0; i--)
            {
                var task = _tasks[i];
                try
                {
                    ProcessTask(task, deltaSec);
                }
                catch (System.Exception ex)
                {
                    Log.Warn($"[DepotMovementSim] Exception in ProcessTask for consist#{task.consistId}: {ex.Message}\n{ex.StackTrace}");
                    task.state = DepotMoveState.Failed;
                    task.failureReason = "exception: " + ex.Message;
                }

                if (task.state == DepotMoveState.Completed || task.state == DepotMoveState.Failed)
                    _tasks.RemoveAt(i);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        void EnsureGraph()
        {
            if (_graph != null) return;
            _graph = DepotServices.Get<TrackGraph>();
        }

        void Fail(DepotMoveTask task, string reason)
        {
            task.state = DepotMoveState.Failed;
            task.failureReason = reason;
            Log.Warn($"[DepotMovementSim] Fail: consist#{task.consistId} (track#{task.fromTrackId}→#{task.toTrackId}) — {reason}");

            // MM-18: callback informujący producer'a że task się wywalił (np. service job
            // przejdzie z EnRoute do Failed → cancel z refund).
            if (task.onCompleted != null)
            {
                try
                {
                    task.onCompleted.Invoke(DepotMoveState.Failed);
                }
                catch (System.Exception ex)
                {
                    Log.Warn($"[DepotMovementSim] task.onCompleted (Failed) threw for consist#{task.consistId}: {ex.Message}");
                }
            }
        }
    }
}
