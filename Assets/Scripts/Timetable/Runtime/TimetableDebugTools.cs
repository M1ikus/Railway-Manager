using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using formap;
using MapSystem;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Smoke testy modułu Timetable w edytorze Unity.
    /// Podłącz do dowolnego GameObject w MapScene, odpal Play, prawy klik → ContextMenu.
    /// Wywołuj akcje w kolejności 1→2→3→4.
    /// </summary>
    public class TimetableDebugTools : MonoBehaviour
    {
        [Header("References (auto-find jeśli null)")]
        public MapLoader mapLoader;
        public RailwayGraph railwayGraph;

        [Header("Pathfinding test")]
        public string startStationQuery = "Warszawa Centralna";
        public string endStationQuery = "Kraków Główny";
        public float stationSnapRadiusM = 300f;

        [Header("Wyniki (read-only)")]
        [SerializeField] private int cachedNodeCount;
        [SerializeField] private int cachedEdgeCount;
        [SerializeField] private int cachedStationCount;
        [SerializeField] private int cachedPlatformCount;
        [SerializeField] private int cachedComponents;

        private PathfindingGraph _pathGraph;
        private List<RailwayStation> _stations;
        private List<StationPlatform> _platforms;
        private List<AdminRegion> _adminRegions;
        private VoivodeshipResolver _voivodeshipResolver;
        private List<CityPlace> _places;
        private HashSet<string> _agglomerations;

        // ─────────────────────────────────────────────
        //  Core actions (wywołuj w kolejności 1→2→3→4)
        // ─────────────────────────────────────────────

        [ContextMenu("1. Build Pathfinding Graph")]
        public void BuildPathfindingGraph()
        {
            ResolveRefs();
            if (mapLoader == null) { Log.Error("[TimetableDebugTools] No MapLoader"); return; }
            if (railwayGraph == null) { Log.Error("[TimetableDebugTools] No RailwayGraph"); return; }

            // Force LOD=0 + reload all tiles (eliminacja mix LOD5/LOD0)
            mapLoader.SetLODLevel(0);
            int tilesLoaded = mapLoader.EnsureAllTilesLoadedSync(forceReloadAll: true);
            Log.Info($"[TimetableDebugTools] Tiles loaded: {tilesLoaded}/{tilesLoaded}");

            // Agregacja railway features i build RailwayGraph
            var railwayFeatures = mapLoader.GetAllFeaturesAcrossTiles(BinaryFormat.LayerType.Railways);
            Log.Info($"[TimetableDebugTools] {railwayFeatures.Count} railway features");

            if (railwayFeatures.Count == 0) { Log.Error("[TimetableDebugTools] Zero railways!"); return; }
            railwayGraph.BuildGraph(railwayFeatures);

            // Union-Find based pathfinding graph (transitywne mergowanie pozycji)
            float t0 = Time.realtimeSinceStartup;
            _pathGraph = new PathfindingGraph();
            // junctionOnlyMerge=true (default): merguje TYLKO junction↔junction.
            // cellSize=3m: ścisła tolerancja — zapobiega fałszywym rozjazdom między równoległymi torami (~4.5m apart).
            _pathGraph.BuildFromFeaturesUnionFind(railwayFeatures, cellSizeM: 3f);
            float elapsed = Time.realtimeSinceStartup - t0;

            cachedNodeCount = _pathGraph.NodeCount;
            cachedEdgeCount = _pathGraph.EdgeCount;

            // Component analysis
            cachedComponents = CountComponents(_pathGraph, out int biggest);

            Log.Warn($"[TimetableDebugTools] Graph: {_pathGraph.NodeCount} nodes, {_pathGraph.EdgeCount} edges, "
                     + $"built in {elapsed:F2}s");
            Log.Warn($"[TimetableDebugTools] Components: {cachedComponents}, "
                     + $"biggest: {biggest} nodes ({biggest * 100f / _pathGraph.NodeCount:F1}%)");
        }

        [ContextMenu("2. Load Admin Regions + Places")]
        public void LoadAdminAndPlaces()
        {
            ResolveRefs();
            _adminRegions = AdminBoundaryLoader.LoadFrom(mapLoader);
            _voivodeshipResolver = new VoivodeshipResolver(_adminRegions);
            _voivodeshipResolver.LogDiagnostics();
            _places = PlaceLoader.LoadFrom(mapLoader, _voivodeshipResolver);
        }

        [ContextMenu("3. Load Stations + Platforms")]
        public void LoadStationsAndPlatforms()
        {
            ResolveRefs();
            if (_pathGraph == null) { Log.Error("[TimetableDebugTools] Run step 1 first"); return; }

            _stations = StationLoader.LoadFrom(mapLoader, _pathGraph, stationSnapRadiusM, _voivodeshipResolver);
            _platforms = PlatformLoader.LoadFrom(mapLoader, _pathGraph);

            cachedStationCount = _stations.Count;
            cachedPlatformCount = _platforms.Count;

            int withNode = _stations.Count(s => s.pathNodeId >= 0);
            Log.Info($"[TimetableDebugTools] {withNode}/{_stations.Count} stacji znalazło węzeł torów");

            if (_places != null)
            {
                _agglomerations = AgglomerationDetector.DetectAgglomerations(_stations, _places);
                if (_agglomerations.Count > 0)
                    Log.Info($"[TimetableDebugTools] Aglomeracje: {string.Join(", ", _agglomerations.Take(10))}");
            }
        }

        [ContextMenu("4. Test Pathfinding")]
        public void TestPathfinding()
        {
            if (_pathGraph == null || _stations == null)
            {
                Log.Error("[TimetableDebugTools] Uruchom kroki 1 i 3 najpierw");
                return;
            }

            var start = FindStation(startStationQuery);
            var end = FindStation(endStationQuery);
            if (start == null) { Log.Error($"Nie znaleziono: {startStationQuery}"); return; }
            if (end == null) { Log.Error($"Nie znaleziono: {endStationQuery}"); return; }
            if (start.pathNodeId < 0 || end.pathNodeId < 0)
            {
                Log.Error($"Brak węzła torów ({start.name}: {start.pathNodeId}, {end.name}: {end.pathNodeId})");
                return;
            }

            Log.Info($"[TimetableDebugTools] {start.name} → {end.name} "
                     + $"(linia prosta: {Vector2.Distance(start.position, end.position) / 1000f:F1} km)");

            float t0 = Time.realtimeSinceStartup;
            var result = RailwayPathfinder.FindPath(_pathGraph, start.pathNodeId, end.pathNodeId);
            float elapsed = Time.realtimeSinceStartup - t0;

            if (!result.success)
            {
                Log.Warn($"[TimetableDebugTools] Brak ścieżki (explored {result.exploredNodes}, {elapsed:F2}s)");
                return;
            }

            Log.Info($"[TimetableDebugTools] ✓ Trasa: {result.totalLengthM / 1000f:F1} km, "
                     + $"{result.nodeIds.Count} węzłów, {elapsed:F2}s, explored {result.exploredNodes}");

            if (result.edgeIds.Count > 0)
            {
                int minSpeed = int.MaxValue, maxSpeed = 0;
                float avgSpeed = 0f;
                foreach (int eid in result.edgeIds)
                {
                    int s = _pathGraph.GetEdge(eid).maxSpeedKmh;
                    if (s < minSpeed) minSpeed = s;
                    if (s > maxSpeed) maxSpeed = s;
                    avgSpeed += s;
                }
                avgSpeed /= result.edgeIds.Count;
                Log.Info($"[TimetableDebugTools]   Vmax: min {minSpeed}, max {maxSpeed}, śr {avgSpeed:F0} km/h");
            }
        }

        // ─────────────────────────────────────────────
        //  TD-019: pathfinding diagnostic — edge metadata dump
        // ─────────────────────────────────────────────

        /// <summary>
        /// TD-019: rozszerzony test pathfinding'u z dump'em metadata per edge — pomaga zidentyfikować
        /// dlaczego A* wybiera "boczną" trasę. Klasyfikuje edges po `metadata["usage"]` / `["service"]`,
        /// liczy junction passes, log każdego segmentu w kolejności.
        ///
        /// Symptom user'a (2026-05-06): A→B w podglądzie rozkładu zahacza o stacje na boki które nie powinny.
        /// Hipoteza: edge weights to czysta length (m) — boczne tory geometrycznie krótsze niż main line
        /// preferowane. Fix wymaga klasyfikacji edges (usage=main/branch/yard/siding) i mnożnika weight'u.
        /// </summary>
        [ContextMenu("TD-019: Debug path with edge metadata")]
        public void DebugPathWithEdgeMetadata()
        {
            if (_pathGraph == null || _stations == null)
            {
                Log.Error("[TimetableDebugTools] Uruchom kroki 1 i 3 najpierw");
                return;
            }

            var start = FindStation(startStationQuery);
            var end = FindStation(endStationQuery);
            if (start == null || end == null) { Log.Error("Stacja nie znaleziona"); return; }
            if (start.pathNodeId < 0 || end.pathNodeId < 0) { Log.Error("Brak pathNodeId"); return; }

            var result = RailwayPathfinder.FindPath(_pathGraph, start.pathNodeId, end.pathNodeId);
            if (!result.success) { Log.Warn("Brak ścieżki"); return; }

            Log.Info($"[TD-019] === PATH DEBUG: {start.name} → {end.name} ===");
            Log.Info($"[TD-019] Total: {result.totalLengthM / 1000f:F2} km, "
                     + $"{result.edgeIds.Count} edges, {result.nodeIds.Count} nodes, "
                     + $"explored {result.exploredNodes}, A* {result.timeAStarMs:F0}ms");
            Log.Info($"[TD-019] Linia prosta start→end: "
                     + $"{Vector2.Distance(start.position, end.position) / 1000f:F2} km "
                     + $"(detour ratio: {result.totalLengthM / Vector2.Distance(start.position, end.position):F2}×)");

            // Klasyfikacja edges po metadata
            int mainCount = 0, branchCount = 0, sidingCount = 0, yardCount = 0;
            int unknownCount = 0, junctionTransitCount = 0;
            float mainLen = 0f, branchLen = 0f, sidingLen = 0f, yardLen = 0f, unknownLen = 0f;

            for (int i = 0; i < result.edgeIds.Count; i++)
            {
                int eid = result.edgeIds[i];
                var edge = _pathGraph.GetEdge(eid);
                string usage = edge.metadata != null && edge.metadata.TryGetValue("usage", out var u) ? u : null;
                string service = edge.metadata != null && edge.metadata.TryGetValue("service", out var s) ? s : null;

                bool isJunctionFrom = _pathGraph.JunctionNodeIds.Contains(edge.fromNodeId);
                bool isJunctionTo = _pathGraph.JunctionNodeIds.Contains(edge.toNodeId);
                if (isJunctionFrom || isJunctionTo) junctionTransitCount++;

                string klasa;
                if (service == "siding" || service == "spur") { klasa = "SIDING"; sidingCount++; sidingLen += edge.lengthM; }
                else if (service == "yard") { klasa = "YARD"; yardCount++; yardLen += edge.lengthM; }
                else if (usage == "main" || usage == "branch")
                {
                    if (usage == "main") { klasa = "MAIN"; mainCount++; mainLen += edge.lengthM; }
                    else { klasa = "BRANCH"; branchCount++; branchLen += edge.lengthM; }
                }
                else { klasa = "UNKNOWN"; unknownCount++; unknownLen += edge.lengthM; }

                // Log podejrzanych edges + co 10. dla baseline (żeby nie zalać konsoli)
                bool suspicious = klasa == "SIDING" || klasa == "YARD" || klasa == "BRANCH";
                if (suspicious || i % 10 == 0)
                {
                    Log.Info($"[TD-019]   edge[{i}] {klasa,-7} len={edge.lengthM,7:F1}m vmax={edge.maxSpeedKmh,3} "
                             + $"usage={usage ?? "-"} service={service ?? "-"} "
                             + $"junctionTransit={(isJunctionFrom || isJunctionTo ? "YES" : "no")}");
                }
            }

            Log.Info($"[TD-019] === SUMMARY classification ===");
            Log.Info($"[TD-019] MAIN:    {mainCount,4} edges, {mainLen / 1000f,7:F1} km ({mainLen / result.totalLengthM * 100,5:F1}%)");
            Log.Info($"[TD-019] BRANCH:  {branchCount,4} edges, {branchLen / 1000f,7:F1} km ({branchLen / result.totalLengthM * 100,5:F1}%)");
            Log.Info($"[TD-019] SIDING:  {sidingCount,4} edges, {sidingLen / 1000f,7:F1} km ({sidingLen / result.totalLengthM * 100,5:F1}%) ⚠️ jeśli >0%");
            Log.Info($"[TD-019] YARD:    {yardCount,4} edges, {yardLen / 1000f,7:F1} km ({yardLen / result.totalLengthM * 100,5:F1}%) ⚠️ jeśli >0%");
            Log.Info($"[TD-019] UNKNOWN: {unknownCount,4} edges, {unknownLen / 1000f,7:F1} km ({unknownLen / result.totalLengthM * 100,5:F1}%) — brak metadata");
            Log.Info($"[TD-019] Junction transitions: {junctionTransitCount} edges traversal junctions");

            if (sidingCount > 0 || yardCount > 0)
                Log.Warn($"[TD-019] ⚠️ Trasa zawiera SIDING/YARD edges — to potencjalna przyczyna objazdów. "
                         + "Fix: penalty multiplier per edge type w RailwayPathfinder.FindPath weight function.");
            if (unknownCount > result.edgeIds.Count / 2)
                Log.Warn($"[TD-019] ⚠️ Większość edges (>50%) bez metadata `usage`/`service` — OSM data niedoczytane "
                         + "lub graph reader pomija te tagi. Sprawdź `RailwayGraph` builder.");
        }

        // ─────────────────────────────────────────────
        //  Optional diagnostic actions
        // ─────────────────────────────────────────────

        [ContextMenu("5. Test Voivodeship (first 15 stations)")]
        public void TestVoivodeship()
        {
            if (_stations == null || _voivodeshipResolver == null || !_voivodeshipResolver.IsReady)
            {
                Log.Warn("[TimetableDebugTools] Uruchom kroki 1-3 (z krokiem 2 dla voivodeship)");
                return;
            }
            foreach (var s in _stations.Take(15))
                Log.Info($"  {s.name,-40} voi={s.voivodeship ?? "?"}  city={s.cityName ?? "?"}");
            int total = _stations.Count(s => s.voivodeship != null);
            Log.Info($"[TimetableDebugTools] {total}/{_stations.Count} stacji z województwem");
        }

        [ContextMenu("8. Test Circulations (M5 smoke)")]
        public void TestCirculations()
        {
            // Test podstawowy: tworzenie, listowanie, konflikt kalendarzy, usuwanie
            Log.Info("=== M5 Circulations smoke test ===");

            int countBefore = CirculationService.Circulations.Count;
            Log.Info($"  Start count: {countBefore}");

            // Utwórz trzy obiegi testowe
            var c1 = CirculationService.AddCirculation(new Circulation
            {
                name = "Test obieg Olsztyn pn-pt",
                calendar = new DayMask { bits = DayMask.Weekdays },
                steps = new List<CirculationStep>
                {
                    new CirculationStep(1, StepKind.Commercial),
                    new CirculationStep(2, StepKind.Commercial)
                },
                assignedVehicleIds = new List<int> { 100 },
                status = CirculationStatus.Active
            });

            var c2 = CirculationService.AddCirculation(new Circulation
            {
                name = "Test obieg Olsztyn weekend",
                calendar = new DayMask { bits = DayMask.Weekend },
                steps = new List<CirculationStep>
                {
                    new CirculationStep(3, StepKind.Commercial)
                },
                assignedVehicleIds = new List<int> { 100 },
                status = CirculationStatus.Active
            });

            var c3 = CirculationService.AddCirculation(new Circulation
            {
                name = "Test obieg Draft",
                calendar = DayMask.Daily(),
                steps = new List<CirculationStep> { new CirculationStep(4) },
                status = CirculationStatus.Draft
            });

            Log.Info($"  Utworzono 3 obiegi: #{c1.id}, #{c2.id}, #{c3.id}");
            Log.Info($"  Trywialny (StepCount=1): c1={c1.IsTrivial}, c2={c2.IsTrivial}, c3={c3.IsTrivial}");

            // Sprawdź konflikty kalendarzy — pojazd 100 w c1 (Weekdays) i c2 (Weekend)
            // powinien NIE kolidować (intersection = 0)
            var conflicts1 = CirculationService.CheckVehicleAssignmentConflicts(100, c1);
            Log.Info($"  Konflikty c1 dla pojazdu 100: {conflicts1.Count} (oczekiwane 0, bo c2 ma Weekend)");

            // Utwórz obieg który BĘDZIE kolidował (Daily nakłada się z Weekdays)
            var c4 = new Circulation
            {
                name = "Test obieg kolidujący",
                calendar = DayMask.Daily(),
                assignedVehicleIds = new List<int> { 100 },
                status = CirculationStatus.Active
            };
            var conflicts4 = CirculationService.CheckVehicleAssignmentConflicts(100, c4);
            Log.Info($"  Konflikty c4 (Daily) dla pojazdu 100: {conflicts4.Count} (oczekiwane 2, bo c1 i c2 pokrywają cały tydzień)");

            // Query helpers
            var forVehicle = CirculationService.GetCirculationsForVehicle(100);
            Log.Info($"  GetCirculationsForVehicle(100): {forVehicle.Count} (oczekiwane 2, Draft pomijany)");

            var usingTt1 = CirculationService.GetCirculationsUsingTimetable(1);
            Log.Info($"  GetCirculationsUsingTimetable(1): {usingTt1.Count} (oczekiwane 1, tylko c1)");

            // Status change
            CirculationService.SetStatus(c3.id, CirculationStatus.Active);
            Log.Info($"  c3 po SetStatus Active: {c3.status}");

            // Cleanup — usuń testowe
            CirculationService.RemoveCirculation(c1.id);
            CirculationService.RemoveCirculation(c2.id);
            CirculationService.RemoveCirculation(c3.id);
            Log.Info($"  Count po cleanup: {CirculationService.Circulations.Count} (oczekiwane {countBefore})");
            Log.Info("=== M5 Circulations smoke test DONE ===");
        }

        [ContextMenu("9. Test CirculationValidator (M5 smoke)")]
        public void TestCirculationValidator()
        {
            Log.Info("=== M5 CirculationValidator smoke test ===");

            // Tworzymy 4 mini-rozkłady (bez pełnych postojów i rezerwacji — tylko minimum)
            var ttA = new Timetable
            {
                name = "Test A: Olsztyn→Korsze",
                frequency = FrequencySpec.SingleRun(6 * 60), // 06:00
                stops = new List<TimetableStop>
                {
                    new TimetableStop { stationName = "Olsztyn Gł.", plannedArrivalSec = 0, plannedDepartureSec = 0 },
                    new TimetableStop { stationName = "Korsze", plannedArrivalSec = 3600, plannedDepartureSec = 3600 }
                },
                composition = new PlannedComposition { mode = CompositionMode.MultipleUnit },
                status = TimetableStatus.Active
            };
            var ttB_ok = new Timetable
            {
                name = "Test B: Korsze→Olsztyn (OK, 5min gap)",
                frequency = FrequencySpec.SingleRun(7 * 60 + 5), // 07:05 (5min po 07:00 końcu A)
                stops = new List<TimetableStop>
                {
                    new TimetableStop { stationName = "Korsze", plannedArrivalSec = 0, plannedDepartureSec = 0 },
                    new TimetableStop { stationName = "Olsztyn Gł.", plannedArrivalSec = 3600, plannedDepartureSec = 3600 }
                },
                composition = new PlannedComposition { mode = CompositionMode.MultipleUnit },
                status = TimetableStatus.Active
            };
            var ttC_shortGap = new Timetable
            {
                name = "Test C: Korsze→Olsztyn (short 1min gap)",
                frequency = FrequencySpec.SingleRun(7 * 60 + 1), // 07:01 (1min po)
                stops = new List<TimetableStop>
                {
                    new TimetableStop { stationName = "Korsze", plannedArrivalSec = 0, plannedDepartureSec = 0 },
                    new TimetableStop { stationName = "Olsztyn Gł.", plannedArrivalSec = 3600, plannedDepartureSec = 3600 }
                },
                composition = new PlannedComposition { mode = CompositionMode.MultipleUnit },
                status = TimetableStatus.Active
            };
            var ttD_wrongStart = new Timetable
            {
                name = "Test D: Elbląg→Malbork (nie spina się)",
                frequency = FrequencySpec.SingleRun(7 * 60 + 30),
                stops = new List<TimetableStop>
                {
                    new TimetableStop { stationName = "Elbląg", plannedArrivalSec = 0, plannedDepartureSec = 0 },
                    new TimetableStop { stationName = "Malbork", plannedArrivalSec = 3600, plannedDepartureSec = 3600 }
                },
                composition = new PlannedComposition { mode = CompositionMode.MultipleUnit },
                status = TimetableStatus.Active
            };

            TimetableService.AddTimetable(ttA);
            TimetableService.AddTimetable(ttB_ok);
            TimetableService.AddTimetable(ttC_shortGap);
            TimetableService.AddTimetable(ttD_wrongStart);
            Log.Info($"  Utworzono 4 test Timetable: A#{ttA.id}, B#{ttB_ok.id}, C#{ttC_shortGap.id}, D#{ttD_wrongStart.id}");

            // Test 1: Sekwencja A → B (OK) powinna dać 0 issues
            var seq1 = new List<CirculationStep>
            {
                new CirculationStep(ttA.id),
                new CirculationStep(ttB_ok.id)
            };
            var issues1 = CirculationValidator.ValidateSequence(seq1);
            Log.Info($"  Seq A→B (OK): {issues1.Count} issues (oczekiwane 0)");
            foreach (var iss in issues1) Log.Info($"    - [{iss.severity}] {iss.message}");

            // Test 2: Sekwencja A → C (short gap) powinna dać 1 warning
            var seq2 = new List<CirculationStep>
            {
                new CirculationStep(ttA.id),
                new CirculationStep(ttC_shortGap.id)
            };
            var issues2 = CirculationValidator.ValidateSequence(seq2);
            Log.Info($"  Seq A→C (short gap): {issues2.Count} issues (oczekiwane 1 Warning)");
            foreach (var iss in issues2) Log.Info($"    - [{iss.severity}] {iss.message}");

            // Test 3: Sekwencja A → D (nie spina się) powinna dać 1 error
            var seq3 = new List<CirculationStep>
            {
                new CirculationStep(ttA.id),
                new CirculationStep(ttD_wrongStart.id)
            };
            var issues3 = CirculationValidator.ValidateSequence(seq3);
            Log.Info($"  Seq A→D (wrong station): {issues3.Count} issues (oczekiwane 1 Error)");
            foreach (var iss in issues3) Log.Info($"    - [{iss.severity}] {iss.message}");

            // Test 4: GetNextStepErrors na żywo — sekwencja [A], próba dodania D
            var currentSteps = new List<CirculationStep> { new CirculationStep(ttA.id) };
            var nextErrors = CirculationValidator.GetNextStepErrors(currentSteps, ttD_wrongStart.id);
            Log.Info($"  GetNextStepErrors dla [A] + D: {nextErrors.Count} issues (oczekiwane 1 Error)");
            foreach (var iss in nextErrors) Log.Info($"    - [{iss.severity}] {iss.message}");

            // Test 5: GetNextStepErrors dla pustej sekwencji + A (pierwszy krok, zawsze OK)
            var firstErrors = CirculationValidator.GetNextStepErrors(new List<CirculationStep>(), ttA.id);
            Log.Info($"  GetNextStepErrors dla [] + A: {firstErrors.Count} issues (oczekiwane 0 — pierwszy krok)");

            // Cleanup
            TimetableService.Timetables.Remove(ttA);
            TimetableService.Timetables.Remove(ttB_ok);
            TimetableService.Timetables.Remove(ttC_shortGap);
            TimetableService.Timetables.Remove(ttD_wrongStart);
            Log.Info("=== M5 CirculationValidator smoke test DONE ===");
        }

        [ContextMenu("7. Diagnose route (start↔end station)")]
        public void DiagnoseRoute()
        {
            if (_pathGraph == null || _stations == null)
            {
                Log.Error("[TimetableDebugTools] Uruchom kroki 1 i 3 najpierw");
                return;
            }
            var start = FindStation(startStationQuery);
            var end = FindStation(endStationQuery);
            if (start == null) { Log.Error($"Nie znaleziono: {startStationQuery}"); return; }
            if (end == null) { Log.Error($"Nie znaleziono: {endStationQuery}"); return; }

            Log.Info($"=== Diagnoza trasy {start.name} ↔ {end.name} ===");
            Log.Info($"  start.pathNodeId={start.pathNodeId}, end.pathNodeId={end.pathNodeId}");
            if (start.pathNodeId < 0 || end.pathNodeId < 0)
            {
                Log.Warn("  Co najmniej jedna stacja nie ma snap'u do grafu (>200m od najbliższego toru).");
                // Spróbuj ze zwiększonym promieniem
                int s2 = _pathGraph.FindNearestNode(start.position, 1000f);
                int e2 = _pathGraph.FindNearestNode(end.position, 1000f);
                Log.Info($"  Re-snap z promieniem 1000m: start={s2}, end={e2}");
                return;
            }

            // BFS od start, sprawdź czy end jest osiągalny + ile kroków
            int comp1Size = BfsReachability(_pathGraph, start.pathNodeId, end.pathNodeId,
                out bool reached, out int stepsToEnd);
            Log.Info($"  BFS od start: osiągalny komponent {comp1Size} nodów. "
                     + (reached
                        ? $"END OSIĄGALNY w {stepsToEnd} krokach BFS."
                        : "END NIEOSIĄGALNY (różne komponenty grafu)"));

            if (!reached)
            {
                int comp2Size = BfsReachability(_pathGraph, end.pathNodeId, -1, out _, out _);
                Log.Warn($"  Komponent end node: {comp2Size} nodów.");

                // Gap analysis — zbierz wszystkie nody małego komponentu, znajdź nearest z innego
                var smallComp = CollectComponent(_pathGraph, end.pathNodeId);
                var smallSet = new HashSet<int>(smallComp);
                Log.Info($"  Bbox małego komponentu (Korsze island): "
                         + ComputeBboxString(_pathGraph, smallComp));

                // Dla każdego node w małym komponencie znajdź najbliższy node spoza
                float bestGap = float.MaxValue;
                int bestSmall = -1, bestBig = -1;
                foreach (int sn in smallComp)
                {
                    var sp = _pathGraph.GetNode(sn).position;
                    // Szukaj w promieniu 200m bo nie wiemy jak duży gap
                    int candidate = FindNearestNodeOutsideSet(_pathGraph, sp, 200f, smallSet);
                    if (candidate < 0) continue;
                    float dist = Vector2.Distance(sp, _pathGraph.GetNode(candidate).position);
                    if (dist < bestGap)
                    {
                        bestGap = dist;
                        bestSmall = sn;
                        bestBig = candidate;
                    }
                }

                if (bestBig >= 0)
                {
                    var spSmall = _pathGraph.GetNode(bestSmall).position;
                    var spBig = _pathGraph.GetNode(bestBig).position;
                    Log.Warn($"  → Najbliższa para między komponentami: gap = {bestGap:F2} m");
                    Log.Warn($"     Mały node {bestSmall} @ ({spSmall.x:F1}, {spSmall.y:F1})");
                    Log.Warn($"     Duży node {bestBig} @ ({spBig.x:F1}, {spBig.y:F1})");
                    bool smallIsJ = _pathGraph.JunctionNodeIds != null && _pathGraph.JunctionNodeIds.Contains(bestSmall);
                    bool bigIsJ = _pathGraph.JunctionNodeIds != null && _pathGraph.JunctionNodeIds.Contains(bestBig);
                    Log.Warn($"     Junction? small={smallIsJ}, big={bigIsJ}");
                    if (bestGap < 5f)
                        Log.Warn("     DIAGNOZA: gap < 5m → Union-Find tolerance OK ale junction marker missing po jednej ze stron");
                    else if (bestGap < 30f)
                        Log.Warn("     DIAGNOZA: gap 5-30m → tolerance Union-Find za mała LUB brak junction marker");
                    else
                        Log.Warn("     DIAGNOZA: gap > 30m → real OSM gap (brak ciągłości torów w danych) lub niezaładowany tile");
                }
                else
                {
                    Log.Warn("  → W promieniu 200m od żadnego node'a małego komponentu nie ma node z innego komponentu.");
                    Log.Warn("    Najprawdopodobniej brakuje torów w OSM (gap > 200m).");
                }
            }
            else
            {
                // Komponenty OK — może problem to limit eksploracji
                var result = RailwayPathfinder.FindPath(_pathGraph, start.pathNodeId, end.pathNodeId);
                if (result.success)
                    Log.Info($"  ✓ A* znajduje trasę: {result.totalLengthM/1000f:F1} km, "
                             + $"{result.nodeIds.Count} nodów, explored {result.exploredNodes}");
                else
                    Log.Warn($"  ✗ A* failuje mimo osiągalności BFS — explored {result.exploredNodes} "
                             + "(może limit 500k? nietypowy heurystyk?)");
            }
        }

        /// <summary>Zbiera wszystkie nody komponentu zawierającego seed.</summary>
        private static List<int> CollectComponent(PathfindingGraph graph, int seed)
        {
            var result = new List<int>();
            if (seed < 0 || seed >= graph.NodeCount) return result;
            var visited = new HashSet<int> { seed };
            var queue = new Queue<int>();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                int nid = queue.Dequeue();
                result.Add(nid);
                var node = graph.GetNode(nid);
                foreach (int eid in node.edgeIds)
                {
                    int neighbor = graph.GetEdge(eid).toNodeId;
                    if (visited.Add(neighbor)) queue.Enqueue(neighbor);
                }
            }
            return result;
        }

        /// <summary>
        /// Najbliższy node w promieniu maxDistM którego nie ma w excludeSet.
        /// Używane do gap analysis między komponentami.
        /// </summary>
        private static int FindNearestNodeOutsideSet(PathfindingGraph graph, Vector2 pos,
                                                      float maxDistM, HashSet<int> excludeSet)
        {
            // Brute force po wszystkich nodach — dla diagnostyki OK (rzadko wywoływane)
            int best = -1;
            float bestSq = maxDistM * maxDistM;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                if (excludeSet.Contains(i)) continue;
                float sq = (graph.GetNode(i).position - pos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>String z bbox komponentu w formie diagnostycznej.</summary>
        private static string ComputeBboxString(PathfindingGraph graph, List<int> nodeIds)
        {
            if (nodeIds == null || nodeIds.Count == 0) return "(empty)";
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (int nid in nodeIds)
            {
                var p = graph.GetNode(nid).position;
                if (p.x < minX) minX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.x > maxX) maxX = p.x;
                if (p.y > maxY) maxY = p.y;
            }
            return $"X:[{minX:F0}..{maxX:F0}] Y:[{minY:F0}..{maxY:F0}] "
                   + $"({maxX - minX:F0}m × {maxY - minY:F0}m)";
        }

        /// <summary>
        /// BFS od startNode. Jeśli targetNode != -1, wczesny exit gdy go napotkamy.
        /// Zwraca rozmiar przeszukanego komponentu + flagę osiągalności + krok BFS w którym znaleziono target.
        /// </summary>
        private static int BfsReachability(PathfindingGraph graph, int startNode, int targetNode,
                                            out bool reachedTarget, out int stepsToTarget)
        {
            reachedTarget = false;
            stepsToTarget = -1;
            if (startNode < 0 || startNode >= graph.NodeCount) return 0;

            var visited = new HashSet<int> { startNode };
            var queue = new Queue<(int node, int depth)>();
            queue.Enqueue((startNode, 0));
            int size = 0;

            while (queue.Count > 0)
            {
                var (nid, depth) = queue.Dequeue();
                size++;
                if (nid == targetNode)
                {
                    reachedTarget = true;
                    stepsToTarget = depth;
                    // Kontynuuj BFS żeby podać pełny rozmiar komponentu
                }
                var node = graph.GetNode(nid);
                foreach (int eid in node.edgeIds)
                {
                    int neighbor = graph.GetEdge(eid).toNodeId;
                    if (visited.Add(neighbor))
                        queue.Enqueue((neighbor, depth + 1));
                }
            }
            return size;
        }

        [ContextMenu("6. List first 10 stations")]
        public void ListStations()
        {
            if (_stations == null) { Log.Warn("Załaduj stacje najpierw"); return; }
            foreach (var s in _stations.Take(10))
                Log.Info($"  {s.stationId} {s.name} ({(s.isMajorStation ? "station" : "halt")}) "
                         + $"node={s.pathNodeId} pos=({s.position.x:F0},{s.position.y:F0})");
            if (_stations.Count > 10) Log.Info($"  ... i {_stations.Count - 10} więcej");
        }

        // ─────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────

        private RailwayStation FindStation(string query)
        {
            if (_stations == null || string.IsNullOrEmpty(query)) return null;
            var q = query.ToLowerInvariant();
            return _stations.FirstOrDefault(s => s.name != null && s.name.ToLowerInvariant().Contains(q));
        }

        private void ResolveRefs()
        {
            if (mapLoader == null) mapLoader = FindAnyObjectByType<MapLoader>();
            if (railwayGraph == null) railwayGraph = FindAnyObjectByType<RailwayGraph>();
        }

        private static int CountComponents(PathfindingGraph graph, out int biggestSize)
        {
            biggestSize = 0;
            if (graph.NodeCount == 0) return 0;

            var visited = new bool[graph.NodeCount];
            int componentCount = 0;
            var queue = new Queue<int>();

            for (int start = 0; start < graph.NodeCount; start++)
            {
                if (visited[start]) continue;
                componentCount++;
                int size = 0;

                queue.Clear();
                queue.Enqueue(start);
                visited[start] = true;

                while (queue.Count > 0)
                {
                    int nodeId = queue.Dequeue();
                    size++;
                    var node = graph.GetNode(nodeId);
                    foreach (var edgeId in node.edgeIds)
                    {
                        int neighbor = graph.GetEdge(edgeId).toNodeId;
                        if (!visited[neighbor])
                        {
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                if (size > biggestSize) biggestSize = size;
            }

            return componentCount;
        }
    }
}
