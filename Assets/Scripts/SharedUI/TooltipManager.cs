using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// MUI-3 (M-UIPolish 2026-05-06): centralny manager tooltipów. Singleton, auto-spawn,
    /// pojedynczy shared widget overlay na top'ie canvas hierarchy.
    ///
    /// <para><b>Architektura:</b></para>
    /// - Single shared `_tooltipWidget` GameObject (BackgroundImage + TMP_Text + CanvasGroup)
    /// - Pozycjonowanie: pod cursorem z offset (16, -16), screen edge clamp gdy out-of-bounds
    /// - Fade in/out (100ms default z <see cref="UITheme.Transitions.Default"/>)
    /// - Hover delay 500ms (configurable per <see cref="TooltipTrigger.hoverDelay"/>)
    /// - Activate przez <see cref="TooltipTrigger"/> attached do dowolnego UI elementu
    ///
    /// <para><b>Use case:</b></para>
    /// <code>
    /// // Auto-attached przez UIBuilders.MakeIconButton(tooltipText: "Buduj torowisko")
    /// // Manual: button.gameObject.AddComponent&lt;TooltipTrigger&gt;().text = "...";
    /// </code>
    ///
    /// <para><b>Wymaga:</b></para>
    /// - W scenie istnieje Canvas (RenderMode ScreenSpaceOverlay preferred). Jeśli nie ma,
    ///   manager loguje warning i tworzy własny overlay canvas.
    /// </summary>
    public class TooltipManager : MonoBehaviour
    {
        public static TooltipManager Instance { get; private set; }

        GameObject _tooltipWidget;
        TextMeshProUGUI _textComponent;
        CanvasGroup _canvasGroup;
        RectTransform _widgetRt;
        Coroutine _fadeRoutine;
        Canvas _hostCanvas;
        bool _ownsHostCanvas;

        const float MaxTooltipWidth = 320f;
        const float ScreenEdgePadding = 8f;
        const float CursorOffsetX = 16f;
        const float CursorOffsetY = -16f;

        public static TooltipManager EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("TooltipManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<TooltipManager>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildTooltipWidget();
        }

        void OnDestroy()
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (_ownsHostCanvas && _hostCanvas != null)
            {
                Destroy(_hostCanvas.gameObject);
            }
            else if (_tooltipWidget != null)
            {
                Destroy(_tooltipWidget);
            }

            if (Instance == this) Instance = null;
        }

        void BuildTooltipWidget()
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (_tooltipWidget != null)
                Destroy(_tooltipWidget);

            _hostCanvas = FindOrCreateOverlayCanvas();
            if (_hostCanvas == null)
            {
                Log.Warn("[TooltipManager] Brak Canvas — tooltipy nieaktywne");
                return;
            }

            _tooltipWidget = new GameObject("TooltipWidget",
                typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            _tooltipWidget.transform.SetParent(_hostCanvas.transform, false);
            _widgetRt = (RectTransform)_tooltipWidget.transform;
            _widgetRt.anchorMin = new Vector2(0f, 1f); // Anchor top-left = łatwiej liczyć absolute pos
            _widgetRt.anchorMax = new Vector2(0f, 1f);
            _widgetRt.pivot = new Vector2(0f, 1f);

            // Background (rounded surface, dev2 SecondarySurface z Inset shape)
            var bgImg = _tooltipWidget.GetComponent<Image>();
            UITheme.ApplySurface(bgImg, UITheme.SecondarySurface, UIShapePreset.Inset);
            bgImg.raycastTarget = false; // Tooltip nie blokuje klików

            _canvasGroup = _tooltipWidget.GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            // Text child (TMP_Text z theme typography Small)
            var textGo = new GameObject("TooltipText",
                typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(_tooltipWidget.transform, false);
            _textComponent = textGo.GetComponent<TextMeshProUGUI>();
            _textComponent.text = "";
            _textComponent.fontSize = UITheme.Typography.Small;
            _textComponent.color = UITheme.PrimaryText;
            _textComponent.alignment = TextAlignmentOptions.Left;
            _textComponent.textWrappingMode = TextWrappingModes.Normal;
            _textComponent.raycastTarget = false;
            // Theme font (dev2 runtime SDF z fallback chain dla polskich znaków)
            var tmpFont = UITheme.TmpFont;
            if (tmpFont != null) _textComponent.font = tmpFont;

            var textRt = _textComponent.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(UITheme.Spacing.Sm, UITheme.Spacing.Xs);
            textRt.offsetMax = new Vector2(-UITheme.Spacing.Sm, -UITheme.Spacing.Xs);

            // Always last sibling = render on top of other UI
            _tooltipWidget.transform.SetAsLastSibling();
            _tooltipWidget.SetActive(false);
        }

        Canvas FindOrCreateOverlayCanvas()
        {
            if (_hostCanvas != null)
                return _hostCanvas;

            // Marker pattern: znajdź istniejący canvas po `TooltipOverlayCanvasMark` komponencie
            // (TooltipManager + canvas mają DontDestroyOnLoad — persistują przez scene change).
            // Wcześniej lookup po `gameObject.name == "TooltipOverlayCanvas"` był brittle —
            // każde przemiałowanie GameObject psułoby reuse.
            var existingMark = FindAnyObjectByType<TooltipOverlayCanvasMark>(FindObjectsInactive.Include);
            if (existingMark != null)
            {
                var existing = existingMark.GetComponent<Canvas>();
                if (existing != null && existing.renderMode == RenderMode.ScreenSpaceOverlay && existing.isRootCanvas)
                {
                    _ownsHostCanvas = false;
                    return existing;
                }
            }

            // Fallback: utwórz dedicated overlay canvas
            var go = new GameObject("TooltipOverlayCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(TooltipOverlayCanvasMark));
            DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999; // Top of stack
            // MUI-10: standard canvas scaler config
            UITheme.ApplyCanvasScaler(go.GetComponent<CanvasScaler>());
            _ownsHostCanvas = true;
            return canvas;
        }

        /// <summary>Marker komponent — identyfikuje canvas TooltipManager'a niezależnie od nazwy GameObject.</summary>
        private class TooltipOverlayCanvasMark : MonoBehaviour { }

        /// <summary>
        /// Pokazuje tooltip z tekstem na podanej pozycji (screen pixels).
        /// Jeśli już aktywny → updateuje tekst i pozycję.
        /// </summary>
        public void Show(string text, Vector2 screenPos)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_tooltipWidget == null || _hostCanvas == null)
                BuildTooltipWidget();
            if (_tooltipWidget == null) return;

            _textComponent.text = text;

            // Compute preferred size (TMP) — wide max 320px, height auto
            var preferred = _textComponent.GetPreferredValues(text, MaxTooltipWidth, 0f);
            float widgetWidth = Mathf.Min(preferred.x, MaxTooltipWidth) + UITheme.Spacing.Sm * 2;
            float widgetHeight = preferred.y + UITheme.Spacing.Xs * 2;
            _widgetRt.sizeDelta = new Vector2(widgetWidth, widgetHeight);

            PositionWidget(screenPos);
            _tooltipWidget.SetActive(true);

            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutine(_canvasGroup.alpha, 1f, UITheme.Transitions.Default));
        }

        /// <summary>Ukrywa tooltip z fade out.</summary>
        public void Hide()
        {
            if (_tooltipWidget == null || !_tooltipWidget.activeSelf) return;
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeRoutineThenDeactivate(_canvasGroup.alpha, 0f, UITheme.Transitions.Default));
        }

        void PositionWidget(Vector2 cursorScreenPos)
        {
            // Anchor top-left of widget. Position widget BELOW-RIGHT of cursor by default.
            // Screen edge clamp: jeśli wystaje za prawą / dolną krawędź → flip.
            float w = _widgetRt.sizeDelta.x;
            float h = _widgetRt.sizeDelta.y;

            float x = cursorScreenPos.x + CursorOffsetX;
            float y = cursorScreenPos.y + CursorOffsetY;

            // Right edge clamp — flip do lewa od kursora
            if (x + w > Screen.width - ScreenEdgePadding)
                x = cursorScreenPos.x - CursorOffsetX - w;
            // Left edge clamp (po flipie)
            if (x < ScreenEdgePadding)
                x = ScreenEdgePadding;

            // Bottom edge clamp — flip do góry (anchor top-left, więc y to top edge widget'u)
            if (y - h < ScreenEdgePadding)
                y = cursorScreenPos.y - CursorOffsetY + h;
            // Top edge clamp
            if (y > Screen.height - ScreenEdgePadding)
                y = Screen.height - ScreenEdgePadding;

            // Convert do RectTransform position (anchor top-left at canvas (0, Screen.height) = anchor top-left)
            _widgetRt.anchoredPosition = new Vector2(x, y - Screen.height);
        }

        IEnumerator FadeRoutine(float from, float to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                _canvasGroup.alpha = Mathf.Lerp(from, to, t / duration);
                yield return null;
            }
            _canvasGroup.alpha = to;
            _fadeRoutine = null;
        }

        IEnumerator FadeRoutineThenDeactivate(float from, float to, float duration)
        {
            yield return FadeRoutine(from, to, duration);
            _tooltipWidget.SetActive(false);
        }
    }
}
