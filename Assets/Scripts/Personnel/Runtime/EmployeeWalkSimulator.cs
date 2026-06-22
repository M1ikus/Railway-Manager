using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-10: Runtime ruchu pracownikow w scenie Depot. Per-employee <see cref="EmployeeVisual"/>
    /// + kolejka tasków (walk destinations).
    ///
    /// Prędkość: 1.4 m/s normal, 2.5 m/s hurry (<see cref="PersonnelBalanceConstants"/>).
    /// Clock: <see cref="GameState.DepotTimeScale"/> (max x5).
    ///
    /// Instant-resolve (D26): gdy scena Depot inactive — task'y wykonuja sie natychmiast
    /// (teleport do destination + onArrive callback), bez spawnu wizualu.
    ///
    /// Pathfinding (M8-10 MVP): <b>straight-line walk</b>. PathGraph integration w polish M8-15
    /// (gdy gracz stworzy sciezki w Depot toolbar). Brak PathGraph = fallback "across grass".
    /// </summary>
    public class EmployeeWalkSimulator : MonoBehaviour
    {
        public static EmployeeWalkSimulator Instance { get; private set; }

        readonly Dictionary<int, EmployeeVisual> _visuals = new();
        readonly Queue<EmployeeWalkTask> _taskQueue = new();
        Transform _container;

        public int VisualCount => _visuals.Count;
        public int QueuedTasks => _taskQueue.Count;

        public static EmployeeWalkSimulator EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("EmployeeWalkSimulator");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<EmployeeWalkSimulator>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            var containerGo = new GameObject("EmployeeVisualsContainer");
            DontDestroyOnLoad(containerGo);
            _container = containerGo.transform;

            // TD-025: reset pathfinder cache przy bootstrap (jeśli scena się zmieniła)
            EmployeeWalkPathfinder.Reset();

            // TD-025: re-evaluate pathfinder przy zmianie sceny (np. wejście do Depot)
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
            if (Instance == this) Instance = null;
        }

        void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        {
            EmployeeWalkPathfinder.Reset();
        }

        void OnSceneUnloaded(UnityEngine.SceneManagement.Scene s)
        {
            EmployeeWalkPathfinder.Reset();
        }

        void FixedUpdate()
        {
            // Process pending tasks
            while (_taskQueue.Count > 0)
            {
                var task = _taskQueue.Dequeue();
                ExecuteTask(task);
            }

            // Tick visuals (walk progression)
            if (!IsDepotSceneActive())
            {
                // Instant-resolve: wszystkie walking visuals teleportuja do destination
                // (ale utrzymuja swoje _walking aby onArrive wywolal sie poprawnie)
                // W praktyce: w tym momencie _visuals powinny byc despawned przez Dispatcher
                return;
            }

            float dt = Time.fixedDeltaTime * Math.Max(0.001f, GameState.DepotTimeScale);
            foreach (var v in _visuals.Values)
            {
                if (v != null) v.Tick(dt);
            }

            ApplySeparation(); // TD-033 H: miękkie rozpychanie kapsuł (deterministyczne, po ruchu)
        }

        // ── TD-033 H: soft NPC separation ─────────────────────────────
        const float SepRadiusM = 0.8f;
        readonly List<int> _sepIds = new();
        Vector2[] _sepPos = new Vector2[8];
        Vector2[] _sepOut = new Vector2[8];

        /// <summary>
        /// Miękkie rozpychanie kapsuł gdy się nakładają (staranie się nie przenikać NPC). Dwuprzebiegowo
        /// (oblicz wszystkie displacement, potem zastosuj) + stabilna kolejność po employeeId = deterministyczne
        /// (MP/test-friendly). Soft: nie blokuje ruchu, tylko odpycha o połowę nakładania.
        /// </summary>
        void ApplySeparation()
        {
            _sepIds.Clear();
            foreach (var k in _visuals.Keys) _sepIds.Add(k);
            if (_sepIds.Count < 2) return;
            _sepIds.Sort(); // stabilna kolejność = determinizm
            int n = _sepIds.Count;
            if (_sepPos.Length < n) { _sepPos = new Vector2[n]; _sepOut = new Vector2[n]; }

            for (int i = 0; i < n; i++)
            {
                var v = _visuals[_sepIds[i]];
                Vector3 p = v != null ? v.transform.position : Vector3.zero;
                _sepPos[i] = new Vector2(p.x, p.z);
            }
            DepotSystem.Nav.NavSeparation.ComputeDisplacements(_sepPos, n, SepRadiusM, _sepOut);
            for (int i = 0; i < n; i++)
            {
                var v = _visuals[_sepIds[i]];
                if (v != null) v.transform.position += new Vector3(_sepOut[i].x, 0f, _sepOut[i].y);
            }
        }

        // ═══ Public API ═══

        /// <summary>Tworzy EmployeeVisual dla pracownika na pozycji. Noop gdy juz istnieje.</summary>
        public EmployeeVisual SpawnEmployee(int employeeId, Vector3 position)
        {
            if (_visuals.ContainsKey(employeeId)) return _visuals[employeeId];
            var emp = PersonnelService.GetById(employeeId);
            if (emp == null) return null;
            if (!RoleDefinitions.SpawnsAsAgentInDepot(emp.role)) return null; // D19: kasjer no-spawn

            if (!IsDepotSceneActive())
            {
                // Instant-resolve: nie spawnuj wizualu, tylko zaloguj
                return null;
            }

            var visual = EmployeeVisual.Create(employeeId, position, _container);
            if (visual != null)
            {
                _visuals[employeeId] = visual;
                // Minimap bridge (Q2 krok 6): rejestruj w Core registry żeby Depot.minimap mógł pull
                MinimapAgentRegistry.Register(employeeId, MinimapAgentRegistry.AgentType.Employee, visual.transform);
            }
            return visual;
        }

        public void DespawnEmployee(int employeeId)
        {
            if (_visuals.TryGetValue(employeeId, out var v))
            {
                if (v != null) Destroy(v.gameObject);
                _visuals.Remove(employeeId);
                MinimapAgentRegistry.Unregister(employeeId, MinimapAgentRegistry.AgentType.Employee);
            }
        }

        public void DespawnAll()
        {
            foreach (var v in _visuals.Values)
                if (v != null) Destroy(v.gameObject);
            _visuals.Clear();
            MinimapAgentRegistry.ClearType(MinimapAgentRegistry.AgentType.Employee);
        }

        public EmployeeVisual GetVisual(int employeeId) =>
            _visuals.TryGetValue(employeeId, out var v) ? v : null;

        /// <summary>Enqueue task. Bedzie wykonany w nastepnym FixedUpdate.</summary>
        public void EnqueueTask(EmployeeWalkTask task)
        {
            if (task == null) return;
            _taskQueue.Enqueue(task);
        }

        // ═══ Internal ═══

        void ExecuteTask(EmployeeWalkTask task)
        {
            if (!IsDepotSceneActive())
            {
                // Instant-resolve: natychmiastowy onArrive bez walkthrough
                InvokeArriveAndChain(task);
                return;
            }

            var visual = GetVisual(task.employeeId);
            if (visual == null)
            {
                // Employee not spawned — spawn at destination as starting point, then nothing to walk
                SpawnEmployee(task.employeeId, task.destination);
                InvokeArriveAndChain(task);
                return;
            }

            // TD-025: build polyline via PathGraph (fallback straight-line)
            Vector3 start = visual.transform.position;
            var polyline = EmployeeWalkPathfinder.BuildPolyline(task.employeeId, start, task.destination);

            // onArrive chain — gdy task ma nextTask, enqueue go automatycznie po dotarciu
            System.Action arriveCallback = () => InvokeArriveAndChain(task);

            visual.StartWalk(polyline, arriveCallback, task.hurry);
        }

        /// <summary>TD-025: invoke onArrive + enqueue nextTask (chain).</summary>
        void InvokeArriveAndChain(EmployeeWalkTask task)
        {
            task.onArrive?.Invoke();
            if (task.nextTask != null)
                _taskQueue.Enqueue(task.nextTask);
        }

        static bool IsDepotSceneActive()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByName("Depot");
            return scene.IsValid() && scene.isLoaded;
        }

        // ═══ Debug ═══

        [ContextMenu("Debug: Despawn all visuals")]
        public void DebugDespawnAll() => DespawnAll();

        [ContextMenu("Debug: Report visuals count")]
        public void DebugReport()
        {
            Log.Info($"[EmployeeWalkSimulator] Visuals: {_visuals.Count}, Queued tasks: {_taskQueue.Count}, " +
                     $"Depot active: {IsDepotSceneActive()}");
        }
    }
}
