using System.Collections.Generic;
using TMPro;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    public class PairSecondaryToolbarUI : MonoBehaviour
    {
        [SerializeField] private Color activeColor = default;
        [SerializeField] private Color normalColor = default;

        private sealed class PairButton
        {
            public PairSecondaryType Type;
            public DepotOptionButtonParts Parts;
        }

        private GameObject panel;
        private GameObject buttonList;
        private TMP_Text sectionTitleText;
        private TMP_Text contextText;
        private TMP_Text activeModeText;
        private readonly List<PairButton> buttons = new();

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
                DepotUIManager.Instance.OnTrackSubModeChanged += OnSubModeChanged;
                DepotUIManager.Instance.OnPairModeChanged += OnPairModeChanged;
                DepotUIManager.Instance.OnPairSecondaryChanged += OnSecondaryChanged;
            }

            if (panel != null)
                panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (DepotUIManager.Instance != null)
            {
                DepotUIManager.Instance.OnToolChanged -= OnToolChanged;
                DepotUIManager.Instance.OnTrackSubModeChanged -= OnSubModeChanged;
                DepotUIManager.Instance.OnPairModeChanged -= OnPairModeChanged;
                DepotUIManager.Instance.OnPairSecondaryChanged -= OnSecondaryChanged;
            }
        }

        private void ApplyDefaultPalette()
        {
            if (activeColor == default)
                activeColor = UITheme.PrimaryAccent;
            if (normalColor == default)
                normalColor = UITheme.SecondarySurface;
        }

        private void OnToolChanged(ToolMode mode)
        {
            UpdateVisibility();
        }

        private void OnSubModeChanged(TrackBuildSubMode mode)
        {
            UpdateVisibility();
        }

        private void OnPairModeChanged(bool active)
        {
            UpdateVisibility();
        }

        private void OnSecondaryChanged(PairSecondaryType type)
        {
            UpdateButtonVisuals();
        }

        private void UpdateVisibility()
        {
            if (panel == null)
                return;

            var manager = DepotUIManager.Instance;
            if (manager == null)
            {
                panel.SetActive(false);
                return;
            }

            bool show = manager.CurrentTool == ToolMode.BuildTrack
                && manager.PairModeActive
                && manager.CurrentTrackSubMode != TrackBuildSubMode.Track;

            panel.SetActive(show);
            if (show)
                UpdateButtonVisuals();
        }

        private void OnButtonClicked(PairButton button)
        {
            if (DepotUIManager.Instance == null)
                return;

            DepotUIManager.Instance.CurrentPairSecondary = button.Type;
        }

        private void UpdateButtonVisuals()
        {
            PairSecondaryType current = DepotUIManager.Instance != null
                ? DepotUIManager.Instance.CurrentPairSecondary
                : PairSecondaryType.SameAsPrimary;

            foreach (PairButton button in buttons)
            {
                DepotUIPanelPrimitives.ApplyOptionButtonState(
                    button.Parts,
                    button.Type == current,
                    true,
                    activeColor,
                    normalColor,
                    normalColor);
            }

            if (sectionTitleText != null)
                sectionTitleText.text = "Para rozjazdu";

            if (contextText != null)
                contextText.text = $"Glowne: {TrackModeLabel(DepotUIManager.Instance != null ? DepotUIManager.Instance.CurrentTrackSubMode : TrackBuildSubMode.TurnoutR190)}";

            if (activeModeText != null)
                activeModeText.text = $"Drugi: {PairLabel(current)}";
        }

        private void BuildUI()
        {
            RectTransform root = GetComponent<RectTransform>();
            if (root == null)
                root = gameObject.AddComponent<RectTransform>();

            root.anchorMin = new Vector2(0f, 0f);
            root.anchorMax = new Vector2(0f, 0f);
            root.pivot = new Vector2(0f, 0f);
            root.anchoredPosition = new Vector2(MainTabBarUI.PanelWidth + 8f, DepotUILayout.FloatingPanelBottom);
            root.sizeDelta = new Vector2(216f, 0f);

            panel = DepotUIPanelPrimitives.CreateVerticalPanel(transform, "PairSecondaryPanel", UITheme.Padding(UITheme.Spacing.Md), UITheme.Spacing.Sm);
            panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            CreateSummaryCard();
            CreateButtonList();

            CreateButton(PairSecondaryType.SameAsPrimary, "Jak glowny");
            CreateButton(PairSecondaryType.R190, "R190 1:9");
            CreateButton(PairSecondaryType.R300, "R300 1:9");
            CreateButton(PairSecondaryType.Crossover, "Krzyzowy R190");
        }

        private void CreateSummaryCard()
        {
            var summaryObject = new GameObject("SummaryCard");
            summaryObject.transform.SetParent(panel.transform, false);
            summaryObject.AddComponent<RectTransform>();

            var layoutElement = summaryObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 58f;

            var background = summaryObject.AddComponent<Image>();
            UITheme.ApplySurface(background, UITheme.TopBarInset, UIShapePreset.Inset);

            var verticalLayout = summaryObject.AddComponent<VerticalLayoutGroup>();
            verticalLayout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            verticalLayout.spacing = UITheme.Spacing.Xxs;
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.childAlignment = TextAnchor.MiddleLeft;

            sectionTitleText = DepotUIPanelPrimitives.CreateTMP("Title", summaryObject.transform, 11f, TextAlignmentOptions.Left, FontStyles.Bold);
            sectionTitleText.text = "Para rozjazdu";
            sectionTitleText.color = UITheme.SecondaryText;
            sectionTitleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;

            contextText = DepotUIPanelPrimitives.CreateTMP("Context", summaryObject.transform, 10f, TextAlignmentOptions.Left, FontStyles.Normal);
            contextText.text = "Glowne: Rozjazd R190";
            contextText.color = UITheme.SecondaryText;
            contextText.gameObject.AddComponent<LayoutElement>().preferredHeight = 12f;

            activeModeText = DepotUIPanelPrimitives.CreateTMP("State", summaryObject.transform, 12f, TextAlignmentOptions.Left, FontStyles.Bold);
            activeModeText.text = "Drugi: Jak glowny";
            activeModeText.color = UITheme.PrimaryText;
            activeModeText.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;
        }

        private void CreateButtonList()
        {
            buttonList = new GameObject("ButtonList");
            buttonList.transform.SetParent(panel.transform, false);
            buttonList.AddComponent<RectTransform>();

            var layoutElement = buttonList.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 168f;

            var background = buttonList.AddComponent<Image>();
            UITheme.ApplySurface(background, UITheme.WithAlpha(UITheme.PrimarySurface, 0.72f), UIShapePreset.Inset);

            var layout = buttonList.AddComponent<VerticalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Sm);
            layout.spacing = UITheme.Spacing.Sm;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childAlignment = TextAnchor.UpperLeft;
        }

        private void CreateButton(PairSecondaryType type, string label)
        {
            DepotOptionButtonParts parts = DepotUIPanelPrimitives.CreateListButton(buttonList.transform, $"PairSec_{type}", label, 34f);
            var button = new PairButton
            {
                Type = type,
                Parts = parts
            };

            parts.Button.onClick.AddListener(() => OnButtonClicked(button));
            buttons.Add(button);
        }

        private static string PairLabel(PairSecondaryType type) => type switch
        {
            PairSecondaryType.SameAsPrimary => "Jak glowny",
            PairSecondaryType.R190 => "R190 1:9",
            PairSecondaryType.R300 => "R300 1:9",
            PairSecondaryType.Crossover => "Krzyzowy R190",
            _ => "Jak glowny"
        };

        private static string TrackModeLabel(TrackBuildSubMode mode) => mode switch
        {
            TrackBuildSubMode.TurnoutR190 => "Rozjazd R190",
            TrackBuildSubMode.TurnoutR300 => "Rozjazd R300",
            TrackBuildSubMode.DoubleCrossoverR190 => "Krzyzowy",
            TrackBuildSubMode.WashZone => "Myjnia",
            TrackBuildSubMode.Turntable => "Obrotnica",
            TrackBuildSubMode.PitLift => "Kanal / podnosnik",
            TrackBuildSubMode.FuelStation => "Stacja paliw",
            TrackBuildSubMode.WaterService => "Wodowanie",
            _ => "Tor"
        };
    }
}
