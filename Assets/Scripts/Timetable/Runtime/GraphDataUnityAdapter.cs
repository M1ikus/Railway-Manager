using System.Collections.Generic;
using UnityEngine;
using formap;
using RailwayManager.Core;
using RailwayManager.GraphData;

// Aliasy żeby uniknąć kolizji nazw między Unity-side (RailwayManager.Timetable.X)
// a library-side (RailwayManager.GraphData.GraphX). Library types mają prefix "Lib".
using LibInitState = RailwayManager.GraphData.InitState;
using LibInitStateReader = RailwayManager.GraphData.InitStateReader;
using LibGraphLogger = RailwayManager.GraphData.GraphLogger;
using LibGraphPathfindingGraph = RailwayManager.GraphData.GraphPathfindingGraph;
using LibGraphAdminRegion = RailwayManager.GraphData.GraphAdminRegion;
using LibGraphCityPlace = RailwayManager.GraphData.GraphCityPlace;
using LibGraphPlaceType = RailwayManager.GraphData.GraphPlaceType;
using LibGraphRailwayStation = RailwayManager.GraphData.GraphRailwayStation;
using LibGraphStationPlatform = RailwayManager.GraphData.GraphStationPlatform;
using LibGraphSignalInfo = RailwayManager.GraphData.GraphSignalInfo;
using LibGraphSignalFunction = RailwayManager.GraphData.GraphSignalFunction;
using LibGraphSignalDirection = RailwayManager.GraphData.GraphSignalDirection;
using LibGraphBoundaryType = RailwayManager.GraphData.GraphBoundaryType;
using LibGraphBlockSection = RailwayManager.GraphData.GraphBlockSection;
using LibGraphBlockSectionBuilder = RailwayManager.GraphData.GraphBlockSectionBuilder;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// M-PL B2: most między pre-built init-state-{country}.bin (formap) a Unity runtime types.
    ///
    /// Pipeline:
    /// 1. <see cref="LibInitStateReader.Read"/> deserializuje binarkę do bibliotekowych typów
    ///    (GraphPoint, GraphPathfindingGraph, GraphAdminRegion, GraphRailwayStation, ...).
    /// 2. Adapter konwertuje na Unity-side types (Vector2, PathfindingGraph, AdminRegion,
    ///    RailwayStation, StationPlatform, CityPlace, SignalInfo, BlockSectionBuilder.BuildResult).
    ///
    /// Performance: pełna PL = 612k nodes + 1.2M edges + 4k regions + ~3k stations + ~10k platforms.
    /// Konwersja czysto in-memory, deserializacja binarki dominuje koszt (~5-10s spodziewane).
    /// </summary>
    public static class GraphDataUnityAdapter
    {
        /// <summary>Bundle wszystkich danych po loadzie — wstrzykiwany do TimetableInitializer.</summary>
        public class LoadResult
        {
            public PathfindingGraph Graph;
            public List<AdminRegion> AdminRegions;
            public List<CityPlace> Places;
            public List<RailwayStation> Stations;
            public List<StationPlatform> Platforms;
            public List<SignalInfo> Signals;
            public BlockSectionBuilder.BuildResult BlockSections;
            public List<List<Vector2>> Coastlines;  // OSM natural=coastline lines for SyntheticWaterRenderer
            public string CountryCode;
            public long SourceMapMtime;
        }

        /// <summary>
        /// Sprawdza czy plik istnieje i ma valid magic + version + countryCode + nie-stale source mtime.
        /// Quick check bez pełnego load — używane przed Initialize żeby zdecydować pre-built vs runtime build.
        /// </summary>
        /// <param name="initStatePath">Ścieżka do init-state-{country}.bin.</param>
        /// <param name="expectedCountryCode">Oczekiwany country code (case-sensitive jak w pliku).</param>
        /// <param name="sourceMapPath">Ścieżka do source mapy (poland-v8.bin) — file mtime musi być &lt;= mtime z headera.
        /// Jeśli null/nie-istnieje, pomija check mtime.</param>
        public static bool IsInitStateValid(string initStatePath, string expectedCountryCode, string sourceMapPath)
        {
            EnsureLoggerWired();

            if (!System.IO.File.Exists(initStatePath))
                return false;

            long sourceMtime = 0;
            if (!string.IsNullOrEmpty(sourceMapPath) && System.IO.File.Exists(sourceMapPath))
                sourceMtime = new System.IO.FileInfo(sourceMapPath).LastWriteTimeUtc.ToFileTimeUtc();

            return LibInitStateReader.IsValidFor(initStatePath, expectedCountryCode, sourceMtime);
        }

        /// <summary>
        /// Pełny load init-state.bin → konwersja na Unity types. Throw na corruption.
        /// </summary>
        public static LoadResult Load(string initStatePath)
        {
            EnsureLoggerWired();

            float t0 = Time.realtimeSinceStartup;
            Log.Info($"[GraphDataUnityAdapter] Loading {initStatePath}...");

            LibInitState state;
            try
            {
                state = LibInitStateReader.Read(initStatePath);
            }
            catch (System.Exception ex)
            {
                Log.Warn($"[GraphDataUnityAdapter] Read failed: {ex.Message}");
                throw;
            }

            float tRead = Time.realtimeSinceStartup - t0;
            Log.Info($"[GraphDataUnityAdapter] Binary read in {tRead:F2}s — converting to Unity types...");

            float tConvStart = Time.realtimeSinceStartup;
            var result = new LoadResult
            {
                CountryCode = state.Header.CountryCode,
                SourceMapMtime = state.Header.SourceMapMtime,
                Graph = ConvertGraph(state.PathfindingGraph),
                AdminRegions = ConvertAdminRegions(state.AdminRegions),
                Places = ConvertPlaces(state.Places),
                Stations = ConvertStations(state.Stations),
                Platforms = ConvertPlatforms(state.Platforms),
                Signals = ConvertSignals(state.Signals),
                BlockSections = ConvertBlockSections(state.BlockSections),
                Coastlines = ConvertCoastlines(state.Coastlines)
            };
            float tConv = Time.realtimeSinceStartup - tConvStart;

            Log.Info($"[GraphDataUnityAdapter] Conversion in {tConv:F2}s — total load: {Time.realtimeSinceStartup - t0:F2}s");
            Log.Info($"[GraphDataUnityAdapter]   country={result.CountryCode}, "
                     + $"{result.Graph.NodeCount} nodes, {result.Graph.EdgeCount} edges, "
                     + $"{result.AdminRegions.Count} regions, {result.Places.Count} places, "
                     + $"{result.Stations.Count} stations, {result.Platforms.Count} platforms, "
                     + $"{result.Signals.Count} signals, {(result.BlockSections.sections?.Count ?? 0)} block sections");
            return result;
        }

        // ─────────────────────────────────────────────
        //  Per-type converters
        // ─────────────────────────────────────────────

        private static PathfindingGraph ConvertGraph(LibGraphPathfindingGraph libGraph)
        {
            var graph = new PathfindingGraph();
            if (libGraph == null)
            {
                graph.LoadFromSerializedData(1f, null, null, null);
                return graph;
            }

            var nodes = new List<PathfindingGraph.Node>(libGraph.NodeCount);
            for (int i = 0; i < libGraph.NodeCount; i++)
            {
                var ln = libGraph.Nodes[i];
                nodes.Add(new PathfindingGraph.Node
                {
                    id = ln.Id,
                    position = new Vector2(ln.Position.X, ln.Position.Y),
                    edgeIds = ln.EdgeIds != null ? new List<int>(ln.EdgeIds) : new List<int>(4)
                });
            }

            var edges = new List<PathfindingGraph.Edge>(libGraph.EdgeCount);
            for (int i = 0; i < libGraph.EdgeCount; i++)
            {
                var le = libGraph.Edges[i];
                edges.Add(new PathfindingGraph.Edge
                {
                    id = le.Id,
                    fromNodeId = le.FromNodeId,
                    toNodeId = le.ToNodeId,
                    segmentId = le.SegmentId,
                    lengthM = le.LengthM,
                    maxSpeedKmh = le.MaxSpeedKmh,
                    metadata = le.Metadata, // Dictionary<string,string> — same shape, can passthrough
                    geometry = null,        // MVP: brak geometrii (Step 4 w lib też skip)
                    isOsmForward = le.IsOsmForward
                });
            }

            var junctions = libGraph.JunctionNodeIds != null
                ? new HashSet<int>(libGraph.JunctionNodeIds)
                : new HashSet<int>();

            graph.LoadFromSerializedData(libGraph.CellSize, nodes, edges, junctions);
            return graph;
        }

        private static List<AdminRegion> ConvertAdminRegions(List<LibGraphAdminRegion> libRegions)
        {
            var result = new List<AdminRegion>(libRegions != null ? libRegions.Count : 0);
            if (libRegions == null) return result;

            // Summary only — verbose per-region log był diagnostyczny, już nie potrzebny
            int countries = 0, voivodeships = 0;
            foreach (var lr in libRegions)
            {
                if (lr.AdminLevel == 2) countries++;
                else if (lr.AdminLevel == 4) voivodeships++;
            }
            Log.Info($"[GraphDataUnityAdapter] AdminRegions: {libRegions.Count} total ({countries} countries, {voivodeships} voivodeships)");

            foreach (var lr in libRegions)
            {
                var bbox = new BBox
                {
                    MinX = lr.BoundingBox.MinX,
                    MinY = lr.BoundingBox.MinY,
                    MaxX = lr.BoundingBox.MaxX,
                    MaxY = lr.BoundingBox.MaxY
                };

                var verts = new List<Vector2>(lr.Vertices != null ? lr.Vertices.Count : 0);
                if (lr.Vertices != null)
                    foreach (var v in lr.Vertices) verts.Add(new Vector2(v.X, v.Y));

                var indices = lr.Indices != null ? new List<int>(lr.Indices) : new List<int>();

                result.Add(new AdminRegion
                {
                    name = lr.Name,
                    adminLevel = lr.AdminLevel,
                    iso3166_1 = lr.Iso3166_1,
                    iso3166_2 = lr.Iso3166_2,
                    boundingBox = bbox,
                    vertices = verts,
                    indices = indices
                });
            }
            return result;
        }

        private static List<CityPlace> ConvertPlaces(List<LibGraphCityPlace> libPlaces)
        {
            var result = new List<CityPlace>(libPlaces != null ? libPlaces.Count : 0);
            if (libPlaces == null) return result;

            foreach (var lp in libPlaces)
            {
                result.Add(new CityPlace
                {
                    name = lp.Name,
                    position = new Vector2(lp.Position.X, lp.Position.Y),
                    type = ConvertPlaceType(lp.Type),
                    population = lp.Population,
                    voivodeship = lp.Voivodeship
                });
            }
            return result;
        }

        private static PlaceType ConvertPlaceType(LibGraphPlaceType lt)
        {
            switch (lt)
            {
                case LibGraphPlaceType.City:    return PlaceType.City;
                case LibGraphPlaceType.Town:    return PlaceType.Town;
                case LibGraphPlaceType.Village: return PlaceType.Village;
                default: return PlaceType.Village;
            }
        }

        private static List<RailwayStation> ConvertStations(List<LibGraphRailwayStation> libStations)
        {
            var result = new List<RailwayStation>(libStations != null ? libStations.Count : 0);
            if (libStations == null) return result;

            foreach (var ls in libStations)
            {
                result.Add(new RailwayStation
                {
                    stationId = ls.StationId,
                    name = ls.Name,
                    position = new Vector2(ls.Position.X, ls.Position.Y),
                    isMajorStation = ls.IsMajorStation,
                    pathNodeId = ls.PathNodeId,
                    voivodeship = ls.Voivodeship,
                    cityName = ls.CityName
                });
            }
            return result;
        }

        private static List<StationPlatform> ConvertPlatforms(List<LibGraphStationPlatform> libPlatforms)
        {
            var result = new List<StationPlatform>(libPlatforms != null ? libPlatforms.Count : 0);
            if (libPlatforms == null) return result;

            foreach (var lp in libPlatforms)
            {
                result.Add(new StationPlatform
                {
                    platformId = lp.PlatformId,
                    stationNodeId = lp.StationNodeId,
                    position = new Vector2(lp.Position.X, lp.Position.Y),
                    platformName = lp.PlatformName,
                    trackRef = lp.TrackRef,
                    lengthM = lp.LengthM
                });
            }
            return result;
        }

        private static List<SignalInfo> ConvertSignals(List<LibGraphSignalInfo> libSignals)
        {
            var result = new List<SignalInfo>(libSignals != null ? libSignals.Count : 0);
            if (libSignals == null) return result;

            foreach (var ls in libSignals)
            {
                result.Add(new SignalInfo
                {
                    nodeId = ls.NodeId,
                    function = ConvertSignalFunction(ls.Function),
                    direction = ConvertSignalDirection(ls.Direction),
                    refNum = ls.RefNum
                });
            }
            return result;
        }

        private static SignalFunction ConvertSignalFunction(LibGraphSignalFunction lf)
        {
            switch (lf)
            {
                case LibGraphSignalFunction.Entry:        return SignalFunction.Entry;
                case LibGraphSignalFunction.Exit:         return SignalFunction.Exit;
                case LibGraphSignalFunction.Block:        return SignalFunction.Block;
                case LibGraphSignalFunction.Intermediate: return SignalFunction.Intermediate;
                default: return SignalFunction.Unknown;
            }
        }

        private static SignalDirection ConvertSignalDirection(LibGraphSignalDirection ld)
        {
            switch (ld)
            {
                case LibGraphSignalDirection.Forward:  return SignalDirection.Forward;
                case LibGraphSignalDirection.Backward: return SignalDirection.Backward;
                default: return SignalDirection.Both;
            }
        }

        private static BlockSectionBuilder.BuildResult ConvertBlockSections(LibGraphBlockSectionBuilder.BuildResult libBs)
        {
            var result = new BlockSectionBuilder.BuildResult
            {
                sections = new List<BlockSection>(libBs.Sections != null ? libBs.Sections.Count : 0),
                edgeToSection = libBs.EdgeToSection ?? new int[0]
            };

            if (libBs.Sections != null)
            {
                foreach (var s in libBs.Sections)
                {
                    result.sections.Add(new BlockSection
                    {
                        id = s.Id,
                        startNodeId = s.StartNodeId,
                        endNodeId = s.EndNodeId,
                        lengthM = s.LengthM,
                        maxSpeedKmh = s.MaxSpeedKmh,
                        edgeCount = s.EdgeCount,
                        startBoundary = ConvertBoundaryType(s.StartBoundary),
                        endBoundary = ConvertBoundaryType(s.EndBoundary)
                    });
                }
            }
            return result;
        }

        private static BoundaryType ConvertBoundaryType(LibGraphBoundaryType lt)
        {
            switch (lt)
            {
                case LibGraphBoundaryType.Junction: return BoundaryType.Junction;
                case LibGraphBoundaryType.Signal:   return BoundaryType.Signal;
                case LibGraphBoundaryType.LineEnd:  return BoundaryType.LineEnd;
                case LibGraphBoundaryType.Station:  return BoundaryType.Station;
                default: return BoundaryType.Junction;
            }
        }

        private static List<List<Vector2>> ConvertCoastlines(List<List<RailwayManager.GraphData.GraphPoint>> libCoastlines)
        {
            var result = new List<List<Vector2>>(libCoastlines != null ? libCoastlines.Count : 0);
            if (libCoastlines == null) return result;
            foreach (var line in libCoastlines)
            {
                if (line == null) continue;
                var unityLine = new List<Vector2>(line.Count);
                foreach (var p in line) unityLine.Add(new Vector2(p.X, p.Y));
                result.Add(unityLine);
            }
            return result;
        }

        // ─────────────────────────────────────────────
        //  Library logger wiring
        // ─────────────────────────────────────────────

        private static bool _loggerWired;
        private static void EnsureLoggerWired()
        {
            if (_loggerWired) return;
            LibGraphLogger.Info  ??= msg => Log.Info(msg);
            LibGraphLogger.Warn  ??= msg => Log.Warn(msg);
            LibGraphLogger.Error ??= msg => Log.Warn("ERROR: " + msg);
            _loggerWired = true;
        }
    }
}
