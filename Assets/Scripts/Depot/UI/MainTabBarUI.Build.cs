using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    /// <summary>
    /// Partial: procedural BuildUI (root rect + tla + tabs container) + CreateHeaderCard
    /// + CreateTabButton. Wywoływane raz w Awake.
    /// </summary>
    public partial class MainTabBarUI
    {
        private void BuildUI()
        {
            RectTransform rt = GetComponent<RectTransform>();
            if (rt == null)
                rt = gameObject.AddComponent<RectTransform>();

            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(PanelWidth, -52f);

            Image background = GetComponent<Image>();
            if (background == null)
                background = gameObject.AddComponent<Image>();
            UITheme.ApplySurface(background, bgColor, UIShapePreset.PanelLarge);

            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Md);
            layout.spacing = UITheme.Spacing.Xs;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;

            CreateHeaderCard();

            foreach (var (tab, icon, label, unlocked, hasSubmenu) in tabDefs)
                CreateTabButton(tab, icon, label, unlocked, hasSubmenu);
        }

        private void CreateHeaderCard()
        {
            var headerObject = new GameObject("HeaderCard");
            headerObject.transform.SetParent(transform, false);
            headerObject.AddComponent<RectTransform>();

            var layoutElement = headerObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 56f;

            var background = headerObject.AddComponent<Image>();
            UITheme.ApplySurface(background, UITheme.TopBarInset, UIShapePreset.Inset);

            var verticalLayout = headerObject.AddComponent<VerticalLayoutGroup>();
            verticalLayout.padding = UITheme.Padding(UITheme.Spacing.Sm);
            verticalLayout.spacing = UITheme.Spacing.Xxs;
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.childAlignment = TextAnchor.MiddleCenter;

            headerTitleText = DepotUIPanelPrimitives.CreateTMP("Title", headerObject.transform, 10f, TextAlignmentOptions.Center, FontStyles.Bold);
            headerTitleText.text = "Nawigacja";
            headerTitleText.color = UITheme.SecondaryText;
            headerTitleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;

            headerStateText = DepotUIPanelPrimitives.CreateTMP("State", headerObject.transform, 10f, TextAlignmentOptions.Center, FontStyles.Bold);
            headerStateText.text = "Tryb: wybor";
            headerStateText.enableAutoSizing = true;
            headerStateText.fontSizeMin = 8f;
            headerStateText.fontSizeMax = 10f;
            headerStateText.textWrappingMode = TextWrappingModes.Normal;
            headerStateText.color = UITheme.PrimaryText;
            headerStateText.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        }

        private void CreateTabButton(MainTab tab, string icon, string label, bool unlocked, bool hasSubmenu)
        {
            var buttonObject = new GameObject($"Tab_{tab}");
            buttonObject.transform.SetParent(transform, false);

            var tabRt = buttonObject.AddComponent<RectTransform>();
            // M11 AS-3: cel guidance asystenta (np. "depot.tab.Fleet" dla kroku „otwórz Tabor").
            RailwayManager.SharedUI.Assistant.AssistantHighlightTargets.Register($"depot.tab.{tab}", tabRt);
            var background = buttonObject.AddComponent<Image>();
            UITheme.ApplySurface(background, unlocked ? normalColor : lockedColor, UIShapePreset.Tab);

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.interactable = unlocked;
            buttonObject.AddComponent<ToolbarButtonStates>(); // MUI-11: stany CBM (hover-skala + press); etykieta już na kaflu → bez pigułki

            var layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 62f;
            layoutElement.minHeight = 62f;

            var accentObject = new GameObject("Accent");
            accentObject.transform.SetParent(buttonObject.transform, false);
            var accentRT = accentObject.AddComponent<RectTransform>();
            accentRT.anchorMin = new Vector2(0f, 0f);
            accentRT.anchorMax = new Vector2(0f, 1f);
            accentRT.pivot = new Vector2(0f, 0.5f);
            accentRT.anchoredPosition = Vector2.zero;
            accentRT.sizeDelta = new Vector2(4f, 0f);
            var accent = accentObject.AddComponent<Image>();
            accent.color = UITheme.PrimaryAccent;
            accentObject.SetActive(false);

            var iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(buttonObject.transform, false);
            var iconRT = iconObject.AddComponent<RectTransform>();
            iconRT.anchorMin = new Vector2(0f, 0.42f);
            iconRT.anchorMax = new Vector2(1f, 1f);
            iconRT.offsetMin = new Vector2(8f, 0f);
            iconRT.offsetMax = new Vector2(-8f, 0f);

            var iconText = DepotUIPanelPrimitives.CreateTMP("Glyph", iconObject.transform, 22, TextAlignmentOptions.Center, FontStyles.Normal);
            iconText.text = icon;
            DepotUIPanelPrimitives.Stretch(iconText.rectTransform);

            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            var labelRT = labelObject.AddComponent<RectTransform>();
            labelRT.anchorMin = new Vector2(0f, 0f);
            labelRT.anchorMax = new Vector2(1f, 0.42f);
            labelRT.offsetMin = new Vector2(8f, 0f);
            labelRT.offsetMax = new Vector2(-8f, 0f);

            var labelText = DepotUIPanelPrimitives.CreateTMP("Text", labelObject.transform, 10, TextAlignmentOptions.Center, FontStyles.Normal);
            labelText.text = label;
            labelText.textWrappingMode = TextWrappingModes.Normal;
            labelText.overflowMode = TextOverflowModes.Ellipsis;
            labelText.enableAutoSizing = true;
            labelText.fontSizeMin = 8;
            labelText.fontSizeMax = 10;
            DepotUIPanelPrimitives.Stretch(labelText.rectTransform);

            var tabButton = new TabButton
            {
                tab = tab,
                button = button,
                background = background,
                accent = accent,
                iconText = iconText,
                labelText = labelText,
                unlocked = unlocked,
                hasSubmenu = hasSubmenu
            };

            var captured = tabButton;
            button.onClick.AddListener(() => OnTabClicked(captured));
            tabButtons.Add(tabButton);
        }
    }
}
