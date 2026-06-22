using System;
using System.Collections.Generic;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// BUG-010 (cz.1+2, post-EA): kwalifikacje pracownika — uprawnienia trakcyjne (jakimi
    /// pojazdami może jeździć) i kategorie rozkładu (jakie pociągi może obsługiwać).
    ///
    /// 🚧 EA mode: gameplay impact = ZERO. Pracownicy nie wymagają żadnych kwalifikacji
    /// (każdy maszynista jeździ EU07 i SM42, każdy konduktor obsługuje IC i lokalne).
    /// Listy poniżej są placeholder strukturalne — wypełniamy w UI dla pokazania, ale
    /// runtime sprawdza <see cref="HasAnyQualification"/> które zawsze zwraca true w EA.
    ///
    /// 🛣️ Post-EA roadmap (M8.5 lub osobny milestone):
    /// - Macierz "rola × pojazd × wymagana kwalifikacja" (np. EU160 wymaga ETCS)
    /// - Macierz "rola × kategoria IRJ × wymagana kwalifikacja"
    /// - Edytor kwalifikacji w UI (training cost + duration)
    /// - Walidacja w <see cref="Runtime.CrewAssignmentService.IsConductorRequired"/>
    ///   i <see cref="Runtime.CrewCirculationValidator"/> Warstwa 5
    ///
    /// Decyzja design'owa user'a 2026-05-07: "okno w UI ale na razie pracownicy nie potrzebują
    /// żadnych kwalifikacji (na SM42, na EU160 itp.)" — placeholder data + UI tylko.
    /// </summary>
    [Serializable]
    public class EmployeeQualifications
    {
        /// <summary>
        /// Uprawnienia trakcyjne — ID seri pojazdów (z fleet catalog, np. "EN57", "EU07", "EU160").
        /// EA: pusta lista = "wszystkie" (semantyka permissive).
        /// </summary>
        public List<string> tractionPermits = new();

        /// <summary>
        /// Uprawnienia kategorii rozkładu — kody IRJ (np. "EI", "EN", "RO", "MP", "OS").
        /// EA: pusta lista = "wszystkie" (semantyka permissive).
        /// </summary>
        public List<string> categoryPermits = new();

        /// <summary>
        /// Helper: czy pracownik ma uprawnienie do tego ID/kategorii.
        /// EA: zawsze true (gameplay omija check). Post-EA: real lookup w listach.
        /// </summary>
        public bool HasTractionPermit(string vehicleSeriesId)
        {
            // EA: permissive. Post-EA: tractionPermits.Contains(vehicleSeriesId).
            return true;
        }

        public bool HasCategoryPermit(string categoryCode)
        {
            // EA: permissive. Post-EA: categoryPermits.Contains(categoryCode).
            return true;
        }

        /// <summary>True jeśli pracownik ma JAKIEKOLWIEK uprawnienia (UI display gate).</summary>
        public bool HasAnyQualification()
        {
            return (tractionPermits != null && tractionPermits.Count > 0)
                || (categoryPermits != null && categoryPermits.Count > 0);
        }
    }
}
