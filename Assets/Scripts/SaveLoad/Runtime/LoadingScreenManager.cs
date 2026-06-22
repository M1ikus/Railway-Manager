using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-10: Loading screen overlay dla długich operacji (New Game, Load Save,
    /// scene transitions, save/load duże bundle'i).
    ///
    /// Pre-EA scope: czarny background, progress bar 0-100%, title + rotating tip.
    /// Pełen polish (artwork, animations, async resource pre-loading) → M12c Visual.
    ///
    /// **TODO M-UIPolish (MUI-4):** Text components są Legacy — migracja na TMP_Text
    /// razem z innymi UI (50 plików × 213 wystąpień). Kolory już z UITheme (OverlayBg/
    /// CardBg/CardInsetBg/TipBg).
    ///
    /// API:
    /// - <see cref="LoadSceneAsync(string, Action{float}, Action)"/> — Unity SceneManager.LoadSceneAsync
    ///   z reportingiem progress + callback po complete. UI overlay zostaje dopóki
    ///   onComplete nie wywoła Hide.
    /// - <see cref="RunLongOperationAsync(IEnumerator, string, Action{float})"/> — dla load
    ///   save'a (operacja niezwiązana ze sceną).
    ///
    /// Pattern: callable z każdej sceny, overlay nad wszystkim (sortingOrder = 9999).
    /// DontDestroyOnLoad → przeżywa scene transitions.
    /// </summary>
    public class LoadingScreenManager : MonoBehaviour
    {
        public static LoadingScreenManager Instance { get; private set; }

        // ── UI references ────────────────────────────────────────────

        Canvas _canvas;
        GameObject _root;
        Slider _progressBar;
        TextMeshProUGUI _titleText;
        TextMeshProUGUI _tipText;
        TextMeshProUGUI _percentText;

        static readonly Color OverlayBg = UITheme.WithAlpha(UITheme.AppBackground, 0.98f);
        static readonly Color CardBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
        static readonly Color CardInsetBg = UITheme.WithAlpha(UITheme.PrimarySurface, 0.90f);
        static readonly Color TipBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.14f);

        // ── Tip rotation ─────────────────────────────────────────────

        const float TipRotationSec = 4f;
        float _tipTimer;
        int _currentTipIndex;
        readonly List<string> _tipKeys = new List<string>
        {
            "loading.tips.tip1",
            "loading.tips.tip2",
            "loading.tips.tip3",
            "loading.tips.tip4",
            "loading.tips.tip5",
            "loading.tips.tip6",
            "loading.tips.tip7"
        };

        // ── State ────────────────────────────────────────────────────

        public bool IsVisible => _root != null && _root.activeSelf;

        // ── Bootstrap ────────────────────────────────────────────────

        public static LoadingScreenManager EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("LoadingScreenManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<LoadingScreenManager>();
            Log.Info("[LoadingScreenManager] Bootstrapped");
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            BuildUI();
            _root.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!IsVisible) return;
            // Tip rotation
            _tipTimer += Time.unscaledDeltaTime;
            if (_tipTimer >= TipRotationSec)
            {
                _tipTimer = 0f;
                ShowNextTip();
            }
        }

        // ── Public API ───────────────────────────────────────────────

        public void Show(string title)
        {
            EnsureExists();
            _root.SetActive(true);
            if (_titleText != null)
                _titleText.text = title ?? LocalizationService.Get("loading.default_title");
            ShowNextTip();
            SetProgress(0f);
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        public void SetProgress(float progress01)
        {
            float p = Mathf.Clamp01(progress01);
            if (_progressBar != null) _progressBar.value = p;
            if (_percentText != null) _percentText.text = $"{Mathf.RoundToInt(p * 100f)}%";
        }

        /// <summary>
        /// Async load sceny z progress bar. SceneManager.LoadSceneAsync raportuje
        /// progress 0..0.9 (0.9 = ready do activate). My mapujemy 0..0.9 → 0..1
        /// żeby gracz widział 0-100%.
        /// </summary>
        public Coroutine LoadSceneAsync(string sceneName,
                                         Action<float> onProgress = null,
                                         Action onComplete = null)
        {
            return StartCoroutine(LoadSceneCoroutine(sceneName, onProgress, onComplete));
        }

        private IEnumerator LoadSceneCoroutine(string sceneName,
                                                Action<float> onProgress,
                                                Action onComplete)
        {
            Show(string.Format(LocalizationService.Get("loading.scene_format"), sceneName));

            var op = SceneManager.LoadSceneAsync(sceneName);
            if (op == null)
            {
                Log.Error($"[LoadingScreenManager] LoadSceneAsync('{sceneName}') returned null");
                Hide();
                onComplete?.Invoke();
                yield break;
            }

            op.allowSceneActivation = true;
            while (!op.isDone)
            {
                // Unity raportuje 0..0.9 (loading), potem 0.9..1 (activating).
                // Mapujemy 0..0.9 → 0..1 żeby gracz widział pełen progress.
                float mapped = Mathf.Clamp01(op.progress / 0.9f);
                SetProgress(mapped);
                onProgress?.Invoke(mapped);
                yield return null;
            }

            SetProgress(1f);
            onProgress?.Invoke(1f);
            onComplete?.Invoke();

            // Hide po krótkim opóźnieniu (1 frame) żeby Unity zdążył apply nową scenę
            yield return null;
            Hide();
        }

        /// <summary>
        /// Async długiej operacji niezwiązanej ze sceną (np. load save'a).
        /// Operation jest IEnumerator który wykonuje pracę i opcjonalnie reportuje
        /// progress przez yielding wartości float (0..1).
        /// </summary>
        public Coroutine RunLongOperationAsync(IEnumerator operation, string title,
                                                 Action<float> onProgress = null)
        {
            return StartCoroutine(RunLongOperationCoroutine(operation, title, onProgress));
        }

        private IEnumerator RunLongOperationCoroutine(IEnumerator operation, string title,
                                                       Action<float> onProgress)
        {
            Show(title);
            while (operation.MoveNext())
            {
                if (operation.Current is float p)
                {
                    SetProgress(p);
                    onProgress?.Invoke(p);
                }
                yield return operation.Current;
            }
            SetProgress(1f);
            onProgress?.Invoke(1f);
            yield return null;
            Hide();
        }

        // ── Tip rotation ─────────────────────────────────────────────

        private void ShowNextTip()
        {
            if (_tipKeys.Count == 0 || _tipText == null) return;
            string key = _tipKeys[_currentTipIndex % _tipKeys.Count];
            _tipText.text = LocalizationService.Get(key);
            _currentTipIndex++;
        }

        // ── UI build ────────────────────────────────────────────────

        private void BuildUI()
        {
            var canvasGo = new GameObject("LoadingCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 9999;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRt = (RectTransform)_root.transform;
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            var bg = _root.AddComponent<Image>();
            UITheme.ApplySurface(bg, OverlayBg, UIShapePreset.Panel);

            var cardGo = new GameObject("Card", typeof(RectTransform));
            cardGo.transform.SetParent(_root.transform, false);
            var cardRt = (RectTransform)cardGo.transform;
            cardRt.anchorMin = new Vector2(0.5f, 0.5f);
            cardRt.anchorMax = new Vector2(0.5f, 0.5f);
            cardRt.pivot = new Vector2(0.5f, 0.5f);
            cardRt.anchoredPosition = new Vector2(0f, 10f);
            cardRt.sizeDelta = new Vector2(760f, 250f);
            var cardBg = cardGo.AddComponent<Image>();
            UITheme.ApplySurface(cardBg, CardBg, UIShapePreset.PanelLarge);

            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(cardGo.transform, false);
            var trt = (RectTransform)titleGo.transform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.anchoredPosition = new Vector2(0f, -22f);
            trt.offsetMin = new Vector2(28f, -54f);
            trt.offsetMax = new Vector2(-28f, 0f);
            _titleText = titleGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(_titleText, UIThemeTextRole.Primary);
            _titleText.fontSize = 28;
            _titleText.alignment = TextAlignmentOptions.Center;

            var progressCard = new GameObject("ProgressCard", typeof(RectTransform));
            progressCard.transform.SetParent(cardGo.transform, false);
            var progressCardRt = (RectTransform)progressCard.transform;
            progressCardRt.anchorMin = new Vector2(0f, 0.5f);
            progressCardRt.anchorMax = new Vector2(1f, 0.5f);
            progressCardRt.pivot = new Vector2(0.5f, 0.5f);
            progressCardRt.anchoredPosition = new Vector2(0f, 8f);
            progressCardRt.sizeDelta = new Vector2(-56f, 84f);
            var progressCardBg = progressCard.AddComponent<Image>();
            UITheme.ApplySurface(progressCardBg, CardInsetBg, UIShapePreset.Panel);

            var barGo = new GameObject("ProgressBar", typeof(RectTransform));
            barGo.transform.SetParent(progressCard.transform, false);
            var brt = (RectTransform)barGo.transform;
            brt.anchorMin = new Vector2(0f, 0.5f);
            brt.anchorMax = new Vector2(1f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.offsetMin = new Vector2(20f, -12f);
            brt.offsetMax = new Vector2(-92f, 12f);

            var bgImg = barGo.AddComponent<Image>();
            UITheme.ApplySurface(bgImg, UITheme.TopBarInset, UIShapePreset.Pill);

            _progressBar = barGo.AddComponent<Slider>();
            _progressBar.minValue = 0f;
            _progressBar.maxValue = 1f;
            _progressBar.value = 0f;
            _progressBar.interactable = false;
            _progressBar.transition = Selectable.Transition.None;

            var fillArea = new GameObject("FillArea", typeof(RectTransform));
            fillArea.transform.SetParent(barGo.transform, false);
            var fart = (RectTransform)fillArea.transform;
            fart.anchorMin = Vector2.zero;
            fart.anchorMax = Vector2.one;
            fart.offsetMin = new Vector2(2f, 2f);
            fart.offsetMax = new Vector2(-2f, -2f);

            var fill = new GameObject("Fill", typeof(RectTransform));
            fill.transform.SetParent(fillArea.transform, false);
            var fillRt = (RectTransform)fill.transform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            UITheme.ApplySurface(fillImg, UITheme.PrimaryAccent, UIShapePreset.Pill);

            _progressBar.fillRect = fillRt;
            _progressBar.targetGraphic = bgImg;
            _progressBar.handleRect = null;

            var pctGo = new GameObject("Percent", typeof(RectTransform));
            pctGo.transform.SetParent(progressCard.transform, false);
            var pctrt = (RectTransform)pctGo.transform;
            pctrt.anchorMin = new Vector2(1f, 0.5f);
            pctrt.anchorMax = new Vector2(1f, 0.5f);
            pctrt.pivot = new Vector2(1f, 0.5f);
            pctrt.anchoredPosition = new Vector2(-18f, 0f);
            pctrt.sizeDelta = new Vector2(68f, 30f);
            _percentText = pctGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(_percentText, UIThemeTextRole.Accent);
            _percentText.fontSize = 16;
            _percentText.alignment = TextAlignmentOptions.Center;

            var tipCard = new GameObject("TipCard", typeof(RectTransform));
            tipCard.transform.SetParent(cardGo.transform, false);
            var tipCardRt = (RectTransform)tipCard.transform;
            tipCardRt.anchorMin = new Vector2(0f, 0f);
            tipCardRt.anchorMax = new Vector2(1f, 0f);
            tipCardRt.pivot = new Vector2(0.5f, 0f);
            tipCardRt.anchoredPosition = new Vector2(0f, 18f);
            tipCardRt.sizeDelta = new Vector2(-56f, 62f);
            var tipCardBg = tipCard.AddComponent<Image>();
            UITheme.ApplySurface(tipCardBg, TipBg, UIShapePreset.Panel);

            var tipGo = new GameObject("Tip", typeof(RectTransform));
            tipGo.transform.SetParent(tipCard.transform, false);
            var tiprt = (RectTransform)tipGo.transform;
            tiprt.anchorMin = new Vector2(0f, 0f);
            tiprt.anchorMax = new Vector2(1f, 1f);
            tiprt.offsetMin = new Vector2(18f, 10f);
            tiprt.offsetMax = new Vector2(-18f, -10f);
            _tipText = tipGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(_tipText, UIThemeTextRole.Secondary);
            _tipText.fontSize = 14;
            _tipText.alignment = TextAlignmentOptions.Center;
            _tipText.fontStyle = FontStyles.Italic;
        }
    }
}
