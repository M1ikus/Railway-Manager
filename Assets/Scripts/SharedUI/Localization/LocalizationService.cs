using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;
using RailwayManager.Core;
using RailwayManager.Core.Settings;

namespace RailwayManager.SharedUI.Localization
{
    /// <summary>
    /// Static service localization (M13-3).
    ///
    /// Resource files: <c>Resources/Locale/{lang}/strings.json</c> — hierarchiczny JSON
    /// (np. <c>{ "fleet": { "panel": { "title": "Mój tabor" } } }</c>) flattened do
    /// dotted keys (<c>fleet.panel.title</c>) przy load.
    ///
    /// API:
    /// <code>
    /// LocalizationService.Get("fleet.panel.title")           // → "Mój tabor"
    /// LocalizationService.Get("delays.format", trainNum, min) // → "Pociąg {0} opóźniony o {1} min"
    /// </code>
    ///
    /// Fallback chain:
    /// 1. Current locale strings
    /// 2. EN strings (jeśli current != EN)
    /// 3. <c>[key]</c> placeholder (debug)
    ///
    /// <see cref="LocalizedText"/> MonoBehaviour subskrybuje <see cref="OnLanguageChanged"/>
    /// i auto-rerenduje TMP text przy zmianie języka.
    ///
    /// Bootstrap przez <c>RuntimeInitializeOnLoadMethod</c> — wczytuje preference
    /// z <see cref="SettingsService"/> i ładuje strings.
    /// </summary>
    public static class LocalizationService
    {
        // ─── State ───────────────────────────────────────

        public static LocaleCode CurrentLocale { get; private set; } = LocaleCode.EN;

        /// <summary>Emitowane po <see cref="SetLanguage"/>.</summary>
        public static event Action OnLanguageChanged;

        private static Dictionary<string, string> _strings = new();

        // Fallback EN strings — załadowane raz, używane gdy current locale nie ma key'a.
        // Ładowane lazy przy pierwszym brakującym key.
        private static Dictionary<string, string> _fallbackEn;

        // ─── Bootstrap ───────────────────────────────────

        /// <summary>
        /// Auto-bootstrap przy starcie gry. Resolve preference + load strings
        /// zanim pierwsze UI zostanie wyrenderowane.
        ///
        /// <c>BeforeSceneLoad</c> — gwarantuje, że strings są załadowane przed <c>Awake()</c>
        /// wszystkich obiektów ze sceny startowej (MainMenu i sub-screens). Bez tego
        /// MainMenuUI.Awake woła Get(...) zanim Bootstrap załaduje strings → UI pokazuje
        /// placeholdery <c>[key]</c> zamiast tekstów (BUG-001 pre-fix).
        ///
        /// <see cref="SettingsService.EnsureExists"/> jest bezpieczne w BeforeSceneLoad —
        /// tworzy GameObject + AddComponent, nie wymaga załadowanej sceny.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            // Wymusza istnienie SettingsService (RuntimeInitializeOnLoadMethod kolejność
            // wykonania jest nieokreślona — explicit EnsureExists daje gwarancję).
            SettingsService.EnsureExists();

            var preference = SettingsService.Instance.Current.Language.Preference;
            var resolved = LocaleResolver.Resolve(preference);
            // emitEvent: true defense — gdyby Bootstrap z jakiegoś powodu wykonał się
            // PO Awake (edge case na niektórych platformach lub po hot-reload w Editorze),
            // UI dostanie event i się odświeży przez subscribe w Awake.
            SetLanguage(resolved, emitEvent: true);

            // Hot-reload przy zmianie preference w Settings UI
            SettingsService.OnSettingsChanged -= OnSettingsServiceChanged; // idempotent
            SettingsService.OnSettingsChanged += OnSettingsServiceChanged;

            Log.Info($"[LocalizationService] Bootstrapped with locale: {resolved}");
        }

        /// <summary>
        /// Subskrybent <see cref="SettingsService.OnSettingsChanged"/>. Re-resolve
        /// preference + SetLanguage (emituje <see cref="OnLanguageChanged"/> →
        /// <see cref="LocalizedText"/> komponenty rerenderują).
        /// </summary>
        private static void OnSettingsServiceChanged()
        {
            if (SettingsService.Instance == null) return;
            var newPref = SettingsService.Instance.Current.Language.Preference;
            var resolved = LocaleResolver.Resolve(newPref);
            if (resolved != CurrentLocale)
                SetLanguage(resolved);
        }

        // ─── Public API ──────────────────────────────────

        /// <summary>
        /// Zmienia aktywny język + ładuje strings + emituje event do subscribers.
        /// Idempotentne (no-op gdy locale already current).
        /// </summary>
        public static void SetLanguage(LocaleCode locale)
        {
            SetLanguage(locale, emitEvent: true);
        }

