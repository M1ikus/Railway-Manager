using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace DepotSystem
{
    /// <summary>
    /// Partial: procedural BuildUI (Hero card + TypeSection z scrollowanym listingiem +
    /// SelectedCard preview + BottomRow z Cancel/Confirm) + CreateTypeButton per RoomType
    /// + widget helpers (CreateTextObj / CreateSectionCard / CreateButton).
    /// </summary>
    public partial class RoomTypePopupUI
    {
        private void BuildUI()
        {
            var canvas = DepotUIManager.Instance != null ? DepotUIManager.Instance.canvas : null;
            if (canvas == null)
                return;

            popupPanel = new GameObject("RoomTypePopup");
            popupPanel.transform.SetParent(canvas.transform, false);
            var panelRT = popupPanel.AddComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.5f, 0.5f);
            panelRT.anchorMax = new Vector2(0.5f, 0.5f);
            panelRT.pivot = new Vector2(0.5f, 0.5f);
            panelRT.sizeDelta = new Vector2(456f, 584f);

            var panelImg = popupPanel.AddComponent<Image>();
            UITheme.ApplySurface(panelImg, panelColor, UIShapePreset.PanelLarge);

            var rootLayout = popupPanel.AddComponent<VerticalLayoutGroup>();
            rootLayout.padding = UITheme.Padding(UITheme.Spacing.Lg);
            rootLayout.spacing = UITheme.Spacing.Md;
            rootLayout.childForceExpandWidth = true;
            rootLayout.childForceExpandHeight = false;

            var heroCard = CreateSectionCard(
                popupPanel.transform,
                "HeroCard",
                headerColor,
                UIShapePreset.Panel,
                UITheme.Padding(UITheme.Spacing.Lg),
                UITheme.Spacing.Xs);
            var heroLE = heroCard.AddComponent<LayoutElement>();
            heroLE.preferredHeight = 94f;

            var headerObj = CreateTextObj(heroCard.transform, "Header", LocalizationService.Get("popup_room_type.header_default"), 18, TextAlignmentOptions.Center);
            titleText = headerObj.GetComponent<TextMeshProUGUI>();
            titleText.fontStyle = FontStyles.Bold;
            var headerLE = headerObj.AddComponent<LayoutElement>();
            headerLE.preferredHeight = 34f;

            var sizeObj = CreateTextObj(heroCard.transform, "SizeInfo", string.Empty, 14, TextAlignmentOptions.Center);
            sizeInfoText = sizeObj.GetComponent<TextMeshProUGUI>();
            sizeInfoText.color = UITheme.SecondaryText;
            var sizeLE = sizeObj.AddComponent<LayoutElement>();
            sizeLE.preferredHeight = 24f;

            var promptObj = CreateTextObj(heroCard.transform, "Prompt", LocalizationService.Get("popup_room_type.select_prompt"), 12, TextAlignmentOptions.Center);
            promptObj.GetComponent<TextMeshProUGUI>().color = UITheme.SecondaryText;
            var promptLE = promptObj.AddComponent<LayoutElement>();
            promptLE.preferredHeight = 18f;

            var typeSection = CreateSectionCard(
                popupPanel.transform,
                "TypeSection",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.35f),
                UIShapePreset.Panel,
                UITheme.Padding(UITheme.Spacing.Md),
                UITheme.Spacing.Sm);
            var typeSectionLE = typeSection.AddComponent<LayoutElement>();
            typeSectionLE.flexibleHeight = 1f;
            typeSectionLE.preferredHeight = 346f;

            var sectionTitle = CreateTextObj(typeSection.transform, "SectionTitle", LocalizationService.Get("popup_room_type.select_prompt"), 13, TextAlignmentOptions.MidlineLeft);
            sectionTitle.GetComponent<TextMeshProUGUI>().fontStyle = FontStyles.Bold;
            var sectionTitleLE = sectionTitle.AddComponent<LayoutElement>();
            sectionTitleLE.preferredHeight = 22f;

            var scrollObj = new GameObject("TypeScroll");
            scrollObj.transform.SetParent(typeSection.transform, false);
            scrollObj.AddComponent<RectTransform>();
            var scrollLE = scrollObj.AddComponent<LayoutElement>();
            scrollLE.flexibleHeight = 1f;
            scrollLE.preferredHeight = 304f;

            var scrollRect = scrollObj.AddComponent<ScrollRect>();
            scrollRect.vertical = true;
            scrollRect.horizontal = false;
            var scrollImg = scrollObj.AddComponent<Image>();
            UITheme.ApplySurface(scrollImg, UITheme.TopBarInset, UIShapePreset.Inset);
            var scrollMask = scrollObj.AddComponent<Mask>();
            scrollMask.showMaskGraphic = true;

            var contentObj = new GameObject("Content");
            contentObj.transform.SetParent(scrollObj.transform, false);
            var contentRT = contentObj.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.sizeDelta = Vector2.zero;

            var contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = UITheme.Padding(UITheme.Spacing.Sm);
            contentLayout.spacing = UITheme.Spacing.Sm;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            var contentFitter = contentObj.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = contentRT;
            buttonContainer = contentObj.transform;

            foreach (var (type, icon, labelKey) in roomTypeDefs)
            {
                CreateTypeButton(contentObj.transform, type, icon, labelKey);
            }

            var selectedCardObj = CreateSectionCard(
                popupPanel.transform,
                "SelectedCard",
                UITheme.TopBarInset,
                UIShapePreset.Inset,
                UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md),
                UITheme.Spacing.Xs);
            selectedTypeCard = selectedCardObj.GetComponent<Image>();
            var selectedCardLE = selectedCardObj.AddComponent<LayoutElement>();
            selectedCardLE.preferredHeight = 68f;

            var selectedObj = CreateTextObj(selectedCardObj.transform, "SelectedType", LocalizationService.Get("popup_room_type.select_prompt"), 14, TextAlignmentOptions.Center);
            selectedTypeText = selectedObj.GetComponent<TextMeshProUGUI>();
            var selectedTextLE = selectedObj.AddComponent<LayoutElement>();
            selectedTextLE.preferredHeight = 20f;

            var selectedMetaObj = CreateTextObj(selectedCardObj.transform, "SelectedTypeMeta", string.Empty, 11, TextAlignmentOptions.Center);
            selectedTypeMetaText = selectedMetaObj.GetComponent<TextMeshProUGUI>();
            selectedTypeMetaText.color = UITheme.SecondaryText;
            var selectedMetaLE = selectedMetaObj.AddComponent<LayoutElement>();
            selectedMetaLE.preferredHeight = 16f;

            var bottomRow = new GameObject("BottomRow");
            bottomRow.transform.SetParent(popupPanel.transform, false);
            bottomRow.AddComponent<RectTransform>();
            var bottomImg = bottomRow.AddComponent<Image>();
            UITheme.ApplySurface(bottomImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.35f), UIShapePreset.Panel);
            var bottomLE = bottomRow.AddComponent<LayoutElement>();
            bottomLE.preferredHeight = 58f;

            var bottomLayout = bottomRow.AddComponent<HorizontalLayoutGroup>();
            bottomLayout.padding = UITheme.Padding(UITheme.Spacing.Md);
            bottomLayout.spacing = UITheme.Spacing.Sm;
            bottomLayout.childForceExpandWidth = true;
            bottomLayout.childForceExpandHeight = true;

            cancelButton = CreateButton(bottomRow.transform, LocalizationService.Get("popup_room_type.btn_cancel"), buttonColor, ClosePopup);
            confirmButton = CreateButton(bottomRow.transform, LocalizationService.Get("popup_room_type.btn_confirm"), confirmColor, OnConfirm);
            confirmButton.interactable = false;

            popupPanel.SetActive(false);
        }

        private void CreateTypeButton(Transform parent, RoomType type, string icon, string labelKey)
        {
            var btnObj = new GameObject($"Btn_{type}");
            btnObj.transform.SetParent(parent, false);
            btnObj.AddComponent<RectTransform>();

            var btnImg = btnObj.AddComponent<Image>();
            UITheme.ApplySurface(btnImg, buttonColor, UIShapePreset.Button);

            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = btnImg;
            btn.colors = UITheme.CreateColorBlock(buttonColor, buttonHoverColor, UITheme.Border, buttonColor, lockedButtonColor);

            var btnLE = btnObj.AddComponent<LayoutElement>();
            btnLE.preferredHeight = 56f;

            var layout = btnObj.AddComponent<HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            layout.spacing = UITheme.Spacing.Md;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;

            var iconBadge = new GameObject("IconBadge");
            iconBadge.transform.SetParent(btnObj.transform, false);
            iconBadge.AddComponent<RectTransform>();
            var iconBadgeImage = iconBadge.AddComponent<Image>();
            UITheme.ApplySurface(iconBadgeImage, UITheme.WithAlpha(UITheme.Border, 0.35f), UIShapePreset.Pill);
            var iconBadgeLE = iconBadge.AddComponent<LayoutElement>();
            iconBadgeLE.preferredWidth = 44f;
            iconBadgeLE.preferredHeight = 34f;

            var iconObj = CreateTextObj(iconBadge.transform, "Icon", icon, 15, TextAlignmentOptions.Center);
            var iconRT = iconObj.GetComponent<RectTransform>();
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;

            var labelColumn = new GameObject("LabelColumn");
            labelColumn.transform.SetParent(btnObj.transform, false);
            labelColumn.AddComponent<RectTransform>();
            var labelColumnLE = labelColumn.AddComponent<LayoutElement>();
            labelColumnLE.flexibleWidth = 1f;

            var labelLayout = labelColumn.AddComponent<VerticalLayoutGroup>();
            labelLayout.spacing = UITheme.Spacing.Xxs;
            labelLayout.childForceExpandWidth = true;
            labelLayout.childForceExpandHeight = false;
            labelLayout.childAlignment = TextAnchor.MiddleLeft;

            var nameObj = CreateTextObj(labelColumn.transform, "Label", LocalizationService.Get(labelKey), 13, TextAlignmentOptions.MidlineLeft);
            var nameText = nameObj.GetComponent<TextMeshProUGUI>();
            nameText.fontStyle = FontStyles.Bold;
            var nameLE = nameObj.AddComponent<LayoutElement>();
            nameLE.preferredHeight = 18f;

            var sizeObj = CreateTextObj(labelColumn.transform, "SizeReq", string.Empty, 11, TextAlignmentOptions.MidlineLeft);
            var sizeLE = sizeObj.AddComponent<LayoutElement>();
            sizeLE.preferredHeight = 16f;

            var statusBadge = new GameObject("StatusBadge");
            statusBadge.transform.SetParent(btnObj.transform, false);
            statusBadge.AddComponent<RectTransform>();
            var statusBadgeImage = statusBadge.AddComponent<Image>();
            UITheme.ApplySurface(statusBadgeImage, UITheme.WithAlpha(validColor, 0.24f), UIShapePreset.Pill);
            var statusBadgeLE = statusBadge.AddComponent<LayoutElement>();
            statusBadgeLE.preferredWidth = 74f;
            statusBadgeLE.preferredHeight = 28f;

            var statusObj = CreateTextObj(statusBadge.transform, "Label", "PASUJE", 10, TextAlignmentOptions.Center);
            var statusText = statusObj.GetComponent<TextMeshProUGUI>();
            statusText.fontStyle = FontStyles.Bold;
            statusText.color = validColor;
            var statusRT = statusObj.GetComponent<RectTransform>();
            statusRT.anchorMin = Vector2.zero;
            statusRT.anchorMax = Vector2.one;
            statusRT.offsetMin = Vector2.zero;
            statusRT.offsetMax = Vector2.zero;

            var capturedType = type;
            btn.onClick.AddListener(() =>
            {
                selectedType = capturedType;
                UpdateTypeButtons();
            });
        }

        private GameObject CreateTextObj(Transform parent, string name, string text, int fontSize, TextAlignmentOptions alignment)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();

            var txt = obj.AddComponent<TextMeshProUGUI>();
            txt.text = text;
            UITheme.ApplyTmpText(txt, UIThemeTextRole.Primary);
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.textWrappingMode = TextWrappingModes.NoWrap;

            return obj;
        }

        private GameObject CreateSectionCard(Transform parent, string name, Color color, UIShapePreset shape, RectOffset padding, float spacing)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();

            var img = obj.AddComponent<Image>();
            UITheme.ApplySurface(img, color, shape);

            var layout = obj.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = spacing;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            return obj;
        }

        private Button CreateButton(Transform parent, string label, Color bgColor, System.Action onClick)
        {
            var btnObj = new GameObject($"Btn_{label}");
            btnObj.transform.SetParent(parent, false);
            btnObj.AddComponent<RectTransform>();

            var img = btnObj.AddComponent<Image>();
            UITheme.ApplySurface(img, bgColor, bgColor == confirmColor ? UIShapePreset.Pill : UIShapePreset.Button);

            var btn = btnObj.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = UITheme.CreateColorBlock(
                bgColor,
                bgColor == confirmColor ? UITheme.PrimaryAccentHover : buttonHoverColor,
                bgColor == confirmColor ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border,
                bgColor,
                lockedButtonColor);
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txt = CreateTextObj(btnObj.transform, "Label", label, 14, TextAlignmentOptions.Center);
            txt.GetComponent<TextMeshProUGUI>().color = bgColor == confirmColor ? UITheme.InverseText : UITheme.PrimaryText;
            var txtRT = txt.GetComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero;
            txtRT.offsetMax = Vector2.zero;

            return btn;
        }
    }
}
