namespace RailwayManager.Core.Difficulty
{
    /// <summary>
    /// Stałe bazowe dla difficulty modifiers — używane jako "Normal" baseline (×1.0×),
    /// preset'y aplikują multipliers (Easy 2.0× / Hard 0.7× / Realistic 0.5×).
    ///
    /// CLAUDE.md: magic numbers balansowe muszą być w <c>*Constants.cs</c>, nie w UI partials.
    /// Konsumenci: GameCreator (apply preset → GameState), DifficultyService (runtime modifiers).
    ///
    /// Te stałe NIE są w <c>Timetable.Economy.EconomyConstants</c> bo GameCreator (asmdef)
    /// nie referuje Timetable — architektura projektu zabrania (Timetable to wyższa warstwa).
    /// Refaktor "Money → grosze + scentralizowanie" odłożony do M-Balance.
    /// </summary>
    public static class DifficultyConstants
    {
        /// <summary>
        /// Bazowy budżet startowy gracza "Normal" preset: 100 000 000 zł.
        /// Unit: PLN (zł), spójne z <see cref="GameState.Money"/>.
        ///
        /// Difficulty presets:
        ///   Easy 2.0× = 200M  → 2× FLIRT_LM4268 (70M) + dobry depot (40M) + 90M bufor
        ///   Normal 1.0× = 100M → 1× FLIRT_LM4268 (35M) + min depot (20M) + 45M bufor
        ///   Hard 0.7× = 70M    → 1× Griffin EU160 (25M) + min depot (15M) + 30M bufor (~6 mies pensji)
        ///   Realistic 0.5× = 50M → 1× SA138 (14M) + bardzo min depot (13M) + 23M bufor (~4 mies pensji, challenge)
        ///
        /// Wszystkie preset'y gwarantują: pełnoprawny depot + minimum 1 nowy zespół trakcyjny.
        /// </summary>
        public const long BaseStartingBudgetPln = 100_000_000L;

        /// <summary>
        /// Bazowa miesięczna pensja maszynisty "Normal": 9 000 zł.
        /// Real-world: średnia maszynista PKP IC + Polregio (2024-25, MB6.5-3 research).
        /// Unit: PLN/miesiąc.
        /// </summary>
        public const int BaseDriverSalaryPln = 9_000;

        /// <summary>
        /// Bazowy interwał awarii "Normal": 1 awaria co 500 km (typowa).
        /// Bumpnięty z 300 km zgodnie z <c>reliabilityScore</c> z fleet catalog'u.
        /// Unit: km między awariami.
        /// </summary>
        public const int BaseBreakdownPerKm = 500;
    }
}
