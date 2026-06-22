using System;
using System.Collections.Generic;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-9: Parametry auto-generowania turnusow pracowniczych.
    /// </summary>
    [Serializable]
    public class CrewAutoGenSettings
    {
        /// <summary>Minimalna przerwa miedzy sluzbami (w minutach, dla chainowania).</summary>
        public int minGapMinutes = 30;

        /// <summary>Max pracy (Service+Deadhead) na turnus / dobe (domyslnie 12h).</summary>
        public int maxWorkHoursPerDay = 12;

        /// <summary>Ktore role generowac (Driver, Conductor albo obie).</summary>
        public AutoGenRoleMode roleMode = AutoGenRoleMode.DriverOnly;

        /// <summary>
        /// Auto-dodac Deadhead powrot gdy koniec dnia daleko od home.
        /// W M8-9: placeholder stub bez referencedTrainRunId (trzeba gracz dokonczyc recznie).
        /// </summary>
        public bool autoReturnDeadhead = true;

        /// <summary>Dozwol multi-day turnusy z Overnight (D20). POST M8-11 pelne wsparcie.</summary>
        public bool allowMultiDay = false;

        /// <summary>Tier hoteli dla multi-day (D20) — default z HotelBookingService.CompanyDefaultHotelTier.</summary>
        public HotelTier defaultHotelTier = HotelTier.Standard;

        /// <summary>Prefix dla nazw turnusow — "M-", "K-" etc.</summary>
        public string namePrefix = "T";

        public CrewAutoGenSettings Clone() => new()
        {
            minGapMinutes = minGapMinutes,
            maxWorkHoursPerDay = maxWorkHoursPerDay,
            roleMode = roleMode,
            autoReturnDeadhead = autoReturnDeadhead,
            allowMultiDay = allowMultiDay,
            defaultHotelTier = defaultHotelTier,
            namePrefix = namePrefix
        };
    }

    public enum AutoGenRoleMode
    {
        DriverOnly,
        ConductorOnly,
        Both
    }

    /// <summary>
    /// M8-9: Wejscie dla generatora — abstrakcja TrainRun niezalezna od Timetable.
    /// Adapter z Timetable.TrainRun w M8-11.
    /// </summary>
    [Serializable]
    public class CrewAutoGenTrainRunInput
    {
        /// <summary>ID TrainRun (referencedTrainRunId w CrewDuty).</summary>
        public int trainRunId;
        public int circulationId = -1;
        public string startStation;
        public string endStation;
        /// <summary>Czas startu HH:MM:SS.</summary>
        public string startTimeIso;
        public string endTimeIso;
        /// <summary>Data kursu (yyyy-MM-dd) — generator filtruje per dzien.</summary>
        public string dateIso;
        /// <summary>Kategoria IRJ (EI/EC/RO/RE/TL/SL...) — dla skill check w validatorze i Conductor required.</summary>
        public string irjCategory;
        /// <summary>Ilosc wagonow pasazerskich (dla D16 — &gt;3 = wymaga konduktora).</summary>
        public int passengerCarsCount;
        public int emuCount;
        public int dmuCount;
    }

    /// <summary>
    /// M8-9: Preview wyniku generatora — lista nowych turnusow PRZED commit.
    /// Gracz widzi preview, moze akceptowac/odrzucac.
    /// </summary>
    public class CrewAutoGenPreview
    {
        public List<CrewCirculation> GeneratedCirculations { get; } = new();
        public int TotalDuties { get; set; }
        public int UnusedTrainRunIds { get; set; }
        public int DeadheadsGenerated { get; set; }
        public List<string> Warnings { get; } = new();
        public CrewAutoGenSettings SettingsUsed { get; set; }
    }
}
