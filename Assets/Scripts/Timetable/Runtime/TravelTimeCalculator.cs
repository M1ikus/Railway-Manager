using System.Collections.Generic;
using RailwayManager.Fleet;
using UnityEngine;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Wylicza czas jazdy pociągu po trasie na podstawie uproszczonej fizyki.
    /// Model: stałe przyspieszenie rozruchu + stałe opóźnienie hamowania, Vmax limitowana
    /// przez krawędzie grafu (z OSM maxspeed lub fallback) oraz przez PlannedComposition.
    /// Dla każdej pary kolejnych krawędzi trasy sprawdzana jest zmiana Vmax — jeśli się
    /// zmniejsza, pociąg hamuje z wyprzedzeniem.
    ///
    /// Uproszczenia zgodne z decyzjami projektowymi:
    /// - Brak wpływu spadków, łuków, przechyłek
    /// - Współczynnik hamowania (BrakingPercent) tylko wizualny, bez wpływu na drogę
    /// - Davis (A + B·v + C·v²) zredukowany do stałej acceleration/deceleration per typ taboru
    /// </summary>
    public static class TravelTimeCalculator
    {
        /// <summary>Segment trasy z perspektywy fizyki: długość + Vmax lokalne.</summary>
        public struct RouteSegment
        {
            public float lengthM;
            public int maxSpeedKmh;
        }

        /// <summary>
        /// Wylicza całkowity czas jazdy (bez postojów) po sekwencji segmentów.
        /// Zakłada że pociąg startuje z 0 km/h, kończy w 0 km/h. Między segmentami Vmax
        /// może się zmieniać — wtedy hamowanie/przyspieszanie w ramach poprzedniego segmentu.
        /// </summary>
        public static float CalculateDrivingTimeSec(
            List<RouteSegment> segments,
            PlannedComposition composition)
        {
            if (segments == null || segments.Count == 0) return 0f;

            float acceleration = GetAccelerationMs2(composition);
            float deceleration = GetDecelerationMs2(composition);
            int trainMaxKmh = composition != null ? composition.maxSpeedKmh : 160;

            float totalTime = 0f;
            float currentSpeedMs = 0f;

            for (int i = 0; i < segments.Count; i++)
            {
                int localMaxKmh = Mathf.Min(segments[i].maxSpeedKmh, trainMaxKmh);
                float localMaxMs = localMaxKmh / 3.6f;

                // Prędkość docelowa na końcu tego segmentu: 0 jeśli to ostatni segment,
                // inaczej min(nextMax, trainMax) — żeby przejść płynnie na następny.
                float exitSpeedMs = 0f;
                if (i < segments.Count - 1)
                {
                    int nextMaxKmh = Mathf.Min(segments[i + 1].maxSpeedKmh, trainMaxKmh);
                    exitSpeedMs = nextMaxKmh / 3.6f;
                }

                totalTime += SimulateSegment(
                    segments[i].lengthM, currentSpeedMs, exitSpeedMs,
                    localMaxMs, acceleration, deceleration);

                currentSpeedMs = exitSpeedMs;
            }

            return totalTime;
        }

        /// <summary>
        /// Symulacja fizyki jednego segmentu: od currentSpeed do exitSpeed, nie przekraczając
        /// localMax. Składa się z 3 faz: rozpęd, cruise, hamowanie. Jeśli segment jest krótki,
        /// pociąg może nie osiągnąć localMax.
        /// </summary>
        private static float SimulateSegment(
            float lengthM, float entrySpeedMs, float exitSpeedMs,
            float maxSpeedMs, float accel, float decel)
        {
            if (lengthM <= 0f) return 0f;

            // Droga potrzebna do rozpędzenia od entry do max
            float accelDist = (maxSpeedMs * maxSpeedMs - entrySpeedMs * entrySpeedMs) / (2f * accel);
            if (accelDist < 0f) accelDist = 0f;

            // Droga potrzebna do hamowania od max do exit
            float decelDist = (maxSpeedMs * maxSpeedMs - exitSpeedMs * exitSpeedMs) / (2f * decel);
            if (decelDist < 0f) decelDist = 0f;

            if (accelDist + decelDist <= lengthM)
            {
                // Klasyczny profil: rozpęd → cruise → hamowanie
                float accelTime = (maxSpeedMs - entrySpeedMs) / accel;
                float cruiseDist = lengthM - accelDist - decelDist;
                float cruiseTime = cruiseDist / maxSpeedMs;
                float decelTime = (maxSpeedMs - exitSpeedMs) / decel;
                return accelTime + cruiseTime + decelTime;
            }

            // Segment zbyt krótki na cruise — trójkątny profil rozpęd→hamowanie przy peak speed
            // Wzory z: a·s1 = (v²-u²)/2, d·s2 = (v²-w²)/2, s1+s2=L
            // Rozwiązanie: v² = (2·a·d·L + d·u² + a·w²) / (a+d)
            float peakSpeedSq =
                (2f * accel * decel * lengthM + decel * entrySpeedMs * entrySpeedMs
                 + accel * exitSpeedMs * exitSpeedMs) / (accel + decel);
            float peakSpeed = Mathf.Sqrt(Mathf.Max(0f, peakSpeedSq));
            if (peakSpeed < Mathf.Max(entrySpeedMs, exitSpeedMs))
                peakSpeed = Mathf.Max(entrySpeedMs, exitSpeedMs);

            float t1 = (peakSpeed - entrySpeedMs) / accel;
            float t2 = (peakSpeed - exitSpeedMs) / decel;
            return Mathf.Max(0f, t1) + Mathf.Max(0f, t2);
        }

        /// <summary>Przyspieszenie w m/s² zależne od trybu składu i nastawy hamulca.</summary>
        private static float GetAccelerationMs2(PlannedComposition composition)
        {
            float baseAccel = TimetableTuningConstants.DefaultAccelerationMs2;
            if (composition == null) return baseAccel;
            if (composition.mode == CompositionMode.MultipleUnit)
                baseAccel *= TimetableTuningConstants.MultipleUnitAccelBonus;
            return baseAccel;
        }

        /// <summary>Opóźnienie hamowania w m/s² zależne od nastawy hamulca (BrakeRegime).</summary>
        private static float GetDecelerationMs2(PlannedComposition composition)
        {
            float baseDecel = TimetableTuningConstants.DefaultDecelerationMs2;
            if (composition == null) return baseDecel;

            switch (composition.brakeRegime)
            {
                case BrakeRegime.G:    return baseDecel * TimetableTuningConstants.BrakeRegimeG_DecelMultiplier;
                case BrakeRegime.P:    return baseDecel * TimetableTuningConstants.BrakeRegimeP_DecelMultiplier;
                case BrakeRegime.R_Mg: return baseDecel * TimetableTuningConstants.BrakeRegimeRMg_DecelMultiplier;
                case BrakeRegime.R_E:  return baseDecel * TimetableTuningConstants.BrakeRegimeRE_DecelMultiplier;
                default:               return baseDecel; // R
            }
        }
    }
}
