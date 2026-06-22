using System.Collections.Generic;
using TMPro;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    public class TrackSubToolbarUI : MonoBehaviour
    {
        [SerializeField] private Color activeColor = default;
        [SerializeField] private Color normalColor = default;
        [SerializeField] private Color lockedColor = default;

        private sealed class TrackSubButton
        {
            public TrackBuildSubMode Mode;
            public DepotOptionButtonParts Parts;
            public bool Unlocked;
        }

        private GameObject panel;
        private GameObject buttonStrip;
        private TMP_Text sectionTitleText;
        private TMP_Text activeModeText;
        private readonly List<TrackSubButton> buttons = new();

        private static readonly (TrackBuildSubMode mode, string icon, string label, bool unlocked)[] SubDefs =
        {
            (TrackBuildSubMode.Track,               "TOR",  "Tor",               true),
            (TrackBuildSubMode.TurnoutR190,         "R19",  "R190 1:9",          true),
            (TrackBuildSubMode.TurnoutR300,         "R30",  "R300 1:9",          true),
            (TrackBuildSubMode.DoubleCrossoverR190, "KRZ",  "Krzyzowy",          true),
            (TrackBuildSubMode.Schemas,             "SCH",  "Schematy",          true),
            // Outdoor equipment placement enabled 2026-05-03 — MVP rect-z-2-klik z walidacją
            // size + cuboid placeholder (OutdoorEquipmentPlacer). Pełen gameplay impact
            // (myjnia czyści, turntable obraca, pitlift podnosi) → M-Modernization.
            (TrackBuildSubMode.WashZone,            "MYJ",  "Myjnia",            true),
            (TrackBuildSubMode.Turntable,           "OBR",  "Obrotnica",         true),
            (TrackBuildSubMode.PitLift,             "KAN",  "Kanal / podnosnik", true),
            (TrackBuildSubMode.FuelStation,         "PAL",  "Stacja paliw",      true),  // MM-9 / MM-D14
            (TrackBuildSubMode.WaterService,        "WOD",  "Wodowanie",         true),  // MM-17
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
                DepotUIManager.Instance.OnTrackSubModeChanged += OnSubModeChanged;
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
            bool show = mode == ToolMode.BuildTrack;
            if (panel != null)
                panel.SetActive(show);

            if (show && DepotUIManager.Instance != null)
            {
                DepotUIManager.Instance.CurrentTrackSubMode = TrackBuildSubMode.Track;
                UpdateButtonVisuals();
            }
        }

        private void OnSubModeChanged(TrackBuildSubMode mode)
        {
            UpdateButtonVisuals();
        }

        private void OnButtonClicked(TrackSubButton button)
        {
            if (!button.Unlocked || DepotUIManager.Instance == null)
                return;

            DepotUIManager.Instance.CurrentTrackSubMode = button.Mode;

            // Special case: Schemas — force-show panelu nawet gdy SubMode nie zmienił się
            // (CurrentTrackSubMode setter nie firuje event przy braku zmiany, więc
            // SchemaToolBootstrap.OnTrackSubModeChanged też nie wywołuje Show())
            if (button.Mode == TrackBuildSubMode.Schemas)
            {
                var bootstrap = DepotSystem.Schemas.UI.SchemaToolBootstrap.Instance;
                if (bootstrap == null)
                    bootstrap = DepotServices.Get<DepotSystem.Schemas.UI.SchemaToolBootstrap>();
                if (bootstrap == null)
                {
                    // Auto-spawn (fallback gdy RuntimeInitializeOnLoadMethod nie zadziałało
                    // lub Instance został zniszczony po hot reload)
                    var go = new GameObject("SchemaToolBootstrap (auto-spawn from TrackSubToolbar)");
                    bootstrap = go.AddComponent<DepotSystem.Schemas.UI.SchemaToolBootstrap>();
                }
                bootstrap.ForceShow();
            }
        }

        private void UpdateButtonVisuals()
        {
            TrackBuildSubMode current = DepotUIManager.Instance != null
                ? DepotUIManager.Instance.CurrentTrackSubMode
                : TrackBuildSubMode.Track;

            foreach (TrackSubButton button in buttons)
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
                sectionTitleText.text = "Element torowy";

            if (activeModeText != null)
                activeModeText.text = $"Wybrano: {ModeLabel(current)}";
        }

        private void BuildUI()
        {
            RectTransform root = GetComponent<RectTransform>();
            if (root == null)
                root = gameObject.AddComponent<RectTransform>();

            DepotUIPanelPrimitives.ConfigureSubToolbarRoot(root);
            panel = DepotUIPanelPrimitives.CreateHorizontalPanel(transform, "TrackSubPanel", UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Lg, UITheme.Spacing.Sm, UITheme.Spacing.Sm), UITheme.Spacing.Sm);

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
            layoutElement.preferredWidth = 184f;
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
            sectionTitleText.text = "Element torowy";
            sectionTitleText.color = UITheme.SecondaryText;
            sectionTitleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 12f;

            activeModeText = DepotUIPanelPrimitives.CreateTMP("State", summaryObject.transform, 12f, TextAlignmentOptions.Left, FontStyles.Bold);
            activeModeText.text = "Wybrano: Tor";
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

        private void CreateButton(TrackBuildSubMode mode, string icon, string label, bool unlocked)
        {
            DepotOptionButtonParts parts = DepotUIPanelPrimitives.CreateOptionButton(buttonStrip.transform, $"TrackSub_{mode}", icon, label, 104f, 44f);
            parts.Button.interactable = unlocked;

            var button = new TrackSubButton
            {
                Mode = mode,
                Parts = parts,
                Unlocked = unlocked
            };

            parts.Button.onClick.AddListener(() => OnButtonClicked(button));
            buttons.Add(button);
        }

        private static string ModeLabel(TrackBuildSubMode mode) => mode switch
        {
            TrackBuildSubMode.Track => "Tor",
            TrackBuildSubMode.TurnoutR190 => "Rozjazd R190",
            TrackBuildSubMode.TurnoutR300 => "Rozjazd R300",
            TrackBuildSubMode.DoubleCrossoverR190 => "Krzyzowy",
            TrackBuildSubMode.Schemas => "Schematy glowic",
            TrackBuildSubMode.WashZone => "Myjnia",
            TrackBuildSubMode.Turntable => "Obrotnica",
            TrackBuildSubMode.PitLift => "Kanal / podnosnik",
            TrackBuildSubMode.FuelStation => "Stacja paliw",
            TrackBuildSubMode.WaterService => "Wodowanie",
            _ => "Tor"
        };
    }
}
