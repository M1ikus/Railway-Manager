using System;
using UnityEngine;

namespace RailwayManager.Core.Settings
{
    /// <summary>
    /// Static singleton serwis ustawień gracza (M13-1).
    ///
    /// Persystencja: PlayerPrefs per-field (klucz <c>Settings.&lt;Section&gt;.&lt;Field&gt;</c>).
    /// Powód per-field zamiast jednego JSON blob: łatwiejsza migracja pojedynczych kluczy
    /// między wersjami, możliwość czytania/zapisywania selektywnie.
    ///
    /// Working copy pattern (Apply/Cancel UX):
    /// <code>
    /// var working = SettingsService.Instance.GetWorkingCopy();  // clone Current
    /// working.Audio.MasterVolume = 0.5f;                         // user edits in UI
    /// SettingsService.Instance.Apply(working);                   // commit + persist + emit event
    /// // OR: discard working (user clicks Cancel)
    /// </code>
    ///
    /// Single source of truth dla MaxUndos zostaje w <see cref="UndoSettings"/> —
    /// SettingsService czyta/pisze przez UndoSettings property (sync proxy).
    /// </summary>
    public class SettingsService : MonoBehaviour
    {
        // ─── Singleton ───────────────────────────────────────────

        public static SettingsService Instance { get; private set; }

        /// <summary>Lazy bootstrap. Bezpieczne wielokrotne wywołanie.</summary>
        public static SettingsService EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("SettingsService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SettingsService>();
            return Instance;
        }

        // Auto-bootstrap przeniesiony do <see cref="RailwayManager.Core.Bootstrap"/>
        // (BeforeSceneLoad phase). Wcześniej tu siedział lokalny [RuntimeInitializeOnLoadMethod]
        // — od 2026-05-13 centralizowany. LocalizationService (SharedUI) i inne moduły
        // przed-scene-load mogą subscribe'ować się na Bootstrap.OnEarlyInit zamiast
        // utrzymywać własne attribute'y.

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            LoadFromPlayerPrefs();
            ApplyRuntimeEffects(); // od razu zaaplikuj rozdzielczość/VSync/quality
            Log.Info("[SettingsService] Initialized");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ─── State ───────────────────────────────────────────────

        /// <summary>Aktualne (zapisane) ustawienia. Nie modyfikuj bezpośrednio — użyj <see cref="GetWorkingCopy"/> + <see cref="Apply"/>.</summary>
        public SettingsData Current { get; private set; } = new();

        /// <summary>Emitowane po każdym <see cref="Apply"/> / <see cref="ResetToDefaults"/> / <see cref="ResetSection"/>.</summary>
        public static event Action OnSettingsChanged;

        // ─── Public API ──────────────────────────────────────────

        /// <summary>Zwraca deep clone <see cref="Current"/> — bezpieczne do modyfikacji w UI bez wpływu na persystowane.</summary>
        public SettingsData GetWorkingCopy() => Current.Clone();

        /// <summary>Commit working copy → Current + zapis PlayerPrefs + emit event.</summary>
        public void Apply(SettingsData working)
        {
            if (working == null) { Log.Warn("[SettingsService] Apply(null) ignored"); return; }
            Current = working.Clone(); // własna kopia, żeby caller nie zmieniał state'u przez ref
            SaveToPlayerPrefs();
            ApplyRuntimeEffects();
            OnSettingsChanged?.Invoke();
            Log.Info("[SettingsService] Settings applied");
        }

        /// <summary>Reset wszystkich sekcji do default + zapis + emit event.</summary>
        public void ResetToDefaults()
        {
            Current = new SettingsData();
            SaveToPlayerPrefs();
            ApplyRuntimeEffects();
            OnSettingsChanged?.Invoke();
            Log.Info("[SettingsService] Reset to defaults (all sections)");
        }

        /// <summary>Reset jednej sekcji do default. Pozostałe sekcje bez zmian.</summary>
        public void ResetSection(SettingsSection section)
        {
            switch (section)
            {
                case SettingsSection.Control:  Current.Control  = new ControlSettings();  break;
                case SettingsSection.Graphics: Current.Graphics = new GraphicsSettings(); break;
                case SettingsSection.Audio:    Current.Audio    = new AudioSettings();    break;
                case SettingsSection.Language: Current.Language = new LanguageSettings(); break;
                case SettingsSection.General:  Current.General  = new GeneralSettings();  break;
            }
            SaveToPlayerPrefs();
            ApplyRuntimeEffects();
            OnSettingsChanged?.Invoke();
            Log.Info($"[SettingsService] Reset section: {section}");
        }

        // ─── Persystencja PlayerPrefs ────────────────────────────

        private const string K = "Settings.";

