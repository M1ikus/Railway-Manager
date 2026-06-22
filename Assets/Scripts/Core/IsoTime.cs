using System;
using System.Globalization;

namespace RailwayManager.Core
{
    /// <summary>
    /// BUG-038: Helper dla ISO date/time parsing z gwarantowaną
    /// <see cref="CultureInfo.InvariantCulture"/>. Zastępuje raw <c>DateTime.Parse</c> /
    /// <c>TimeSpan.Parse</c> które zachowują się różnie per OS locale.
    ///
    /// Bez tego helper'a gracz z exotic locale (`tr-TR`/`fa-IR`/`de-DE`) → throw
    /// <see cref="FormatException"/> przy parsowaniu standardowych ISO strings
    /// ("2026-05-07", "12:34:00") → save corruption + crashe w hot path.
    ///
    /// Konwencja projektu: wszystkie persystowane daty/czasy są w ISO 8601 format
    /// (`YYYY-MM-DD` / `HH:MM:SS` / `YYYY-MM-DDTHH:MM:SS`), bez zależności od kraju.
    /// </summary>
    public static class IsoTime
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Parse ISO date string (np. "2026-05-07"). Throw FormatException przy fail.</summary>
        public static DateTime ParseDate(string s) => DateTime.Parse(s, Inv);

        /// <summary>Parse z explicit format (np. "yyyy-MM-dd"). Throw przy fail.</summary>
        public static DateTime ParseDateExact(string s, string format)
            => DateTime.ParseExact(s, format, Inv);

        /// <summary>Parse ISO time string (np. "12:34:56"). Throw przy fail.</summary>
        public static TimeSpan ParseTime(string s) => TimeSpan.Parse(s, Inv);

        /// <summary>Try parse ISO date — returns false zamiast throw.</summary>
        public static bool TryParseDate(string s, out DateTime result)
            => DateTime.TryParse(s, Inv, DateTimeStyles.None, out result);

        /// <summary>Try parse ISO time — returns false zamiast throw.</summary>
        public static bool TryParseTime(string s, out TimeSpan result)
            => TimeSpan.TryParse(s, Inv, out result);
    }
}
