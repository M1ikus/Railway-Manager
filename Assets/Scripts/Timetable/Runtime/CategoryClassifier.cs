using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Auto-klasyfikator kategorii rozkładowej IRJ na podstawie charakterystyki trasy + kursowania.
    ///
    /// Algorytm:
    /// 1. Typ trakcji: z CompositionMode + BrakeRegime/tabor → TractionLetter (E/J/S/M)
    /// 2. Grupa IRJ (pierwsze 2 litery):
    ///    - PW/PX → ustawione jawnie przez gracza (empty vehicle move)
    ///    - LP/LT/LS → jawnie przez gracza (lone loco)
    ///    - Nocny? (start w 22-4) → nocne warianty (MH/EN) jeśli kwalifikują
    ///    - Międzywojewódzki? (crosses voivodeship border) → M*
    ///      - % postojów: ≥80 → MO, 40-79 → MP, <40 + Vmax≥160 → EI, <40 → MP
    ///    - Aglomeracyjny? (trasa w 1 aglomeracji) → RA
    ///    - Wojewódzki (w 1 województwie):
    ///      - % postojów: ≥80 → RO, 40-79 → RP, <40 + Vmax≥160 → EI, <40 → RP
    ///
    /// Gracz może override przez ustawienie IrjCategory ręcznie w Timetable.
    /// </summary>
    public static class CategoryClassifier
    {
        /// <summary>Wejście dla klasyfikatora — wszystko co jest potrzebne do decyzji.</summary>
        public struct ClassificationInput
        {
            public List<Vector2> routePolyline;     // punkty trasy (pozycje w worldspace)
            public int stopsOnRoute;                // liczba postojów
            public int totalStationsOnRoute;        // liczba wszystkich stacji+przystanków
            public int startMinutesFromMidnight;    // start pierwszej stacji
            public int maxSpeedKmh;                 // docelowa Vmax rozkładu (min trasy i taboru)
            public CompositionMode compositionMode; // EMU/DMU vs Lok+wagony
            public bool isElectric;                 // czy trakcja elektryczna
            public VoivodeshipResolver voivodeshipResolver; // null → zakłada wojewódzki
            public HashSet<string> agglomerations;  // set aglomeracyjnych miast (null → RA nie aktywne)
            public List<string> stationCityNames;   // nazwy miast stacji trasy (do testu aglomeracji)
        }

        /// <summary>Wylicza pełną kategorię IRJ (grupa + trakcja).</summary>
        public static IrjCategory Classify(ClassificationInput input)
        {
            var traction = GetTractionLetter(input.compositionMode, input.isElectric);
            var group = GetGroup(input);
            return new IrjCategory(group, traction);
        }

        private static TractionLetter GetTractionLetter(CompositionMode mode, bool electric)
        {
            if (mode == CompositionMode.MultipleUnit)
                return electric ? TractionLetter.ElectricUnit : TractionLetter.DieselUnit;
            return electric ? TractionLetter.ElectricLoco : TractionLetter.DieselLoco;
        }

        private static IrjGroup GetGroup(ClassificationInput input)
        {
            bool isNight = IsNightStart(input.startMinutesFromMidnight);
            float stopRatio = input.totalStationsOnRoute > 0
                ? (float)input.stopsOnRoute / input.totalStationsOnRoute
                : 0f;
            bool crossesVoi = input.voivodeshipResolver != null
                && input.voivodeshipResolver.CrossesVoivodeshipBorder(input.routePolyline);

            if (crossesVoi)
            {
                // Międzywojewódzki: MP/MH/MO/EI
                if (stopRatio >= TimetableTuningConstants.StopRatioThresholdLocal)
                    return IrjGroup.InterregionalLocal;      // MO — osobowy

                bool qualifiesForExpress = stopRatio < TimetableTuningConstants.StopRatioThresholdFast
                    && input.maxSpeedKmh >= TimetableTuningConstants.ExpressMinSpeedKmh;
                if (qualifiesForExpress)
                    return IrjGroup.ExpressDomestic;         // EI — ekspresowy krajowy

                return isNight
                    ? IrjGroup.InterregionalFastNight        // MH — pospieszny nocny
                    : IrjGroup.InterregionalFast;            // MP — pospieszny
            }

            // Wojewódzki: RA/RP/RO/EI
            if (IsSingleAgglomeration(input))
                return IrjGroup.RegionalAgglomeration;       // RA

            if (stopRatio >= TimetableTuningConstants.StopRatioThresholdLocal)
                return IrjGroup.RegionalLocal;               // RO

            bool expressQualifies = stopRatio < TimetableTuningConstants.StopRatioThresholdFast
                && input.maxSpeedKmh >= TimetableTuningConstants.ExpressMinSpeedKmh;
            if (expressQualifies)
                return IrjGroup.ExpressDomestic;             // EI

            return IrjGroup.RegionalFast;                    // RP
        }

        /// <summary>Start godzinowy w oknie 22:00-03:59?</summary>
        private static bool IsNightStart(int minutesFromMidnight)
        {
            int hour = minutesFromMidnight / 60;
            return TimetableTuningConstants.IsNightHour(hour);
        }

        /// <summary>
        /// Czy wszystkie stacje na trasie należą do jednej aglomeracji?
        /// Używane do kategorii RA — wojewódzki osobowy aglomeracyjny.
        /// </summary>
        private static bool IsSingleAgglomeration(ClassificationInput input)
        {
            if (input.agglomerations == null || input.stationCityNames == null
                || input.stationCityNames.Count == 0) return false;

            string first = null;
            foreach (var city in input.stationCityNames)
            {
                if (string.IsNullOrEmpty(city)) return false;
                if (!input.agglomerations.Contains(city)) return false;
                if (first == null) first = city;
                else if (first != city) return false;
            }
            return true;
        }
    }
}
