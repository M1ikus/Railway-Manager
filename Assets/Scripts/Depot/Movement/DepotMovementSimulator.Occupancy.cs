using UnityEngine;

namespace DepotSystem
{
    public partial class DepotMovementSimulator
    {
        // ── TD-031: zajętość pozycyjna w ruchu (footprint + cap dojazdu) ──────────

        /// <summary>
        /// TD-031: zapisuje footprint składu (center ± L/2 na polyline) jako interwały occupancy na
        /// torach które aktualnie nachodzi; usuwa z segmentów których już nie dotyka. Wołane co tick
        /// w AdvanceMovement oraz przy admission (ExecutePathfinding). Straddle (2 tory naraz) wychodzi
        /// naturalnie — interwał per nachodzony segment.
        /// </summary>
        void UpdateOccupantIntervalsForTask(DepotMoveTask task)
        {
            if (task.trackSegments == null || _graph == null) return;

            float L = ComputeConsistScale(task.vehicleIds).z;
            float half = L * 0.5f;
            float frontPoly = task.currentDistanceM + half;
            float rearPoly = task.currentDistanceM - half;

            foreach (var seg in task.trackSegments)
            {
                if (seg.trackId < 0) continue;

                float ovLo = Mathf.Max(rearPoly, seg.polyStartM);
                float ovHi = Mathf.Min(frontPoly, seg.polyEndM);
                if (ovHi <= ovLo + 1e-4f)
                {
                    _graph.RemoveOccupant(seg.trackId, task.consistId);
                    continue;
                }

                float a = TrackOccupancyMath.TaskDistToTrackLocal(seg, ovLo);
                float b = TrackOccupancyMath.TaskDistToTrackLocal(seg, ovHi);
                int dir = seg.reversedVsTrack ? -1 : 1; // nos ku rosnącemu polyline = orientacja składu
                _graph.SetOccupantInterval(seg.trackId, task.consistId, task.vehicleIds, a, b, dir);
            }
        }

        /// <summary>
        /// TD-031: dynamiczny limit dojazdu (na CENTER, w przestrzeni polyline) = pozycja za najbliższą
        /// INNĄ jednostką w kierunku jazdy, z buforem styku. forward=true → upper bound (+inf gdy nikogo);
        /// forward=false (cofanie) → lower bound (-inf gdy nikogo). Ustawia też task.dynamicStopBlockerId.
        /// Liczone w przestrzeni polyline taska (occupanci innych torów rzutowani przez TaskTrackSegment).
        /// </summary>
        float ComputeDynamicStopCap(DepotMoveTask task, bool forward)
        {
            task.dynamicStopBlockerId = -1;
            if (task.trackSegments == null || _graph == null)
                return forward ? float.PositiveInfinity : float.NegativeInfinity;

            float L = ComputeConsistScale(task.vehicleIds).z;
            float half = L * 0.5f;
            float gap = DepotOccupancyConstants.ContactGapM;

            if (forward)
            {
                float nose = task.currentDistanceM + half;
                float bestLo = float.PositiveInfinity;
                int bestId = -1;
                foreach (var seg in task.trackSegments)
                {
                    if (seg.trackId < 0) continue;
                    foreach (var occ in _graph.GetOccupants(seg.trackId))
                    {
                        if (occ == null || occ.ConsistId == task.consistId) continue;
                        float a = TrackOccupancyMath.TrackLocalToTaskDist(seg, occ.FrontDistM);
                        float b = TrackOccupancyMath.TrackLocalToTaskDist(seg, occ.RearDistM);
                        float lo = a < b ? a : b;
                        float hi = a < b ? b : a;
                        if (hi > nose + 1e-3f && lo < bestLo) { bestLo = lo; bestId = occ.ConsistId; }
                    }
                }
                if (float.IsPositiveInfinity(bestLo)) return float.PositiveInfinity;
                task.dynamicStopBlockerId = bestId;
                return bestLo - gap - half;
            }
            else
            {
                float rear = task.currentDistanceM - half;
                float bestHi = float.NegativeInfinity;
                int bestId = -1;
                foreach (var seg in task.trackSegments)
                {
                    if (seg.trackId < 0) continue;
                    foreach (var occ in _graph.GetOccupants(seg.trackId))
                    {
                        if (occ == null || occ.ConsistId == task.consistId) continue;
                        float a = TrackOccupancyMath.TrackLocalToTaskDist(seg, occ.FrontDistM);
                        float b = TrackOccupancyMath.TrackLocalToTaskDist(seg, occ.RearDistM);
                        float lo = a < b ? a : b;
                        float hi = a < b ? b : a;
                        if (lo < rear - 1e-3f && hi > bestHi) { bestHi = hi; bestId = occ.ConsistId; }
                    }
                }
                if (float.IsNegativeInfinity(bestHi)) return float.NegativeInfinity;
                task.dynamicStopBlockerId = bestId;
                return bestHi + gap + half;
            }
        }

        /// <summary>
        /// TD-031 pass 1: przelicza dynamicStopCapM dla wszystkich Moving tasków ze snapshotu interwałów
        /// z POCZĄTKU ticku (tylko odczyt). Dzięki temu wynik jest niezależny od kolejności przetwarzania
        /// tasków w pass 2 (determinizm — żaden mover nie zakłada że drugi już ustąpił).
        /// </summary>
        void RecomputeAllDynamicStopCaps()
        {
            for (int i = 0; i < _tasks.Count; i++)
            {
                var t = _tasks[i];
                if (t.state != DepotMoveState.Moving) continue;
                float planned = t.stopDistanceM > 0f ? t.stopDistanceM : t.totalLengthM;
                bool forward = planned >= t.currentDistanceM;
                t.dynamicStopCapM = ComputeDynamicStopCap(t, forward);
            }
        }
    }
}
