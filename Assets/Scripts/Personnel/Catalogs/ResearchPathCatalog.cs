using System.Collections.Generic;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-15 / §3.7: Katalog dostepnych sciezek R&amp;D. W M8-15: 2 przykladowe (framework).
    /// Post-M12d: rozszerzenie do drzew badan per dzial (Maintenance, Traction, Comfort, Safety).
    /// </summary>
    public static class ResearchPathCatalog
    {
        public const string EffectMaintenanceTimeReduction = "MaintenanceTimeReduction";
        public const string EffectTractionEnergyReduction = "TractionEnergyReduction";

        /// <summary>Zwraca fresh copy listy sciezek (tak aby UI moglo trackowac progress niezaleznie).</summary>
        public static List<ResearchPath> CreateAll()
        {
            return new List<ResearchPath>
            {
                new()
                {
                    pathId = "better_inspections",
                    displayName = "Lepsze przeglądy",
                    description = "Usprawnione procedury diagnostyczne i lepsze narzędzia.\n" +
                                  "Skraca czas przeglądów P3-P5 o 10%.",
                    durationDays = 30,
                    daysRemaining = 30,
                    requiredResearchers = 3,
                    minSkill = 2,
                    status = ResearchPathStatus.Available,
                    effectKey = EffectMaintenanceTimeReduction,
                    effectValue = 0.10f
                },
                new()
                {
                    pathId = "traction_optimization",
                    displayName = "Optymalizacja trakcji",
                    description = "Optymalizacja procedur prowadzenia i rozkładu prędkości.\n" +
                                  "Obniża zużycie energii na trakcję o 5%.",
                    durationDays = 60,
                    daysRemaining = 60,
                    requiredResearchers = 2,
                    minSkill = 4,
                    status = ResearchPathStatus.Available,
                    effectKey = EffectTractionEnergyReduction,
                    effectValue = 0.05f
                }
            };
        }
    }
}
