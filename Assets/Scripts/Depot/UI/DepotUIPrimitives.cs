using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;

namespace DepotSystem
{
    /// <summary>
    /// Shared layout consts dla Depot UI (sub-toolbar geometry, build menu height).
    /// Wcześniej żyło w <c>MainTabBarUI.cs</c>; wyciągnięte 2026-05-13 — używane też
    /// przez 7 innych UI komponentów (BuildMenuUI, RoomBuildPanelUI, 4 sub-toolbary,
    /// PairSecondaryToolbarUI).
    /// </summary>
    internal static class DepotUILayout
    {
        public const float BuildMenuHeight = 108f; // MUI-11: wyższy pasek pod większe kwadratowe kafle (styl CBM)
        public const float SubToolbarHeight = 52f;
        public const float SubToolbarBottom = BuildMenuHeight;
        public const float SubToolbarTop = BuildMenuHeight + SubToolbarHeight;
        public const float FloatingPanelBottom = SubToolbarTop + 8f;
    }

    /// <summary>
    /// DTO trzymający komponenty proceduralnie zbudowanego button'u (background + accent
    /// + icon + label). Używany przez sub-toolbary i panele opcji w Depot.
    /// </summary>
    internal sealed class DepotOptionButtonParts
    {
        public Button Button;
        public Image Background;
        public Image Accent;
        public TMP_Text IconText;
        /// <summary>
        /// MUI-11: ustawiony zamiast <see cref="IconText"/> gdy przycisk dostał sprite ikony.
        /// Monochromatyczna maska tintowana per stan (active/hover/locked) jak glif TMP.
        /// Dokładnie jeden z (IconText, IconImage) jest nie-null.
        /// </summary>
        public Image IconImage;
        public TMP_Text LabelText;
    }

    /// <summary>
    /// Static helpers dla procedural Depot UI (TMP creation, button builders, color
    /// blocks, layout stretching). Konsumenci: MainTabBarUI, BuildMenuUI, RoomBuildPanelUI,
    /// 4 sub-toolbary, PairSecondaryToolbarUI.
    ///
    /// 2026-05-13: wyciągnięte z <c>MainTabBarUI.cs</c> do osobnego pliku — wcześniej
    /// nested static class w pliku zarządzającym tylko tab barem, mimo że używane jest
    /// w 8 miejscach.
    /// </summary>
    internal static class DepotUIPanelPrimitives
    {
        /// <summary>
        /// TMP creator dla Depot UI — delegate do <see cref="UIPrimitives.MakeTMP"/> (Krok 1 adoption pass 2026-05-15).
        /// Wcześniej duplikat tej samej logiki w <c>MenuScreenPrimitives</c>.
        /// </summary>
        public static TMP_Text CreateTMP(string name, Transform parent, float fontSize, TextAlignmentOptions alignment, FontStyles style)
            => UIPrimitives.MakeTMP(name, parent, fontSize, UIThemeTextRole.Primary, alignment, style);

        public static ColorBlock CreateButtonColors(Color normal, Color highlighted, Color pressed)
        {
            return UITheme.CreateColorBlock(
                normal,
                highlighted,
                pressed,
                normal,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
        }

        public static Color GetIconColor(bool active, bool unlocked)
        {
            if (!unlocked)
                return UITheme.DisabledText;

            return active ? UITheme.InverseText : UITheme.PrimaryText;
        }

        public static Color GetLabelColor(bool active, bool unlocked)
        {
            if (!unlocked)
                return UITheme.DisabledText;

            return active ? UITheme.InverseText : UITheme.SecondaryText;
        }

        /// <summary>Rozciąga RectTransform na cały parent — delegate do <see cref="UIPrimitives.Stretch"/>.</summary>
        public static void Stretch(RectTransform rectTransform) => UIPrimitives.Stretch(rectTransform);

        public static void ConfigureSubToolbarRoot(RectTransform rectTransform)
        {
            if (rectTransform == null)
                return;

            rectTransform.anchorMin = new Vector2(0f, 0f);
            rectTransform.anchorMax = new Vector2(1f, 0f);
            rectTransform.pivot = new Vector2(0f, 0f);
            rectTransform.offsetMin = new Vector2(MainTabBarUI.PanelWidth, DepotUILayout.SubToolbarBottom);
            rectTransform.offsetMax = new Vector2(0f, DepotUILayout.SubToolbarTop);
        }

        public static GameObject CreateHorizontalPanel(Transform parent, string name, RectOffset padding, float spacing)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);

