using System.Collections.Generic;
using System.Globalization;
using RailwayManager.Core;
using RailwayManager.Core.Settings;

namespace RailwayManager.SharedUI.Localization
{
    /// <summary>
    /// Rozstrzyga konkretny <see cref="LocaleCode"/> na podstawie:
    /// 1. Manual override z Settings (<see cref="LanguagePreference"/> != Auto)
    /// 2. Steam autodetect (M14 — obecnie stub w <see cref="SteamLanguageBridge"/>)
    /// 3. System locale (<see cref="CultureInfo.CurrentCulture"/>)
    /// 4. Fallback: <see cref="LocaleCode.EN"/>
    ///
    /// Używane przez <see cref="LocalizationService"/> przy bootstrap i przy zmianie
    /// preference w Settings UI.
    ///
    /// <para><b>Refactor 2026-05-15:</b> wszystkie 4 attributes per locale (folder name /
    /// ISO 2-letter / Steam language / CultureInfo name) w jednym <c>_registry</c> dictionary
    /// zamiast 5 osobnych switchy. Dodanie nowego języka = 1 wpis w dict + 1 wartość w
    /// <see cref="LocaleCode"/> enum, nie 5 miejsc do edycji.</para>
    /// </summary>
    public static class LocaleResolver
    {
        private readonly struct LocaleDef
        {
            public readonly string FolderName;
            public readonly string IsoCode;     // CultureInfo.TwoLetterISOLanguageName (np. "cs" dla Czech, nie "cz")
            public readonly string SteamLang;   // SteamApps.GetCurrentGameLanguage() ("polish"/"english"/…)
            public readonly string CultureName; // CultureInfo culture name ("pl-PL"/"en-US"/…)

            public LocaleDef(string folderName, string isoCode, string steamLang, string cultureName)
            {
                FolderName = folderName;
                IsoCode = isoCode;
                SteamLang = steamLang;
                CultureName = cultureName;
            }
        }

        private static readonly Dictionary<LocaleCode, LocaleDef> _registry = new()
        {
            [LocaleCode.PL] = new("pl", "pl", "polish",     "pl-PL"),
            [LocaleCode.EN] = new("en", "en", "english",    "en-US"),
            [LocaleCode.DE] = new("de", "de", "german",     "de-DE"),
            [LocaleCode.CZ] = new("cz", "cs", "czech",      "cs-CZ"),
            [LocaleCode.JP] = new("jp", "ja", "japanese",   "ja-JP"),
            [LocaleCode.RU] = new("ru", "ru", "russian",    "ru-RU"),
            [LocaleCode.UK] = new("uk", "uk", "ukrainian",  "uk-UA"),
        };

        // ─── Public resolve ───────────────────────────────

        /// <summary>
        /// Rozstrzyga preference do konkretnego LocaleCode. Wywoływane raz przy bootstrap
        /// + każdorazowo gdy gracz zmienia preference w Settings.
        /// </summary>
        public static LocaleCode Resolve(LanguagePreference preference)
        {
            // 1. Manual override (gracz wybrał konkretny język w Settings)
            if (preference != LanguagePreference.Auto)
                return PreferenceToCode(preference);

            // 2. Steam autodetect (M14 — obecnie stub returning null)
            if (SteamLanguageBridge.IsAvailable)
            {
                var steamLang = SteamLanguageBridge.GetCurrentGameLanguage();
                var steamCode = SteamStringToCode(steamLang);
                if (steamCode.HasValue)
                {
                    Log.Info($"[LocaleResolver] Steam autodetect: {steamLang} → {steamCode.Value}");
                    return steamCode.Value;
                }
            }

            // 3. System locale via CultureInfo
            var sysCode = CultureInfoToCode(CultureInfo.CurrentCulture);
            if (sysCode.HasValue)
            {
                Log.Info($"[LocaleResolver] System locale: {CultureInfo.CurrentCulture.Name} → {sysCode.Value}");
                return sysCode.Value;
            }

            // 4. Final fallback
            Log.Info("[LocaleResolver] No match — fallback to EN");
            return LocaleCode.EN;
        }

        // ─── Public lookups (z registry) ──────────────────

        /// <summary>
        /// Konwertuje <see cref="LocaleCode"/> na nazwę folderu w
        /// <c>Resources/Locale/{folder}/strings.json</c>. Lowercase ISO-like kody.
        /// </summary>
        public static string ToFolderName(LocaleCode code) =>
            _registry.TryGetValue(code, out var def) ? def.FolderName : "en";

        /// <summary>
        /// Konwertuje <see cref="LocaleCode"/> na <see cref="CultureInfo"/> dla
        /// <see cref="NumberFormatService"/> (currency/date formatting).
        /// </summary>
        public static CultureInfo ToCultureInfo(LocaleCode code) =>
            _registry.TryGetValue(code, out var def)
                ? new CultureInfo(def.CultureName)
                : CultureInfo.InvariantCulture;

        // ─── Private helpers ──────────────────────────────

        /// <summary>1:1 mapping <see cref="LanguagePreference"/> → <see cref="LocaleCode"/>.</summary>
        private static LocaleCode PreferenceToCode(LanguagePreference pref)
        {
            return pref switch
            {
                LanguagePreference.PL => LocaleCode.PL,
                LanguagePreference.EN => LocaleCode.EN,
                LanguagePreference.DE => LocaleCode.DE,
                LanguagePreference.CZ => LocaleCode.CZ,
                LanguagePreference.JP => LocaleCode.JP,
                LanguagePreference.RU => LocaleCode.RU,
                LanguagePreference.UK => LocaleCode.UK,
                _                     => LocaleCode.EN
            };
        }

        /// <summary>Reverse lookup: Steam language string → LocaleCode.</summary>
        private static LocaleCode? SteamStringToCode(string steamLang)
        {
            if (string.IsNullOrEmpty(steamLang)) return null;
            var needle = steamLang.ToLowerInvariant();
            foreach (var kv in _registry)
            {
                if (kv.Value.SteamLang == needle) return kv.Key;
            }
            return null;
        }

        /// <summary>Reverse lookup: CultureInfo (system locale) → LocaleCode po ISO 2-letter.</summary>
        private static LocaleCode? CultureInfoToCode(CultureInfo culture)
        {
            if (culture == null) return null;
            var needle = culture.TwoLetterISOLanguageName.ToLowerInvariant();
            foreach (var kv in _registry)
            {
                if (kv.Value.IsoCode == needle) return kv.Key;
            }
            return null;
        }
    }
}
