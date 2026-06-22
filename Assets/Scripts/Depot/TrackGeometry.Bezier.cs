using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public static partial class TrackGeometry
    {
        // ═══════════════════════════════════════════
        //  KRZYWA BÉZIERA (bothSnapped, gładka trasa)
        // ═══════════════════════════════════════════

        /// <summary>
        /// Gładka trasa bothSnapped: kubiczny Bézier z tangencją na obu końcach.
        /// Nigdy nie tworzy prostych odcinków — krzywa jest gładka z konstrukcji.
        /// Próbuje ±startDir × ±endDir i wybiera ścieżkę z najlepszym min. promieniem.
        /// Penalizuje S-krzywe (control pointy po przeciwnych stronach linii A→B),
        /// bo tworzą pozorną prostą między endpointami.
        /// </summary>
        public static List<Vector3> CalculateBezierRoute(
            Vector3 startPos, Vector3 startDir,
            Vector3 endPos, Vector3 endDir,
            float minRadius)
        {
            float dist = Vector3.Distance(startPos, endPos);
            if (dist < MIN_TRACK_LENGTH) return null;

            Vector3 toEnd = (endPos - startPos).normalized;
            // Prostopadła do linii A→B (do wykrywania S-krzywych)
            Vector3 perpAB = Vector3.Cross(Vector3.up, toEnd).normalized;

            List<Vector3> bestPath = null;
            float bestScore = float.MinValue;

            // Cubic Bézier: P0=startPos, P1=startPos+sDir*d, P2=endPos+eDir*d, P3=endPos
            // Tangenta wyjścia z P0 = sDir (outward z A)
            // Tangenta wjazdu do P3 = -eDir (inward do B)
            Vector3[] sDirs = { startDir, -startDir };
            Vector3[] eDirs = { endDir, -endDir };

            foreach (var sDir in sDirs)
            {
                foreach (var eDir in eDirs)
                {
                    // Ile każdy kierunek jest "do przodu" (w stronę A→B)
                    float sFwd = Vector3.Dot(sDir, toEnd);
                    float eFwd = Vector3.Dot(eDir, -toEnd);

                    // Odległość control pointów od endpointów
                    float d;
                    if (sFwd > 0.5f && eFwd > 0.5f)
                    {
                        // Forward: oba tangenty wskazują na siebie → łagodna krzywa
                        d = dist * 0.4f;
                    }
                    else if (sFwd < 0.5f && eFwd < 0.5f)
                    {
                        // Nie do przodu: U-turn / prostopadłe / do tyłu → duży d na pętlę
                        d = Mathf.Max(dist, minRadius);
                    }
                    else
                    {
                        // Mieszany układ (jeden do przodu, drugi nie)
                        d = Mathf.Max(dist * 0.55f, minRadius * 0.5f);
                    }

                    // Minimum d żeby uniknąć zdegenerowanych płaskich krzywych
                    d = Mathf.Max(d, 5f);

                    Vector3 P0 = startPos;
                    Vector3 P1 = startPos + sDir * d;
                    Vector3 P2 = endPos + eDir * d;
                    Vector3 P3 = endPos;

                    var path = SampleCubicBezier(P0, P1, P2, P3);
                    if (path == null || path.Count < 3) continue;

                    float pathMinR = GetMinimumRadius(path);
                    float pathLen = CalculatePolylineLength(path);

                    // --- Scoring ---
                    float score;
                    if (pathMinR >= minRadius * 0.9f)
                        score = 10000f - pathLen;   // spełnia R → preferuj krótszą
                    else
                        score = pathMinR;            // nie spełnia R → preferuj większy R

                    // S-krzywa: control pointy po przeciwnych stronach linii A→B
                    // S-krzywe przechodzą przez środek między endpointami,
                    // co wygląda jak prosta między nimi → ciężka penalizacja
                    float sSide = Vector3.Dot(sDir, perpAB);
                    float eSide = Vector3.Dot(eDir, perpAB);
                    if (sSide * eSide < -0.01f)
                        score -= 5000f;

                    // Preferuj oryginalne kierunki outward (nie odwrócone)
                    float dirMatch = Vector3.Dot(sDir, startDir) + Vector3.Dot(eDir, endDir);
                    score += dirMatch * 0.5f;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPath = path;
                    }
                }
            }

            return bestPath;
        }

        /// <summary>
        /// Wygładzanie Laplacian: każdy wewnętrzny punkt → średnia z sąsiadów.
        /// Endpoints zachowane. Zaokrągla ostre przejścia.
        /// </summary>
        private static List<Vector3> SmoothPolyline(List<Vector3> points, int iterations)
        {
            var result = new List<Vector3>(points);

            for (int iter = 0; iter < iterations; iter++)
            {
                var smoothed = new List<Vector3>(result.Count);
                smoothed.Add(result[0]); // Zachowaj start

                for (int i = 1; i < result.Count - 1; i++)
                {
                    Vector3 avg = (result[i - 1] + result[i] + result[i + 1]) / 3f;
                    avg.y = 0;
                    smoothed.Add(avg);
                }

                smoothed.Add(result[result.Count - 1]); // Zachowaj koniec
                result = smoothed;
            }

            return result;
        }

        /// <summary>
        /// Próbkuje kubiczną krzywą Béziera.
        /// </summary>
        private static List<Vector3> SampleCubicBezier(Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3)
        {
            float approxLen = Vector3.Distance(P0, P1) + Vector3.Distance(P1, P2) + Vector3.Distance(P2, P3);
            int samples = Mathf.Max(10, Mathf.CeilToInt(approxLen / ARC_SAMPLE_STEP));

            List<Vector3> result = new();
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float u = 1f - t;
                Vector3 point = u * u * u * P0
                              + 3f * u * u * t * P1
                              + 3f * u * t * t * P2
                              + t * t * t * P3;
                point.y = 0f;
                result.Add(point);
            }
            return result;
        }
    }
}
