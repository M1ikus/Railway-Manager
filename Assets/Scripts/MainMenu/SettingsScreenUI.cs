using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using RailwayManager.Core;
using RailwayManager.Core.Settings;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace MainMenu
{
    /// <summary>
    /// Pełnoekranowy panel "Ustawienia" (M13-1).
    /// 5 zakładek: Sterowanie / Grafika / Dźwięk / Język / Ogólne (per D34).
    /// "Ogólne" zawiera 2 podsekcje: Rozgrywka / Interfejs (Język wydzielony jako osobna zakładka).
    ///
    /// Working copy pattern: w <see cref="Show"/> klonuje <see cref="SettingsService.Current"/>
    /// do <see cref="_working"/>, kontrolki modyfikują _working, na Apply commitsuje przez
    /// <see cref="SettingsService.Apply"/>.
    ///
    /// Klasa rozbita na 5 partials dla utrzymania (konwencja FleetPanelUI z M3):
    /// - <c>SettingsScreenUI.cs</c> (this) — base: state, references, Build entry, Show/Hide,
    ///   Apply/Cancel/Reset handlers, RefreshLanguage, root/topbar/content area, primitives
    /// - <c>SettingsScreenUI.Sidebar.cs</c> — BuildSidebar, ApplySidebarState
    /// - <c>SettingsScreenUI.BottomBar.cs</c> — BuildBottomBar, AddBottomBarButton
    /// - <c>SettingsScreenUI.Sections.cs</c> — PopulateXxx (5 sekcji) + FpsToIndex/IndexToFps
    /// - <c>SettingsScreenUI.RowBuilders.cs</c> — Slider/Toggle/Dropdown/Heading/Info row helpers
    ///
    /// Rebindowanie klawiszy → M13-2 (placeholder w zakładce Sterowanie).
    /// Lista języków → M13-3 (na razie persystencja preference, faktyczny switch później).
    /// AudioMixer hook → M12b (na razie tylko persystencja sliderów).
    /// </summary>
    public partial class SettingsScreenUI : MonoBehaviour, IMenuScreen
    {
        // ─── layout constants ──────────────────────────
        private const float SidebarWidth    = 240f;
        private const float TopBarHeight    = 70f;
        private const float BottomBarHeight = 70f;

        // ─── colours ───────────────────────────────────
        private static readonly Color BottomBarBg = UITheme.OverlayPanelStrong;
        private static readonly Color SidebarBg   = UITheme.OverlayPanel;
        private static readonly Color BtnNormal   = UITheme.WithAlpha(UITheme.SecondarySurface, 0.85f);
        private static readonly Color BtnActive   = UITheme.PrimarySurface;
        private static readonly Color BtnAction   = UITheme.SecondarySurface;
        private static readonly Color BtnPrimary  = UITheme.PrimaryAccent;
        private static readonly Color BtnDanger   = UITheme.WithAlpha(UITheme.Danger, 0.9f);
        private static readonly Color RowBg       = UITheme.OverlayPanel;
        private static readonly Color HeadingBg   = UITheme.OverlayPanelStrong;
        private static readonly Color SliderBg    = UITheme.SecondarySurface;
        private static readonly Color DropdownBg  = UITheme.SecondarySurface;
        private static readonly Color ToggleBg    = UITheme.SecondarySurface;
        private static readonly Color TextPrimary = UITheme.PrimaryText;
        private static readonly Color TextMuted   = UITheme.SecondaryText;
        private static readonly Color Accent      = UITheme.PrimaryAccent;

        // ─── section i18n keys (5 zakładek per D34) ────
        private const int SectionCount = 5;
        private readonly string[] _secKeys =
        {
            "settings.tabs.controls",
            "settings.tabs.graphics",
            "settings.tabs.audio",
            "settings.tabs.language",
            "settings.tabs.general"
        };

        // ─── state ─────────────────────────────────────
        private SettingsData _working;       // working copy modyfikowana przez UI
        private int _activeSection;

        // ─── root references ────────────────────────────
        private GameObject      _root;
        private Transform       _contentParent;
        private RectTransform   _contentRT;
        private TextMeshProUGUI _titleLbl;
        private TextMeshProUGUI _backLbl;

        // ─── sidebar ────────────────────────────────────
        private readonly TextMeshProUGUI[] _secLbls    = new TextMeshProUGUI[SectionCount];
        private readonly Image[]           _secBgs     = new Image[SectionCount];
        private readonly Image[]           _secAccents = new Image[SectionCount];

        // ─── bottom bar labels ──────────────────────────
        private TextMeshProUGUI _resetSectionLbl;
        private TextMeshProUGUI _resetAllLbl;
        private TextMeshProUGUI _cancelLbl;
        private TextMeshProUGUI _applyLbl;

        // ─── dynamic row labels (per current section, used by RefreshRowLabels) ───
        // Tuple (TMP label, i18n key) — key resolved przez LocalizationService.Get
        private readonly List<(TextMeshProUGUI lbl, string i18nKey)> _activeLabels = new();

        // ─── dynamic dropdowns (per current section, used by RefreshDropdownOptions) ───
        // Trzymane osobno bo opcje wymagają full rebuild (TMP_Dropdown.options[]) przy zmianie języka.
        // Slidery i toggles tylko zmieniają labele (przez _activeLabels), nie wymagają rebuilda.
        private readonly List<(TMP_Dropdown dd, string[] optionKeys)> _activeDropdowns = new();

        // ─── rebinding (zakładka Sterowanie) ────────────
        // Lokalna instancja InputActions używana wyłącznie do wyświetlania bindings
        // i wykonywania rebindów. Apply przez RebindingService.SaveOverrides → JSON
        // do PlayerPrefs. Wszystkie inne callsite'y (CameraController, DepotOrbitCamera, etc.)
        // dostają nowe bindings przy najbliższym new InputActions() — info "wymaga restartu sceny".
        private InputActions _rebindActions;
        private RebindModalUI _rebindModal;
        private string _rebindingsSnapshotJson;
        private bool _rebindingsDirty;

        public System.Action OnBack;

        // ═══════════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ═══════════════════════════════════════════════════════════════════

        public void Build(Transform canvasTransform)
        {
            BuildRoot(canvasTransform);
            BuildSharedTopBar();
            BuildSidebar();    // partial: Sidebar.cs
            BuildContentArea();
            BuildBottomBar();  // partial: BottomBar.cs

            // Rebind modal (osobny GO jako sibling SettingsScreen na canvasie)
            var modalGO = new GameObject("RebindModalUI");
            modalGO.transform.SetParent(canvasTransform, false);
            _rebindModal = modalGO.AddComponent<RebindModalUI>();
            _rebindModal.Build(canvasTransform);

            _root.SetActive(false);
        }

        public void Show()
        {
            _root.SetActive(true);
            _working = SettingsService.EnsureExists().GetWorkingCopy();

            // Lazy-init rebind InputActions instance (osobna od pozostałych callsite'ów)
            if (_rebindActions == null)
            {
                _rebindActions = new InputActions();
                RebindingService.ApplyOverridesTo(_rebindActions);
            }
            CaptureRebindingSnapshot();

            _activeSection = 0;
            ApplySidebarState();
            PopulateSection(0);
            RefreshLanguage();
        }

        public void Hide()
        {
            _rebindModal?.CancelActiveOperation();
            RestorePendingRebindingsIfNeeded();
            _root.SetActive(false);
        }
        public bool IsVisible => _root != null && _root.activeSelf;

        void OnDestroy()
        {
            RestorePendingRebindingsIfNeeded();
            _rebindActions?.Dispose();
            _rebindActions = null;
        }

        public void RefreshLanguage()
        {
            if (_titleLbl != null) _titleLbl.text = LocalizationService.Get("settings.title");
            if (_backLbl != null)  _backLbl.text  = "←";

            for (int i = 0; i < SectionCount; i++)
                if (_secLbls[i] != null)
                    _secLbls[i].text = LocalizationService.Get(_secKeys[i]);

            // Bottom bar labels
            if (_resetSectionLbl != null) _resetSectionLbl.text = LocalizationService.Get("settings.bottom.reset_section");
            if (_resetAllLbl     != null) _resetAllLbl.text     = LocalizationService.Get("settings.bottom.reset_all");
            if (_cancelLbl       != null) _cancelLbl.text       = LocalizationService.Get("settings.bottom.cancel");
            if (_applyLbl        != null) _applyLbl.text        = LocalizationService.Get("settings.bottom.apply");

            RefreshRowLabels();
        }

        // === i18n hot-reload (M13-4e) ===
        // SettingsScreenUI używa już RefreshLanguage callbacka z MainMenuUI.OnLocaleChanged
        // (legacy ścieżka dla MenuLanguage). Plus własna subskrypcja na LocalizationService —
        // w przypadku zmiany języka gdy SettingsScreen jest aktywny, repopulate sekcji żeby
        // dropdown options też się odświeżyły (Slider/Toggle labels obsłużone przez
        // _activeLabels iteration; dropdowns wymagają full repopulate).

        void OnEnable()
        {
            LocalizationService.OnLanguageChanged += OnLocaleChanged;
        }

        void OnDisable()
        {
            LocalizationService.OnLanguageChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged()
        {
            // Optymalizacja vs poprzednie pełne PopulateSection: dropdowns dostają tylko
            // refresh options in-place (TMP_Dropdown.ClearOptions/AddOptions zachowuje value),
            // a slider/toggle/rebind rows aktualizują się przez _activeLabels (RefreshRowLabels).
            // Wcześniej sekcja Sterowanie z 30+ wierszami była rebuildowana przy każdym switch'u PL↔EN.
            if (IsVisible)
                RefreshDropdownOptions();
            RefreshLanguage();
        }

        /// <summary>
        /// Aktualizuje opcje wszystkich aktywnych dropdownów do bieżącego locale.
        /// Zachowuje wybór (SetValueWithoutNotify) — sam tekst opcji się zmienia.
        /// </summary>
        private void RefreshDropdownOptions()
        {
            foreach (var (dd, optionKeys) in _activeDropdowns)
            {
                if (dd == null || optionKeys == null) continue;

                int currentValue = dd.value;
                dd.ClearOptions();
                var opts = new List<TMP_Dropdown.OptionData>(optionKeys.Length);
                foreach (var optKey in optionKeys)
                    opts.Add(new TMP_Dropdown.OptionData(LocalizationService.Get(optKey)));
                dd.AddOptions(opts);
                dd.SetValueWithoutNotify(Mathf.Clamp(currentValue, 0, optionKeys.Length - 1));
                dd.RefreshShownValue();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  ROOT / TOP BAR / CONTENT AREA
        // ═══════════════════════════════════════════════════════════════════

        private void BuildRoot(Transform parent)
        {
            _root = MenuScreenPrimitives.CreateFullscreenRoot("SettingsScreen", parent);
        }

        private void BuildSharedTopBar()
        {
            MenuScreenPrimitives.CreateTopBar("TopBar", _root.transform, OnCancelClicked, out _backLbl, out _titleLbl);
        }

        private void BuildContentArea()
        {
            // BUG-015: migracja na MenuScreenPrimitives.BuildVerticalScrollArea —
            // helper dodaje Scrollbar Vertical AutoHideAndExpandViewport (poprzednia
            // wersja miała OK hierarchię ale brak suwaka po prawej).
            MenuScreenPrimitives.BuildVerticalScrollArea(
                _root.transform,
                offsetMin: new Vector2(SidebarWidth, BottomBarHeight),
                offsetMax: new Vector2(0f, -TopBarHeight),
                contentPadding: new RectOffset(60, 60, 30, 30),
                contentSpacing: UITheme.Spacing.Sm,
                contentAlignment: TextAnchor.UpperCenter,
                out _contentRT);
            _contentParent = _contentRT.transform;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CONTENT POPULATE (delegates to partial Sections.cs)
        // ═══════════════════════════════════════════════════════════════════

        private void PopulateSection(int idx)
        {
            // Clear existing rows
            foreach (Transform ch in _contentParent)
                Destroy(ch.gameObject);
            _activeLabels.Clear();
            _activeDropdowns.Clear();

            switch (idx)
            {
                case 0: PopulateControl();   break;  // partial Sections.cs
                case 1: PopulateGraphics();  break;
                case 2: PopulateAudio();     break;
                case 3: PopulateLanguage();  break;
                case 4: PopulateGeneral();   break;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRT);
        }

        /// <summary>
        /// Aktualizuje labele kontrolek wg current locale. Każda row builder rejestruje
        /// (label, i18n key) w <see cref="_activeLabels"/> przy tworzeniu.
        /// </summary>
        private void RefreshRowLabels()
        {
            foreach (var entry in _activeLabels)
                if (entry.lbl != null)
                    entry.lbl.text = LocalizationService.Get(entry.i18nKey);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  APPLY / CANCEL / RESET HANDLERS
        // ═══════════════════════════════════════════════════════════════════

        private void OnApplyClicked()
        {
            SettingsService.Instance.Apply(_working);
            CaptureRebindingSnapshot();
            Log.Info("[SettingsScreenUI] Apply clicked");
            OnBack?.Invoke();
        }

        private void OnCancelClicked()
        {
            // Cancel = discard working copy + ewentualny revert preview-applied rebindings.
            // Audio/video/locale NIE wymagają tu revertu bo SettingsService.Apply jest wołane
            // wyłącznie z OnApplyClicked — kontrolki modyfikują tylko lokalny _working dopóki
            // user nie kliknie Apply. Rebindings to wyjątek: idą do PlayerPrefs natychmiast
            // przez RebindingService.SaveOverrides w OnRebindClick (preview live), więc Cancel
            // musi je revertować z snapshotu zrobionego w Show.
            RestorePendingRebindingsIfNeeded();
            _working = null;
            Log.Info("[SettingsScreenUI] Cancel clicked — discarded working copy");
            OnBack?.Invoke();
        }

        private void OnResetSectionClicked()
        {
            var section = _activeSection switch
            {
                0 => SettingsSection.Control,
                1 => SettingsSection.Graphics,
                2 => SettingsSection.Audio,
                3 => SettingsSection.Language,
                4 => SettingsSection.General,
                _ => SettingsSection.Control
            };
            ResetWorkingSection(section);

            // Sterowanie: dodatkowo reset bindings overrides (są poza SettingsData — własna persystencja)
            if (section == SettingsSection.Control && _rebindActions != null)
            {
                RebindingService.ResetAllOverrides(_rebindActions);
                MarkRebindingsDirty();
            }

            PopulateSection(_activeSection);
            RefreshRowLabels();
            Log.Info($"[SettingsScreenUI] Reset section: {section}");
        }

        private void OnResetAllClicked()
        {
            _working = new SettingsData();

            // Reset także bindings overrides (zakładka Sterowanie)
            if (_rebindActions != null)
            {
                RebindingService.ResetAllOverrides(_rebindActions);
                MarkRebindingsDirty();
            }

            PopulateSection(_activeSection);
            RefreshRowLabels();
            Log.Info("[SettingsScreenUI] Reset all clicked");
        }

        private void ResetWorkingSection(SettingsSection section)
        {
            if (_working == null)
                return;

            switch (section)
            {
                case SettingsSection.Control:  _working.Control  = new ControlSettings();  break;
                case SettingsSection.Graphics: _working.Graphics = new GraphicsSettings(); break;
                case SettingsSection.Audio:    _working.Audio    = new AudioSettings();    break;
                case SettingsSection.Language: _working.Language = new LanguageSettings(); break;
                case SettingsSection.General:  _working.General  = new GeneralSettings();  break;
            }
        }

        private void CaptureRebindingSnapshot()
        {
            _rebindingsSnapshotJson = RebindingService.GetSavedOverridesJson();
            _rebindingsDirty = false;
        }

        private void MarkRebindingsDirty()
        {
            _rebindingsDirty = true;
        }

        private void RestorePendingRebindingsIfNeeded()
        {
            if (!_rebindingsDirty)
                return;

            RebindingService.RestoreSavedOverridesJson(_rebindingsSnapshotJson, _rebindActions);
            _rebindingsDirty = false;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  PRIMITIVE GO HELPERS (used by all partials)
        //  Aliasy do MenuScreenPrimitives — żeby partial'e nie musiały kwalifikować
        //  pełną nazwą przy każdym wywołaniu.
        // ═══════════════════════════════════════════════════════════════════

        private static GameObject NewGO(string name, Transform parent) => MenuScreenPrimitives.NewGO(name, parent);
        private static TextMeshProUGUI MakeTMP(string name, Transform parent) => MenuScreenPrimitives.MakeTMP(name, parent);
        private static void FillRT(GameObject go) => MenuScreenPrimitives.Fill(go);
    }
}
