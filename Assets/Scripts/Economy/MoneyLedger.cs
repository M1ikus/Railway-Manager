using System;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;

namespace RailwayManager.Economy
{
    /// <summary>
    /// M-Economy: prymityw pieniędzy gracza. JEDYNY ruchacz <see cref="GameState.Money"/> +
    /// sumy dzienne (przychód/koszt/dotacje) + breakdown per-kategoria.
    ///
    /// Niska warstwa (asmdef Economy, refs tylko Core) — referują WSZYSCY którzy wydają/zarabiają:
    /// Fleet (zakup taboru, modernizacje), Depot (budowa, rozbudowa), Timetable (bilety, koszty per-km).
    /// Rozwiązuje problem: EconomyManager mieszka w Timetable, a Fleet/Depot są poniżej i nie mogą
    /// go wołać — teraz wołają MoneyLedger bezpośrednio. EconomyManager (Timetable) buduje na tym
    /// per-obieg / historię / dotacje, delegując ruch pieniędzy tutaj. Jeden ruchacz = brak double-count.
    ///
    /// Static (jak FleetService) — bez lifecycle. Save/Load przez Restore/Reset.
    /// </summary>
    public static class MoneyLedger
    {
        // ── Sumy dzienne (running) ───────────────────────────────────
        public static long RevenueTodayGroszy { get; private set; }
        public static long CostsTodayGroszy { get; private set; }
        public static long SubsidiesTodayGroszy { get; private set; }
        public static long NetTodayGroszy => RevenueTodayGroszy - CostsTodayGroszy + SubsidiesTodayGroszy;

        /// <summary>Koszt dziś per-kategoria (dla FinancePanelUI drill-down „co dzisiaj wydałem").</summary>
        static readonly Dictionary<string, long> _costsByCategory = new();
        public static IReadOnlyDictionary<string, long> CostsByCategory => _costsByCategory;

        /// <summary>
        /// BUG-054: akumulator groszy (signed). Sub-1zł kwoty agregowane do flush gdy ≥100gr.
        /// Przeniesione z EconomyManager — zachowuje sub-1zł precision (bilet 99gr ×100 = 99zł).
        /// </summary>
        static long _pendingMoneyGroszy;

        /// <summary>
        /// MB-1 Phase B: kategorie „operacyjne" skalowane przez OperationalCostMultiplier (Hard +30%,
        /// Easy -30%). Capital (vehicle_purchase, construction_*) NIE jest tu → bez mnożnika.
        /// </summary>
        static readonly HashSet<string> OperationalCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            "overhead", "operational", "platform_fee", "maintenance", "maintenance_external", "parts"
        };

        static void Flush()
        {
            long wholeZl = _pendingMoneyGroszy / 100; // truncate toward zero (signed OK)
            if (wholeZl != 0)
            {
                GameState.Money += wholeZl;
                _pendingMoneyGroszy -= wholeZl * 100;
            }
        }

        // ── Prymitywy (Timetable EconomyManager buduje na tym per-obieg) ──

        public static void AddRevenue(long amountGroszy)
        {
            if (amountGroszy <= 0) return;
            RevenueTodayGroszy += amountGroszy;
            _pendingMoneyGroszy += amountGroszy;
            Flush();
        }

        public static void AddSubsidy(long amountGroszy)
        {
            if (amountGroszy <= 0) return;
            SubsidiesTodayGroszy += amountGroszy;
            _pendingMoneyGroszy += amountGroszy;
            Flush();
        }

        /// <summary>Koszt [gr] w kategorii. Zwraca FAKTYCZNIE pobraną kwotę (po difficulty mult dla
        /// kategorii operacyjnych) — caller (EconomyManager) używa do per-line attribution.</summary>
        public static long AddCost(long amountGroszy, string category)
        {
            if (amountGroszy <= 0) return 0;
            if (category != null && OperationalCategories.Contains(category))
            {
                float m = DifficultyService.Modifiers.OperationalCostMultiplier;
                amountGroszy = (long)(amountGroszy * m);
                if (amountGroszy <= 0) return 0;
            }
            CostsTodayGroszy += amountGroszy;
            _pendingMoneyGroszy -= amountGroszy;
            Flush();
            if (!string.IsNullOrEmpty(category))
                _costsByCategory[category] = (_costsByCategory.TryGetValue(category, out var c) ? c : 0L) + amountGroszy;
            return amountGroszy;
        }

        // ── Capital convenience (Fleet/Depot — bez kontekstu obiegu) ──

        /// <summary>Czy gracza stać na <paramref name="groszy"/> [gr] (Money [zł] × 100 ≥ koszt).
        /// Polityka „nie stać → nie rób": caller sprawdza PRZED <see cref="Spend"/>.</summary>
        public static bool CanAfford(long groszy) => GameState.Money * 100L >= groszy;

        /// <summary>Wydatek kapitałowy [gr] (zakup taboru, budowa). NIE sprawdza czy stać — caller robi CanAfford.</summary>
        public static void Spend(long groszy, string category, string source) => AddCost(groszy, category);

        /// <summary>Przychód kapitałowy [gr] (refund przy undo/usunięciu, sprzedaż).</summary>
        public static void Earn(long groszy, string category, string source) => AddRevenue(groszy);

        // ── Day cycle + Save/Load ────────────────────────────────────

        /// <summary>Reset sum dziennych (po archiwizacji dnia w EconomyManager.OnDayEnded). Resztki
        /// akumulatora &lt;1zł przepadają (jak było — akceptowalne).</summary>
        public static void ResetDayTotals()
        {
            RevenueTodayGroszy = 0;
            CostsTodayGroszy = 0;
            SubsidiesTodayGroszy = 0;
            _costsByCategory.Clear();
        }

        /// <summary>Pełny reset (nowa gra).</summary>
        public static void ResetAll()
        {
            ResetDayTotals();
            _pendingMoneyGroszy = 0;
        }

        /// <summary>Restore sum z save'a (EconomyManager.RestoreFromSave). Akumulator czyszczony.</summary>
        public static void RestoreTotals(long revenueGroszy, long costsGroszy, long subsidiesGroszy)
        {
            RevenueTodayGroszy = revenueGroszy;
            CostsTodayGroszy = costsGroszy;
            SubsidiesTodayGroszy = subsidiesGroszy;
            _costsByCategory.Clear();
            _pendingMoneyGroszy = 0;
        }
    }
}
