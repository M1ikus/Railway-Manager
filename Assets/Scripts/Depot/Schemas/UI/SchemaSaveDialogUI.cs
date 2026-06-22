using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DepotSystem.Schemas.Placement;
using DepotSystem.Schemas.Snapshot;
using RailwayManager.Core;
using RailwayManager.SharedUI;

namespace DepotSystem.Schemas.UI
{
    /// <summary>
    /// MD-6 - modal dialog "Zapisz jako preset..." dla user custom schematow.
    /// </summary>
    public class SchemaSaveDialogUI : MonoBehaviour
    {
        public static SchemaSaveDialogUI Instance { get; private set; }

        [Header("Dialog size")]
        public Vector2 dialogSize = new Vector2(500, 450);

        private Canvas _canvas;
        private GameObject _root;
        private GameObject _modalBackdrop;
        private TMP_InputField _nameInput;
        private TMP_InputField _descriptionInput;
        private TMP_InputField _tagsInput;
        private Toggle _workshopCheckbox;
        private Button _saveButton;
        private Button _cancelButton;
        private TMP_Text _validationLabel;
        private TMP_Text _subtitleLabel;

        private TurnoutSchemaDefinition _baseDef;
        private SchemaParameters _editParams;
        private SchemaGeometry _editGeometry;
        private SnapshotGeometry _snapshotGeometry;
        private Action<TurnoutSchemaDefinition> _onSaved;

        public const int MaxNameLength = 60;
        public const int MaxDescriptionLength = 200;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            BuildUI();
            Hide();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Show(
            TurnoutSchemaDefinition baseDef,
            SchemaParameters editParams,
            SchemaGeometry editGeometry,
            Action<TurnoutSchemaDefinition> onSaved = null)
        {
            _baseDef = baseDef;
            _editParams = editParams;
            _editGeometry = editGeometry;
            _snapshotGeometry = null;
            _onSaved = onSaved;

            ApplyDialogDefaults(baseDef);
            if (_root != null)
            {
                _root.SetActive(true);
            }
        }

        public void ShowForSnapshot(
            SnapshotGeometry snapshotGeometry,
            SchemaGeometry previewGeometry,
            Action<TurnoutSchemaDefinition> onSaved = null,
            string defaultName = "Moj snapshot schemat")
        {
            _baseDef = null;
            _editParams = null;
            _editGeometry = previewGeometry;
            _snapshotGeometry = snapshotGeometry;
            _onSaved = onSaved;

            if (_nameInput != null) _nameInput.text = defaultName;
            if (_descriptionInput != null) _descriptionInput.text = "";
            if (_tagsInput != null) _tagsInput.text = "snapshot, custom";
            if (_validationLabel != null) _validationLabel.text = "";
            UpdateSubtitle();

            if (_root != null)
            {
                _root.SetActive(true);
            }
        }

        private void ApplyDialogDefaults(TurnoutSchemaDefinition baseDef)
        {
            if (_nameInput != null)
                _nameInput.text = baseDef != null ? $"{baseDef.name} (kopia)" : "Moj schemat";
            if (_descriptionInput != null)
                _descriptionInput.text = baseDef?.description ?? "";
            if (_tagsInput != null)
                _tagsInput.text = baseDef?.tags != null ? string.Join(", ", baseDef.tags) : "";
            if (_validationLabel != null)
                _validationLabel.text = "";

            UpdateSubtitle();
        }

        public void Hide()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        public bool IsVisible => _root != null && _root.activeSelf;

        private void BuildUI()
        {
            var canvasGO = new GameObject("SchemaSaveDialogCanvas");
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);
            canvasGO.AddComponent<GraphicRaycaster>();

            _root = new GameObject("SchemaSaveDialog_Root");
            _root.transform.SetParent(_canvas.transform, false);
            var rootRT = _root.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;

            _modalBackdrop = new GameObject("Backdrop");
            _modalBackdrop.transform.SetParent(_root.transform, false);
            var backdropRT = _modalBackdrop.AddComponent<RectTransform>();
            backdropRT.anchorMin = Vector2.zero;
            backdropRT.anchorMax = Vector2.one;
            backdropRT.offsetMin = Vector2.zero;
            backdropRT.offsetMax = Vector2.zero;
            var backdropImg = _modalBackdrop.AddComponent<Image>();
            backdropImg.color = UITheme.WithAlpha(UITheme.AppBackground, 0.74f);
            var backdropBtn = _modalBackdrop.AddComponent<Button>();
            backdropBtn.transition = Selectable.Transition.None;
            backdropBtn.onClick.AddListener(OnCancelClicked);

