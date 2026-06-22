using System.Collections.Generic;
using formap;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Streaming processor — kolekcjuje WSZYSTKIE Railway features per tile (deep copy).
    /// RailwayGraph.BuildGraph + PathfindingGraph.BuildFromFeaturesUnionFind wymagają
    /// wszystkich features na raz (graph topology + Union-Find merge), więc nie da się
    /// incremental. Memory cost: 122k features × ~2 KB avg = ~250 MB peak.
    ///
    /// Deep copy każdego MeshGeometry żeby Tile cache mógł być odrzucony bezpiecznie
    /// po przetworzeniu (no shared references).
    /// </summary>
    public class RailwayFeatureCollector
    {
        private readonly List<MeshGeometry> _features = new();

        private int _filteredOut;

        public void OnTile(Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> layers)
        {
            if (!layers.TryGetValue(BinaryFormat.LayerType.Railways, out var tileFeatures)) return;

            foreach (var src in tileFeatures)
            {
                if (src == null) continue;
                if (!IsMainlineRail(src))
                {
                    _filteredOut++;
                    continue;
                }
                _features.Add(DeepCopy(src));
            }
        }

        public List<MeshGeometry> Finalize()
        {
            Log.Info($"[RailwayFeatureCollector] Collected {_features.Count} mainline railways, filtered {_filteredOut} sidings/tram/narrow_gauge");
            return _features;
        }

        /// <summary>
        /// Filter — zachowaj tylko główne tory (railway=rail, not sidings/yards/tram/narrow_gauge).
        /// Redukuje dataset o ~30-50% → szybszy PathfindingGraph build.
        /// </summary>
        private static bool IsMainlineRail(MeshGeometry feature)
        {
            if (feature.Metadata == null) return true;

            if (feature.Metadata.TryGetValue("railway", out var railway))
            {
                // Skip non-main rail types
                if (railway == "tram" || railway == "narrow_gauge" || railway == "light_rail"
                 || railway == "monorail" || railway == "subway" || railway == "construction"
                 || railway == "abandoned" || railway == "disused" || railway == "preserved"
                 || railway == "miniature" || railway == "funicular")
                    return false;
            }

            if (feature.Metadata.TryGetValue("service", out var service))
            {
                // Skip yard infrastructure (large dataset, marginal for inter-city pathfinding)
                if (service == "yard" || service == "spur" || service == "crossover")
                    return false;
            }

            // Skip usage=industrial (dedicated industrial sidings)
            if (feature.Metadata.TryGetValue("usage", out var usage))
            {
                if (usage == "industrial" || usage == "tourism" || usage == "military")
                    return false;
            }

            return true;
        }

        private static MeshGeometry DeepCopy(MeshGeometry src)
        {
            var copy = new MeshGeometry
            {
                BoundingBox = src.BoundingBox,
                Vertices = new List<UnityEngine.Vector2>(src.Vertices),
                Indices = new List<int>(src.Indices),
                HoleStarts = new List<int>(src.HoleStarts),
                SegmentIds = new List<int>(src.SegmentIds),
                JunctionIndices = new List<int>(src.JunctionIndices)
            };
            // Metadata — shallow copy strings (immutable in C#, safe to share refs)
            foreach (var kvp in src.Metadata)
                copy.Metadata[kvp.Key] = kvp.Value;
            return copy;
        }
    }
}
