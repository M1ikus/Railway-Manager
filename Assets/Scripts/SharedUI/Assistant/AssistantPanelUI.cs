using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core.Assistant;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.SharedUI.Assistant
{
    /// <summary>
    /// M11 AS-1c: panel „co mogę zrobić" (warstwa [0] UX — PULL, gracz świadomie otwiera
    /// klikiem w awatar). Sekcje: aktualna sugestia (z monitora) + lista capability
    /// (gating CanExecute) + historia działań. Toggle auto-mode per capability dojdzie w AS-6.
    /// Content przebudowywany przy każdym otwarciu (panel otwierany rzadko — prostota nad cache).
    /// Bez PauseStack (asystent niczego nie blokuje).
    /// </summary>
    public class AssistantPanelUI : MonoBehaviour
    {
        public static AssistantPanelUI Instance { get; private set; }

        const float PanelWidth = 400f;
        const float PanelHeight = 480f;
        const float RowHeight = 26f;
        const float SmallButtonWidth = 116f;

        GameObject _root;
        RectTransform _content;
        readonly List<IAssistantCapability> _capBuffer = new List<IAssistantCapability>();

        public bool IsOpen => _root != null && _root.activeSelf;

        public static AssistantPanelUI EnsureExists(AssistantAvatarUI host)
        {
            if (Instance != null) return Instance;
            if (host == null || host.Canvas == null)
            {
                Core.Log.Warn("[AssistantPanelUI] EnsureExists bez hosta — pomijam");
                return null;
            }
            var go = new GameObject("AssistantPanelUI");
            go.transform.SetParent(host.transform);
            Instance = go.AddComponent<AssistantPanelUI>();
            Instance.BuildUI(host.Canvas);
            return Instance;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Toggle()
        {
            if (IsOpen) Hide();
            else Show();
        }

        public void Show()
        {
            if (_root == null) return;
            AssistantWhisperUI.Instance?.HideSilent(); // panel zastępuje szept (wzajemne wykluczenie)
            RebuildContent();
            _root.SetActive(true);
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        // ────────────────────────── Build ──────────────────────────

        void BuildUI(Canvas canvas)
        {
            _root = new GameObject("PanelRoot", typeof(RectTransform));
            _root.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            rt.anchoredPosition = new Vector2(12f, 76f); // nad awatarem, w miejscu szeptu

            var img = _root.AddComponent<Image>();
            UITheme.ApplySurface(img, UITheme.PrimarySurface, UIShapePreset.Panel);

            _content = UIBuilders.MakeContainer(_root.transform, UIBuilders.ContainerLayout.Vertical,
                padding: UITheme.Spacing.Md, spacing: UITheme.Spacing.Sm);
            var crt = _content;
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            _root.SetActive(false);
        }

        void RebuildContent()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                Destroy(_content.GetChild(i).gameObject);
            }

            // ── Nagłówek + zamknięcie ──
            var header = UIBuilders.MakeContainer(_content, UIBuilders.ContainerLayout.Horizontal,
                padding: 0f, spacing: UITheme.Spacing.Sm);
            SetRowHeight(header.gameObject, 30f);
            var title = UIBuilders.MakeLabel(header,
                string.Format(LocalizationService.Get("assistant.panel.title_format"), AssistantState.DisplayName),
                UIBuilders.TypographyRole.H3);
            SetPreferred(title.gameObject, PanelWidth - 90f, 30f);
            var closeBtn = UIBuilders.MakeButton(header, "✕", UIButtonTone.Secondary);
            SetPreferred(closeBtn.gameObject, 30f, 26f);
            closeBtn.onClick.AddListener(Hide);

            UIBuilders.MakeSeparator(_content);

            // ── Sugestia (aktualny kandydat monitora) ──
            AddSectionHeader(LocalizationService.Get("assistant.panel.suggestion_header"));
            var candidate = NextStepMonitor.CurrentCandidate;
            if (candidate == null)
            {
                AddBodyLabel(LocalizationService.Get("assistant.panel.no_suggestion"));
            }
            else
            {
                AddBodyLabel(LocalizationService.Get(candidate.messageKey));
                var cap = AssistantCapabilityRegistry.Get(candidate.capabilityId);
                if (cap != null) AddCapabilityButtonsRow(cap);
            }

            UIBuilders.MakeSeparator(_content);

            // ── Capability (gating CanExecute) ──
            AddSectionHeader(LocalizationService.Get("assistant.panel.capabilities_header"));
            AssistantCapabilityRegistry.GetAll(_capBuffer);
            int shown = 0;
            foreach (var cap in _capBuffer)
            {
                if (!cap.CanExecute()) continue;
                shown++;
                AddBodyLabel(LocalizationService.Get("assistant.capability." + cap.Id));
                AddCapabilityButtonsRow(cap);
            }
            if (shown == 0)
            {
                AddBodyLabel(LocalizationService.Get("assistant.panel.no_suggestion"));
            }

            UIBuilders.MakeSeparator(_content);

            // ── Historia działań (AS-D6 log „co zrobiłem") ──
            AddSectionHeader(LocalizationService.Get("assistant.panel.history_header"));
            var history = AssistantState.History;
            if (history.Count == 0)
            {
                AddBodyLabel(LocalizationService.Get("assistant.panel.history_empty"));
            }
            else
            {
                int max = Mathf.Min(history.Count, 5);
                for (int i = 0; i < max; i++)
                {
                    AddSmallLabel("• " + history[i].text);
                }
            }
        }

        void AddCapabilityButtonsRow(IAssistantCapability cap)
        {
            var row = UIBuilders.MakeContainer(_content, UIBuilders.ContainerLayout.Horizontal,
                padding: 0f, spacing: UITheme.Spacing.Sm);
            SetRowHeight(row.gameObject, RowHeight);

            var showHow = UIBuilders.MakeButton(row,
                LocalizationService.Get("assistant.panel.show_how"), UIButtonTone.Secondary);
            SetPreferred(showHow.gameObject, SmallButtonWidth, RowHeight - 2f);
            string capId = cap.Id;
            showHow.onClick.AddListener(() =>
            {
                Hide();
                AssistantOrchestrator.Instance?.ShowGuidanceFor(capId);
            });

            if (cap.CanAutoExecute)
            {
                var propose = UIBuilders.MakeButton(row,
                    LocalizationService.Get("assistant.panel.propose"), UIButtonTone.Primary);
                SetPreferred(propose.gameObject, SmallButtonWidth + 20f, RowHeight - 2f);
                propose.onClick.AddListener(() =>
                {
                    Hide();
                    AssistantOrchestrator.Instance?.ShowPlanFor(capId);
                });
            }

            // AS-6 / AS-D6: opt-in auto-mode — tylko capability addytywne (AutoModeAllowed).
            if (cap.CanAutoExecute && cap.AutoModeAllowed)
            {
                bool on = AssistantState.IsAutoModeEnabled(cap.Id);
                var autoBtn = UIBuilders.MakeButton(row,
                    string.Format(LocalizationService.Get("assistant.panel.auto_format"),
                        LocalizationService.Get(on ? "common.on" : "common.off")),
                    on ? UIButtonTone.Primary : UIButtonTone.Secondary);
                SetPreferred(autoBtn.gameObject, 90f, RowHeight - 2f);
                autoBtn.onClick.AddListener(() =>
                {
                    AssistantState.SetAutoMode(capId, !AssistantState.IsAutoModeEnabled(capId));
                    RebuildContent(); // odśwież label/tone toggla
                });
            }
        }

        // ── Layout helpers ──

        void AddSectionHeader(string text)
        {
            var tmp = UIBuilders.MakeLabel(_content, text, UIBuilders.TypographyRole.Small,
                UITheme.WithAlpha(UITheme.GetTextColor(UIThemeTextRole.Primary), 0.7f));
            SetRowHeight(tmp.gameObject, 18f);
        }

        void AddBodyLabel(string text)
        {
            var tmp = UIBuilders.MakeLabel(_content, text, UIBuilders.TypographyRole.Body);
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            SetRowHeight(tmp.gameObject, 38f);
        }

        void AddSmallLabel(string text)
        {
            var tmp = UIBuilders.MakeLabel(_content, text, UIBuilders.TypographyRole.Small);
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            SetRowHeight(tmp.gameObject, 18f);
        }

        static void SetRowHeight(GameObject go, float height)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;
        }

        static void SetPreferred(GameObject go, float width, float height)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
        }
    }
}
