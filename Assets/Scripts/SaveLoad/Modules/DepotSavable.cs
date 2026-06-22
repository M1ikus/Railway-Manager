using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using DepotSystem;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;
using DepotSystem.OutdoorEquipment;
using RailwayManager.Core;

namespace RailwayManager.SaveLoad.Modules
{
    /// <summary>
    /// M13-7 / M13-15 / MM-1 / MM-9: Persystencja Depot 3D — geometria torów + ściany + pokoje
    /// + sieć trakcyjna + meble + lvl pokoi + outdoor equipment.
    ///
    /// Module ID: "depot_3d". Schema v6 (v2 walls+rooms, v3 furniture, v4 room.level,
    /// v5 placedOutdoorEquipment, v6 counters + paths + visual rebuild snapshots).
    ///
    /// Pola w bundle:
    /// - trackNodes (JArray TrackNode) — punkty geometrii torów
    /// - trackEdges (JArray TrackEdge) — krawędzie z polyline + curvature + catenary flag
    /// - tracksData (JArray DepotTrackData) — torowe metadata (typ/nazwa/occupancy)
    /// - walls (JArray) — segmenty ścian + otwory (drzwi/okna), v2+
    /// - wallCounters (JObject) — nextWallId/nextOpeningId/nextBuildingId, v2+
    /// - rooms (JArray) — wykryte pokoje z roomType (player-assigned) + level (MM-1, v4+), v2+
    /// - roomsCounter (JObject) — nextRoomId, v2+
    ///
    /// **Catenary** (sieć trakcyjna 3D — słupy + przewody) NIE jest osobno persystowana.
    /// Jest deterministycznie regenerowana z <c>TrackEdge.HasCatenary</c> + <c>DepotTrackData.HasCatenary</c>
    /// po Deserialize przez wywołanie <c>CatenaryGenerator.GenerateNetwork()</c>. Save 60% mniejszy
    /// niż gdyby trzymać setki słupów/przęseł w JSON.
    ///
    /// **Floor wizualizacja** (DetectedRoom.floorObject) nie jest persystowana. Po load
    /// dane (cells/bounds/roomType) są w pamięci, ale GameObject podłogi powstaje na
    /// najbliższe OnWallsChanged event (np. gracz dotknie ściany → trigger). MVP limitation.
    ///
    /// **Backward compat:** brak migrator'ów (od 2026-05-15 reset wszystkich SchemaVersion
    /// do 1 — patrz CLAUDE.md "Schema versioning"). Stary save bez nowszych pól → Deserialize
    /// fallback `?? default` na każdym field. Save z wersji ze starym schematem (v1-v6) →
    /// SaveOrchestrator wykrywa sourceVersion > current=1, próbuje direct deserialize,
    /// fallback'i obsługują brakujące/dodatkowe pola gracefully.
    ///
    /// Decyzje:
    /// - WallBuildingSystem/RoomDetectionSystem to MonoBehaviour znalezione przez FindAnyObjectByType
    ///   (singletony w scenie Depot). Brak instancji → Serialize wraca tylko track data.
    /// - GameObject pola (wallObject/openingObject/floorObject) NIE są serializowane — manual JObject
    ///   build pomija je celowo (JsonConvert by się zapętlił na UnityEngine.Object refs).
    /// - Po Deserialize: WallBuildingSystem/RoomDetectionSystem dostają OnWallsChanged event
    ///   żeby aktualizowały event subscribers (UI, room detection). CatenaryGenerator
    ///   regeneruje sieć trakcyjną.
    /// </summary>
    public class DepotSavable : ISavable
    {
        public string ModuleId => "depot_3d";
        public int SchemaVersion => 1; // pre-EA reset 2026-05-15; bump po EA = real migrator

        // BUG-044: cache scene refs per top-level call (15× FindAnyObjectByType wcześniej).
        // Invalidate na początku Serialize/Deserialize/InitializeDefault — bo scene reload
        // unieważnia referencje (savable singleton w SaveRegistry przeżywa scene change).
        private TrackGraph _cachedGraph;
        private PrefabTrackBuilder _cachedTrackBuilder;
        private PathGraph _cachedPathGraph;
        private PathVisualBuilder _cachedPathVisualBuilder;
        private PathBuildStateMachine _cachedPathBuildStateMachine;
        private WallBuildingSystem _cachedWallSys;
        private RoomDetectionSystem _cachedRoomSys;
        private FurniturePlacer _cachedFurniturePlacer;
        private OutdoorEquipmentPlacer _cachedOutdoorPlacer;
        private CatenaryGenerator _cachedCatGen;

        private void InvalidateSceneRefs()
        {
            _cachedGraph = null;
            _cachedTrackBuilder = null;
            _cachedPathGraph = null;
            _cachedPathVisualBuilder = null;
            _cachedPathBuildStateMachine = null;
            _cachedWallSys = null;
            _cachedRoomSys = null;
            _cachedFurniturePlacer = null;
            _cachedOutdoorPlacer = null;
            _cachedCatGen = null;
        }

