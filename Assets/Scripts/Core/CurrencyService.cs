namespace RailwayManager.Core
{
    /// <summary>Waluta wyświetlania (M-Economy Faza 2). ISO 4217.</summary>
    public enum Currency
    {
        PLN = 0, // złoty polski (baza wewnętrzna gry)
        EUR = 1, // euro
        USD = 2, // dolar amerykański
        CZK = 3, // korona czeska
    }

    /// <summary>
    /// M-Economy Faza 2: model waluty WYŚWIETLANIA. Baza wewnętrzna gry to ZAWSZE PLN (grosze) —
    /// <see cref="GameState.Money"/> i cała ekonomia liczą w złotówkach. Tu konwertujemy tylko do
    /// PREZENTACJI (gracz wybiera w Settings, persistowane przez SettingsService).
    ///
    /// To NIE forex: brak operacji wielowalutowych, brak przewalutowań w logice, kursy stałe.
    /// Pełny forex (operacje w DE zarabiają EUR itd.) = post-EA. Gotowe pod DLC per-kraj.
    ///
    /// Kursy = STAŁE GAME CONSTANTS (przybliżone, NIE live), tunable w M-Balance.
    /// W Core (obok Money), bo <c>NumberFormatService</c> (SharedUI) musi formatować.
    /// </summary>
    public static class CurrencyService
    {
        // Ile PLN = 1 jednostka danej waluty. Przybliżone (2024-25), tunable (M-Balance).
        public const decimal PlnPerEur = 4.30m;
        public const decimal PlnPerUsd = 4.00m;
        public const decimal PlnPerCzk = 0.17m;

        /// <summary>Aktualna waluta wyświetlania. Ustawiana przez SettingsService (z PlayerPrefs).
        /// Default PLN (zachowanie identyczne jak przed Fazą 2).</summary>
        public static Currency DisplayCurrency { get; set; } = Currency.PLN;

        /// <summary>Ile PLN przypada na 1 jednostkę waluty (PLN = 1).</summary>
        public static decimal PlnPerUnit(Currency c) => c switch
        {
            Currency.EUR => PlnPerEur,
            Currency.USD => PlnPerUsd,
            Currency.CZK => PlnPerCzk,
            _ => 1m,
        };

        /// <summary>Konwertuje kwotę w PLN (zł) na walutę wyświetlania (lub podaną).</summary>
        public static decimal ConvertFromPln(decimal plnAmount, Currency? target = null)
        {
            var c = target ?? DisplayCurrency;
            return c == Currency.PLN ? plnAmount : plnAmount / PlnPerUnit(c);
        }

        /// <summary>Symbol waluty (zł / € / $ / Kč).</summary>
        public static string Symbol(Currency c) => c switch
        {
            Currency.EUR => "€",
            Currency.USD => "$",
            Currency.CZK => "Kč",
            _ => "zł",
        };

        /// <summary>Czy symbol przed kwotą ($1 234 — konwencja USD) czy po (1 234 zł — reszta).</summary>
        public static bool IsPrefixSymbol(Currency c) => c == Currency.USD;
    }
}