            var dialogGO = new GameObject("Dialog");
            dialogGO.transform.SetParent(_root.transform, false);
            var dialogRT = dialogGO.AddComponent<RectTransform>();
            dialogRT.sizeDelta = dialogSize;
            dialogRT.anchorMin = new Vector2(0.5f, 0.5f);
            dialogRT.anchorMax = new Vector2(0.5f, 0.5f);
            dialogRT.pivot = new Vector2(0.5f, 0.5f);
            dialogRT.anchoredPosition = Vector2.zero;

            var dialogBg = dialogGO.AddComponent<Image>();
            UITheme.ApplySurface(dialogBg, UITheme.OverlayPanelStrong, UIShapePreset.PanelLarge);

            BuildHeader(dialogGO.transform);
            BuildForm(dialogGO.transform);
        }

        private void BuildHeader(Transform parent)
        {
            var headerGO = new GameObject("HeaderCard");
            headerGO.transform.SetParent(parent, false);
            var headerRT = headerGO.AddComponent<RectTransform>();
            headerRT.anchorMin = new Vector2(0f, 1f);
            headerRT.anchorMax = new Vector2(1f, 1f);
            headerRT.pivot = new Vector2(0.5f, 1f);
            headerRT.sizeDelta = new Vector2(0f, 72f);
            headerRT.offsetMin = new Vector2(20f, -72f);
            headerRT.offsetMax = new Vector2(-20f, -12f);

            var headerBg = headerGO.AddComponent<Image>();
            UITheme.ApplySurface(headerBg, UITheme.TopBarInset, UIShapePreset.Panel);

            var layout = headerGO.AddComponent<VerticalLayoutGroup>();
            layout.spacing = UITheme.Spacing.Xxs;
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Md);
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;

            var title = CreateTmpLabel(headerGO.transform, "Zapisz schemat jako preset", 18f, UIThemeTextRole.Primary, FontStyles.Bold);
            title.alignment = TextAlignmentOptions.MidlineLeft;

            _subtitleLabel = CreateTmpLabel(headerGO.transform, "Nazwij wariant, dodaj opis i zapisz go do wlasnej biblioteki.", 11f, UIThemeTextRole.Secondary);
            _subtitleLabel.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private void BuildForm(Transform parent)
        {
            var formGO = new GameObject("Form");
            formGO.transform.SetParent(parent, false);
            var formRT = formGO.AddComponent<RectTransform>();
            formRT.anchorMin = new Vector2(0f, 0f);
            formRT.anchorMax = new Vector2(1f, 1f);
            formRT.offsetMin = new Vector2(20f, 20f);
            formRT.offsetMax = new Vector2(-20f, -96f);

            var layout = formGO.AddComponent<VerticalLayoutGroup>();
            layout.spacing = UITheme.Spacing.Md;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;

            CreateFieldLabel(formGO.transform, "Nazwa* (max 60 znakow):");
            _nameInput = CreateInputField(formGO.transform, "np. Moj ladder 5T", MaxNameLength);

            CreateFieldLabel(formGO.transform, "Opis (opcjonalny, max 200 znakow):");
            _descriptionInput = CreateInputField(formGO.transform, "np. Drabinka 5-torowa z szerokim srodkiem", MaxDescriptionLength);

            CreateFieldLabel(formGO.transform, "Tagi (opcjonalne, oddzielone przecinkami):");
            _tagsInput = CreateInputField(formGO.transform, "np. ladder, passenger, wide-middle", 100);

            _workshopCheckbox = CreateDisabledToggle(formGO.transform, "Udostepnij na Steam Workshop (dostepne w wersji 1.0)");

            var validationCard = new GameObject("ValidationCard");
            validationCard.transform.SetParent(formGO.transform, false);
            var validationBg = validationCard.AddComponent<Image>();
            UITheme.ApplySurface(validationBg, UITheme.WithAlpha(UITheme.Danger, 0.15f), UIShapePreset.Panel);
            var validationLayout = validationCard.AddComponent<HorizontalLayoutGroup>();
            validationLayout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            validationLayout.childForceExpandWidth = true;
            validationLayout.childForceExpandHeight = false;
            var validationLE = validationCard.AddComponent<LayoutElement>();
            validationLE.preferredHeight = 36;

            var validationGO = new GameObject("ValidationLabel");
            validationGO.transform.SetParent(validationCard.transform, false);
            _validationLabel = validationGO.AddComponent<TextMeshProUGUI>();
            _validationLabel.text = "";
            _validationLabel.fontSize = 12f;
            _validationLabel.alignment = TextAlignmentOptions.Center;
            UITheme.ApplyTmpText(_validationLabel, UIThemeTextRole.Danger);

            var spacer = new GameObject("Spacer");
            spacer.transform.SetParent(formGO.transform, false);
            var spacerLE = spacer.AddComponent<LayoutElement>();
            spacerLE.flexibleHeight = 1f;

            var buttonRow = new GameObject("ButtonRow");
            buttonRow.transform.SetParent(formGO.transform, false);
            var rowBg = buttonRow.AddComponent<Image>();
            UITheme.ApplySurface(rowBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Panel);
            var rowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = UITheme.Spacing.Md;
            rowLayout.padding = UITheme.Padding(UITheme.Spacing.Md);
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            var rowLE = buttonRow.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 60f;

            _cancelButton = CreateButton(buttonRow.transform, "Anuluj", OnCancelClicked, false);
            _saveButton = CreateButton(buttonRow.transform, "Zapisz", OnSaveClicked, true);
        }

