using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-2: Parametry zatrudnienia kandydata przez <see cref="Runtime.PersonnelService.Hire"/>.
    ///
    /// Budowany przez RecruitmentUI (M8-3) z <see cref="EmployeeCandidate"/> + ew. negocjacje
    /// pensji (redukcja przez biurowego 3★+). Zamiennie — manualnie przez debug context menu.
    ///
    /// <see cref="negotiatedSalaryGroszy"/> = 0 oznacza "uzyj RoleDefinitions.GetExpectedSalaryGroszy".
    /// </summary>
    [Serializable]
    public class HireTerms
    {
        public string firstName;
        public string lastName;
        public int age;
        public string birthDateIso;

        public EmployeeRole role;
        /// <summary>Skill 1-5. PersonnelService clampuje do zakresu.</summary>
        public int skill = 1;

        /// <summary>Wynegocjowana pensja w groszach. 0 = oblicz z RoleDefinitions (default rynkowa).</summary>
        public int negotiatedSalaryGroszy;

        /// <summary>Jednorazowy bonus przy zatrudnieniu (0 = brak). Koszt opłacany natychmiast przez EconomyManager (M8-3 hook).</summary>
        public int hireBonusGroszy;

        public ShiftType initialShift = ShiftType.Morning;
        public WorkCyclePattern initialCycle = WorkCyclePattern.Cycle5_2;
    }
}
