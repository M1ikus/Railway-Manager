using System.Collections.Generic;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Timetable.Assistant
{
    /// <summary>
    /// M11 AS-5c: warstwa scenowa plannera — zbiera <see cref="RelationFacts"/> dla pary
    /// stacji (pathfinding po sieci + elektryfikacja per segment + popyt z DemandQuery).
    /// Czysta logika decyzyjna zostaje w RelationPlannerCore; tu tylko fakty.
    ///
    /// Null-safe: graf niegotowy / stacja niezmapowana na węzeł / brak ścieżki →
    /// facts.routeFound = false (W1 odrzuci wszystkich kandydatów z powodem no_route).
    /// </summary>
    public static class RelationFactsGatherer
    {
        public static bool IsReady =>
            TimetableInitializer.Instance != null && TimetableInitializer.Instance.IsReady;

        public static RelationFacts Gather(RailwayStation from, RailwayStation to)
        {
            var facts = new RelationFacts
            {
                fromStationId = from?.stationId ?? -1,
                toStationId = to?.stationId ?? -1,
                fromName = from?.name ?? "?",
                toName = to?.name ?? "?"
            };

            var init = TimetableInitializer.Instance;
            if (init == null || !init.IsReady || from == null || to == null) return facts;
            if (from.pathNodeId < 0 || to.pathNodeId < 0) return facts; // stacja bez węzła grafu

            var path = RailwayPathfinder.FindPath(init.Graph, from.pathNodeId, to.pathNodeId);
            if (!path.success || path.totalLengthM <= 0f) return facts;

            facts.routeFound = true;
            facts.routeLengthKm = path.totalLengthM / 1000f;
            facts.fullyElectrified = ComputeFullyElectrified(init.Graph, path.edgeIds);
            facts.estimatedDailyDemand = DemandQuery.EstimatedDailyDemand(from.stationId, to.stationId);
            return facts;
        }

        /// <summary>Cała trasa pod siecią = każdy segment ma tag electrified (W1 filtr EMU).</summary>
        public static bool ComputeFullyElectrified(PathfindingGraph graph, List<int> edgeIds)
        {
            if (graph == null || edgeIds == null || edgeIds.Count == 0) return false;
            foreach (var edgeId in edgeIds)
            {
                if (!IsEdgeElectrified(graph.GetEdge(edgeId).metadata)) return false;
            }
            return true;
        }

        /// <summary>
        /// Tag OSM `electrified` per krawędź — wartości jak w SegmentSpeedResolver.IsElectrified
        /// (yes / contact_line / rail / 3rd_rail). Pure — testowalne EditMode na słowniku.
        /// </summary>
        public static bool IsEdgeElectrified(IReadOnlyDictionary<string, string> metadata)
        {
            if (metadata == null) return false;
            if (!metadata.TryGetValue("electrified", out var value)) return false;
            if (string.IsNullOrEmpty(value)) return false;
            switch (value.ToLowerInvariant())
            {
                case "yes":
                case "contact_line":
                case "rail":
                case "3rd_rail":
                    return true;
                default:
                    return false;
            }
        }
    }
}