        private void OnSaveClicked()
        {
            string name = _nameInput?.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
            {
                if (_validationLabel != null) _validationLabel.text = "Nazwa nie moze byc pusta.";
                return;
            }

            if (name.Length > MaxNameLength)
            {
                if (_validationLabel != null) _validationLabel.text = $"Nazwa moze miec maksymalnie {MaxNameLength} znakow.";
                return;
            }

            string description = _descriptionInput?.text?.Trim() ?? "";
            string tagsRaw = _tagsInput?.text?.Trim() ?? "";

            string[] tags;
            if (!string.IsNullOrEmpty(tagsRaw))
            {
                var parts = tagsRaw.Split(',');
                var cleanList = new System.Collections.Generic.List<string>();
                foreach (var part in parts)
                {
                    string trimmed = part.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        cleanList.Add(trimmed);
                    }
                }

                tags = cleanList.ToArray();
            }
            else
            {
                tags = Array.Empty<string>();
            }

            string previewBase64 = "";
            if (_editGeometry != null)
            {
                previewBase64 = SchemaThumbnailGenerator.RenderThumbnailBase64(_editGeometry);
            }

            string sanitizedName = TurnoutSchemaCatalog.SanitizeFilename(name).ToLowerInvariant();
            string id = $"user_{sanitizedName}_{DateTime.UtcNow.Ticks % 1000000}";
            bool isSnapshot = _snapshotGeometry != null;

            var savedDef = new TurnoutSchemaDefinition
            {
                schemaFormatVersion = 1,
                id = id,
                name = name,
                description = description,
                category = isSnapshot ? "Custom" : (_baseDef?.category ?? "Ladder"),
                type = isSnapshot ? "snapshot" : (_baseDef?.type ?? "generative"),
                author = "Player",
                tags = tags,
                version = "1.0",
                createdAt = DateTime.UtcNow.ToString("o"),
                modifiedAt = DateTime.UtcNow.ToString("o"),
                workshopId = 0,
                previewPngBase64 = previewBase64,
                parameters = isSnapshot ? null : ClonePameters(_editParams),
                snapshotGeometry = isSnapshot ? _snapshotGeometry : null,
            };

            bool ok = TurnoutSchemaCatalog.SaveUser(savedDef);
            if (!ok)
            {
                if (_validationLabel != null) _validationLabel.text = "Zapis nie powiodl sie - sprawdz Console.";
                return;
            }