        // ─── PlayerPrefs helper (boilerplate elimination) ────────────────
        //
        // Bool/Enum unboxing było zduplikowane w Load i Save (60+ lat call'i razem).
        // Te helpery ukrywają `? 1 : 0` i `(int)(object)enum` boxing — error-prone
        // przy ręcznym powtarzaniu (literówka `0` zamiast `1` jako default = inverted
        // boolean). Generic Enum<T> jest cheap przy first call, JIT inline'uje.

        private static bool GetBool(string key, bool def) => PlayerPrefs.GetInt(key, def ? 1 : 0) == 1;
        private static void SetBool(string key, bool val) => PlayerPrefs.SetInt(key, val ? 1 : 0);

        private static T GetEnum<T>(string key, T def) where T : struct, System.Enum
            => (T)(object)PlayerPrefs.GetInt(key, (int)(object)def);
        private static void SetEnum<T>(string key, T val) where T : struct, System.Enum
            => PlayerPrefs.SetInt(key, (int)(object)val);

        // ─── Load / Save ─────────────────────────────────────────────────

        private void LoadFromPlayerPrefs()
        {
            var c = Current;

            // Control
            c.Control.MouseSensitivity   = PlayerPrefs.GetFloat(K + "Control.MouseSensitivity", 1f);
            c.Control.ScrollSensitivity  = PlayerPrefs.GetFloat(K + "Control.ScrollSensitivity", 1f);
            c.Control.PanSpeed           = PlayerPrefs.GetFloat(K + "Control.PanSpeed", 1f);
            c.Control.InvertCameraScroll = GetBool(K + "Control.InvertCameraScroll", false);
            c.Control.InvertMouseY       = GetBool(K + "Control.InvertMouseY", false);

            // Graphics (FpsLimit przeniesione do General.Gameplay per D34)
            c.Graphics.ResolutionWidth  = PlayerPrefs.GetInt(K + "Graphics.ResolutionWidth",  Screen.currentResolution.width);
            c.Graphics.ResolutionHeight = PlayerPrefs.GetInt(K + "Graphics.ResolutionHeight", Screen.currentResolution.height);
            c.Graphics.RefreshRate      = PlayerPrefs.GetInt(K + "Graphics.RefreshRate", 60);
            c.Graphics.Window           = GetEnum(K + "Graphics.Window",       WindowMode.Borderless);
            c.Graphics.VSync            = GetBool(K + "Graphics.VSync",        true);
            c.Graphics.Quality          = GetEnum(K + "Graphics.Quality",      QualityPreset.High);
            c.Graphics.Shadows          = GetEnum(K + "Graphics.Shadows",      ShadowLevel.High);
            c.Graphics.Textures         = GetEnum(K + "Graphics.Textures",     TextureLevel.High);
            c.Graphics.LodBias          = PlayerPrefs.GetFloat(K + "Graphics.LodBias", 1f);
            c.Graphics.AntiAliasing     = GetEnum(K + "Graphics.AntiAliasing", AntiAliasing.SMAA);
            c.Graphics.PostProcessing   = GetBool(K + "Graphics.PostProcessing", true);

            // Audio
            c.Audio.MasterVolume      = PlayerPrefs.GetFloat(K + "Audio.MasterVolume", 1f);
            c.Audio.MusicVolume       = PlayerPrefs.GetFloat(K + "Audio.MusicVolume", 0.8f);
            c.Audio.SfxVolume         = PlayerPrefs.GetFloat(K + "Audio.SfxVolume", 1f);
            c.Audio.VoiceVolume       = PlayerPrefs.GetFloat(K + "Audio.VoiceVolume", 1f);
            c.Audio.MuteWhenUnfocused = GetBool(K + "Audio.MuteWhenUnfocused", true);

            // Language (osobna sekcja per D34)
            c.Language.Preference = GetEnum(K + "Language.Preference", LanguagePreference.Auto);
            c.Language.DisplayCurrency = GetEnum(K + "Language.DisplayCurrency", Currency.PLN);

            // General > Gameplay (FpsLimit dorzucone per D34)
            var g = c.General.Gameplay;
            g.DefaultTimeSpeed              = GetEnum(K + "General.Gameplay.DefaultTimeSpeed",     TimeSpeed.X1);
            g.FpsLimit                      = PlayerPrefs.GetInt(K + "General.Gameplay.FpsLimit", 0);
            g.AutoPauseOnBreakdown          = GetBool(K + "General.Gameplay.AutoPauseOnBreakdown",        false);
            g.AutoPauseOnInfrastructure     = GetBool(K + "General.Gameplay.AutoPauseOnInfrastructure",   true);
            g.AutoPauseOnCriticalDecision   = GetBool(K + "General.Gameplay.AutoPauseOnCriticalDecision", true);
            g.AutoPauseOnCollision          = GetBool(K + "General.Gameplay.AutoPauseOnCollision",        true);
            g.AutoPauseOnStrike             = GetBool(K + "General.Gameplay.AutoPauseOnStrike",           false);
            g.ShowTutorialOnNewGame         = GetBool(K + "General.Gameplay.ShowTutorialOnNewGame",       true);
            g.RandomEventFrequency          = GetEnum(K + "General.Gameplay.RandomEventFrequency", EventFrequency.Normal);
            g.NotifyDelays                  = GetBool(K + "General.Gameplay.NotifyDelays",    true);
            g.NotifyMoney                   = GetBool(K + "General.Gameplay.NotifyMoney",     true);
            g.NotifyPersonnel               = GetBool(K + "General.Gameplay.NotifyPersonnel", true);
            g.NotifyOther                   = GetBool(K + "General.Gameplay.NotifyOther",     true);
            g.MaxUndos                      = UndoSettings.MaxUndos; // single source of truth = UndoSettings

            // General > Interface
            var i = c.General.Interface;
            i.UiScale                = PlayerPrefs.GetFloat(K + "General.Interface.UiScale", 1f);
            i.TooltipsDelaySeconds   = PlayerPrefs.GetFloat(K + "General.Interface.TooltipsDelaySeconds", 0.5f);
            i.ShowKeybindsInTooltips = GetBool(K + "General.Interface.ShowKeybindsInTooltips", true);
            i.ColorBlindMode         = GetEnum(K + "General.Interface.ColorBlindMode", ColorBlindMode.None);

            Log.Info("[SettingsService] Loaded from PlayerPrefs");
        }

