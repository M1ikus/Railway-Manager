using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.Core.Assistant;

namespace RailwayManager.SharedUI.Assistant
{
    /// <summary>
    /// M11 AS-1c: szept asystenta — jednolinijkowy dymek nad awatarem (warstwa [1] UX:
    /// ambient PUSH, NIGDY modal). Auto-expire hybrydowy jak PersonnelNotificationToastUI
    /// (BUG-041: real-time LUB game-time — cokolwiek minie pierwsze; odporny na pauzę i x500).
    /// Expire = ciche zniknięcie (sugestia ZOSTAJE w panelu — nie ginie). X = snooze
    /// (orchestrator zapisuje w SuggestionMemoryService). Klik w treść = akcja (otwórz panel/preview).
    /// </summary>
    public class AssistantWhisperUI : MonoBehaviour
    {
        public static AssistantWhisperUI Instance { get; private set; }

        const float BubbleWidth = 380f;
        const float BubbleHeight = 64f;

        GameObject _root;
        TextMeshProUGUI _text;
        System.Action _onClick;
        System.Action _onDismissedByX;

        float _realRemaining;
        float _gameRemaining;

        public bool IsShowing => _root != null && _root.activeSelf;

        public static AssistantWhisperUI EnsureExists(AssistantAvatarUI host)
        {
            if (Instance != null) return Instance;
            if (host == null || host.Canvas == null)
            {
                Log.Warn("[AssistantWhisperUI] EnsureExists bez hosta — pomijam");
                return null;
            }
            var go = new GameObject("AssistantWhisperUI");
            go.transform.SetParent(host.transform);
            Instance = go.AddComponent<AssistantWhisperUI>();
            Instance.BuildUI(host.Canvas);
            return Instance;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Pokazuje szept (zastępuje poprzedni — max 1 naraz, zasada UX).
        /// <paramref name="onClick"/> = akcja kliknięcia w treść; <paramref name="onDismissedByX"/> = snooze.
        /// </summary>
        public void Show(string text, System.Action onClick, System.Action onDismissedByX)
        {
            if (_root == null) return;
            _text.text = text;
            _onClick = onClick;
            _onDismissedByX = onDismissedByX;
            _realRemaining = AssistantConstants.WhisperAutoExpireRealSec;
            _gameRemaining = AssistantConstants.WhisperAutoExpireGameSec;
            _root.SetActive(true);
        }

        /// <summary>Ciche schowanie (expire / otwarcie panelu) — bez snooze.</summary>
        public void HideSilent()
        {
            if (_root != null) _root.SetActive(false);
        }

        void Update()
        {
            if (!IsShowing) return;

            // Hybryda expire (wzorzec BUG-041): real-time działa na pauzie, game-time przy x500.
            float deltaReal = Time.unscaledDeltaTime;
            float deltaGame = GameState.IsPaused ? 0f : deltaReal * GameState.TimeScale;
            _realRemaining -= deltaReal;
            _gameRemaining -= deltaGame;
            if (_realRemaining <= 0f || _gameRemaining <= 0f)
            {
                HideSilent(); // expire = cisza; sugestia zostaje w panelu
            }
        }

        void BuildUI(Canvas canvas)
        {
            _root = new GameObject("WhisperBubble", typeof(RectTransform));
            _root.transform.SetParent(canvas.transform, false);
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(BubbleWidth, BubbleHeight);
            rt.anchoredPosition = new Vector2(12f, 76f); // nad awatarem (12 + 56 + 8)

            var img = _root.AddComponent<Image>();
            UITheme.ApplySurface(img, UITheme.SecondarySurface, UIShapePreset.Panel);
            var btn = _root.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                HideSilent();
                _onClick?.Invoke();
            });

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(_root.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(UITheme.Spacing.Md, UITheme.Spacing.Xs);
            trt.offsetMax = new Vector2(-30f, -UITheme.Spacing.Xs); // miejsce na X
            _text = textGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(_text, UIThemeTextRole.Primary);
            _text.fontSize = UITheme.Typography.Small;
            _text.alignment = TextAlignmentOptions.MidlineLeft;
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.overflowMode = TextOverflowModes.Ellipsis;
            _text.raycastTarget = false;

            // X — snooze (mały, prawy-górny róg dymka).
            var xGo = new GameObject("Dismiss", typeof(RectTransform), typeof(Image), typeof(Button));
            xGo.transform.SetParent(_root.transform, false);
            var xrt = (RectTransform)xGo.transform;
            xrt.anchorMin = Vector2.one;
            xrt.anchorMax = Vector2.one;
            xrt.pivot = Vector2.one;
            xrt.sizeDelta = new Vector2(22f, 22f);
            xrt.anchoredPosition = new Vector2(-4f, -4f);
            var ximg = xGo.GetComponent<Image>();
            UITheme.ApplySurface(ximg, UITheme.WithAlpha(UITheme.Border, 0.35f), UIShapePreset.Pill);
            var xbtn = xGo.GetComponent<Button>();
            xbtn.targetGraphic = ximg;
            xbtn.onClick.AddListener(() =>
            {
                HideSilent();
                _onDismissedByX?.Invoke();
            });
            var xlabel = new GameObject("X", typeof(RectTransform));
            xlabel.transform.SetParent(xGo.transform, false);
            var xlrt = (RectTransform)xlabel.transform;
            xlrt.anchorMin = Vector2.zero;
            xlrt.anchorMax = Vector2.one;
            xlrt.offsetMin = Vector2.zero;
            xlrt.offsetMax = Vector2.zero;
            var xtmp = xlabel.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(xtmp, UIThemeTextRole.Secondary);
            xtmp.fontSize = UITheme.Typography.Tiny;
            xtmp.alignment = TextAlignmentOptions.Center;
            xtmp.text = "✕";
            xtmp.raycastTarget = false;

            _root.SetActive(false);
        }
    }
}
