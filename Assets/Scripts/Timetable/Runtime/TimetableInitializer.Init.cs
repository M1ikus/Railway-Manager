using System.Collections.Generic;
using UnityEngine;
using formap;
using MapSystem;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    // Audit 2026-05-15 (split god class): pipeline ładowania danych wyniesiony z root
    // TimetableInitializer.cs. Zawiera Initialize + InitializeCoroutine (8-step streaming
    // build) + TryLoadFromInitState (M-PL B2 fast-path) + LoadSignals/ParseSignalFunction
    // + FindStation + legacy BuildFromTilesCoroutine.
    public partial class TimetableInitializer
    {
        /// <summary>True gdy <see cref="InitializeCoroutine"/> w locie (zapobiega multiple spawn).</summary>
        public bool IsInitializing { get; private set; }

        /// <summary>
        /// Inicjalizacja wywoływana ręcznie (np. z MainTabBarUI przy pierwszym kliknięciu "Rozkłady")
        /// lub automatycznie przez coroutine po załadowaniu MapScene.
        /// Asynchroniczna — startuje <see cref="InitializeCoroutine"/> i returns natychmiast.
        /// Callerzy polują <see cref="IsReady"/> żeby wiedzieć gdy gotowe.
        /// Na pełnej Polsce trwa kilka minut (5624 tiles × LoadTile + graph build).
        /// </summary>
        public void Initialize()
        {
            if (isReady || IsInitializing) return;
            StartCoroutine(InitializeCoroutine());
        }

        private System.Collections.IEnumerator InitializeCoroutine()
        {
            IsInitializing = true;

            var mapLoader = FindAnyObjectByType<MapLoader>();
            var railwayGraph = FindAnyObjectByType<RailwayGraph>();
            if (mapLoader == null || railwayGraph == null)
            {
                Log.Warn("[TimetableInitializer] MapLoader or RailwayGraph not found — skipping init");
                IsInitializing = false;
                yield break;
            }

            float t0 = Time.realtimeSinceStartup;

            // M-PL B2: try fast-path — pre-built init-state-{country}.bin obok poland-v8.bin.
            // formap generuje init-state-pl.bin offline. Unity ładuje w sekundach zamiast 600s build.
            // Patrz GraphDataUnityAdapter + RailwayManager.GraphData.dll.
            if (TryLoadFromInitState(t0))
            {
                IsInitializing = false;
                yield break;
            }

            Log.Info("[TimetableInitializer] Initializing timetable pipeline (STREAMING mode)...");

            mapLoader.SetLODLevel(0);
            mapLoader.BeginExtraction(); // suppress RenderTile dla concurrent TileManager loadingu

            // Streaming approach — per tile load → process → discard. Memory bounded constant.
            // Wszyscy processors akumulują dane inkrementalnie. Tile data nie cache'owana.
            var adminProc = new AdminBoundaryStreamProcessor();
            var placeProc = new PlaceStreamProcessor();
            var stationProc = new StationStreamProcessor();
            var platformProc = new PlatformStreamProcessor();
            var signalProc = new SignalsStreamProcessor();
            var railwayCol = new RailwayFeatureCollector();

            // M-PL: SYNC stream zamiast coroutine. Coroutine yield powoduje Editor degradation
            // po N iteracji (yield = Editor watchdog tick → asset refresh/etc → freeze).
            // Sync blokuje main thread 30-60s ale gwarantuje completion (Editor "not responding"
            // modal może się pojawić — kliknij Wait).
            Log.Info("[TimetableInitializer] Starting sync stream (Editor będzie wisieć ~30-60s)...");
            yield return null; // jeden yield przed sync żeby UI pokazał "loading..."
            mapLoader.StreamAllTilesSync((tileID, layers) =>
            {
                railwayCol.OnTile(layers);
                adminProc.OnTile(layers);
                placeProc.OnTile(layers);
                stationProc.OnTile(layers);
                platformProc.OnTile(layers);
                signalProc.OnTile(layers);
            }, maxTilesToProcess: maxTilesInStream);
            yield return null; // post-sync yield żeby UI mógł wrócić

            // === FINALIZE PHASE — ALL SYNC (no yields) ===
            // Yield między Finalize steps powoduje Editor degradation (jak w streaming).
            // Cały Finalize w jednym sync block. Editor wisi 5-15 min — modal "not responding"
            // → Wait. Po skończeniu wraca naturalnie.
            Log.Info("[TimetableInitializer] Finalize ALL — sync block, Editor będzie wisiał 5-15 min...");

            Log.Info("[TimetableInitializer] Finalize 1/8: AdminBoundaries + VoivodeshipResolver + CountryOverlay");
            var adminRegions = adminProc.Finalize();
            Resolver = new VoivodeshipResolver(adminRegions);
            CountryOverlayService.Initialize(adminRegions);
            float tStep1 = Time.realtimeSinceStartup - t0;
            Log.Info($"[TimetableInitializer]   Step 1 complete in {tStep1:F2}s");

            Log.Info("[TimetableInitializer] Finalize 2/8: RailwayGraph + PathfindingGraph (HEAVY)");
            float tStep2Start = Time.realtimeSinceStartup;
            var railwayFeatures = railwayCol.Finalize();
            Log.Info($"[TimetableInitializer]   collected {railwayFeatures.Count} railway features");
            railwayGraph.BuildGraph(railwayFeatures);
            float tRailwayGraph = Time.realtimeSinceStartup - tStep2Start;
            Log.Info($"[TimetableInitializer]   RailwayGraph built in {tRailwayGraph:F2}s");
            float tPathGraphStart = Time.realtimeSinceStartup;
            Graph = new PathfindingGraph();
            Graph.BuildFromFeaturesUnionFind(railwayFeatures, graphCellSizeM);
            float tPathGraph = Time.realtimeSinceStartup - tPathGraphStart;
            Log.Info($"[TimetableInitializer]   PathfindingGraph built in {tPathGraph:F2}s");
            railwayFeatures = null;

            Log.Info("[TimetableInitializer] Finalize 3/8: Places");
            float tStep3Start = Time.realtimeSinceStartup;
            Places = placeProc.Finalize(Resolver);
            Log.Info($"[TimetableInitializer]   Step 3 complete in {Time.realtimeSinceStartup - tStep3Start:F2}s");

            Log.Info("[TimetableInitializer] Finalize 4/8: Stations");
            float tStep4Start = Time.realtimeSinceStartup;
            Stations = stationProc.Finalize(Graph, stationSnapRadiusM, Resolver);
            Log.Info($"[TimetableInitializer]   Step 4 complete in {Time.realtimeSinceStartup - tStep4Start:F2}s");

            Log.Info("[TimetableInitializer] Finalize 5/8: Platforms");
            float tStep5Start = Time.realtimeSinceStartup;
            Platforms = platformProc.Finalize(Graph);
            // M-TimetableUX 2026-05-11: propagacja `ref` peronu → `railway:track_ref` najbliższej edge.
            // Bez tego StationTrackData.Generate widzi tylko edges z OSM-explicit track_ref
            // (rzadkie), pomijając tory dla peronów które MAJĄ ref ale OSM nie zoznaczył ich na torze.
            Graph.PropagateTrackRefsFromPlatforms(Platforms);
            Log.Info($"[TimetableInitializer]   Step 5 complete in {Time.realtimeSinceStartup - tStep5Start:F2}s");

            Log.Info("[TimetableInitializer] Finalize 6/8: Agglomerations + TimetableService");
            float tStep6Start = Time.realtimeSinceStartup;
            Agglomerations = AgglomerationDetector.DetectAgglomerations(Stations, Places);
            TimetableService.Initialize();
            // M-TimetableUX F1.14: subscribe MeetingEventsService dla auto-invalidate na timetable changes
            RailwayManager.Timetable.Meetings.MeetingEventsService.Bootstrap();
            Log.Info($"[TimetableInitializer]   Step 6 complete in {Time.realtimeSinceStartup - tStep6Start:F2}s");

            Log.Info("[TimetableInitializer] Finalize 7/8: StationTrackData");
            float tStep7Start = Time.realtimeSinceStartup;
            TrackData = new StationTrackData();
            TrackData.Load();
            if (!TrackData.IsLoaded)
            {
                StationTrackData.Generate(this);
                TrackData.Load();
            }
            Log.Info($"[TimetableInitializer]   Step 7 complete in {Time.realtimeSinceStartup - tStep7Start:F2}s");

            Log.Info("[TimetableInitializer] Finalize 8/8: Signals + BlockSections");
            float tStep8Start = Time.realtimeSinceStartup;
            Signals = signalProc.Finalize(Graph);
            float tSignals = Time.realtimeSinceStartup - tStep8Start;
            Log.Info($"[TimetableInitializer]   Signals Finalize in {tSignals:F2}s");
            var stationBoundaryNodes = new HashSet<int>();
            foreach (var st in Stations)
                if (st.pathNodeId >= 0 && st.isMajorStation)
                    stationBoundaryNodes.Add(st.pathNodeId);
            float tBlockStart = Time.realtimeSinceStartup;
            var buildResult = BlockSectionBuilder.Build(Graph, stationBoundaryNodes);
            BlockSections = new BlockSectionGraph(buildResult);
            Log.Info($"[TimetableInitializer]   BlockSections built in {Time.realtimeSinceStartup - tBlockStart:F2}s (boundaryNodes={stationBoundaryNodes.Count})");

            // EndExtraction — TileManager force update reloaduje visible tile normalnie z RenderTile
            mapLoader.EndExtraction();
            yield return null; // single yield po wszystkim, daj UI czas na refresh

            if (Graph == null || Stations == null)
            {
                Log.Warn("[TimetableInitializer] Build incomplete — required data missing, not marking ready");
                IsInitializing = false;
                yield break;
            }

            float elapsed = Time.realtimeSinceStartup - t0;
            isReady = true;
            IsInitializing = false;
            Log.Info($"[TimetableInitializer] Ready in {elapsed:F2}s — "
                     + $"{Graph.NodeCount} nodes, {Stations.Count} stations, "
                     + $"{Platforms.Count} platforms, {BlockSections.SectionCount} block sections");
        }

        /// <summary>
        /// M-PL B2 fast-path: ładuje init-state-{country}.bin (pre-built przez formap).
        /// Pomija cały build pipeline (5-15 min) — tylko deserializacja + downstream init.
        ///
        /// Returns true gdy load successful + isReady=true. False = brak/stale/corrupted file
        /// → caller fallback na streaming build.
        ///
        /// **WAŻNE:** wywołuje też <c>mapLoader.EndExtraction()</c> na końcu — TileManager startuje
        /// w trybie paused (MapLoader.cs:277 czeka aż TimetableInitializer przejmie kontrolę).
        /// Bez EndExtraction tile loading nigdy nie startuje → mapa jest pusta wizualnie
        /// (logic ready, ale Buildings/Forests/Water/Highways/POIs nie renderowane).
        /// </summary>
        private bool TryLoadFromInitState(float t0)
        {
            // Country code — na razie zawsze "PL" (single country). Multi-country DLC = parametryzacja.
            const string CountryCode = "PL";

            // Source map path — używany do mtime check (init-state stale gdy source mapy nowszy).
            // v8: poland-v8.bin (mtime przypięty do init-state.SourceMapMtime przy deployu → gate valid).
            string sourceMapPath = System.IO.Path.Combine(AppPaths.PolandMapsDir, "poland-v8.bin");
            if (!System.IO.File.Exists(sourceMapPath))
            {
                // Fallback do warm-maz dla pre-M-PL save'ów / dev environments
                string warmMaz = System.IO.Path.Combine(AppPaths.PolandMapsDir, "warminsko-mazurskie-v7.bin");
                if (System.IO.File.Exists(warmMaz)) sourceMapPath = warmMaz;
            }

            string initStatePath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(sourceMapPath) ?? ".",
                $"init-state-{CountryCode.ToLower()}.bin");

            if (!GraphDataUnityAdapter.IsInitStateValid(initStatePath, CountryCode, sourceMapPath))
            {
                Log.Info($"[TimetableInitializer] Pre-built init-state not found/stale at {initStatePath} — falling back to runtime build");
                return false;
            }

            // MapLoader/TileManager startuje paused (MapLoader.cs:277) czekając aż przejmiemy
            // kontrolę. W fast-path nie potrzebujemy extraction — od razu wznawiamy renderowanie
            // tile (Buildings/Forests/Water/Highways/etc.) równolegle z deserializacją init-state.
            var mapLoader = FindAnyObjectByType<MapLoader>();
            if (mapLoader != null)
            {
                mapLoader.SetLODLevel(0);
                mapLoader.EndExtraction(); // unpause TileManager + uruchom rendering
            }

            Log.Info($"[TimetableInitializer] M-PL B2 fast-path: loading {initStatePath}...");

            GraphDataUnityAdapter.LoadResult loaded;
            try
            {
                loaded = GraphDataUnityAdapter.Load(initStatePath);
            }
            catch (System.Exception ex)
            {
                Log.Warn($"[TimetableInitializer] Init-state load failed: {ex.Message} — falling back to runtime build");
                return false;
            }

            // Wstrzyknij załadowane dane do publicznych pól
            Graph = loaded.Graph;
            Stations = loaded.Stations;
            Platforms = loaded.Platforms;
            Places = loaded.Places;
            Signals = loaded.Signals;
            Coastlines = loaded.Coastlines;
            Resolver = new VoivodeshipResolver(loaded.AdminRegions);
            BlockSections = new BlockSectionGraph(loaded.BlockSections);

            // M-TimetableUX 2026-05-11: propagacja `ref` peronu na `railway:track_ref` edge
            // (path: bulk-load z init-state.bin — bin nie ma propagated track_refs, robimy w runtime).
            Graph.PropagateTrackRefsFromPlatforms(Platforms);

            // Downstream init — szybkie operacje in-memory
            CountryOverlayService.Initialize(loaded.AdminRegions);
            Agglomerations = AgglomerationDetector.DetectAgglomerations(Stations, Places);
            TimetableService.Initialize();
            // M-TimetableUX F1.14: subscribe MeetingEventsService dla auto-invalidate
            RailwayManager.Timetable.Meetings.MeetingEventsService.Bootstrap();
            // M-TimetableUX F1.15: subscribe TimetableWorkflowOrchestrator (suggestion auto-trigger)
            RailwayManager.Timetable.Workflows.TimetableWorkflowOrchestrator.Bootstrap();

            // Station track data — z JSON file albo wygeneruj
            TrackData = new StationTrackData();
            TrackData.Load();
            if (!TrackData.IsLoaded)
            {
                StationTrackData.Generate(this);
                TrackData.Load();
            }

            float elapsed = Time.realtimeSinceStartup - t0;
            isReady = true;
            Log.Info($"[TimetableInitializer] Fast-path Ready in {elapsed:F2}s — "
                     + $"{Graph.NodeCount} nodes, {Stations.Count} stations, "
                     + $"{Platforms.Count} platforms, {BlockSections.SectionCount} block sections "
                     + $"(saved ~{600 - elapsed:F0}s vs runtime build)");
            return true;
        }

        /// <summary>
        /// LEGACY (M-PL pre-streaming): wszystkie Loader'y odczytują features z pełnego tile cache.
        /// Zastąpione przez streaming approach w <see cref="InitializeCoroutine"/> — ta metoda
        /// pozostaje dla referencji i ewentualnego rollback. NIE WYWOŁYWAĆ na pełnej Polsce
        /// (memory bloat 30+ GB, freeze na fat tiles).
        /// </summary>
        [System.Obsolete("Use streaming via StreamAllTilesSync + processors w InitializeCoroutine")]
        private System.Collections.IEnumerator BuildFromTilesCoroutine(MapLoader mapLoader, RailwayGraph railwayGraph)
        {
            // Build railway graph + pathfinding (heaviest step — może zająć 30-90s na pełnej PL)
            Log.Info("[TimetableInitializer] Step 1/8: aggregating railway features + RailwayGraph...");
            var railwayFeatures = mapLoader.GetAllFeaturesAcrossTiles(BinaryFormat.LayerType.Railways);
            railwayGraph.BuildGraph(railwayFeatures);
            yield return null;

            Log.Info($"[TimetableInitializer] Step 2/8: PathfindingGraph from {railwayFeatures.Count} features (this may take 30-90s)...");
            Graph = new PathfindingGraph();
            Graph.BuildFromFeaturesUnionFind(railwayFeatures, graphCellSizeM);

            // Lokalna ref → null żeby GC mogło zwolnić ~120k MeshGeometry przed kolejnymi krokami.
            railwayFeatures = null;
            yield return null;

            // Admin regions + voivodeships
            Log.Info("[TimetableInitializer] Step 3/8: AdminBoundaries + VoivodeshipResolver...");
            var adminRegions = AdminBoundaryLoader.LoadFrom(mapLoader);
            Resolver = new VoivodeshipResolver(adminRegions);
            CountryOverlayService.Initialize(adminRegions);
            yield return null;

            // Places
            Log.Info("[TimetableInitializer] Step 4/8: Places (cities/towns/villages)...");
            Places = PlaceLoader.LoadFrom(mapLoader, Resolver);
            yield return null;

            // Stations + platforms
            Log.Info("[TimetableInitializer] Step 5/8: Stations + Platforms...");
            Stations = StationLoader.LoadFrom(mapLoader, Graph, stationSnapRadiusM, Resolver);
            yield return null;
            Platforms = PlatformLoader.LoadFrom(mapLoader, Graph);
            yield return null;
            // M-TimetableUX 2026-05-11: propagacja `ref` peronu → `railway:track_ref` edge.
            Graph.PropagateTrackRefsFromPlatforms(Platforms);
            yield return null;

            // Agglomerations + TimetableService
            Log.Info("[TimetableInitializer] Step 6/8: Agglomerations + TimetableService...");
            Agglomerations = AgglomerationDetector.DetectAgglomerations(Stations, Places);
            TimetableService.Initialize();
            // M-TimetableUX F1.14: subscribe MeetingEventsService dla auto-invalidate
            RailwayManager.Timetable.Meetings.MeetingEventsService.Bootstrap();
            // M-TimetableUX F1.15: subscribe TimetableWorkflowOrchestrator (suggestion auto-trigger)
            RailwayManager.Timetable.Workflows.TimetableWorkflowOrchestrator.Bootstrap();
            yield return null;

            // Dane torów stacji — ładuj z JSON, generuj jeśli brak
            Log.Info("[TimetableInitializer] Step 7/8: StationTrackData...");
            TrackData = new StationTrackData();
            TrackData.Load();
            if (!TrackData.IsLoaded)
            {
                StationTrackData.Generate(this);
                TrackData.Load();
            }
            yield return null;

            // Sygnały + bloki semaforowe
            Log.Info("[TimetableInitializer] Step 8/8: Signals + BlockSections...");
            Signals = LoadSignals(mapLoader, Graph);
            yield return null;

            // Odcinki blokowe — granice = railway=station (nie halt/przystanek)
            var stationBoundaryNodes = new HashSet<int>();
            foreach (var st in Stations)
                if (st.pathNodeId >= 0 && st.isMajorStation)
                    stationBoundaryNodes.Add(st.pathNodeId);
            var buildResult = BlockSectionBuilder.Build(Graph, stationBoundaryNodes);
            BlockSections = new BlockSectionGraph(buildResult);
        }

        /// <summary>Szuka stacji po fragmencie nazwy (case-insensitive).</summary>
        public RailwayStation FindStation(string query)
        {
            if (Stations == null || string.IsNullOrEmpty(query)) return null;
            var q = query.ToLowerInvariant();
            foreach (var s in Stations)
                if (s.name != null && s.name.ToLowerInvariant().Contains(q))
                    return s;
            return null;
        }

        /// <summary>
        /// Zbiera semafory z warstwy POIs (railway=signal), parsuje function/direction
        /// z railway:signal:*:function i snapuje do węzłów grafu.
        /// </summary>
        static List<SignalInfo> LoadSignals(MapLoader mapLoader, PathfindingGraph graph)
        {
            var result = new List<SignalInfo>();
            if (mapLoader == null || graph == null) return result;

            var poiFeatures = mapLoader.GetAllFeaturesAcrossTiles(BinaryFormat.LayerType.POIs);
            if (poiFeatures == null) return result;

            int total = 0, withFunction = 0, duplicates = 0;
            int entry = 0, exit = 0, block = 0, intermediate = 0;

            // Deduplikacja po pozycji (formap replikuje feature'y między tile'ami)
            var seenPositions = new HashSet<string>();

            foreach (var feature in poiFeatures)
            {
                if (feature.Vertices == null || feature.Vertices.Count == 0) continue;
                if (!feature.Metadata.TryGetValue("railway", out var railway)) continue;
                if (railway != "signal") continue;

                total++;

                var func = ParseSignalFunction(feature.Metadata);
                if (func == SignalFunction.Unknown) continue;
                withFunction++;

                var pos = feature.Vertices[0];

                // Dedupe po pozycji (centymetrowa precyzja)
                string posKey = $"{pos.x:F2}|{pos.y:F2}";
                if (!seenPositions.Add(posKey))
                {
                    duplicates++;
                    continue;
                }

                switch (func)
                {
                    case SignalFunction.Entry: entry++; break;
                    case SignalFunction.Exit: exit++; break;
                    case SignalFunction.Block: block++; break;
                    case SignalFunction.Intermediate: intermediate++; break;
                }

                // Snap radius 10m — na dwutorowej tory są ~4m od siebie,
                // większy radius snapuje sygnały jednego toru do drugiego
                int nodeId = graph.FindNearestNode(pos, 10f);
                if (nodeId < 0) continue;

                var dir = SignalDirection.Both;
                if (feature.Metadata.TryGetValue("railway:signal:direction", out var dirStr))
                {
                    if (dirStr == "forward") dir = SignalDirection.Forward;
                    else if (dirStr == "backward") dir = SignalDirection.Backward;
                }

                feature.Metadata.TryGetValue("ref", out var refNum);

                result.Add(new SignalInfo
                {
                    nodeId = nodeId,
                    function = func,
                    direction = dir,
                    refNum = refNum ?? ""
                });
            }

            Log.Info($"[TimetableInitializer] Signals: {total} total, {withFunction} with function, " +
                $"{duplicates} duplicates skipped, {result.Count} snapped — " +
                $"entry={entry} exit={exit} block={block} intermediate={intermediate}");
            return result;
        }

        static SignalFunction ParseSignalFunction(System.Collections.Generic.Dictionary<string, string> metadata)
        {
            // TYLKO semafory główne (main) i kombinowane (combined) dzielą bloki.
            // NIE używamy distant (tarcze ostrzegawcze) — one uprzedzają o stanie
            // następnego semafora głównego, ale same nie są granicą bloku.
            // Pomijamy też crossing (przejazdowe), shunting (manewrowe),
            // speed_limit, stop itp.
            string[] keys = {
                "railway:signal:main:function",
                "railway:signal:combined:function"
            };
            foreach (var key in keys)
            {
                if (metadata.TryGetValue(key, out var val))
                {
                    switch (val)
                    {
                        case "entry": return SignalFunction.Entry;
                        case "exit": return SignalFunction.Exit;
                        case "block": return SignalFunction.Block;
                        case "intermediate": return SignalFunction.Intermediate;
                    }
                }
            }
            return SignalFunction.Unknown;
        }
    }
}