        private void SaveToPlayerPrefs()
        {
            var c = Current;

            // Control
            PlayerPrefs.SetFloat(K + "Control.MouseSensitivity",  c.Control.MouseSensitivity);
            PlayerPrefs.SetFloat(K + "Control.ScrollSensitivity", c.Control.ScrollSensitivity);
            PlayerPrefs.SetFloat(K + "Control.PanSpeed",          c.Control.PanSpeed);
            SetBool(K + "Control.InvertCameraScroll", c.Control.InvertCameraScroll);
            SetBool(K + "Control.InvertMouseY",       c.Control.InvertMouseY);

            // Graphics
            PlayerPrefs.SetInt  (K + "Graphics.ResolutionWidth",  c.Graphics.ResolutionWidth);
            PlayerPrefs.SetInt  (K + "Graphics.ResolutionHeight", c.Graphics.ResolutionHeight);
            PlayerPrefs.SetInt  (K + "Graphics.RefreshRate",      c.Graphics.RefreshRate);
            SetEnum (K + "Graphics.Window",       c.Graphics.Window);
            SetBool (K + "Graphics.VSync",        c.Graphics.VSync);
            SetEnum (K + "Graphics.Quality",      c.Graphics.Quality);
            SetEnum (K + "Graphics.Shadows",      c.Graphics.Shadows);
            SetEnum (K + "Graphics.Textures",     c.Graphics.Textures);
            PlayerPrefs.SetFloat(K + "Graphics.LodBias", c.Graphics.LodBias);
            SetEnum (K + "Graphics.AntiAliasing", c.Graphics.AntiAliasing);
            SetBool (K + "Graphics.PostProcessing", c.Graphics.PostProcessing);

            // Audio
            PlayerPrefs.SetFloat(K + "Audio.MasterVolume", c.Audio.MasterVolume);
            PlayerPrefs.SetFloat(K + "Audio.MusicVolume",  c.Audio.MusicVolume);
            PlayerPrefs.SetFloat(K + "Audio.SfxVolume",    c.Audio.SfxVolume);
            PlayerPrefs.SetFloat(K + "Audio.VoiceVolume",  c.Audio.VoiceVolume);
            SetBool(K + "Audio.MuteWhenUnfocused", c.Audio.MuteWhenUnfocused);

            // Language
            SetEnum(K + "Language.Preference", c.Language.Preference);
            SetEnum(K + "Language.DisplayCurrency", c.Language.DisplayCurrency);

            // General > Gameplay
            var g = c.General.Gameplay;
            SetEnum(K + "General.Gameplay.DefaultTimeSpeed",        g.DefaultTimeSpeed);
            PlayerPrefs.SetInt(K + "General.Gameplay.FpsLimit",     g.FpsLimit);
            SetBool(K + "General.Gameplay.AutoPauseOnBreakdown",        g.AutoPauseOnBreakdown);
            SetBool(K + "General.Gameplay.AutoPauseOnInfrastructure",   g.AutoPauseOnInfrastructure);
            SetBool(K + "General.Gameplay.AutoPauseOnCriticalDecision", g.AutoPauseOnCriticalDecision);
            SetBool(K + "General.Gameplay.AutoPauseOnCollision",        g.AutoPauseOnCollision);
            SetBool(K + "General.Gameplay.AutoPauseOnStrike",           g.AutoPauseOnStrike);
            SetBool(K + "General.Gameplay.ShowTutorialOnNewGame",       g.ShowTutorialOnNewGame);
            SetEnum(K + "General.Gameplay.RandomEventFrequency",        g.RandomEventFrequency);
            SetBool(K + "General.Gameplay.NotifyDelays",    g.NotifyDelays);
            SetBool(K + "General.Gameplay.NotifyMoney",     g.NotifyMoney);
            SetBool(K + "General.Gameplay.NotifyPersonnel", g.NotifyPersonnel);
            SetBool(K + "General.Gameplay.NotifyOther",     g.NotifyOther);
            UndoSettings.MaxUndos = g.MaxUndos; // write through

            // General > Interface
            var i = c.General.Interface;
            PlayerPrefs.SetFloat(K + "General.Interface.UiScale",              i.UiScale);
            PlayerPrefs.SetFloat(K + "General.Interface.TooltipsDelaySeconds", i.TooltipsDelaySeconds);
            SetBool(K + "General.Interface.ShowKeybindsInTooltips", i.ShowKeybindsInTooltips);
            SetEnum(K + "General.Interface.ColorBlindMode",         i.ColorBlindMode);

            PlayerPrefs.Save();
        }