        // BUG-044 follow-up (2026-05-10): poprzednia wersja miała expression-body z rekursją
        // (`(_cachedGraph = GetGraph())` — wołało samo siebie do stack overflow przy
        // pierwszym Serialize gdy obiekt nie był jeszcze zcache'owany). Pełnotekstowe if'y
        // jak w GetFurniturePlacer eliminują pułapkę.
        private TrackGraph GetGraph()
        {
            if (_cachedGraph == null)
                _cachedGraph = Object.FindAnyObjectByType<TrackGraph>();
            return _cachedGraph;
        }
        private PrefabTrackBuilder GetTrackBuilder()
        {
            if (_cachedTrackBuilder == null)
                _cachedTrackBuilder = Object.FindAnyObjectByType<PrefabTrackBuilder>();
            return _cachedTrackBuilder;
        }
        private PathGraph GetPathGraph()
        {
            if (_cachedPathGraph == null)
                _cachedPathGraph = Object.FindAnyObjectByType<PathGraph>();
            return _cachedPathGraph;
        }
        private PathVisualBuilder GetPathVisualBuilder()
        {
            if (_cachedPathVisualBuilder == null)
                _cachedPathVisualBuilder = Object.FindAnyObjectByType<PathVisualBuilder>();
            return _cachedPathVisualBuilder;
        }
        private PathBuildStateMachine GetPathBuildStateMachine()
        {
            if (_cachedPathBuildStateMachine == null)
                _cachedPathBuildStateMachine = Object.FindAnyObjectByType<PathBuildStateMachine>();
            return _cachedPathBuildStateMachine;
        }
        private WallBuildingSystem GetWallSys()
        {
            if (_cachedWallSys == null)
                _cachedWallSys = Object.FindAnyObjectByType<WallBuildingSystem>();
            return _cachedWallSys;
        }
        private RoomDetectionSystem GetRoomSys()
        {
            if (_cachedRoomSys == null)
                _cachedRoomSys = Object.FindAnyObjectByType<RoomDetectionSystem>();
            return _cachedRoomSys;
        }
        private FurniturePlacer GetFurniturePlacer()
        {
            if (_cachedFurniturePlacer == null)
                _cachedFurniturePlacer = FurniturePlacer.Instance ?? Object.FindAnyObjectByType<FurniturePlacer>();
            return _cachedFurniturePlacer;
        }
        private OutdoorEquipmentPlacer GetOutdoorPlacer()
        {
            if (_cachedOutdoorPlacer == null)
                _cachedOutdoorPlacer = OutdoorEquipmentPlacer.Instance ?? Object.FindAnyObjectByType<OutdoorEquipmentPlacer>();
            return _cachedOutdoorPlacer;
        }
        private CatenaryGenerator GetCatGen()
        {
            if (_cachedCatGen == null)
                _cachedCatGen = Object.FindAnyObjectByType<CatenaryGenerator>();
            return _cachedCatGen;
        }

