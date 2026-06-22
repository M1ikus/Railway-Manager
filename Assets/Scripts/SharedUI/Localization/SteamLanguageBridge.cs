namespace RailwayManager.SharedUI.Localization
{
    /// <summary>
    /// Stub dla Steam autodetect języka (M13-3). Pełna integracja w M14
    /// razem z resztą Steamworks SDK (Steam Cloud, achievements, store).
    ///
    /// Pre-EA / dev: zawsze zwraca null → fallback do <see cref="System.Globalization.CultureInfo.CurrentCulture"/>
    /// w <see cref="LocaleResolver"/>.
    ///
    /// Po wpięciu Steamworks (Facepunch.Steamworks lub Steamworks.NET) ten stub
    /// zostanie zastąpiony wywołaniem <c>SteamApps.GetCurrentGameLanguage()</c>
    /// zwracającym jeden z: "polish" / "english" / "german" / "czech" / "japanese" /
    /// "russian" / "ukrainian" — mapowanie do <see cref="LocaleCode"/> robione
    /// w <see cref="LocaleResolver"/>.
    /// </summary>
    public static class SteamLanguageBridge
    {
        /// <summary>
        /// Zwraca string Steam language ("polish", "english", ...) jeśli Steamworks
        /// jest aktywne i połączone. <c>null</c> w pozostałych przypadkach (offline, dev).
        /// </summary>
        public static string GetCurrentGameLanguage()
        {
            // M13-3 stub: brak Steamworks SDK w projekcie.
            // M14 wpięcie: return SteamApps.GetCurrentGameLanguage();
            return null;
        }

        /// <summary>True jeśli Steam jest aktywny i można zaufać <see cref="GetCurrentGameLanguage"/>.</summary>
        public static bool IsAvailable => false; // M14: SteamManager.Initialized && SteamApps.IsSubscribed
    }
}
