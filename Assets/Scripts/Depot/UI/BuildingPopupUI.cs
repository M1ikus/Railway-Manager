using System;
using TMPro;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Popup informacji o pomieszczeniu po kliknieciu podlogi pokoju w trybie Select.
    /// </summary>
    public class BuildingPopupUI : MonoBehaviour
    {
        [Header("Colors")]
        [SerializeField] private Color panelColor = default;
        [SerializeField] private Color headerColor = default;
        [SerializeField] private Color sectionColor = default;
        [SerializeField] private Color buttonColor = default;
        [SerializeField] private Color dangerColor = default;

        private GameObject popupPanel;
        private TextMeshProUGUI titleText;
        private TextMeshProUGUI typeText;
        private TextMeshProUGUI sizeText;
        private TextMeshProUGUI furnitureText;
        private Image typeRowImage;
        private Button changeTypeButton;
        private Button demolishButton;
        private Button closeButton;

        private RoomDetectionSystem roomSystem;
        private RoomTypePopupUI roomTypePopup;
        private WallBuildingSystem wallSystem;
        private DetectedRoom currentRoom;
        private Camera mainCamera;

        private InputActions inputActions;
        private InputActions.VehicleActions vehicleActions;
        private InputActions.UIPopupActions popupActions;

        public bool IsPopupVisible() => popupPanel != null && popupPanel.activeSelf;

        void Awake()
        {
            ApplyDefaultPalette();

            inputActions = new InputActions();
            RailwayManager.Core.Settings.RebindingService.ApplyOverridesTo(inputActions);
            vehicleActions = inputActions.Vehicle;
            popupActions = inputActions.UIPopup;
        }

        void OnEnable()
        {
            vehicleActions.Enable();
            popupActions.Enable();
        }

        void OnDisable()
        {
            vehicleActions.Disable();
            popupActions.Disable();
        }

        void OnDestroy()
        {
            inputActions?.Dispose();
        }

        void Start()
        {
            mainCamera = Camera.main;
            roomSystem = DepotServices.Get<RoomDetectionSystem>();
            wallSystem = DepotServices.Get<WallBuildingSystem>();
        }

        void Update()
        {
            if (DepotUIManager.Instance == null)
                return;

            if (DepotUIManager.Instance.CurrentTool != ToolMode.Select)
                return;

            if (Keyboard.current != null
                && Keyboard.current.escapeKey.wasPressedThisFrame
                && popupPanel != null
                && popupPanel.activeSelf)
            {
                PauseMenuUI.LastEscConsumedFrame = Time.frameCount;
                ClosePopup();
                return;
            }

            if (DepotUIManager.Instance.IsPointerOverUI())
                return;

            if (vehicleActions.Select.WasPressedThisFrame())
                TrySelectRoom();
        }

        public void ShowPopup(DetectedRoom room, Vector3 worldPos)
        {
            currentRoom = room;

            if (popupPanel == null)
                BuildUI();

            UpdateContent();

            if (mainCamera == null)
                mainCamera = Camera.main;

            if (mainCamera != null)
            {
                Vector3 screenPos = mainCamera.WorldToScreenPoint(worldPos + Vector3.up * 2f);
                popupPanel.GetComponent<RectTransform>().position = screenPos;
            }

            popupPanel.SetActive(true);
        }

        public void ClosePopup()
        {
            if (popupPanel != null)
                popupPanel.SetActive(false);

            currentRoom = null;
        }

        private void ApplyDefaultPalette()
        {
            if (panelColor == default)
                panelColor = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
            if (headerColor == default)
                headerColor = UITheme.TopBarInset;
            if (sectionColor == default)
                sectionColor = UITheme.WithAlpha(UITheme.TopBarInset, 0.95f);
            if (buttonColor == default)
                buttonColor = UITheme.SecondarySurface;
            if (dangerColor == default)
                dangerColor = UITheme.Danger;
        }

        private void TrySelectRoom()
        {
            if (mainCamera == null)
                mainCamera = Camera.main;
            if (mainCamera == null || Mouse.current == null)
                return;

            if (roomSystem == null)
                roomSystem = DepotServices.Get<RoomDetectionSystem>();
            if (wallSystem == null)
                wallSystem = DepotServices.Get<WallBuildingSystem>();

            Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f))
                return;

            if (roomSystem == null)
                return;

            DetectedRoom room = roomSystem.FindRoomByFloor(hit.collider.gameObject);
            if (room != null)
                ShowPopup(room, hit.point);
        }

        private void UpdateContent()
        {
            if (currentRoom == null)
                return;

            string typeName = LocalizationService.Get("popup_building.type_unassigned");
            if (currentRoom.roomType != RoomType.None && RoomRequirements.MinSize.ContainsKey(currentRoom.roomType))
                typeName = RoomRequirements.MinSize[currentRoom.roomType].label;

            titleText.text = string.Format(LocalizationService.Get("popup_building.title_format"), currentRoom.roomId);
            typeText.text = typeName;
            sizeText.text = $"{currentRoom.bounds.width}x{currentRoom.bounds.height} | {currentRoom.areaSqM:F0} m2";
            furnitureText.text = LocalizationService.Get("popup_building.furniture_stub");

            if (typeRowImage != null)
            {
                UITheme.ApplySurface(
                    typeRowImage,
                    UITheme.WithAlpha(GetRoomTypeColor(currentRoom.roomType), 0.34f),
                    UIShapePreset.Pill);
            }
        }

        private void BuildUI()
        {
            Canvas canvas = DepotUIManager.Instance != null ? DepotUIManager.Instance.canvas : null;
            if (canvas == null)
                return;

            popupPanel = new GameObject("BuildingPopup");
            popupPanel.transform.SetParent(canvas.transform, false);

            RectTransform panelRT = popupPanel.AddComponent<RectTransform>();
            panelRT.sizeDelta = new Vector2(376f, 300f);
            panelRT.pivot = new Vector2(0.5f, 0f);

            var panelImage = popupPanel.AddComponent<Image>();
            UITheme.ApplySurface(panelImage, panelColor, UIShapePreset.PanelLarge);

            VerticalLayoutGroup layout = popupPanel.AddComponent<VerticalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg);
            layout.spacing = UITheme.Spacing.Md;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = false;

            titleText = CreateSectionText(
                popupPanel.transform,
                "Title",
                LocalizationService.Get("popup_building.title_default"),
                17,
                40f,
                TextAlignmentOptions.Center,
                headerColor,
                UIThemeTextRole.Primary,
                FontStyles.Bold);

            GameObject summaryCard = CreateSectionCard(popupPanel.transform, "SummaryCard", 76f, 8f);
            typeText = CreateInfoRow(summaryCard.transform, "Type", "Typ", UIThemeTextRole.Primary, out typeRowImage);
            sizeText = CreateInfoRow(summaryCard.transform, "Size", "Rozmiar", UIThemeTextRole.Secondary, out _);

            GameObject detailCard = CreateSectionCard(popupPanel.transform, "DetailCard", 58f, 6f);
            furnitureText = CreateInfoRow(detailCard.transform, "Furniture", "Wyposazenie", UIThemeTextRole.Secondary, out _);

            GameObject buttonRow = CreateHorizontalRow(popupPanel.transform, "Buttons", 40f, true);
            changeTypeButton = CreateActionButton(buttonRow.transform, "ChangeType", LocalizationService.Get("popup_building.btn_change_type"), 118f, buttonColor, false, OnChangeType);
            demolishButton = CreateActionButton(buttonRow.transform, "Demolish", LocalizationService.Get("popup_building.btn_demolish"), 110f, dangerColor, true, OnDemolish);
            closeButton = CreateActionButton(buttonRow.transform, "Close", LocalizationService.Get("popup_building.btn_close"), 64f, buttonColor, false, ClosePopup);

            popupPanel.SetActive(false);
        }

        private void OnChangeType()
        {
            if (currentRoom == null)
                return;

            if (roomTypePopup == null)
                roomTypePopup = DepotServices.Get<RoomTypePopupUI>();

            if (roomTypePopup == null)
                return;

            roomTypePopup.ShowPopup(currentRoom);
            ClosePopup();
        }

        private void OnDemolish()
        {
            if (currentRoom == null)
                return;

            if (wallSystem != null && currentRoom.buildingId >= 0)
            {
                wallSystem.RemoveBuilding(currentRoom.buildingId);
                ClosePopup();
            }
        }

        private TextMeshProUGUI CreateSectionText(
            Transform parent,
            string name,
            string label,
            int fontSize,
            float height,
            TextAlignmentOptions alignment,
            Color backgroundColor,
            UIThemeTextRole textRole,
            FontStyles fontStyle)
        {
            GameObject section = new GameObject($"{name}Section");
            section.transform.SetParent(parent, false);
            section.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            section.AddComponent<LayoutElement>().preferredHeight = height;
            var background = section.AddComponent<Image>();
            UITheme.ApplySurface(background, backgroundColor, UIShapePreset.Inset);

            GameObject textObject = new GameObject(name);
            textObject.transform.SetParent(section.transform, false);
            RectTransform textRT = textObject.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(10f, 0f);
            textRT.offsetMax = new Vector2(-10f, 0f);

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(text, textRole);
            text.text = label;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.richText = false;
            return text;
        }

        private GameObject CreateSectionCard(Transform parent, string name, float height, float spacing)
        {
            GameObject card = new GameObject(name);
            card.transform.SetParent(parent, false);
            card.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            card.AddComponent<LayoutElement>().preferredHeight = height;

            var background = card.AddComponent<Image>();
            UITheme.ApplySurface(background, sectionColor, UIShapePreset.Inset);

            VerticalLayoutGroup group = card.AddComponent<VerticalLayoutGroup>();
            group.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            group.spacing = spacing;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            group.childControlWidth = true;
            group.childControlHeight = false;
            return card;
        }

        private GameObject CreateHorizontalRow(Transform parent, string name, float height, bool tintedBackground = false)
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

            HorizontalLayoutGroup group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = UITheme.Spacing.Sm;
            group.padding = tintedBackground ? UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs) : new RectOffset(0, 0, 0, 0);
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = true;
            group.childControlWidth = true;
            group.childControlHeight = true;
            return row;
        }

        private Button CreateActionButton(
            Transform parent,
            string name,
            string label,
            float preferredWidth,
            Color backgroundColor,
            bool inverseText,
            Action onClick)
        {
            GameObject buttonObject = new GameObject(name);
            buttonObject.transform.SetParent(parent, false);
            buttonObject.AddComponent<RectTransform>();

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.preferredWidth = preferredWidth;
            layout.flexibleWidth = preferredWidth < 100f ? 0f : 1f;

            Image background = buttonObject.AddComponent<Image>();
            UITheme.ApplySurface(background, backgroundColor, inverseText ? UIShapePreset.Pill : UIShapePreset.Button);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.colors = UITheme.CreateColorBlock(
                backgroundColor,
                inverseText ? UITheme.Darken(backgroundColor, 0.06f) : UITheme.RaisedSurface,
                inverseText ? UITheme.Darken(backgroundColor, 0.14f) : UITheme.Border,
                backgroundColor,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            button.onClick.AddListener(() => onClick?.Invoke());

            GameObject labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRT = labelObject.AddComponent<RectTransform>();
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.offsetMin = Vector2.zero;
            labelRT.offsetMax = Vector2.zero;

            TextMeshProUGUI text = labelObject.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(text, inverseText ? UIThemeTextRole.Inverse : UIThemeTextRole.Primary);
            text.text = label;
            text.fontSize = 12;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.richText = false;

            return button;
        }

        private TextMeshProUGUI CreateInfoRow(Transform parent, string name, string label, UIThemeTextRole valueRole, out Image rowBackground)
        {
            GameObject row = new GameObject($"{name}Row");
            row.transform.SetParent(parent, false);
            row.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, 30f);
            row.AddComponent<LayoutElement>().preferredHeight = 30f;

            rowBackground = row.AddComponent<Image>();
            UITheme.ApplySurface(rowBackground, sectionColor, UIShapePreset.Pill);

            HorizontalLayoutGroup group = row.AddComponent<HorizontalLayoutGroup>();
            group.padding = UITheme.Padding(UITheme.Spacing.Md, 0f);
            group.spacing = UITheme.Spacing.Md;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = true;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childAlignment = TextAnchor.MiddleLeft;

            GameObject labelObject = new GameObject("Label");
            labelObject.transform.SetParent(row.transform, false);
            labelObject.AddComponent<RectTransform>();
            LayoutElement labelLayout = labelObject.AddComponent<LayoutElement>();
            labelLayout.preferredWidth = 92f;

            TextMeshProUGUI labelText = labelObject.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(labelText, UIThemeTextRole.Secondary);
            labelText.text = label;
            labelText.fontSize = 11;
            labelText.fontStyle = FontStyles.Bold;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.richText = false;

            GameObject valueObject = new GameObject(name);
            valueObject.transform.SetParent(row.transform, false);
            valueObject.AddComponent<RectTransform>();
            LayoutElement valueLayout = valueObject.AddComponent<LayoutElement>();
            valueLayout.flexibleWidth = 1f;

            TextMeshProUGUI valueText = valueObject.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(valueText, valueRole);
            valueText.fontSize = 12;
            valueText.fontStyle = FontStyles.Normal;
            valueText.alignment = TextAlignmentOptions.MidlineRight;
            valueText.richText = false;

            return valueText;
        }

        private Color GetRoomTypeColor(RoomType roomType)
        {
            return roomType switch
            {
                RoomType.Hall => UITheme.Warning,
                RoomType.Storage => UITheme.Success,
                RoomType.Dispatcher => UITheme.PrimaryAccentHover,
                RoomType.Office => UITheme.PrimaryAccentHover,
                RoomType.Social => UITheme.Warning,
                RoomType.Supervisor => UITheme.PrimaryAccent,
                RoomType.Bathroom => UITheme.PrimaryAccent,
                RoomType.Locker => UITheme.SecondarySurface,
                RoomType.Corridor => UITheme.SecondarySurface,
                RoomType.TrafficController => UITheme.PrimaryAccent,
                RoomType.None => UITheme.SecondarySurface,
                _ => UITheme.SecondarySurface
            };
        }
    }
}
