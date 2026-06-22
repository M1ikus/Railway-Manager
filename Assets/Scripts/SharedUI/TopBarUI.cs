using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.Core.Settings;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// Shared top bar for gameplay scenes.
    /// Built procedurally so scenes do not need manual UI wiring.
    /// </summary>
    public class TopBarUI : MonoBehaviour
    {
        private TMP_InputField depotNameInput;
        private TMP_Text clockText;
        private Button speed1xButton;
        private Button speed5xButton;
        private Button speed25xButton;
        private Button speed150xButton;
        private Button speed500xButton;
        private Button speedPauseButton;
        private TMP_Text moneyText;
        private Button map2dButton;
        private TMP_Text map2dButtonLabel;
        private Image _repBarFill;
        private TMP_Text _repBarLabel;
        private bool _isMapScene;

        public float GameTimeSeconds => GameState.GameTimeSeconds;
        public float TimeScale => GameState.TimeScale;
        public bool IsPaused => GameState.IsPaused;

        void Awake()
        {
            BuildUI();
        }

        void Start()
        {
            if (depotNameInput != null)
            {
                depotNameInput.text = GameState.DepotName;
                depotNameInput.onEndEdit.AddListener(val => GameState.DepotName = val);
            }

            if (speed1xButton != null) speed1xButton.onClick.AddListener(() => SetTimeScale(1f));
            if (speed5xButton != null) speed5xButton.onClick.AddListener(() => SetTimeScale(5f));
            if (speed25xButton != null) speed25xButton.onClick.AddListener(() => SetTimeScale(25f));
            if (speed150xButton != null) speed150xButton.onClick.AddListener(() => SetTimeScale(150f));
            if (speed500xButton != null) speed500xButton.onClick.AddListener(() => SetTimeScale(500f));
            if (speedPauseButton != null) speedPauseButton.onClick.AddListener(TogglePause);

            if (map2dButton != null)
            {
                _isMapScene = gameObject.scene.name == "MapScene";
                ApplySceneButtonLabel();
            }

            SetMoney(GameState.Money);
            UpdateSpeedButtonColors();
            UpdateReputationBar();
        }

        void OnEnable()
        {
            LocalizationService.OnLanguageChanged += OnLocaleChanged;
            // 2026-05-13: nowe events w Core. Wcześniej Money mial sync bug (UI refreshowane
            // tylko gdy ktoś wywoła SetMoney; bezpośrednie `GameState.Money -= X` z innych
            // serwisów — PayrollService/FleetService/JobService — nie aktualizowało UI).
            // Reputation pollowane per-frame w Update.
            GameState.OnMoneyChanged += OnMoneyChangedHandler;
            GameState.OnGlobalReputationChanged += OnReputationChangedHandler;
            // M-Economy Faza 2b: zmiana waluty wyświetlania (Apply w Settings) → przerenderuj kwotę.
            SettingsService.OnSettingsChanged += OnSettingsChangedHandler;
        }

        void OnDisable()
        {
            LocalizationService.OnLanguageChanged -= OnLocaleChanged;
            GameState.OnMoneyChanged -= OnMoneyChangedHandler;
            GameState.OnGlobalReputationChanged -= OnReputationChangedHandler;
            SettingsService.OnSettingsChanged -= OnSettingsChangedHandler;
        }

        private void OnSettingsChangedHandler()
        {
            // Waluta mogła się zmienić → przerenderuj bilans aktualną wartością PLN (FormatCurrency konwertuje).
            if (moneyText != null)
                moneyText.text = NumberFormatService.FormatCurrency(GameState.Money);
        }

        void Update()
        {
            // Tick zegara i day rollover są w GameClock (Core) — TopBar tylko wyświetla.
            // Money + Reputation refreshowane event-driven (OnEnable subscribe), nie per-frame.
            UpdateClockDisplay();
        }

        private void OnMoneyChangedHandler(long oldValue, long newValue)
        {
            if (moneyText != null)
                moneyText.text = NumberFormatService.FormatCurrency(newValue);
        }

        private void OnReputationChangedHandler(int oldValue, int newValue)
        {
            UpdateReputationBar();
        }

        public void SetSceneButton(string label, System.Action switchAction)
        {
            if (map2dButton == null)
                return;

            map2dButton.interactable = true;
            if (map2dButtonLabel != null)
                map2dButtonLabel.text = label;

            map2dButton.onClick.RemoveAllListeners();
            map2dButton.onClick.AddListener(() => switchAction?.Invoke());
        }

        public void SetTimeScale(float scale)
        {
            GameState.TimeScale = scale;
            GameState.IsPaused = false;
            UpdateSpeedButtonColors();
        }

        public void TogglePause()
        {
            GameState.IsPaused = !GameState.IsPaused;
            UpdateSpeedButtonColors();
        }

        public void SetMoney(long amount)
        {
            GameState.Money = amount;
            if (moneyText != null)
                moneyText.text = NumberFormatService.FormatCurrency(amount);
        }

        public void AddMoney(long amount)
        {
            SetMoney(GameState.Money + amount);
        }

        public long GetMoney() => GameState.Money;

        private void OnLocaleChanged()
        {
            if (map2dButton != null)
                ApplySceneButtonLabel();

            if (depotNameInput != null && depotNameInput.placeholder is TMP_Text phText)
                phText.text = LocalizationService.Get("top_bar.depot_name_placeholder");

            SetMoney(GameState.Money);
            UpdateReputationBar();
        }

        private void ApplySceneButtonLabel()
        {
            if (_isMapScene)
                SetSceneButton(LocalizationService.Get("top_bar.scene_button.depot"), SceneController.SwitchToDepot);
            else
                SetSceneButton(LocalizationService.Get("top_bar.scene_button.map"), SceneController.SwitchToMap);
        }

        private void UpdateClockDisplay()
        {
            if (clockText == null)
                return;

            int totalMinutes = Mathf.FloorToInt(GameState.GameTimeSeconds / 60f);
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            clockText.text = $"{hours:D2}:{minutes:D2}";
        }

        private void UpdateSpeedButtonColors()
        {
            SetButtonColor(speed1xButton, !GameState.IsPaused && Mathf.Approximately(GameState.TimeScale, 1f));
            SetButtonColor(speed5xButton, !GameState.IsPaused && Mathf.Approximately(GameState.TimeScale, 5f));
            SetButtonColor(speed25xButton, !GameState.IsPaused && Mathf.Approximately(GameState.TimeScale, 25f));
            SetButtonColor(speed150xButton, !GameState.IsPaused && Mathf.Approximately(GameState.TimeScale, 150f));
            SetButtonColor(speed500xButton, !GameState.IsPaused && Mathf.Approximately(GameState.TimeScale, 500f));
            SetButtonColor(speedPauseButton, GameState.IsPaused);
        }

        private void SetButtonColor(Button button, bool active)
        {
            if (button == null)
                return;

            Color normal = active ? UITheme.PrimaryAccent : UITheme.SecondarySurface;
            Color highlighted = active ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface;
            Color pressed = active ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border;
            button.colors = UITheme.CreateColorBlock(normal, highlighted, pressed, normal, UITheme.WithAlpha(UITheme.Border, 0.55f));

            if (button.targetGraphic is Image bg)
                bg.color = normal;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.color = active ? UITheme.InverseText : UITheme.PrimaryText;
        }

        private void BuildUI()
        {
            RectTransform rt = GetComponent<RectTransform>();
            if (rt == null)
                rt = gameObject.AddComponent<RectTransform>();

            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 52f);

            Image bg = GetComponent<Image>();
            if (bg == null)
                bg = gameObject.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.TopBarBackground, UIShapePreset.Panel);

            HorizontalLayoutGroup layout = gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = UITheme.Spacing.Md;
            layout.padding = UITheme.Padding(UITheme.Spacing.Lg, UITheme.Spacing.Sm);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = false;
            layout.childControlHeight = true;

            depotNameInput = CreateInputField(transform, "DepotName", 220f, 14, LocalizationService.Get("top_bar.depot_name_placeholder"));
            CreateSeparator(transform);

            clockText = CreateText(transform, "Clock", "06:00", 18, UIThemeTextRole.Primary, 86f, TextAlignmentOptions.Center);
            CreateSeparator(transform);

            speed1xButton = CreateSmallButton(transform, "Speed1x", "x1", 36f);
            speed5xButton = CreateSmallButton(transform, "Speed5x", "x5", 36f);
            speed25xButton = CreateSmallButton(transform, "Speed25x", "x25", 40f);
            speed150xButton = CreateSmallButton(transform, "Speed150x", "x150", 46f);
            speed500xButton = CreateSmallButton(transform, "Speed500x", "x500", 46f);
            speedPauseButton = CreateSmallButton(transform, "SpeedPause", "||", 40f);

            GameObject spacer = new GameObject("Spacer");
            spacer.transform.SetParent(transform, false);
            RectTransform spacerRt = spacer.AddComponent<RectTransform>();
            spacerRt.sizeDelta = new Vector2(10f, 32f);
            LayoutElement spacerLe = spacer.AddComponent<LayoutElement>();
            spacerLe.flexibleWidth = 1f;

            // Placeholder pusty — Start() wymusi SetMoney(GameState.Money) → FormatCurrency.
            moneyText = CreateText(transform, "Money", string.Empty, 16, UIThemeTextRole.Success, 150f, TextAlignmentOptions.Right);
            BuildReputationBar();

            CreateSeparator(transform);

            map2dButton = CreateSmallButton(transform, "Map2D", string.Empty, 92f);
            map2dButtonLabel = map2dButton.GetComponentInChildren<TMP_Text>();
        }

        private void BuildReputationBar()
        {
            var container = new GameObject("ReputationBar", typeof(RectTransform));
            container.transform.SetParent(transform, false);
            RectTransform rt = container.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(148f, 24f);

            LayoutElement le = container.AddComponent<LayoutElement>();
            le.preferredWidth = 148f;
            le.minWidth = 148f;

            var bg = new GameObject("Bg", typeof(RectTransform));
            bg.transform.SetParent(container.transform, false);
            RectTransform bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = new Vector2(0f, 4f);
            bgRt.offsetMax = new Vector2(0f, -4f);
            Image bgImg = bg.AddComponent<Image>();
            UITheme.ApplySurface(bgImg, UITheme.TopBarInset, UIShapePreset.Pill);

            var fill = new GameObject("Fill", typeof(RectTransform));
            fill.transform.SetParent(bg.transform, false);
            RectTransform fillRt = fill.GetComponent<RectTransform>();
            fillRt.anchorMin = new Vector2(0f, 0f);
            fillRt.anchorMax = new Vector2(0.5f, 1f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _repBarFill = fill.AddComponent<Image>();
            UITheme.ApplySurface(_repBarFill, UITheme.Success, UIShapePreset.Pill);

            var label = new GameObject("Label", typeof(RectTransform));
            label.transform.SetParent(container.transform, false);
            RectTransform labelRt = label.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            _repBarLabel = label.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(_repBarLabel, UIThemeTextRole.Primary);
            _repBarLabel.fontSize = 12;
            _repBarLabel.alignment = TextAlignmentOptions.Center;
            _repBarLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _repBarLabel.raycastTarget = false;
            _repBarLabel.text = LocalizationService.Get("top_bar.reputation_format", 50);
        }

        private void UpdateReputationBar()
        {
            if (_repBarFill == null)
                return;

            int rep = GameState.GlobalReputation;
            float pct = Mathf.Clamp01(rep / 100f);
            _repBarFill.rectTransform.anchorMax = new Vector2(pct, 1f);
            _repBarFill.color = UITheme.GetReputationColor(rep);

            if (_repBarLabel != null)
                _repBarLabel.text = LocalizationService.Get("top_bar.reputation_format", rep);
        }

        private TMP_Text CreateText(Transform parent, string name, string content, int fontSize, UIThemeTextRole role, float width, TextAlignmentOptions alignment)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, 32f);

            LayoutElement le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = width;

            TextMeshProUGUI text = obj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(text, role);
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.text = content;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            return text;
        }

        private TMP_InputField CreateInputField(Transform parent, string name, float width, int fontSize, string placeholder)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, 32f);

            LayoutElement le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = width;

            Image bg = obj.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.TopBarInset, UIShapePreset.Button);

            GameObject viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(obj.transform, false);
            RectTransform viewportRt = viewportObj.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(8f, 4f);
            viewportRt.offsetMax = new Vector2(-8f, -4f);
            viewportObj.AddComponent<RectMask2D>();

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(viewportObj.transform, false);
            RectTransform textRt = textObj.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            TextMeshProUGUI textComp = textObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(textComp, UIThemeTextRole.Primary);
            textComp.fontSize = fontSize;
            textComp.alignment = TextAlignmentOptions.MidlineLeft;
            textComp.textWrappingMode = TextWrappingModes.NoWrap;
            textComp.richText = false;
            textComp.raycastTarget = false;

            GameObject phObj = new GameObject("Placeholder");
            phObj.transform.SetParent(viewportObj.transform, false);
            RectTransform phRt = phObj.AddComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;

            TextMeshProUGUI phText = phObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(phText, UIThemeTextRole.Secondary);
            phText.fontSize = fontSize;
            phText.fontStyle = FontStyles.Italic;
            phText.alignment = TextAlignmentOptions.MidlineLeft;
            phText.textWrappingMode = TextWrappingModes.NoWrap;
            phText.raycastTarget = false;
            phText.text = placeholder;

            TMP_InputField input = obj.AddComponent<TMP_InputField>();
            input.textViewport = viewportRt;
            input.textComponent = textComp;
            input.placeholder = phText;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.targetGraphic = bg;

            return input;
        }

        private Button CreateSmallButton(Transform parent, string name, string label, float width)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, 32f);

            LayoutElement le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = width;

            Image bg = obj.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.SecondarySurface, UIShapePreset.Pill);

            Button button = obj.AddComponent<Button>();
            button.targetGraphic = bg;
            button.colors = UITheme.CreateColorBlock(
                UITheme.SecondarySurface,
                UITheme.RaisedSurface,
                UITheme.Border,
                UITheme.SecondarySurface,
                UITheme.WithAlpha(UITheme.Border, 0.55f));

            GameObject labelObj = new GameObject("Label");
            labelObj.transform.SetParent(obj.transform, false);
            RectTransform labelRt = labelObj.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            TextMeshProUGUI text = labelObj.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(text, UIThemeTextRole.Primary);
            text.fontSize = 14;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.text = label;

            return button;
        }

        private void CreateSeparator(Transform parent)
        {
            GameObject sep = new GameObject("Sep");
            sep.transform.SetParent(parent, false);

            RectTransform rt = sep.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(2f, 32f);

            LayoutElement le = sep.AddComponent<LayoutElement>();
            le.preferredWidth = 2f;

            Image img = sep.AddComponent<Image>();
            img.color = UITheme.TopBarDivider;
        }
    }
}
