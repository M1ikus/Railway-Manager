using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.Timetable.Workflows;

namespace RailwayManager.Timetable.UI
{
    /// <summary>
    /// M-TimetableUX F1.15 polish: visual step indicator widget dla pipeline workflow.
    /// Subscribuje <see cref="TimetableWorkflowOrchestrator.OnStepChanged"/> + rerenders
    /// 4-step progress bar (Tworzenie / Sugestia / Pojazd / Drużyna).
    ///
    /// **Visibility:** active tylko gdy WorkflowStep != Idle. Auto-hide w Idle state.
    /// **Position:** top-center floating panel, fixed coord (TBD post-EA: configurable).
    /// </summary>
    public class WorkflowStepIndicatorWidget : MonoBehaviour
    {
        public static WorkflowStepIndicatorWidget Instance { get; private set; }

        private GameObject _panel;
        private readonly List<TextMeshProUGUI> _stepLabels = new();
        private Button _cancelButton;
        private Button _backButton;
        private Button _nextButton;
        private Image _backImage;
        private Image _nextImage;

        // Visual constants
        private static readonly Color ColorPending = new Color(0.4f, 0.4f, 0.4f);
        private static readonly Color ColorActive = new Color(1f, 0.85f, 0.2f);
        private static readonly Color ColorCompleted = new Color(0.5f, 0.85f, 0.5f);

        private static readonly WorkflowStep[] DisplayedSteps =
        {
            WorkflowStep.CreatingTimetable,
            WorkflowStep.ReviewingSuggestions,
            WorkflowStep.AssigningVehicle,
            WorkflowStep.AssigningCrew
        };

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
        }

        void OnEnable()
        {
            TimetableWorkflowOrchestrator.OnStepChanged += HandleStepChanged;
            HandleStepChanged(TimetableWorkflowOrchestrator.CurrentStep);
        }

        void OnDisable()
        {
            TimetableWorkflowOrchestrator.OnStepChanged -= HandleStepChanged;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static WorkflowStepIndicatorWidget EnsureExists(Transform canvasParent)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("WorkflowStepIndicator");
            if (canvasParent != null) go.transform.SetParent(canvasParent, false);
            return go.AddComponent<WorkflowStepIndicatorWidget>();
        }

        private void BuildUI()
        {
            _panel = new GameObject("Panel");
            _panel.transform.SetParent(transform, false);
            var rt = _panel.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0, -10);
            rt.sizeDelta = new Vector2(640, 44);

            var bg = _panel.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.PrimarySurface, 0.92f), UIShapePreset.Panel);

            var hh = _panel.AddComponent<HorizontalLayoutGroup>();
            hh.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            hh.spacing = UITheme.Spacing.Sm;
            hh.childAlignment = TextAnchor.MiddleCenter;
            hh.childForceExpandWidth = false;
            hh.childForceExpandHeight = false;

            // 4 step labels
            for (int i = 0; i < DisplayedSteps.Length; i++)
            {
                var stepLabel = MakeStepLabel(_panel.transform, TimetableWorkflowOrchestrator.StepLabel(DisplayedSteps[i]));
                _stepLabels.Add(stepLabel);

                if (i < DisplayedSteps.Length - 1)
                {
                    var sep = new GameObject("Sep");
                    sep.transform.SetParent(_panel.transform, false);
                    sep.AddComponent<LayoutElement>().preferredWidth = 8;
                    var sepText = sep.AddComponent<TextMeshProUGUI>();
                    sepText.text = "→";
                    sepText.fontSize = 14;
                    UITheme.ApplyTmpText(sepText, UIThemeTextRole.Secondary);
                    sepText.alignment = TextAlignmentOptions.Center;
                }
            }

            // Back / Next / Cancel buttons (F1.15 polish back/forward navigation)
            (_backButton, _backImage) = MakeNavButton(_panel.transform, "← Wstecz", UITheme.WithAlpha(UITheme.SecondarySurface, 0.85f),
                () => TimetableWorkflowOrchestrator.GoBackToPreviousStep());
            (_nextButton, _nextImage) = MakeNavButton(_panel.transform, "Dalej →", UITheme.WithAlpha(UITheme.PrimaryAccent, 0.85f),
                () => TimetableWorkflowOrchestrator.AdvanceToNextStep());
            (_cancelButton, _) = MakeNavButton(_panel.transform, "Anuluj", UITheme.WithAlpha(UITheme.Danger, 0.7f),
                TimetableWorkflowOrchestrator.CancelWorkflow);

            _panel.SetActive(false);
        }

        private (Button btn, Image img) MakeNavButton(Transform parent, string label, Color bg, System.Action onClick)
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 70; le.preferredHeight = 26;
            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, bg, UIShapePreset.Inset);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => onClick?.Invoke());

            var lblGo = new GameObject("Lbl");
            lblGo.transform.SetParent(go.transform, false);
            var lrt = lblGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            var lt = lblGo.AddComponent<TextMeshProUGUI>();
            lt.text = label; lt.fontSize = 11; lt.alignment = TextAlignmentOptions.Center;
            UITheme.ApplyTmpText(lt, UIThemeTextRole.Primary);
            return (btn, img);
        }

        private TextMeshProUGUI MakeStepLabel(Transform parent, string text)
        {
            var go = new GameObject("Step");
            go.transform.SetParent(parent, false);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 120;
            le.preferredHeight = 26;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = 11;
            t.alignment = TextAlignmentOptions.Center;
            UITheme.ApplyTmpText(t, UIThemeTextRole.Primary);
            t.color = ColorPending;
            return t;
        }

        private void HandleStepChanged(WorkflowStep newStep)
        {
            if (_panel == null) return;

            if (newStep == WorkflowStep.Idle)
            {
                _panel.SetActive(false);
                return;
            }
            _panel.SetActive(true);

            int activeIdx = -1;
            for (int i = 0; i < DisplayedSteps.Length; i++)
            {
                if (DisplayedSteps[i] == newStep) { activeIdx = i; break; }
            }

            // Color: pending (gray) / active (yellow) / completed (green)
            for (int i = 0; i < _stepLabels.Count; i++)
            {
                if (i < activeIdx) _stepLabels[i].color = ColorCompleted;
                else if (i == activeIdx) _stepLabels[i].color = ColorActive;
                else _stepLabels[i].color = ColorPending;
            }

            // Done state: all completed
            if (newStep == WorkflowStep.Done)
            {
                for (int i = 0; i < _stepLabels.Count; i++)
                    _stepLabels[i].color = ColorCompleted;
            }

            // F1.15 polish: enabled state Back/Next buttons per step
            UpdateNavButtonStates();
        }

        private void UpdateNavButtonStates()
        {
            if (_backButton != null)
            {
                bool canBack = TimetableWorkflowOrchestrator.CanGoBackToPreviousStep();
                _backButton.interactable = canBack;
                if (_backImage != null)
                    _backImage.color = canBack ? UITheme.WithAlpha(UITheme.SecondarySurface, 0.85f)
                                                : UITheme.WithAlpha(UITheme.SecondarySurface, 0.35f);
            }
            if (_nextButton != null)
            {
                bool canNext = TimetableWorkflowOrchestrator.CanAdvanceToNextStep();
                _nextButton.interactable = canNext;
                if (_nextImage != null)
                    _nextImage.color = canNext ? UITheme.WithAlpha(UITheme.PrimaryAccent, 0.85f)
                                                : UITheme.WithAlpha(UITheme.PrimaryAccent, 0.35f);
            }
        }
    }
}
