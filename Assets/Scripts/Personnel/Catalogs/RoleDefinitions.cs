using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1: Helpery zwracajace per-rola definicje (pensja bazowa, fatigue rate,
    /// wyswietlana nazwa PL, czy moze miec turnus/obieg pracowniczy).
    ///
    /// Konwencja: jeden switch per concern — dodanie nowej roli = dodanie wpisu w kazdym.
    /// Alternatywa to SO/JSON data-driven (post-EA).
    /// </summary>
    public static class RoleDefinitions
    {
        // ── Pensje bazowe (1-star, groszy miesiecznie) ──

        public static int GetBaseSalaryGroszy(EmployeeRole role) => role switch
        {
            EmployeeRole.Driver            => PersonnelBalanceConstants.BaseSalaryDriver,
            EmployeeRole.Conductor         => PersonnelBalanceConstants.BaseSalaryConductor,
            EmployeeRole.Mechanic          => PersonnelBalanceConstants.BaseSalaryMechanic,
            EmployeeRole.Cleaner           => PersonnelBalanceConstants.BaseSalaryCleaner,
            EmployeeRole.WashBay           => PersonnelBalanceConstants.BaseSalaryWashBay,
            EmployeeRole.Office            => PersonnelBalanceConstants.BaseSalaryOffice,
            EmployeeRole.Research          => PersonnelBalanceConstants.BaseSalaryResearch,
            EmployeeRole.TicketClerk       => PersonnelBalanceConstants.BaseSalaryTicket,
            EmployeeRole.Dispatcher        => PersonnelBalanceConstants.BaseSalaryDispatcher,
            EmployeeRole.TrafficController => PersonnelBalanceConstants.BaseSalaryTrafficController,
            _ => PersonnelBalanceConstants.BaseSalaryCleaner // fallback
        };

        /// <summary>
        /// Oblicza pensje dla konkretnego skilla wg wzoru (D2):
        /// <c>salary = base × (0.7 + 0.15 × skill)</c>
        /// </summary>
        public static int GetExpectedSalaryGroszy(EmployeeRole role, int skill)
        {
            int baseSalary = GetBaseSalaryGroszy(role);
            float mult = PersonnelBalanceConstants.SkillSalaryMultBase
                       + PersonnelBalanceConstants.SkillSalaryMultPerStar * skill;
            return (int)(baseSalary * mult);
        }

        // ── Fatigue rate per godzina na zmianie ──

        public static float GetFatigueRatePerHour(EmployeeRole role) => role switch
        {
            EmployeeRole.Driver            => PersonnelBalanceConstants.FatigueRateDriverPerHour,
            EmployeeRole.Conductor         => PersonnelBalanceConstants.FatigueRateConductorPerHour,
            EmployeeRole.Mechanic          => PersonnelBalanceConstants.FatigueRateMechanicPerHour,
            EmployeeRole.Cleaner           => PersonnelBalanceConstants.FatigueRateCleanerPerHour,
            EmployeeRole.WashBay           => PersonnelBalanceConstants.FatigueRateWashPerHour,
            EmployeeRole.Office            => PersonnelBalanceConstants.FatigueRateOfficePerHour,
            EmployeeRole.Research          => PersonnelBalanceConstants.FatigueRateResearchPerHour,
            EmployeeRole.TicketClerk       => PersonnelBalanceConstants.FatigueRateTicketPerHour,
            EmployeeRole.Dispatcher        => PersonnelBalanceConstants.FatigueRateDispatcherPerHour,
            EmployeeRole.TrafficController => PersonnelBalanceConstants.FatigueRateTrafficControllerPerHour,
            _ => 0.8f
        };

        // ── Czy rola moze miec obieg pracowniczy (turnus) ──

        /// <summary>D5: Driver i Conductor maja turnusy (CrewCirculation). Reszta dziala przez shift/przypisanie stacjonarne.</summary>
        public static bool CanHaveCrewCirculation(EmployeeRole role) =>
            role == EmployeeRole.Driver || role == EmployeeRole.Conductor;

        // ── Wyswietlana nazwa PL (UI) ──

        /// <summary>
        /// M13-4k-2: Wyświetlana nazwa roli w aktualnym języku (PL/EN/DE/CZ/JP/RU/UK).
        /// Klucze w `personnel.role.*` w resource files. Nazwa metody zachowana
        /// (GetDisplayNamePl) dla backward compat z istniejącymi 17 call site'ami —
        /// wewnętrznie deleguje do LocalizationService.
        /// </summary>
        public static string GetDisplayNamePl(EmployeeRole role) => LocalizationService.Get(role switch
        {
            EmployeeRole.Driver            => "personnel.role.driver",
            EmployeeRole.Conductor         => "personnel.role.conductor",
            EmployeeRole.Mechanic          => "personnel.role.mechanic",
            EmployeeRole.Cleaner           => "personnel.role.cleaner",
            EmployeeRole.WashBay           => "personnel.role.wash_bay",
            EmployeeRole.Office            => "personnel.role.office",
            EmployeeRole.Research          => "personnel.role.research",
            EmployeeRole.TicketClerk       => "personnel.role.ticket_clerk",
            EmployeeRole.Dispatcher        => "personnel.role.dispatcher",
            EmployeeRole.TrafficController => "personnel.role.traffic_controller",
            _ => role.ToString()
        });

        // ── Kolor placeholder capsule (M8-10) ──

        public static uint GetCapsuleColorRgb(EmployeeRole role) => role switch
        {
            EmployeeRole.Driver            => PersonnelBalanceConstants.ColorDriverRgb,
            EmployeeRole.Conductor         => PersonnelBalanceConstants.ColorConductorRgb,
            EmployeeRole.Mechanic          => PersonnelBalanceConstants.ColorMechanicRgb,
            EmployeeRole.Cleaner           => PersonnelBalanceConstants.ColorCleanerRgb,
            EmployeeRole.WashBay           => PersonnelBalanceConstants.ColorWashBayRgb,
            EmployeeRole.Office            => PersonnelBalanceConstants.ColorOfficeRgb,
            EmployeeRole.Research          => PersonnelBalanceConstants.ColorResearchRgb,
            EmployeeRole.TicketClerk       => PersonnelBalanceConstants.ColorTicketRgb,
            EmployeeRole.Dispatcher        => PersonnelBalanceConstants.ColorDispatcherRgb,
            EmployeeRole.TrafficController => PersonnelBalanceConstants.ColorTrafficCtrlRgb,
            _ => 0x888888u
        };

        /// <summary>
        /// Czy rola spawnuje sie jako agent 3D w Depot (D19, D25).
        /// TicketClerk: false (tylko popup stacji).
        /// Reszta: true.
        /// </summary>
        public static bool SpawnsAsAgentInDepot(EmployeeRole role) =>
            role != EmployeeRole.TicketClerk;

        // ── Capacity helpers (Dispatcher/TrafficController) ──

        /// <summary>D27: Capacity dyspozytora = 50 + 5×(skill-1).</summary>
        public static int GetDispatcherCapacity(int skill) =>
            PersonnelBalanceConstants.DispatcherBaseCapacity
            + PersonnelBalanceConstants.DispatcherCapacityPerStar * (skill - 1);

        /// <summary>D28: Capacity dyzurnego ruchu = 10 + 5×(skill-1).</summary>
        public static int GetTrafficControllerCapacity(int skill) =>
            PersonnelBalanceConstants.TrafficControllerBaseCapacity
            + PersonnelBalanceConstants.TrafficControllerCapacityPerStar * (skill - 1);
    }
}