        public JObject Serialize()
        {
            InvalidateSceneRefs(); // BUG-044: fresh fetch per call (scene may have changed)
            var data = new JObject();

            // ── Track graph (existing v1) ──────────────
            var graph = GetGraph();
            if (graph != null)
            {
                data["trackNodes"] = JArray.FromObject(graph.Nodes.Values);
                data["trackEdges"] = JArray.FromObject(graph.Edges.Values);
                data["tracksData"] = JArray.FromObject(graph.Tracks.Values);
                // Counter'y: TrackGraph nie expose'uje getterów — używamy max(id)+1 jako fallback.
                // Restore zrobi RestoreFromSave który klampuje do max+1 jeśli value <=0.
                data["trackCounters"] = new JObject
                {
                    ["nextNodeId"] = ComputeNextId(graph.Nodes.Keys),
                    ["nextEdgeId"] = ComputeNextId(graph.Edges.Keys),
                    ["nextTrackId"] = ComputeNextId(graph.Tracks.Keys)
                };

                var trackBuilder = GetTrackBuilder();
                data["turnoutEntities"] = trackBuilder != null
                    ? JArray.FromObject(trackBuilder.GetTurnoutSnapshot())
                    : new JArray();
            }
            else
            {
                Log.Info("[DepotSavable] TrackGraph null (nie w scenie Depot) — track data pominięte");
            }

            // ── Walls (v2+) ────────────────────────────
            var pathGraph = GetPathGraph();
            if (pathGraph != null)
            {
                data["pathNodes"] = JArray.FromObject(pathGraph.Nodes.Values);
                data["pathEdges"] = JArray.FromObject(pathGraph.Edges.Values);
                data["pathCounters"] = new JObject
                {
                    ["nextNodeId"] = ComputeNextId(pathGraph.Nodes.Keys),
                    ["nextEdgeId"] = ComputeNextId(pathGraph.Edges.Keys)
                };
            }

            var pathBuild = GetPathBuildStateMachine();
            data["pathCells"] = pathBuild != null
                ? JArray.FromObject(pathBuild.GetPlacedCellSnapshots())
                : new JArray();

            var pathVisuals = GetPathVisualBuilder();
            data["pathVisualSegments"] = pathVisuals != null
                ? JArray.FromObject(pathVisuals.GetSnapshot())
                : new JArray();

            var wallSys = GetWallSys();
            if (wallSys != null)
            {
                var wallsArr = new JArray();
                foreach (var w in wallSys.AllWalls)
                {
                    if (w == null) continue;
                    var openingsArr = new JArray();
                    if (w.openings != null)
                    {
                        foreach (var o in w.openings)
                        {
                            if (o == null) continue;
                            openingsArr.Add(new JObject
                            {
                                ["openingId"]      = o.openingId,
                                ["type"]           = o.type.ToString(),
                                ["distanceOnWall"] = o.distanceOnWall
                            });
                        }
                    }
                    wallsArr.Add(new JObject
                    {
                        ["wallId"]     = w.wallId,
                        ["startPos"]   = Vec3ToJson(w.startPos),
                        ["endPos"]     = Vec3ToJson(w.endPos),
                        ["height"]     = w.height,
                        ["buildingId"] = w.buildingId,
                        ["openings"]   = openingsArr
                    });
                }
                data["walls"] = wallsArr;
                // WallBuildingSystem nie expose'uje getterów counter'ów — fallback do max+1 z restore.
                data["wallCounters"] = new JObject
                {
                    ["nextWallId"]     = ComputeMaxIdPlusOne(wallSys.AllWalls, w => w?.wallId ?? 0),
                    ["nextOpeningId"]  = ComputeMaxOpeningIdPlusOne(wallSys.AllWalls),
                    ["nextBuildingId"] = ComputeMaxIdPlusOne(wallSys.AllWalls, w => w?.buildingId ?? 0)
                };
            }
            else
            {
                Log.Info("[DepotSavable] WallBuildingSystem null — walls pominięte");
            }

            // ── Rooms (v2+) ────────────────────────────
            var roomSys = GetRoomSys();
            if (roomSys != null)
            {
                var roomsArr = new JArray();
                foreach (var r in roomSys.Rooms)
                {
                    if (r == null) continue;
                    var cellsArr = new JArray();
                    if (r.cells != null)
                    {
                        foreach (var c in r.cells)
                            cellsArr.Add(new JObject { ["x"] = c.x, ["y"] = c.y });
                    }
                    var doorCellsArr = new JArray();
                    if (r.doorCells != null)
                    {
                        foreach (var c in r.doorCells)
                            doorCellsArr.Add(new JObject { ["x"] = c.x, ["y"] = c.y });
                    }
                    roomsArr.Add(new JObject
                    {
                        ["roomId"]     = r.roomId,
                        ["roomType"]   = r.roomType.ToString(),
                        ["areaSqM"]    = r.areaSqM,
                        ["bounds"]     = new JObject
                        {
                            ["x"]      = r.bounds.x,
                            ["y"]      = r.bounds.y,
                            ["width"]  = r.bounds.width,
                            ["height"] = r.bounds.height
                        },
                        ["buildingId"] = r.buildingId,
                        ["cells"]      = cellsArr,
                        ["doorCells"]  = doorCellsArr,  // MF-6/MF-11 — w MF-6 zawsze pusta, MF-11 DoorPlacer dorzuca
                        ["level"]      = r.level        // MM-1 (v4+), domyślnie 1 dla nowych pokoi
                    });
                }
                data["rooms"] = roomsArr;
                // RoomDetectionSystem nie expose'uje getter'a — fallback do max+1 z restore.
                data["roomsCounter"] = new JObject
                {
                    ["nextRoomId"] = ComputeMaxIdPlusOne(roomSys.Rooms, r => r?.roomId ?? 0)
                };
            }
            else
            {
                Log.Info("[DepotSavable] RoomDetectionSystem null — rooms pominięte");
            }

            // ── Furniture (v3+) ────────────────────────
            // MF-9: PlacedFurnitureItem per-depot. FurniturePlacer może być null jeśli
            // gracz jeszcze nie wszedł w Furniture tool (lazy-spawn) — wtedy zapisujemy
            // pustą listę. Migracja v2→v3 = identity (DepotMigrator_v2_v3 niżej w pliku) —
            // stary save bez placedFurniture/doorCells dostaje puste listy gracefully
            // przez Deserialize. Identity zachowuje MigrationRunner chain consistency.
            var placer = GetFurniturePlacer();
            var furnitureArr = new JArray();
            int nextFurnitureId = 1;
            if (placer != null)
            {
                foreach (var inst in placer.PlacedInstances)
                {
                    if (inst == null) continue;
                    furnitureArr.Add(new JObject
                    {
                        ["instanceId"]         = inst.instanceId,
                        ["itemId"]             = inst.itemId,
                        ["depotId"]            = inst.depotId,
                        ["position"]           = Vec3ToJson(inst.position),
                        ["rotation"]           = inst.rotation,
                        ["assignedEmployeeId"] = inst.assignedEmployeeId
                    });
                }
                nextFurnitureId = placer.NextInstanceId;
            }
            data["placedFurniture"] = furnitureArr;
            data["furnitureCounter"] = new JObject
            {
                ["nextInstanceId"] = nextFurnitureId
            };

            // ── Outdoor equipment (v5+, MM-9) ──────────
            // PlacedOutdoorEquipment ma instanceId + type + cornerA/cornerB. Visualization
            // (visualObject) NIE jest serializowane — odtwarzane po Deserialize z presetu.
            var outdoorPlacer = OutdoorEquipmentPlacer.Instance;
            if (outdoorPlacer == null) outdoorPlacer = GetOutdoorPlacer();
            var outdoorArr = new JArray();
            if (outdoorPlacer != null)
            {
                foreach (var oe in outdoorPlacer.Placed)
                {
                    if (oe == null) continue;
                    outdoorArr.Add(new JObject
                    {
                        ["instanceId"] = oe.instanceId,
                        ["type"]       = oe.type.ToString(),
                        ["cornerA"]    = Vec3ToJson(oe.cornerA),
                        ["cornerB"]    = Vec3ToJson(oe.cornerB),
                    });
                }
            }
            data["placedOutdoorEquipment"] = outdoorArr;

            // ── Buildable area tier (Q1 pakiety kombinowane, 2026-05-17 interpretacja A) ──
            // currentTier (0..MAX_TIER=4). Każdy tier to pakiet length+width.
            // Tier 0 = 800×300 (start), Tier 4 = 2000×400 (max).
            // Expand kierunkowy E+S z NW corner fixed origin (góra=droga, lewa=external tracks).
            var groundGen = DepotServices.Get<GroundGenerator>();
            if (groundGen != null)
            {
                data["currentTier"] = groundGen.CurrentTier;
            }

            // ── TD-031: aktywne manewry zajezdni (resume po save/load) ──
            // Zapisujemy lekki rekord (cel + flagi) — polyline/prędkość re-derive'owane przy re-enqueue.
            // POMIJAMY taski serwisowe (onCompleted != null): callback nieserializowalny, te są domeną
            // MaintenanceJobsSavable. Pozycja składu i tak jest w occupancy grafu (tracksData).
            var moveSim = Object.FindAnyObjectByType<DepotMovementSimulator>();
            if (moveSim != null)
            {
                var moveTasks = new JArray();
                var seenConsists = new System.Collections.Generic.HashSet<int>();
                foreach (var t in moveSim.ActiveTasks)
                {
                    if (t == null || t.onCompleted != null) continue;
                    if (t.state != DepotMoveState.Moving && t.state != DepotMoveState.Queued &&
                        t.state != DepotMoveState.Pathfinding) continue;
                    if (!seenConsists.Add(t.consistId)) continue; // jeden resume per consist
                    var to = new JObject
                    {
                        ["consistId"] = t.consistId,
                        ["vehicleIds"] = JArray.FromObject(t.vehicleIds ?? new System.Collections.Generic.List<int>()),
                        ["toTrackId"] = t.toTrackId,
                        ["exitAfterComplete"] = t.exitAfterComplete,
                        ["entryOnComplete"] = t.entryOnComplete
                    };
                    if (t.targetWorldPos.HasValue)
                        to["targetWorldPos"] = Vec3ToJson(t.targetWorldPos.Value);
                    moveTasks.Add(to);
                }
                data["depotMoveTasks"] = moveTasks;
            }

            return data;
        }

