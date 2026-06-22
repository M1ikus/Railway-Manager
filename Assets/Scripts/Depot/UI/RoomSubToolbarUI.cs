using System.Collections.Generic;
using TMPro;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    public class RoomSubToolbarUI : MonoBehaviour
    {
        [SerializeField] private Color activeColor = default;
        [SerializeField] private Color normalColor = default;
        [SerializeField] private Color lockedColor = default;

        private sealed class RoomSubButton
        {
            public RoomBuildSubMode Mode;
            public DepotOptionButtonParts Parts;
            public bool Unlocked;
        }

        private GameObject panel;
        private GameObject buttonStrip;
        private TMP_Text sectionTitleText;
        private TMP_Text activeModeText;
        private readonly List<RoomSubButton> buttons = new();

        private static readonly (RoomBuildSubMode mode, string icon, string label, bool unlocked)[] SubDefs =
        {
            (RoomBuildSubMode.Hall, "HAL", "Hala", true),
            (RoomBuildSubMode.Storage, "MAG", "Magazyn", true),
            (RoomBuildSubMode.Dispatcher, "DSP", "Dyspozytor", true),
            (RoomBuildSubMode.Office, "BIO", "Biuro", true),
            (RoomBuildSubMode.Social, "SOC", "Socjalny", true),
            (RoomBuildSubMode.Supervisor, "NAC", "Naczelnik", true),
            (RoomBuildSubMode.Bathroom, "LAZ", "Lazienka", true),
            (RoomBuildSubMode.Locker, "SZT", "Szatnia", true),
            (RoomBuildSubMode.Corridor, "KOR", "Korytarz", true),
            (RoomBuildSubMode.TrafficController, "DR", "Dyzurny ruchu", false),
        };

        void Awake()
        {
            ApplyDefaultPalette();
            BuildUI();
        }

        void Start()
        {
            if (DepotUIManager.Instance != null)
            {
                DepotUIManager.Instance.OnToolChanged += OnToolChanged;
                DepotUIManager.Instance.OnRoomSubModeChanged += OnSubModeChanged;
            }

            if (panel != null)
                panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (DepotUIManager.Instance != null)
            {
                DepotUIManager.Instance.OnToolChanged -= OnToolChanged;
                DepotUIManager.Instance.OnRoomSubModeChanged -= OnSubModeChanged;
            }
        }

        private void ApplyDefaultPalette()
        {
            if (activeColor == default)
                activeColor = UITheme.PrimaryAccent;
            if (normalColor == default)
                normalColor = UITheme.SecondarySurface;
            if (lockedColor == default)
                lockedColor = UITheme.WithAlpha(UITheme.Border, 0.72f);
        }

        private void OnToolChanged(ToolMode mode)
        {
            bool show = mode == ToolMode.BuildRoom;
            if (panel != null)
                panel.SetActive(show);

            if (show && DepotUIManager.Instance != null)
            {
                DepotUIManager.Instance.CurrentRoomSubMode = RoomBuildSubMode.Hall;
                UpdateButtonVisuals();
            }
        }

        private void OnSubModeChanged(RoomBuildSubMode mode)
        {
            UpdateButtonVisuals();
        }

        private void OnButtonClicked(RoomSubButton button)
        {
            if (!button.Unlocked || DepotUIManager.Instance == null)
                return;

            DepotUIManager.Instance.CurrentRoomSubMode = button.Mode;
        }

        private void UpdateButtonVisuals()
        {
            RoomBuildSubMode current = DepotUIManager.Instance != null
                ? DepotUIManager.Instance.CurrentRoomSubMode
                : RoomBuildSubMode.Hall;

            foreach (RoomSubButton button in buttons)
            {
                DepotUIPanelPrimitives.ApplyOptionButtonState(
                    button.Parts,
                    button.Mode == current,
                    button.Unlocked,
                    activeColor,
                    normalColor,
                    lockedColor);
            }

            if (sectionTitleText != null)
                sectionTitleText.text = "Typ pomieszczenia";

            if (activeModeText != null)
                activeModeText.text = $"Wybrano: {SubModeLabel(current)}";
        }

        private void BuildUI()
        {
            RectTransform root = GetComponent<RectTransform>();
            if (root == null)
                root = gameObject.AddComponent<RectTransform>();

            DepotUIPanelPrimitives.ConfigureSubToolbarRoot(root);
            panel = DepotUIPanelPrimitives.CreateHorizontalPanel(transform, "RoomSubPanel", UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Lg, UITheme.Spacing.Sm, UITheme.Spacing.Sm), UITheme.Spacing.Sm);

            CreateSummaryCard();
            CreateButtonStrip();

            foreach (var (mode, icon, label, unlocked) in SubDefs)
                CreateButton(mode, icon, label, unlocked);
        }

        private void CreateSummaryCard()
        {
            var summaryObject = new GameObject("SummaryCard");
            summaryObject.transform.SetParent(panel.transform, false);
            summaryObject.AddComponent<RectTransform>();

            var layoutElement = summaryObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = 176f;
            layoutElement.preferredHeight = 36f;
            layoutElement.flexibleWidth = 0f;

            var background = summaryObject.AddComponent<UnityEngine.UI.Image>();
            UITheme.ApplySurface(background, UITheme.TopBarInset, UIShapePreset.Inset);

            var verticalLayout = summaryObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            verticalLayout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            verticalLayout.spacing = 0f;
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.childAlignment = TextAnchor.MiddleLeft;

            sectionTitleText = DepotUIPanelPrimitives.CreateTMP("Title", summaryObject.transform, 10f, TextAlignmentOptions.Left, FontStyles.Bold);
            sectionTitleText.text = "Typ pomieszczenia";
            sectionTitleText.color = UITheme.SecondaryText;
            sectionTitleText.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 12f;

            activeModeText = DepotUIPanelPrimitives.CreateTMP("State", summaryObject.transform, 12f, TextAlignmentOptions.Left, FontStyles.Bold);
            activeModeText.text = "Wybrano: Hala";
            activeModeText.color = UITheme.PrimaryText;
            activeModeText.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 16f;
        }

        private void CreateButtonStrip()
        {
            buttonStrip = new GameObject("ButtonStrip");
            buttonStrip.transform.SetParent(panel.transform, false);
            buttonStrip.AddComponent<RectTransform>();

            var layoutElement = buttonStrip.AddComponent<UnityEngine.UI.LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            layoutElement.preferredHeight = 36f;

            var background = buttonStrip.AddComponent<UnityEngine.UI.Image>();
            UITheme.ApplySurface(background, UITheme.WithAlpha(UITheme.PrimarySurface, 0.72f), UIShapePreset.Inset);

            var layout = buttonStrip.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs);
            layout.spacing = UITheme.Spacing.Sm;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;
        }

        private void CreateButton(RoomBuildSubMode mode, string icon, string label, bool unlocked)
        {
            DepotOptionButtonParts parts = DepotUIPanelPrimitives.CreateOptionButton(buttonStrip.transform, $"RoomSub_{mode}", icon, label, 94f, 44f);
            parts.Button.interactable = unlocked;

            var button = new RoomSubButton
            {
                Mode = mode,
                Parts = parts,
                Unlocked = unlocked
            };

            parts.Button.onClick.AddListener(() => OnButtonClicked(button));
            buttons.Add(button);
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
    }
}
