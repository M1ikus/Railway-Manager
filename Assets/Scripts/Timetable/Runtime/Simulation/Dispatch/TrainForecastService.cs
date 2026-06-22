using System.Collections.Generic;
using UnityEngine;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// M-Dispatch Faza 1: liczy prognozę czasoprzestrzenną (<see cref="TrainForecast"/>) z bieżącego
    /// stanu <see cref="SimulatedTrain"/>. Czysta, deterministyczna funkcja — brak RNG, brak mutacji.
    ///
    /// Model v1 (ZGRUBNY, świadomie): per-blok cruise przy Vmax segmentu (cap maxSpeedKmh
    /// kompozycji) + trzymanie do planowej godziny odjazdu na postojach w zakresie bloku
    /// (modeluje, że osobowy okupuje blok peronowy do swojego odjazdu, a spóźniony ekspres NIE
    /// czeka). Pomija accel/decel i kompresję postoju — refine w kolejnych fazach, jeśli
    /// rozstrzyganie będzie wymagać większej dokładności.
    ///
    /// <para><b>Skala czasu:</b> caller podaje <paramref name="nowSec"/> w tej samej skali co
    /// <c>departureTimeOfDaySec + stop.plannedDepartureSec</c> (sekundy-od-północy, czyli
    /// <c>GameState.GameTimeSeconds</c>). Horyzont prognozy &lt;&lt; doba, więc cross-midnight to
    /// rzadki edge case (do obsłużenia w fazie wpięcia, jeśli zajdzie potrzeba).</para>
    /// </summary>
    public static class TrainForecastService
    {
        public static TrainForecast Compute(SimulatedTrain st, int priority, double nowSec, float horizonSec)
        {
            var reservations = new List<BlockReservation>();
            if (st == null || st.trainRun == null || st.routeBlockCount <= 0)
                return new TrainForecast(st?.trainRun?.id ?? -1, priority, reservations);

            int startIdx = Mathf.Clamp(st.currentBlockIndex, 0, st.routeBlockCount - 1);
            double pos = st.trainRun.currentPositionOnRouteM;
            double t = nowSec;

            int compMaxKmh = (st.timetable != null && st.timetable.composition != null)
                ? st.timetable.composition.maxSpeedKmh : 0;
            float compCapMps = compMaxKmh > 0 ? compMaxKmh / 3.6f : float.MaxValue;

            for (int i = startIdx; i < st.routeBlockCount; i++)
            {
                float blockStart = st.blockEntryDistM[i];
                float blockExit = st.blockExitDistM[i];
                if (blockExit <= blockStart) blockExit = blockStart + 1f; // guard degeneratu

                double from = System.Math.Max(pos, blockStart);
                double enterSec = (i == startIdx) ? nowSec : t;

                // Prędkość reprezentatywna: Vmax w środku bloku, cap kompozycji, clamp dolny.
                float mid = (blockStart + blockExit) * 0.5f;
                float v = st.GetVmaxAtDistance(mid);
                if (v > compCapMps) v = compCapMps;
                if (v < TimetableTuningConstants.DispatchMinForecastSpeedMps)
                    v = TimetableTuningConstants.DispatchMinForecastSpeedMps;

                double travelSec = (blockExit - from) / v;
                double holdSec = ScheduledHoldInBlock(st, blockStart, blockExit, (float)from, enterSec, v);
                double exitSec = enterSec + travelSec + holdSec;

                reservations.Add(new BlockReservation(st.routeBlockKeys[i], enterSec, exitSec));

                t = exitSec;
                pos = blockExit;
                if (t - nowSec > horizonSec) break;
            }

            return new TrainForecast(st.trainRun.id, priority, reservations);
        }

        /// <summary>
        /// Łączny czas trzymania na postojach w zakresie bloku [blockStart, blockExit):
        /// pociąg czeka do planowej godziny odjazdu (jeśli dojechał przed czasem). Spóźniony
        /// pociąg (dojazd po planie) nie czeka → hold 0. Modeluje okupację bloku peronowego.
        /// </summary>
        static double ScheduledHoldInBlock(SimulatedTrain st, float blockStart, float blockExit,
                                           float fromDist, double enterSec, float v)
        {
            var stops = st.timetable != null ? st.timetable.stops : null;
            if (stops == null || st.stopDistancesM == null) return 0.0;

            double hold = 0.0;
            int n = Mathf.Min(stops.Count, st.stopDistancesM.Length);
            for (int s = 0; s < n; s++)
            {
                float d = st.stopDistancesM[s];
                if (d < blockStart || d >= blockExit) continue;

                // Prognozowany czas dojazdu do tego stopu (free-run + dotychczasowy hold).
                double arrAtStopSec = enterSec + System.Math.Max(0.0, (d - fromDist)) / v + hold;
                double schedDepAbs = st.departureTimeOfDaySec + stops[s].plannedDepartureSec;
                if (schedDepAbs > arrAtStopSec)
                    hold += schedDepAbs - arrAtStopSec; // trzymaj do planowego odjazdu
            }
            return hold;
        }
    }
}
