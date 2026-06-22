using System.Collections.Generic;
using TMPro;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    public class CatenarySubToolbarUI : MonoBehaviour
    {
        [SerializeField] private Color activeColor = default;
        [SerializeField] private Color normalColor = default;

        private sealed class CatenarySubButton
        {
            public CatenaryBuildSubMode Mode;
            public DepotOptionButtonParts Parts;
        }

        private GameObject panel;
        private GameObject buttonStrip;
        private TMP_Text sectionTitleText;
        private TMP_Text activeModeText;
        private readonly List<CatenarySubButton> buttons = new();

        private static readonly (CatenaryBuildSubMode mode, string icon, string label)[] SubDefs =
        {
            (CatenaryBuildSubMode.AddCatenary, "ON", "Dodaj siec"),
            (CatenaryBuildSubMode.RemoveCatenary, "OFF", "Zdejmij siec"),
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
                DepotUIManager.Instance.OnCatenarySubModeChanged += OnSubModeChanged;
            }

            if (panel != null)
                panel.SetActive(false);
        }

        void OnDestroy()
        {
            if (DepotUIManager.Instance != null)
            {
                DepotUIManager.Instance.OnToolChanged -= OnToolChanged;
                DepotUIManager.Instance.OnCatenarySubModeChanged -= OnSubModeChanged;
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
            bool show = mode == ToolMode.BuildCatenary;
            if (panel != null)
                panel.SetActive(show);

            if (show && DepotUIManager.Instance != null)
            {
                DepotUIManager.Instance.CurrentCatenarySubMode = CatenaryBuildSubMode.AddCatenary;
                UpdateButtonVisuals();
            }
        }

        private void OnSubModeChanged(CatenaryBuildSubMode mode)
        {
            UpdateButtonVisuals();
        }

        private void OnButtonClicked(CatenarySubButton button)
        {
            if (DepotUIManager.Instance == null)
                return;

            DepotUIManager.Instance.CurrentCatenarySubMode = button.Mode;
        }

        private void UpdateButtonVisuals()
        {
            CatenaryBuildSubMode current = DepotUIManager.Instance != null
                ? DepotUIManager.Instance.CurrentCatenarySubMode
                : CatenaryBuildSubMode.AddCatenary;

            foreach (CatenarySubButton button in buttons)
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
                sectionTitleText.text = "Siec trakcyjna";

            if (activeModeText != null)
                activeModeText.text = $"Wybrano: {ModeLabel(current)}";
        }

        private void BuildUI()
        {
            RectTransform root = GetComponent<RectTransform>();
            if (root == null)
                root = gameObject.AddComponent<RectTransform>();

            DepotUIPanelPrimitives.ConfigureSubToolbarRoot(root);
            panel = DepotUIPanelPrimitives.CreateHorizontalPanel(transform, "CatenarySubPanel", UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Lg, UITheme.Spacing.Sm, UITheme.Spacing.Sm), UITheme.Spacing.Sm);

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
            sectionTitleText.text = "Siec trakcyjna";
            sectionTitleText.color = UITheme.SecondaryText;
            sectionTitleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 12f;

            activeModeText = DepotUIPanelPrimitives.CreateTMP("State", summaryObject.transform, 12f, TextAlignmentOptions.Left, FontStyles.Bold);
            activeModeText.text = "Wybrano: Dodaj siec";
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

        private void CreateButton(CatenaryBuildSubMode mode, string icon, string label)
        {
            DepotOptionButtonParts parts = DepotUIPanelPrimitives.CreateOptionButton(buttonStrip.transform, $"CatSub_{mode}", icon, label, 118f, 44f);
            var button = new CatenarySubButton
            {
                Mode = mode,
                Parts = parts
            };

            parts.Button.onClick.AddListener(() => OnButtonClicked(button));
            buttons.Add(button);
        }

        private static string ModeLabel(CatenaryBuildSubMode mode) => mode switch
        {
            CatenaryBuildSubMode.AddCatenary => "Dodaj siec",
            CatenaryBuildSubMode.RemoveCatenary => "Zdejmij siec",
            _ => "Dodaj siec"
        };
    }
}
