using System;
using UnityEngine;

namespace RailwayManager.Core.Settings
{
    // ═══════════════════════════════════════════════════════════════════
    //  ROOT
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Root POCO ustawień gracza — 5 sekcji (Sterowanie / Grafika / Dźwięk / Język / Ogólne) per D34.
    /// Persystencja per-field przez PlayerPrefs (klucz <c>Settings.&lt;Section&gt;.&lt;Field&gt;</c>) —
    /// patrz <see cref="SettingsService"/>.
    ///
    /// Working copy pattern: UI clonuje przez <see cref="Clone"/>, modyfikuje, na Apply
    /// przekazuje do <see cref="SettingsService.Apply"/>.
    /// </summary>
    [Serializable]
    public class SettingsData
    {
        public ControlSettings  Control  = new();
        public GraphicsSettings Graphics = new();
        public AudioSettings    Audio    = new();
        public LanguageSettings Language = new();
        public GeneralSettings  General  = new();

        /// <summary>Deep clone via JsonUtility — bezpieczne dla working copy pattern.</summary>
        public SettingsData Clone()
        {
            var json = JsonUtility.ToJson(this);
            return JsonUtility.FromJson<SettingsData>(json);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SECTION POCOs
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Sterowanie (Controls / Rebinding). Bindings overrides: osobny blob (M13-2).</summary>
    [Serializable]
    public class ControlSettings
    {
        public float MouseSensitivity   = 1f;     // 0.1 - 3.0
        public float ScrollSensitivity  = 1f;     // 0.1 - 3.0  (camera zoom / map scroll)
        public float PanSpeed           = 1f;     // 0.1 - 3.0  (kamera Depot/Map)
        public bool  InvertCameraScroll = false;  // odwrócenie scroll kamery (Depot orbit)
        public bool  InvertMouseY       = false;  // inwersja osi Y (Depot orbit pitch)
    }

    /// <summary>Grafika (Display / Quality). FPS limit przeniesione do <see cref="GameplaySection"/> per D34.</summary>
    [Serializable]
    public class GraphicsSettings
    {
        public int           ResolutionWidth  = 1920;
        public int           ResolutionHeight = 1080;
        public int           RefreshRate      = 60;
        public WindowMode    Window           = WindowMode.Borderless;
        public bool          VSync            = true;
        public QualityPreset Quality          = QualityPreset.High;
        public ShadowLevel   Shadows          = ShadowLevel.High;
        public TextureLevel  Textures         = TextureLevel.High;
        public float         LodBias          = 1.0f;          // 0.5 - 2.0
        public AntiAliasing  AntiAliasing     = AntiAliasing.SMAA;
        public bool          PostProcessing   = true;          // bloom + URP volume override
    }

    /// <summary>Dźwięk. AudioMixer hook → M12b (placeholder w M13-1).</summary>
    [Serializable]
    public class AudioSettings
    {
        public float MasterVolume      = 1.0f;  // 0 - 1
        public float MusicVolume       = 0.8f;
        public float SfxVolume         = 1.0f;
        public float VoiceVolume       = 1.0f;  // placeholder, voice/announcements w M12b
        public bool  MuteWhenUnfocused = true;  // Application.isFocused == false → mute
    }

    /// <summary>Język — osobna zakładka per D34 (wydzielone z Ogólne 2026-04-26). Pełen i18n framework w M13-3.</summary>
    [Serializable]
    public class LanguageSettings
    {
        /// <summary>
        /// Wybór języka. <see cref="LanguagePreference.Auto"/> = autodetect przez
        /// SteamApps.GetCurrentGameLanguage() → CultureInfo.CurrentCulture fallback (M13-3).
        /// Switch języka działa (M13-3): zmiana Preference przeładowuje UI przez LocalizationService.
        /// </summary>
        public LanguagePreference Preference = LanguagePreference.Auto;

        /// <summary>M-Economy Faza 2: waluta WYŚWIETLANIA kwot (PLN/EUR/USD/CZK). Baza gry zawsze PLN —
        /// to tylko prezentacja (konwersja kursem w <see cref="RailwayManager.Core.CurrencyService"/>).
        /// Default PLN. Synchronizowane do CurrencyService przy load/Apply.</summary>
        public Currency DisplayCurrency = Currency.PLN;
    }

    /// <summary>Ogólne — kontener dwóch podsekcji (Rozgrywka / Interfejs). Język wydzielony jako osobna zakładka per D34.</summary>
    [Serializable]
    public class GeneralSettings
    {
        public GameplaySection  Gameplay  = new();
        public InterfaceSection Interface = new();
    }

    /// <summary>Rozgrywka — pod-sekcja Ogólne. Trudność NIE jest tu (D33 — per-save w GameCreator). FPS limit tu (przeniesione z Grafiki per D34).</summary>
    [Serializable]
    public class GameplaySection
    {
        public TimeSpeed DefaultTimeSpeed = TimeSpeed.X1;
        public int FpsLimit = 0;  // 0 = Unlimited (przeniesione z GraphicsSettings per D34)

        // Auto-pauza per kategoria zdarzenia
        public bool AutoPauseOnBreakdown      = false;
        public bool AutoPauseOnInfrastructure = true;
        public bool AutoPauseOnCriticalDecision = true;
        public bool AutoPauseOnCollision      = true;
        public bool AutoPauseOnStrike         = false; // post-1.0, placeholder

        public bool ShowTutorialOnNewGame = true;
        public EventFrequency RandomEventFrequency = EventFrequency.Normal;

        // Powiadomienia
        public bool NotifyDelays    = true;
        public bool NotifyMoney     = true;
        public bool NotifyPersonnel = true;
        public bool NotifyOther     = true;

        /// <summary>Mirror dla <see cref="UndoSettings.MaxUndos"/> — single source of truth zostaje w UndoSettings.</summary>
        public int MaxUndos = UndoSettings.DEFAULT;
    }

    /// <summary>Interfejs — pod-sekcja Ogólne. UI scale 100% only na EA (D11).</summary>
    [Serializable]
    public class InterfaceSection
    {
        public float UiScale              = 1.0f;  // EA: 100% only (info "więcej opcji wkrótce")
        public float TooltipsDelaySeconds = 0.5f;  // 0 - 2.0
        public bool  ShowKeybindsInTooltips = true;
        public ColorBlindMode ColorBlindMode = ColorBlindMode.None; // placeholder na EA
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ENUMS
    // ═══════════════════════════════════════════════════════════════════

    public enum WindowMode
    {
        Fullscreen,
        Borderless,
        Windowed
    }

    public enum QualityPreset
    {
        Low,
        Medium,
        High,
        Ultra
    }

    public enum ShadowLevel
    {
        Off,
        Low,
        Medium,
        High
    }

    public enum TextureLevel
    {
        Low,
        Medium,
        High,
        Ultra
    }

    public enum AntiAliasing
    {
        Off,
        FXAA,
        SMAA,
        TAA
    }

    public enum TimeSpeed
    {
        X1,
        X5,
        X25,
        X150,
        X500
    }

    public enum EventFrequency
    {
        Off,
        Rare,
        Normal,
        Often
    }

    /// <summary>
    /// Wybór języka gracza. <see cref="Auto"/> = autodetect przy pierwszym uruchomieniu.
    /// 7 języków (5 z contentem na EA: PL/EN/DE/CZ/JP; 2 puste fallback: RU/UK).
    /// </summary>
    public enum LanguagePreference
    {
        Auto,
        PL,
        EN,
        DE,
        CZ,
        JP,
        RU,
        UK
    }

    public enum ColorBlindMode
    {
        None,
        Protanopia,
        Deuteranopia,
        Tritanopia
    }

    /// <summary>Identyfikator sekcji dla per-section reset / per-section save.</summary>
    public enum SettingsSection
    {
        Control,
        Graphics,
        Audio,
        Language,
        General
    }
}