        public void Deserialize(JObject data, int sourceVersion)
        {
            InvalidateSceneRefs(); // BUG-044: fresh fetch per call
            if (!CanApplyToCurrentDepotScene())
            {
                DeferDeserialize(data, sourceVersion);
                return;
            }

            DeserializeIntoScene(data, sourceVersion);
        }

        private void DeserializeIntoScene(JObject data, int sourceVersion)
        {
            // M-Economy Faza 5: wczytanie save'a odtwarza infrastrukturę (tory: RestoreVisualsFromGraph
            // → PlaceTrackVisuals; catenary: GenerateNetwork) ścieżkami budującymi — to NIE budowa gracza,
            // tylko restore „już opłaconego". Suppress, żeby load nie obciążał gracza kosztami budowy.
            bool prevSuppress = DepotSystem.ConstructionBilling.SuppressCharging;
            DepotSystem.ConstructionBilling.SuppressCharging = true;
            try { DeserializeIntoSceneImpl(data, sourceVersion); }
            finally { DepotSystem.ConstructionBilling.SuppressCharging = prevSuppress; }
        }

        private void DeserializeIntoSceneImpl(JObject data, int sourceVersion)
        {
            InvalidateSceneRefs(); // BUG-044: fresh fetch per call
            DepotSystem.Undo.UndoManager.ClearAll();
            DepotSystem.Undo.UndoManager.Silenced = false;

            // ── Buildable area tier (Q1 interpretacja A, 2026-05-17) ──────────────
            // Restore PRZED Track graph żeby ExternalTracks (jeśli generowane on demand)
            // miały poprawne bounds. Fallback do Tier 0 (start size 800×300) jeśli pole brak.
            var groundGen = DepotServices.Get<GroundGenerator>();
            if (groundGen != null)
            {
                int savedTier = data["currentTier"]?.Value<int>() ?? 0;
                groundGen.RestoreTier(savedTier);
            }

            // ── Track graph ────────────────────────────
            var graph = GetGraph();
            if (graph != null)
            {
                var trackCounters = data["trackCounters"] as JObject;
                graph.RestoreFromSave(
                    ParseArray<TrackNode>(data["trackNodes"] as JArray),
                    ParseArray<TrackEdge>(data["trackEdges"] as JArray),
                    ParseArray<DepotTrackData>(data["tracksData"] as JArray),
                    trackCounters?.Value<int?>("nextNodeId") ?? -1,
                    trackCounters?.Value<int?>("nextEdgeId") ?? -1,
                    trackCounters?.Value<int?>("nextTrackId") ?? -1);

                var trackBuilder = GetTrackBuilder();
                if (trackBuilder != null)
                {
                    trackBuilder.RestoreVisualsFromGraph(graph);
                    trackBuilder.RestoreTurnoutsFromSave(ParseArray<TurnoutEntitySnapshot>(data["turnoutEntities"] as JArray));
                }

                var movementSim = Object.FindAnyObjectByType<DepotMovementSimulator>();
                if (movementSim != null)
                {
                    movementSim.RestoreParkedVisualsFromGraph(graph);

                    // TD-031: wznów aktywne manewry (resume) — PO odtworzeniu dokładnych pozycji składów.
                    if (data["depotMoveTasks"] is JArray moveTasks)
                    {
                        foreach (var mt in moveTasks)
                        {
                            if (!(mt is JObject mo)) continue;
                            int cid = mo.Value<int?>("consistId") ?? -1;
                            if (cid < 0) continue;
                            var vids = new System.Collections.Generic.List<int>();
                            if (mo["vehicleIds"] is JArray va)
                                foreach (var x in va) vids.Add(x.Value<int>());
                            int toTrack = mo.Value<int?>("toTrackId") ?? -1;
                            Vector3? target = mo["targetWorldPos"] is JObject tp ? JsonToVec3(tp) : (Vector3?)null;
                            bool exitFlag = mo.Value<bool?>("exitAfterComplete") ?? false;
                            bool entryFlag = mo.Value<bool?>("entryOnComplete") ?? false;
                            movementSim.RestoreActiveMove(cid, vids, toTrack, target, exitFlag, entryFlag);
                        }
                    }
                }

                Log.Info($"[DepotSavable] Track graph restored: {graph.Nodes.Count} nodes, " +
                         $"{graph.Edges.Count} edges, {graph.Tracks.Count} tracks.");
            }
            else
            {
                Log.Warn("[DepotSavable] TrackGraph null podczas Deserialize — track data odłożone " +
                         "do momentu wejścia gracza w scenę Depot.");
            }

            var pathGraph = GetPathGraph();
            if (pathGraph != null)
            {
                var pathCounters = data["pathCounters"] as JObject;
                pathGraph.RestoreFromSave(
                    ParseArray<PathNode>(data["pathNodes"] as JArray),
                    ParseArray<PathEdge>(data["pathEdges"] as JArray),
                    pathCounters?.Value<int?>("nextNodeId") ?? -1,
                    pathCounters?.Value<int?>("nextEdgeId") ?? -1);
                Log.Info($"[DepotSavable] Path graph restored: {pathGraph.Nodes.Count} nodes, {pathGraph.Edges.Count} edges.");
            }

            var pathVisuals = GetPathVisualBuilder();
            if (pathVisuals != null)
                pathVisuals.RestoreFromSave(ParseArray<PathVisualSegmentSnapshot>(data["pathVisualSegments"] as JArray));

            var pathBuild = GetPathBuildStateMachine();
            if (pathBuild != null)
                pathBuild.RestorePlacedCellsFromSave(ParseArray<PathCellSnapshot>(data["pathCells"] as JArray));

            // ── Walls (v2+) ────────────────────────────
            var wallSys = GetWallSys();
            if (wallSys != null)
            {
                var wallsArr = data["walls"] as JArray;
                var wallsList = new List<WallSegment>();
                if (wallsArr != null)
                {
                    foreach (var item in wallsArr)
                    {
                        var w = item as JObject;
                        if (w == null) continue;
                        var seg = new WallSegment
                        {
                            wallId     = w.Value<int>("wallId"),
                            startPos   = JsonToVec3(w["startPos"] as JObject),
                            endPos     = JsonToVec3(w["endPos"] as JObject),
                            height     = w.Value<float?>("height") ?? 3f,
                            buildingId = w.Value<int?>("buildingId") ?? -1,
                            openings   = new List<WallOpening>()
                        };
                        var openingsArr = w["openings"] as JArray;
                        if (openingsArr != null)
                        {
                            foreach (var o in openingsArr)
                            {
                                var oj = o as JObject;
                                if (oj == null) continue;
                                System.Enum.TryParse<OpeningType>(oj.Value<string>("type") ?? "Door", out var typ);
                                seg.openings.Add(new WallOpening
                                {
                                    openingId      = oj.Value<int>("openingId"),
                                    type           = typ,
                                    distanceOnWall = oj.Value<float?>("distanceOnWall") ?? 0f
                                });
                            }
                        }
                        wallsList.Add(seg);
                    }
                }

                var counters = data["wallCounters"] as JObject;
                wallSys.RestoreFromSave(wallsList,
                    counters?.Value<int?>("nextWallId") ?? 1,
                    counters?.Value<int?>("nextOpeningId") ?? 1,
                    counters?.Value<int?>("nextBuildingId") ?? 1);

                Log.Info($"[DepotSavable] Walls restored: {wallSys.AllWalls.Count} segments. " +
                         "Mesh regeneration: pierwsza interakcja z systemem ścian (limitation).");
            }
            else
            {
                Log.Info("[DepotSavable] WallBuildingSystem null — walls skipped (nie w scenie Depot).");
            }

            // ── Rooms (v2+) ────────────────────────────
            var roomSys = GetRoomSys();
            if (roomSys != null)
            {
                var roomsArr = data["rooms"] as JArray;
                var roomsList = new List<DetectedRoom>();
                if (roomsArr != null)
                {
                    foreach (var item in roomsArr)
                    {
                        var r = item as JObject;
                        if (r == null) continue;
                        System.Enum.TryParse<RoomType>(r.Value<string>("roomType") ?? "None", out var rt);
                        var b = r["bounds"] as JObject;
                        var room = new DetectedRoom
                        {
                            roomId     = r.Value<int>("roomId"),
                            roomType   = rt,
                            areaSqM    = r.Value<float?>("areaSqM") ?? 0f,
                            bounds     = b == null ? new RectInt() : new RectInt(
                                b.Value<int?>("x") ?? 0,
                                b.Value<int?>("y") ?? 0,
                                b.Value<int?>("width") ?? 0,
                                b.Value<int?>("height") ?? 0),
                            buildingId = r.Value<int?>("buildingId") ?? -1,
                            cells      = new List<Vector2Int>(),
                            doorCells  = new List<Vector2Int>(),
                            // MM-1: brak field w starszych save'ach → fallback 1
                            level      = r.Value<int?>("level") ?? 1
                        };
                        var cellsArr = r["cells"] as JArray;
                        if (cellsArr != null)
                        {
                            foreach (var c in cellsArr)
                            {
                                var cj = c as JObject;
                                if (cj == null) continue;
                                room.cells.Add(new Vector2Int(
                                    cj.Value<int?>("x") ?? 0,
                                    cj.Value<int?>("y") ?? 0));
                            }
                        }
                        // MF-6/MF-11: doorCells (brak w starszych save'ach → pusta lista)
                        var doorCellsArr = r["doorCells"] as JArray;
                        if (doorCellsArr != null)
                        {
                            foreach (var c in doorCellsArr)
                            {
                                var cj = c as JObject;
                                if (cj == null) continue;
                                room.doorCells.Add(new Vector2Int(
                                    cj.Value<int?>("x") ?? 0,
                                    cj.Value<int?>("y") ?? 0));
                            }
                        }
                        roomsList.Add(room);
                    }
                }

                var counter = data["roomsCounter"] as JObject;
                roomSys.RestoreFromSave(roomsList, counter?.Value<int?>("nextRoomId") ?? 1);

                Log.Info($"[DepotSavable] Rooms restored: {roomSys.Rooms.Count} rooms.");
            }
            else
            {
                Log.Info("[DepotSavable] RoomDetectionSystem null — rooms skipped (nie w scenie Depot).");
            }

            // ── Furniture (v3+) ────────────────────────
            // MF-9: parse placedFurniture do listy + lazy-create FurniturePlacer jeśli null
            var furnitureArr = data["placedFurniture"] as JArray;
            int nextInstanceId = 1;
            var furnitureCounter = data["furnitureCounter"] as JObject;
            if (furnitureCounter != null)
                nextInstanceId = furnitureCounter.Value<int?>("nextInstanceId") ?? 1;

            var restored = new List<PlacedFurnitureItem>();
            if (furnitureArr != null)
            {
                foreach (var item in furnitureArr)
                {
                    var f = item as JObject;
                    if (f == null) continue;
                    restored.Add(new PlacedFurnitureItem
                    {
                        instanceId         = f.Value<int>("instanceId"),
                        itemId             = f.Value<string>("itemId") ?? "",
                        depotId            = f.Value<int?>("depotId") ?? -1,
                        position           = JsonToVec3(f["position"] as JObject),
                        rotation           = f.Value<int?>("rotation") ?? 0,
                        assignedEmployeeId = f.Value<int?>("assignedEmployeeId") ?? -1
                    });
                }
            }

            // FurniturePlacer może być null jeśli gracz jeszcze nie wszedł w Furniture tool.
            // Jeśli mamy items do restore, lazy-create. Jeśli pusta lista i brak placera → skip.
            //
            // Idempotency: FurniturePlacer.Instance jest set w Awake (singleton pattern); drugi
            // Deserialize w tej samej scenie znajdzie istniejący placer przez GetFurniturePlacer
            // (`FurniturePlacer.Instance ?? FindAnyObjectByType`). Defer pattern (load przed
            // depot scene loaded) tworzy nową instance DepotSavable z czystym cache, ale
            // Instance lookup nadal znajduje istniejący placer.
            //
            // FurnitureCatalog.LoadAll() raz na entry — wcześniej było wołane 2× (przed
            // AddComponent + przed RestoreFromSave), niepotrzebne.
            var placer = GetFurniturePlacer();

            // FurnitureCatalog.LoadAll raz dla całej operacji (poprzednio 2×).
            if ((restored.Count > 0 || placer != null) && !FurnitureCatalog.IsLoaded)
                FurnitureCatalog.LoadAll();

            if (placer == null && restored.Count > 0)
            {
                var go = new GameObject("FurniturePlacer (auto-created by DepotSavable.Deserialize)");
                placer = go.AddComponent<FurniturePlacer>();
            }
            if (placer != null)
            {
                placer.RestoreFromSave(restored, nextInstanceId);
                Log.Info($"[DepotSavable] Furniture restored: {restored.Count} instances, nextInstanceId={nextInstanceId}");
            }
            else if (restored.Count == 0)
            {
                Log.Info("[DepotSavable] No furniture to restore (empty list).");
            }

            // ── Outdoor equipment (v5+, MM-9) ──────────
            // PlacedOutdoorEquipment z cornerA/cornerB. Visualization spawn'uje OutdoorEquipmentPlacer
            // przez RestoreFromSave (analog do FurniturePlacer). Stary save (v4 i niższe) bez
            // placedOutdoorEquipment → empty list gracefully.
            var outdoorArr = data["placedOutdoorEquipment"] as JArray;
            if (outdoorArr != null && outdoorArr.Count > 0)
            {
                var outdoorPlacer = OutdoorEquipmentPlacer.Instance;
                if (outdoorPlacer == null) outdoorPlacer = GetOutdoorPlacer();
                if (outdoorPlacer != null)
                {
                    var restoredOutdoor = new List<PlacedOutdoorEquipment>();
                    foreach (var item in outdoorArr)
                    {
                        var oj = item as JObject;
                        if (oj == null) continue;
                        if (!System.Enum.TryParse<OutdoorEquipmentType>(
                                oj.Value<string>("type") ?? "WashZone", out var oeType)) continue;
                        var oe = new PlacedOutdoorEquipment
                        {
                            instanceId = oj.Value<int?>("instanceId") ?? 0,
                            type = oeType,
                            cornerA = JsonToVec3(oj["cornerA"] as JObject),
                            cornerB = JsonToVec3(oj["cornerB"] as JObject),
                        };
                        restoredOutdoor.Add(oe);
                    }
                    outdoorPlacer.RestoreFromSave(restoredOutdoor);
                    Log.Info($"[DepotSavable] Outdoor equipment restored: {restoredOutdoor.Count} instances.");
                }
            }
            else
            {
                var outdoorPlacer = OutdoorEquipmentPlacer.Instance;
                if (outdoorPlacer == null) outdoorPlacer = GetOutdoorPlacer();
                if (outdoorPlacer != null)
                {
                    outdoorPlacer.RestoreFromSave(new List<PlacedOutdoorEquipment>());
                    Log.Info("[DepotSavable] Outdoor equipment restored: 0 instances.");
                }
            }

            // ── Catenary regen ─────────────────────────
            // Sieć trakcyjna 3D regenerowana z TrackEdge.HasCatenary (już w trackEdges).
            // Trigger tylko jeśli jesteśmy w scenie Depot (CatenaryGenerator istnieje).
            var catGen = GetCatGen();
            if (catGen != null && graph != null)
            {
                try
                {
                    catGen.GenerateNetwork();
                    Log.Info("[DepotSavable] Catenary regenerated from electrified tracks.");
                }
                catch (System.Exception e)
                {
                    Log.Warn($"[DepotSavable] CatenaryGenerator.GenerateNetwork threw: {e.Message}. " +
                             "Sieć trakcyjna może nie być widoczna do następnego buildu (limitation).");
                }
            }
        }

