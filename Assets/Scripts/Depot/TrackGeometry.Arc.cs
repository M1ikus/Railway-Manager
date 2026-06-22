using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public static partial class TrackGeometry
    {
        // ═══════════════════════════════════════════
        //  PROMIEŃ I ŁUK
        // ═══════════════════════════════════════════

        /// <summary>
        /// Oblicza wymagany promień łuku aby połączyć dwa punkty z kierunkami.
        /// </summary>
        public static float CalculateRequiredRadius(
            Vector3 startPos, Vector3 startDir,
            Vector3 endPos, Vector3 endDir)
        {
            startPos.y = 0; endPos.y = 0;
            startDir.y = 0; endDir.y = 0;
            startDir.Normalize(); endDir.Normalize();

            Vector3 toEnd = endPos - startPos;
            float distance = toEnd.magnitude;
            if (distance < 0.01f) return MIN_RADIUS;

            // Odchylenie boczne od kierunku startDir
            Vector3 perpStart = Vector3.Cross(Vector3.up, startDir).normalized;
            float lateralOffset = Mathf.Abs(Vector3.Dot(toEnd, perpStart));
            float longitudinalOffset = Vector3.Dot(toEnd, startDir);

            if (lateralOffset < 0.01f) return float.MaxValue; // Współliniowe

            // Dla prostego łuku łączącego dwa punkty:
            // R = (d² + h²) / (2h) gdzie d = longitudinal, h = lateral
            float R = (longitudinalOffset * longitudinalOffset + lateralOffset * lateralOffset)
                      / (2f * lateralOffset);

            return Mathf.Abs(R);
        }

        // ═══════════════════════════════════════════
        //  GENERATORY POLYLINE
        // ═══════════════════════════════════════════

        /// <summary>
        /// Generuje prostą linię między dwoma punktami z próbkowaniem co 1m.
        /// </summary>
        public static List<Vector3> GenerateStraightLine(Vector3 start, Vector3 end)
        {
            List<Vector3> points = new();
            float distance = Vector3.Distance(start, end);
            int samples = Mathf.Max(2, Mathf.CeilToInt(distance / ARC_SAMPLE_STEP));

            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                points.Add(Vector3.Lerp(start, end, t));
            }

            return points;
        }

        /// <summary>
        /// Oblicza trasę z łukiem kołowym: prosta → łuk → prosta
        /// Używa podejścia "dubins-like": dwa centra łuków, wybór krótszej trasy
        /// </summary>
        public static List<Vector3> CalculateArcRoute(
            Vector3 startPos, Vector3 startDir,
            Vector3 endPos, Vector3 endDir,
            float radius)
        {
            // Cross(up, dir) daje PRAWY prostopadły w Unity (left-handed)
            Vector3 perpRight = Vector3.Cross(Vector3.up, startDir).normalized;

            // Centrum po prawej → obrót CW (clockwise od góry)
            // Centrum po lewej → obrót CCW (counterclockwise od góry)
            Vector3 centerRight = startPos + perpRight * radius;
            Vector3 centerLeft = startPos - perpRight * radius;

            // Kierunek skrętu = po której stronie startDir jest cel
            Vector3 toEnd = (endPos - startPos).normalized;
            float cross = startDir.x * toEnd.z - startDir.z * toEnd.x;

            Vector3 arcCenter;
            bool ccw;

            if (cross < 0)
            {
                arcCenter = centerRight;
                ccw = false; // CW
            }
            else
            {
                arcCenter = centerLeft;
                ccw = true; // CCW
            }

            // startAngle na okręgu
            Vector3 toStart = startPos - arcCenter;
            float startAngle = Mathf.Atan2(toStart.z, toStart.x);

            // Oblicz kąt końcowy łuku — tangent-based approach
            float distToEnd = Vector3.Distance(arcCenter, endPos);
            float endAngle;

            if (distToEnd <= radius * 1.02f)
            {
                // endPos jest na okręgu (lub bardzo blisko) — projekcja wystarczy
                Vector3 centerToEnd = (endPos - arcCenter).normalized;
                endAngle = Mathf.Atan2(centerToEnd.z, centerToEnd.x);
            }
            else
            {
                // endPos jest POZA okręgiem — znajdź punkt styczny
                // W punkcie stycznym prosta do endPos jest styczna do łuku (brak załamania 90°)
                float alpha = Mathf.Acos(Mathf.Clamp(radius / distToEnd, -1f, 1f));
                Vector3 centerToEndDir = endPos - arcCenter;
                float psi = Mathf.Atan2(centerToEndDir.z, centerToEndDir.x);

                // CW → punkt styczny w ψ+α, CCW → punkt styczny w ψ−α
                endAngle = ccw ? (psi - alpha) : (psi + alpha);
            }

            // Oblicz kąt łuku z poprawnym zawijaniem
            float arcAngle = endAngle - startAngle;

            if (ccw)
            {
                while (arcAngle <= 0) arcAngle += 2f * Mathf.PI;
                while (arcAngle > 2f * Mathf.PI) arcAngle -= 2f * Mathf.PI;
            }
            else
            {
                while (arcAngle >= 0) arcAngle -= 2f * Mathf.PI;
                while (arcAngle < -2f * Mathf.PI) arcAngle += 2f * Mathf.PI;
            }

            // Generuj polyline
            List<Vector3> result = new();

            // 1. Łuk kołowy
            float arcLength = Mathf.Abs(arcAngle) * radius;
            int arcSamples = Mathf.Max(3, Mathf.CeilToInt(arcLength / ARC_SAMPLE_STEP));

            for (int i = 0; i <= arcSamples; i++)
            {
                float t = (float)i / arcSamples;
                float angle = startAngle + arcAngle * t;

                Vector3 point = arcCenter + new Vector3(
                    Mathf.Cos(angle) * radius,
                    0f,
                    Mathf.Sin(angle) * radius
                );
                result.Add(point);
            }

            // 2. Prosta styczna od końca łuku do endPos (jeśli jest odległość)
            Vector3 arcEnd = result[result.Count - 1];
            float straightDist = Vector3.Distance(arcEnd, endPos);

            if (straightDist > ARC_SAMPLE_STEP)
            {
                int straightSamples = Mathf.Max(1, Mathf.CeilToInt(straightDist / ARC_SAMPLE_STEP));
                for (int i = 1; i <= straightSamples; i++)
                {
                    float t = (float)i / straightSamples;
                    result.Add(Vector3.Lerp(arcEnd, endPos, t));
                }
            }
            else if (straightDist > 0.01f)
            {
                result.Add(endPos);
            }

            return result;
        }

        /// <summary>
        /// Generuje łuk z podanych parametrów: pozycja startowa, kierunek, promień, długość łuku.
        /// Używane przy ręcznej edycji parametrów toru.
        /// </summary>
        public static List<Vector3> GenerateArcFromParams(
            Vector3 startPos, Vector3 startDir, float radius, float arcLength, bool turnLeft)
        {
            startPos.y = 0;
            startDir.y = 0;
            startDir.Normalize();

            float arcAngle = arcLength / radius;

            // Cross(up, dir) daje PRAWĄ prostopadłą w Unity (left-handed)
            Vector3 perpRight = Vector3.Cross(Vector3.up, startDir).normalized;
            Vector3 center = turnLeft
                ? startPos - perpRight * radius   // centrum po lewej
                : startPos + perpRight * radius;  // centrum po prawej

            Vector3 toStart = startPos - center;
            float startAngle = Mathf.Atan2(toStart.z, toStart.x);
            // Lewo = CCW (dodatni kąt), Prawo = CW (ujemny kąt)
            float sweepAngle = turnLeft ? arcAngle : -arcAngle;

            int samples = Mathf.Max(3, Mathf.CeilToInt(arcLength / ARC_SAMPLE_STEP));
            List<Vector3> result = new();

            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float angle = startAngle + sweepAngle * t;
                result.Add(center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            return result;
        }
    }
}
