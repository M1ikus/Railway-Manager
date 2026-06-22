using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Dialog for configuring a branch that returns to the parallel track.
    /// </summary>
    public class BranchReturnDialogUI : MonoBehaviour
    {
        [Header("Colors")]
        [SerializeField] private Color panelColor = default;
        [SerializeField] private Color sectionColor = default;
        [SerializeField] private Color inputColor = default;
        [SerializeField] private Color confirmColor = default;
        [SerializeField] private Color cancelColor = default;
        [SerializeField] private Color validColor = default;
        [SerializeField] private Color invalidColor = default;

        public Action<float, int, float> OnConfirmed;
        public Action OnCancelled;

        private GameObject panel;
        private TMP_InputField spacingInput;
        private TMP_Dropdown returnTypeDropdown;
        private GameObject radiusRow;
        private TMP_InputField radiusInput;
        private TextMeshProUGUI infoLabel;
        private Button okButton;
        private Button cancelButton;

        private TurnoutData.TurnoutDefinition currentMainDef;
        private bool isBuilt;

        private InputActions inputActions;
        private InputActions.UIPopupActions popupActions;

        public bool IsVisible => panel != null && panel.activeSelf;

        void Awake()
        {
            ApplyDefaultPalette();

            inputActions = new InputActions();
            RailwayManager.Core.Settings.RebindingService.ApplyOverridesTo(inputActions);
            popupActions = inputActions.UIPopup;
        }

        void OnEnable()
        {
            popupActions.Enable();
        }

        void OnDisable()
        {
            popupActions.Disable();
        }

        void OnDestroy()
        {
            inputActions?.Dispose();
        }

        void Update()
        {
            if (IsVisible && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                PauseMenuUI.LastEscConsumedFrame = Time.frameCount;
                Hide();
                OnCancelled?.Invoke();
            }
        }

        public void Show(TurnoutData.TurnoutDefinition mainDef)
        {
            currentMainDef = mainDef;

            if (!isBuilt)
                BuildDialog();

            spacingInput.text = "5";
            radiusInput.text = "190";
            returnTypeDropdown.value = 0;
            radiusRow.SetActive(true);

            panel.SetActive(true);
            Recalculate();
        }

        public void Hide()
        {
            if (panel != null)
                panel.SetActive(false);
        }

        private void ApplyDefaultPalette()
        {
            if (panelColor == default)
                panelColor = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
            if (sectionColor == default)
                sectionColor = UITheme.TopBarInset;
            if (inputColor == default)
                inputColor = UITheme.TopBarInset;
            if (confirmColor == default)
                confirmColor = UITheme.PrimaryAccent;
            if (cancelColor == default)
                cancelColor = UITheme.SecondarySurface;
            if (validColor == default)
                validColor = UITheme.Success;
            if (invalidColor == default)
                invalidColor = UITheme.Danger;
        }

        private void BuildDialog()
        {
            Canvas canvas = DepotUIManager.Instance?.canvas;
            if (canvas == null)
                return;

            panel = new GameObject("BranchReturnDialog");
            panel.transform.SetParent(canvas.transform, false);

            RectTransform root = panel.AddComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(408f, 356f);

            var panelImage = panel.AddComponent<Image>();
            UITheme.ApplySurface(panelImage, panelColor, UIShapePreset.PanelLarge);

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = UITheme.Spacing.Md;
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = false;

            CreateSectionLabel(panel.transform, "Title", "Odgalezienie z powrotem", 16, 40f, TextAlignmentOptions.Center);

            GameObject contextCard = CreateSectionCard(panel.transform, "ContextCard", 58f, 4f);
            CreateLabel(contextCard.transform, "ContextLead", 11, UITheme.SecondaryText, 18f).text = "Konfiguracja";
            TextMeshProUGUI contextText = CreateLabel(contextCard.transform, "ContextText", 12, UITheme.PrimaryText, 28f);
            contextText.text = "Ustaw odstep torow i sposob powrotu, a dialog wyliczy potrzebna wstawke prosta.";
            contextText.alignment = TextAlignmentOptions.TopLeft;

            GameObject formCard = CreateSectionCard(panel.transform, "FormCard", 132f, 8f);

            GameObject spacingRow = CreateHorizontalRow(formCard.transform, "SpacingRow", 30f);
            CreateLabel(spacingRow.transform, "SpacingLabel", 12, UITheme.SecondaryText, 26f).text = "Miedzytorze (m):";
            spacingInput = CreateInputField(spacingRow.transform, "SpacingInput", 12, "5", 26f);
            spacingInput.contentType = TMP_InputField.ContentType.DecimalNumber;
            spacingInput.onValueChanged.AddListener(_ => Recalculate());

            GameObject typeRow = CreateHorizontalRow(formCard.transform, "TypeRow", 30f);
            CreateLabel(typeRow.transform, "TypeLabel", 12, UITheme.SecondaryText, 26f).text = "Typ powrotny:";
            returnTypeDropdown = CreateDropdown(typeRow.transform, "ReturnType", new[] { "Luk (promien)", "R190 1:9", "R300 1:9" });
            returnTypeDropdown.onValueChanged.AddListener(OnReturnTypeChanged);

            radiusRow = CreateHorizontalRow(formCard.transform, "RadiusRow", 30f);
            CreateLabel(radiusRow.transform, "RadiusLabel", 12, UITheme.SecondaryText, 26f).text = "Promien (m):";
            radiusInput = CreateInputField(radiusRow.transform, "RadiusInput", 12, "190", 26f);
            radiusInput.contentType = TMP_InputField.ContentType.DecimalNumber;
            radiusInput.onValueChanged.AddListener(_ => Recalculate());

            infoLabel = CreateSectionLabel(panel.transform, "Info", string.Empty, 12, 42f, TextAlignmentOptions.Center);
            infoLabel.color = validColor;

            GameObject buttonRow = CreateHorizontalRow(panel.transform, "ButtonRow", 38f, true);
            okButton = CreateActionButton(buttonRow.transform, "OkBtn", "OK", 132f, confirmColor, true);
            okButton.onClick.AddListener(OnOkClicked);

            cancelButton = CreateActionButton(buttonRow.transform, "CancelBtn", "Anuluj", 120f, cancelColor, false);
            cancelButton.onClick.AddListener(OnCancelClicked);

            panel.SetActive(false);
            isBuilt = true;
        }

        private void OnReturnTypeChanged(int value)
        {
            radiusRow.SetActive(value == 0);
            Recalculate();
        }

        private bool TryParseFloat(string text, out float value)
        {
            text = text.Replace(',', '.');
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private void Recalculate()
        {
            if (!TryParseFloat(spacingInput.text, out float spacing) || spacing <= 0f)
            {
                infoLabel.text = "Podaj poprawne miedzytorze";
                infoLabel.color = invalidColor;
                okButton.interactable = false;
                return;
            }

            int returnType = returnTypeDropdown.value;
            float returnRadius = 190f;
            TurnoutData.TurnoutDefinition? returnDef = null;

            if (returnType == 0)
            {
                if (!TryParseFloat(radiusInput.text, out returnRadius) || returnRadius <= 0f)
                {
                    infoLabel.text = "Podaj poprawny promien";
                    infoLabel.color = invalidColor;
                    okButton.interactable = false;
                    return;
                }
            }
            else if (returnType == 1)
            {
                returnDef = TurnoutData.R190_1_9;
            }
            else if (returnType == 2)
            {
                returnDef = TurnoutData.R300_1_9;
            }

            var (insertLen, valid) = TurnoutData.ComputeBranchReturnInsert(currentMainDef, spacing, returnRadius, returnDef);
            if (!valid || insertLen < -0.01f)
            {
                infoLabel.text = "Niemozliwe - za maly odstep lub promien";
                infoLabel.color = invalidColor;
                okButton.interactable = false;
                return;
            }

            infoLabel.text = $"Wstawka prosta: {Mathf.Max(0f, insertLen):F5} m";
            infoLabel.color = validColor;
            okButton.interactable = true;
        }

        private void OnOkClicked()
        {
            if (!TryParseFloat(spacingInput.text, out float spacing))
                return;

            int returnType = returnTypeDropdown.value;
            float radius = 190f;
            if (returnType == 0)
                TryParseFloat(radiusInput.text, out radius);

            panel.SetActive(false);
            OnConfirmed?.Invoke(spacing, returnType, radius);
        }

        private void OnCancelClicked()
        {
            panel.SetActive(false);
            OnCancelled?.Invoke();
        }

        private TextMeshProUGUI CreateLabel(Transform parent, string name, int fontSize, Color color, float height)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>().sizeDelta = new Vector2(240f, height);
            obj.AddComponent<LayoutElement>().preferredHeight = height;

            TextMeshProUGUI text = obj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(text, UIThemeTextRole.Primary);
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.richText = false;
            return text;
        }

        private TextMeshProUGUI CreateSectionLabel(Transform parent, string name, string label, int fontSize, float height, TextAlignmentOptions alignment)
        {
            GameObject section = new GameObject($"{name}Section");
            section.transform.SetParent(parent, false);
            section.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            section.AddComponent<LayoutElement>().preferredHeight = height;
            var background = section.AddComponent<Image>();
            UITheme.ApplySurface(background, sectionColor, UIShapePreset.Inset);

            TextMeshProUGUI text = CreateLabel(section.transform, name, fontSize, UITheme.PrimaryText, height);
            text.text = label;
            text.fontStyle = FontStyles.Bold;
            text.alignment = alignment;

            RectTransform textRT = text.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(8f, 0f);
            textRT.offsetMax = new Vector2(-8f, 0f);
            return text;
        }

        private TMP_InputField CreateInputField(Transform parent, string name, int fontSize, string placeholder, float height)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>().sizeDelta = new Vector2(108f, height);

            LayoutElement layout = obj.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.flexibleWidth = 1f;

            Image bg = obj.AddComponent<Image>();
            UITheme.ApplySurface(bg, inputColor, UIShapePreset.Inset);

            // TMP_InputField requires Viewport (RectMask2D) + Text/Placeholder as children.
            GameObject viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(obj.transform, false);
            RectTransform viewportRT = viewportObj.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = new Vector2(8f, 3f);
            viewportRT.offsetMax = new Vector2(-8f, -3f);
            viewportObj.AddComponent<RectMask2D>();

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(viewportObj.transform, false);
            RectTransform textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            TextMeshProUGUI textComp = textObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(textComp, UIThemeTextRole.Primary);
            textComp.fontSize = fontSize;
            textComp.alignment = TextAlignmentOptions.MidlineLeft;
            textComp.textWrappingMode = TextWrappingModes.NoWrap;
            textComp.richText = false;
            textComp.raycastTarget = false;

            GameObject placeholderObj = new GameObject("Placeholder");
            placeholderObj.transform.SetParent(viewportObj.transform, false);
            RectTransform placeholderRT = placeholderObj.AddComponent<RectTransform>();
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.offsetMin = Vector2.zero;
            placeholderRT.offsetMax = Vector2.zero;

            TextMeshProUGUI placeholderText = placeholderObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(placeholderText, UIThemeTextRole.Secondary);
            placeholderText.fontSize = fontSize;
            placeholderText.fontStyle = FontStyles.Italic;
            placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
            placeholderText.textWrappingMode = TextWrappingModes.NoWrap;
            placeholderText.raycastTarget = false;
            placeholderText.text = placeholder;

            TMP_InputField input = obj.AddComponent<TMP_InputField>();
            input.textViewport = viewportRT;
            input.textComponent = textComp;
            input.placeholder = placeholderText;
            input.lineType = TMP_InputField.LineType.SingleLine;
            UITheme.ApplyTmpInputField(input, bg, textComp, placeholderText);
            return input;
        }

        private TMP_Dropdown CreateDropdown(Transform parent, string name, string[] options)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>().sizeDelta = new Vector2(140f, 26f);

            LayoutElement layout = obj.AddComponent<LayoutElement>();
            layout.preferredHeight = 26f;
            layout.flexibleWidth = 1f;

            Image bg = obj.AddComponent<Image>();
            UITheme.ApplySurface(bg, inputColor, UIShapePreset.Inset);

            TMP_Dropdown dropdown = obj.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = bg;
            dropdown.colors = UITheme.CreateColorBlock(
                inputColor,
                UITheme.RaisedSurface,
                UITheme.Border,
                inputColor,
                UITheme.WithAlpha(UITheme.Border, 0.55f));

            GameObject captionObj = new GameObject("Label");
            captionObj.transform.SetParent(obj.transform, false);
            RectTransform captionRT = captionObj.AddComponent<RectTransform>();
            captionRT.anchorMin = Vector2.zero;
            captionRT.anchorMax = Vector2.one;
            captionRT.offsetMin = new Vector2(8f, 3f);
            captionRT.offsetMax = new Vector2(-22f, -3f);

            TextMeshProUGUI captionText = captionObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(captionText, UIThemeTextRole.Primary);
            captionText.fontSize = 12;
            captionText.alignment = TextAlignmentOptions.MidlineLeft;
            dropdown.captionText = captionText;

            GameObject arrowObj = new GameObject("Arrow");
            arrowObj.transform.SetParent(obj.transform, false);
            RectTransform arrowRT = arrowObj.AddComponent<RectTransform>();
            arrowRT.anchorMin = new Vector2(1f, 0.5f);
            arrowRT.anchorMax = new Vector2(1f, 0.5f);
            arrowRT.pivot = new Vector2(1f, 0.5f);
            arrowRT.sizeDelta = new Vector2(14f, 14f);
            arrowRT.anchoredPosition = new Vector2(-6f, 0f);

            TextMeshProUGUI arrowText = arrowObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(arrowText, UIThemeTextRole.Secondary);
            arrowText.fontSize = 12;
            arrowText.alignment = TextAlignmentOptions.Center;
            arrowText.text = "v";

            GameObject templateObj = new GameObject("Template");
            templateObj.transform.SetParent(obj.transform, false);
            RectTransform templateRT = templateObj.AddComponent<RectTransform>();
            templateRT.anchorMin = new Vector2(0f, 0f);
            templateRT.anchorMax = new Vector2(1f, 0f);
            templateRT.pivot = new Vector2(0.5f, 1f);
            templateRT.sizeDelta = new Vector2(0f, 78f);

            Image templateBg = templateObj.AddComponent<Image>();
            UITheme.ApplySurface(templateBg, sectionColor, UIShapePreset.Inset);
            templateObj.AddComponent<ScrollRect>();

            GameObject itemObj = new GameObject("Item");
            itemObj.transform.SetParent(templateObj.transform, false);
            RectTransform itemRT = itemObj.AddComponent<RectTransform>();
            itemRT.anchorMin = new Vector2(0f, 0.5f);
            itemRT.anchorMax = new Vector2(1f, 0.5f);
            itemRT.sizeDelta = new Vector2(0f, 24f);
            var itemBg = itemObj.AddComponent<Image>();
            UITheme.ApplySurface(itemBg, inputColor, UIShapePreset.Button);
            itemObj.AddComponent<Toggle>();

            GameObject itemLabelObj = new GameObject("Item Label");
            itemLabelObj.transform.SetParent(itemObj.transform, false);
            RectTransform itemLabelRT = itemLabelObj.AddComponent<RectTransform>();
            itemLabelRT.anchorMin = Vector2.zero;
            itemLabelRT.anchorMax = Vector2.one;
            itemLabelRT.offsetMin = new Vector2(6f, 0f);
            itemLabelRT.offsetMax = new Vector2(-6f, 0f);

            TextMeshProUGUI itemText = itemLabelObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(itemText, UIThemeTextRole.Primary);
            itemText.fontSize = 12;
            itemText.alignment = TextAlignmentOptions.MidlineLeft;

            dropdown.itemText = itemText;
            dropdown.template = templateRT;
            templateObj.SetActive(false);

            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(options));
            return dropdown;
        }

        private Button CreateActionButton(Transform parent, string name, string label, float width, Color bgColor, bool primary)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>().sizeDelta = new Vector2(width, 30f);

            LayoutElement layout = obj.AddComponent<LayoutElement>();
            layout.preferredWidth = width;

            Image bg = obj.AddComponent<Image>();
            UITheme.ApplySurface(bg, bgColor, primary ? UIShapePreset.Pill : UIShapePreset.Button);

            Button button = obj.AddComponent<Button>();
            button.targetGraphic = bg;
            button.colors = UITheme.CreateColorBlock(
                bgColor,
                primary ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface,
                primary ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border,
                bgColor,
                UITheme.WithAlpha(UITheme.Border, 0.55f));

            GameObject textObj = new GameObject("Label");
            textObj.transform.SetParent(obj.transform, false);
            RectTransform textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(text, primary ? UIThemeTextRole.Inverse : UIThemeTextRole.Primary);
            text.fontSize = 12;
            text.alignment = TextAlignmentOptions.Center;
            text.text = label;

            return button;
        }

        private GameObject CreateSectionCard(Transform parent, string name, float height, float spacing)
        {
            GameObject card = new GameObject(name);
            card.transform.SetParent(parent, false);
            card.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            card.AddComponent<LayoutElement>().preferredHeight = height;

            Image background = card.AddComponent<Image>();
            UITheme.ApplySurface(background, sectionColor, UIShapePreset.Inset);

            VerticalLayoutGroup layout = card.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = UITheme.Padding(UITheme.Spacing.Md);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            return card;
        }

        private GameObject CreateHorizontalRow(Transform parent, string name, float height, bool withBackground = true)
        {
            GameObject row = new GameObject(name);
            row.transform.SetParent(parent, false);
            row.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            row.AddComponent<LayoutElement>().preferredHeight = height;

            if (withBackground)
            {
                var background = row.AddComponent<Image>();
                UITheme.ApplySurface(background, sectionColor, UIShapePreset.Inset);
            }

            HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = UITheme.Spacing.Sm;
            layout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Xs);
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            return row;
        }
    }
}
