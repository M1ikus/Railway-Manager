namespace RailwayManager.Personnel
{
    /// <summary>
    /// M6.5: Mnozniki kosztu pracodawcy (ponad pensje brutto pracownika).
    ///
    /// Pensja BRUTTO pracownika != koszt PRACODAWCY. Roznica to skladki ZUS pracodawcy
    /// (emerytalna + rentowa + wypadkowa + Fundusz Pracy + FGSP) plus ewentualnie PPK.
    /// W <see cref="PayrollService"/> mnozymy pensje brutto przez ten mnoznik zeby uzyskac
    /// total cost dla firmy odjety z budzetu.
    ///
    /// 🌍 ARCHITEKTURA PER-KRAJ (DLC ready, post-EA):
    ///   - W EA wszyscy pracownicy maja Employee.countryCode = "PL".
    ///   - W DLC (Niemcy, Czechy, Slowacja): per-kraj rozne stawki + tradycje.
    ///   - Lokalizacja depotu: pracownik dziedziczy countryCode z depotu zatrudnienia.
    ///     Niemiecki depot → niemieckie skladki pracodawcy + niemieckie pensje base.
    ///
    /// Wartosci real-world 2024-2025:
    /// - PL: ZUS pracodawcy = emerytalna 9.76% + rentowa 6.50% + wypadkowa 1.67-1.80%
    ///   + Fundusz Pracy 2.45% + FGSP 0.10% = ~20.5%. Konserwatywne 1.21f (z ewent. PPK 1.5%).
    /// - DE: Sozialversicherung ~21% pracodawcy (Krankenversicherung 7.55% + Renten 9.30%
    ///   + Arbeitslosen 1.30% + Pflege 1.525% + Unfall 0.80%). Multiplier 1.21f (placeholder).
    /// - CZ: Socialni pojisteni 24% + zdravotni 9% pracodawcy = ~24.8%. Multiplier 1.25f (placeholder).
    /// </summary>
    public static class PayrollConstants
    {
        // ═══ Per-country employer cost multipliers ═══

        /// <summary>PL: ZUS pracodawcy ~20.5% + ewent. PPK = ~1.21x kosztu na pensje brutto.</summary>
        public const float EmployerCostMultiplier_PL = 1.21f;

        /// <summary>DE: Sozialversicherung ~21% pracodawcy. Placeholder do post-EA DLC.</summary>
        public const float EmployerCostMultiplier_DE = 1.21f;

        /// <summary>CZ: Socialni + zdravotni ~25% pracodawcy. Placeholder do post-EA DLC.</summary>
        public const float EmployerCostMultiplier_CZ = 1.25f;

        /// <summary>SK: Podobne CZ. Placeholder do post-EA DLC.</summary>
        public const float EmployerCostMultiplier_SK = 1.24f;

        // ═══ API ═══

        /// <summary>
        /// Resolve mnoznika kosztu pracodawcy per kraj. EA: zawsze PL.
        /// Post-EA DLC: Employee.countryCode determinuje stawke.
        /// Fallback dla unknown country: PL (konserwatywnie).
        /// </summary>
        public static float GetEmployerCostMultiplier(string countryCode) => countryCode switch
        {
            "DE" => EmployerCostMultiplier_DE,
            "CZ" => EmployerCostMultiplier_CZ,
            "SK" => EmployerCostMultiplier_SK,
            _    => EmployerCostMultiplier_PL, // PL default (EA only)
        };

        /// <summary>Konwertuje brutto pracownika na total cost firmy.</summary>
        public static int ConvertBruttoToEmployerCost(int bruttoGroszy, string countryCode)
        {
            return (int)(bruttoGroszy * GetEmployerCostMultiplier(countryCode));
        }

        // ═══ Country codes (DLC roadmap) ═══

        public const string CountryPL = "PL";
        public const string CountryDE = "DE";
        public const string CountryCZ = "CZ";
        public const string CountrySK = "SK";

        /// <summary>Default kraj dla wszystkich pracownikow w EA.</summary>
        public const string DefaultCountryEA = CountryPL;

        // ═══ Quick-bonus tiers (one-time bonuses dla konkretnego pracownika) ═══
        // Unit: grosze (1 zł = 100 gr). UI Personnel/EmployeeDetailsUI.cs używa
        // 3 tiers jako quick-action buttons (~1k zł / ~5k zł / ~10k zł brutto).

        /// <summary>Quick-bonus mały: 1000 zł brutto (100_000 groszy).</summary>
        public const long QuickBonusSmallGroszy  = 100_000L;

        /// <summary>Quick-bonus średni: 5000 zł brutto (500_000 groszy).</summary>
        public const long QuickBonusMediumGroszy = 500_000L;

        /// <summary>Quick-bonus duży: 10000 zł brutto (1_000_000 groszy).</summary>
        public const long QuickBonusLargeGroszy  = 1_000_000L;
    }
}
