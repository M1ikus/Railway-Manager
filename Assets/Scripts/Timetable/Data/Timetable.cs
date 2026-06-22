using System;
using System.Collections.Generic;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Rozkład jazdy — statyczny plan kursowania po konkretnej trasie.
    /// Generuje wystąpienia (TrainRun) zgodnie z FrequencySpec i DayMask.
    /// </summary>
    [Serializable]
    public class Timetable
    {
        public int id;
        public string name;                       // np. "R1 Warszawa→Kraków poranna"

        // ── Powiązania ───────────────────────────
        public int routeId;                        // → Route.id
        public string commercialCategoryId;        // → CommercialCategory.id (handlowa, gracza)

        /// <summary>Kategoria rozkładowa IRJ — auto-wyliczana przez CategoryClassifier, możliwy override.</summary>
        public IrjCategory irjCategory;
        public bool irjCategoryManualOverride;

        /// <summary>Numer pociągu wg IRJ (4/5/6 cyfr). Pusty = wygeneruj auto przy zapisie.</summary>
        public string trainNumber;

        // ── Skład ────────────────────────────────
        public PlannedComposition composition = new();

        // ── Postoje ──────────────────────────────
        public List<TimetableStop> stops = new();

        // ── Kursowanie ───────────────────────────
        public FrequencySpec frequency = FrequencySpec.SingleRun(360); // domyślnie 06:00
        public DayMask calendar = DayMask.Daily();

        /// <summary>Start ważności rozkładu (czas gry w sekundach). 0 = od razu.</summary>
        public long validFromGameTime;

        /// <summary>Koniec ważności (czas gry w sekundach). 0 = bezterminowo.</summary>
        public long validToGameTime;

        /// <summary>
        /// Data początku obowiązywania rozkładu w formacie ISO (YYYY-MM-DD).
        /// Wpisywana w UI kreatora. Po integracji z GameTimeService konwertowana
        /// na <see cref="validFromGameTime"/>. Pusta = "od razu".
        /// </summary>
        public string startDateIso;

        /// <summary>
        /// Cykliczność tygodniowa — liczba tygodni od <see cref="startDateIso"/> przez
        /// które rozkład obowiązuje. 0 = bezterminowo. Default 4 (≈ miesiąc).
        ///
        /// To jest trzeci poziom cykliczności rozkładu:
        /// 1. godzinowa  → <see cref="frequency"/> (FrequencySpec, Single/Takt)
        /// 2. dzienna    → <see cref="calendar"/>  (DayMask, dni tygodnia)
        /// 3. tygodniowa → ten field            (ile tygodni do przodu zamawiamy)
        ///
        /// Po integracji z GameTimeService → <see cref="validToGameTime"/> = validFrom + weeksValid*7d.
        /// </summary>
        public int weeksValid = 4;

        // ── Ekonomia / obsada ───────────────────
        /// <summary>Depot ID gdzie tabor wraca po kursach (-1 = nie przypisano).</summary>
        public int assignedDepotId = -1;

        /// <summary>Status rozkładu (aktywny / wstrzymany / archiwalny).</summary>
        public TimetableStatus status = TimetableStatus.Active;

        /// <summary>Nazwy stacji pośrednich (via) — do wyświetlenia na liście.</summary>
        public List<string> viaStationNames = new();

        /// <summary>Notatki gracza.</summary>
        public string notes;

        /// <summary>M9c-D F7: syntetyczny rozkład dostawczy (efemeryczny) — pomijany w save,
        /// sprzątany po dostawie.</summary>
        public bool isDeliveryTimetable;

        /// <summary>Helper: czas trwania całego kursu w sekundach (od startu do ostatniego postoju).</summary>
        public int TotalDurationSec => stops.Count > 0 ? stops[stops.Count - 1].plannedDepartureSec : 0;

        /// <summary>Helper: stacja startowa — pierwszy postój.</summary>
        public TimetableStop FirstStop => stops.Count > 0 ? stops[0] : null;

        /// <summary>Helper: stacja końcowa — ostatni postój.</summary>
        public TimetableStop LastStop => stops.Count > 0 ? stops[stops.Count - 1] : null;

        /// <summary>Czas jazdy netto (bez postojów) w sekundach.</summary>
        public int DrivingTimeSec
        {
            get
            {
                int total = TotalDurationSec;
                int stopTime = 0;
                foreach (var s in stops)
                    stopTime += s.plannedDepartureSec - s.plannedArrivalSec;
                return total - stopTime;
            }
        }

        /// <summary>Łączny czas postojów w sekundach.</summary>
        public int TotalStopTimeSec
        {
            get
            {
                int t = 0;
                foreach (var s in stops)
                    t += s.plannedDepartureSec - s.plannedArrivalSec;
                return t;
            }
        }

        /// <summary>Godz. startu (minuty od północy).</summary>
        public int StartMinutes => frequency.firstRunMinutesFromMidnight;

        /// <summary>Godz. zakończenia (minuty od północy).</summary>
        public int EndMinutes => StartMinutes + (TotalDurationSec / 60);

        /// <summary>Trasa z via (do wyświetlenia).</summary>
        public string RouteDisplayName
        {
            get
            {
                if (viaStationNames.Count == 0) return name;
                return $"{name} via {string.Join(", ", viaStationNames)}";
            }
        }
    }
}
