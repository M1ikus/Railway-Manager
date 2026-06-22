using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace DepotSystem
{
    public partial class TrackPopupUI
    {
        private void CreateTurnoutPopup()
        {
            turnoutPopupPanel = new GameObject("TurnoutPopup");
            turnoutPopupPanel.transform.SetParent(transform, false);

            RectTransform rt = turnoutPopupPanel.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(320f, 224f);

            var turnoutPanelImage = turnoutPopupPanel.AddComponent<Image>();
            UITheme.ApplySurface(turnoutPanelImage, panelColor, UIShapePreset.PanelLarge);

            VerticalLayoutGroup layout = turnoutPopupPanel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = UITheme.Spacing.Md;
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = false;

            GameObject summaryCard = CreateSectionCard(turnoutPopupPanel.transform, "TurnoutSummaryCard", 116f, 8f);
            TextMeshProUGUI turnoutLead = CreateLabel(summaryCard.transform, "TurnoutLead", 11, UITheme.SecondaryText, 18f);
            turnoutLead.text = "Rozjazd";
            turnoutNameText = CreateSectionLabel(summaryCard.transform, "TurnoutName", 15, 34f, TextAlignmentOptions.Center, headerColor, UITheme.PrimaryText, FontStyles.Bold);
            turnoutInfoText = CreateSectionLabel(summaryCard.transform, "TurnoutInfo", 12, 34f, TextAlignmentOptions.MidlineLeft, sectionColor, UITheme.SecondaryText, FontStyles.Normal);

            GameObject buttonRow = CreateHorizontalRow(turnoutPopupPanel.transform, "TurnoutButtons", 36f, true);

            deleteTurnoutButton = CreateActionButton(
                buttonRow.transform,
                "DeleteTurnout",
                LocalizationService.Get("popup_depot_track.turnout.btn_delete"),
                132f,
                dangerButtonColor);
            deleteTurnoutButton.onClick.AddListener(OnDeleteTurnoutClicked);

            closeTurnoutButton = CreateActionButton(
                buttonRow.transform,
                "CloseTurnout",
                LocalizationService.Get("popup_depot_track.turnout.btn_close"),
                88f,
                secondaryButtonColor);
            closeTurnoutButton.onClick.AddListener(CloseTurnoutPopup);

            turnoutPopupPanel.SetActive(false);
        }

        private void CreatePopup()
        {
            popupPanel = new GameObject("TrackPopup");
            popupPanel.transform.SetParent(transform, false);

            RectTransform rt = popupPanel.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(352f, 520f);

            var popupPanelImage = popupPanel.AddComponent<Image>();
            UITheme.ApplySurface(popupPanelImage, panelColor, UIShapePreset.PanelLarge);

            VerticalLayoutGroup layout = popupPanel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = UITheme.Spacing.Md;
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = false;

            GameObject summaryCard = CreateSectionCard(popupPanel.transform, "TrackSummaryCard", 112f, 8f);
            TextMeshProUGUI summaryLead = CreateLabel(summaryCard.transform, "SummaryLead", 11, UITheme.SecondaryText, 18f);
            summaryLead.text = "Wybrany tor";
            GameObject nameSection = CreateSectionContainer(summaryCard.transform, "TrackNameSection", 36f, headerColor);
            trackNameInput = CreateInputField(nameSection.transform, "TrackName", 14, LocalizationService.Get("popup_depot_track.name_placeholder"), 28f);
            trackTypeText = CreateSectionLabel(summaryCard.transform, "TrackType", 11, 34f, TextAlignmentOptions.MidlineLeft, sectionColor, UITheme.SecondaryText, FontStyles.Normal);

            GameObject geometryCard = CreateSectionCard(popupPanel.transform, "GeometryCard", 170f, 8f);
            TextMeshProUGUI geometryLead = CreateLabel(geometryCard.transform, "GeometryLead", 11, UITheme.SecondaryText, 18f);
            geometryLead.text = "Parametry geometrii";
            GameObject lengthRow = CreateHorizontalRow(geometryCard.transform, "LengthRow", 30f);
            CreateLabel(lengthRow.transform, "LengthLabel", 11, UITheme.SecondaryText, 26f).text = LocalizationService.Get("popup_depot_track.length_label");
            lengthInput = CreateInputField(lengthRow.transform, "LengthInput", 12, "0", 26f);
            lengthInput.contentType = TMP_InputField.ContentType.DecimalNumber;

            GameObject radiusRow = CreateHorizontalRow(geometryCard.transform, "RadiusRow", 30f);
            CreateLabel(radiusRow.transform, "RadiusLabel", 11, UITheme.SecondaryText, 26f).text = LocalizationService.Get("popup_depot_track.radius_label");
            radiusInput = CreateInputField(radiusRow.transform, "RadiusInput", 12, LocalizationService.Get("popup_depot_track.radius_placeholder"), 26f);
            radiusInput.contentType = TMP_InputField.ContentType.DecimalNumber;

            GameObject angleRow = CreateHorizontalRow(geometryCard.transform, "AngleRow", 30f);
            CreateLabel(angleRow.transform, "AngleLabel", 11, UITheme.SecondaryText, 26f).text = LocalizationService.Get("popup_depot_track.angle_label");
            angleValueText = CreateLabel(angleRow.transform, "AngleValue", 12, UITheme.PrimaryText, 26f);

            applyParamsButton = CreateActionButton(
                geometryCard.transform,
                "ApplyBtn",
                LocalizationService.Get("popup_depot_track.btn_apply"),
                0f,
                primaryButtonColor);
            applyParamsButton.onClick.AddListener(OnApplyParams);
            applyParamsButton.GetComponent<LayoutElement>().preferredHeight = 32f;

            GameObject actionsCard = CreateSectionCard(popupPanel.transform, "ActionsCard", 112f, 8f);
            TextMeshProUGUI actionsLead = CreateLabel(actionsCard.transform, "ActionsLead", 11, UITheme.SecondaryText, 18f);
            actionsLead.text = "Akcje toru";

            GameObject catRow = CreateHorizontalRow(actionsCard.transform, "CatenaryRow", 30f);

            GameObject catIconObj = new GameObject("CatenaryIcon");
            catIconObj.transform.SetParent(catRow.transform, false);
            catIconObj.AddComponent<RectTransform>().sizeDelta = new Vector2(18f, 18f);
            LayoutElement catIconLe = catIconObj.AddComponent<LayoutElement>();
            catIconLe.preferredWidth = 18f;
            catIconLe.preferredHeight = 18f;
            catIconLe.flexibleWidth = 0f;
            catenaryIcon = catIconObj.AddComponent<Image>();
            UITheme.ApplySurface(catenaryIcon, catenaryOffColor, UIShapePreset.Button);

            toggleCatenaryButton = CreateActionButton(
                catRow.transform,
                "ToggleCat",
                LocalizationService.Get("popup_depot_track.btn_catenary"),
                170f,
                secondaryButtonColor);
            toggleCatenaryButton.onClick.AddListener(OnToggleCatenaryClicked);

            parallelButton = CreateActionButton(
                actionsCard.transform,
                "ParallelBtn",
                LocalizationService.Get("popup_depot_track.btn_parallel"),
                0f,
                UITheme.PrimaryAccentHover);
            parallelButton.onClick.AddListener(OnParallelClicked);
            parallelButton.GetComponent<LayoutElement>().preferredHeight = 32f;

            GameObject buttonRow = CreateHorizontalRow(popupPanel.transform, "Buttons", 36f, true);

            renameButton = CreateActionButton(
                buttonRow.transform,
                "RenameBtn",
                LocalizationService.Get("popup_depot_track.btn_rename"),
                90f,
                secondaryButtonColor);
            renameButton.onClick.AddListener(OnRenameClicked);

            deleteButton = CreateActionButton(
                buttonRow.transform,
                "DeleteBtn",
                LocalizationService.Get("popup_depot_track.btn_delete"),
                90f,
                dangerButtonColor);
            deleteButton.onClick.AddListener(OnDeleteClicked);

            closeButton = CreateActionButton(
                buttonRow.transform,
                "CloseBtn",
                LocalizationService.Get("popup_depot_track.btn_close"),
                74f,
                secondaryButtonColor);
            closeButton.onClick.AddListener(ClosePopup);

            popupPanel.SetActive(false);
        }

        private void CreateParallelDialog()
        {
            parallelDialog = new GameObject("ParallelDialog");
            parallelDialog.transform.SetParent(transform, false);

            RectTransform rt = parallelDialog.AddComponent<RectTransform>();
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(368f, 372f);

            var parallelPanelImage = parallelDialog.AddComponent<Image>();
            UITheme.ApplySurface(parallelPanelImage, panelColor, UIShapePreset.PanelLarge);

            VerticalLayoutGroup layout = parallelDialog.AddComponent<VerticalLayoutGroup>();
            layout.spacing = UITheme.Spacing.Md;
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = false;

            TextMeshProUGUI title = CreateSectionLabel(parallelDialog.transform, "Title", 14, 34f, TextAlignmentOptions.Center, headerColor, UITheme.PrimaryText, FontStyles.Bold);
            title.text = LocalizationService.Get("popup_depot_track.parallel.title");

            GameObject contextCard = CreateSectionCard(parallelDialog.transform, "ParallelContextCard", 72f, 6f);
            TextMeshProUGUI contextLead = CreateLabel(contextCard.transform, "ContextLead", 11, UITheme.SecondaryText, 18f);
            contextLead.text = "Generator";
            TextMeshProUGUI contextBody = CreateLabel(contextCard.transform, "ContextBody", 12, UITheme.PrimaryText, 24f);
            contextBody.text = "Okresl liczbe torow po lewej i prawej stronie oraz odstep miedzy nimi.";
            contextBody.alignment = TextAlignmentOptions.TopLeft;

            GameObject formCard = CreateSectionCard(parallelDialog.transform, "ParallelFormCard", 156f, 8f);
            TextMeshProUGUI formLead = CreateLabel(formCard.transform, "FormLead", 11, UITheme.SecondaryText, 18f);
            formLead.text = "Parametry ukladania";
            GameObject leftRow = CreateHorizontalRow(formCard.transform, "LeftRow", 30f);
            CreateLabel(leftRow.transform, "LeftLabel", 12, UITheme.SecondaryText, 26f).text = LocalizationService.Get("popup_depot_track.parallel.left_label");
            leftCountInput = CreateInputField(leftRow.transform, "LeftCount", 12, "0", 26f);
            leftCountInput.contentType = TMP_InputField.ContentType.IntegerNumber;

            GameObject rightRow = CreateHorizontalRow(formCard.transform, "RightRow", 30f);
            CreateLabel(rightRow.transform, "RightLabel", 12, UITheme.SecondaryText, 26f).text = LocalizationService.Get("popup_depot_track.parallel.right_label");
            rightCountInput = CreateInputField(rightRow.transform, "RightCount", 12, "3", 26f);
            rightCountInput.contentType = TMP_InputField.ContentType.IntegerNumber;

            GameObject spacingRow = CreateHorizontalRow(formCard.transform, "SpacingRow", 30f);
            CreateLabel(spacingRow.transform, "SpacingLabel", 12, UITheme.SecondaryText, 26f).text = LocalizationService.Get("popup_depot_track.parallel.spacing_label");
            spacingInput = CreateInputField(spacingRow.transform, "Spacing", 12, "5", 26f);
            spacingInput.contentType = TMP_InputField.ContentType.DecimalNumber;

            GameObject catRow = CreateHorizontalRow(formCard.transform, "CatToggleRow", 30f);
            CreateLabel(catRow.transform, "CatLabel", 12, UITheme.SecondaryText, 26f).text = LocalizationService.Get("popup_depot_track.parallel.catenary_label");
            catenaryToggle = CreateToggle(catRow.transform, "CatToggle");

            GameObject buttonRow = CreateHorizontalRow(parallelDialog.transform, "DialogButtons", 36f, true);

            generateButton = CreateActionButton(
                buttonRow.transform,
                "GenerateBtn",
                LocalizationService.Get("popup_depot_track.parallel.btn_generate"),
                128f,
                primaryButtonColor);
            generateButton.onClick.AddListener(OnGenerateParallel);

            cancelParallelButton = CreateActionButton(
                buttonRow.transform,
                "CancelBtn",
                LocalizationService.Get("popup_depot_track.parallel.btn_cancel"),
                108f,
                secondaryButtonColor);
            cancelParallelButton.onClick.AddListener(CloseParallelDialog);

            parallelDialog.SetActive(false);
        }

        private TextMeshProUGUI CreateLabel(Transform parent, string name, int fontSize, Color color, float height)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>().sizeDelta = new Vector2(240f, height);

            LayoutElement layout = obj.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.flexibleWidth = 1f;

            TextMeshProUGUI text = obj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(text, UIThemeTextRole.Primary);
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.richText = false;
            return text;
        }

        private TMP_InputField CreateInputField(Transform parent, string name, int fontSize, string placeholder, float height)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>().sizeDelta = new Vector2(112f, height);

            LayoutElement layout = obj.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.flexibleWidth = 1f;

            Image bg = obj.AddComponent<Image>();
            UITheme.ApplySurface(bg, inputColor, UIShapePreset.Inset);

            // TMP_InputField requires Viewport (RectMask2D) + Text/Placeholder as children.
            GameObject viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(obj.transform, false);
            RectTransform viewportRt = viewportObj.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(8f, 3f);
            viewportRt.offsetMax = new Vector2(-8f, -3f);
            viewportObj.AddComponent<RectMask2D>();

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(viewportObj.transform, false);
            RectTransform textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            TextMeshProUGUI textComp = textObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(textComp, UIThemeTextRole.Primary);
            textComp.fontSize = fontSize;
            textComp.alignment = TextAlignmentOptions.MidlineLeft;
            textComp.textWrappingMode = TextWrappingModes.NoWrap;
            textComp.richText = false;
            textComp.raycastTarget = false;

            GameObject phObj = new GameObject("Placeholder");
            phObj.transform.SetParent(viewportObj.transform, false);
            RectTransform phRt = phObj.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;

            TextMeshProUGUI phText = phObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(phText, UIThemeTextRole.Secondary);
            phText.fontSize = fontSize;
            phText.fontStyle = FontStyles.Italic;
            phText.alignment = TextAlignmentOptions.MidlineLeft;
            phText.textWrappingMode = TextWrappingModes.NoWrap;
            phText.raycastTarget = false;
            phText.text = placeholder;

            TMP_InputField input = obj.AddComponent<TMP_InputField>();
            input.textViewport = viewportRt;
            input.textComponent = textComp;
            input.placeholder = phText;
            input.lineType = TMP_InputField.LineType.SingleLine;
            UITheme.ApplyTmpInputField(input, bg, textComp, phText);
            return input;
        }

        private Button CreateActionButton(Transform parent, string name, string label, float width, Color bgColor)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>().sizeDelta = new Vector2(width > 0f ? width : 0f, 30f);

            LayoutElement layout = obj.AddComponent<LayoutElement>();
            if (width > 0f)
                layout.preferredWidth = width;
            layout.preferredHeight = 30f;
            layout.flexibleWidth = width > 0f ? 0f : 1f;

            Image bg = obj.AddComponent<Image>();
            UITheme.ApplySurface(bg, bgColor, ShouldUseInverseText(bgColor) ? UIShapePreset.Pill : UIShapePreset.Button);

            Button btn = obj.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.colors = UITheme.CreateColorBlock(
                bgColor,
                UITheme.Darken(bgColor, 0.05f),
                UITheme.Darken(bgColor, 0.12f),
                bgColor,
                UITheme.WithAlpha(UITheme.Border, 0.55f));

            GameObject textObj = new GameObject("Label");
            textObj.transform.SetParent(obj.transform, false);
            RectTransform textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(text, ShouldUseInverseText(bgColor) ? UIThemeTextRole.Inverse : UIThemeTextRole.Primary);
            text.fontSize = 12;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.richText = false;
            text.text = label;

            return btn;
        }

        private TMP_Dropdown CreateDropdown(Transform parent, string name, string[] options)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>().sizeDelta = new Vector2(120f, 26f);

            LayoutElement layout = obj.AddComponent<LayoutElement>();
            layout.preferredHeight = 26f;
            layout.flexibleWidth = 1f;

            Image bg = obj.AddComponent<Image>();
            UITheme.ApplySurface(bg, inputColor, UIShapePreset.Inset);

            TMP_Dropdown dropdown = obj.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = bg;

            GameObject captionObj = new GameObject("Label");
            captionObj.transform.SetParent(obj.transform, false);
            RectTransform captionRt = captionObj.AddComponent<RectTransform>();
            captionRt.anchorMin = Vector2.zero;
            captionRt.anchorMax = Vector2.one;
            captionRt.offsetMin = new Vector2(8f, 3f);
            captionRt.offsetMax = new Vector2(-22f, -3f);

            TextMeshProUGUI captionText = captionObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(captionText, UIThemeTextRole.Primary);
            captionText.fontSize = 12;
            captionText.alignment = TextAlignmentOptions.MidlineLeft;
            dropdown.captionText = captionText;

            GameObject arrowObj = new GameObject("Arrow");
            arrowObj.transform.SetParent(obj.transform, false);
            RectTransform arrowRt = arrowObj.AddComponent<RectTransform>();
            arrowRt.anchorMin = new Vector2(1f, 0f);
            arrowRt.anchorMax = new Vector2(1f, 1f);
            arrowRt.pivot = new Vector2(1f, 0.5f);
            arrowRt.sizeDelta = new Vector2(18f, 0f);
            TextMeshProUGUI arrowText = arrowObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(arrowText, UIThemeTextRole.Secondary);
            arrowText.alignment = TextAlignmentOptions.Center;
            arrowText.fontSize = 10;
            arrowText.text = "v";

            GameObject templateObj = new GameObject("Template");
            templateObj.transform.SetParent(obj.transform, false);
            RectTransform templateRt = templateObj.AddComponent<RectTransform>();
            templateRt.anchorMin = new Vector2(0f, 0f);
            templateRt.anchorMax = new Vector2(1f, 0f);
            templateRt.pivot = new Vector2(0.5f, 1f);
            templateRt.sizeDelta = new Vector2(0f, 78f);

            Image templateBg = templateObj.AddComponent<Image>();
            UITheme.ApplySurface(templateBg, sectionColor, UIShapePreset.Inset);
            templateObj.AddComponent<ScrollRect>();

            GameObject itemObj = new GameObject("Item");
            itemObj.transform.SetParent(templateObj.transform, false);
            RectTransform itemRt = itemObj.AddComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(0f, 0.5f);
            itemRt.anchorMax = new Vector2(1f, 0.5f);
            itemRt.sizeDelta = new Vector2(0f, 26f);

            Toggle itemToggle = itemObj.AddComponent<Toggle>();
            var itemBackground = itemObj.AddComponent<Image>();
            UITheme.ApplySurface(itemBackground, inputColor, UIShapePreset.Button);
            itemToggle.targetGraphic = itemBackground;

            GameObject itemLabelObj = new GameObject("Item Label");
            itemLabelObj.transform.SetParent(itemObj.transform, false);
            RectTransform itemLabelRt = itemLabelObj.AddComponent<RectTransform>();
            itemLabelRt.anchorMin = Vector2.zero;
            itemLabelRt.anchorMax = Vector2.one;
            itemLabelRt.offsetMin = new Vector2(6f, 0f);
            itemLabelRt.offsetMax = new Vector2(-6f, 0f);

            TextMeshProUGUI itemText = itemLabelObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(itemText, UIThemeTextRole.Primary);
            itemText.fontSize = 12;
            itemText.alignment = TextAlignmentOptions.MidlineLeft;

            dropdown.itemText = itemText;
            dropdown.template = templateRt;

            templateObj.SetActive(false);
            dropdown.ClearOptions();
            dropdown.AddOptions(new System.Collections.Generic.List<string>(options));
            return dropdown;
        }

        private Toggle CreateToggle(Transform parent, string name)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>().sizeDelta = new Vector2(24f, 24f);

            LayoutElement layout = obj.AddComponent<LayoutElement>();
            layout.preferredWidth = 24f;
            layout.preferredHeight = 24f;
            layout.flexibleWidth = 0f;

            Image bg = obj.AddComponent<Image>();
            UITheme.ApplySurface(bg, inputColor, UIShapePreset.Inset);

            Toggle toggle = obj.AddComponent<Toggle>();
            toggle.targetGraphic = bg;

            GameObject checkObj = new GameObject("Checkmark");
            checkObj.transform.SetParent(obj.transform, false);
            RectTransform checkRt = checkObj.AddComponent<RectTransform>();
            checkRt.anchorMin = new Vector2(0.18f, 0.18f);
            checkRt.anchorMax = new Vector2(0.82f, 0.82f);
            checkRt.offsetMin = Vector2.zero;
            checkRt.offsetMax = Vector2.zero;

            Image checkImg = checkObj.AddComponent<Image>();
            checkImg.color = UITheme.Success;

            toggle.graphic = checkImg;
            toggle.isOn = false;
            return toggle;
        }

        private GameObject CreateHorizontalRow(Transform parent, string name, float height, bool tintedBackground = true)
        {
            GameObject row = new GameObject(name);
            row.transform.SetParent(parent, false);
            row.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, height);

            LayoutElement layout = row.AddComponent<LayoutElement>();
            layout.preferredHeight = height;

            if (tintedBackground)
            {
                var background = row.AddComponent<Image>();
                UITheme.ApplySurface(background, sectionColor, UIShapePreset.Inset);
            }

            HorizontalLayoutGroup hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Xs);
            hlg.spacing = UITheme.Spacing.Sm;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            return row;
        }

        private GameObject CreateSectionCard(Transform parent, string name, float height, float spacing)
        {
            GameObject card = new GameObject(name);
            card.transform.SetParent(parent, false);
            card.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            card.AddComponent<LayoutElement>().preferredHeight = height;

            var background = card.AddComponent<Image>();
            UITheme.ApplySurface(background, sectionColor, UIShapePreset.Inset);

            VerticalLayoutGroup layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Md);
            layout.spacing = spacing;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            return card;
        }

        private void CreateSpacer(Transform parent, float height)
        {
            GameObject spacer = new GameObject("Spacer");
            spacer.transform.SetParent(parent, false);
            spacer.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            spacer.AddComponent<LayoutElement>().preferredHeight = height;
        }

        private GameObject CreateSectionContainer(Transform parent, string name, float height, Color backgroundColor)
        {
            GameObject section = new GameObject(name);
            section.transform.SetParent(parent, false);
            section.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            section.AddComponent<LayoutElement>().preferredHeight = height;
            var background = section.AddComponent<Image>();
            UITheme.ApplySurface(background, backgroundColor, UIShapePreset.Inset);

            HorizontalLayoutGroup layout = section.AddComponent<HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Xs);
            layout.spacing = 0f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            return section;
        }

        private TextMeshProUGUI CreateSectionLabel(
            Transform parent,
            string name,
            int fontSize,
            float height,
            TextAlignmentOptions alignment,
            Color backgroundColor,
            Color textColor,
            FontStyles fontStyle)
        {
            GameObject section = CreateSectionContainer(parent, $"{name}Section", height, backgroundColor);
            TextMeshProUGUI text = CreateLabel(section.transform, name, fontSize, textColor, height - 4f);
            text.alignment = alignment;
            text.fontStyle = fontStyle;
            return text;
        }

        private static bool ShouldUseInverseText(Color color)
        {
            float luminance = (0.2126f * color.r) + (0.7152f * color.g) + (0.0722f * color.b);
            return luminance < 0.55f;
        }
    }
}
