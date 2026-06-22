using System;
using RailwayManager.Core;
using UnityEngine;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M6-5: Modyfikatory popytu — mnożniki nakładane na baseDemand z OD matrix.
    ///
    /// Combined formula (w PassengerManager.MaybeSpawnAgents):
    /// effective = base × timeOfDay × dayOfWeek × season × holiday × commute × reputation × offerFreq
    ///
    /// Wartości placeholder do M6.5 Rebalance (post-M13 Save/Load).
    /// </summary>
    public static class DemandModifiers
    {
        // ── Godzina dnia (rush hours) ────────────────────────────────

        /// <summary>
        /// Modifier dla godziny (0-23). Rush hours peakują o 7-9 i 16-18.
        /// </summary>
        public static float GetHourOfDayModifier(int hour)
        {
            // Rush hour poranny (7-9): ×1.8
            if (hour >= 7 && hour <= 9) return 1.8f;
            // Rush popołudniowy (16-18): ×1.6
            if (hour >= 16 && hour <= 18) return 1.6f;
            // Dzień (10-15, 19-20): ×1.0
            if (hour >= 10 && hour <= 20) return 1.0f;
            // Wieczór/noc (21-23, 5-6): ×0.4
            if (hour >= 21 || hour <= 6) return 0.4f;
            return 1.0f;
        }

        // ── Dzień tygodnia (weekend vs weekday) ──────────────────────

        public static float GetDayOfWeekModifier(DayOfWeek dow)
        {
            switch (dow)
            {
                case DayOfWeek.Monday:
                case DayOfWeek.Tuesday:
                case DayOfWeek.Wednesday:
                case DayOfWeek.Thursday:
                    return 1.3f;   // pn-cz: maksimum commute
                case DayOfWeek.Friday:
                    return 1.2f;   // pt: lekko mniej (część już na weekend)
                case DayOfWeek.Saturday:
                    return 0.7f;   // sobota
                case DayOfWeek.Sunday:
                    return 0.6f;   // niedziela: minimum
                default:
                    return 1.0f;
            }
        }

        // ── Sezonowość ───────────────────────────────────────────────

        public static float GetSeasonModifier(DateTime date)
        {
            int m = date.Month;
            // Wakacje (VII, VIII) — większy popyt turystyczny, mniejszy commute
            if (m == 7 || m == 8) return 0.9f;
            // Zima (XII-II) — lekko mniej
            if (m == 12 || m == 1 || m == 2) return 0.85f;
            // Wiosna + jesień (III-VI, IX-XI) — pełen popyt
            return 1.0f;
        }

        // ── Święta ───────────────────────────────────────────────────

        /// <summary>
        /// Polskie święta — dni szczególnego popytu (święto = ludzie jadą do rodzin, dużo pax)
        /// lub zerowego (Boże Narodzenie — wszyscy już są gdzie trzeba).
        /// </summary>
        public static float GetHolidayModifier(DateTime date)
        {
            int m = date.Month, d = date.Day;

            // Boże Narodzenie (24-26 XII) — bardzo dużo podróży przed, cisza w same święta
            if (m == 12 && d == 23) return 1.8f;          // wigilia-1, maks ruch
            if (m == 12 && (d == 24 || d == 25)) return 0.3f; // święta = zamknięte
            if (m == 12 && d == 26) return 0.5f;           // drugi dzień

            // Nowy Rok
            if (m == 12 && d == 31) return 1.5f;
            if (m == 1 && d == 1) return 0.4f;

            // Wszystkich Świętych (1 XI) — MEGA ruch (wizyty na cmentarzach)
            if (m == 11 && d == 1) return 2.0f;
            if (m == 10 && d == 31) return 1.5f; // dzień przed

            // Święto Pracy + Konstytucji (1-3 V) — weekend wyjazdów
            if (m == 5 && d >= 1 && d <= 3) return 1.3f;

            // Boże Ciało (ruchome — przybliżenie czerwiec)
            // Wielkanoc (ruchome) — skip dla MVP, M12 random events

            return 1.0f;
        }

        // ── Commute pattern (D9 B5) ──────────────────────────────────

        /// <summary>
        /// Modifier uwzględniający kierunek commute:
        /// - Rano (6-9): małe→duże (praca/szkoła) ×1.5, odwrotnie ×0.6
        /// - Popołudnie (15-19): duże→małe (powroty) ×1.4, odwrotnie ×0.7
        /// - Poza peakami: neutralne 1.0
        /// </summary>
        public static float GetCommuteModifier(float fromImportance, float toImportance, int hour, DayOfWeek dow)
        {
            // Tylko dni robocze — weekend commute zawsze 1.0
            if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday) return 1.0f;

            // Różnica ważności określa "rozmiar stacji"
            float diff = toImportance - fromImportance;

            // Poranny peak (do pracy/szkoły)
            if (hour >= 6 && hour <= 9)
            {
                if (diff > 2f) return 1.5f;       // małe → duże = peak commute
                if (diff < -2f) return 0.6f;      // duże → małe = anti-commute
                return 1.0f;
            }
            // Popołudniowy peak (powroty)
            if (hour >= 15 && hour <= 19)
            {
                if (diff < -2f) return 1.4f;      // duże → małe = powroty
                if (diff > 2f) return 0.7f;       // małe → duże = anti-commute
                return 1.0f;
            }
            return 1.0f;
        }

        // ── Offer frequency (B2) ─────────────────────────────────────

        /// <summary>
        /// Więcej kursów na danej OD pair = większy popyt (ludzie widzą że mogą łatwo jechać).
        /// </summary>
        public static float GetOfferFrequencyModifier(int runsPerDayOnPair)
        {
            if (runsPerDayOnPair <= 0) return 0f;      // brak oferty = brak pax (demand-driven)
            if (runsPerDayOnPair == 1) return 0.6f;    // 1 kurs dziennie = mało wyboru
            if (runsPerDayOnPair <= 3) return 0.9f;    // 2-3 = OK
            if (runsPerDayOnPair <= 8) return 1.1f;    // 4-8 = komfort
            return 1.3f;                                // 9+ = wysoka częstotliwość
        }

        // ── Combined ─────────────────────────────────────────────────

        /// <summary>
        /// Wszystkie time-based modifiers skonsolidowane (niezależne od OD pair).
        /// Do użycia tam gdzie mnożnik tygodniowy jest wspólny dla wszystkich par.
        /// </summary>
        public static float GetTimeCombined(DateTime gameDateTime)
        {
            return GetHourOfDayModifier(gameDateTime.Hour)
                 * GetDayOfWeekModifier(gameDateTime.DayOfWeek)
                 * GetSeasonModifier(gameDateTime)
                 * GetHolidayModifier(gameDateTime);
        }

        /// <summary>Zwraca bieżący game DateTime (start + GameDay + GameTimeSeconds dziś).</summary>
        public static DateTime GetCurrentGameDateTime()
        {
            var start = IsoTime.ParseDate(RailwayManager.Core.GameState.GameStartDateIso);
            var date = start.AddDays(RailwayManager.Core.GameState.GameDay);
            return date.AddSeconds(RailwayManager.Core.GameState.GameTimeSeconds);
        }
    }
}
