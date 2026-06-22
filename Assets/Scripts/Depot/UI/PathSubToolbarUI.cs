using System.Collections.Generic;
using TMPro;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    public class PathSubToolbarUI : MonoBehaviour
    {
        [SerializeField] private Color activeColor = default;
        [SerializeField] private Color normalColor = default;

        private sealed class PathSubButton
        {
            public PathBuildSubMode Mode;
            public DepotOptionButtonParts Parts;
        }

        private GameObject panel;
        private GameObject buttonStrip;
        private TMP_Text sectionTitleText;
        private TMP_Text activeModeText;
        private readonly List<PathSubButton> buttons = new();

        private static readonly (PathBuildSubMode mode, string icon, string label)[] SubDefs =
        {
            (PathBuildSubMode.Path, "P", "Sciezka"),
            (PathBuildSubMode.Road, "D", "Droga"),
            (PathBuildSubMode.Parking, "PK", "Parking"),
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
                DepotUIManager.Instance.OnPathSubModeChanged += OnSubModeChanged;
            }

            if (panel != null)
                panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (DepotUIManager.Instance != null)
            {
                DepotUIManager.Instance.OnToolChanged -= OnToolChanged;
                DepotUIManager.Instance.OnPathSubModeChanged -= OnSubModeChanged;
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
            bool show = mode == ToolMode.BuildPath;
            if (panel != null)
                panel.SetActive(show);

            if (show && DepotUIManager.Instance != null)
            {
                DepotUIManager.Instance.CurrentPathSubMode = PathBuildSubMode.Path;
                UpdateButtonVisuals();
            }
        }

        private void OnSubModeChanged(PathBuildSubMode mode)
        {
            UpdateButtonVisuals();
        }

        private void OnButtonClicked(PathSubButton button)
        {
            if (DepotUIManager.Instance == null)
                return;

            DepotUIManager.Instance.CurrentPathSubMode = button.Mode;
        }

        private void UpdateButtonVisuals()
        {
            PathBuildSubMode current = DepotUIManager.Instance != null
                ? DepotUIManager.Instance.CurrentPathSubMode
                : PathBuildSubMode.Path;

            foreach (PathSubButton button in buttons)
            {
                DepotUIPanelPrimitives.ApplyOptionButtonState(
                    button.Parts,
                    button.Mode == current,
                    true,
                    activeColor,
                    normalColor,
                    normalColor);
            }

            if (sectionTitleText != null)
                sectionTitleText.text = "Komunikacja";

            if (activeModeText != null)
                activeModeText.text = $"Wybrano: {ModeLabel(current)}";
        }

        private void BuildUI()
        {
            RectTransform root = GetComponent<RectTransform>();
            if (root == null)
                root = gameObject.AddComponent<RectTransform>();

            DepotUIPanelPrimitives.ConfigureSubToolbarRoot(root);
            panel = DepotUIPanelPrimitives.CreateHorizontalPanel(transform, "PathSubPanel", UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Lg, UITheme.Spacing.Sm, UITheme.Spacing.Sm), UITheme.Spacing.Sm);

            CreateSummaryCard();
            CreateButtonStrip();

            foreach (var (mode, icon, label) in SubDefs)
                CreateButton(mode, icon, label);
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

            var background = summaryObject.AddComponent<Image>();
            UITheme.ApplySurface(background, UITheme.TopBarInset, UIShapePreset.Inset);

            var verticalLayout = summaryObject.AddComponent<VerticalLayoutGroup>();
            verticalLayout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            verticalLayout.spacing = 0f;
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.childAlignment = TextAnchor.MiddleLeft;

            sectionTitleText = DepotUIPanelPrimitives.CreateTMP("Title", summaryObject.transform, 10f, TextAlignmentOptions.Left, FontStyles.Bold);
            sectionTitleText.text = "Komunikacja";
            sectionTitleText.color = UITheme.SecondaryText;
            sectionTitleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 12f;

            activeModeText = DepotUIPanelPrimitives.CreateTMP("State", summaryObject.transform, 12f, TextAlignmentOptions.Left, FontStyles.Bold);
            activeModeText.text = "Wybrano: Sciezka";
            activeModeText.color = UITheme.PrimaryText;
            activeModeText.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;
        }

        private void CreateButtonStrip()
        {
            buttonStrip = new GameObject("ButtonStrip");
            buttonStrip.transform.SetParent(panel.transform, false);
            buttonStrip.AddComponent<RectTransform>();

            var layoutElement = buttonStrip.AddComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            layoutElement.preferredHeight = 36f;

            var background = buttonStrip.AddComponent<Image>();
            UITheme.ApplySurface(background, UITheme.WithAlpha(UITheme.PrimarySurface, 0.72f), UIShapePreset.Inset);

            var layout = buttonStrip.AddComponent<HorizontalLayoutGroup>();
            layout.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs);
            layout.spacing = UITheme.Spacing.Sm;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;
        }

        private void CreateButton(PathBuildSubMode mode, string icon, string label)
        {
            DepotOptionButtonParts parts = DepotUIPanelPrimitives.CreateOptionButton(buttonStrip.transform, $"PathSub_{mode}", icon, label, 96f, 44f);
            var button = new PathSubButton
            {
                Mode = mode,
                Parts = parts
            };

            parts.Button.onClick.AddListener(() => OnButtonClicked(button));
            buttons.Add(button);
        }

        private static string ModeLabel(PathBuildSubMode mode) => mode switch
        {
            PathBuildSubMode.Path => "Sciezka",
            PathBuildSubMode.Road => "Droga",
            PathBuildSubMode.Parking => "Parking",
            _ => "Sciezka"
        };
    }
}
