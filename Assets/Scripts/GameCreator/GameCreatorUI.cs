using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using MainMenu;
using RailwayManager.Core;
using RailwayManager.Core.Difficulty;
using RailwayManager.Core.GameRules;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.GameCreator
{

/// <summary>
/// Scena kreatora gry — budowana proceduralnie.
/// Czyta GameCreatorContext.Mode i dostosowuje dostępne zakładki:
///   SinglePlayer : Ogólnie | Rozgrywka
///   Multiplayer  : Ogólnie | Serwer
///
/// Klasa rozbita na partial files:
/// - <c>GameCreatorUI.cs</c>                — pola, konstanty, lifecycle, i18n hot-reload,
///                                              prymitywy (NewGO/MakeTMP/FillRT/EnsureEventSystem)
/// - <c>GameCreatorUI.Layout.cs</c>         — BuildCanvas/TopBar/Sidebar/ContentArea/BottomBar
/// - <c>GameCreatorUI.Sections.cs</c>       — PopulateSection (dispatcher) + 3 sekcje
///                                              (Ogolnie/Rozgrywka/Serwer)
/// - <c>GameCreatorUI.Rows.cs</c>           — InputRow/DropdownRow/SliderRow/ToggleRow/MakeRow/AddSpacer
/// - <c>GameCreatorUI.Difficulty.*.cs</c>   — sekcja Difficulty (preset selector + 10 modifierów +
///                                              tooltip + live preview + apply on start)
/// - <c>GameCreatorUI.CancelConfirmation.cs</c> — confirmation modal przed powrotem do MainMenu
/// </summary>
public partial class GameCreatorUI : MonoBehaviour
{
    // ── layout ────────────────────────────────────
    private const float SidebarWidth = 220f;
    private const float TopBarHeight  = 70f;
    private const float BotBarHeight  = 70f;

    // ── input limits ─────────────────────────────
    // Per-folder CLAUDE.md MP server config: max 40 chars name, password optional.
    private const int MaxGameNameLength       = 40;
    private const int MaxServerNameLength     = 40;
    private const int MaxServerPasswordLength = 32;
    private const int MaxSeedDigits           = 11; // int.MinValue = -2147483648 (11 chars)

    // ── colours ───────────────────────────────────
    private static readonly Color PanelBg     = UITheme.AppBackground;
    private static readonly Color TopBarBg    = UITheme.TopBarBackground;
    private static readonly Color SidebarBg   = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
    private static readonly Color BotBarBg    = UITheme.TopBarBackground;
    private static readonly Color BtnNormal   = UITheme.TopBarInset;
    private static readonly Color BtnActive   = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.24f);
    private static readonly Color RowBg       = UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f);
    private static readonly Color InputBg     = UITheme.WithAlpha(UITheme.PrimarySurface, 0.92f);
    private static readonly Color SliderBg    = UITheme.WithAlpha(UITheme.Border, 0.76f);
    private static readonly Color DropdownBg  = UITheme.WithAlpha(UITheme.SecondarySurface, 0.9f);
    private static readonly Color ToggleBg    = UITheme.WithAlpha(UITheme.PrimarySurface, 0.96f);
    private static readonly Color BtnPrimary  = UITheme.Success;
    private static readonly Color BtnCancel   = UITheme.WithAlpha(UITheme.Border, 0.84f);
    private static readonly Color TextPrimary = UITheme.PrimaryText;
    private static readonly Color TextMuted   = UITheme.SecondaryText;
    private static readonly Color Accent      = UITheme.PrimaryAccent;

    // ── state ─────────────────────────────────────
    // Sekcja Mapa wyrzucona 2026-05-14 — była dekoracyjna (5 mockowych pozycji, _selectedMap
    // bez konsumenta, niezgodne ze spec'em DLC region picker). Wybór regionu/DLC wraca w M-DLC.
    private bool   _isMP;
    private int    _activeSection;

    // ── section setup (2 tabs always) ─────────────
    private readonly TextMeshProUGUI[] _secLbls    = new TextMeshProUGUI[2];
    private readonly Image[]           _secBgs     = new Image[2];
    private readonly Image[]           _secAccents = new Image[2];
    private string[] _secKeys; // i18n keys per tab (variant SP/MP)

    // ── i18n active labels ───────────────────────
    // Lista (TMP, i18nKey) — registered przez row builders, repopulate na zmianie języka
    // przez OnLocaleChanged → PopulateSection(_activeSection).
    private readonly List<(TextMeshProUGUI lbl, string i18nKey)> _activeLabels = new();
    // Bottom bar buttons (refresh przy zmianie języka)
    private TextMeshProUGUI _lblCancelBtn, _lblStartBtn;

    // ── root refs ─────────────────────────────────
    private Transform         _root;          // root panel (fills canvas)
    private Transform         _contentParent;
    private RectTransform     _contentRT;
    private TextMeshProUGUI   _titleLbl;

    // ── row label refs (for language refresh) ─────
    // Ogólnie
    private TextMeshProUGUI _lblGameName;
    // TD-022: usunięte _lblDifficulty + _lblFunds — duplikaty Difficulty/StartBudgetMultiplier
    // z sekcji Rozgrywka. Single source of truth = Rozgrywka section.
    // Rozgrywka
    private TextMeshProUGUI _lblPauseOnStart, _lblAutosave, _lblAutosaveInt, _lblSeed, _lblDispatchPolicy;
    // Serwer
    private TextMeshProUGUI _lblSrvName, _lblMaxPlayers, _lblPassword, _lblVisibility;

    // ── TD-022: control handles (do ApplyOnStart) ──
    // Ogólnie
    private TMP_InputField _fieldGameName;
    private TMP_InputField _fieldAssistantName;  // M11 AS-1d: imię asystenta (puste = default "pan Tadeusz")
    private TextMeshProUGUI _lblAssistantName;
    // Rozgrywka
    // Speed slider usunięty 2026-05-14 — TopBarUI ma dyskretne x1/x5/x25/x150/x500 z `Mathf.Approximately`,
    // slider 0.5-3x w kreatorze dawał wartości niezgodne z runtime'em. Speed steruje się z top bara w grze.
    private Toggle         _togglePauseOnStart;
    private Toggle         _toggleAutosave;
    private TMP_Dropdown   _ddAutosaveInterval;
    private TMP_InputField _fieldSeed;          // GameState.Seed (deterministic RNG)
    private TMP_Dropdown   _ddDispatchPolicy;   // M-Dispatch Faza 4b — polityka dispatchera mapy OSM
    // Serwer (M10 placeholder — wartości zbierane do GameCreatorContext, MP system jeszcze nie konsumuje)
    private TMP_InputField _fieldSrvName;
    private TMP_Dropdown   _ddMaxPlayers;
    private TMP_InputField _fieldPassword;
    private TMP_Dropdown   _ddVisibility;

    // Persistent form state. Section UI is rebuilt on tab/locale changes, so Start
    // must read these values instead of only the currently visible controls.
    private string _gameNameValue;
    private string _assistantNameValue; // M11 AS-1d
    private bool _pauseOnStartValue;
    private bool _autosaveEnabledValue = true;
    private int _autosaveIntervalIndex;
    private int _seedValue; // GameState.Seed — 0 = deterministyczne baseline (default WorldSavable.InitializeDefault).
    private DispatchPolicy _dispatchPolicyValue = DispatchPolicy.Balanced; // M-Dispatch Faza 4b
    private DifficultyPreset _difficultyPresetValue = DifficultyPreset.Normal;
    private DifficultyModifiers _difficultyModifiersValue = new DifficultyModifiers();
    private readonly Dictionary<GameRule, bool> _gameRuleValues = new();
    private string _serverNameValue = "";
    private int _serverMaxPlayersIndex = 1; // 4 players
    private string _serverPasswordValue = "";
    private int _serverVisibilityIndex;

    // Per-folder CLAUDE.md: cancel confirmation modal — TYLKO gdy gracz dotknął jakiegokolwiek
    // kontrolki (`_isDirty == true`), inaczej direct exit do MainMenu. Set przez row builderów
    // (InputRow/SliderRow/ToggleRow/DropdownRow) + Difficulty/Rules listeners.
    // Programatyczne write'y przez SetValueWithoutNotify NIE flag'ują dirty (preset switch,
    // CaptureActiveSectionState restore, suppressAutoCustom).
    private bool _isDirty;

    private void MarkDirty() => _isDirty = true;

    // ─────────────────────────────────────────────
    //  ENTRY
    // ─────────────────────────────────────────────

    private void Awake()
    {
        _isMP = GameCreatorContext.IsMultiplayer;
        InitializeFormState();

        _secKeys = _isMP
            ? new[] { "game_creator.tabs.general", "game_creator.tabs.server"   }
            : new[] { "game_creator.tabs.general", "game_creator.tabs.gameplay" };

        EnsureEventSystem();
        BuildCanvas();
        BuildTopBar();
        BuildSidebar();
        BuildContentArea();
        BuildBottomBar();

        _activeSection = 0;
        ApplySidebarState();
        PopulateSection(0);
    }

    // ── Input System ──
    private InputActions _inputActions;
    private InputActions.UIPopupActions _popupActions;

    private void OnEnable()
    {
        if (_inputActions == null)
        {
            _inputActions = new InputActions();
            RailwayManager.Core.Settings.RebindingService.ApplyOverridesTo(_inputActions);
            _popupActions = _inputActions.UIPopup;
        }
        _popupActions.Enable();
        LocalizationService.OnLanguageChanged += OnLocaleChanged;
    }

    private void OnDisable()
    {
        _popupActions.Disable();
        LocalizationService.OnLanguageChanged -= OnLocaleChanged;
    }

    private void OnDestroy()
    {
        _inputActions?.Dispose();
    }

    private void Update()
    {
        // ESC: direct keyboard check (see TrackPopupUI for rationale)
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            // MB-1: jeśli confirmation modal otwarty — ESC zamyka modal (nie scenę)
            if (_cancelModalGO != null && _cancelModalGO.activeSelf)
                HideCancelConfirmation();
            else
                ShowCancelConfirmation();
        }
    }

    // ─────────────────────────────────────────────
    //  i18n hot-reload (M13-4f)
    // ─────────────────────────────────────────────

    private void OnLocaleChanged()
    {
        // Top bar title
        if (_titleLbl != null)
            _titleLbl.text = LocalizationService.Get(_isMP ? "game_creator.title.multiplayer" : "game_creator.title.single_player");

        // Sidebar tabs
        for (int i = 0; i < _secLbls.Length; i++)
            if (_secLbls[i] != null) _secLbls[i].text = LocalizationService.Get(_secKeys[i]);

        // Bottom bar buttons
        if (_lblCancelBtn != null) _lblCancelBtn.text = LocalizationService.Get("game_creator.bottom.cancel");
        if (_lblStartBtn != null)
            _lblStartBtn.text = LocalizationService.Get(_isMP ? "game_creator.bottom.create_server" : "game_creator.bottom.start_game");

        // Active section: dropdown options są ładowane raz przy create — żeby zmienić język opcji
        // trzeba rebuild. Najprostsze: repopulate active section.
        PopulateSection(_activeSection);
    }

    // ─────────────────────────────────────────────
    //  PRIMITIVE HELPERS
    // ─────────────────────────────────────────────

    private static GameObject NewGO(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    private static TextMeshProUGUI MakeTMP(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var text = go.AddComponent<TextMeshProUGUI>();
        UITheme.ApplyTmpText(text, UIThemeTextRole.Primary);
        // i18n safety: row height jest sztywny (44-52px), więc default NoWrap + Ellipsis chroni
        // przed łamaniem wiersza w DE/CZ/EN dłuższych tłumaczeniach. Multi-line cases (cancel modal
        // body, tooltip, preset description, preview metrics) override do `Normal` w callsite'ach.
        text.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        text.overflowMode     = TMPro.TextOverflowModes.Ellipsis;
        return text;
    }

    private static void FillRT(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
    }
}

} // namespace RailwayManager.GameCreator