            Log.Info($"[SchemaSaveDialogUI] Saved '{savedDef.id}' = '{savedDef.name}' (preview {previewBase64.Length} chars base64)");
            _onSaved?.Invoke(savedDef);
            Hide();
        }

        private void OnCancelClicked()
        {
            Hide();
        }

        private void UpdateSubtitle()
        {
            if (_subtitleLabel == null)
            {
                return;
            }

            _subtitleLabel.text = _snapshotGeometry != null
                ? "Zapisujesz zaznaczony snapshot torow jako gotowy preset."
                : "Nazwij wariant, dodaj opis i zapisz go do wlasnej biblioteki.";
        }

        private static SchemaParameters ClonePameters(SchemaParameters src)
        {
            if (src == null) return new SchemaParameters();
            return new SchemaParameters
            {
                trackCount = src.trackCount,
                trackSpacing = src.trackSpacing,
                trackSpacings = src.trackSpacings != null ? (float[])src.trackSpacings.Clone() : null,
                turnoutType = src.turnoutType,
                turnoutTypes = src.turnoutTypes != null ? (string[])src.turnoutTypes.Clone() : null,
                mirror = src.mirror,
            };
        }

        private void CreateFieldLabel(Transform parent, string text)
        {
            var go = new GameObject("FieldLabel");
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 13f;
            label.alignment = TextAlignmentOptions.BottomLeft;
            UITheme.ApplyTmpText(label, UIThemeTextRole.Secondary);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 22f;
        }

        private TMP_InputField CreateInputField(Transform parent, string placeholder, int maxLength)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);

            var background = go.AddComponent<Image>();
            UITheme.ApplySurface(background, UITheme.SecondarySurface, UIShapePreset.Inset);

            var inputField = go.AddComponent<TMP_InputField>();
            inputField.targetGraphic = background;
            inputField.characterLimit = maxLength;

            var textAreaGO = new GameObject("TextArea");
            textAreaGO.transform.SetParent(go.transform, false);
            var textAreaRT = textAreaGO.AddComponent<RectTransform>();
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.offsetMin = new Vector2(10f, 5f);
            textAreaRT.offsetMax = new Vector2(-10f, -5f);
            textAreaGO.AddComponent<RectMask2D>();

            var placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(textAreaGO.transform, false);
            StretchFull(placeholderGO);
            var placeholderText = placeholderGO.AddComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.fontSize = 13f;
            placeholderText.alignment = TextAlignmentOptions.MidlineLeft;
            placeholderText.fontStyle = FontStyles.Italic;
            UITheme.ApplyTmpText(placeholderText, UIThemeTextRole.Secondary);
            placeholderText.color = UITheme.WithAlpha(UITheme.SecondaryText, 0.72f);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(textAreaGO.transform, false);
            StretchFull(textGO);
            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.fontSize = 13f;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(text, UIThemeTextRole.Primary);

            inputField.textViewport = textAreaRT;
            inputField.textComponent = text;
            inputField.placeholder = placeholderText;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 35f;
            return inputField;
        }

        private Toggle CreateDisabledToggle(Transform parent, string label)
        {
            var go = new GameObject("DisabledToggle");
            go.transform.SetParent(parent, false);

            var toggle = go.AddComponent<Toggle>();
            toggle.isOn = false;
            toggle.interactable = false;

            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(go.transform, false);
            var bgRT = bgGO.AddComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0f, 0.5f);
            bgRT.anchorMax = new Vector2(0f, 0.5f);
            bgRT.pivot = new Vector2(0f, 0.5f);
            bgRT.sizeDelta = new Vector2(20f, 20f);
            bgRT.anchoredPosition = new Vector2(5f, 0f);
            var bgImg = bgGO.AddComponent<Image>();
            UITheme.ApplySurface(bgImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.7f), UIShapePreset.Inset);

            var checkGO = new GameObject("Checkmark");
            checkGO.transform.SetParent(bgGO.transform, false);
            var checkRT = checkGO.AddComponent<RectTransform>();
            checkRT.anchorMin = Vector2.zero;
            checkRT.anchorMax = Vector2.one;
            checkRT.offsetMin = new Vector2(2f, 2f);
            checkRT.offsetMax = new Vector2(-2f, -2f);
            var checkImg = checkGO.AddComponent<Image>();
            UITheme.ApplySurface(checkImg, UITheme.WithAlpha(UITheme.DisabledText, 0.55f), UIShapePreset.Inset);

            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = new Vector2(35f, 0f);
            labelRT.offsetMax = Vector2.zero;
            var labelText = labelGO.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 12f;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.fontStyle = FontStyles.Italic;
            UITheme.ApplyTmpText(labelText, UIThemeTextRole.Disabled);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 30f;
            return toggle;
        }

        private Button CreateButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick, bool primary)
        {
            var go = new GameObject("Button");
            go.transform.SetParent(parent, false);

            Color normal = primary ? UITheme.PrimaryAccent : UITheme.SecondarySurface;
            Color highlighted = primary ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface;
            Color pressed = primary ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border;

            var image = go.AddComponent<Image>();
            UITheme.ApplySurface(image, normal, primary ? UIShapePreset.Pill : UIShapePreset.Button);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = UITheme.CreateColorBlock(normal, highlighted, pressed, highlighted, UITheme.WithAlpha(UITheme.Border, 0.55f));
            button.onClick.AddListener(onClick);

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            StretchFull(labelGO);
            var label = labelGO.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 14f;
            label.alignment = TextAlignmentOptions.Center;
            label.fontStyle = FontStyles.Bold;
            UITheme.ApplyTmpText(label, UIThemeTextRole.Primary);

            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.preferredHeight = 40f;
            return button;
        }

        private TMP_Text CreateTmpLabel(Transform parent, string value, float fontSize, UIThemeTextRole role, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = value;
            label.fontSize = fontSize;
            label.fontStyle = style;
            UITheme.ApplyTmpText(label, role);
            return label;
        }

        private static void StretchFull(GameObject target)
        {
            var rect = target.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