        public void InitializeDefault()
        {
            s_pendingDepotData = null;
            InvalidateSceneRefs(); // BUG-044: fresh fetch per call
            DepotSystem.Undo.UndoManager.ClearAll();
            DepotSystem.Undo.UndoManager.Silenced = false;

            GetGraph()?.ClearAllForReset();
            GetTrackBuilder()?.ClearAll();
            GetPathGraph()?.ClearAllForReset();
            GetPathVisualBuilder()?.ClearAll();
            GetPathBuildStateMachine()?.RestorePlacedCellsFromSave(new List<PathCellSnapshot>());
            GetWallSys()?.ClearAllForReset();
            GetRoomSys()?.ClearAllForReset();
            GetFurniturePlacer()?.ClearAllPlaced();
            GetOutdoorPlacer()?.RestoreFromSave(new List<PlacedOutdoorEquipment>());

            // Reset buildable area do Tier 0 / start size (Q1 2026-05-17 interpretacja A)
            var groundGen = DepotServices.Get<GroundGenerator>();
            groundGen?.RestoreTier(0);
        }

        // ── Reflection helpers ─────────────────────────

        private static JObject s_pendingDepotData;
        private static int s_pendingSourceVersion;
        private static bool s_pendingRestoreHooked;

        private bool CanApplyToCurrentDepotScene()
        {
            var depotScene = SceneManager.GetSceneByName("Depot");
            return depotScene.IsValid() && depotScene.isLoaded && GetGraph() != null;
        }

