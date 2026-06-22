using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M6-4: Panel "Finanse" — tabela bilansu dziennego (dzisiaj + historia).
    ///
    /// Otwierany przez <see cref="UIIntent.OpenFinancesPanel"/> (zakładka Finanse
    /// w MainTabBarUI), zamykany przez <see cref="UIIntent.CloseFinancesPanel"/>
    /// lub ESC. Full-screen overlay na Depot scene.
    ///
    /// Pokazuje:
    /// - Dzisiaj: rev / cost / subsidy / NET (live, updates co frame gdy panel otwarty)
    /// - Per linia: tabela obiegów z rev/cost/pax
    /// - Historia: ostatnie 7 dni (zwinięta lista)
    /// </summary>
    public class FinancePanelUI : MonoBehaviour
    {
        public static FinancePanelUI Instance { get; private set; }

        private static readonly Color OverlayBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.985f);
        private static readonly Color HeaderBg = UITheme.WithAlpha(UITheme.TopBarInset, 0.94f);
        private static readonly Color ColumnBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.9f);

        Canvas _canvas;
        GameObject _root;
        TextMeshProUGUI _todaySummaryText;
        TextMeshProUGUI _linesText;
        TextMeshProUGUI _historyText;
        bool _isVisible;

        public static FinancePanelUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("FinancePanelUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<FinancePanelUI>();
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

        void OnEnable()
        {
            UIIntents.OnIntent += HandleUIIntent;
        }

        void OnDisable()
        {
            UIIntents.OnIntent -= HandleUIIntent;
        }

        void HandleUIIntent(UIIntent intent)
        {
            if (intent == UIIntent.OpenFinancesPanel && !_isVisible) Show();
            else if (intent == UIIntent.CloseFinancesPanel && _isVisible) Hide();
        }

        void Update()
        {
            if (_isVisible) RefreshContent();
        }

        public void Show()
        {
            _root.SetActive(true);
            _isVisible = true;
            SceneController.FullscreenOverlayOpen = true;
        }

        public void Hide()
        {
            _root.SetActive(false);
            _isVisible = false;
            SceneController.FullscreenOverlayOpen = false;
        }

        void RefreshContent()
        {
            var econ = EconomyManager.Instance;
            if (econ == null)
            {
                _todaySummaryText.text = LocalizationService.Get("finance.service_inactive");
                return;
            }

            // Dzisiaj summary
            string today = GameState.CurrentDateIso;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(string.Format(LocalizationService.Get("finance.today.header_format"), today));
            sb.AppendLine();
            sb.AppendLine(string.Format(LocalizationService.Get("finance.today.revenue_format"), (econ.RevenueTodayGroszy / 100f).ToString("F0")));
            sb.AppendLine(string.Format(LocalizationService.Get("finance.today.costs_format"), (econ.CostsTodayGroszy / 100f).ToString("F0")));
            sb.AppendLine(string.Format(LocalizationService.Get("finance.today.subsidies_format"), (econ.SubsidiesTodayGroszy / 100f).ToString("F0")));
            sb.AppendLine(string.Format(LocalizationService.Get("finance.today.net_format"),
                ToHex(econ.NetTodayGroszy >= 0 ? UITheme.Success : UITheme.Danger),
                (econ.NetTodayGroszy / 100f).ToString("F0")));
            sb.AppendLine();
            sb.AppendLine(string.Format(LocalizationService.Get("finance.today.money_format"), GameState.Money.ToString("N0")));
            _todaySummaryText.text = sb.ToString();

            // Per linia
            var lines = new System.Text.StringBuilder();
            lines.AppendLine(LocalizationService.Get("finance.lines.header"));
            lines.AppendLine();
            if (econ.LineBalances.Count == 0)
            {
                lines.AppendLine(LocalizationService.Get("finance.lines.empty"));
            }
            else
            {
                foreach (var kvp in econ.LineBalances)
                {
                    var lb = kvp.Value;
                    long net = lb.NetGroszy;
                    string netColor = ToHex(net >= 0 ? UITheme.Success : UITheme.Danger);

                    // Nazwa obiegu zamiast ID
                    string lineName;
                    if (lb.circulationId < 0)
                        lineName = LocalizationService.Get("finance.lines.overhead_label");
                    else
                    {
                        var circ = CirculationService.GetCirculation(lb.circulationId);
                        lineName = circ != null
                            ? string.Format(LocalizationService.Get("finance.lines.circ_format"), lb.circulationId, circ.name)
                            : string.Format(LocalizationService.Get("finance.lines.circ_id_format"), lb.circulationId);
                    }

                    lines.AppendLine($"<b>{lineName}</b>");
                    lines.AppendLine(string.Format(LocalizationService.Get("finance.lines.stats_format"), lb.runsCompletedToday, lb.passengerCount));
                    lines.AppendLine(string.Format(LocalizationService.Get("finance.lines.rev_cost_format"),
                        (lb.revenueGroszy / 100f).ToString("F0"), (lb.costsGroszy / 100f).ToString("F0")));

                    // M6-6 dotacja — status/kwota
                    if (lb.circulationId >= 0)
                    {
                        string subStatus = SubsidyCalculator.Explain(lb);
                        string subColor = ToHex(lb.subsidiesGroszy > 0 ? UITheme.PrimaryAccent : UITheme.SecondaryText);
                        lines.AppendLine(string.Format(LocalizationService.Get("finance.lines.subsidy_format"), subColor, subStatus));
                    }

                    lines.AppendLine(string.Format(LocalizationService.Get("finance.lines.net_format"), netColor, (net / 100f).ToString("F0")));
                    lines.AppendLine();
                }
            }
            _linesText.text = lines.ToString();

            // Historia (ostatnie 7 dni) + prosty wykres słupkowy
            var hist = new System.Text.StringBuilder();
            hist.AppendLine(LocalizationService.Get("finance.history.header"));
            hist.AppendLine();
            var history = econ.History;
            if (history.Count == 0)
            {
                hist.AppendLine(LocalizationService.Get("finance.history.empty"));
            }
            else
            {
                int start = Mathf.Max(0, history.Count - 7);

                // Znajdź max |net| do skalowania słupków (absolute)
                long maxAbsNet = 1;
                for (int i = start; i < history.Count; i++)
                    maxAbsNet = System.Math.Max(maxAbsNet, System.Math.Abs(history[i].NetGroszy));

                const int barMaxChars = 20;
                for (int i = history.Count - 1; i >= start; i--)
                {
                    var d = history[i];
                    string netColor = ToHex(d.NetGroszy >= 0 ? UITheme.Success : UITheme.Danger);

                    // Słupek znaków '█' proporcjonalny do |net|/max
                    int barLen = (int)((System.Math.Abs(d.NetGroszy) * barMaxChars) / maxAbsNet);
                    barLen = Mathf.Clamp(barLen, 0, barMaxChars);
                    string bar = new string('█', barLen) + new string('·', barMaxChars - barLen);

                    hist.AppendLine(string.Format(LocalizationService.Get("finance.history.date_format"), d.dateIso));
                    hist.AppendLine(string.Format(LocalizationService.Get("finance.history.bar_format"), netColor, bar));
                    hist.AppendLine(string.Format(LocalizationService.Get("finance.history.stats_format"),
                        (d.revenueGroszy / 100f).ToString("F0"),
                        (d.costsGroszy / 100f).ToString("F0"),
                        (d.subsidiesGroszy / 100f).ToString("F0"),
                        netColor,
                        (d.NetGroszy / 100f).ToString("F0")));
                    hist.AppendLine();
                }
            }
            _historyText.text = hist.ToString();
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("FinancePanelCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 230;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root", typeof(RectTransform));
            _root.transform.SetParent(_canvas.transform, false);
            var rootRt = _root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            var bg = _root.AddComponent<Image>();
            UITheme.ApplySurface(bg, OverlayBg, UIShapePreset.PanelLarge);

            // Header z tytułem + close X
            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(_root.transform, false);
            var hRt = header.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0f, 1f);
            hRt.anchorMax = new Vector2(1f, 1f);
            hRt.pivot = new Vector2(0.5f, 1f);
            hRt.sizeDelta = new Vector2(0f, 60f);
            hRt.anchoredPosition = Vector2.zero;
            var hBg = header.AddComponent<Image>();
            UITheme.ApplySurface(hBg, HeaderBg, UIShapePreset.Panel);

            AddText(header.transform, "Title",
                LocalizationService.Get("finance.title"),
                22, TextAlignmentOptions.MidlineLeft, Color.white, new Vector2(20f, 0f));

            // Close button
            var closeBtn = CreateButton(header.transform, LocalizationService.Get("finance.close_btn"),
                new Vector2(-20f, -5f), new Vector2(50f, 50f),
                new Vector2(1f, 1f), new Vector2(1f, 1f), UITheme.Danger);
            closeBtn.onClick.AddListener(Hide);

            // Center box — 3 kolumny: today summary | lines | history
            var center = new GameObject("Center", typeof(RectTransform));
            center.transform.SetParent(_root.transform, false);
            var cRt = center.GetComponent<RectTransform>();
            cRt.anchorMin = Vector2.zero;
            cRt.anchorMax = Vector2.one;
            cRt.offsetMin = new Vector2(20f, 20f);
            cRt.offsetMax = new Vector2(-20f, -80f);

            // Left column (today summary)
            var leftCol = CreateColumn(center.transform, "Today", 0f, 0.35f);
            _todaySummaryText = AddText(leftCol.transform, "TodayText",
                "", 14, TextAlignmentOptions.TopLeft, Color.white, new Vector2(10f, 10f));
            _todaySummaryText.richText = true;
            _todaySummaryText.textWrappingMode = TextWrappingModes.Normal;
            _todaySummaryText.overflowMode = TextOverflowModes.Overflow;
            FillRect(_todaySummaryText.rectTransform, new Vector2(15f, 15f), new Vector2(-15f, -15f));

            // Middle column (per line)
            var midCol = CreateColumn(center.transform, "Lines", 0.36f, 0.67f);
            _linesText = AddText(midCol.transform, "LinesText",
                "", 14, TextAlignmentOptions.TopLeft, Color.white, new Vector2(10f, 10f));
            _linesText.richText = true;
            _linesText.textWrappingMode = TextWrappingModes.Normal;
            _linesText.overflowMode = TextOverflowModes.Overflow;
            FillRect(_linesText.rectTransform, new Vector2(15f, 15f), new Vector2(-15f, -15f));

            // Right column (history)
            var rightCol = CreateColumn(center.transform, "History", 0.68f, 1f);
            _historyText = AddText(rightCol.transform, "HistoryText",
                "", 14, TextAlignmentOptions.TopLeft, Color.white, new Vector2(10f, 10f));
            _historyText.richText = true;
            _historyText.textWrappingMode = TextWrappingModes.Normal;
            _historyText.overflowMode = TextOverflowModes.Overflow;
            FillRect(_historyText.rectTransform, new Vector2(15f, 15f), new Vector2(-15f, -15f));
        }

        static GameObject CreateColumn(Transform parent, string name, float anchorMinX, float anchorMaxX)
        {
            var col = new GameObject(name, typeof(RectTransform));
            col.transform.SetParent(parent, false);
            var rt = col.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(anchorMinX, 0f);
            rt.anchorMax = new Vector2(anchorMaxX, 1f);
            rt.offsetMin = new Vector2(5f, 0f);
            rt.offsetMax = new Vector2(-5f, 0f);
            var bg = col.AddComponent<Image>();
            UITheme.ApplySurface(bg, ColumnBg, UIShapePreset.Panel);
            return col;
        }

        static TextMeshProUGUI AddText(Transform parent, string name, string text, int fontSize,
                            TextAlignmentOptions alignment, Color color, Vector2 anchoredPos = default)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var txt = go.AddComponent<TextMeshProUGUI>();
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.color = color;
            txt.richText = true;
            txt.raycastTarget = false;
            txt.text = text;
            UITheme.ApplyTmpText(txt, color == UITheme.SecondaryText ? UIThemeTextRole.Secondary : UIThemeTextRole.Primary);
            txt.color = color;
            return txt;
        }

        static void FillRect(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        static Button CreateButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size,
                                    Vector2 anchorMin, Vector2 anchorMax, Color? bgOverride = null)
        {
            var go = new GameObject($"Btn_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = anchorMax;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var img = go.AddComponent<Image>();
            Color bg = bgOverride ?? UITheme.WithAlpha(UITheme.Border, 0.82f);
            UITheme.ApplySurface(img, bg, UIShapePreset.Pill);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = UITheme.CreateColorBlock(
                bg,
                UITheme.Darken(bg, 0.05f),
                UITheme.Darken(bg, 0.12f),
                bg,
                UITheme.WithAlpha(UITheme.Border, 0.55f));

            var txt = AddText(go.transform, "Label", label, 18, TextAlignmentOptions.Center, Color.white);
            txt.raycastTarget = false;
            return btn;
        }

        static string ToHex(Color color) => ColorUtility.ToHtmlStringRGB(color);
    }
}
