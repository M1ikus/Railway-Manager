namespace RailwayManager.SharedUI.Localization
{
    /// <summary>
    /// Konkretny kod języka (M13-3). Mapowany 1:1 do folderu zasobów
    /// <c>Resources/Locale/{folder}/strings.json</c> + ISO/Steam codes
    /// (<see cref="LocaleResolver"/>).
    ///
    /// Różni się od <see cref="RailwayManager.Core.Settings.LanguagePreference"/>
    /// (która ma dodatkową wartość <c>Auto</c> dla Steam autodetect). LocaleCode jest
    /// "konkretną decyzją" — po Resolve preference nigdy nie ma Auto.
    /// </summary>
    public enum LocaleCode
    {
        PL,  // Polski (główna społeczność)
        EN,  // English (lingua franca)
        DE,  // Deutsch
        CZ,  // Čeština
        JP,  // 日本語
        RU,  // Русский (puste fallback do EN na EA)
        UK   // Українська (puste fallback do EN na EA)
    }
}
