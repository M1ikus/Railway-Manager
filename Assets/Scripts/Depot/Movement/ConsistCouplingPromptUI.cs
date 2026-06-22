using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace DepotSystem
{
    /// <summary>
    /// TD-032: popup-na-styku. Gdy skład dojedzie DO STYKU za innym (event
    /// <see cref="DepotMovementSimulator.OnConsistArrivedAtContact"/>), pyta gracza
    /// „Połączyć X z Y?". Screen-space overlay śledzący punkt styku przez WorldToScreenPoint.
    /// Tak → re-walidacja stationary → <see cref="DepotMovementSimulator.CoupleConsists"/>.
    /// Nie / ESC / RMB → schowaj. Ruch (inny manewr) na którymś składzie → auto-anuluj.
    /// </summary>
    public class ConsistCouplingPromptUI : MonoBehaviour
    {
        public static ConsistCouplingPromptUI Instance { get; private set; }

        static readonly Color PanelBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.97f);
        static readonly Color WarnBg = UITheme.WithAlpha(UITheme.Warning, 0.18f);

        const float W = 320f;
        const float Pad = 12f;
        const float TitleH = 44f;
        const float WarnH = 30f;
        const float BtnH = 34f;
        const float OffsetYpx = 56f;   // panel nad punktem styku

        Camera _camera;
        int _moverId = -1;
        int _blockerId = -1;
        Vector3 _contactWorldPos;
        bool _active;

        GameObject _panel;
        RectTransform _panelRt;
        TextMeshProUGUI _titleText;
        GameObject _warnCard;
        RectTransform _buttonsRow;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
            HidePrompt();
            DepotMovementSimulator.OnConsistArrivedAtContact += OnArrivedAtContact;
        }

        void OnDestroy()
        {
            DepotMovementSimulator.OnConsistArrivedAtContact -= OnArrivedAtContact;
            if (Instance == this) Instance = null;
        }

        // ── Event: dojazd do styku ──────────────────────────────────

        void OnArrivedAtContact(int moverId, int blockerId, Vector3 contactWorldPos)
        {
            var sim = DepotMovementSimulator.Instance;
            if (sim == null) return;

            // Re-walidacja: oba istnieją (visual) i stoją (brak taska).
            if (!sim.HasConsistVisual(moverId) || !sim.HasConsistVisual(blockerId)) return;
            if (sim.HasTaskForConsist(moverId) || sim.HasTaskForConsist(blockerId)) return;

            _moverId = moverId;
            _blockerId = blockerId;
            _contactWorldPos = contactWorldPos;
            _active = true;

            _titleText.text = string.Format(
                LocalizationService.Get("popup_couple.title_format"),
                sim.GetConsistDisplayName(moverId),
                sim.GetConsistDisplayName(blockerId));

            // Warn obiegu (hook null poza Timetable → false → karta ukryta).
            bool inCirc =
                DepotMovementSimulator.IsConsistInActiveCirculation(sim.GetConsistVehicleIds(moverId)) ||
                DepotMovementSimulator.IsConsistInActiveCirculation(sim.GetConsistVehicleIds(blockerId));
            _warnCard.SetActive(inCirc);

            // Layout zależny od obecności warna.
            float yToButtons = Pad + TitleH + (inCirc ? (6f + WarnH) : 0f) + 8f;
            _buttonsRow.anchoredPosition = new Vector2(Pad, -yToButtons);
            _panelRt.sizeDelta = new Vector2(W, yToButtons + BtnH + Pad);

            _panel.SetActive(true);
            UpdateFollow();
        }

        // ── Input: anuluj ────────────────────────────────────────────

        void Update()
        {
            if (!_active) return;
            bool esc = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            bool rmb = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
            if (esc || rmb)
            {
                if (esc) PauseMenuUI.LastEscConsumedFrame = Time.frameCount;
                HidePrompt();
            }
        }

        void LateUpdate()
        {
            if (!_active) return;
            var sim = DepotMovementSimulator.Instance;
            // Inwalidacja: składy zniknęły lub ruszyły (inny manewr) → anuluj prompt.
            if (sim == null
                || !sim.HasConsistVisual(_moverId) || !sim.HasConsistVisual(_blockerId)
                || sim.HasTaskForConsist(_moverId) || sim.HasTaskForConsist(_blockerId))
            {
                HidePrompt();
                return;
            }
            UpdateFollow();
        }

        void UpdateFollow()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            var sp = _camera.WorldToScreenPoint(_contactWorldPos);
            if (sp.z <= 0f)
            {
                // Punkt za kamerą → schowaj panel wizualnie (prompt zostaje aktywny).
                if (_panel.activeSelf) _panel.SetActive(false);
                return;
            }
            if (!_panel.activeSelf) _panel.SetActive(true);
            _panelRt.position = new Vector3(sp.x, sp.y + OffsetYpx, 0f);
        }

        // ── Akcje ────────────────────────────────────────────────────

        void Confirm()
        {
            var sim = DepotMovementSimulator.Instance;
            if (sim != null
                && sim.HasConsistVisual(_moverId) && sim.HasConsistVisual(_blockerId)
                && !sim.HasTaskForConsist(_moverId) && !sim.HasTaskForConsist(_blockerId))
            {
                sim.CoupleConsists(_moverId, _blockerId);
            }
            HidePrompt();
        }

        void HidePrompt()
        {
            _active = false;
            _moverId = -1;
            _blockerId = -1;
            if (_panel != null) _panel.SetActive(false);
        }

        // ── Build ────────────────────────────────────────────────────

        void BuildUI()
        {
            var canvasGo = new GameObject("ConsistCouplePromptCanvas");
            canvasGo.transform.SetParent(transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 230;   // nad ConsistPopupUI (220)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            _panel = new GameObject("Panel");
            _panel.transform.SetParent(canvas.transform, false);
            _panelRt = _panel.AddComponent<RectTransform>();
            _panelRt.anchorMin = new Vector2(0.5f, 0f);
            _panelRt.anchorMax = new Vector2(0.5f, 0f);
            _panelRt.pivot = new Vector2(0.5f, 0f);   // pozycja = dolny-środek nad stykiem
            _panelRt.sizeDelta = new Vector2(W, Pad + TitleH + BtnH + Pad);
            UITheme.ApplySurface(_panel.AddComponent<Image>(), PanelBg, UIShapePreset.PanelLarge);

            _titleText = CreateText(_panel.transform, "Title", 15,
                TextAlignmentOptions.Top, new Vector2(Pad, -Pad), new Vector2(W - 2 * Pad, TitleH),
                UIThemeTextRole.Accent);
            _titleText.richText = true;

            _warnCard = new GameObject("WarnCard", typeof(RectTransform));
            _warnCard.transform.SetParent(_panel.transform, false);
            var warnRt = _warnCard.GetComponent<RectTransform>();
            warnRt.anchorMin = new Vector2(0f, 1f);
            warnRt.anchorMax = new Vector2(0f, 1f);
            warnRt.pivot = new Vector2(0f, 1f);
            warnRt.anchoredPosition = new Vector2(Pad, -(Pad + TitleH + 6f));
            warnRt.sizeDelta = new Vector2(W - 2 * Pad, WarnH);
            UITheme.ApplySurface(_warnCard.AddComponent<Image>(), WarnBg, UIShapePreset.Panel);
            var warnText = CreateText(_warnCard.transform, "WarnText", 11,
                TextAlignmentOptions.Left, new Vector2(8f, -6f), new Vector2(W - 2 * Pad - 16f, WarnH - 8f),
                UIThemeTextRole.Secondary);
            warnText.richText = true;
            warnText.text = LocalizationService.Get("popup_couple.warn_circulation");

            _buttonsRow = new GameObject("ButtonsRow", typeof(RectTransform)).GetComponent<RectTransform>();
            _buttonsRow.SetParent(_panel.transform, false);
            _buttonsRow.anchorMin = new Vector2(0f, 1f);
            _buttonsRow.anchorMax = new Vector2(0f, 1f);
            _buttonsRow.pivot = new Vector2(0f, 1f);
            _buttonsRow.anchoredPosition = new Vector2(Pad, -(Pad + TitleH + 8f));
            _buttonsRow.sizeDelta = new Vector2(W - 2 * Pad, BtnH);

            float halfW = (W - 2 * Pad - 8f) / 2f;
            var noBtn = CreateButton(_buttonsRow, LocalizationService.Get("popup_couple.btn_no"),
                new Vector2(0f, 0f), new Vector2(halfW, BtnH), false);
            noBtn.onClick.AddListener(HidePrompt);
            var yesBtn = CreateButton(_buttonsRow, LocalizationService.Get("popup_couple.btn_yes"),
                new Vector2(halfW + 8f, 0f), new Vector2(halfW, BtnH), true);
            yesBtn.onClick.AddListener(Confirm);
        }

        static TextMeshProUGUI CreateText(Transform parent, string name, int fontSize,
            TextAlignmentOptions alignment, Vector2 anchoredPos, Vector2 size, UIThemeTextRole role)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var txt = go.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(txt, role);
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.textWrappingMode = TextWrappingModes.Normal;
            txt.overflowMode = TextOverflowModes.Overflow;
            txt.raycastTarget = false;
            return txt;
        }

        static Button CreateButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size, bool primary)
        {
            var go = new GameObject($"Btn_{label}");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            UITheme.ApplyButtonStyle(btn, img, primary ? UIButtonTone.Primary : UIButtonTone.Secondary, UIShapePreset.Button);

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            var txt = textGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(txt, primary ? UIThemeTextRole.Inverse : UIThemeTextRole.Primary);
            txt.fontSize = 13;
            txt.alignment = TextAlignmentOptions.Center;
            txt.raycastTarget = false;
            txt.text = label;
            return btn;
        }
    }
}
