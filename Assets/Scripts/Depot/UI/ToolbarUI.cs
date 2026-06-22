using System.Collections.Generic;
using TMPro;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Dolny pasek narzedzi. Generuje sie proceduralnie.
    /// Kazde narzedzie to przycisk z krotszym badge'em i etykieta.
    /// </summary>
    public class ToolbarUI : MonoBehaviour
    {
        [Header("Colors")]
        [SerializeField] private Color activeColor = default;
        [SerializeField] private Color unlockedColor = default;
        [SerializeField] private Color lockedColor = default;

        private class ToolButtonData
        {
            public ToolMode mode;
            public Button button;
            public TextMeshProUGUI iconText;
            public TextMeshProUGUI labelText;
            public Image background;
            public Image badgeBackground;
            public bool unlocked;
        }

        private readonly List<ToolButtonData> toolButtons = new();
        private DepotUIManager uiManager;

        private static readonly (ToolMode mode, string icon, string label, bool unlocked)[] toolDefs = new[]
        {
            (ToolMode.Select,         "SEL", "Zaznacz",       true),
            (ToolMode.BuildTrack,     "TOR", "Tor",           false),
            (ToolMode.BuildCatenary,  "SIE", "Siec",          false),
            (ToolMode.BuildPath,      "SCZ", "Sciezka",       false),
            (ToolMode.BuildRoom,      "POM", "Pomieszczenia", false),
            // ToolMode.Furniture jest aktywny przez RoomBuildPanelUI (klik na akcję meblową
            // w popupie pomieszczenia). Toolbar nie pokazuje osobnego przycisku — meble
            // są filtrowane per RoomType i wyświetlane wewnątrz BuildRoom panel.
            (ToolMode.Demolish,       "USN", "Usun",          false),
        };

        void Awake()
        {
            if (activeColor == default)
                activeColor = UITheme.PrimaryAccent;
            if (unlockedColor == default)
                unlockedColor = UITheme.SecondarySurface;
            if (lockedColor == default)
                lockedColor = UITheme.WithAlpha(UITheme.PrimarySurface, 0.55f);

            BuildUI();
        }

        void Start()
        {
            uiManager = DepotUIManager.Instance;

            foreach (var tb in toolButtons)
            {
                var captured = tb;
                if (tb.button != null)
                    tb.button.onClick.AddListener(() => OnToolButtonClicked(captured));

                tb.button.interactable = tb.unlocked;
            }

            if (uiManager != null)
                uiManager.OnToolChanged += OnToolChanged;

            UpdateButtonVisuals();
        }

        private void OnToolButtonClicked(ToolButtonData tb)
        {
            if (!tb.unlocked || uiManager == null)
                return;

            if (uiManager.CurrentTool == tb.mode)
                uiManager.CurrentTool = ToolMode.Select;
            else
                uiManager.CurrentTool = tb.mode;
        }

        private void OnToolChanged(ToolMode newMode)
        {
            UpdateButtonVisuals();
        }

        private void UpdateButtonVisuals()
        {
            foreach (var tb in toolButtons)
            {
                if (tb.button == null)
                    continue;

                bool isActive = uiManager != null && uiManager.CurrentTool == tb.mode;

                Color backgroundColor = isActive
                    ? activeColor
                    : tb.unlocked ? unlockedColor : lockedColor;

                if (tb.background != null)
                    UITheme.ApplySurface(tb.background, backgroundColor, isActive ? UIShapePreset.Pill : UIShapePreset.Button);

                if (tb.badgeBackground != null)
                {
                    Color badgeColor = isActive
                        ? UITheme.WithAlpha(UITheme.InverseText, 0.18f)
                        : tb.unlocked ? UITheme.WithAlpha(UITheme.Border, 0.35f) : UITheme.WithAlpha(UITheme.DisabledText, 0.18f);
                    UITheme.ApplySurface(tb.badgeBackground, badgeColor, UIShapePreset.Pill);
                }

                tb.button.interactable = tb.unlocked;
                tb.button.colors = UITheme.CreateColorBlock(
                    backgroundColor,
                    tb.unlocked ? (isActive ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface) : lockedColor,
                    tb.unlocked ? UITheme.Darken(isActive ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface, 0.12f) : lockedColor,
                    backgroundColor,
                    UITheme.WithAlpha(UITheme.Border, 0.55f));

                UIThemeTextRole iconRole;
                UIThemeTextRole labelRole;
                if (isActive)
                {
                    iconRole = UIThemeTextRole.Inverse;
                    labelRole = UIThemeTextRole.Inverse;
                }
                else if (tb.unlocked)
                {
                    iconRole = UIThemeTextRole.Primary;
                    labelRole = UIThemeTextRole.Secondary;
                }
                else
                {
                    iconRole = UIThemeTextRole.Disabled;
                    labelRole = UIThemeTextRole.Disabled;
                }

                if (tb.iconText != null)
                {
                    UITheme.ApplyTmpText(tb.iconText, iconRole);
                    tb.iconText.fontStyle = FontStyles.Bold;
                }

                if (tb.labelText != null)
                {
                    UITheme.ApplyTmpText(tb.labelText, labelRole);
                    tb.labelText.fontStyle = isActive ? FontStyles.Bold : FontStyles.Normal;
                }
            }
        }

        public void UnlockTool(ToolMode mode)
        {
            var tb = toolButtons.Find(t => t.mode == mode);
            if (tb == null)
                return;

            tb.unlocked = true;
            if (tb.button != null)
                tb.button.interactable = true;

            UpdateButtonVisuals();
        }

        public void LockTool(ToolMode mode)
        {
            var tb = toolButtons.Find(t => t.mode == mode);
            if (tb == null)
                return;

            tb.unlocked = false;
            if (tb.button != null)
                tb.button.interactable = false;

            if (uiManager != null && uiManager.CurrentTool == mode)
                uiManager.CurrentTool = ToolMode.Select;

            UpdateButtonVisuals();
        }

        void OnDestroy()
        {
            if (uiManager != null)
                uiManager.OnToolChanged -= OnToolChanged;
        }

        private void BuildUI()
        {
            RectTransform rt = GetComponent<RectTransform>();
            if (rt == null)
                rt = gameObject.AddComponent<RectTransform>();

            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 68f);

            Image bg = GetComponent<Image>();
            if (bg == null)
                bg = gameObject.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.TopBarBackground, 0.96f), UIShapePreset.PanelLarge);

            HorizontalLayoutGroup rootLayout = gameObject.AddComponent<HorizontalLayoutGroup>();
            rootLayout.spacing = UITheme.Spacing.Md;
            rootLayout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            rootLayout.childAlignment = TextAnchor.MiddleCenter;
            rootLayout.childForceExpandWidth = false;
            rootLayout.childForceExpandHeight = true;
            rootLayout.childControlWidth = false;
            rootLayout.childControlHeight = true;

            CreateFlexSpacer(transform);

            GameObject toolRail = CreateCard(transform, "ToolRail", UITheme.WithAlpha(UITheme.SecondarySurface, 0.35f), UIShapePreset.Panel);
            LayoutElement railLE = toolRail.AddComponent<LayoutElement>();
            railLE.minHeight = 52f;

            HorizontalLayoutGroup railLayout = toolRail.AddComponent<HorizontalLayoutGroup>();
            railLayout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            railLayout.spacing = UITheme.Spacing.Md;
            railLayout.childAlignment = TextAnchor.MiddleCenter;
            railLayout.childForceExpandWidth = false;
            railLayout.childForceExpandHeight = true;
            railLayout.childControlWidth = false;
            railLayout.childControlHeight = true;

            foreach (var def in toolDefs)
            {
                var tb = CreateToolButton(toolRail.transform, def.mode, def.icon, def.label, def.unlocked);
                toolButtons.Add(tb);
            }

            CreateFlexSpacer(transform);
        }

        private ToolButtonData CreateToolButton(Transform parent, ToolMode mode, string icon, string label, bool unlocked)
        {
            GameObject obj = CreateCard(parent, $"Tool_{mode}", unlocked ? unlockedColor : lockedColor, UIShapePreset.Button);

            RectTransform rt = obj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(74f, 52f);

            LayoutElement le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = 74f;

            Button btn = obj.AddComponent<Button>();
            btn.targetGraphic = obj.GetComponent<Image>();
            btn.interactable = unlocked;

            VerticalLayoutGroup layout = obj.AddComponent<VerticalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Sm);
            layout.spacing = UITheme.Spacing.Xs;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            GameObject badgeObj = CreateCard(obj.transform, "Badge", UITheme.WithAlpha(UITheme.Border, 0.35f), UIShapePreset.Pill);
            LayoutElement badgeLE = badgeObj.AddComponent<LayoutElement>();
            badgeLE.preferredWidth = 42f;
            badgeLE.preferredHeight = 20f;

            GameObject iconObj = CreateTextObject(badgeObj.transform, "Icon", icon, 10, FontStyles.Bold, TextAlignmentOptions.Center, UIThemeTextRole.Primary);
            RectTransform iconRt = iconObj.GetComponent<RectTransform>();
            iconRt.anchorMin = Vector2.zero;
            iconRt.anchorMax = Vector2.one;
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;

            GameObject labelObj = CreateTextObject(obj.transform, "Label", label, 9, FontStyles.Normal, TextAlignmentOptions.Center, UIThemeTextRole.Secondary);
            var labelTmp = labelObj.GetComponent<TextMeshProUGUI>();
            labelTmp.enableAutoSizing = true;
            labelTmp.fontSizeMin = 8;
            labelTmp.fontSizeMax = 9;
            LayoutElement labelLE = labelObj.AddComponent<LayoutElement>();
            labelLE.preferredHeight = 14f;

            return new ToolButtonData
            {
                mode = mode,
                button = btn,
                iconText = iconObj.GetComponent<TextMeshProUGUI>(),
                labelText = labelObj.GetComponent<TextMeshProUGUI>(),
                background = obj.GetComponent<Image>(),
                badgeBackground = badgeObj.GetComponent<Image>(),
                unlocked = unlocked
            };
        }

        private void CreateFlexSpacer(Transform parent)
        {
            GameObject spacer = new GameObject("FlexSpacer");
            spacer.transform.SetParent(parent, false);
            spacer.AddComponent<RectTransform>().sizeDelta = new Vector2(10f, 52f);
            LayoutElement le = spacer.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
        }

        private GameObject CreateCard(Transform parent, string name, Color color, UIShapePreset shape)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();

            Image image = obj.AddComponent<Image>();
            UITheme.ApplySurface(image, color, shape);
            return obj;
        }

        private GameObject CreateTextObject(Transform parent, string name, string text, int fontSize, FontStyles fontStyle, TextAlignmentOptions alignment, UIThemeTextRole role)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();

            TextMeshProUGUI label = obj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(label, role);
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = fontStyle;
            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return obj;
        }
    }
}
