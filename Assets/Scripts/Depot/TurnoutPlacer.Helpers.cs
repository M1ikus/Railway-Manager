using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public partial class TurnoutPlacer
    {
        // ═══════════════════════════════════════════
        //  HELPERS — parallel/near track lookup, turnout membership
        // ═══════════════════════════════════════════

        /// <summary>
        /// Szuka równoległego toru. sideFilter: 0=auto (najbliższy), 1=lewo, 2=prawo.
        /// Strony określane cross productem: srcDir × toTrack.y > 0 = lewo.
        /// </summary>
        public PlacedTrackSegment FindParallelTrack(PlacedTrackSegment sourceTrack, float maxDist = 10f, int sideFilter = 0)
        {
            if (trackBuilder == null || sourceTrack == null) return null;

            Vector3 srcDir = TrackGeometry.GetStartTangent(sourceTrack.Polyline).normalized;
            Vector3 srcMid = (sourceTrack.StartPosition + sourceTrack.EndPosition) / 2f;

            PlacedTrackSegment best = null;
            float bestScore = float.MaxValue;
            float srcAlongMid = Vector3.Dot(srcMid, srcDir);

            // Zbierz wszystkie trackId będące członkami rozjazdów (odnogi, wstawki, body)
            var turnoutMemberIds = new HashSet<int>();
            foreach (var kvp in trackBuilder.TurnoutEntities)
                foreach (int id in kvp.Value.MemberTrackIds)
                    turnoutMemberIds.Add(id);

            foreach (var track in trackBuilder.PlacedTracks)
            {
                if (track == sourceTrack) continue;
                if (track.Polyline == null || track.Polyline.Count < 2) continue;
                if (!TrackGeometry.IsStraightPolyline(track.Polyline)) continue;

                // Pomiń tory będące częścią rozjazdu (odnogi, wstawki, body)
                if (turnoutMemberIds.Contains(track.GraphTrackId)) continue;

                Vector3 tDir = TrackGeometry.GetStartTangent(track.Polyline).normalized;

                float dot = Mathf.Abs(Vector3.Dot(srcDir, tDir));
                if (dot < 0.996f) continue;

                Vector3 tMid = (track.StartPosition + track.EndPosition) / 2f;
                Vector3 toTrack = tMid - srcMid;
                toTrack.y = 0;
                Vector3 perp = toTrack - Vector3.Dot(toTrack, srcDir) * srcDir;
                float perpDist = perp.magnitude;

                if (perpDist < 2f || perpDist > maxDist) continue;

                // Filtruj po stronie: cross product srcDir × toTrack → Y component
                if (sideFilter != 0)
                {
                    float cross = srcDir.x * toTrack.z - srcDir.z * toTrack.x;
                    // cross > 0 = tor po lewej, cross < 0 = tor po prawej
                    if (sideFilter == 1 && cross < 0f) continue; // chcemy lewo, a tor jest po prawej
                    if (sideFilter == 2 && cross > 0f) continue; // chcemy prawo, a tor jest po lewej
                }

                // Preferuj segment z najbliższym środkiem wzdłuż toru
                float tAlongMid = Vector3.Dot(tMid, srcDir);
                float alongDist = Mathf.Abs(srcAlongMid - tAlongMid);
                float score = perpDist + alongDist * 0.1f;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = track;
                }
            }

            return best;
        }

        private PlacedTrackSegment FindTrackNearPoint(Vector3 point)
        {
            if (trackBuilder == null) return null;

            float minDist = 3f;
            PlacedTrackSegment nearest = null;

            foreach (var track in trackBuilder.PlacedTracks)
            {
                if (track.TrackObject == null) continue;
                float distStart = Vector3.Distance(point, track.StartPosition);
                float distEnd = Vector3.Distance(point, track.EndPosition);
                float dist = Mathf.Min(distStart, distEnd);

                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = track;
                }
            }

            return nearest;
        }

        private bool IsTurnoutMember(int graphTrackId)
        {
            if (trackBuilder == null) return false;
            foreach (var kvp in trackBuilder.TurnoutEntities)
                foreach (int id in kvp.Value.MemberTrackIds)
                    if (id == graphTrackId) return true;
            return false;
        }
    }
}
