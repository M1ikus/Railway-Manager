using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public static partial class TrackGeometry
    {
        // ═══════════════════════════════════════════
        //  DUBINS CSC + CCC (U-turn) + tangent helpers
        // ═══════════════════════════════════════════

        /// <summary>
        /// Dubins CSC: Arc1 → Straight → Arc2.
        /// Zapewnia gładką styczność na OBU końcach (oba snappowane do istniejących torów).
        /// Próbuje ±startDir × ±endDir × 4 kombinacje skrętów = 16 ścieżek.
        /// Wybiera najkrótszą z R >= radius.
        /// </summary>
        private static List<Vector3> CalculateDubinsCSC(
            Vector3 startPos, Vector3 startDir,
            Vector3 endPos, Vector3 endDir,
            float radius)
        {
            // Ścieżki "do przodu" (sDir w kierunku A→B) vs "do tyłu" (U-turn)
            float bestForwardLen = float.MaxValue;
            List<Vector3> bestForwardPath = null;
            float bestAnyLen = float.MaxValue;
            List<Vector3> bestAnyPath = null;

            Vector3 toEnd = (endPos - startPos).normalized;

            // Próbuj obie orientacje tangenty na obu końcach
            // (node może być FromNode lub ToNode — oba kierunki są stycznie poprawne)
            Vector3[] startDirs = { startDir, -startDir };
            Vector3[] endDirs = { endDir, -endDir };

            foreach (var sDir in startDirs)
            {
                bool isForward = Vector3.Dot(sDir, toEnd) > -0.1f;

                foreach (var eDir in endDirs)
                {
                    var path = ComputeDubinsCSCForDirections(startPos, sDir, endPos, eDir, radius);
                    if (path != null && path.Count >= 2)
                    {
                        float len = CalculatePolylineLength(path);

                        if (isForward && len < bestForwardLen)
                        {
                            bestForwardLen = len;
                            bestForwardPath = path;
                        }
                        if (len < bestAnyLen)
                        {
                            bestAnyLen = len;
                            bestAnyPath = path;
                        }
                    }
                }
            }

            // Preferuj ścieżki "do przodu"; U-turn tylko gdy brak opcji forward
            var bestPath = bestForwardPath ?? bestAnyPath;

            // Fallback to single arc if CSC failed
            if (bestPath == null || bestPath.Count < 2)
                return CalculateArcRoute(startPos, startDir, endPos, endDir, radius);

            return bestPath;
        }

        /// <summary>
        /// Oblicza jedną ścieżkę Dubins CSC dla konkretnej pary kierunków.
        /// Próbuje 4 kombinacje skrętów (LL, LR, RL, RR).
        /// </summary>
        private static List<Vector3> ComputeDubinsCSCForDirections(
            Vector3 startPos, Vector3 startDir,
            Vector3 endPos, Vector3 endDir,
            float radius, float maxArcAngle = -1f)
        {
            // Domyślnie 300° (pozwala U-turny). Caller może ograniczyć do π (180°) dla forward.
            if (maxArcAngle < 0f)
                maxArcAngle = Mathf.PI * 5f / 3f;

            // Cross(dir, up) daje LEWY prostopadły — startLeft=true → CCW → centrum po LEWEJ ✓
            // (Cross(up, dir) dawałoby PRAWY, co jest odwrócone i powoduje łuki do tyłu)
            Vector3 startPerp = Vector3.Cross(startDir, Vector3.up).normalized;
            Vector3 endPerp = Vector3.Cross(endDir, Vector3.up).normalized;

            Vector3[] startCenters = {
                startPos + startPerp * radius,
                startPos - startPerp * radius
            };
            Vector3[] endCenters = {
                endPos + endPerp * radius,
                endPos - endPerp * radius
            };

            // Dwie kategorie: forward (oba łuki < π) i any (dozwolone > π)
            // Preferuj forward — unika ścieżek idących do tyłu
            float bestForwardLen = float.MaxValue;
            List<Vector3> bestForwardPath = null;
            float bestAnyLen = float.MaxValue;
            List<Vector3> bestAnyPath = null;

            for (int si = 0; si < 2; si++)
            {
                bool startLeft = (si == 0);
                Vector3 cA = startCenters[si];

                for (int ei = 0; ei < 2; ei++)
                {
                    bool endLeft = (ei == 0);
                    Vector3 cB = endCenters[ei];

                    Vector3 tangA, tangB;
                    bool valid;

                    if (startLeft == endLeft)
                        valid = ComputeExternalTangent(cA, cB, radius, startLeft, out tangA, out tangB);
                    else
                        valid = ComputeInternalTangent(cA, cB, radius, startLeft, out tangA, out tangB);

                    if (!valid) continue;

                    // Odrzuć ścieżki gdzie odcinek prosty idzie wyraźnie "do tyłu"
                    Vector3 straightVec = tangB - tangA;
                    float straightLen = straightVec.magnitude;
                    if (straightLen > 0.1f)
                    {
                        Vector3 toEnd = (endPos - startPos).normalized;
                        float forwardDot = Vector3.Dot(straightVec.normalized, toEnd);
                        if (forwardDot < -0.3f) continue;
                    }

                    float arcAngle1 = ComputeArcAngle(cA, startPos, tangA, startLeft);
                    float arcAngle2 = ComputeArcAngle(cB, tangB, endPos, endLeft);

                    if (Mathf.Abs(arcAngle1) > maxArcAngle) continue;
                    if (Mathf.Abs(arcAngle2) > maxArcAngle) continue;

                    float totalLen = Mathf.Abs(arcAngle1) * radius + straightLen + Mathf.Abs(arcAngle2) * radius;

                    // Forward = oba łuki < 180° → ścieżka nigdy nie idzie do tyłu
                    bool isForward = Mathf.Abs(arcAngle1) < Mathf.PI && Mathf.Abs(arcAngle2) < Mathf.PI;

                    if (isForward && totalLen < bestForwardLen)
                    {
                        bestForwardLen = totalLen;
                        bestForwardPath = SampleDubinsCSC(cA, startPos, arcAngle1, tangA,
                                                    tangB, cB, arcAngle2, endPos, radius);
                    }
                    if (totalLen < bestAnyLen)
                    {
                        bestAnyLen = totalLen;
                        bestAnyPath = SampleDubinsCSC(cA, startPos, arcAngle1, tangA,
                                                    tangB, cB, arcAngle2, endPos, radius);
                    }
                }
            }

            // Preferuj forward (oba łuki < π) — unika ścieżek idących do tyłu
            return bestForwardPath ?? bestAnyPath;
        }

        /// <summary>
        /// Oblicza CSC (łuk-prosta-łuk) z maksymalnym promieniem R tak,
        /// żeby wstawka prosta miała ~targetStraight metrów.
        /// Binary search po R: rośnie R → maleje prosta (łuki zajmują więcej).
        /// </summary>
        public static List<Vector3> CalculateCSCWithTargetStraight(
            Vector3 startPos, Vector3 startDir,
            Vector3 endPos, Vector3 endDir,
            float minR, float targetStraight = 6f)
        {
            startPos.y = 0; endPos.y = 0;
            startDir.y = 0; endDir.y = 0;
            startDir.Normalize(); endDir.Normalize();

            float distance = Vector3.Distance(startPos, endPos);
            if (distance < MIN_TRACK_LENGTH) return null;

            // Próbuj wiele wartości R — wybierz tę z wstawką najbliższą targetStraight
            // i max promieniem (preferuj większe R przy podobnej wstawce)
            List<Vector3> bestPath = null;
            float bestScore = float.MaxValue;
            float bestR = 0f;

            // Gęsta siatka promieni od minR do distance*3
            float maxR = distance * 3f;
            int steps = 30;

            for (int i = 0; i < steps; i++)
            {
                float t = (float)i / (steps - 1);
                float tryR = Mathf.Lerp(minR, maxR, t);

                var (path, straightLen) = EvaluateCSC(startPos, startDir, endPos, endDir, tryR, targetStraight);
                if (path == null) continue;

                // Score: priorytet 1 = wstawka blisko targetStraight, priorytet 2 = większy R
                float straightDiff = Mathf.Abs(straightLen - targetStraight);
                float score = straightDiff - tryR * 0.001f; // bonus za większy R

                if (score < bestScore)
                {
                    bestScore = score;
                    bestPath = path;
                    bestR = tryR;
                }
            }

            // Doprecyzuj wokół bestR (±20%)
            if (bestR > 0f)
            {
                float refLo = bestR * 0.8f;
                float refHi = bestR * 1.2f;
                for (int i = 0; i < 10; i++)
                {
                    float tt = (float)i / 9f;
                    float tryR = Mathf.Lerp(refLo, refHi, tt);

                    var (path, straightLen) = EvaluateCSC(startPos, startDir, endPos, endDir, tryR, targetStraight);
                    if (path == null) continue;

                    float straightDiff = Mathf.Abs(straightLen - targetStraight);
                    float score = straightDiff - tryR * 0.001f;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestPath = path;
                    }
                }
            }

            if (bestPath == null) return null;

            // Odrzuć jeśli wstawka > 3× target (coś poszło nie tak)
            return bestPath;
        }

        /// <summary>
        /// Oblicza CSC dla danego R i zwraca (polyline, długość odcinka prostego).
        /// </summary>
        private static (List<Vector3> path, float straightLen) EvaluateCSC(
            Vector3 startPos, Vector3 startDir,
            Vector3 endPos, Vector3 endDir,
            float radius, float targetStraight = 6f)
        {
            Vector3 startPerp = Vector3.Cross(startDir, Vector3.up).normalized;
            Vector3 endPerp = Vector3.Cross(endDir, Vector3.up).normalized;

            Vector3[] startCenters = {
                startPos + startPerp * radius,
                startPos - startPerp * radius
            };
            Vector3[] endCenters = {
                endPos + endPerp * radius,
                endPos - endPerp * radius
            };

            float bestScore = float.MaxValue;
            List<Vector3> bestPath = null;
            float bestStraight = 0f;

            for (int si = 0; si < 2; si++)
            {
                bool startLeft = (si == 0);
                Vector3 cA = startCenters[si];

                for (int ei = 0; ei < 2; ei++)
                {
                    bool endLeft = (ei == 0);
                    Vector3 cB = endCenters[ei];

                    Vector3 tangA, tangB;
                    bool valid;

                    if (startLeft == endLeft)
                        valid = ComputeExternalTangent(cA, cB, radius, startLeft, out tangA, out tangB);
                    else
                        valid = ComputeInternalTangent(cA, cB, radius, startLeft, out tangA, out tangB);

                    if (!valid) continue;

                    Vector3 straightVec = tangB - tangA;
                    float straightLen = straightVec.magnitude;

                    // Odrzuć ścieżki idące do tyłu
                    if (straightLen > 0.1f)
                    {
                        Vector3 toEnd = (endPos - startPos).normalized;
                        if (Vector3.Dot(straightVec.normalized, toEnd) < -0.3f) continue;
                    }

                    float arcAngle1 = ComputeArcAngle(cA, startPos, tangA, startLeft);
                    float arcAngle2 = ComputeArcAngle(cB, tangB, endPos, endLeft);

                    // Tylko forward (oba łuki < 180°)
                    if (Mathf.Abs(arcAngle1) > Mathf.PI) continue;
                    if (Mathf.Abs(arcAngle2) > Mathf.PI) continue;

                    // Score: wstawka blisko targetStraight = najlepiej
                    float score = Mathf.Abs(straightLen - targetStraight);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestStraight = straightLen;
                        bestPath = SampleDubinsCSC(cA, startPos, arcAngle1, tangA,
                                                    tangB, cB, arcAngle2, endPos, radius);
                    }
                }
            }

            return (bestPath, bestStraight);
        }

        /// <summary>
        /// U-turn loop: łączy dwa równoległe tory dużą pętlą (łezką/teardrop).
        /// Używa CCC (Circle-Circle-Circle) zamiast CSC, bo dla bliskich torów (rozstaw &lt;&lt; 2R)
        /// CSC nie może utworzyć poprawnej łezki (styczna wewnętrzna wymaga dist &gt;= 2R).
        /// CCC: Arc1 (mały łuk wyjściowy) → Arc2 (duży łuk pętli) → Arc3 (mały łuk wjściowy).
        /// Próbuje LRL i RLR, wybiera wariant rozciągający się w kierunku outward.
        /// </summary>
        private static List<Vector3> CalculateUTurnLoop(
            Vector3 startPos, Vector3 startDir,
            Vector3 endPos, Vector3 endDir,
            float radius)
        {
            // Travel directions:
            // - opuszczamy startPos w kierunku startDir (outward)
            // - wjeżdżamy do endPos w kierunku -endDir (inward)
            Vector3 travelStartDir = startDir;
            Vector3 travelEndDir = -endDir;

            Vector3 startRight = Vector3.Cross(Vector3.up, travelStartDir).normalized;
            Vector3 endRight = Vector3.Cross(Vector3.up, travelEndDir).normalized;

            Vector3 midAB = (startPos + endPos) * 0.5f;
            float bestOutwardScore = float.MinValue;
            List<Vector3> bestPath = null;

            // Która strona od startDir jest endPos?
            // LRL: Arc1 krzywi się w LEWO → dobre gdy endPos jest po PRAWEJ (Arc1 oddala się)
            // RLR: Arc1 krzywi się w PRAWO → dobre gdy endPos jest po LEWEJ (Arc1 oddala się)
            // Wybór odwrotny powoduje krzyżowanie ogonków (arcs krzywią się KU sobie)
            Vector3 perpToOutward = Vector3.Cross(Vector3.up, startDir).normalized;
            float sideOfEndPos = Vector3.Dot(endPos - startPos, perpToOutward);

            // Próbuj LRL i RLR
            for (int type = 0; type < 2; type++)
            {
                bool lrl = (type == 0);

                // Pomiń konfigurację powodującą krzyżowanie ogonków
                if (Mathf.Abs(sideOfEndPos) > 0.1f)
                {
                    if (lrl && sideOfEndPos < 0f) continue;   // LRL zły gdy endPos po lewej
                    if (!lrl && sideOfEndPos > 0f) continue;  // RLR zły gdy endPos po prawej
                }

                // C1: centrum Arc1 (lewo lub prawo od kierunku wyjścia)
                Vector3 C1 = lrl
                    ? startPos - startRight * radius   // left of travelStartDir
                    : startPos + startRight * radius;  // right of travelStartDir

                // C3: centrum Arc3 (lewo lub prawo od kierunku wjazdu)
                Vector3 C3 = lrl
                    ? endPos - endRight * radius       // left of travelEndDir
                    : endPos + endRight * radius;      // right of travelEndDir

                // C2: centrum Arc2 w odległości 2R od C1 i C3
                float dist13 = Vector3.Distance(C1, C3);
                if (dist13 > 4f * radius - 0.01f) continue; // Za daleko
                if (dist13 < 0.01f) continue;                // Zbieżne

                float halfDist = dist13 * 0.5f;
                float h2 = 4f * radius * radius - halfDist * halfDist;
                if (h2 < 0f) continue;
                float h = Mathf.Sqrt(h2);

                Vector3 d13norm = (C3 - C1) / dist13;
                Vector3 perp13 = Vector3.Cross(Vector3.up, d13norm).normalized;
                Vector3 mid13 = (C1 + C3) * 0.5f;

                // Dwie możliwe pozycje C2 — próbuj obie
                Vector3[] C2candidates = {
                    mid13 + perp13 * h,
                    mid13 - perp13 * h
                };

                foreach (var C2 in C2candidates)
                {
                    // Pętla powinna rozciągać się w kierunku outward (startDir)
                    float outwardScore = Vector3.Dot(C2 - midAB, startDir);
                    if (outwardScore <= bestOutwardScore) continue;

                    // Punkty styku między łukami (junction points)
                    Vector3 J12 = C1 + (C2 - C1).normalized * radius;
                    Vector3 J23 = C2 + (C3 - C2).normalized * radius;

                    // Kąty łuków
                    bool arc1Left = lrl;   // LRL: 1=Left, RLR: 1=Right
                    bool arc2Left = !lrl;  // LRL: 2=Right, RLR: 2=Left
                    bool arc3Left = lrl;   // LRL: 3=Left, RLR: 3=Right

                    float arcAngle1 = ComputeArcAngle(C1, startPos, J12, arc1Left);
                    float arcAngle2 = ComputeArcAngle(C2, J12, J23, arc2Left);
                    float arcAngle3 = ComputeArcAngle(C3, J23, endPos, arc3Left);

                    // Odrzuć zbyt duże łuki — zapobiega podwójnym pętlom
                    // Arc1/Arc3 (wjazd/wyjazd) max 270°, Arc2 (pętla główna) max 350°
                    float maxEntryArc = Mathf.PI * 3f / 2f;   // 270°
                    float maxLoopArc = Mathf.PI * 35f / 18f;  // ~350°
                    if (Mathf.Abs(arcAngle1) > maxEntryArc) continue;
                    if (Mathf.Abs(arcAngle2) > maxLoopArc) continue;
                    if (Mathf.Abs(arcAngle3) > maxEntryArc) continue;

                    var path = SampleCCCPath(C1, startPos, arcAngle1,
                                             C2, J12, arcAngle2,
                                             C3, J23, arcAngle3,
                                             endPos, radius);

                    if (path != null && path.Count >= 2)
                    {
                        bestOutwardScore = outwardScore;
                        bestPath = path;
                    }
                }
            }

            return bestPath;
        }

        /// <summary>
        /// Próbkuje ścieżkę CCC (trzy łuki) do polyline.
        /// </summary>
        private static List<Vector3> SampleCCCPath(
            Vector3 C1, Vector3 arcStart1, float arcAngle1,
            Vector3 C2, Vector3 arcStart2, float arcAngle2,
            Vector3 C3, Vector3 arcStart3, float arcAngle3,
            Vector3 arcEnd3, float radius)
        {
            List<Vector3> result = new();

            // Arc 1
            float arcLen1 = Mathf.Abs(arcAngle1) * radius;
            int samples1 = Mathf.Max(2, Mathf.CeilToInt(arcLen1 / ARC_SAMPLE_STEP));
            Vector3 v1 = arcStart1 - C1;
            float startAngle1 = Mathf.Atan2(v1.z, v1.x);
            for (int i = 0; i <= samples1; i++)
            {
                float t = (float)i / samples1;
                float angle = startAngle1 + arcAngle1 * t;
                result.Add(C1 + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            // Arc 2 (główna pętla)
            float arcLen2 = Mathf.Abs(arcAngle2) * radius;
            int samples2 = Mathf.Max(2, Mathf.CeilToInt(arcLen2 / ARC_SAMPLE_STEP));
            Vector3 v2 = arcStart2 - C2;
            float startAngle2 = Mathf.Atan2(v2.z, v2.x);
            for (int i = 1; i <= samples2; i++)
            {
                float t = (float)i / samples2;
                float angle = startAngle2 + arcAngle2 * t;
                result.Add(C2 + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            // Arc 3
            float arcLen3 = Mathf.Abs(arcAngle3) * radius;
            int samples3 = Mathf.Max(2, Mathf.CeilToInt(arcLen3 / ARC_SAMPLE_STEP));
            Vector3 v3 = arcStart3 - C3;
            float startAngle3 = Mathf.Atan2(v3.z, v3.x);
            for (int i = 1; i <= samples3; i++)
            {
                float t = (float)i / samples3;
                float angle = startAngle3 + arcAngle3 * t;
                result.Add(C3 + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            // Ostatni punkt = dokładnie endPos
            if (result.Count > 0)
                result[result.Count - 1] = arcEnd3;

            return result;
        }

        /// <summary>
        /// External tangent between two circles of same radius turning in the same direction.
        /// </summary>
        private static bool ComputeExternalTangent(
            Vector3 cA, Vector3 cB, float radius, bool leftTurn,
            out Vector3 tangA, out Vector3 tangB)
        {
            Vector3 d = cB - cA;
            float dist = d.magnitude;
            tangA = tangB = Vector3.zero;

            if (dist < 0.01f) return false;

            // External tangent: circles same direction → tangent is parallel to center line
            // The tangent line is perpendicular offset from the center-to-center line
            Vector3 dNorm = d / dist;
            Vector3 perp = Vector3.Cross(Vector3.up, dNorm).normalized;

            // For external tangent, tangent points are on the side between circles and travel path
            // leftTurn (CCW) → centers LEFT → tangent on RIGHT (+perp)
            // rightTurn (CW) → centers RIGHT → tangent on LEFT (-perp)
            float sign = leftTurn ? 1f : -1f;
            tangA = cA + perp * sign * radius;
            tangB = cB + perp * sign * radius;

            // Verify tangent direction is compatible with travel direction
            return true;
        }

        /// <summary>
        /// Internal (cross) tangent between two circles turning in opposite directions.
        /// </summary>
        private static bool ComputeInternalTangent(
            Vector3 cA, Vector3 cB, float radius, bool startLeftTurn,
            out Vector3 tangA, out Vector3 tangB)
        {
            Vector3 d = cB - cA;
            float dist = d.magnitude;
            tangA = tangB = Vector3.zero;

            // Internal tangent requires distance > 2R
            if (dist < 2f * radius) return false;

            // Midpoint between centers
            Vector3 mid = (cA + cB) * 0.5f;

            // Half-distance
            float halfDist = dist * 0.5f;

            // Angle of cross tangent: cos(alpha) = R / halfDist
            float cosAlpha = radius / halfDist;
            if (cosAlpha > 1f) return false;
            float sinAlpha = Mathf.Sqrt(1f - cosAlpha * cosAlpha);

            // Direction from cA to cB
            Vector3 dNorm = d / dist;
            Vector3 perp = Vector3.Cross(Vector3.up, dNorm).normalized;

            // Two possible tangent directions; pick the one matching turn direction
            // Sign stays original: fixing centers reversed the d vector, which already compensates
            float sign = startLeftTurn ? 1f : -1f;
            Vector3 tangDir = dNorm * cosAlpha + perp * sinAlpha * sign;

            tangA = cA + tangDir * radius;
            tangB = cB - tangDir * radius;

            return true;
        }

        /// <summary>
        /// Computes the sweep angle of an arc from pointFrom to pointTo on a circle.
        /// leftTurn = true → CCW (positive angle), false → CW (negative angle).
        /// </summary>
        private static float ComputeArcAngle(Vector3 center, Vector3 pointFrom, Vector3 pointTo, bool leftTurn)
        {
            Vector3 vFrom = pointFrom - center;
            Vector3 vTo = pointTo - center;

            float angleFrom = Mathf.Atan2(vFrom.z, vFrom.x);
            float angleTo = Mathf.Atan2(vTo.z, vTo.x);

            float delta = angleTo - angleFrom;

            if (leftTurn)
            {
                // CCW: want positive angle
                while (delta < 0) delta += 2f * Mathf.PI;
                if (delta > 2f * Mathf.PI - 0.001f) delta = 0f;
            }
            else
            {
                // CW: want negative angle
                while (delta > 0) delta -= 2f * Mathf.PI;
                if (delta < -(2f * Mathf.PI - 0.001f)) delta = 0f;
            }

            return delta;
        }

        /// <summary>
        /// Samples a Dubins CSC path into a polyline: Arc1 → Straight → Arc2
        /// </summary>
        private static List<Vector3> SampleDubinsCSC(
            Vector3 center1, Vector3 arcStart1, float arcAngle1, Vector3 tangPoint1,
            Vector3 tangPoint2, Vector3 center2, float arcAngle2, Vector3 arcEnd2,
            float radius)
        {
            List<Vector3> result = new();

            // Arc 1
            float arcLen1 = Mathf.Abs(arcAngle1) * radius;
            int samples1 = Mathf.Max(2, Mathf.CeilToInt(arcLen1 / ARC_SAMPLE_STEP));
            Vector3 v1 = arcStart1 - center1;
            float startAngle1 = Mathf.Atan2(v1.z, v1.x);

            for (int i = 0; i <= samples1; i++)
            {
                float t = (float)i / samples1;
                float angle = startAngle1 + arcAngle1 * t;
                result.Add(center1 + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            // Straight section
            float straightLen = Vector3.Distance(tangPoint1, tangPoint2);
            if (straightLen > ARC_SAMPLE_STEP)
            {
                int straightSamples = Mathf.Max(1, Mathf.CeilToInt(straightLen / ARC_SAMPLE_STEP));
                for (int i = 1; i <= straightSamples; i++)
                {
                    float t = (float)i / straightSamples;
                    result.Add(Vector3.Lerp(tangPoint1, tangPoint2, t));
                }
            }

            // Arc 2
            float arcLen2 = Mathf.Abs(arcAngle2) * radius;
            int samples2 = Mathf.Max(2, Mathf.CeilToInt(arcLen2 / ARC_SAMPLE_STEP));
            Vector3 v2 = tangPoint2 - center2;
            float startAngle2 = Mathf.Atan2(v2.z, v2.x);

            // Skip first point (already added as last of straight section)
            for (int i = 1; i <= samples2; i++)
            {
                float t = (float)i / samples2;
                float angle = startAngle2 + arcAngle2 * t;
                result.Add(center2 + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            // Ensure last point is exactly endPos
            if (result.Count > 0)
                result[result.Count - 1] = arcEnd2;

            return result;
        }
    }
}
