using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// M-TimetableUX F1.6 polish: detect stations w pobliżu route ale NIE na path'cie
    /// (topologically off-branch). Foundation dla ghost markers UI (semi-transparent station
    /// markers na mapie z optional click-to-add-as-waypoint flow).
    ///
    /// **Pre-F1.6 polish detection-only:** logic returns list. UI rendering (visual semi-transparent
    /// markers + click handler) deferred do post-EA visual polish (wymaga RoutePreviewOverlay
    /// extension lub MapOverlayService MarkerRenderer).
    ///
    /// **Distinction vs F1.6 main:**
    /// - F1.6 main (commit 505c34e): refactor `FindStationsPerSegment` z spatial → topological.
    ///   Off-path stations są pomijane — TD-019 fix.
    /// - F1.6 polish (this file): detect off-path stations near route → list. Gracz może
    ///   opcjonalnie dodać jako explicit waypoint przez F1.3 multi-stage UX.
    /// </summary>
    public static class OffPathStationsDetector
    {
        /// <summary>Default radius (m) — stacja musi być w tym promieniu od najbliższego path node żeby liczyła się jako "off-path near route".</summary>
        public const float DefaultDetectionRadiusM = 1000f;

        /// <summary>
        /// Zwraca listę stacji które są geograficznie w pobliżu route (radius m) ale topologically
        /// NIE są na path'cie (station.pathNodeId nie jest w route.nodeIds).
        /// </summary>
        public static List<RailwayStation> GetOffPathStations(
            Route route,
            TimetableInitializer init,
            float radiusM = DefaultDetectionRadiusM)
        {
            var result = new List<RailwayStation>();
            if (route?.nodeIds == null || init?.Stations == null || init.Graph == null)
                return result;

            // Build set on-path nodeIds dla O(1) lookup
            var onPathNodes = new HashSet<int>(route.nodeIds);
            float radiusSq = radiusM * radiusM;

            // Pre-compute path node positions dla distance check
            var pathNodePositions = new List<Vector2>(route.nodeIds.Count);
            for (int i = 0; i < route.nodeIds.Count; i++)
            {
                int nid = route.nodeIds[i];
                if (nid >= 0 && nid < init.Graph.NodeCount)
                    pathNodePositions.Add(init.Graph.GetNode(nid).position);
            }
            if (pathNodePositions.Count == 0) return result;

            foreach (var st in init.Stations)
            {
                if (st.pathNodeId < 0) continue;
                if (onPathNodes.Contains(st.pathNodeId)) continue; // już on-path, skip

                // Check distance to nearest path node
                float bestDistSq = float.MaxValue;
                for (int i = 0; i < pathNodePositions.Count; i++)
                {
                    float distSq = (pathNodePositions[i] - st.position).sqrMagnitude;
                    if (distSq < bestDistSq) bestDistSq = distSq;
                }

                if (bestDistSq <= radiusSq)
                    result.Add(st);
            }

            return result;
        }

        /// <summary>
        /// Diagnostic: count off-path stations dla typowej route (Łódź → Skarżysko etc.).
        /// </summary>
        public static (int total, int major, int halt) CountOffPathStations(
            Route route, TimetableInitializer init, float radiusM = DefaultDetectionRadiusM)
        {
            var offPath = GetOffPathStations(route, init, radiusM);
            int major = 0, halt = 0;
            foreach (var st in offPath)
            {
                if (st.isMajorStation) major++;
                else halt++;
            }
            Log.Info($"[F1.6 polish] Off-path stations near route ({radiusM}m): " +
                     $"{offPath.Count} total ({major} major, {halt} halt)");
            return (offPath.Count, major, halt);
        }
    }
}
