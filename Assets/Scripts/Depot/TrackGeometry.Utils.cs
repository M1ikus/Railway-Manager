using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public static partial class TrackGeometry
    {
        // ═══════════════════════════════════════════
        //  HELPER METHODS — polyline analysis utils
        // ═══════════════════════════════════════════

        /// <summary>
        /// Oblicza minimalny promień krzywizny w polyline.
        /// Używa wzoru na promień okręgu opisanego na 3 kolejnych punktach (dokładny dla łuków).
        /// R = |AB| * |BC| * |AC| / (4 * pole_trójkąta)
        /// </summary>
        public static float GetMinimumRadius(List<Vector3> polyline)
        {
            if (polyline == null || polyline.Count < 3) return float.MaxValue;

            float minR = float.MaxValue;

            for (int i = 1; i < polyline.Count - 1; i++)
            {
                Vector3 a = polyline[i - 1];
                Vector3 b = polyline[i];
                Vector3 c = polyline[i + 1];

                Vector3 ab = b - a;
                Vector3 bc = c - b;
                Vector3 ac = c - a;

                float abLen = ab.magnitude;
                float bcLen = bc.magnitude;
                float acLen = ac.magnitude;

                if (abLen < 0.01f || bcLen < 0.01f) continue;

                // 2 * pole trójkąta = |cross(AB, AC)|
                float crossMag = Vector3.Cross(ab, ac).magnitude;
                if (crossMag < 0.0001f) continue; // współliniowe

                // Promień okręgu opisanego: R = (|AB| * |BC| * |AC|) / (2 * |cross|)
                float r = (abLen * bcLen * acLen) / (2f * crossMag);

                if (r < minR) minR = r;
            }

            return minR;
        }

        /// <summary>
        /// Oblicza długość polyline (suma odcinków)
        /// </summary>
        public static float CalculatePolylineLength(List<Vector3> polyline)
        {
            if (polyline == null || polyline.Count < 2) return 0f;

            float length = 0f;
            for (int i = 1; i < polyline.Count; i++)
                length += Vector3.Distance(polyline[i - 1], polyline[i]);

            return length;
        }

        /// <summary>
        /// Oblicza tangentę (kierunek) w danym punkcie polyline
        /// </summary>
        public static Vector3 GetTangentAtIndex(List<Vector3> polyline, int index)
        {
            if (polyline == null || polyline.Count < 2)
                return Vector3.forward;

            if (index <= 0)
                return (polyline[1] - polyline[0]).normalized;
            if (index >= polyline.Count - 1)
                return (polyline[polyline.Count - 1] - polyline[polyline.Count - 2]).normalized;

            return (polyline[index + 1] - polyline[index - 1]).normalized;
        }

        /// <summary>
        /// Oblicza tangentę na początku polyline
        /// </summary>
        public static Vector3 GetStartTangent(List<Vector3> polyline)
        {
            return GetTangentAtIndex(polyline, 0);
        }

        /// <summary>
        /// Oblicza tangentę na końcu polyline
        /// </summary>
        public static Vector3 GetEndTangent(List<Vector3> polyline)
        {
            return GetTangentAtIndex(polyline, polyline.Count - 1);
        }

        /// <summary>
        /// Znajduje punkt na polyline w danej odległości od początku.
        /// Zwraca (pozycja, tangenta, indeks_segmentu).
        /// </summary>
        public static (Vector3 position, Vector3 tangent) GetPointAtDistance(
            List<Vector3> polyline, float distance)
        {
            if (polyline == null || polyline.Count < 2)
                return (Vector3.zero, Vector3.forward);

            float accumulated = 0f;

            for (int i = 1; i < polyline.Count; i++)
            {
                float segLen = Vector3.Distance(polyline[i - 1], polyline[i]);
                if (accumulated + segLen >= distance)
                {
                    float t = (distance - accumulated) / segLen;
                    Vector3 pos = Vector3.Lerp(polyline[i - 1], polyline[i], t);
                    Vector3 tang = (polyline[i] - polyline[i - 1]).normalized;
                    return (pos, tang);
                }
                accumulated += segLen;
            }

            // Poza końcem - zwróć ostatni punkt
            return (polyline[polyline.Count - 1], GetEndTangent(polyline));
        }

        /// <summary>
        /// Generuje offsetowaną polyline (do szyn po lewej/prawej stronie).
        /// Offset jest prostopadły do tangenty w każdym punkcie.
        /// </summary>
        /// <param name="centerline">Polyline osi toru</param>
        /// <param name="offset">Odległość offsetu (+ = lewo, - = prawo)</param>
        /// <returns>Offsetowana polyline</returns>
        public static List<Vector3> OffsetPolyline(List<Vector3> centerline, float offset)
        {
            if (centerline == null || centerline.Count < 2) return new List<Vector3>();

            List<Vector3> result = new();

            for (int i = 0; i < centerline.Count; i++)
            {
                Vector3 tangent = GetTangentAtIndex(centerline, i);
                Vector3 perpendicular = Vector3.Cross(Vector3.up, tangent).normalized;
                result.Add(centerline[i] + perpendicular * offset);
            }

            return result;
        }

        /// <summary>
        /// Sprawdza czy polyline jest prostoliniowa.
        /// Mierzy maksymalne odchylenie boczne (perpendicular) od linii start→end.
        /// </summary>
        public static bool IsStraightPolyline(List<Vector3> polyline, float tolerance = 0.15f)
        {
            if (polyline == null || polyline.Count < 2) return true;

            Vector3 start = polyline[0];
            Vector3 end = polyline[polyline.Count - 1];
            Vector3 lineDir = (end - start);
            float lineLen = lineDir.magnitude;
            if (lineLen < 0.001f) return true;
            lineDir /= lineLen;

            for (int i = 1; i < polyline.Count - 1; i++)
            {
                Vector3 toPoint = polyline[i] - start;
                float proj = Vector3.Dot(toPoint, lineDir);
                Vector3 closest = start + lineDir * proj;
                float perpDist = Vector3.Distance(polyline[i], closest);
                if (perpDist > tolerance) return false;
            }

            return true;
        }

        /// <summary>
        /// Rzutuje punkt na polyline. Zwraca dystans wzdłuż polyline do najbliższego punktu.
        /// </summary>
        public static float ProjectPointOnPolyline(List<Vector3> polyline, Vector3 point)
        {
            if (polyline == null || polyline.Count < 2) return 0f;

            float bestDist = float.MaxValue;
            float bestAlongDist = 0f;
            float accumulated = 0f;

            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector3 a = polyline[i];
                Vector3 b = polyline[i + 1];
                float segLen = Vector3.Distance(a, b);
                if (segLen < 0.001f) { accumulated += segLen; continue; }

                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / (segLen * segLen));
                Vector3 closest = a + ab * t;
                float dist = Vector3.Distance(point, closest);

                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestAlongDist = accumulated + t * segLen;
                }
                accumulated += segLen;
            }

            return bestAlongDist;
        }

        /// <summary>
        /// Wykrywa czy polyline skręca w lewo czy w prawo.
        /// Zwraca true dla skrętu w lewo.
        /// W Unity (left-handed): Cross(start, end).y &lt; 0 = skręt w lewo.
        /// </summary>
        public static bool DetectTurnLeft(List<Vector3> polyline)
        {
            if (polyline == null || polyline.Count < 3) return true; // domyślnie lewo

            Vector3 startTangent = GetStartTangent(polyline);
            Vector3 endTangent = GetEndTangent(polyline);

            // W Unity (left-handed): ujemny crossY = skręt w lewo
            float crossY = Vector3.Cross(startTangent, endTangent).y;
            return crossY < 0f;
        }
    }
}
