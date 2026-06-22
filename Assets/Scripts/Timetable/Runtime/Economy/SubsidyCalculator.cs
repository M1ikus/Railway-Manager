using System;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;
using RailwayManager.Core.GameRules;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M6-6 + M6.5-6: Dotacje wojewódzkie dla obiegów osobowych (D8).
    ///
    /// **M6.5-6 refactor:** stawki per-województwo z <see cref="SubsidyRulesCatalog"/>
    /// (subsidy_rules.json, 16 województw). Peryferyjne (Podkarpackie/Świętokrzyskie/etc.)
    /// dostają więcej (2500 zł/run) niż centralne (Mazowieckie 800 zł/run). Reference:
    /// Polregio Łódzkie 20.92 zł/pociągokm = ~2100 zł/run dla 100km — Łódzkie ma 1800 zł.
    ///
    /// Warunki kwalifikacji (evaluate na koniec dnia):
    /// - Obieg status Active
    /// - ≥ <c>MinRunsPerDayForFullSubsidy</c> (default 4) kursów zrealizowanych dziś
    /// - Średnia cena biletu ≤ <c>MaxAvgTicketPriceGroszy</c> (default 40 zł)
    ///   (surogat kategorii "Regional" — tanie kursy osobowe)
    ///
    /// Dotacja: <c>SubsidyRule.subsidyPerRunGroszy</c>(per województwo) × liczba kursów
    ///   × DifficultyModifiers.SubsidyMultiplier × <see cref="PunctualityMultiplier"/> (TD-007 done 2026-05-06).
    ///
    /// 🌍 ARCHITEKTURA PER-KRAJ (DLC ready, post-EA):
    ///   - W EA: tylko PL (16 województw).
    ///   - W DLC: per-kraj subsidy_rules + resolver wg kraju depotu/circulation.
    /// </summary>
    public static class SubsidyCalculator
    {
        // ── Legacy constants (zachowane dla backward compat / fallback gdy catalog nie loaded) ──

        public const int MinRunsPerDay = 4;
        public const int MaxAvgPriceGroszy = 4000;         // 40 zł — bumpnięte z 30 (real R/Osobowy 20-40 zł)
        public const int SubsidyPerRunGroszyFallback = 100000; // 1000 zł — fallback gdy catalog nie loaded

        // TD-007 (M-Balance 2026-05-06): punctuality KPI thresholds.
        // <60% on-time → 0× (gracz traci dotację za niską jakość)
        // 60-80% on-time → 0.5× (penalty)
        // >80% on-time → 1× (full subsidy)
        public const float PunctualityFullThreshold = 0.80f;
        public const float PunctualityZeroThreshold = 0.60f;
        public const float PunctualityHalfMultiplier = 0.5f;

        /// <summary>
        /// TD-007: zwraca multiplier dotacji [0..1] na podstawie punctuality ratio (LineBalance).
        /// Brak danych (0 ukończonych runów) = 1.0 (nie penalizujemy nowo aktywowanych obiegów).
        /// </summary>
        public static float PunctualityMultiplier(LineBalance lb)
        {
            if (lb == null) return 1f;
            float ratio = lb.PunctualityRatio;
            if (ratio >= PunctualityFullThreshold) return 1f;
            if (ratio < PunctualityZeroThreshold) return 0f;
            return PunctualityHalfMultiplier;
        }

        // ── Lookup helpers ──

        /// <summary>
        /// Pobiera primary voivodeship code dla obiegu (przez Circulation→Step→Timetable→Route).
        /// Null jeśli brak danych. Caller fallback na default rate.
        /// </summary>
        static string GetCirculationVoivodeshipCode(int circulationId)
        {
            var c = CirculationService.GetCirculation(circulationId);
            if (c == null || c.StepCount == 0) return null;

            int timetableId = c.steps[0].timetableId;
            Timetable timetable = null;
            foreach (var t in TimetableService.Timetables)
                if (t.id == timetableId) { timetable = t; break; }
            if (timetable == null) return null;

            Route route = null;
            foreach (var r in TimetableService.Routes)
                if (r.id == timetable.routeId) { route = r; break; }
            return route?.startVoivodeship;
        }

        /// <summary>Stawka per-run dla obiegu (per województwo, z catalog'u). Fallback gdy brak.</summary>
        static int GetSubsidyPerRunForCirculation(int circulationId)
        {
            string code = GetCirculationVoivodeshipCode(circulationId);
            if (string.IsNullOrEmpty(code)) return SubsidyPerRunGroszyFallback;
            int fromCatalog = SubsidyRulesCatalog.GetSubsidyPerRunGroszy(code);
            return fromCatalog > 0 ? fromCatalog : SubsidyPerRunGroszyFallback;
        }

        // ── Public API ──

        /// <summary>
        /// Ocenia czy obieg kwalifikuje się do dotacji i zwraca kwotę [gr].
        /// Zero jeśli nie kwalifikuje się.
        /// </summary>
        public static int CalculateDailySubsidy(LineBalance lb)
        {
            if (lb == null) return 0;

            // MB-1 Phase B: GameRule check — gracz wybral hard mode bez dotacji
            if (!GameRulesService.IsEnabled(GameRule.VoivodeshipSubsidies)) return 0;

            var defaults = SubsidyRulesCatalog.Defaults;
            int minRuns = defaults?.minRunsPerDayForFullSubsidy ?? MinRunsPerDay;
            int maxAvgPrice = defaults?.maxAvgTicketPriceGroszy ?? MaxAvgPriceGroszy;

            if (lb.runsCompletedToday < minRuns) return 0;
            if (lb.passengerCount <= 0) return 0;

            // Średnia cena biletu = revenue / passengerCount
            long avgPriceGroszy = lb.revenueGroszy / lb.passengerCount;
            if (avgPriceGroszy > maxAvgPrice) return 0;

            // M6.5-6: stawka per-województwo (peryferyjne 2500/run, centralne 800/run)
            int subsidyPerRun = GetSubsidyPerRunForCirculation(lb.circulationId);

            // MB-1 Phase B: difficulty multiplier (Easy 1.5x, Realistic 0.5x)
            float diffMult = DifficultyService.Modifiers.SubsidyMultiplier;

            // TD-007 (M-Balance 2026-05-06): punctuality KPI multiplier.
            // Tracking w EconomyManager.OnRunDespawned per LineBalance (on-time vs late ±5min).
            // Curve: <60% = 0×, 60-80% = 0.5×, >80% = 1×. Brak historii = 1× (nie karzemy nowych).
            float punctualityMult = PunctualityMultiplier(lb);

            // BUG-073: clamp long → int z ostrzeżeniem przy overflow. Modded gameplay
            // (subsidyPerRun 1M groszy × 50 runs × 1.5 = 75M groszy) przekracza int.MaxValue.
            long resultLong = (long)Math.Round(subsidyPerRun * (long)lb.runsCompletedToday * diffMult * punctualityMult);
            if (resultLong > int.MaxValue)
            {
                Log.Warn($"[SubsidyCalculator] Subsidy overflow: {resultLong} > int.MaxValue, clamped");
                return int.MaxValue;
            }
            if (resultLong < 0) return 0; // negative = sanity check, nie powinno się zdarzyć
            return (int)resultLong;
        }

        /// <summary>
        /// Tekst diagnostyczny dla UI: wyjaśnia czy + ile dotacji, dlaczego nie.
        /// </summary>
        public static string Explain(LineBalance lb)
        {
            if (lb == null) return "(brak danych)";

            // MB-1 Phase B: rule explanation
            if (!GameRulesService.IsEnabled(GameRule.VoivodeshipSubsidies))
                return "Dotacje wojewódzkie wyłączone (regułą gry)";

            var defaults = SubsidyRulesCatalog.Defaults;
            int minRuns = defaults?.minRunsPerDayForFullSubsidy ?? MinRunsPerDay;
            int maxAvgPrice = defaults?.maxAvgTicketPriceGroszy ?? MaxAvgPriceGroszy;

            if (lb.runsCompletedToday < minRuns)
                return $"Za mało kursów ({lb.runsCompletedToday}/{minRuns})";

            if (lb.passengerCount <= 0)
                return "Brak pasażerów — dotacja = 0";

            long avgPrice = lb.revenueGroszy / lb.passengerCount;
            if (avgPrice > maxAvgPrice)
                return $"Średnia cena biletu {avgPrice / 100f:F1}zł > limit {maxAvgPrice / 100f:F0}zł";

            string voiv = GetCirculationVoivodeshipCode(lb.circulationId);
            int subsidyPerRun = GetSubsidyPerRunForCirculation(lb.circulationId);
            int amount = CalculateDailySubsidy(lb);
            float diffMult = DifficultyService.Modifiers.SubsidyMultiplier;
            float punctMult = PunctualityMultiplier(lb);
            string multInfo = (System.Math.Abs(diffMult - 1f) > 0.01f) ? $" × diff {diffMult:F2}" : "";
            string voivInfo = !string.IsNullOrEmpty(voiv) ? $" [{voiv}: {subsidyPerRun / 100f:F0}zł/run]" : " [voiv?: fallback]";

            // TD-007: punctuality info — jawny breakdown kursów + multiplier
            int totalKlasyfikowanych = lb.punctualOnTimeToday + lb.punctualLateToday;
            string punctInfo = totalKlasyfikowanych > 0
                ? $" punkt {lb.PunctualityRatio * 100:F0}% ({lb.punctualOnTimeToday}/{totalKlasyfikowanych}) × {punctMult:F1}"
                : " punkt n/d";

            return $"Dotacja {amount / 100f:F0}zł ({lb.runsCompletedToday} kursów{multInfo}{punctInfo}){voivInfo}";
        }
    }
}
