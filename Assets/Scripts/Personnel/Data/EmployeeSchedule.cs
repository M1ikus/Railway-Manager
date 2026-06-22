using System;
using System.Collections.Generic;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1 / D6: Indywidualny harmonogram pracownika. Warstwa override'ow
    /// nad zmianami bazowymi (cykl + defaultShift).
    ///
    /// Przyklad:
    /// - Pracownik ma Cycle5_2 + defaultShift=Morning, cycleStartDayOffset=0
    /// - Dzien 1 (pn) = praca rano, ..., dzien 5 (pt) = praca rano
    /// - Dzien 6 (sb), 7 (nd) = wolne
    /// - Override Vacation dla 2026-04-22..2026-04-26 → calosc tygodnia urlop
    /// - Override ShiftSwap dla 2026-05-03 z replacementShift=Night → tego dnia pracuje nocna
    ///
    /// Konflikty auto-wykryte w <see cref="Runtime.ScheduleValidator"/> (M8-4).
    /// Edytor UI w <see cref="UI.EmployeeScheduleEditorUI"/> (M8-4).
    /// </summary>
    [Serializable]
    public class EmployeeSchedule
    {
        public int employeeId;

        /// <summary>Domyslna zmiana wg cyklu — Morning/Afternoon/Night.</summary>
        public ShiftType defaultShift = ShiftType.Morning;

        /// <summary>Cykl dni pracujacych/wolnych (5+2 itp.).</summary>
        public WorkCyclePattern cycle = WorkCyclePattern.Cycle5_2;

        /// <summary>Od ktorego dnia cyklu zaczyna (offset modulo cycle length). Default 0 = od poczatku.</summary>
        public int cycleStartDayOffset;

        /// <summary>Lista override'ow. Moze byc pusta.</summary>
        public List<ScheduleOverride> overrides = new();
    }
}
