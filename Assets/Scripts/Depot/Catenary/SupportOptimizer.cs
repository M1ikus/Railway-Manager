using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    /// <summary>
    /// Etap 3 pipeline'u sieci trakcyjnej: optymalizacja podpór.
    /// Znajduje minimalny zestaw słupów/bramek pokrywający wszystkie przewody.
    /// </summary>
    public static class SupportOptimizer
    {
        // Geometria
        public const float PoleHeight = 8f;
        public const float PoleRadius = 0.08f;
        public const float MinTrackClearance = 2.5f;
        public const float BasePoleOffset = 2.5f;
        public const float ContactWireHeight = 5.5f;

        // Grupowanie
        public const float GantryGroupingThreshold = 6f;
        public const float AlignmentTolerance = 10f;

        // Koszty optymalizacyjne
        private const float CostPole = 1.0f;
        private const float CostGantry2 = 1.4f;
        private const float CostGantry3 = 2.2f;
        private const float CostGantry5 = 3.5f;

        /// <summary>
        /// Główna metoda: z wire paths generuje SupportPointy i SupportStructure.
        /// </summary>
        public static (List<SupportPoint> points, List<SupportStructure> supports)
            OptimizeSupports(
                List<WirePath> wirePaths,
                List<CatenaryZone> zones,
                TrackGraph graph)
        {
            // 1. Wybierz punkty kontrolne będące kandydatami na podpory
            var candidates = SelectSupportCandidates(wirePaths);

            // 2. Zachłanny set-cover: wybierz minimalny zestaw pokrywający wszystkie przęsła
            var selectedPoints = GreedyCover(candidates, wirePaths, graph);

            // 3. Grupuj w struktury (słupy/bramki)
            var supports = GroupIntoStructures(selectedPoints, graph);

            // 4. Clearance check
            EnforceClearance(supports, graph);

            Log.Info($"[SupportOptimizer] {selectedPoints.Count} support points → " +
                      $"{supports.Count(s => s.Type == SupportType.Pole)} poles, " +
                      $"{supports.Count(s => s.Type == SupportType.Gantry)} gantries");

            return (selectedPoints, supports);
        }

        // ═══════════════════════════════════════════
        //  SELEKCJA KANDYDATÓW
        // ═══════════════════════════════════════════

        private static List<SupportPoint> SelectSupportCandidates(List<WirePath> wirePaths)
        {
            var points = new List<SupportPoint>();

            foreach (var path in wirePaths)
            {
                foreach (var cp in path.ControlPoints)
                {
                    if (!cp.IsSupportCandidate) continue;

                    points.Add(new SupportPoint
                    {
                        TrackId = cp.TrackId,
                        DistAlongTrack = cp.DistAlongTrack,
                        Position = cp.Position,
                        Tangent = cp.Tangent,
                        LocalRadius = cp.LocalRadius,
                        IsLongSide = cp.ZigzagOffset >= 0f,
                        WireControlPointIndex = path.ControlPoints.IndexOf(cp)
                    });
                }
            }

            return points;
        }

        // ═══════════════════════════════════════════
        //  ZACHLANNY SET-COVER
        // ═══════════════════════════════════════════

        /// <summary>
        /// Wybiera podzbiór punktów tak, żeby:
        /// - żaden odcinek bez podpory nie przekroczył max spacingu
        /// - liczba podpór była minimalna
        /// </summary>
        private static List<SupportPoint> GreedyCover(
            List<SupportPoint> allCandidates,
            List<WirePath> wirePaths,
            TrackGraph graph)
        {
            var selected = new List<SupportPoint>();

            // Grupuj kandydatów po torze
            var byTrack = new Dictionary<int, List<SupportPoint>>();
            foreach (var p in allCandidates)
            {
                if (!byTrack.ContainsKey(p.TrackId))
                    byTrack[p.TrackId] = new List<SupportPoint>();
                byTrack[p.TrackId].Add(p);
            }

            foreach (var kvp in byTrack)
            {
                int trackId = kvp.Key;
                var candidates = kvp.Value;
                candidates.Sort((a, b) => a.DistAlongTrack.CompareTo(b.DistAlongTrack));

                if (candidates.Count == 0) continue;

                // Zawsze bierz pierwszy i ostatni punkt
                var trackSelected = new List<SupportPoint> { candidates[0] };

                // Zachłannie: od ostatniego wybranego, wybierz najdalszy punkt
                // który nie przekracza max spacingu
                int lastIdx = 0;
                while (lastIdx < candidates.Count - 1)
                {
                    float lastDist = candidates[lastIdx].DistAlongTrack;
                    float maxSpacing = GetMaxSpacing(candidates[lastIdx]);

                    // Szukaj najdalszego kandydata w zasięgu
                    int bestIdx = lastIdx + 1;
                    for (int i = lastIdx + 1; i < candidates.Count; i++)
                    {
                        float gap = candidates[i].DistAlongTrack - lastDist;
                        if (gap <= maxSpacing)
                            bestIdx = i;
                        else
                            break;
                    }

                    trackSelected.Add(candidates[bestIdx]);
                    lastIdx = bestIdx;
                }

                selected.AddRange(trackSelected);
            }

            return selected;
        }

        private static float GetMaxSpacing(SupportPoint point)
        {
            return CatenarySpacing.GetSpacing(point.LocalRadius);
        }

        // ═══════════════════════════════════════════
        //  GRUPOWANIE W STRUKTURY
        // ═══════════════════════════════════════════

        private static List<SupportStructure> GroupIntoStructures(
            List<SupportPoint> selectedPoints,
            TrackGraph graph)
        {
            var supports = new List<SupportStructure>();
            var processed = new HashSet<SupportPoint>();

            // Zbierz polyline WSZYSTKICH torów (do clearance)
            var allTrackPolylines = new Dictionary<int, List<Vector3>>();
            foreach (var track in graph.Tracks.Values)
            {
                var poly = graph.GetTrackPolyline(track.TrackId);
                if (poly != null && poly.Count >= 2)
                    allTrackPolylines[track.TrackId] = poly;
            }

            // Sortuj po pozycji X (sweep line)
            var sorted = selectedPoints.OrderBy(p => p.Position.x).ThenBy(p => p.Position.z).ToList();

            foreach (var seed in sorted)
            {
                if (processed.Contains(seed)) continue;

                var group = new List<SupportPoint> { seed };
                processed.Add(seed);

                // Zbierz bliskie punkty z INNYCH torów
                foreach (var candidate in sorted)
                {
                    if (processed.Contains(candidate)) continue;
                    if (candidate.TrackId == seed.TrackId) continue;

                    // Już mamy punkt z tego toru?
                    bool trackInGroup = group.Any(g => g.TrackId == candidate.TrackId);
                    if (trackInGroup) continue;

                    float dist2D = Vector2.Distance(
                        new Vector2(seed.Position.x, seed.Position.z),
                        new Vector2(candidate.Position.x, candidate.Position.z));

                    // Równoległość tangent
                    float tangentDot = Mathf.Abs(Vector3.Dot(seed.Tangent, candidate.Tangent));
                    bool isParallel = tangentDot > 0.85f;

                    bool isOverlapping = dist2D < MinTrackClearance;
                    if (isOverlapping || (dist2D < AlignmentTolerance && isParallel))
                    {
                        group.Add(candidate);
                        processed.Add(candidate);
                    }
                }

                // Stwórz strukturę
                if (group.Count == 1)
                {
                    var pole = CreatePole(group[0], allTrackPolylines);
                    if (pole != null)
                        supports.Add(pole);
                    else
                        supports.Add(CreateGantry(group, allTrackPolylines));
                }
                else
                {
                    // Sprawdź spread — czy to naprawdę osobne tory (bramka) czy nakładające się (słup)
                    float maxSpread = 0f;
                    for (int i = 0; i < group.Count; i++)
                        for (int j = i + 1; j < group.Count; j++)
                        {
                            float d = Vector2.Distance(
                                new Vector2(group[i].Position.x, group[i].Position.z),
                                new Vector2(group[j].Position.x, group[j].Position.z));
                            maxSpread = Mathf.Max(maxSpread, d);
                        }

                    if (maxSpread > MinTrackClearance)
                        supports.Add(CreateGantry(group, allTrackPolylines));
                    else
                    {
                        var pole = CreatePoleFromGroup(group, allTrackPolylines);
                        if (pole != null)
                            supports.Add(pole);
                        else
                            supports.Add(CreateGantry(group, allTrackPolylines));
                    }
                }
            }

            // Post-processing: merge bliskich konstrukcji
            MergeNearbySupports(supports);

            return supports;
        }

        // ═══════════════════════════════════════════
        //  TWORZENIE STRUKTUR
        // ═══════════════════════════════════════════

        private static SupportStructure CreatePole(
            SupportPoint point, Dictionary<int, List<Vector3>> allTrackPolylines)
        {
            Vector3 perp = Vector3.Cross(point.Tangent, Vector3.up).normalized;
            float poleSide = point.IsLongSide ? 1f : -1f;
            float poleOffset = BasePoleOffset;

            // Sprawdź stronę — jeśli żadna nie ma clearance przy 2.5m, zwróć null (→ bramka)
            Vector3 polePos = point.Position + perp * poleSide * poleOffset;
            if (!CheckClearance(polePos, allTrackPolylines))
            {
                float otherSide = -poleSide;
                Vector3 otherPos = point.Position + perp * otherSide * poleOffset;
                if (CheckClearance(otherPos, allTrackPolylines))
                    poleSide = otherSide;
                else
                    return null; // brak miejsca na słup z żadnej strony → wymuszenie bramki
            }

            var support = new SupportStructure
            {
                Type = SupportType.Pole,
                Position = point.Position,
                Tangent = point.Tangent,
                PoleHeight = PoleHeight,
                PoleOffset = poleOffset,
                PoleSide = poleSide,
                CantileverLength = poleOffset,
                HasStayWire = false
            };

            point.Support = support;
            support.Points.Add(point);
            return support;
        }

        private static SupportStructure CreatePoleFromGroup(
            List<SupportPoint> group, Dictionary<int, List<Vector3>> allTrackPolylines)
        {
            var pole = CreatePole(group[0], allTrackPolylines);
            if (pole == null) return null;
            for (int i = 1; i < group.Count; i++)
            {
                group[i].Support = pole;
                pole.Points.Add(group[i]);
            }
            return pole;
        }

        private static SupportStructure CreateGantry(
            List<SupportPoint> points, Dictionary<int, List<Vector3>> allTrackPolylines)
        {
            Vector3 center = Vector3.zero;
            foreach (var p in points) center += p.Position;
            center /= points.Count;

            // Średni tangent (wyrównany do jednego kierunku)
            Vector3 refTangent = points[0].Tangent;
            refTangent.y = 0f;
            refTangent = refTangent.normalized;

            Vector3 avgTangent = refTangent;
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 t = points[i].Tangent;
                t.y = 0f;
                t = t.normalized;
                if (Vector3.Dot(t, refTangent) < 0f) t = -t;
                avgTangent += t;
            }
            avgTangent.y = 0f;
            avgTangent = avgTangent.sqrMagnitude > 0.01f ? avgTangent.normalized : refTangent;

            Vector3 perp = Vector3.Cross(avgTangent, Vector3.up).normalized;

            // Rzutuj punkty na oś perp
            float minOffset = float.MaxValue, maxOffset = float.MinValue;
            foreach (var p in points)
            {
                float offset = Vector3.Dot(p.Position - center, perp);
                minOffset = Mathf.Min(minOffset, offset);
                maxOffset = Mathf.Max(maxOffset, offset);
            }

            float leftOffset = minOffset - BasePoleOffset;
            float rightOffset = maxOffset + BasePoleOffset;

            // Clearance nóg
            EnsureLegClearance(ref leftOffset, ref rightOffset, center, perp, allTrackPolylines);

            var support = new SupportStructure
            {
                Type = SupportType.Gantry,
                Position = center,
                Tangent = avgTangent,
                PoleHeight = PoleHeight,
                GantryWidth = rightOffset - leftOffset,
                LeftLegPosition = center + perp * leftOffset,
                RightLegPosition = center + perp * rightOffset
            };

            foreach (var p in points)
            {
                p.Support = support;
                support.Points.Add(p);
            }

            // Snap punktów na linię bramki
            SnapPointsToGantryLine(support, center, avgTangent, allTrackPolylines);

            return support;
        }

        // ═══════════════════════════════════════════
        //  SNAP PUNKTÓW NA LINIĘ BRAMKI
        // ═══════════════════════════════════════════

        private static void SnapPointsToGantryLine(
            SupportStructure gantry, Vector3 center, Vector3 tangent,
            Dictionary<int, List<Vector3>> allTrackPolylines)
        {
            foreach (var point in gantry.Points)
            {
                if (!allTrackPolylines.ContainsKey(point.TrackId)) continue;
                var poly = allTrackPolylines[point.TrackId];
                if (poly == null || poly.Count < 2) continue;

                float totalLen = TrackGeometry.CalculatePolylineLength(poly);
                float bestDist = point.DistAlongTrack;
                float bestDot = float.MaxValue;

                float searchRadius = 15f;
                float searchStep = 0.5f;
                float searchStart = Mathf.Max(0f, point.DistAlongTrack - searchRadius);
                float searchEnd = Mathf.Min(totalLen, point.DistAlongTrack + searchRadius);

                for (float d = searchStart; d <= searchEnd; d += searchStep)
                {
                    var (pos, _) = TrackGeometry.GetPointAtDistance(poly, d);
                    Vector3 diff = new Vector3(pos.x, 0f, pos.z) - center;
                    float dot = Mathf.Abs(Vector3.Dot(diff, tangent));
                    if (dot < bestDot) { bestDot = dot; bestDist = d; }
                }

                // Fine search
                float fineStart = Mathf.Max(0f, bestDist - searchStep);
                float fineEnd = Mathf.Min(totalLen, bestDist + searchStep);
                for (float d = fineStart; d <= fineEnd; d += 0.05f)
                {
                    var (pos, _) = TrackGeometry.GetPointAtDistance(poly, d);
                    Vector3 diff = new Vector3(pos.x, 0f, pos.z) - center;
                    float dot = Mathf.Abs(Vector3.Dot(diff, tangent));
                    if (dot < bestDot) { bestDot = dot; bestDist = d; }
                }

                var (snappedPos, snappedTan) = TrackGeometry.GetPointAtDistance(poly, bestDist);
                point.DistAlongTrack = bestDist;
                point.Position = new Vector3(snappedPos.x, 0f, snappedPos.z);
                point.Tangent = snappedTan.sqrMagnitude > 0.001f ? snappedTan.normalized : point.Tangent;
            }

            // Przelicz center
            Vector3 newCenter = Vector3.zero;
            foreach (var p in gantry.Points) newCenter += p.Position;
            newCenter /= gantry.Points.Count;
            gantry.Position = newCenter;
        }

        // ═══════════════════════════════════════════
        //  MERGE BLISKICH KONSTRUKCJI
        // ═══════════════════════════════════════════

        private static void MergeNearbySupports(List<SupportStructure> supports)
        {
            const float MergeDistThreshold = 4f;

            bool changed = true;
            int maxIter = 10;
            while (changed && maxIter-- > 0)
            {
                changed = false;
                var absorbed = new HashSet<SupportStructure>();

                for (int i = 0; i < supports.Count; i++)
                {
                    var a = supports[i];
                    if (absorbed.Contains(a)) continue;

                    for (int j = i + 1; j < supports.Count; j++)
                    {
                        var b = supports[j];
                        if (absorbed.Contains(b)) continue;

                        float dist2D = Vector2.Distance(
                            new Vector2(a.Position.x, a.Position.z),
                            new Vector2(b.Position.x, b.Position.z));
                        if (dist2D > 50f) continue;

                        Vector3 diff = b.Position - a.Position;
                        diff.y = 0f;
                        Vector3 tangent = a.Tangent.sqrMagnitude > 0.01f ? a.Tangent : Vector3.forward;
                        float alongDist = Mathf.Abs(Vector3.Dot(diff, tangent));
                        if (alongDist > MergeDistThreshold) continue;

                        float dot = Mathf.Abs(Vector3.Dot(a.Tangent, b.Tangent));
                        if (dot < 0.7f) continue;

                        // Scal B do A
                        foreach (var p in b.Points)
                        {
                            p.Support = a;
                            a.Points.Add(p);
                        }
                        absorbed.Add(b);
                        changed = true;
                    }
                }
                supports.RemoveAll(s => absorbed.Contains(s));
            }

            // Promuj słupy wielotorowe do bramek
            foreach (var s in supports)
            {
                if (s.Type != SupportType.Pole) continue;
                var trackIds = new HashSet<int>();
                foreach (var p in s.Points) trackIds.Add(p.TrackId);
                if (trackIds.Count < 2) continue;

                float maxSpread = 0f;
                for (int i = 0; i < s.Points.Count; i++)
                    for (int j = i + 1; j < s.Points.Count; j++)
                    {
                        float d = Vector2.Distance(
                            new Vector2(s.Points[i].Position.x, s.Points[i].Position.z),
                            new Vector2(s.Points[j].Position.x, s.Points[j].Position.z));
                        maxSpread = Mathf.Max(maxSpread, d);
                    }

                if (maxSpread > MinTrackClearance)
                {
                    s.Type = SupportType.Gantry;
                    RecalcGantryLegs(s);
                }
            }

            // Przelicz nogi bramek
            foreach (var s in supports)
                if (s.Type == SupportType.Gantry)
                    RecalcGantryLegs(s);

            // Deduplikuj punkty na tym samym torze (w ramach jednej konstrukcji)
            foreach (var s in supports)
            {
                if (s.Points.Count < 2) continue;
                var byTrack = new Dictionary<int, List<SupportPoint>>();
                foreach (var p in s.Points)
                {
                    if (!byTrack.ContainsKey(p.TrackId))
                        byTrack[p.TrackId] = new List<SupportPoint>();
                    byTrack[p.TrackId].Add(p);
                }
                var toRemove = new HashSet<SupportPoint>();
                foreach (var kvp in byTrack)
                {
                    if (kvp.Value.Count < 2) continue;
                    SupportPoint best = null;
                    float bestDist = float.MaxValue;
                    foreach (var p in kvp.Value)
                    {
                        float d = Mathf.Abs(Vector3.Dot(p.Position - s.Position, s.Tangent));
                        if (d < bestDist) { bestDist = d; best = p; }
                    }
                    foreach (var p in kvp.Value)
                        if (p != best) toRemove.Add(p);
                }
                s.Points.RemoveAll(p => toRemove.Contains(p));
            }

            // Pre-oblicz AttachPosition dla bramek (projekcja na belkę)
            foreach (var s in supports)
            {
                if (s.Type == SupportType.Gantry && s.Points.Count > 0)
                {
                    Vector3 beamDir = (s.RightLegPosition - s.LeftLegPosition);
                    float beamLen = beamDir.magnitude;
                    if (beamLen > 0.01f)
                    {
                        Vector3 beamDirN = beamDir / beamLen;
                        foreach (var p in s.Points)
                        {
                            Vector3 ptXZ = new Vector3(p.Position.x, 0f, p.Position.z);
                            Vector3 leftXZ = new Vector3(s.LeftLegPosition.x, 0f, s.LeftLegPosition.z);
                            float t = Vector3.Dot(ptXZ - leftXZ, beamDirN);
                            t = Mathf.Clamp(t, 0f, beamLen);
                            Vector3 attach = leftXZ + beamDirN * t;
                            p.AttachPosition = attach;
                        }
                    }
                }
                else
                {
                    foreach (var p in s.Points)
                        p.AttachPosition = p.Position;
                }
            }

            // Merge AttachPosition jest robiony w BuildGantryVisual po projekcji na belkę
        }

        private static void RecalcGantryLegs(SupportStructure gantry)
        {
            if (gantry.Points.Count < 2) return;

            Vector3 center = Vector3.zero;
            foreach (var p in gantry.Points) center += p.Position;
            center /= gantry.Points.Count;
            gantry.Position = center;

            Vector3 refTangent = gantry.Points[0].Tangent;
            refTangent.y = 0f;
            refTangent = refTangent.normalized;

            Vector3 avgTangent = refTangent;
            for (int i = 1; i < gantry.Points.Count; i++)
            {
                Vector3 t = gantry.Points[i].Tangent;
                t.y = 0f;
                t = t.normalized;
                if (Vector3.Dot(t, refTangent) < 0f) t = -t;
                avgTangent += t;
            }
            avgTangent.y = 0f;
            avgTangent = avgTangent.sqrMagnitude > 0.01f ? avgTangent.normalized : refTangent;

            Vector3 perp = Vector3.Cross(avgTangent, Vector3.up).normalized;
            gantry.Tangent = avgTangent;

            float minOffset = float.MaxValue, maxOffset = float.MinValue;
            foreach (var p in gantry.Points)
            {
                float offset = Vector3.Dot(p.Position - center, perp);
                minOffset = Mathf.Min(minOffset, offset);
                maxOffset = Mathf.Max(maxOffset, offset);
            }

            gantry.LeftLegPosition = center + perp * (minOffset - BasePoleOffset);
            gantry.RightLegPosition = center + perp * (maxOffset + BasePoleOffset);
            gantry.GantryWidth = (maxOffset + BasePoleOffset) - (minOffset - BasePoleOffset);
        }

        // ═══════════════════════════════════════════
        //  CLEARANCE
        // ═══════════════════════════════════════════

        private static void EnforceClearance(List<SupportStructure> supports, TrackGraph graph)
        {
            var allTrackPolylines = new Dictionary<int, List<Vector3>>();
            foreach (var track in graph.Tracks.Values)
            {
                var poly = graph.GetTrackPolyline(track.TrackId);
                if (poly != null && poly.Count >= 2)
                    allTrackPolylines[track.TrackId] = poly;
            }

            foreach (var s in supports)
            {
                if (s.Type == SupportType.Gantry)
                {
                    float leftOffset = Vector3.Dot(s.LeftLegPosition - s.Position,
                        Vector3.Cross(s.Tangent, Vector3.up).normalized);
                    float rightOffset = Vector3.Dot(s.RightLegPosition - s.Position,
                        Vector3.Cross(s.Tangent, Vector3.up).normalized);

                    Vector3 perp = Vector3.Cross(s.Tangent, Vector3.up).normalized;
                    EnsureLegClearance(ref leftOffset, ref rightOffset, s.Position, perp, allTrackPolylines);

                    s.LeftLegPosition = s.Position + perp * leftOffset;
                    s.RightLegPosition = s.Position + perp * rightOffset;
                    s.GantryWidth = rightOffset - leftOffset;
                }
            }
        }

        private static bool CheckClearance(Vector3 position, Dictionary<int, List<Vector3>> allTrackPolylines)
        {
            Vector2 pos2D = new Vector2(position.x, position.z);
            foreach (var kvp in allTrackPolylines)
            {
                float projDist = TrackGeometry.ProjectPointOnPolyline(kvp.Value, position);
                var (closestPos, _) = TrackGeometry.GetPointAtDistance(kvp.Value, projDist);
                float dist = Vector2.Distance(pos2D, new Vector2(closestPos.x, closestPos.z));
                if (dist < MinTrackClearance) return false;
            }
            return true;
        }

        private static void EnsureLegClearance(
            ref float leftOffset, ref float rightOffset,
            Vector3 center, Vector3 perp,
            Dictionary<int, List<Vector3>> allTrackPolylines)
        {
            for (int iter = 0; iter < 50; iter++)
            {
                bool needsAdjust = false;
                if (!CheckClearance(center + perp * leftOffset, allTrackPolylines))
                {
                    leftOffset -= 0.5f;
                    needsAdjust = true;
                }
                if (!CheckClearance(center + perp * rightOffset, allTrackPolylines))
                {
                    rightOffset += 0.5f;
                    needsAdjust = true;
                }
                if (!needsAdjust) break;
            }
        }
    }
}
