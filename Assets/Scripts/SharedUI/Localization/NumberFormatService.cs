using System;
using System.Globalization;
using RailwayManager.Core;

namespace RailwayManager.SharedUI.Localization
{
    /// <summary>
    /// Formatowanie liczb / dat / waluty per current locale (M13-3).
    ///
    /// Waluta w grze ZAWSZE PLN (sim w polskich realiach) — lokalizujemy tylko
    /// formatting (separatory tysięcy/dziesiętne), znak waluty zawsze "zł".
    /// Gracz JP widzi "1,234 zł" co znaczy "1234 PLN sformatowane po japońsku" —
    /// to symulacja kolejowa, nie kursy walut.
    ///
    /// Przykłady (decimals=2):
    /// <code>
    /// PL: 1 234,56 zł     EN: 1,234.56 zł     DE: 1.234,56 zł
    /// JP: 1,234.56 zł     RU: 1 234,56 zł     UK: 1 234,56 zł
    /// </code>
    /// </summary>
    public static class NumberFormatService
    {
        /// <summary>
        /// Format kwoty pieniężnej. <paramref name="amount"/> jest w PLN (zł) — bazie wewnętrznej gry —
        /// i konwertowane do waluty WYŚWIETLANIA (<see cref="CurrencyService.DisplayCurrency"/>) z jej
        /// symbolem. Separatory tysięcy/dziesiętne per current locale. Default PLN → "X zł" (jak dawniej).
        ///
        /// <para><b>M-Economy Faza 2:</b> dodana wielowalutowość wyświetlania (PLN/EUR/USD/CZK). Wcześniej
        /// twardo "zł". Konwersja kursem z CurrencyService (NIE forex — tylko prezentacja).</para>
        /// <para><b>Bug-fix 2026-05-15:</b> explicit <see cref="NumberFormatInfo"/> zamiast
        /// <c>ToString("C", culture)</c> (kultura wstrzykiwała własny symbol $/€/¥).</para>
        /// </summary>
        /// <param name="decimals">Ile miejsc po przecinku (default 2). <c>long</c> overload używa 0.</param>
        public static string FormatCurrency(decimal amount, LocaleCode? localeOverride = null, int decimals = 2)
        {
            var locale = localeOverride ?? LocalizationService.CurrentLocale;
            var culture = LocaleResolver.ToCultureInfo(locale);
            var currency = CurrencyService.DisplayCurrency;
            decimal display = CurrencyService.ConvertFromPln(amount, currency);

            var nfi = (NumberFormatInfo)culture.NumberFormat.Clone();
            nfi.CurrencySymbol = CurrencyService.Symbol(currency);
            bool prefix = CurrencyService.IsPrefixSymbol(currency);
            nfi.CurrencyPositivePattern = prefix ? 0 : 3; // "$n" : "n $" (suffix ze spacją)
            nfi.CurrencyNegativePattern = prefix ? 1 : 8; // "-$n" : "-n $"
            nfi.CurrencyDecimalDigits = decimals;
            return display.ToString("C", nfi);
        }

        /// <summary>Convenience overload dla integer / long — domyślnie 0 miejsc po przecinku (typowe dla money UI tycoona).</summary>
        public static string FormatCurrency(long amount, LocaleCode? localeOverride = null)
            => FormatCurrency((decimal)amount, localeOverride, decimals: 0);

        /// <summary>Format daty według current locale (short pattern).</summary>
        public static string FormatDate(DateTime date, LocaleCode? localeOverride = null)
        {
            var locale = localeOverride ?? LocalizationService.CurrentLocale;
            var culture = LocaleResolver.ToCultureInfo(locale);
            return date.ToString("d", culture);
        }

        /// <summary>Format datetime z czasem (short date + short time).</summary>
        public static string FormatDateTime(DateTime dt, LocaleCode? localeOverride = null)
        {
            var locale = localeOverride ?? LocalizationService.CurrentLocale;
            var culture = LocaleResolver.ToCultureInfo(locale);
            return dt.ToString("g", culture);
        }

        /// <summary>Format liczby z separatorami tysięcy + opcjonalna ilość miejsc po przecinku.</summary>
        public static string FormatNumber(decimal n, int decimals = 0, LocaleCode? localeOverride = null)
        {
            var locale = localeOverride ?? LocalizationService.CurrentLocale;
            var culture = LocaleResolver.ToCultureInfo(locale);
            string fmt = decimals > 0 ? $"N{decimals}" : "N0";
            return n.ToString(fmt, culture);
        }

        public static string FormatNumber(long n, LocaleCode? localeOverride = null)
            => FormatNumber((decimal)n, 0, localeOverride);

        public static string FormatNumber(int n, LocaleCode? localeOverride = null)
            => FormatNumber((decimal)n, 0, localeOverride);

        /// <summary>Format procentu (0.85 → "85%" wg locale formatting).</summary>
        public static string FormatPercent(double fraction, int decimals = 0, LocaleCode? localeOverride = null)
        {
            var locale = localeOverride ?? LocalizationService.CurrentLocale;
            var culture = LocaleResolver.ToCultureInfo(locale);
            string fmt = decimals > 0 ? $"P{decimals}" : "P0";
            return fraction.ToString(fmt, culture);
        }

        /// <summary>Aktualna kultura (skrót dla integracji z innymi serwisami które potrzebują CultureInfo).</summary>
        public static CultureInfo CurrentCulture => LocaleResolver.ToCultureInfo(LocalizationService.CurrentLocale);
    }
}
