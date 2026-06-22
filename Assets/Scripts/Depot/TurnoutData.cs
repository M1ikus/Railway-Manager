using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// Definicje rozjazdów kolejowych.
    /// R190 1:9 i R300 1:9 - standardowe rozjazdy zajezdniowe/kolejowe.
    /// </summary>
    public static class TurnoutData
    {
        /// <summary>
        /// Definicja rozjazdu
        /// </summary>
        public struct TurnoutDefinition
        {
            public string Name;
            public float Radius;            // Promień łuku (m)
            public float FrogRatio;         // Stosunek krzyżownicy (np. 9 z "1:9")
            public float FrogAngle;         // Kąt krzyżownicy (rad)
            public float Length;            // Długość całkowita rozjazdu (m)
            public float ArcLength;         // Długość części łukowej nogi odgałęziającej (m)
            public float StraightPrefix;    // Prosta PRZED łukiem w nodze odgałęziającej (m)
            public float StraightExtension; // Prosta za łukiem w nodze odgałęziającej (m)
            public float TangentLength;     // Długość stycznej (m)
        }

        // ═══════════════════════════════════════════
        //  PREDEFINIOWANE ROZJAZDY
        // ═══════════════════════════════════════════

        /// <summary>
        /// Rozjazd R190 1:9 - typowy rozjazd zajezdniowy
        /// Łuk 21.02487m + prosta 6.092377m = 27.117247m
        /// </summary>
        public static readonly TurnoutDefinition R190_1_9 = CreateTurnout(
            "R190 1:9", 190f, 9f,
            overrideLength: 27.117247f,
            overrideStraightExtension: 6.092377f);

        /// <summary>
        /// Rozjazd R300 1:9 - standardowy rozjazd kolejowy
        /// Łuk 33.19717m, długość całkowita 33.23m
        /// </summary>
        public static readonly TurnoutDefinition R300_1_9 = CreateTurnout(
            "R300 1:9", 300f, 9f,
            overrideLength: 33.23f,
            overrideArcLength: 33.19717f);

        /// <summary>
        /// Rozjazd krzyżowy R190 1:9 - symetryczny
        /// Prosta 6.092377m + łuk R190 21.02487m + prosta 6.092377m = 33.209624m
        /// </summary>
        public static readonly TurnoutDefinition Crossover_R190 = CreateTurnout(
            "Krzyżowy R190", 190f, 9f,
            overrideLength: 33.209624f,
            overrideArcLength: 21.02487f,
            overrideStraightPrefix: 6.092377f,
            overrideStraightExtension: 6.092377f);

        /// <summary>
        /// Tworzy definicję rozjazdu z podanych parametrów
        /// </summary>
        public static TurnoutDefinition CreateTurnout(string name, float radius, float frogRatio,
            float overrideLength = -1f, float overrideStraightExtension = -1f, float overrideArcLength = -1f,
            float overrideStraightPrefix = -1f)
        {
            float frogAngle = Mathf.Atan(1f / frogRatio);

            // Długość łuku nogi odgałęziającej: R * frogAngle, lub nadpisana wartość
            float arcLength = overrideArcLength > 0f ? overrideArcLength : radius * frogAngle;

            // Prosta przed łukiem (jeśli podana)
            float straightPrefix = overrideStraightPrefix > 0f ? overrideStraightPrefix : 0f;

            // Prosta za łukiem (jeśli podana)
            float straightExtension = overrideStraightExtension > 0f ? overrideStraightExtension : 0f;

            // Długość całkowita: domyślnie prefix + arcLength + extension, ale można nadpisać
            float length = overrideLength > 0f ? overrideLength : straightPrefix + arcLength + straightExtension;

            // Długość stycznej: T = R * tan(alpha/2)
            float tangentLength = radius * Mathf.Tan(frogAngle / 2f);

            return new TurnoutDefinition
            {
                Name = name,
                Radius = radius,
                FrogRatio = frogRatio,
                FrogAngle = frogAngle,
                Length = length,
                ArcLength = arcLength,
                StraightPrefix = straightPrefix,
                StraightExtension = straightExtension,
                TangentLength = tangentLength
            };
        }

        /// <summary>
        /// Generuje geometrię rozjazdu (polyline obu ramion).
        /// Origin = punkt rozgałęzienia (początek rozjazdu).
        /// straightDir = kierunek toru prostego (znormalizowany).
        /// divergeLeft = true = odgałęzienie w lewo, false = w prawo.
        /// </summary>
        /// <returns>(straightLeg, divergingLeg) - polyline obu ramion</returns>
        public static (List<Vector3> straightLeg, List<Vector3> divergingLeg) GenerateTurnoutGeometry(
            Vector3 origin, Vector3 straightDir, TurnoutDefinition def, bool divergeLeft)
        {
            straightDir = straightDir.normalized;
            Vector3 perpendicular = Vector3.Cross(Vector3.up, straightDir).normalized;
            if (!divergeLeft) perpendicular = -perpendicular;

            // Prosta noga - po prostu prosta linia o długości rozjazdu
            List<Vector3> straightLeg = new List<Vector3>();
            int straightSamples = Mathf.Max(2, Mathf.CeilToInt(def.Length));
            for (int i = 0; i <= straightSamples; i++)
            {
                float t = (float)i / straightSamples;
                straightLeg.Add(origin + straightDir * (t * def.Length));
            }

            // === Odgałęziająca noga: [prosta prefix] + łuk R + [prosta extension] ===
            List<Vector3> divergingLeg = new List<Vector3>();

            // Początek odgałęzienia (przed łukiem może być prosta prefix)
            Vector3 arcOrigin = origin;

            // Część 0: Prosta przed łukiem (jeśli jest) — idzie wzdłuż prostej nogi
            if (def.StraightPrefix > 0.01f)
            {
                int prefixSamples = Mathf.Max(2, Mathf.CeilToInt(def.StraightPrefix));
                for (int i = 0; i <= prefixSamples; i++)
                {
                    float t = (float)i / prefixSamples;
                    divergingLeg.Add(origin + straightDir * (t * def.StraightPrefix));
                }
                arcOrigin = origin + straightDir * def.StraightPrefix;
            }

            // Część 1: Łuk o promieniu R i kącie frogAngle
            int arcSamples = Mathf.Max(5, Mathf.CeilToInt(def.ArcLength));

            for (int i = 0; i <= arcSamples; i++)
            {
                // Pomijaj i==0 jeśli mamy prefix (duplikat ostatniego punktu prefix)
                if (def.StraightPrefix > 0.01f && i == 0) continue;

                float t = (float)i / arcSamples;
                float angle = def.FrogAngle * t;

                Vector3 point = arcOrigin
                    + perpendicular * def.Radius * (1f - Mathf.Cos(angle))
                    + straightDir * def.Radius * Mathf.Sin(angle);

                divergingLeg.Add(point);
            }

            // Część 2: Prosta za łukiem (jeśli jest) — w kierunku stycznej na końcu łuku
            if (def.StraightExtension > 0.01f)
            {
                // Kierunek na końcu łuku = straightDir obrócony o frogAngle
                Vector3 arcEndDir = GetDivergingEndDirection(straightDir, def, divergeLeft);
                Vector3 arcEndPos = divergingLeg[divergingLeg.Count - 1];

                int extSamples = Mathf.Max(2, Mathf.CeilToInt(def.StraightExtension));
                for (int i = 1; i <= extSamples; i++)
                {
                    float t = (float)i / extSamples;
                    divergingLeg.Add(arcEndPos + arcEndDir * (t * def.StraightExtension));
                }
            }

            return (straightLeg, divergingLeg);
        }

        /// <summary>
        /// Oblicza punkt końcowy odgałęzienia rozjazdu (łuk + prosta extension)
        /// </summary>
        public static Vector3 GetDivergingEndpoint(Vector3 origin, Vector3 straightDir, TurnoutDefinition def, bool divergeLeft)
        {
            straightDir = straightDir.normalized;
            Vector3 perpendicular = Vector3.Cross(Vector3.up, straightDir).normalized;
            if (!divergeLeft) perpendicular = -perpendicular;

            // Przesunięcie o prefix (prosta przed łukiem)
            Vector3 arcOrigin = origin;
            if (def.StraightPrefix > 0.01f)
                arcOrigin = origin + straightDir * def.StraightPrefix;

            // Koniec łuku
            Vector3 arcEnd = arcOrigin
                + perpendicular * def.Radius * (1f - Mathf.Cos(def.FrogAngle))
                + straightDir * def.Radius * Mathf.Sin(def.FrogAngle);

            // Prosta extension za łukiem
            if (def.StraightExtension > 0.01f)
            {
                Vector3 extDir = GetDivergingEndDirection(straightDir, def, divergeLeft);
                arcEnd += extDir * def.StraightExtension;
            }

            return arcEnd;
        }

        /// <summary>
        /// Oblicza kierunek na końcu odgałęzienia (taki sam na końcu łuku i prostej za nim)
        /// </summary>
        public static Vector3 GetDivergingEndDirection(Vector3 straightDir, TurnoutDefinition def, bool divergeLeft)
        {
            straightDir = straightDir.normalized;
            float angle = def.FrogAngle;
            if (!divergeLeft) angle = -angle;

            // Obrót straightDir o kąt frogAngle wokół osi Y
            return Quaternion.Euler(0, angle * Mathf.Rad2Deg, 0) * straightDir;
        }

        /// <summary>
        /// Oblicza lateralny offset (prostopadły do prostej) końca odgałęzienia.
        /// Wynik zawsze >= 0.
        /// </summary>
        public static float ComputeLateralOffset(Vector3 straightDir, TurnoutDefinition def)
        {
            straightDir = straightDir.normalized;
            Vector3 endpoint = GetDivergingEndpoint(Vector3.zero, straightDir, def, true);
            Vector3 perp = Vector3.Cross(Vector3.up, straightDir).normalized;
            return Mathf.Abs(Vector3.Dot(endpoint, perp));
        }

        /// <summary>
        /// Oblicza długość wstawki prostej dla odgałęzienia z powrotem do równoległego toru.
        /// trackSpacing = międzytorze (odległość prostopadła między torami).
        /// returnRadius = promień łuku powrotnego (gdy returnDef == null) lub null (gdy używamy rozjazdu).
        /// returnDef = definicja rozjazdu powrotnego (gdy != null, returnRadius jest ignorowane).
        /// Zwraca (insertLength, isValid). isValid = false gdy wstawka wychodziłaby ujemna.
        /// </summary>
        public static (float insertLength, bool isValid) ComputeBranchReturnInsert(
            TurnoutDefinition mainDef, float trackSpacing,
            float returnRadius, TurnoutDefinition? returnDef)
        {
            Vector3 refDir = Vector3.forward;
            float hMain = ComputeLateralOffset(refDir, mainDef);

            float hReturn;
            if (returnDef.HasValue)
                hReturn = ComputeLateralOffset(refDir, returnDef.Value);
            else
                hReturn = returnRadius * (1f - Mathf.Cos(mainDef.FrogAngle));

            float sinAlpha = Mathf.Sin(mainDef.FrogAngle);
            if (sinAlpha < 0.0001f) return (0f, false);

            float insert = (trackSpacing - hMain - hReturn) / sinAlpha;
            return (insert, insert >= -0.01f);
        }

        /// <summary>
        /// Generuje polyline łuku powrotnego (od kąta alpha do 0, tj. powrót do kierunku prostego).
        /// startPos = punkt startu łuku (koniec wstawki prostej).
        /// incomingDir = kierunek wchodzący (pod kątem alpha od prostej).
        /// radius = promień łuku.
        /// alpha = kąt do skrętu (frogAngle).
        /// turnLeft = kierunek skrętu łuku (przeciwny do divergeLeft głównego rozjazdu).
        /// </summary>
        public static List<Vector3> GenerateReturnArc(
            Vector3 startPos, Vector3 incomingDir, float radius, float alpha, bool turnLeft)
        {
            incomingDir = incomingDir.normalized;
            Vector3 perpRight = Vector3.Cross(incomingDir, Vector3.up).normalized;
            Vector3 perpToCenter = turnLeft ? -perpRight : perpRight;

            Vector3 center = startPos + perpToCenter * radius;

            int samples = Mathf.Max(5, Mathf.CeilToInt(radius * alpha));
            var points = new List<Vector3>();

            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                float angle = alpha * t;
                float sign = turnLeft ? 1f : -1f;

                // Obrót wektora (startPos - center) o kąt angle wokół Y
                Vector3 fromCenter = startPos - center;
                Vector3 rotated = Quaternion.Euler(0, sign * angle * Mathf.Rad2Deg, 0) * fromCenter;
                points.Add(center + rotated);
            }

            return points;
        }
    }
}
