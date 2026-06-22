using System;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1 / D5: Pojedyncza sluzba w obiegu pracowniczym.
    ///
    /// Typy:
    /// - Service: prowadzi TrainRun (referencedTrainRunId wymagany)
    /// - Break: przerwa na stacji (moze byc miedzy Service'ami)
    /// - Deadhead: jedzie pasazerem (referencedTrainRunId wymagany — ten drugi TR)
    /// - Handover: przekazanie zmiany innemu maszyniscie (5 min min)
    /// - Overnight: nocleg w hotelu (D20, <see cref="overnightHotel"/> wymagany)
    ///
    /// Multi-day: <see cref="dayOffset"/> 0 = dzien startu turnusu, 1 = nastepny dzien itd.
    /// Czasy <see cref="startTimeIso"/>/<see cref="endTimeIso"/> w ramach tego dnia [HH:MM:SS].
    ///
    /// Validator (M8-8): spojnosc stacji, ordering czasowy, limit 12h/dobe, min break after 4h pracy.
    /// </summary>
    [Serializable]
    public class CrewDuty
    {
        public CrewDutyKind kind;

        /// <summary>Czas startu sluzby (HH:MM:SS w ramach dnia).</summary>
        public string startTimeIso;

        /// <summary>Czas konca sluzby (HH:MM:SS).</summary>
        public string endTimeIso;

        /// <summary>
        /// Offset dnia wzgledem startu turnusu (multi-day, D20).
        /// 0 = dzien 1 (start turnusu), 1 = dzien 2, 2 = dzien 3 (EA max).
        /// </summary>
        public int dayOffset;

        /// <summary>Nazwa stacji poczatkowej sluzby (pusta dla Break/Handover w tym samym miejscu).</summary>
        public string startStationName;

        /// <summary>Nazwa stacji koncowej.</summary>
        public string endStationName;

        /// <summary>ID TrainRun dla Service/Deadhead. -1 = brak (Break/Handover/Overnight).</summary>
        public int referencedTrainRunId = -1;

        /// <summary>ID obiegu taboru (informational) — z ktorego obiegu pochodzi referencedTrainRun. -1 = brak.</summary>
        public int referencedCirculationId = -1;

        /// <summary>Indeks sluzby w turnusie (dla stable reference w validator errors).</summary>
        public int dutyIndex;

        /// <summary>Nocleg dla kind=Overnight. null dla innych typow.</summary>
        public HotelBooking overnightHotel;

        // ── Helpery ───────────────────────────────────

        /// <summary>Czas trwania w minutach (end - start). Uwzglednia rollover doby dla Night.</summary>
        public int GetDurationMinutes()
        {
            if (string.IsNullOrEmpty(startTimeIso) || string.IsNullOrEmpty(endTimeIso)) return 0;

            var tStart = IsoTime.ParseTime(startTimeIso);
            var tEnd = IsoTime.ParseTime(endTimeIso);
            var span = tEnd - tStart;

            // Rollover przez polnoc: end < start => +24h
            if (span.TotalMinutes < 0) span = span.Add(TimeSpan.FromHours(24));

            return (int)span.TotalMinutes;
        }

        // ── TD-025: pre-duty trigger helpers ──────────────

        /// <summary>
        /// TD-025: zwraca absolutny gameTimeSec startu tej duty względem dnia startu turnusu.
        /// <c>turnusStartDateIso</c> + <see cref="dayOffset"/> dni + <see cref="startTimeIso"/>.
        ///
        /// Returns -1 gdy któryś z timestampów jest pusty/błędny.
        /// </summary>
        public long GetAbsoluteStartGameTime(string turnusStartDateIso)
        {
            if (string.IsNullOrEmpty(turnusStartDateIso) || string.IsNullOrEmpty(startTimeIso))
                return -1L;
            try
            {
                var startDate = IsoTime.ParseDate(turnusStartDateIso).AddDays(dayOffset);
                var startTime = IsoTime.ParseTime(startTimeIso);
                // Absolute gameTime = (daysSinceEpoch × 86400) + secondsInDay.
                // GameState.GameStartDateIso defines day 0; convert relative.
                var epoch = IsoTime.ParseDate(RailwayManager.Core.GameState.GameStartDateIso);
                int daysSinceEpoch = (int)(startDate - epoch).TotalDays;
                long secondsInDay = (long)startTime.TotalSeconds;
                return (long)daysSinceEpoch * 86400L + secondsInDay;
            }
            catch
            {
                return -1L;
            }
        }

        /// <summary>
        /// TD-025: czy duty jest "blisko startu" — od teraz minęłoby
        /// &lt;= <paramref name="leadMinutes"/>. Używane do triggera
        /// <see cref="EmployeeWorkflowState.ComingToDepot"/> dla Driver/Conductor.
        ///
        /// Zwraca false gdy start już minął (duty rozpoczęte lub zakończone).
        /// </summary>
        public bool IsImminent(string turnusStartDateIso, long currentGameTime, int leadMinutes)
        {
            long absoluteStart = GetAbsoluteStartGameTime(turnusStartDateIso);
            if (absoluteStart < 0) return false;
            long leadSec = leadMinutes * 60L;
            long delta = absoluteStart - currentGameTime;
            return delta > 0 && delta <= leadSec;
        }
    }
}
