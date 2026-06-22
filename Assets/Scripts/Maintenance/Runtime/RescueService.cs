using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Maintenance
{
    /// <summary>Faza aktywnego rescue.</summary>
    public enum RescuePhase
    {
        /// <summary>Rescue loko jedzie z depotu do miejsca awarii.</summary>
        Inbound,
        /// <summary>Rescue loko holuje broken pociąg z powrotem do depotu.</summary>
        Returning,
    }

    /// <summary>Stan aktywnej akcji rescue.</summary>
    public struct OngoingRescue
    {
        public int brokenTrainRunId;
        public int rescueLocoId;
        public List<int> brokenVehicleIds;   // pojazdy w broken consist (zostaną InRepair po returning)
        public long startedGameTime;
        public long inboundFinishGameTime;   // moment gry kiedy rescue dojeżdża do awarii
        public long returnFinishGameTime;    // moment gry kiedy rescue dociera z powrotem do depotu
        public float pathLengthM;            // długość jednostronna (depot → awaria)
        public RescuePhase phase;
    }

    /// <summary>
    /// M7-3b/c: Serwis dispatchu rescue lokomotywy gdy self-repair fail.
    ///
    /// Flow:
    /// 1. Pociąg w AwaitingRescue → UI daje graczowi listę wolnych lok (FindCandidates).
    /// 2. Player wybiera (lub auto-dispatch po 15 min game time).
    /// 3. M7-3c: compute A* path depot → broken train → ETA fazy Inbound (rescue loko jedzie).
    /// 4. Po ETA Inbound: broken train despawn, rescue holuje virtual consist.
    /// 5. Po ETA Returning (druga połowa): rescue loko + broken vehicles → InRepair,
    ///    TrainRun cancelled, reputation hit.
    ///
    /// MVP M7-3c: brak wizualizacji ruchu rescue loko na mapie — tylko timer ETA w UI.
    /// Pełna symulacja wizualna (SimulatedTrain dla rescue) to post-EA polish.
    ///
    /// Żyje w Timetable asmdef (widzi PathfindingGraph z TrainRunSimulator).
    /// </summary>
    public class RescueService : MonoBehaviour
    {
        public static RescueService Instance { get; private set; }

        /// <summary>Timeout w sec game time po którym auto-dispatch uruchamia się.</summary>
        public const int AutoDispatchTimeoutSec = 900; // 15 min

        /// <summary>Prędkość rescue lokomotywy [km/h] do kalkulacji ETA.</summary>
        public const float RescueSpeedKmh = 80f;

        /// <summary>Overhead coupling/uncoupling [sec game time] — dodany do każdej fazy.</summary>
        public const int CouplingOverheadSec = 60;

        readonly List<OngoingRescue> _ongoing = new();

        public IReadOnlyList<OngoingRescue> Ongoing => _ongoing;

        // ── TD-037: save/restore misji w toku ─────────────────────────
        // OngoingRescue jest czysto timerowe (fazy + absolutne game-time finish'e) → restore =
        // odtworzenie listy; Update podejmuje sam. Stany pojazdów (loco/broken InRepair) są w fleet.
        // Pending static — serwis może nie istnieć przy Deserialize (moduł "maintenance" wcześnie
        // w ModuleOrder, RescueService wstaje po OnSimulatorBootstrapped).

        static List<OngoingRescue> _pendingRestore;

        /// <summary>TD-037: odkłada misje do wznowienia (konsumpcja w Update gdy serwis żyje).</summary>
        public static void SetPendingRestore(List<OngoingRescue> rescues)
        {
            _pendingRestore = (rescues != null && rescues.Count > 0) ? rescues : null;
        }

        /// <summary>Liczba misji czekających na wznowienie (diagnostyka + testy).</summary>
        public static int PendingRestoreCount => _pendingRestore?.Count ?? 0;

        /// <summary>TD-037: snapshot misji w toku (do save). Sort po brokenTrainRunId — determinizm.</summary>
        public List<OngoingRescue> BuildSnapshot()
        {
            var copy = new List<OngoingRescue>(_ongoing);
            copy.Sort((a, b) => a.brokenTrainRunId.CompareTo(b.brokenTrainRunId));
            return copy;
        }

        void ConsumePendingRestoreIfAny()
        {
            if (_pendingRestore == null) return;
            var pending = _pendingRestore;
            _pendingRestore = null;
            _ongoing.Clear();
            _ongoing.AddRange(pending);
            Log.Info($"[Rescue] TD-037: wznowiono {_ongoing.Count} misji rescue (fazy/timery z save)");
        }

        public static RescueService EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("RescueService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<RescueService>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            ConsumePendingRestoreIfAny(); // TD-037

            if (_ongoing.Count == 0) return;

            long now = CurrentGameTime();
            for (int i = _ongoing.Count - 1; i >= 0; i--)
            {
                var r = _ongoing[i];

                if (r.phase == RescuePhase.Inbound && now >= r.inboundFinishGameTime)
                {
                    // Przejście Inbound → Returning: broken train despawn, holowanie w toku.
                    OnInboundComplete(ref r);
                    _ongoing[i] = r;
                }
                else if (r.phase == RescuePhase.Returning && now >= r.returnFinishGameTime)
                {
                    // Returning ukończony: wszystkie vehicles → InRepair, remove ongoing.
                    OnReturnComplete(r);
                    _ongoing.RemoveAt(i);
                }
            }
        }

        /// <summary>Czy dla danego TrainRun'a jest aktywne rescue (Inbound/Returning).</summary>
        public bool HasOngoing(int trainRunId)
        {
            foreach (var r in _ongoing)
                if (r.brokenTrainRunId == trainRunId) return true;
            return false;
        }

        public bool TryGetOngoing(int trainRunId, out OngoingRescue result)
        {
            foreach (var r in _ongoing)
                if (r.brokenTrainRunId == trainRunId)
                {
                    result = r;
                    return true;
                }
            result = default;
            return false;
        }

        /// <summary>
        /// Zwraca listę wolnych lokomotyw mogących wykonać rescue danego pociągu.
        /// Kryteria: ElectricLocomotive/DieselLocomotive, status wolny (StoppedInDepot/StoppedOnMap),
        /// trakcja kompatybilna (diesel może wszędzie, electric tylko na zelektryfikowanej).
        /// </summary>
        public static List<FleetVehicleData> FindCandidates(bool stuckOnElectrifiedLine)
        {
            var result = new List<FleetVehicleData>();
            foreach (var v in FleetService.OwnedVehicles)
            {
                if (v.type != FleetVehicleType.ElectricLocomotive
                 && v.type != FleetVehicleType.DieselLocomotive) continue;

                bool available = v.status == FleetVehicleStatus.StoppedInDepot
                              || v.status == FleetVehicleStatus.StoppedOnMap;
                if (!available) continue;

                if (stuckOnElectrifiedLine)
                {
                    // Obie trakcje OK (electric + diesel mogą jechać po sieci)
                }
                else
                {
                    // Tylko diesel może wjechać na niezelektryfikowany
                    if (v.type != FleetVehicleType.DieselLocomotive) continue;
                }

                result.Add(v);
            }
            return result;
        }

        /// <summary>
        /// M7-3c: Inicjuje rescue z pathfindingiem. Oblicza A* od depot home station
        /// do pozycji broken train, wylicza ETA, rejestruje OngoingRescue.
        ///
        /// Zwraca true gdy sukces (path found, rescue zaplanowany). Gdy graph niedostępny
        /// lub brak ścieżki — fallback na instant-despawn (jak M7-3b MVP).
        /// </summary>
        public bool InitiateRescue(SimulatedTrain brokenTrain, int rescueLocoId)
        {
            if (brokenTrain == null) return false;
            var rescueV = FleetService.GetOwnedById(rescueLocoId);
            if (rescueV == null) { Log.Warn($"[Rescue] Vehicle#{rescueLocoId} nieznane"); return false; }

            // Broken vehicles snapshot (z trainRun'a)
            var brokenVehicleIds = new List<int>();
            if (brokenTrain.trainRun?.runningVehicleIds != null)
                brokenVehicleIds.AddRange(brokenTrain.trainRun.runningVehicleIds);

            // Źródło i cel A*
            var sim = TrainRunSimulator.Instance;
            var graph = sim != null ? sim.Graph : null;
            int homeNodeId = GameState.HomeDepotStationId;

            float pathLengthM = -1f;
            if (graph != null && homeNodeId >= 0 && homeNodeId < graph.NodeCount)
            {
                Vector2 brokenPos = GetBrokenTrainPosition(brokenTrain);
                Vector2 homePos = graph.GetNode(homeNodeId).position;

                // Pathfinding: home station → najbliższy node broken train
                var res = RailwayPathfinder.FindPathByPosition(
                    graph, homePos, brokenPos, snapRadiusM: 500f);

                if (res.success)
                {
                    pathLengthM = res.totalLengthM;
                    Log.Info($"[Rescue] Path found: depot→broken {pathLengthM / 1000f:F1} km ({res.nodeIds.Count} nodes)");
                }
                else
                {
                    Log.Warn($"[Rescue] A* failed (explored {res.exploredNodes}) — fallback instant");
                }
            }
            else
            {
                Log.Warn($"[Rescue] Graph/home-station unavailable (graph={graph != null}, home={homeNodeId}) — fallback instant");
            }

            // Fallback: instant despawn (jak stary placeholder) jeśli pathfinding niedostępny
            if (pathLengthM < 0f)
            {
                InstantDespawn(brokenTrain, rescueLocoId, brokenVehicleIds);
                return false;
            }

            // ETA kalkulacja
            float speedMs = RescueSpeedKmh * 1000f / 3600f;
            long oneWaySec = (long)(pathLengthM / speedMs) + CouplingOverheadSec;

            long now = CurrentGameTime();
            var rescue = new OngoingRescue
            {
                brokenTrainRunId = brokenTrain.trainRun.id,
                rescueLocoId = rescueLocoId,
                brokenVehicleIds = brokenVehicleIds,
                startedGameTime = now,
                inboundFinishGameTime = now + oneWaySec,
                returnFinishGameTime = now + 2 * oneWaySec,
                pathLengthM = pathLengthM,
                phase = RescuePhase.Inbound,
            };
            _ongoing.Add(rescue);

            // Rescue loko — zablokowana (nie może być używana do innych tasków)
            rescueV.status = FleetVehicleStatus.InRepair;
            rescueV.currentTask = $"Jazda do awarii run#{brokenTrain.trainRun.id}";
            FleetService.NotifyOwnedChanged();

            Log.Info($"[Rescue] Dispatched loko#{rescueLocoId} → run#{brokenTrain.trainRun.id}: " +
                     $"{pathLengthM / 1000f:F1}km one-way, ETA inbound {oneWaySec / 60}min, " +
                     $"total {2 * oneWaySec / 60}min");
            return true;
        }

        /// <summary>
        /// Faza Inbound ukończona — rescue dojechał do miejsca awarii.
        /// Broken train despawn (TrainRun cancelled, SimulatedTrain → Completed).
        /// Rescue loko teraz "holuje" — dalej zablokowana, phase=Returning.
        /// </summary>
        void OnInboundComplete(ref OngoingRescue r)
        {
            Log.Info($"[Rescue] Inbound complete — run#{r.brokenTrainRunId} despawning, rescue returning");

            // Znajdź broken train i ustaw jako Completed
            var sim = TrainRunSimulator.Instance;
            if (sim != null && sim.ActiveTrains.TryGetValue(r.brokenTrainRunId, out var st))
            {
                st.trainRun.isCancelled = true;
                st.state = TrainState.Completed; // TrainRunSimulator despawnuje w następnym ticku
            }

            // Update rescue loko task
            var rescueV = FleetService.GetOwnedById(r.rescueLocoId);
            if (rescueV != null)
            {
                rescueV.currentTask = $"Holowanie do depotu (run#{r.brokenTrainRunId})";
                FleetService.NotifyOwnedChanged();
            }

            r.phase = RescuePhase.Returning;
        }

        /// <summary>
        /// Faza Returning ukończona — rescue doholował do depotu.
        /// Wszystkie broken vehicles + rescue loko → InRepair.
        /// Reputation hit (placeholder log), TrainRun już cancelled.
        /// </summary>
        void OnReturnComplete(OngoingRescue r)
        {
            Log.Info($"[Rescue] Returning complete — run#{r.brokenTrainRunId}, wszystkie vehicles InRepair");

            foreach (int vid in r.brokenVehicleIds)
            {
                var v = FleetService.GetOwnedById(vid);
                if (v != null)
                {
                    v.status = FleetVehicleStatus.InRepair;
                    v.currentTask = $"Holowana do depotu po awarii (run#{r.brokenTrainRunId})";
                }
            }

            // Rescue loko zostaje InRepair (sama też potrzebuje inspekcji po holowaniu).
            // Można później zmienić na StoppedInDepot bez inspection — ale dla konsystencji: InRepair.
            var rescueV = FleetService.GetOwnedById(r.rescueLocoId);
            if (rescueV != null)
            {
                rescueV.status = FleetVehicleStatus.InRepair;
                rescueV.currentTask = null;
            }

            // Reputation hit (extra za rescue — traktujemy jako CanceledRun)
            var rep = RailwayManager.Timetable.Economy.ReputationManager.Instance;
            if (rep != null)
            {
                rep.ApplyEvent(
                    RailwayManager.Timetable.Economy.ReputationEventType.CanceledRun,
                    null, null, $"Rescue holowanie run#{r.brokenTrainRunId}");
            }

            FleetService.NotifyOwnedChanged();
        }

        /// <summary>
        /// Fallback gdy pathfinding niedostępny — natychmiast despawn broken + flag vehicles InRepair.
        /// Używane tylko gdy graph/home-station unavailable (błąd konfiguracji).
        /// </summary>
        void InstantDespawn(SimulatedTrain brokenTrain, int rescueLocoId, List<int> brokenVehicleIds)
        {
            Log.Warn($"[Rescue] Fallback instant-despawn dla run#{brokenTrain.trainRun.id}");

            var rescueV = FleetService.GetOwnedById(rescueLocoId);
            if (rescueV != null)
            {
                rescueV.status = FleetVehicleStatus.InRepair;
                rescueV.currentTask = $"Holowanie pociągu run#{brokenTrain.trainRun.id}";
            }
            foreach (int vid in brokenVehicleIds)
            {
                var v = FleetService.GetOwnedById(vid);
                if (v != null)
                {
                    v.status = FleetVehicleStatus.InRepair;
                    v.currentTask = $"Holowana do depotu po awarii (run#{brokenTrain.trainRun.id})";
                }
            }

            brokenTrain.trainRun.isCancelled = true;
            brokenTrain.state = TrainState.Completed;
            FleetService.NotifyOwnedChanged();
        }

        static Vector2 GetBrokenTrainPosition(SimulatedTrain st)
        {
            if (st.visualTransform != null)
            {
                var p = st.visualTransform.position;
                return new Vector2(p.x, p.z);
            }
            return Vector2.zero;
        }

        static long CurrentGameTime()
            => (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;

        // ── LEGACY compatibility ────────────────────────────

        /// <summary>
        /// LEGACY (M7-3b): instant-flag rescue loko. Zachowane dla kompatybilności —
        /// nowe call-sites używają <see cref="InitiateRescue"/>.
        /// </summary>
        public static void ExecuteRescue(int brokenTrainRunId, int rescueLocoId)
        {
            var rescueV = FleetService.GetOwnedById(rescueLocoId);
            if (rescueV != null)
            {
                rescueV.status = FleetVehicleStatus.InRepair;
                rescueV.currentTask = $"Holowanie pociągu run#{brokenTrainRunId}";
            }
        }
    }
}