        private void DeferDeserialize(JObject data, int sourceVersion)
        {
            s_pendingDepotData = data?.DeepClone() as JObject ?? new JObject();
            s_pendingSourceVersion = sourceVersion;
            EnsurePendingRestoreHook();
            Log.Info("[DepotSavable] Depot scene not ready; deferred depot_3d restore until Depot scene is loaded.");
        }

        private static void EnsurePendingRestoreHook()
        {
            if (s_pendingRestoreHooked) return;
            SceneManager.sceneLoaded += OnSceneLoadedForPendingRestore;
            s_pendingRestoreHooked = true;
        }

        private static void OnSceneLoadedForPendingRestore(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Depot" || s_pendingDepotData == null) return;
            var go = new GameObject("DepotSavableDeferredRestore");
            go.AddComponent<DepotSavableDeferredRestore>();
        }

        internal static void ApplyPendingRestoreIfReady()
        {
            if (s_pendingDepotData == null) return;

            var savable = new DepotSavable();
            if (!savable.CanApplyToCurrentDepotScene())
            {
                EnsurePendingRestoreHook();
                Log.Warn("[DepotSavable] Pending depot_3d restore still waiting for Depot systems.");
                return;
            }

            var data = s_pendingDepotData;
            int version = s_pendingSourceVersion;
            s_pendingDepotData = null;
            SceneManager.sceneLoaded -= OnSceneLoadedForPendingRestore;
            s_pendingRestoreHooked = false;

            savable.DeserializeIntoScene(data, version);
            Log.Info("[DepotSavable] Applied deferred depot_3d restore.");
        }

