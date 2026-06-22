using System.Collections.Generic;
using TMPro;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    public class RoomBuildPanelUI : MonoBehaviour
    {
        [SerializeField] private Color panelBg = default;
        [SerializeField] private Color headerBg = default;
        [SerializeField] private Color activeColor = default;
        [SerializeField] private Color normalColor = default;

        private static readonly Dictionary<RoomBuildSubMode, (RoomActionMode action, string icon, string label)[]> ActionDefs
            = new()
        {
            [RoomBuildSubMode.Hall] = new[]
            {
                (RoomActionMode.BuildRoom, "HAL", "Buduj hale"),
                (RoomActionMode.PlaceHallTrack, "TOR", "Tor w hali"),
                (RoomActionMode.ElectrifyHall, "EL", "Elektryfikacja"),
                // 2026-05-08: PlaceWashBay/PlaceLift usunięte — myjnia/podnośnik teraz w
                // BuildTrack sub-mode (WSH/POD outdoor) lub przez meble wash_gate/pit_*
                // w sekcji "Meble" tego samego sub-mode'a Hala (post-MM-9).
            },
            [RoomBuildSubMode.Storage] = new[] { (RoomActionMode.BuildRoom, "MAG", "Buduj magazyn") },
            [RoomBuildSubMode.Dispatcher] = new[] { (RoomActionMode.BuildRoom, "DSP", "Buduj dyspozytornie") },
            [RoomBuildSubMode.Office] = new[] { (RoomActionMode.BuildRoom, "BIO", "Buduj biuro") },
            [RoomBuildSubMode.Social] = new[] { (RoomActionMode.BuildRoom, "SOC", "Buduj socjalny") },
            [RoomBuildSubMode.Supervisor] = new[] { (RoomActionMode.BuildRoom, "NAC", "Buduj naczelnika") },
            [RoomBuildSubMode.Bathroom] = new[] { (RoomActionMode.BuildRoom, "LAZ", "Buduj lazienke") },
            [RoomBuildSubMode.Locker] = new[] { (RoomActionMode.BuildRoom, "SZT", "Buduj szatnie") },
            [RoomBuildSubMode.Corridor] = new[] { (RoomActionMode.BuildRoom, "KOR", "Buduj korytarz") },
            [RoomBuildSubMode.TrafficController] = new[] { (RoomActionMode.BuildRoom, "DR", "Buduj dyzurnego") },
        };

        private sealed class RoomActionButton
        {
            public RoomActionMode Action;
            public DepotOptionButtonParts Parts;
        }

        private GameObject panel;
        private GameObject actionsCard;
        private GameObject buttonContainer;
        private TMP_Text headerLabel;
        private TMP_Text descriptionLabel;
        private TMP_Text activeActionLabel;
        private TMP_Text actionsCountLabel;
        private readonly List<RoomActionButton> actionButtons = new();

        void Awake()
        {
            ApplyDefaultPalette();
            BuildUI();
        }

        void Start()
        {
            SubscribeEvents();
            if (panel != null)
                panel.SetActive(false);
        }

        void OnDestroy()
        {
            var manager = DepotUIManager.Instance;
            if (manager == null)
                return;

            manager.OnToolChanged -= OnToolChanged;
            manager.OnRoomSubModeChanged -= OnRoomSubModeChanged;
            manager.OnRoomActionChanged -= OnRoomActionChanged;
        }

        private void ApplyDefaultPalette()
        {
            if (panelBg == default)
                panelBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
            if (headerBg == default)
                headerBg = UITheme.TopBarInset;
            if (activeColor == default)
                activeColor = UITheme.PrimaryAccent;
            if (normalColor == default)
                normalColor = UITheme.SecondarySurface;
        }

        private void SubscribeEvents()
        {
            var manager = DepotUIManager.Instance;
            if (manager == null)
                return;

            manager.OnToolChanged += OnToolChanged;
            manager.OnRoomSubModeChanged += OnRoomSubModeChanged;
            manager.OnRoomActionChanged += OnRoomActionChanged;
        }

        private void OnToolChanged(ToolMode mode)
        {
            bool show = mode == ToolMode.BuildRoom;
            if (panel != null)
                panel.SetActive(show);

            if (show)
                RebuildContent();
        }

        private void OnRoomSubModeChanged(RoomBuildSubMode mode)
        {
            RebuildContent();
        }

        private void OnRoomActionChanged(RoomActionMode action)
        {
            UpdateButtonVisuals();
        }

        private void RebuildContent()
        {
            if (DepotUIManager.Instance == null || buttonContainer == null)
                return;

            foreach (Transform child in buttonContainer.transform)
                Destroy(child.gameObject);
            actionButtons.Clear();

            RoomBuildSubMode subMode = DepotUIManager.Instance.CurrentRoomSubMode;
            if (headerLabel != null)
                headerLabel.text = SubModeLabel(subMode);
            if (descriptionLabel != null)
                descriptionLabel.text = SubModeDescription(subMode);

            if (!ActionDefs.TryGetValue(subMode, out var defs))
                defs = System.Array.Empty<(RoomActionMode, string, string)>();

            foreach (var (action, icon, label) in defs)
                CreateActionButton(action, icon, label);

            // MF-Furniture reorg (2026-05-03): meble per RoomType są tu, nie w osobnym
            // ToolMode.Furniture. Filtruj FurnitureCatalog po IsCompatibleWith(roomTypeStr).
            int furnitureCount = AppendFurnitureButtons(MapSubModeToRoomType(subMode));

            if (actionsCountLabel != null)
                actionsCountLabel.text = $"Akcje: {defs.Length + furnitureCount}";

            UpdateButtonVisuals();
        }

        /// <summary>
        /// MF-Furniture reorg: dorzuca buttony per furniture item compatible z danym
        /// RoomType (string). Klik wywołuje FurniturePlacer.StartPlacement bez zmiany
        /// ToolMode (gracz zostaje w BuildRoom). Drzwi (specialPlacement=WallCell) też
        /// pokazujemy — Placer deleguje do DoorPlacer wewnętrznie.
        /// Zwraca liczbę dorzuconych buttonów (do actionsCountLabel).
        /// </summary>
        private int AppendFurnitureButtons(string roomTypeStr)
        {
            if (string.IsNullOrEmpty(roomTypeStr)) return 0;
            if (!FurnitureCatalog.IsLoaded) FurnitureCatalog.LoadAll();

            int count = 0;
            foreach (var item in FurnitureCatalog.AllItems)
            {
                if (item == null) continue;
                if (!item.IsCompatibleWith(roomTypeStr)) continue;

                string icon = MakeFurnitureIcon(item);
                CreateFurnitureActionButton(item, icon, item.displayName);
                count++;
            }
            return count;
        }

        /// <summary>3-letterowe icon shortcut z displayName (uppercase).</summary>
        private static string MakeFurnitureIcon(FurnitureItem item)
        {
            string src = !string.IsNullOrEmpty(item.displayName) ? item.displayName : item.id;
            string clean = src.Replace(" ", "").Replace("(", "").Replace(")", "");
            int len = Mathf.Min(3, clean.Length);
            return clean.Substring(0, len).ToUpperInvariant();
        }

        /// <summary>Mapping RoomBuildSubMode → RoomType.ToString() dla FurnitureItem.IsCompatibleWith.</summary>
        private static string MapSubModeToRoomType(RoomBuildSubMode mode) => mode switch
        {
            RoomBuildSubMode.Hall => RoomType.Hall.ToString(),
            RoomBuildSubMode.Storage => RoomType.Storage.ToString(),
            RoomBuildSubMode.Dispatcher => RoomType.Dispatcher.ToString(),
            RoomBuildSubMode.Office => RoomType.Office.ToString(),
            RoomBuildSubMode.Social => RoomType.Social.ToString(),
            RoomBuildSubMode.Supervisor => RoomType.Supervisor.ToString(),
            RoomBuildSubMode.Bathroom => RoomType.Bathroom.ToString(),
            RoomBuildSubMode.Locker => RoomType.Locker.ToString(),
            RoomBuildSubMode.Corridor => RoomType.Corridor.ToString(),
            RoomBuildSubMode.TrafficController => RoomType.TrafficController.ToString(),
            _ => null
        };

        private void UpdateButtonVisuals()
        {
            RoomActionMode currentAction = DepotUIManager.Instance != null
                ? DepotUIManager.Instance.CurrentRoomAction
                : RoomActionMode.None;

            foreach (RoomActionButton button in actionButtons)
            {
                DepotUIPanelPrimitives.ApplyOptionButtonState(
                    button.Parts,
                    button.Action == currentAction,
                    true,
                    activeColor,
                    normalColor,
                    normalColor);
            }

            if (activeActionLabel != null)
                activeActionLabel.text = currentAction == RoomActionMode.None
                    ? "Aktywna akcja: brak"
                    : $"Aktywna akcja: {ActionLabel(currentAction)}";
        }

        private void OnButtonClicked(RoomActionMode action)
        {
            if (DepotUIManager.Instance == null)
                return;

            if (DepotUIManager.Instance.CurrentRoomAction == action)
                DepotUIManager.Instance.CurrentRoomAction = RoomActionMode.None;
            else
                DepotUIManager.Instance.CurrentRoomAction = action;
        }

        private void BuildUI()
        {
            RectTransform root = GetComponent<RectTransform>();
            if (root == null)
                root = gameObject.AddComponent<RectTransform>();

            root.anchorMin = new Vector2(1f, 0f);
            root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 0f);
            root.offsetMin = new Vector2(-236f, DepotUILayout.SubToolbarTop);
            root.offsetMax = new Vector2(0f, -52f);

            panel = new GameObject("RoomBuildPanel");
            panel.transform.SetParent(transform, false);

            RectTransform panelRT = panel.AddComponent<RectTransform>();
            DepotUIPanelPrimitives.Stretch(panelRT);

            var panelImage = panel.AddComponent<Image>();
            UITheme.ApplySurface(panelImage, panelBg, UIShapePreset.Panel);

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Md);
            layout.spacing = UITheme.Spacing.Sm;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;

            CreateContextCard();
            CreateActionsCard();
        }

        private void CreateContextCard()
        {
            var contextCard = new GameObject("ContextCard");
            contextCard.transform.SetParent(panel.transform, false);
            contextCard.AddComponent<RectTransform>();

            var layoutElement = contextCard.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 126f;

            var image = contextCard.AddComponent<Image>();
            UITheme.ApplySurface(image, headerBg, UIShapePreset.Inset);

            var verticalLayout = contextCard.AddComponent<VerticalLayoutGroup>();
            verticalLayout.padding = UITheme.Padding(UITheme.Spacing.Md);
            verticalLayout.spacing = UITheme.Spacing.Xs;
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.childAlignment = TextAnchor.UpperLeft;

            var titleLabel = DepotUIPanelPrimitives.CreateTMP("Title", contextCard.transform, 11f, TextAlignmentOptions.Left, FontStyles.Bold);
            titleLabel.text = "Pomieszczenia";
            titleLabel.color = UITheme.SecondaryText;
            titleLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

            headerLabel = DepotUIPanelPrimitives.CreateTMP("HeaderText", contextCard.transform, 15f, TextAlignmentOptions.Left, FontStyles.Bold);
            headerLabel.text = "Pomieszczenie";
            headerLabel.color = UITheme.PrimaryText;
            headerLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

            descriptionLabel = DepotUIPanelPrimitives.CreateTMP("Description", contextCard.transform, 11f, TextAlignmentOptions.Left, FontStyles.Normal);
            descriptionLabel.text = "Wybierz wariant i akcje budowy.";
            descriptionLabel.color = UITheme.SecondaryText;
            descriptionLabel.textWrappingMode = TextWrappingModes.Normal;
            descriptionLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

            var statusRow = new GameObject("StatusRow");
            statusRow.transform.SetParent(contextCard.transform, false);
            statusRow.AddComponent<RectTransform>();
            statusRow.AddComponent<LayoutElement>().preferredHeight = 28f;

            var rowLayout = statusRow.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = UITheme.Spacing.Sm;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = true;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;

            actionsCountLabel = CreateStatusPill(statusRow.transform, "ActionsCount", "Akcje: 0", false);
            activeActionLabel = CreateStatusPill(statusRow.transform, "ActiveAction", "Aktywna akcja: brak", true);
        }

        private void CreateActionsCard()
        {
            actionsCard = new GameObject("ActionsCard");
            actionsCard.transform.SetParent(panel.transform, false);
            actionsCard.AddComponent<RectTransform>();

            var cardLayout = actionsCard.AddComponent<LayoutElement>();
            cardLayout.flexibleHeight = 1f;
            cardLayout.minHeight = 180f;

            var cardImage = actionsCard.AddComponent<Image>();
            UITheme.ApplySurface(cardImage, UITheme.WithAlpha(UITheme.PrimarySurface, 0.76f), UIShapePreset.Inset);

            var verticalLayout = actionsCard.AddComponent<VerticalLayoutGroup>();
            verticalLayout.padding = UITheme.Padding(UITheme.Spacing.Md);
            verticalLayout.spacing = UITheme.Spacing.Sm;
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.childAlignment = TextAnchor.UpperLeft;

            DepotUIPanelPrimitives.CreateSectionHeader(actionsCard.transform, "ActionsHeader", "Dostepne akcje", 28f, TextAlignmentOptions.Left);

            buttonContainer = new GameObject("ButtonContainer");
            buttonContainer.transform.SetParent(actionsCard.transform, false);
            buttonContainer.AddComponent<RectTransform>();

            var buttonContainerLayout = buttonContainer.AddComponent<LayoutElement>();
            buttonContainerLayout.flexibleHeight = 1f;

            // Reorg 2026-05-03: siatka 3 kolumny (zamiast pionowej kolumny po jednym buttonie).
            // Panel ma ~216px usable width (236-padding). 3 × 64 + 2 × 6 spacing = 204px → mieści się.
            var grid = buttonContainer.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(0, 0, 0, 0);
            grid.cellSize = new Vector2(64f, 52f);
            grid.spacing = new Vector2(6f, 6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperCenter;

            buttonContainer.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void CreateActionButton(RoomActionMode action, string icon, string label)
        {
            // Width 64f tylko dla preferredSize w LayoutElement — GridLayoutGroup nadpisuje
            // przez cellSize. Wartość zachowana dla spójności z innymi kontrolerami.
            DepotOptionButtonParts parts = DepotUIPanelPrimitives.CreateOptionButton(
                buttonContainer.transform,
                $"Action_{action}",
                icon,
                label,
                64f,
                52f);

            var button = new RoomActionButton
            {
                Action = action,
                Parts = parts
            };

            parts.Button.onClick.AddListener(() => OnButtonClicked(button.Action));
            actionButtons.Add(button);
        }

        /// <summary>
        /// MF-Furniture reorg: button dla furniture item (klik startuje placement bez
        /// zmiany ToolMode). Wizualnie nie traktujemy jak "active room action" — RoomActionMode
        /// pozostaje None, gracz w trybie BuildRoom + furniture preview cuboid pod kursorem.
        /// </summary>
        private void CreateFurnitureActionButton(FurnitureItem item, string icon, string label)
        {
            DepotOptionButtonParts parts = DepotUIPanelPrimitives.CreateOptionButton(
                buttonContainer.transform,
                $"Furniture_{item.id}",
                icon,
                label,
                64f,
                52f);

            var captured = item;
            parts.Button.onClick.AddListener(() => OnFurnitureButtonClicked(captured));
        }

        private void OnFurnitureButtonClicked(FurnitureItem item)
        {
            if (item == null) return;
            // Anuluj jakąkolwiek aktywną room action (np. PlaceHallTrack) — meble są niezależne
            if (DepotUIManager.Instance != null)
                DepotUIManager.Instance.CurrentRoomAction = RoomActionMode.None;

            // Lazy-create placera — RoomBuildPanelUI jest entry point dla mebli po reorg M-Furniture
            var placer = FurniturePlacer.Instance;
            if (placer == null)
            {
                var go = new GameObject("FurniturePlacer (auto-created by RoomBuildPanelUI)");
                placer = go.AddComponent<FurniturePlacer>();
            }
            placer.StartPlacement(item, OwnershipService.LocalDepotId);
        }

        private static string SubModeLabel(RoomBuildSubMode mode) => mode switch
        {
            RoomBuildSubMode.Hall => "Hala",
            RoomBuildSubMode.Storage => "Magazyn",
            RoomBuildSubMode.Dispatcher => "Dyspozytor",
            RoomBuildSubMode.Office => "Biuro",
            RoomBuildSubMode.Social => "Socjalny",
            RoomBuildSubMode.Supervisor => "Naczelnik",
            RoomBuildSubMode.Bathroom => "Lazienka",
            RoomBuildSubMode.Locker => "Szatnia",
            RoomBuildSubMode.Corridor => "Korytarz",
            RoomBuildSubMode.TrafficController => "Dyzurny ruchu",
            _ => "Pomieszczenie"
        };

        private static string SubModeDescription(RoomBuildSubMode mode) => mode switch
        {
            RoomBuildSubMode.Hall => "Buduj hale oraz wyposazenie potrzebne do obslugi taboru.",
            RoomBuildSubMode.Storage => "Tworz magazyny i zaplecze dla czesci oraz materialow.",
            RoomBuildSubMode.Dispatcher => "Przygotuj przestrzen dla koordynacji ruchu i planowania pracy.",
            RoomBuildSubMode.Office => "Dodaj biura dla codziennej administracji i zarzadzania.",
            RoomBuildSubMode.Social => "Organizuj przestrzen socjalna dla zalogi.",
            RoomBuildSubMode.Supervisor => "Wydziel pokoj naczelnika i nadzoru operacyjnego.",
            RoomBuildSubMode.Bathroom => "Rozmieszczaj lazienki i podstawowe zaplecze sanitarne.",
            RoomBuildSubMode.Locker => "Dodaj szatnie dla personelu.",
            RoomBuildSubMode.Corridor => "Lacz pomieszczenia czytelnymi traktami komunikacyjnymi.",
            RoomBuildSubMode.TrafficController => "Przygotuj stanowisko dyzurnego ruchu.",
            _ => "Wybierz wariant i akcje budowy."
        };

        private static string ActionLabel(RoomActionMode action) => action switch
        {
            RoomActionMode.BuildRoom => "budowa pomieszczenia",
            RoomActionMode.PlaceHallTrack => "tor w hali",
            RoomActionMode.ElectrifyHall => "elektryfikacja hali",
            _ => "brak"
        };

        private TMP_Text CreateStatusPill(Transform parent, string objectName, string text, bool flexible)
        {
            var pill = new GameObject(objectName);
            pill.transform.SetParent(parent, false);
            pill.AddComponent<RectTransform>();

            var layout = pill.AddComponent<LayoutElement>();
            layout.preferredHeight = 28f;
            layout.flexibleWidth = flexible ? 1f : 0f;

            var image = pill.AddComponent<Image>();
            UITheme.ApplySurface(image, UITheme.WithAlpha(UITheme.PrimarySurface, 0.9f), UIShapePreset.Pill);

            var label = DepotUIPanelPrimitives.CreateTMP("Label", pill.transform, 10f, TextAlignmentOptions.Center, FontStyles.Bold);
            label.text = text;
            label.color = UITheme.PrimaryText;
            DepotUIPanelPrimitives.Stretch(label.rectTransform);
            return label;
        }
    }
}
