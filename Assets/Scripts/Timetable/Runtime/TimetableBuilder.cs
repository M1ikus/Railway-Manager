using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Logika budowy rozkładu jazdy — wylicza czasy postojów na podstawie trasy,
    /// parametrów taboru i kategorii handlowej. Oddzielona od UI.
    /// </summary>
    public static class TimetableBuilder
    {
        /// <summary>
        /// Buduje listę segmentów trasy z Vmax per edge — input dla TravelTimeCalculator.
        /// </summary>
        public static List<TravelTimeCalculator.RouteSegment> ComputeRouteSegments(
            Route route, PathfindingGraph graph)
        {
            var segments = new List<TravelTimeCalculator.RouteSegment>();
            if (route?.nodeIds == null || route.nodeIds.Count < 2 || graph == null) return segments;

            for (int i = 0; i < route.nodeIds.Count - 1; i++)
            {
                int fromId = route.nodeIds[i];
                int toId = route.nodeIds[i + 1];

                // Znajdź edge — użyj edge.lengthM (z geometrii OSM) i maxSpeedKmh
                int edgeId = graph.FindEdgeBetween(fromId, toId);
                float length;
                int maxSpeed;

                if (edgeId >= 0)
                {
                    var edge = graph.GetEdge(edgeId);
                    length = edge.lengthM;
                    maxSpeed = edge.maxSpeedKmh > 0 ? edge.maxSpeedKmh : LineUsageSpeedCatalog.Unknown;
                }
                else
                {
                    length = Vector2.Distance(graph.GetNode(fromId).position, graph.GetNode(toId).position);
                    maxSpeed = LineUsageSpeedCatalog.Unknown;
                }

                if (length < 0.1f) continue;

                segments.Add(new TravelTimeCalculator.RouteSegment
                {
                    lengthM = length,
                    maxSpeedKmh = maxSpeed
                });
            }
            return segments;
        }

        /// <summary>
        /// Buduje listę TimetableStop dla wszystkich stacji na trasie z wyliczonymi czasami.
        /// </summary>
        public static List<TimetableStop> BuildStops(
            Route route,
            PathfindingGraph graph,
            CommercialCategory category,
            CompositionMode compositionMode,
            int maxSpeedKmh,
            int startMinutesFromMidnight)
        {
            var stops = new List<TimetableStop>();
            if (route?.stations == null || route.stations.Count == 0) return stops;

            // Compute segments + total driving time
            var segments = ComputeRouteSegments(route, graph);

            var composition = new PlannedComposition
            {
                mode = compositionMode,
                maxSpeedKmh = maxSpeedKmh,
                brakeRegime = BrakeRegime.R
            };

            // Compute cumulative time to each node on route
            var cumulativeTimeSec = ComputeCumulativeTimes(segments, composition, maxSpeedKmh);

            float totalRouteTimeSec = cumulativeTimeSec.Count > 0 ? cumulativeTimeSec[cumulativeTimeSec.Count - 1] : 0;
            float totalLenM = 0f;
            foreach (var s in segments) totalLenM += s.lengthM;
            Log.Info($"[TimetableBuilder] BuildStops: {segments.Count} segments, {totalLenM / 1000f:F1} km, "
                     + $"cumulative time {totalRouteTimeSec:F0}s ({totalRouteTimeSec / 60f:F1} min), "
                     + $"Vmax cap={maxSpeedKmh}");

            int startSec = startMinutesFromMidnight * 60;
            int minStop = category?.minStopSeconds ?? TimetableTuningConstants.DefaultMinStopSeconds;
            int accumulatedDelay = 0; // accumulated stop time

            for (int si = 0; si < route.stations.Count; si++)
            {
                var rs = route.stations[si];
                bool isFirst = si == 0;
                bool isLast = si == route.stations.Count - 1;

                // Find approximate driving time to this station
                int drivingTimeSec = FindDrivingTimeToStation(
                    rs, route, graph, cumulativeTimeSec);

                // Determine stop type from category policy
                StopType stopType;
                if (isFirst || isLast)
                    stopType = StopType.PH;
                else if (category?.defaultStopPolicy == StopPolicy.AllStations)
                    stopType = StopType.PH;
                else if (category?.defaultStopPolicy == StopPolicy.MajorStationsOnly && rs.isMajorStation)
                    stopType = StopType.PH;
                else
                    stopType = StopType.Transit;

                int stopDuration = stopType == StopType.PH ? minStop : 0;
                if (isFirst || isLast) stopDuration = 0; // start/end = no dwell

                int arrivalSec = drivingTimeSec + accumulatedDelay;
                int departureSec = arrivalSec + stopDuration;

                stops.Add(new TimetableStop
                {
                    stationNodeId = rs.stationNodeId,
                    stationName = rs.stationName,
                    plannedArrivalSec = arrivalSec,
                    plannedDepartureSec = departureSec,
                    stopType = stopType,
                    distanceFromStartM = rs.distanceFromStartM
                });

                accumulatedDelay += stopDuration;
            }

            return stops;
        }

        /// <summary>
        /// Tworzy kompletny Timetable z podanych danych. Gotowy do AddTimetable.
        /// </summary>
        public static Timetable CreateTimetable(
            Route route,
            string commercialCategoryId,
            List<TimetableStop> stops,
            CompositionMode compositionMode,
            int maxSpeedKmh,
            int startMinutesFromMidnight)
        {
            var tt = new Timetable
            {
                name = route.name,
                routeId = route.id,
                commercialCategoryId = commercialCategoryId,
                stops = stops,
                frequency = FrequencySpec.SingleRun(startMinutesFromMidnight),
                calendar = DayMask.Daily(),
                composition = new PlannedComposition
                {
                    assignment = CompositionAssignment.Symbolic,
                    mode = compositionMode,
                    maxSpeedKmh = maxSpeedKmh,
                    brakeRegime = BrakeRegime.R,
                    symbolicNotation = compositionMode == CompositionMode.MultipleUnit ? "EZT" : "Lok+Wag"
                }
            };

            // Auto-classify IRJ (simplified — full version needs more inputs)
            int totalStations = route.stations?.Count ?? 0;
            int passengerStops = 0;
            foreach (var s in stops)
                if (s.stopType == StopType.PH) passengerStops++;

            tt.trainNumber = ""; // auto-generate later

            Log.Info($"[TimetableBuilder] Created timetable: {tt.name}, "
                     + $"{stops.Count} stops ({passengerStops} passenger), "
                     + $"duration {(stops.Count > 0 ? stops[stops.Count - 1].plannedDepartureSec : 0) / 60}min");

            return tt;
        }

        // ─────────────────────────────────────────────

        private static List<float> ComputeCumulativeTimes(
            List<TravelTimeCalculator.RouteSegment> segments,
            PlannedComposition composition, int trainMaxKmh)
        {
            var times = new List<float> { 0f };
            float cumulative = 0f;

            // Simplified: compute each segment independently as if starting from cruise
            foreach (var seg in segments)
            {
                int localMax = Mathf.Min(seg.maxSpeedKmh, trainMaxKmh);
                float speedMs = localMax / 3.6f;
                float time = speedMs > 0f ? seg.lengthM / speedMs : 0f;
                // Add 15% for accel/decel overhead (simplified)
                time *= 1.15f;
                cumulative += time;
                times.Add(cumulative);
            }
            return times;
        }

        private static int FindDrivingTimeToStation(
            RouteStation station, Route route, PathfindingGraph graph,
            List<float> cumulativeTimeSec)
        {
            if (route.nodeIds == null || cumulativeTimeSec == null) return 0;

            // Find which nodeId index this station is closest to
            int stationNode = station.stationNodeId;
            int bestIdx = 0;
            float bestDist = float.MaxValue;

            for (int i = 0; i < route.nodeIds.Count && i < cumulativeTimeSec.Count; i++)
            {
                if (route.nodeIds[i] == stationNode)
                    return (int)cumulativeTimeSec[i];

                var nodePos = graph.GetNode(route.nodeIds[i]).position;
                float dist = (nodePos - station.position).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }

            return bestIdx < cumulativeTimeSec.Count ? (int)cumulativeTimeSec[bestIdx] : 0;
        }
    }
}