        // ─── Runtime effects (Apply'em hardware-related changes natychmiast) ─────

        /// <summary>
        /// Aplikuje ustawienia które wpływają na runtime hardware (rozdzielczość, VSync, FPS, quality).
        /// Wywoływane po Apply / Reset.
        ///
        /// AudioMixer placeholder — faktyczny hook do mikserów dźwięku w M12b. Pole jest
        /// odczytywane przez UI ale nie wpływa na rzeczywisty volume w M13-1.
        /// </summary>
        private void ApplyRuntimeEffects()
        {
            // Resolution + window mode
            var fsMode = Current.Graphics.Window switch
            {
                WindowMode.Fullscreen => FullScreenMode.ExclusiveFullScreen,
                WindowMode.Borderless => FullScreenMode.FullScreenWindow,
                WindowMode.Windowed   => FullScreenMode.Windowed,
                _                     => FullScreenMode.FullScreenWindow
            };

            // Refresh rate w Unity 6 = RefreshRate struct (rational numerator/denominator,
            // np. 59.94 Hz = 60000/1001). Dla EA trzymamy int Hz w SettingsData i konwertujemy
            // przy SetResolution (numerator = Hz, denominator = 1).
            // Jeśli rozdzielczość różni się od aktualnej → SetResolution. Inaczej skip (uniknij flash).
            if (Screen.width != Current.Graphics.ResolutionWidth ||
                Screen.height != Current.Graphics.ResolutionHeight ||
                Screen.fullScreenMode != fsMode)
            {
                var refreshRate = new RefreshRate
                {
                    numerator   = (uint)Mathf.Max(1, Current.Graphics.RefreshRate),
                    denominator = 1
                };
                Screen.SetResolution(
                    Current.Graphics.ResolutionWidth,
                    Current.Graphics.ResolutionHeight,
                    fsMode,
                    refreshRate);
            }

            // VSync
            QualitySettings.vSyncCount = Current.Graphics.VSync ? 1 : 0;

            // FPS limit (0 = unlimited) — czytane z General.Gameplay per D34
            Application.targetFrameRate = Current.General.Gameplay.FpsLimit > 0 ? Current.General.Gameplay.FpsLimit : -1;

            // Quality preset (mapuje na QualitySettings level — Unity ma 6 levelów, nasz preset 4)
            int qualityLevel = Current.Graphics.Quality switch
            {
                QualityPreset.Low    => 0,
                QualityPreset.Medium => 2,
                QualityPreset.High   => 4,
                QualityPreset.Ultra  => 5,
                _                    => 4
            };
            QualitySettings.SetQualityLevel(qualityLevel, applyExpensiveChanges: false);

            // LOD bias
            QualitySettings.lodBias = Current.Graphics.LodBias;

            // M-Economy Faza 2: waluta wyświetlania → CurrencyService (NumberFormatService czyta stamtąd).
            CurrencyService.DisplayCurrency = Current.Language.DisplayCurrency;

            // Shadow / Texture / AA / PostProcessing — pozostawione na M12a Performance i M12b
            // (wymaga URP volume bind + shadow distance tuning per quality level).
        }
    }
}
