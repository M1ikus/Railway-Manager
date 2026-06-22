using System;
using DepotSystem;
using DepotSystem.RoomLevel;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-15 / §3.6: Pracownicy biurowi obnizaja fixed costs firmy.
    ///
    /// Wzor (D3.6): <c>reduction = sum(per clerk × skill/5 × 0.01)</c>, max 30%.
    /// Innymi slowy: kazdy 5★ clerk = -5% kosztu, kazdy 1★ = -1%.
    /// Cap 30% (6 clerks 5★ = 30%, wiecej = plateau).
    ///
    /// Hook dla M6 EconomyManager: <c>dailyOverhead × (1 - GetFixedCostReduction())</c>.
    /// Integracja wymaga dodania hook w EconomyManager (post-M8).
    ///
    /// Uproszczenie szybkosci rekrutacji (D3.6): +1 biurowy = -1 dzien refresh cyklu (min 3 dni).
    /// </summary>
    public static class OfficeService
    {
        public static event Action OnOfficeChanged;

        public const float MaxReduction = 0.30f;

        /// <summary>
        /// Zwraca procent obnizenia fixed costs (0.00-0.30). Default 0 gdy brak biurowych.
        /// Wzor: sum(skill/5 × 0.01) clampowany do 30%.
        /// </summary>
        public static float GetFixedCostReduction()
        {
            float total = 0f;
            foreach (var e in PersonnelService.Employees)
            {
                if (e.role != EmployeeRole.Office) continue;
                if (!e.IsActive) continue;
                // Tylko OnShift lub Available (nie liczy Sick/Resting)
                if (e.status != EmployeeStatus.OnShift && e.status != EmployeeStatus.Available) continue;
                total += PersonnelBalanceConstants.OfficeFixedCostReductionPerClerkPerStar * e.skill;
            }
            return Math.Min(MaxReduction, total);
        }

        /// <summary>Mnoznik kosztu (1.0 - reduction). Dla wygodnego uzycia w EconomyManager.</summary>
        public static float GetFixedCostMultiplier() => 1f - GetFixedCostReduction();

        /// <summary>Liczba dni refresh'u rynku pracy zmodyfikowana przez clerkow (D3.6): -1 dzien per clerk, min 3.</summary>
        public static int GetAdjustedMarketRefreshDays()
        {
            int clerks = PersonnelService.CountActiveByRole(EmployeeRole.Office);
            int days = PersonnelBalanceConstants.CandidateMarketRefreshDays - clerks;
            return Math.Max(3, days);
        }

        /// <summary>
        /// Wywolane gdy status pracownika biurowego sie zmienia (OnShift/Sick/Fired) —
        /// UI moze re-refreshowac preview.
        /// </summary>
        public static void NotifyChanged() => OnOfficeChanged?.Invoke();

        // ════════════════════════════════════════════════════════
        //  MM-5 — Office lvl bonusy (spec 2.2)
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// MM-5: R&D speed multiplier dla <see cref="ResearchService"/> daily tick,
        /// w zależności od lvla najlepszego pokoju Office (MM-D15 best-lvl-wins).
        ///
        /// Mapping z spec'a sekcja 2.2:
        /// <list type="bullet">
        /// <item>brak Office → 0.0 (research zatrzymany, brak postępu)</item>
        /// <item>lvl 1 → 1.0× (baseline)</item>
        /// <item>lvl 2 → 1.2×</item>
        /// <item>lvl 3 → 1.5×</item>
        /// <item>lvl 4 → 1.8×</item>
        /// <item>lvl 5 → 2.2× (max)</item>
        /// </list>
        ///
        /// MM-D22: drabinka unlocków R&D wycięta do osobnego milestone'u.
        /// Tutaj tylko speed multiplier (×) dla currently active path.
        /// </summary>
        public static float GetResearchSpeedMultiplier()
        {
            var svc = RoomLevelService.Instance;
            if (svc == null) return 1.0f; // defensive — przed Depot scene załaduje się
            int lvl = svc.GetBestLevelForType(RoomType.Office);
            return lvl switch
            {
                0 => 0.0f,  // brak biura — research stoi
                1 => 1.0f,
                2 => 1.2f,
                3 => 1.5f,
                4 => 1.8f,
                5 => 2.2f,
                _ => 1.0f,
            };
        }

        /// <summary>
        /// MM-5: max liczba biurowych (Office + Research łącznie, dzielą cap z Office room).
        /// Convenience wrapper na <see cref="RoleCaps"/>.
        /// </summary>
        public static int GetMaxOfficeHeadcount() => RoleCaps.OfficeCapForLvl(GetOfficeLvl());

        /// <summary>Aktualny lvl pokoju Office (0 gdy brak), MM-D15 best-lvl-wins.</summary>
        public static int GetOfficeLvl()
        {
            var svc = RoomLevelService.Instance;
            return svc == null ? 0 : svc.GetBestLevelForType(RoomType.Office);
        }
    }
}