        /// <summary>Lookup string po dotted key. Fallback chain: current → EN → "[key]".</summary>
        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";

            // Lazy init defense — gdyby Bootstrap nie zdążył lub został pominięty
            // (np. w Editorze po hot-reload, w testach jednostkowych bez sceny), załaduj
            // strings synchronously przy pierwszym lookup. Idempotent — no-op gdy już load.
            if (_strings.Count == 0)
                EnsureLoaded();

            if (_strings.TryGetValue(key, out var value))
                return value;

            // Fallback: try EN
            if (CurrentLocale != LocaleCode.EN)
            {
                EnsureFallbackEnLoaded();
                if (_fallbackEn != null && _fallbackEn.TryGetValue(key, out var enValue))
                    return enValue;
            }

            // Final fallback — debug placeholder
            return $"[{key}]";
        }

        /// <summary>Lookup z <c>string.Format</c> (np. <c>Get("delays.format", trainNum, min)</c>).</summary>
        public static string Get(string key, params object[] args)
        {
            var template = Get(key);
            if (args == null || args.Length == 0) return template;

            try
            {
                return string.Format(template, args);
            }
            catch (FormatException e)
            {
                Log.Warn($"[LocalizationService] Format failed for key '{key}': {e.Message}");
                return template;
            }
        }

        /// <summary>True jeśli klucz istnieje w current locale (bez fallback).</summary>
        public static bool HasKey(string key) => _strings.ContainsKey(key);

        // ─── Internal: load + parse ──────────────────────

        private static void SetLanguage(LocaleCode locale, bool emitEvent)
        {
            if (locale == CurrentLocale && _strings.Count > 0) return;

            CurrentLocale = locale;
            _strings = LoadStrings(locale) ?? new Dictionary<string, string>();
            Log.Info($"[LocalizationService] SetLanguage: {locale} ({_strings.Count} keys)");

            if (emitEvent) OnLanguageChanged?.Invoke();
        }

        private static Dictionary<string, string> LoadStrings(LocaleCode locale)
        {
            string folder = LocaleResolver.ToFolderName(locale);
            string resourcePath = $"Locale/{folder}/strings";

            var ta = Resources.Load<TextAsset>(resourcePath);
            if (ta == null)
            {
                Log.Warn($"[LocalizationService] Resource not found: {resourcePath}.json — using empty");
                return new Dictionary<string, string>();
            }

            try
            {
                var jObj = JObject.Parse(ta.text);
                var dict = new Dictionary<string, string>();
                FlattenJObject(jObj, "", dict);
                return dict;
            }
            catch (Exception e)
            {
                Log.Error($"[LocalizationService] Failed to parse {resourcePath}.json: {e.Message}");
                return new Dictionary<string, string>();
            }
        }

        private static void EnsureFallbackEnLoaded()
        {
            if (_fallbackEn != null) return;
            _fallbackEn = LoadStrings(LocaleCode.EN);
        }

        /// <summary>
        /// Defense in depth — gwarantuje że <see cref="_strings"/> jest załadowane przed
        /// pierwszym <see cref="Get"/>, nawet gdy <see cref="Bootstrap"/> nie został wywołany
        /// (testy jednostkowe, hot-reload w Editorze).
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_strings.Count > 0) return;

            // Resolve preference jeśli SettingsService dostępny, inaczej fallback EN
            var locale = LocaleCode.EN;
            try
            {
                if (SettingsService.Instance != null)
                {
                    var preference = SettingsService.Instance.Current.Language.Preference;
                    locale = LocaleResolver.Resolve(preference);
                }
            }
            catch (Exception e)
            {
                Log.Warn($"[LocalizationService] EnsureLoaded fallback to EN: {e.Message}");
            }

            CurrentLocale = locale;
            _strings = LoadStrings(locale) ?? new Dictionary<string, string>();
            Log.Info($"[LocalizationService] EnsureLoaded: {locale} ({_strings.Count} keys)");
        }

        /// <summary>
        /// Rekurencyjne flatten hierarchical JSON do flat dict z dotted keys.
        /// <c>{"fleet":{"panel":{"title":"X"}}}</c> → <c>{"fleet.panel.title":"X"}</c>
        /// </summary>
        private static void FlattenJObject(JToken token, string prefix, Dictionary<string, string> dict)
        {
            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties())
                {
                    var nextPrefix = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    FlattenJObject(prop.Value, nextPrefix, dict);
                }
            }
            else if (token.Type == JTokenType.String)
            {
                dict[prefix] = token.ToString();
            }
            // Inne typy (number/bool/array) ignorujemy — strings only
        }
    }
}
