using System.Collections.Generic;
using TMPro;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Dolny pasek narzedzi budowania.
    /// Pojawia sie gdy zakladka "Budowanie" jest aktywna.
    /// </summary>
    public class BuildMenuUI : MonoBehaviour
    {
        [Header("Colors")]
        [SerializeField] private Color panelBg = default;
        [SerializeField] private Color activeColor = default;
        [SerializeField] private Color normalColor = default;
        [SerializeField] private Color lockedColor = default;

        private class BuildToolButton
        {
            public ToolMode mode;
            public Button button;
            public Image background;
            public Image accent;
            public TMP_Text iconText;
            public Graphic iconImage; // MUI-11: SVGImage (wektor) lub Image — glif gdy sprite dostępny
            public TMP_Text labelText;
            public Color chipColor; // MUI-11 Wariant B: kolor kafelka (kategoria)
            public bool unlocked;
        }

        private GameObject panel;
        private GameObject toolButtonContainer;
        private TMP_Text sectionTitleText;
        private TMP_Text activeToolText;
        private readonly List<BuildToolButton> toolButtons = new();

        // chipHex (MUI-11 Wariant B): kolor kafelka per kategoria akcji (styl CBM).
        private static readonly (ToolMode mode, string icon, string iconRes, string label, string chipHex, bool unlocked)[] buildToolDefs =
        {
            (ToolMode.BuildTrack,    "TOR",  "ico_toolbar_track",    "Tory",           "3E7CB0", true),
            (ToolMode.BuildCatenary, "EL",   "ico_toolbar_catenary", "Siec trakcyjna", "C8893C", true),
            (ToolMode.BuildPath,     "DR",   "ico_toolbar_path",     "Sciezki",        "4F9E5B", true),
            (ToolMode.BuildRoom,     "POM",  "ico_toolbar_room",     "Pomieszczenia",  "2E9080", true),
            (ToolMode.Demolish,      "X",    "ico_toolbar_demolish", "Wyburz",         "C44C3A", true),
        };

        void Awake()
        {
            ApplyDefaultPalette();
            BuildUI();
        }

        void Start()
        {
            if (DepotUIManager.Instance != null)
                DepotUIManager.Instance.OnToolChanged += OnToolChanged;

            if (panel != null)
                panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (DepotUIManager.Instance != null)
                DepotUIManager.Instance.OnToolChanged -= OnToolChanged;
        }

        private void ApplyDefaultPalette()
        {
            if (panelBg == default)
                panelBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
            if (activeColor == default)
                activeColor = UITheme.PrimaryAccent;
            if (normalColor == default)
                normalColor = UITheme.SecondarySurface;
            if (lockedColor == default)
                lockedColor = UITheme.WithAlpha(UITheme.Border, 0.72f);
        }

        private void OnToolChanged(ToolMode mode)
        {
            UpdateVisuals();
        }

        private void OnBuildToolClicked(BuildToolButton tb)
        {
            if (!tb.unlocked || DepotUIManager.Instance == null)
                return;

            DepotUIManager.Instance.CurrentTool = tb.mode;
        }

        public void Show()
        {
            if (panel != null)
                panel.SetActive(true);
            
            UpdateVisuals();

            // MUI-1: Pokaż siatkę przy wejściu w tryb budowania
            var ground = DepotServices.Get<GroundGenerator>();
            if (ground != null)
                ground.SetGridVisible(true);
        }

        public void Hide()
        {
            if (panel != null)
                panel.SetActive(false);

            if (DepotUIManager.Instance != null)
            {
                var tool = DepotUIManager.Instance.CurrentTool;
                if (tool != ToolMode.Select)
                    DepotUIManager.Instance.CurrentTool = ToolMode.Select;
            }

            // MUI-1: Ukryj siatkę przy wyjściu z trybu budowania
            var ground = DepotServices.Get<GroundGenerator>();
            if (ground != null)
                ground.SetGridVisible(false);
        }

        public bool IsVisible => panel != null && panel.activeSelf;

        public void UnlockTool(ToolMode mode)
        {
            var tb = toolButtons.Find(t => t.mode == mode);
            if (tb != null)
            {
                tb.unlocked = true;
                if (tb.button != null)
                    tb.button.interactable = true;
                UpdateVisuals();
            }
        }

        public void LockTool(ToolMode mode)
        {
            var tb = toolButtons.Find(t => t.mode == mode);
            if (tb != null)
            {
                tb.unlocked = false;
                if (tb.button != null)
                    tb.button.interactable = false;

                if (DepotUIManager.Instance != null && DepotUIManager.Instance.CurrentTool == mode)
                    DepotUIManager.Instance.CurrentTool = ToolMode.Select;

                UpdateVisuals();
            }
        }

        private void UpdateVisuals()
        {
            var currentTool = DepotUIManager.Instance != null ? DepotUIManager.Instance.CurrentTool : ToolMode.Select;

            foreach (var tb in toolButtons)
            {
                bool isActive = tb.mode == currentTool;
                Color chip = tb.chipColor;

                // Wariant B (CBM): kafelek = kolor kategorii. Aktywny = jaśniejszy, locked = przyciemniony.
                Color background = !tb.unlocked
                    ? UITheme.Darken(chip, 0.5f)
                    : isActive ? Color.Lerp(chip, Color.white, 0.16f) : chip;

                if (tb.background != null)
                    tb.background.color = background;

                if (tb.button != null)
                {
                    tb.button.interactable = tb.unlocked;
                    tb.button.colors = DepotUIPanelPrimitives.CreateButtonColors(
                        background,
                        Color.Lerp(background, Color.white, 0.10f),
                        UITheme.Darken(background, 0.14f));
                }

                if (tb.accent != null)
                    tb.accent.gameObject.SetActive(isActive);

                // Wariant B: biały glif na kolorowym kafelku (kolor niesie kafelek, nie glif).
                Color glyph = tb.unlocked ? Color.white : new Color(1f, 1f, 1f, 0.45f);
                if (tb.iconText != null)
                    tb.iconText.color = glyph;
                if (tb.iconImage != null)
                    tb.iconImage.color = glyph;

                if (tb.labelText != null)
                {
                    tb.labelText.color = glyph;
                    tb.labelText.fontStyle = isActive ? FontStyles.Bold : FontStyles.Normal;
                }
            }

            if (sectionTitleText != null)
                sectionTitleText.text = "Budowanie";

            if (activeToolText != null)
                activeToolText.text = GetActiveToolSummary(currentTool);
        }

        private void BuildUI()
        {
            RectTransform root = GetComponent<RectTransform>();
            if (root == null)
                root = gameObject.AddComponent<RectTransform>();

            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(1f, 0f);
            root.pivot = new Vector2(0f, 0f);
            root.offsetMin = new Vector2(MainTabBarUI.PanelWidth, 0f);
            root.offsetMax = new Vector2(0f, DepotUILayout.BuildMenuHeight);

            panel = new GameObject("BuildMenuPanel");
            panel.transform.SetParent(transform, false);

            var panelRT = panel.AddComponent<RectTransform>();
            DepotUIPanelPrimitives.Stretch(panelRT);

            var panelImage = panel.AddComponent<Image>();
            UITheme.ApplySurface(panelImage, panelBg, UIShapePreset.Panel);

            var topDivider = new GameObject("TopDivider");
            topDivider.transform.SetParent(panel.transform, false);
            var dividerRT = topDivider.AddComponent<RectTransform>();
            dividerRT.anchorMin = new Vector2(0f, 1f);
            dividerRT.anchorMax = new Vector2(1f, 1f);
            dividerRT.pivot = new Vector2(0.5f, 1f);
            dividerRT.anchoredPosition = Vector2.zero;
            dividerRT.sizeDelta = new Vector2(0f, 1f);
            topDivider.AddComponent<Image>().color = UITheme.TopBarDivider;

            var layout = panel.AddComponent<HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            layout.spacing = UITheme.Spacing.Md;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            CreateSummaryCard();
            CreateToolButtonStrip();

            foreach (var (mode, icon, iconRes, label, chipHex, unlocked) in buildToolDefs)
                CreateToolButton(mode, icon, iconRes, label, chipHex, unlocked);
        }

        private void CreateSummaryCard()
        {
            var summaryObject = new GameObject("SummaryCard");
            summaryObject.transform.SetParent(panel.transform, false);
            summaryObject.AddComponent<RectTransform>();

            var summaryLayout = summaryObject.AddComponent<LayoutElement>();
            summaryLayout.preferredWidth = 176f;
            summaryLayout.preferredHeight = 56f;
            summaryLayout.flexibleWidth = 0f;

            var summaryImage = summaryObject.AddComponent<Image>();
            UITheme.ApplySurface(summaryImage, UITheme.TopBarInset, UIShapePreset.Inset);

            var verticalLayout = summaryObject.AddComponent<VerticalLayoutGroup>();
            verticalLayout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            verticalLayout.spacing = UITheme.Spacing.Xxs;
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.childAlignment = TextAnchor.MiddleLeft;

            sectionTitleText = DepotUIPanelPrimitives.CreateTMP("Title", summaryObject.transform, 11f, TextAlignmentOptions.Left, FontStyles.Bold);
            sectionTitleText.text = "Budowanie";
            sectionTitleText.color = UITheme.SecondaryText;
            sectionTitleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            activeToolText = DepotUIPanelPrimitives.CreateTMP("State", summaryObject.transform, 13f, TextAlignmentOptions.Left, FontStyles.Bold);
            activeToolText.text = "Wybierz narzedzie budowy";
            activeToolText.color = UITheme.PrimaryText;
            activeToolText.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        }

        private void CreateToolButtonStrip()
        {
            toolButtonContainer = new GameObject("ToolButtonStrip");
            toolButtonContainer.transform.SetParent(panel.transform, false);
            toolButtonContainer.AddComponent<RectTransform>();

            var stripLayout = toolButtonContainer.AddComponent<LayoutElement>();
            stripLayout.flexibleWidth = 1f;
            stripLayout.preferredHeight = 80f;

            var stripImage = toolButtonContainer.AddComponent<Image>();
            UITheme.ApplySurface(stripImage, UITheme.WithAlpha(UITheme.PrimarySurface, 0.72f), UIShapePreset.Inset);

            var layout = toolButtonContainer.AddComponent<HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs);
            layout.spacing = UITheme.Spacing.Sm;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;
        }

        private void CreateToolButton(ToolMode mode, string icon, string iconRes, string label, string chipHex, bool unlocked)
        {
            var buttonObject = new GameObject($"Build_{mode}");
            buttonObject.transform.SetParent(toolButtonContainer.transform, false);

            var buttonRt = buttonObject.AddComponent<RectTransform>();
            // M11 AS-3: cel guidance asystenta (np. "depot.tool.BuildTrack" dla kroku „kliknij TOR").
            RailwayManager.SharedUI.Assistant.AssistantHighlightTargets.Register($"depot.tool.{mode}", buttonRt);

            // Wariant B (MUI-11, CBM): tło przycisku = kolor kategorii (kafelek). Biały glif na wierzchu.
            Color chip = ColorUtility.TryParseHtmlString($"#{chipHex}", out var parsedChip) ? parsedChip : Color.magenta;
            var background = buttonObject.AddComponent<Image>();
            UITheme.ApplySurface(background, unlocked ? chip : UITheme.Darken(chip, 0.5f), UIShapePreset.Button);

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.interactable = unlocked;
            buttonObject.AddComponent<ToolbarButtonStates>(); // MUI-11: stany CBM (hover-skala + press); nazwa=tooltip, wybrany=UpdateVisuals

            // MUI-11: kwadratowy kafelek (styl CBM) zamiast szerokiego prostokąta. flexibleWidth=0 +
            // minWidth blokują rozciąganie przez layout — ikona wypełnia tile zamiast tonąć w pustce.
            var layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 88f;
            layoutElement.preferredHeight = 88f;
            layoutElement.minWidth = 88f;
            layoutElement.minHeight = 88f;
            layoutElement.flexibleWidth = 0f;
            layoutElement.flexibleHeight = 0f;

            var accentObject = new GameObject("Accent");
            accentObject.transform.SetParent(buttonObject.transform, false);
            var accentRT = accentObject.AddComponent<RectTransform>();
            accentRT.anchorMin = new Vector2(0f, 1f);
            accentRT.anchorMax = new Vector2(1f, 1f);
            accentRT.pivot = new Vector2(0.5f, 1f);
            accentRT.anchoredPosition = Vector2.zero;
            accentRT.sizeDelta = new Vector2(0f, 4f);
            var accent = accentObject.AddComponent<Image>();
            accent.color = Color.white; // MUI-11 Wariant B: biały pasek-akcent na kolorowym kafelku
            accentObject.SetActive(false);

            var verticalLayout = buttonObject.AddComponent<VerticalLayoutGroup>();
            verticalLayout.padding = UITheme.Padding(UITheme.Spacing.Xs, UITheme.Spacing.Xs);
            verticalLayout.spacing = UITheme.Spacing.Xxs;
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.childAlignment = TextAnchor.MiddleCenter;

            // MUI-11: gdy ikona dostępna → DUŻY glif przez standardowy Image (ikony to Texture2D sprite'y
            // z mipmapami+Trilinear, patrz UiIcons.BuildMipmappedSprite) + tooltip; gdy placeholder → glif TMP.
            TMP_Text iconText = null;
            Graphic iconImage = null;
            TMP_Text labelText = null;
            var iconSprite = UiIcons.Get(iconRes);
            if (iconSprite != null)
            {
                var iconObject = new GameObject("Icon", typeof(RectTransform));
                iconObject.transform.SetParent(buttonObject.transform, false);
                var img = iconObject.AddComponent<Image>();
                img.sprite = iconSprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
                iconImage = img;
                iconObject.AddComponent<LayoutElement>().preferredHeight = 80f; // CBM: duża ikona wypełnia kafelek
                UIBuilders.AttachTooltip(buttonObject, label); // nazwa trybu w tooltipie zamiast etykiety
            }
            else
            {
                iconText = DepotUIPanelPrimitives.CreateTMP("Icon", buttonObject.transform, 22, TextAlignmentOptions.Center, FontStyles.Normal);
                iconText.text = icon;
                iconText.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

                labelText = DepotUIPanelPrimitives.CreateTMP("Label", buttonObject.transform, 11, TextAlignmentOptions.Center, FontStyles.Normal);
                labelText.text = label;
                labelText.enableAutoSizing = true;
                labelText.fontSizeMin = 9;
                labelText.fontSizeMax = 11;
                labelText.textWrappingMode = TextWrappingModes.Normal;
                labelText.overflowMode = TextOverflowModes.Ellipsis;
                labelText.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;
            }

            var toolButton = new BuildToolButton
            {
                mode = mode,
                button = button,
                background = background,
                accent = accent,
                iconText = iconText,
                iconImage = iconImage,
                labelText = labelText,
                chipColor = chip,
                unlocked = unlocked
            };

            var captured = toolButton;
            button.onClick.AddListener(() => OnBuildToolClicked(captured));
            toolButtons.Add(toolButton);
        }

        private static string GetActiveToolSummary(ToolMode toolMode)
        {
            return toolMode switch
            {
                ToolMode.BuildTrack => "Aktywne: tory",
                ToolMode.BuildCatenary => "Aktywne: siec trakcyjna",
                ToolMode.BuildPath => "Aktywne: sciezki",
                ToolMode.BuildRoom => "Aktywne: pomieszczenia",
                ToolMode.Demolish => "Aktywne: wyburzanie",
                _ => "Wybierz narzedzie budowy"
            };
        }
    }
}
