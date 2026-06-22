using System;
using System.Collections.Generic;
using RailwayManager.Timetable;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1 / D5: Obieg pracowniczy (turnus) — sekwencja sluzb pracownika per dzien.
    /// NIEZALEZNY od obiegu taboru (<see cref="Circulation"/> w M5 Timetable).
    ///
    /// Przyklad: maszynista Jan wykonuje turnus M-KR-01 skladajacy sie z:
    /// - Service: TR_1234 Krakow→Warszawa 5:30-9:30 (z obiegu taboru C-1)
    /// - Break: przerwa 9:30-11:00 w Warszawie
    /// - Service: TR_5678 Warszawa→Olsztyn 11:00-15:00 (z obiegu taboru C-7, inny pojazd)
    /// - Overnight: nocleg w Olsztynie (tier Standard)
    /// - Deadhead: powrot sluzbowy TR_9999 Olsztyn→Krakow nastepnego dnia (dayOffset=1)
    ///
    /// Multi-day (D20): max 3 dni w EA, durationDays ∈ [1, 3].
    /// Musi konczyc sie w home depot (validator).
    ///
    /// Reuse <see cref="DayMask"/> i <see cref="CirculationStatus"/> z Timetable.
    /// </summary>
    [Serializable]
    public class CrewCirculation
    {
        public int crewCirculationId;

        /// <summary>Nazwa turnusu (np. "M-KR-01", generowana auto lub recznie).</summary>
        public string name;

        /// <summary>Rola dla tego turnusu. W EA tylko Driver/Conductor maja turnusy.</summary>
        public EmployeeRole role = EmployeeRole.Driver;

        /// <summary>ID przypisanego pracownika. -1 = turnus bez obsady (szkielet).</summary>
        public int assignedEmployeeId = -1;

        /// <summary>Dni tygodnia gdy turnus dziala (cykliczne). Reuse z Timetable.</summary>
        public DayMask calendarDays = DayMask.Daily();

        /// <summary>Konkretne daty (ISO) — override jednorazowy. Zamiast calendarDays gdy non-cyclical.</summary>
        public List<string> specificDates = new();

        /// <summary>Sekwencja sluzb. Kolejnosc wg <see cref="CrewDuty.dayOffset"/> + startTimeIso.</summary>
        public List<CrewDuty> duties = new();

        /// <summary>Status (Draft/Active/Paused/Archived). Active generuje runtime assignments.</summary>
        public CirculationStatus status = CirculationStatus.Draft;

        /// <summary>Czas trwania turnusu w dniach (1 = jednodniowy, 2-3 = multi-day z noclegami). D20: EA max 3.</summary>
        public int durationDays = 1;

        /// <summary>Notatki gracza.</summary>
        public string notes;

        // ── Helpery ───────────────────────────────────

        /// <summary>Sumaryczny czas sluzb (Service+Deadhead) w minutach — do walidacji 12h limit.</summary>
        public int GetTotalWorkMinutes()
        {
            int sum = 0;
            foreach (var d in duties)
            {
                if (d.kind != CrewDutyKind.Service && d.kind != CrewDutyKind.Deadhead) continue;
                sum += d.GetDurationMinutes();
            }
            return sum;
        }

        /// <summary>Czy turnus ma przypisanego pracownika (nie szkielet).</summary>
        public bool HasAssignedEmployee => assignedEmployeeId > 0;
    }
}
