using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;
using RailwayManager.Core;
using RailwayManager.Core.Settings;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace MainMenu
{
    /// <summary>
    /// Pełnoekranowy modal "Naciśnij klawisz" — używany przez Settings UI
    /// w zakładce Sterowanie do interaktywnego rebindowania klawiszy (M13-2b).
    ///
    /// Cykl życia:
    /// - <see cref="Build"/> — raz w Awake parent UI (np. SettingsScreenUI.Build)
    /// - <see cref="Show"/> — wywołane z UI rowa "Zmień..."; rejestruje
    ///   <see cref="InputActionRebindingExtensions.PerformInteractiveRebinding"/>
    ///   na wybranym binding'u
    /// - Auto-hide po success / cancel / timeout (5s default)
    /// - <c>onComplete(bool success)</c> wywołane po zakończeniu — caller refresh'uje UI
    /// </summary>
    public class RebindModalUI : MonoBehaviour
    {
        // ── colours (zbliżone do Settings UI) ───────────
        private static readonly Color Backdrop    = UITheme.WithAlpha(UITheme.PrimaryText, 0.72f);
        private static readonly Color CardBg      = UITheme.OverlayPanelStrong;
        private static readonly Color BtnBg       = UITheme.SecondarySurface;
        private static readonly Color TextPrimary = UITheme.PrimaryText;
        private static readonly Color TextMuted   = UITheme.SecondaryText;
        private static readonly Color Accent      = UITheme.PrimaryAccent;

        // ── refs ────────────────────────────────────────
        private GameObject       _root;
        private TextMeshProUGUI  _titleLbl;
        private TextMeshProUGUI  _hintLbl;
        private TextMeshProUGUI  _countdownLbl;
        private TextMeshProUGUI  _cancelLbl;
        private string           _currentActionDisplayName;

        // ── state ───────────────────────────────────────
        private Action<bool> _onComplete;
        private InputActionRebindingExtensions.RebindingOperation _op;
        private float _countdownEnd;
        private float _timeoutSeconds;

        // ═══════════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Buduje GO modal'a jako dziecko canvasa. Wywołać raz w setup parent UI.</summary>
        public void Build(Transform canvasTransform)
        {
            // Backdrop (fullscreen, blokuje raycast)
            _root = MenuScreenPrimitives.CreateFullscreenRoot("RebindModal", canvasTransform);
            _root.GetComponent<Image>().color = Backdrop;

            // Card (centered, 480×260)
            var card = NewGO("Card", _root.transform);
            card.AddComponent<Image>().color = CardBg;
            var cardRT = card.GetComponent<RectTransform>();
            cardRT.anchorMin = new Vector2(0.5f, 0.5f);
            cardRT.anchorMax = new Vector2(0.5f, 0.5f);
            cardRT.pivot     = new Vector2(0.5f, 0.5f);
            cardRT.sizeDelta = new Vector2(480f, 260f);

            var vl = card.AddComponent<VerticalLayoutGroup>();
            vl.padding = UITheme.Padding(UITheme.Spacing.Xxl, UITheme.Spacing.Xl);
            vl.spacing = UITheme.Spacing.Md;
            vl.childAlignment      = TextAnchor.MiddleCenter;
            vl.childControlWidth   = true;
            vl.childControlHeight  = false;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            // Title (action name)
            _titleLbl = MakeTMP("Title", card.transform);
            _titleLbl.fontSize  = 22;
            _titleLbl.fontStyle = FontStyles.Bold;
            _titleLbl.color     = Accent;
            _titleLbl.alignment = TextAlignmentOptions.Center;
            _titleLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;

            // Hint
            _hintLbl = MakeTMP("Hint", card.transform);
            _hintLbl.fontSize  = 18;
            _hintLbl.color     = TextPrimary;
            _hintLbl.alignment = TextAlignmentOptions.Center;
            _hintLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            // Countdown
            _countdownLbl = MakeTMP("Countdown", card.transform);
            _countdownLbl.fontSize  = 14;
            _countdownLbl.color     = TextMuted;
            _countdownLbl.alignment = TextAlignmentOptions.Center;
            _countdownLbl.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            // Cancel button
            var cancelGO = NewGO("Cancel", card.transform);
            var cancelImg = cancelGO.AddComponent<Image>();
            cancelImg.color = BtnBg;
            var cancelBtn = cancelGO.AddComponent<Button>();
            cancelBtn.targetGraphic = cancelImg;
            cancelBtn.transition = Selectable.Transition.ColorTint;
            cancelBtn.colors = UITheme.CreateColorBlock(
                BtnBg,
                UITheme.RaisedSurface,
                UITheme.Border,
                BtnBg,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            cancelBtn.onClick.AddListener(OnCancelClicked);
            var cancelLE = cancelGO.AddComponent<LayoutElement>();
            cancelLE.preferredWidth  = 180f;
            cancelLE.preferredHeight = 42f;

            _cancelLbl = MakeTMP("Lbl", cancelGO.transform);
            _cancelLbl.text      = LocalizationService.Get("rebind.modal.cancel");
            _cancelLbl.fontSize  = 16;
            _cancelLbl.color     = TextPrimary;
            _cancelLbl.alignment = TextAlignmentOptions.Center;
            _cancelLbl.raycastTarget = false;
            FillRT(_cancelLbl.gameObject);

            // i18n: re-applikujemy labele przy zmianie języka
            LocalizationService.OnLanguageChanged += RefreshLanguage;

            _root.SetActive(false);
        }

        private void RefreshLanguage()
        {
            if (_cancelLbl != null)
                _cancelLbl.text = LocalizationService.Get("rebind.modal.cancel");
            if (IsVisible)
            {
                if (_titleLbl != null && !string.IsNullOrEmpty(_currentActionDisplayName))
                    _titleLbl.text = LocalizationService.Get("rebind.modal.title_format", _currentActionDisplayName);
                if (_hintLbl != null)
                    _hintLbl.text = LocalizationService.Get("rebind.modal.prompt");
                // _countdownLbl tick'uje co frame w Update — RefreshLanguage zostawia jego format
            }
        }

        /// <summary>
        /// Pokazuje modal i rozpoczyna interaktywny rebind dla wskazanego binding'u.
        /// Wywołuje <paramref name="onComplete"/> po success / cancel / timeout.
        /// </summary>
        public void Show(string actionDisplayName, InputAction action, int bindingIndex,
            Action<bool> onComplete, float timeoutSec = 5f)
        {
            if (action == null)
            {
                Log.Warn("[RebindModalUI] Show called with null action");
                onComplete?.Invoke(false);
                return;
            }

            _onComplete = onComplete;
            _timeoutSeconds = timeoutSec;
            _countdownEnd = Time.unscaledTime + timeoutSec;
            _currentActionDisplayName = actionDisplayName;

            _titleLbl.text = LocalizationService.Get("rebind.modal.title_format", actionDisplayName);
            _hintLbl.text  = LocalizationService.Get("rebind.modal.prompt");
            _countdownLbl.text = LocalizationService.Get("rebind.modal.timeout_format", timeoutSec.ToString("F1"));
            _root.SetActive(true);

            // Rebind requires action to be disabled
            bool wasEnabled = action.enabled;
            if (wasEnabled) action.Disable();

            _op = RebindingService.BeginRebind(
                action, bindingIndex,
                onComplete: success =>
                {
                    if (wasEnabled) action.Enable();
                    Hide();
                    _onComplete?.Invoke(success);
                    _onComplete = null;
                    _op = null;
                },
                timeoutSeconds: timeoutSec);
        }

        public void Hide() => _root?.SetActive(false);

        public void CancelActiveOperation()
        {
            if (_op != null)
                _op.Cancel();
            else
                Hide();
        }

        public bool IsVisible => _root != null && _root.activeSelf;

        // ═══════════════════════════════════════════════════════════════════
        //  UPDATE — countdown tick
        // ═══════════════════════════════════════════════════════════════════

        void Update()
        {
            if (!IsVisible) return;
            float remaining = Mathf.Max(0f, _countdownEnd - Time.unscaledTime);
            _countdownLbl.text = LocalizationService.Get("rebind.modal.timeout_format", remaining.ToString("F1"));
        }

        // ═══════════════════════════════════════════════════════════════════
        //  HANDLERS
        // ═══════════════════════════════════════════════════════════════════

        void OnCancelClicked()
        {
            // Cancel rebind operation — onCancel callback w BeginRebind wywoła _onComplete(false) + Hide()
            _op?.Cancel();
        }

        void OnDestroy()
        {
            LocalizationService.OnLanguageChanged -= RefreshLanguage;
            _op?.Dispose();
            _op = null;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  PRIMITIVES — aliasy do MenuScreenPrimitives
        // ═══════════════════════════════════════════════════════════════════

        private static GameObject NewGO(string name, Transform parent) => MenuScreenPrimitives.NewGO(name, parent);
        private static TextMeshProUGUI MakeTMP(string name, Transform parent) => MenuScreenPrimitives.MakeTMP(name, parent);
        private static void FillRT(GameObject go) => MenuScreenPrimitives.Fill(go);
    }
}