            var panelRT = panel.AddComponent<RectTransform>();
            Stretch(panelRT);

            var background = panel.AddComponent<Image>();
            UITheme.ApplySurface(background, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.Panel);
            CreateDivider(panel.transform, true);

            var layout = panel.AddComponent<HorizontalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            return panel;
        }

        public static GameObject CreateVerticalPanel(Transform parent, string name, RectOffset padding, float spacing)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);

            var panelRT = panel.AddComponent<RectTransform>();
            Stretch(panelRT);

            var background = panel.AddComponent<Image>();
            UITheme.ApplySurface(background, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.Panel);

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;

            return panel;
        }

        /// <param name="iconSprite">
        /// MUI-11 (opcjonalne): gdy podane, ikona renderuje się jako tintowalny <see cref="Image"/>
        /// zamiast 3-literowego glifu TMP <paramref name="icon"/>. <paramref name="icon"/> zostaje
        /// jako fallback gdy sprite == null. Wstecznie kompatybilne — istniejące call-site bez
        /// sprite'a działają bez zmian.
        /// </param>
        public static DepotOptionButtonParts CreateOptionButton(
            Transform parent,
            string objectName,
            string icon,
            string label,
            float width,
            float height = 48f,
            Sprite iconSprite = null)
        {
            var buttonObject = new GameObject(objectName);
            buttonObject.transform.SetParent(parent, false);

            buttonObject.AddComponent<RectTransform>();
            var background = buttonObject.AddComponent<Image>();
            UITheme.ApplySurface(background, UITheme.SecondarySurface, UIShapePreset.Button);

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            buttonObject.AddComponent<ButtonPressFeedback>(); // MUI-11: subtelny press feel

            var layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = width;
            layoutElement.preferredHeight = height;
            layoutElement.minWidth = width;
            layoutElement.minHeight = height;

            var accentObject = new GameObject("Accent");
            accentObject.transform.SetParent(buttonObject.transform, false);
            var accentRT = accentObject.AddComponent<RectTransform>();
            accentRT.anchorMin = new Vector2(0f, 1f);
            accentRT.anchorMax = new Vector2(1f, 1f);
            accentRT.pivot = new Vector2(0.5f, 1f);
            accentRT.anchoredPosition = Vector2.zero;
            accentRT.sizeDelta = new Vector2(0f, 4f);
            var accent = accentObject.AddComponent<Image>();
            accent.color = UITheme.PrimaryAccent;
            accentObject.SetActive(false);

            var verticalLayout = buttonObject.AddComponent<VerticalLayoutGroup>();
            verticalLayout.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Sm, UITheme.Spacing.Sm, UITheme.Spacing.Xs);
            verticalLayout.spacing = UITheme.Spacing.Xxs;
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.childAlignment = TextAnchor.MiddleCenter;

            TMP_Text iconText = null;
            Image iconImage = null;
            if (iconSprite != null)
            {
                var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(buttonObject.transform, false);
                iconImage = iconObject.GetComponent<Image>();
                iconImage.sprite = iconSprite;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false; // pointer events idą do button parent
                iconImage.color = Color.white; // MUI-11: kolorowe sprite'y = pełen kolor (BEZ hue-tintu); ApplyOptionButtonState dim'uje tylko locked
                iconObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            }
            else
            {
                iconText = CreateTMP("Icon", buttonObject.transform, 18f, TextAlignmentOptions.Center, FontStyles.Normal);
                iconText.text = icon;
                iconText.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
            }

            var labelText = CreateTMP("Label", buttonObject.transform, 10f, TextAlignmentOptions.Center, FontStyles.Normal);
            labelText.text = label;
            labelText.enableAutoSizing = true;
            labelText.fontSizeMin = 8;
            labelText.fontSizeMax = 10;
            labelText.textWrappingMode = TextWrappingModes.Normal;
            labelText.overflowMode = TextOverflowModes.Ellipsis;
            labelText.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            return new DepotOptionButtonParts
            {
                Button = button,
                Background = background,
                Accent = accent,
                IconText = iconText,
                IconImage = iconImage,
                LabelText = labelText
            };
        }

        public static DepotOptionButtonParts CreateListButton(Transform parent, string objectName, string label, float height = 32f)
        {
            var buttonObject = new GameObject(objectName);
            buttonObject.transform.SetParent(parent, false);

            buttonObject.AddComponent<RectTransform>();
            var background = buttonObject.AddComponent<Image>();
            UITheme.ApplySurface(background, UITheme.SecondarySurface, UIShapePreset.Button);

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            var layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = height;
            layoutElement.minHeight = height;

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

            var labelText = CreateTMP("Label", buttonObject.transform, 12f, TextAlignmentOptions.Left, FontStyles.Normal);
            labelText.text = label;
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            labelText.overflowMode = TextOverflowModes.Ellipsis;
            var labelRT = labelText.rectTransform;
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = new Vector2(12f, 0f);
            labelRT.offsetMax = new Vector2(-8f, 0f);

            return new DepotOptionButtonParts
            {
                Button = button,
                Background = background,
                Accent = accent,
                LabelText = labelText
            };
        }

        public static TMP_Text CreateSectionHeader(Transform parent, string objectName, string label, float height, TextAlignmentOptions alignment)
        {
            var headerObject = new GameObject(objectName);
            headerObject.transform.SetParent(parent, false);
            headerObject.AddComponent<RectTransform>();
            headerObject.AddComponent<LayoutElement>().preferredHeight = height;
            var background = headerObject.AddComponent<Image>();
            UITheme.ApplySurface(background, UITheme.TopBarInset, UIShapePreset.Inset);

            var text = CreateTMP("Text", headerObject.transform, 11f, alignment, FontStyles.Bold);
            text.text = label;
            text.color = UITheme.SecondaryText;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(10f, 0f);
            text.rectTransform.offsetMax = new Vector2(-10f, 0f);
            return text;
        }

        public static void ApplyOptionButtonState(DepotOptionButtonParts parts, bool active, bool unlocked, Color activeColor, Color normalColor, Color lockedColor)
        {
            if (parts == null)
                return;

            var background = active ? activeColor : unlocked ? normalColor : lockedColor;

            if (parts.Background != null)
                parts.Background.color = background;

            if (parts.Button != null)
            {
                parts.Button.interactable = unlocked;
                parts.Button.colors = CreateButtonColors(
                    background,
                    active ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface,
                    active ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border);
            }

            if (parts.Accent != null)
                parts.Accent.gameObject.SetActive(active);

            if (parts.IconText != null)
                parts.IconText.color = GetIconColor(active, unlocked);

            if (parts.IconImage != null)
                // MUI-11: kolorowa ikona zachowuje pełen kolor (active/hover sygnalizuje tło+accent+label),
                // tylko locked przygaszamy alpha. NIE hue-tint (zabiłby wielokolorowość).
                parts.IconImage.color = unlocked ? Color.white : new Color(1f, 1f, 1f, 0.35f);

            if (parts.LabelText != null)
            {
                parts.LabelText.color = GetLabelColor(active, unlocked);
                parts.LabelText.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        private static void CreateDivider(Transform parent, bool topAligned)
        {
            var divider = new GameObject("Divider");
            divider.transform.SetParent(parent, false);

            var dividerRT = divider.AddComponent<RectTransform>();
            dividerRT.anchorMin = new Vector2(0f, topAligned ? 1f : 0f);
            dividerRT.anchorMax = new Vector2(1f, topAligned ? 1f : 0f);
            dividerRT.pivot = new Vector2(0.5f, topAligned ? 1f : 0f);
            dividerRT.anchoredPosition = Vector2.zero;
            dividerRT.sizeDelta = new Vector2(0f, 1f);

            divider.AddComponent<Image>().color = UITheme.TopBarDivider;
        }
    }
}
