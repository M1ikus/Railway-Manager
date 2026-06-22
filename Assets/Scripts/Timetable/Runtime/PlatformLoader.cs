using System.Collections.Generic;
using UnityEngine;
using formap;
using MapSystem;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Streaming processor — akumuluje peron centroids per tile, deduplikuje po centroid pos.
    /// Snap do stacji w <see cref="Finalize"/> po graph build complete.
    /// </summary>
    public class PlatformStreamProcessor
    {
        private readonly List<(Vector2 centroid, float lengthM, string platformName, string trackRef)> _raw = new();
        private readonly HashSet<string> _seen = new();

        public void OnTile(Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> layers)
        {
            if (!layers.TryGetValue(BinaryFormat.LayerType.Platforms, out var features)) return;
            foreach (var feature in features)
            {
                if (feature?.Vertices == null || feature.Vertices.Count == 0) continue;

                Vector2 centroid = ComputeCentroidStatic(feature.Vertices);
                string key = $"{centroid.x:F0}|{centroid.y:F0}";
                if (!_seen.Add(key)) continue;

                feature.Metadata.TryGetValue("ref", out var platformName);
                feature.Metadata.TryGetValue("railway:track_ref", out var trackRef);

                float lengthM = ComputePerimeterStatic(feature.Vertices) * 0.5f;
                _raw.Add((centroid, lengthM, platformName ?? "?", trackRef ?? ""));
            }
        }

        public List<StationPlatform> Finalize(PathfindingGraph pathGraph, float maxStationDistanceM = 500f)
        {
            var result = new List<StationPlatform>(_raw.Count);
            int nextId = 1;
            int unmatched = 0;

            foreach (var (centroid, lengthM, platformName, trackRef) in _raw)
            {
                int nearestNode = pathGraph.FindNearestNode(centroid, maxStationDistanceM);
                if (nearestNode < 0) { unmatched++; continue; }

                result.Add(new StationPlatform
                {
                    platformId = nextId++,
                    stationNodeId = nearestNode,
                    position = centroid,
                    platformName = platformName,
                    trackRef = trackRef,
                    lengthM = lengthM
                });
            }

            Log.Info($"[PlatformStreamProcessor] Loaded {result.Count} platforms ({unmatched} unmatched)");
            return result;
        }

        private static Vector2 ComputeCentroidStatic(List<Vector2> points)
        {
            Vector2 sum = Vector2.zero;
            foreach (var p in points) sum += p;
            return sum / points.Count;
        }

        private static float ComputePerimeterStatic(List<Vector2> points)
        {
            if (points.Count < 2) return 0f;
            float total = 0f;
            for (int i = 1; i < points.Count; i++)
                total += Vector2.Distance(points[i - 1], points[i]);
            return total;
        }
    }

    /// <summary>
    /// Ładuje perony z warstwy Platforms pliku .bin (railway=platform z OSM).
    /// Każdy peron jest mapowany na najbliższą stację w PathfindingGraph po odległości.
    /// Docelowo w M9 można wymienić heurystykę odległościową na OSM public_transport=stop_area relations.
    /// </summary>
    public static class PlatformLoader
    {
        /// <summary>
        /// Ładuje perony z warstwy Platforms i przypisuje każdy do najbliższego węzła pathfindingu
        /// reprezentującego stację (max promień = maxStationDistanceM, domyślnie 500 m).
        /// </summary>
        public static List<StationPlatform> LoadFrom(
            MapLoader mapLoader,
            PathfindingGraph pathGraph,
            float maxStationDistanceM = 500f)
        {
            var result = new List<StationPlatform>();
            if (mapLoader == null || pathGraph == null)
            {
                Log.Warn("[PlatformLoader] Map loader or path graph is null");
                return result;
            }

            var platformFeatures = mapLoader.GetAllFeaturesAcrossTiles(BinaryFormat.LayerType.Platforms);
            Log.Info($"[PlatformLoader] GetAllFeaturesAcrossTiles returned {platformFeatures?.Count ?? 0} Platform feature copies");
            if (platformFeatures == null || platformFeatures.Count == 0)
            {
                Log.Info("[PlatformLoader] No platforms in current map (tiled map may need all tiles loaded)");
                return result;
            }

            int nextId = 1;
            int unmatched = 0;
            var seen = new HashSet<string>();

            foreach (var feature in platformFeatures)
            {
                if (feature.Vertices == null || feature.Vertices.Count == 0) continue;

                Vector2 centroid = ComputeCentroid(feature.Vertices);

                // Dedupe: peron może pojawić się w kilku tile (polygon spans boundaries)
                string key = $"{centroid.x:F0}|{centroid.y:F0}";
                if (!seen.Add(key)) continue;
                int nearestNode = pathGraph.FindNearestNode(centroid, maxStationDistanceM);
                if (nearestNode < 0)
                {
                    unmatched++;
                    continue;
                }

                feature.Metadata.TryGetValue("ref", out var platformName);
                feature.Metadata.TryGetValue("railway:track_ref", out var trackRef);

                float lengthM = ComputePerimeter(feature.Vertices) * 0.5f; // pół obwodu ≈ długość peronu

                result.Add(new StationPlatform
                {
                    platformId = nextId++,
                    stationNodeId = nearestNode,
                    position = centroid,
                    platformName = platformName ?? "?",
                    trackRef = trackRef ?? "",
                    lengthM = lengthM
                });
            }

            Log.Info($"[PlatformLoader] Loaded {result.Count} platforms ({unmatched} unmatched to stations)");
            return result;
        }

        private static Vector2 ComputeCentroid(List<Vector2> points)
        {
            Vector2 sum = Vector2.zero;
            foreach (var p in points) sum += p;
            return sum / points.Count;
        }

        private static float ComputePerimeter(List<Vector2> points)
        {
            if (points.Count < 2) return 0f;
            float total = 0f;
            for (int i = 1; i < points.Count; i++)
                total += Vector2.Distance(points[i - 1], points[i]);
            // Zamknij polygon
            total += Vector2.Distance(points[points.Count - 1], points[0]);
            return total;
        }
    }
}
