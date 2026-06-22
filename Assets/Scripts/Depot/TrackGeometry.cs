using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// Statyczna klasa obliczeń geometrii torowej.
    /// Oblicza trasy między dwoma punktami jako polyline (lista Vector3).
    /// Wspiera: odcinki proste, łuki kołowe z promieniem R, auto-fit.
    /// Wszystkie obliczenia w płaszczyźnie XZ (Y = 0).
    ///
    /// Klasa rozbita na partial files per algorytm:
    /// - <c>TrackGeometry.cs</c>        — stałe, glowne API: CalculateRoute*, CalculateConnector (ten plik)
    /// - <c>TrackGeometry.Bezier.cs</c> — kubiczny Bezier (gladka trasa bothSnapped) + smoothing
    /// - <c>TrackGeometry.Arc.cs</c>    — promien lukow, GenerateStraightLine, CalculateArcRoute,
    ///                                    GenerateArcFromParams (recznie parametry)
    /// - <c>TrackGeometry.Dubins.cs</c> — Dubins CSC + CCC U-turn loop + tangent helpers
    ///                                    (External/Internal tangent, ComputeArcAngle, samplers)
    /// - <c>TrackGeometry.Utils.cs</c>  — analiza polyline: GetMinimumRadius, length, tangents,
    ///                                    GetPointAtDistance, OffsetPolyline, projection, IsStraight
    /// </summary>
    public static partial class TrackGeometry
    {
        /// <summary>Minimalny promień łuku (m)</summary>
        public const float MIN_RADIUS = 75f;

        /// <summary>Odległość między próbkami na łuku (m)</summary>
        public const float ARC_SAMPLE_STEP = 1f;

        /// <summary>Minimalna długość toru (m)</summary>
        public const float MIN_TRACK_LENGTH = 2f;

        /// <summary>Kąt poniżej którego trasa uznawana za prostą (stopnie)</summary>
        public const float STRAIGHT_THRESHOLD_DEG = 1f;

        // ═══════════════════════════════════════════
        //  GŁÓWNE METODY OBLICZENIOWE
        // ═══════════════════════════════════════════

        /// <summary>
        /// Oblicza optymalną trasę między dwoma punktami z podanymi kierunkami.
        /// Auto-fit: dobiera promień R automatycznie (największy możliwy).
        /// </summary>
        /// <param name="startPos">Pozycja punktu A</param>
        /// <param name="startDir">Kierunek wyjścia z punktu A (znormalizowany)</param>
        /// <param name="endPos">Pozycja punktu B</param>
        /// <param name="endDir">Kierunek wjazdu do punktu B (znormalizowany)</param>
        /// <param name="minRadius">Minimalny dopuszczalny promień łuku</param>
        /// <param name="bothSnapped">Czy oba końce snappowane do istniejących torów (CSC)</param>
        /// <returns>Polyline - lista punktów trasy</returns>
        public static List<Vector3> CalculateRouteAutoFit(
            Vector3 startPos, Vector3 startDir,
            Vector3 endPos, Vector3 endDir,
            float minRadius = MIN_RADIUS,
            bool bothSnapped = false)
        {
            // Spłaszcz do XZ
            startPos.y = 0;
            endPos.y = 0;
            startDir.y = 0;
            endDir.y = 0;
            startDir.Normalize();
            endDir.Normalize();

            float distance = Vector3.Distance(startPos, endPos);
            if (distance < MIN_TRACK_LENGTH)
                return new List<Vector3> { startPos, endPos };

            // Sprawdź czy punkty są współliniowe
            Vector3 toEnd = (endPos - startPos).normalized;
            float angleBetween = Vector3.Angle(startDir, toEnd);

            // endDir akceptujemy w obu kierunkach wzdłuż linii (±toEnd)
            float endAlignment = Mathf.Min(Vector3.Angle(endDir, toEnd), Vector3.Angle(endDir, -toEnd));
            if (angleBetween < STRAIGHT_THRESHOLD_DEG && (!bothSnapped || endAlignment < STRAIGHT_THRESHOLD_DEG))
            {
                // Prosta trasa
                return GenerateStraightLine(startPos, endPos);
            }

            if (bothSnapped)
            {
                // Kierunki podróży:
                // - startDir = outward od startu = kierunek WYJAZDU z node'a ✓
                // - endDir = outward od końca, ale nowy tor WJEŻDŻA → travel = -endDir (inward)
                Vector3 travelEndDir = -endDir;

                // === KROK 1: CSC z dużymi promieniami — od największego ===
                // Im większy R, tym łagodniejsze łuki i krótsza wstawka prosta
                float[] radiiToTry = {
                    distance * 5f,
                    distance * 3f,
                    distance * 2f,
                    distance * 1.5f,
                    distance * 1f,
                    distance * 0.7f,
                    distance * 0.45f,
                    minRadius,
                    Mathf.Max(distance * 0.2f, 1f),
                };

                // Sortuj malejąco — największy promień najpierw
                System.Array.Sort(radiiToTry);
                System.Array.Reverse(radiiToTry);

                foreach (float tryR in radiiToTry)
                {
                    if (tryR < minRadius * 0.5f) continue;
                    var csc = ComputeDubinsCSCForDirections(
                        startPos, startDir, endPos, travelEndDir, tryR,
                        maxArcAngle: Mathf.PI);
                    if (csc != null && csc.Count >= 2)
                    {
                        float cscMinR = GetMinimumRadius(csc);
                        if (cscMinR >= minRadius * 0.9f)
                            return csc;
                    }
                }

                // === KROK 2: CSC nie znalazł forward path → próbuj CCC U-turn ===
                // Tylko dla mniej-więcej równoległych torów (outward dirs w tę samą stronę)
                float dirDot = Vector3.Dot(startDir, endDir);
                if (dirDot > 0.3f)
                {
                    Vector3 startLeft = Vector3.Cross(startDir, Vector3.up).normalized;
                    float perpDist = Mathf.Abs(Vector3.Dot(endPos - startPos, startLeft));

                    // Promień proporcjonalny do rozstawu i dystansu
                    float baseFromPerp = perpDist * 0.5f;
                    float baseFromDist = distance * 0.3f;
                    float baseRadius = Mathf.Max(Mathf.Max(baseFromPerp, baseFromDist), 2f);

                    float[] uturnRadii = {
                        baseRadius,
                        baseRadius * 1.5f,
                        baseRadius * 2.5f,
                    };

                    List<Vector3> bestUturn = null;
                    float bestUturnLen = float.MaxValue;

                    foreach (float uR in uturnRadii)
                    {
                        var uturnResult = CalculateUTurnLoop(
                            startPos, startDir, endPos, endDir, uR);
                        if (uturnResult != null && uturnResult.Count >= 2)
                        {
                            float uturnLen = CalculatePolylineLength(uturnResult);
                            if (uturnLen < bestUturnLen)
                            {
                                bestUturnLen = uturnLen;
                                bestUturn = uturnResult;
                            }
                        }
                    }

                    if (bestUturn != null)
                        return bestUturn;
                }
            }

            // Fallback: pojedynczy łuk z faktycznym promieniem (predykcja na czerwono gdy R < minRadius)
            float requiredRadius = CalculateRequiredRadius(startPos, startDir, endPos, endDir);
            if (float.IsInfinity(requiredRadius) || float.IsNaN(requiredRadius) || requiredRadius > 100000f)
                requiredRadius = minRadius;

            return CalculateArcRoute(startPos, startDir, endPos, endDir, requiredRadius);
        }

        /// <summary>
        /// Oblicza trasę z podanym promieniem łuku.
        /// bothSnapped=true: Arc1 → Straight → Arc2 (Dubins CSC)
        /// bothSnapped=false: Arc → Straight (prosty łuk)
        /// </summary>
        public static List<Vector3> CalculateRoute(
            Vector3 startPos, Vector3 startDir,
            Vector3 endPos, Vector3 endDir,
            float radius,
            bool bothSnapped = false)
        {
            // Spłaszcz do XZ
            startPos.y = 0;
            endPos.y = 0;
            startDir.y = 0;
            endDir.y = 0;
            startDir.Normalize();
            endDir.Normalize();

            float distance = Vector3.Distance(startPos, endPos);
            if (distance < MIN_TRACK_LENGTH)
                return new List<Vector3> { startPos, endPos };

            // Sprawdź współliniowość
            Vector3 toEnd = (endPos - startPos).normalized;
            float angleBetween = Vector3.Angle(startDir, toEnd);

            float endAlignment2 = Mathf.Min(Vector3.Angle(endDir, toEnd), Vector3.Angle(endDir, -toEnd));
            if (angleBetween < STRAIGHT_THRESHOLD_DEG && (!bothSnapped || endAlignment2 < STRAIGHT_THRESHOLD_DEG))
                return GenerateStraightLine(startPos, endPos);

            if (bothSnapped)
            {
                var cscResult = CalculateDubinsCSC(startPos, startDir, endPos, endDir, Mathf.Max(radius, MIN_RADIUS));
                if (cscResult != null && cscResult.Count >= 2)
                    return cscResult;
            }

            return CalculateArcRoute(startPos, startDir, endPos, endDir, radius);
        }

        /// <summary>
        /// Generuje polyline prostą + łuk kołowy do toru równoległego.
        /// Używane przy generowaniu łączników rozjazdowych.
        /// </summary>
        /// <param name="origin">Punkt rozgałęzienia</param>
        /// <param name="direction">Kierunek toru głównego</param>
        /// <param name="targetPos">Pozycja początku toru równoległego</param>
        /// <param name="radius">Promień łuku</param>
        /// <returns>Polyline łącznika</returns>
        public static List<Vector3> CalculateConnector(
            Vector3 origin, Vector3 direction,
            Vector3 targetPos, float radius)
        {
            // Kierunek do celu
            Vector3 targetDir = (targetPos - origin).normalized;

            // Użyj CalculateRoute z kierunkiem startowym = direction, końcowym = direction
            // (tor równoległy ma ten sam kierunek co główny)
            return CalculateRoute(origin, direction, targetPos, direction, radius);
        }
    }
}