        private static int ComputeNextId(IEnumerable<int> existingIds)
        {
            int next = 0;
            if (existingIds == null) return next;
            foreach (int id in existingIds)
                if (id >= next) next = id + 1;
            return next;
        }

        private static int ComputeMaxIdPlusOne<T>(IEnumerable<T> items, System.Func<T, int> idSelector)
        {
            int max = 0;
            if (items == null) return 1;
            foreach (var item in items)
            {
                int id = idSelector(item);
                if (id > max) max = id;
            }
            return max + 1;
        }

        private static int ComputeMaxOpeningIdPlusOne(IEnumerable<WallSegment> walls)
        {
            int max = 0;
            if (walls == null) return 1;
            foreach (var w in walls)
            {
                if (w?.openings == null) continue;
                foreach (var o in w.openings)
                    if (o != null && o.openingId > max) max = o.openingId;
            }
            return max + 1;
        }

        private static List<T> ParseArray<T>(JArray arr) where T : class
        {
            var result = new List<T>();
            if (arr == null) return result;
            foreach (var item in arr)
            {
                var parsed = item.ToObject<T>();
                if (parsed != null) result.Add(parsed);
            }
            return result;
        }

        // ── Vector3 ↔ JObject (manual żeby ominąć GameObject ref serialization) ──

        private static JObject Vec3ToJson(Vector3 v) => new JObject
        {
            ["x"] = v.x, ["y"] = v.y, ["z"] = v.z
        };

        private static Vector3 JsonToVec3(JObject j) => j == null ? Vector3.zero : new Vector3(
            j.Value<float?>("x") ?? 0f,
            j.Value<float?>("y") ?? 0f,
            j.Value<float?>("z") ?? 0f);
    }

    internal sealed class DepotSavableDeferredRestore : MonoBehaviour
    {
        private System.Collections.IEnumerator Start()
        {
            yield return null;
            DepotSavable.ApplyPendingRestoreIfReady();
            Destroy(gameObject);
        }
    }

    public static class DepotSavableBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SaveRegistry.Register(new DepotSavable());
        }
    }
}
