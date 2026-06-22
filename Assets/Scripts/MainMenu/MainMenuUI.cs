using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace MainMenu
{
    public enum MenuLanguage { PL, EN }

    /// <summary>
    /// Procedurally builds the main menu UI.
    /// Setup: empty GameObject with this script in MainMenu scene.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("Background")]
        [Tooltip("Opcjonalne zdjęcie tła. Jeśli puste — ciemne tło.")]
        public Sprite backgroundImage;

        [Header("Language")]
        public MenuLanguage language = MenuLanguage.PL;

        private Canvas canvas;
        private GameObject menuContent;
        private LoadGameScreenUI loadGameScreen;
        private CreditsScreenUI creditsScreen;
        private HelpScreenUI helpScreen;
        private SettingsScreenUI settingsScreen;
        private ModsScreenUI modsScreen;
        private MultiplayerScreenUI multiplayerScreen;
        /// <summary>Wszystkie ekrany w kolejności build — iterated przez ReturnToMenu/HandleEscape/OnLocaleChanged.</summary>
        private readonly System.Collections.Generic.List<IMenuScreen> _screens = new();
        private MenuLanguage activeLanguage;
        private readonly System.Collections.Generic.List<TextMeshProUGUI> buttonLabels = new();

        // Refs do i18n refresh (M13-4a)
        private TextMeshProUGUI _titleLbl;
        private TextMeshProUGUI _versionLbl;

        private const float LeftMargin = 120f;
        private const float TopOffset = -140f;
        private const float ButtonSpacing = 65f;
        private const int TitleFontSize = 52;
        private const int ButtonFontSize = 30;
        private const int VersionFontSize = 18;
        private const float MenuBackdropWidth = 640f;

        private MenuEntry[] entries;

        // ESC obsłużone bezpośrednio w Update przez Keyboard.current (idiom projektu — patrz TrackPopupUI).
        // Brak własnego InputActions / action map'a — MainMenu nie ma popupów wymagających rebindowalnego Cancel.

        void OnDestroy()
        {
            LocalizationService.OnLanguageChanged -= OnLocaleChanged;
        }

        void Awake()
        {
            // Sync legacy `language` field z bieżącym locale (sub-screens jeszcze go używają — M13-4b/c/d)
            language = LocaleToMenuLanguage(LocalizationService.CurrentLocale);
            activeLanguage = language;

            BuildEntries();
            BuildCanvas();
            BuildMenuContent();
            BuildLoadGameScreen();
            BuildCreditsScreen();
            BuildHelpScreen();
            BuildSettingsScreen();
            BuildModsScreen();
            BuildMultiplayerScreen();

            // Subskrybcja zmian języka z Settings UI / Steam autodetect
            LocalizationService.OnLanguageChanged += OnLocaleChanged;
        }

        /// <summary>
        /// Hot-reload przy zmianie języka. Aktualizuje legacy <see cref="language"/> field
        /// (do czasu pełnego rolloutu sub-screens w M13-4b/c/d), refreshuje labele.
        /// </summary>
        private void OnLocaleChanged()
        {
            language = LocaleToMenuLanguage(LocalizationService.CurrentLocale);
            activeLanguage = language;
            RefreshLabels();

            foreach (var screen in _screens)
                if (screen != null && screen.IsVisible)
                    screen.RefreshLanguage();
        }

        /// <summary>
        /// Konwersja LocaleCode → legacy MenuLanguage (PL/EN). Inne języki (DE/CZ/JP/RU/UK)
        /// mapują na EN dopóki sub-screens nie używają LocalizationService bezpośrednio.
        /// </summary>
        private static MenuLanguage LocaleToMenuLanguage(LocaleCode code)
        {
            return code == LocaleCode.PL ? MenuLanguage.PL : MenuLanguage.EN;
        }

        void Update()
        {
            // Inspector-driven manual language change (legacy fallback) → propagate do LocalizationService
            if (language != activeLanguage)
            {
                activeLanguage = language;
                var targetCode = language == MenuLanguage.PL ? LocaleCode.PL : LocaleCode.EN;
                if (LocalizationService.CurrentLocale != targetCode)
                    LocalizationService.SetLanguage(targetCode);
            }

            // ESC: direct keyboard check (see TrackPopupUI for rationale)
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                HandleEscape();
        }

        private void HandleEscape()
        {
            foreach (var screen in _screens)
            {
                if (screen != null && screen.IsVisible)
                {
                    ReturnToMenu();
                    return;
                }
            }
        }

        private void ShowLoadScreen()
        {
            menuContent.SetActive(false);
            loadGameScreen.Show();
        }

        private void ShowCreditsScreen()
        {
            menuContent.SetActive(false);
            creditsScreen.Show();
        }

        private void ShowHelpScreen()
        {
            menuContent.SetActive(false);
            helpScreen.Show();
        }

        private void ReturnToMenu()
        {
            foreach (var screen in _screens)
                screen?.Hide();
            menuContent.SetActive(true);
        }

        private void BuildMenuContent()
        {
            // Container for all main menu elements (hide/show as group)
            menuContent = new GameObject("MenuContent");
            menuContent.transform.SetParent(canvas.transform, false);
            var rt = menuContent.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            BuildBackground(menuContent.transform);
            BuildMenuBackdrop(menuContent.transform);
            BuildTitle(menuContent.transform);
            BuildMenuButtons(menuContent.transform);
            BuildVersionLabel(menuContent.transform);
        }

        private void BuildLoadGameScreen()
        {
            var obj = new GameObject("LoadGameScreenUI");
            obj.transform.SetParent(transform, false);
            loadGameScreen = obj.AddComponent<LoadGameScreenUI>();
            loadGameScreen.Build(canvas.transform);
            loadGameScreen.OnBack = ReturnToMenu;
            _screens.Add(loadGameScreen);
        }

        private void BuildCreditsScreen()
        {
            var obj = new GameObject("CreditsScreenUI");
            obj.transform.SetParent(transform, false);
            creditsScreen = obj.AddComponent<CreditsScreenUI>();
            creditsScreen.Build(canvas.transform);
            creditsScreen.OnBack = ReturnToMenu;
            _screens.Add(creditsScreen);
        }

        private void BuildHelpScreen()
        {
            var obj = new GameObject("HelpScreenUI");
            obj.transform.SetParent(transform, false);
            helpScreen = obj.AddComponent<HelpScreenUI>();
            helpScreen.Build(canvas.transform);
            helpScreen.OnBack = ReturnToMenu;
            _screens.Add(helpScreen);
        }

        private void BuildSettingsScreen()
        {
            var obj = new GameObject("SettingsScreenUI");
            obj.transform.SetParent(transform, false);
            settingsScreen = obj.AddComponent<SettingsScreenUI>();
            settingsScreen.Build(canvas.transform);
            settingsScreen.OnBack = ReturnToMenu;
            _screens.Add(settingsScreen);
        }

        private void BuildModsScreen()
        {
            var obj = new GameObject("ModsScreenUI");
            obj.transform.SetParent(transform, false);
            modsScreen = obj.AddComponent<ModsScreenUI>();
            modsScreen.Build(canvas.transform);
            modsScreen.OnBack = ReturnToMenu;
            _screens.Add(modsScreen);
        }

        private void BuildMultiplayerScreen()
        {
            var obj = new GameObject("MultiplayerScreenUI");
            obj.transform.SetParent(transform, false);
            multiplayerScreen = obj.AddComponent<MultiplayerScreenUI>();
            multiplayerScreen.Build(canvas.transform);
            multiplayerScreen.OnBack = ReturnToMenu;
            _screens.Add(multiplayerScreen);
        }

        private void RefreshLabels()
        {
            for (int i = 0; i < buttonLabels.Count && i < entries.Length; i++)
                buttonLabels[i].text = LocalizationService.Get(entries[i].labelKey);

            if (_titleLbl != null)
                _titleLbl.text = LocalizationService.Get("main_menu.title");

            if (_versionLbl != null)
                _versionLbl.text = LocalizationService.Get("main_menu.version_format", Application.version);
        }

        // ─────────────────────────────────────────────
        //  CANVAS
        // ─────────────────────────────────────────────

        private void BuildCanvas()
        {
            var canvasObj = new GameObject("MainMenuCanvas");
            canvasObj.transform.SetParent(transform, false);

            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);

            canvasObj.AddComponent<GraphicRaycaster>();

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var esObj = new GameObject("EventSystem");
                esObj.transform.SetParent(transform, false);
                esObj.AddComponent<EventSystem>();
                esObj.AddComponent<InputSystemUIInputModule>();
            }
        }

        // ─────────────────────────────────────────────
        //  BACKGROUND
        // ─────────────────────────────────────────────

        private void BuildBackground(Transform parent)
        {
            var bgObj = new GameObject("Background");
            bgObj.transform.SetParent(parent, false);

            var img = bgObj.AddComponent<Image>();
            var rt = bgObj.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            if (backgroundImage != null)
            {
                img.sprite = backgroundImage;
                img.type = Image.Type.Simple;
                img.preserveAspect = false;
                img.color = Color.white;
            }
            else
            {
                img.color = UITheme.AppBackground;
            }

            bgObj.transform.SetAsFirstSibling();
        }

        private void BuildMenuBackdrop(Transform parent)
        {
            var panelObj = new GameObject("MenuBackdrop");
            panelObj.transform.SetParent(parent, false);

            var panelRt = panelObj.AddComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0f, 0f);
            panelRt.anchorMax = new Vector2(0f, 1f);
            panelRt.pivot = new Vector2(0f, 0.5f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta = new Vector2(MenuBackdropWidth, 0f);

            var panelImg = panelObj.AddComponent<Image>();
            panelImg.color = UITheme.OverlayPanel;

            var stripeObj = new GameObject("AccentStripe");
            stripeObj.transform.SetParent(panelObj.transform, false);
            var stripeRt = stripeObj.AddComponent<RectTransform>();
            stripeRt.anchorMin = new Vector2(0f, 0f);
            stripeRt.anchorMax = new Vector2(0f, 1f);
            stripeRt.pivot = new Vector2(0f, 0.5f);
            stripeRt.anchoredPosition = Vector2.zero;
            stripeRt.sizeDelta = new Vector2(10f, 0f);

            var stripeImg = stripeObj.AddComponent<Image>();
            stripeImg.color = UITheme.PrimaryAccent;
        }

        // ─────────────────────────────────────────────
        //  TITLE
        // ─────────────────────────────────────────────

        private void BuildTitle(Transform parent)
        {
            var titleObj = CreateTextObject("Title", parent);
            var tmp = titleObj.GetComponent<TextMeshProUGUI>();
            _titleLbl = tmp; // ref dla RefreshLabels
            tmp.text = LocalizationService.Get("main_menu.title");
            tmp.fontSize = TitleFontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = UITheme.PrimaryText;
            tmp.alignment = TextAlignmentOptions.Left;

            var rt = titleObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(LeftMargin, TopOffset);
            rt.sizeDelta = new Vector2(600f, 70f);
        }

        // ─────────────────────────────────────────────
        //  MENU BUTTONS
        // ─────────────────────────────────────────────

        private struct MenuEntry
        {
            public string labelKey;          // i18n klucz, np. "main_menu.buttons.load"
            public System.Action onClick;
        }

        private void BuildEntries()
        {
            entries = new MenuEntry[]
            {
                new MenuEntry { labelKey = "main_menu.buttons.load",               onClick = OnLoad },
                new MenuEntry { labelKey = "main_menu.buttons.new_single_player",  onClick = OnNewSinglePlayer },
                new MenuEntry { labelKey = "main_menu.buttons.multiplayer",        onClick = OnMultiplayer },
                new MenuEntry { labelKey = "main_menu.buttons.settings",           onClick = OnSettings },
                new MenuEntry { labelKey = "main_menu.buttons.mods",               onClick = OnMods },
                new MenuEntry { labelKey = "main_menu.buttons.help",               onClick = OnHelp },
                new MenuEntry { labelKey = "main_menu.buttons.credits",            onClick = OnCredits },
                new MenuEntry { labelKey = "main_menu.buttons.exit",               onClick = OnExit },
            };
        }

        private void BuildMenuButtons(Transform parent)
        {
            var container = new GameObject("MenuButtons");
            container.transform.SetParent(parent, false);

            var containerRT = container.AddComponent<RectTransform>();
            containerRT.anchorMin = new Vector2(0f, 1f);
            containerRT.anchorMax = new Vector2(0.5f, 1f);
            containerRT.pivot = new Vector2(0f, 1f);
            containerRT.anchoredPosition = new Vector2(LeftMargin, TopOffset - 90f);
            containerRT.sizeDelta = new Vector2(600f, 600f);

            var layout = container.AddComponent<VerticalLayoutGroup>();
            layout.spacing = UITheme.Spacing.Md;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            foreach (var entry in entries)
                CreateMenuButton(entry, container.transform);
        }

        private void CreateMenuButton(MenuEntry entry, Transform parent)
        {
            string label = LocalizationService.Get(entry.labelKey);
            // GO name: czyta klucz, łatwy debug w hierarchy
            var btnObj = new GameObject(entry.labelKey);
            btnObj.transform.SetParent(parent, false);

            var rt = btnObj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(500f, ButtonSpacing);

            // Invisible image as raycast target for Button
            var img = btnObj.AddComponent<Image>();
            img.color = Color.clear;

            var btn = btnObj.AddComponent<Button>();
            UITheme.ApplyButtonStyle(btn, img, UIButtonTone.Ghost, UIShapePreset.Button);

            var textObj = CreateTextObject("Label", btnObj.transform);
            var tmp = textObj.GetComponent<TextMeshProUGUI>();
            tmp.text = label;
            buttonLabels.Add(tmp);
            tmp.fontSize = ButtonFontSize;
            tmp.fontStyle = FontStyles.Normal;
            tmp.color = UITheme.PrimaryText;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;

            var textRT = textObj.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            var action = entry.onClick;
            btn.onClick.AddListener(() => action());

            var hover = btnObj.AddComponent<MenuButtonHover>();
            hover.Init(tmp, UITheme.PrimaryText, UITheme.PrimaryAccent);
        }

        // ─────────────────────────────────────────────
        //  VERSION LABEL
        // ─────────────────────────────────────────────

        private void BuildVersionLabel(Transform parent)
        {
            var vObj = CreateTextObject("VersionLabel", parent);
            var tmp = vObj.GetComponent<TextMeshProUGUI>();
            _versionLbl = tmp; // ref dla RefreshLabels
            tmp.text = LocalizationService.Get("main_menu.version_format", Application.version);
            tmp.fontSize = VersionFontSize;
            tmp.color = UITheme.SecondaryText;
            tmp.alignment = TextAlignmentOptions.BottomRight;
            tmp.raycastTarget = false;

            var rt = vObj.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-30f, 20f);
            rt.sizeDelta = new Vector2(200f, 40f);
        }

        // ─────────────────────────────────────────────
        //  HELPERS
        // ─────────────────────────────────────────────

        private GameObject CreateTextObject(string name, Transform parent) => MenuScreenPrimitives.CreateTMP(name, parent);

        // ─────────────────────────────────────────────
        //  BUTTON ACTIONS (stubs)
        // ─────────────────────────────────────────────

        private void OnLoad()
        {
            ShowLoadScreen();
        }

        private void OnNewSinglePlayer()
        {
            GameCreatorContext.Mode = GameCreatorContext.GameMode.SinglePlayer;
            GameCreatorContext.ResetServerConfig();
            SceneManager.LoadScene("GameCreator");
        }

        private void OnMultiplayer()
        {
            menuContent.SetActive(false);
            multiplayerScreen.Show();
        }

        private void OnSettings()
        {
            menuContent.SetActive(false);
            settingsScreen.Show();
        }

        private void OnMods()
        {
            menuContent.SetActive(false);
            modsScreen.Show();
        }

        private void OnHelp()
        {
            ShowHelpScreen();
        }

        private void OnCredits()
        {
            ShowCreditsScreen();
        }

        private void OnExit()
        {
            Log.Info("[MainMenu] Wyjście");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    internal static class MenuScreenPrimitives
    {
        /// <summary>
        /// MainMenu-specific high-level helpers (TopBar/Button/ScrollArea/FullscreenRoot).
        /// Generic helpers (NewGO/Stretch/Fill/MakeTMP) delegują do <see cref="UIPrimitives"/>
        /// w SharedUI (Krok 1 adoption pass 2026-05-15) — wcześniej były duplikatem z
        /// <c>DepotUIPanelPrimitives</c>.
        /// </summary>
        public static GameObject NewGO(string name, Transform parent) => UIPrimitives.NewGO(name, parent);

        /// <summary>Skrót: <see cref="UIPrimitives.MakeTMP(string, Transform, float, UIThemeTextRole, TextAlignmentOptions, FontStyles)"/> z defaultami.</summary>
        public static TextMeshProUGUI MakeTMP(string name, Transform parent) => UIPrimitives.MakeTMP(name, parent);

        /// <summary>Rozciąga RectTransform GameObject'a na cały parent — delegate do <see cref="UIPrimitives.Fill"/>.</summary>
        public static void Fill(GameObject go) => UIPrimitives.Fill(go);

        public static GameObject CreateFullscreenRoot(string name, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.AddComponent<Image>().color = UITheme.AppBackground;

            RectTransform rt = root.GetComponent<RectTransform>();
            Stretch(rt);

            return root;
        }

        public static GameObject CreateTopBar(string name, Transform parent, System.Action onBack, out TextMeshProUGUI backLabel, out TextMeshProUGUI titleLabel)
        {
            var bar = new GameObject(name);
            bar.transform.SetParent(parent, false);
            bar.AddComponent<Image>().color = UITheme.OverlayPanelStrong;

            RectTransform barRT = bar.GetComponent<RectTransform>();
            barRT.anchorMin = new Vector2(0f, 1f);
            barRT.anchorMax = new Vector2(1f, 1f);
            barRT.pivot = new Vector2(0.5f, 1f);
            barRT.anchoredPosition = Vector2.zero;
            barRT.sizeDelta = new Vector2(0f, 70f);

            var layout = bar.AddComponent<HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Md);
            layout.spacing = UITheme.Spacing.Xl;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            GameObject backButton = CreateButton("BackButton", bar.transform, "\u2190", 28, 52f, 52f);
            backLabel = backButton.GetComponentInChildren<TextMeshProUGUI>();
            backButton.GetComponent<Button>().onClick.AddListener(() => onBack?.Invoke());

            titleLabel = CreateTMP("Title", bar.transform, 32, UIThemeTextRole.Primary, TextAlignmentOptions.Left, FontStyles.Bold)
                .GetComponent<TextMeshProUGUI>();
            LayoutElement titleLayout = titleLabel.gameObject.AddComponent<LayoutElement>();
            titleLayout.preferredWidth = 400f;
            titleLayout.preferredHeight = 50f;

            var divider = new GameObject("Divider");
            divider.transform.SetParent(bar.transform, false);
            RectTransform dividerRT = divider.AddComponent<RectTransform>();
            dividerRT.anchorMin = new Vector2(0f, 0f);
            dividerRT.anchorMax = new Vector2(1f, 0f);
            dividerRT.pivot = new Vector2(0.5f, 0f);
            dividerRT.anchoredPosition = Vector2.zero;
            dividerRT.sizeDelta = new Vector2(0f, 1f);
            divider.AddComponent<Image>().color = UITheme.TopBarDivider;
            divider.AddComponent<LayoutElement>().ignoreLayout = true;

            return bar;
        }

        /// <summary>
        /// CreateTMP zwracający GameObject (legacy API używane przez CreateTopBar/CreateButton dla potem extracted layout).
        /// Implementacja przez <see cref="UIPrimitives.MakeTMP"/>; ten wrapper tylko zwraca .gameObject.
        /// </summary>
        public static GameObject CreateTMP(
            string name,
            Transform parent,
            int fontSize = 18,
            UIThemeTextRole role = UIThemeTextRole.Primary,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left,
            FontStyles style = FontStyles.Normal)
        {
            return UIPrimitives.MakeTMP(name, parent, fontSize, role, alignment, style).gameObject;
        }

        public static GameObject CreateButton(string name, Transform parent, string label, int fontSize, float width, float height, bool primary = false)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>().sizeDelta = new Vector2(width, height);

            var background = obj.AddComponent<Image>();
            var button = obj.AddComponent<Button>();
            ApplyButtonStyle(button, background, primary);
            obj.AddComponent<RailwayManager.SharedUI.ButtonPressFeedback>(); // MUI-11: subtelny press feel

            var textObj = CreateTMP(
                "Label",
                obj.transform,
                fontSize,
                primary ? UITheme.GetButtonTextRole(UIButtonTone.Primary) : UITheme.GetButtonTextRole(UIButtonTone.Secondary),
                TextAlignmentOptions.Center);
            TextMeshProUGUI labelText = textObj.GetComponent<TextMeshProUGUI>();
            labelText.text = label;
            Stretch(textObj.GetComponent<RectTransform>());

            LayoutElement layout = obj.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = height;

            return obj;
        }

        public static void ApplyButtonStyle(Button button, Image background, bool primary = false)
        {
            if (button == null || background == null)
                return;

            UITheme.ApplyButtonStyle(
                button,
                background,
                primary ? UIButtonTone.Primary : UIButtonTone.Secondary,
                UIShapePreset.Pill);
        }

        /// <summary>Rozciąga RectTransform na cały parent — delegate do <see cref="UIPrimitives.Stretch"/>.</summary>
        public static void Stretch(RectTransform rectTransform) => UIPrimitives.Stretch(rectTransform);

        /// <summary>
        /// Buduje pionowy scroll area z poprawną hierarchią Unity ScrollRect:
        /// <code>
        /// ScrollRect (root, ScrollRect component)
        ///   ├─ Viewport (Image + RectMask2D, child of ScrollRect)
        ///   │    └─ Content (VerticalLayoutGroup + ContentSizeFitter, child of Viewport)
        ///   └─ Scrollbar Vertical (child of ScrollRect)
        ///        └─ SlidingArea
        ///             └─ Handle (Image)
        /// </code>
        ///
        /// Używane przez Help / Mods / LoadGame screens (BUG-002 fix). Wcześniej każdy z tych
        /// ekranów miał Viewport jako sibling ScrollRect (oba pod root) — Unity ScrollRect
        /// wymaga że Viewport jest child ScrollRect, inaczej IScrollHandler events nie działają.
        ///
        /// <paramref name="contentRT"/> zwraca RectTransform Content node — Caller dodaje do niego
        /// dzieci (rows, cards itd.).
        /// </summary>
        public static ScrollRect BuildVerticalScrollArea(
            Transform parent,
            Vector2 offsetMin,
            Vector2 offsetMax,
            RectOffset contentPadding,
            float contentSpacing,
            TextAnchor contentAlignment,
            out RectTransform contentRT)
        {
            // Root: ScrollRect GameObject (zajmuje przestrzeń w parent)
            var scrollObj = new GameObject("ScrollRect");
            scrollObj.transform.SetParent(parent, false);
            var scrollObjRT = scrollObj.AddComponent<RectTransform>();
            scrollObjRT.anchorMin = Vector2.zero;
            scrollObjRT.anchorMax = Vector2.one;
            scrollObjRT.offsetMin = offsetMin;
            scrollObjRT.offsetMax = offsetMax;

            var scrollRect = scrollObj.AddComponent<ScrollRect>();
            scrollRect.horizontal        = false;
            scrollRect.vertical          = true;
            scrollRect.scrollSensitivity = 30f;
            scrollRect.movementType      = ScrollRect.MovementType.Clamped;

            // Viewport: child ScrollRect (z Image dla raycast + RectMask2D dla clipping)
            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollObj.transform, false);
            var viewportRT = viewport.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = new Vector2(-12f, 0f); // miejsce dla scrollbara po prawej
            // Image z prawie-zerowym alpha — raycast target żeby scroll wheel reagował
            var viewportImg = viewport.AddComponent<Image>();
            viewportImg.color = UITheme.WithAlpha(UITheme.PrimaryText, 0.01f);
            viewportImg.raycastTarget = true;
            viewport.AddComponent<RectMask2D>();

            // Content: child Viewport (anchored top, ContentSizeFitter rośnie w dół)
            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            contentRT = content.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot     = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = Vector2.zero;

            var vl = content.AddComponent<VerticalLayoutGroup>();
            vl.padding              = contentPadding;
            vl.spacing              = contentSpacing;
            vl.childAlignment       = contentAlignment;
            vl.childControlWidth    = true;
            vl.childControlHeight   = false;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Scrollbar Vertical: child ScrollRect (sibling Viewport)
            var scrollbarObj = new GameObject("Scrollbar Vertical");
            scrollbarObj.transform.SetParent(scrollObj.transform, false);
            var scrollbarRT = scrollbarObj.AddComponent<RectTransform>();
            scrollbarRT.anchorMin = new Vector2(1f, 0f);
            scrollbarRT.anchorMax = new Vector2(1f, 1f);
            scrollbarRT.pivot     = new Vector2(1f, 1f);
            scrollbarRT.sizeDelta = new Vector2(10f, 0f);
            scrollbarRT.anchoredPosition = Vector2.zero;
            var scrollbarBg = scrollbarObj.AddComponent<Image>();
            scrollbarBg.color = UITheme.WithAlpha(UITheme.OverlayPanel, 0.4f);

            var scrollbar = scrollbarObj.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            // Sliding Area
            var slidingArea = new GameObject("SlidingArea");
            slidingArea.transform.SetParent(scrollbarObj.transform, false);
            var slidingRT = slidingArea.AddComponent<RectTransform>();
            slidingRT.anchorMin = Vector2.zero;
            slidingRT.anchorMax = Vector2.one;
            slidingRT.offsetMin = new Vector2(2f, 2f);
            slidingRT.offsetMax = new Vector2(-2f, -2f);

            // Handle
            var handleObj = new GameObject("Handle");
            handleObj.transform.SetParent(slidingArea.transform, false);
            var handleRT = handleObj.AddComponent<RectTransform>();
            handleRT.anchorMin = Vector2.zero;
            handleRT.anchorMax = Vector2.one;
            handleRT.offsetMin = Vector2.zero;
            handleRT.offsetMax = Vector2.zero;
            var handleImg = handleObj.AddComponent<Image>();
            handleImg.color = UITheme.WithAlpha(UITheme.PrimaryText, 0.45f);

            scrollbar.targetGraphic = handleImg;
            scrollbar.handleRect    = handleRT;

            // Bind ScrollRect references — kolejność ważna (handle dopiero po scrollbar setup)
            scrollRect.viewport = viewportRT;
            scrollRect.content  = contentRT;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scrollRect.verticalScrollbarSpacing    = 4f;

            return scrollRect;
        }
    }
}
