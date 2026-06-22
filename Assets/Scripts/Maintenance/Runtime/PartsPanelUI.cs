using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Maintenance
{
    /// <summary>
    /// M7-6: Panel "Magazyn czesci" - 12 typow czesci z zamowieniami.
    ///
    /// Otwiera sie przez <see cref="UIIntent.OpenPartsPanel"/> (zakladka Magazyn
    /// w MainTabBarUI), zamyka przez <see cref="UIIntent.ClosePartsPanel"/> lub ESC.
    /// Pelnoekranowy overlay w Depot scene.
    ///
    /// Layout (2 kolumny):
    /// - Lewa: lista 12 typow z aktualnym stanem + przyciski zamowienia (+1, +5, +20)
    /// - Prawa: pending orders (ETA) + historia zakupow
    /// </summary>
    public class PartsPanelUI : MonoBehaviour
    {
        public static PartsPanelUI Instance { get; private set; }

        Canvas _canvas;
        GameObject _root;
        RectTransform _listContent;
        TextMeshProUGUI _pendingText;
        TextMeshProUGUI _historyText;
        bool _isVisible;
        float _refreshTimer;
        const float RefreshInterval = 0.5f;

        // Rows regenerated on refresh
        readonly List<GameObject> _rows = new();

        private static readonly Color PanelBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
        private static readonly Color HeaderBg = UITheme.WithAlpha(UITheme.TopBarBackground, 1f);
        private static readonly Color ColumnBg = UITheme.WithAlpha(UITheme.PrimarySurface, 0.9f);
        private static readonly Color ScrollBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.16f);
        private static readonly Color SectionHeaderBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f);
        private static readonly Color SectionBodyBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.2f);

        public static PartsPanelUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PartsPanelUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PartsPanelUI>();
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
            if (intent == UIIntent.OpenPartsPanel && !_isVisible) Show();
            else if (intent == UIIntent.ClosePartsPanel && _isVisible) Hide();
        }

        void Update()
        {
            if (_isVisible)
            {
                // BUG-048: unscaledDeltaTime — UI refresh ma działać przy pauzie.
                _refreshTimer += Time.unscaledDeltaTime;
                if (_refreshTimer >= RefreshInterval)
                {
                    _refreshTimer = 0f;
                    RefreshContent();
                }
            }

            if (_isVisible && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SceneController.LastEscConsumedFrame = Time.frameCount;
                Hide();
            }
        }

        public void Show()
        {
            _root.SetActive(true);
            _isVisible = true;
            SceneController.FullscreenOverlayOpen = true;
            RefreshContent();
        }

        public void Hide()
        {
            _root.SetActive(false);
            _isVisible = false;
            SceneController.FullscreenOverlayOpen = false;
        }

        void RefreshContent()
        {
            var svc = PartInventoryService.Instance;
            if (svc == null)
            {
                foreach (var r in _rows) if (r != null) Destroy(r);
                _rows.Clear();
                _pendingText.text = LocalizationService.Get("maintenance.parts.service_inactive");
                _historyText.text = string.Empty;
                return;
            }

            RefreshPartsList(svc);
            RefreshPending(svc);
            RefreshHistory(svc);
        }

        void RefreshPartsList(PartInventoryService svc)
        {
            foreach (var r in _rows) if (r != null) Destroy(r);
            _rows.Clear();

            foreach (var type in PartCatalog.AllTypes)
                _rows.Add(CreatePartRow(svc, type));
        }

        GameObject CreatePartRow(PartInventoryService svc, ComponentType type)
        {
            var info = PartCatalog.Get(type);
            int stock = svc.GetStock(type);

            var row = new GameObject($"Row_{type}", typeof(RectTransform));
            row.transform.SetParent(_listContent, false);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 50f);

            var bg = row.AddComponent<Image>();
            Color rowColor = stock == 0 ? UITheme.WithAlpha(UITheme.Danger, 0.28f)
                           : stock < 3 ? UITheme.WithAlpha(UITheme.Warning, 0.25f)
                                       : UITheme.WithAlpha(UITheme.SecondarySurface, 0.78f);
            UITheme.ApplySurface(bg, rowColor, UIShapePreset.Inset);

            string stockColor = ToHtmlColor(stock == 0 ? UITheme.Danger : stock < 3 ? UITheme.Warning : UITheme.Success);
            string infoText = string.Format(LocalizationService.Get("maintenance.parts.row_info_format"),
                info.displayName, stockColor, stock, info.PriceZl, info.deliveryDays);

            var txt = AddText(row.transform, "Info", infoText, 12, TextAlignmentOptions.MidlineLeft, UIThemeTextRole.Primary);
            txt.rectTransform.anchorMin = new Vector2(0f, 0f);
            txt.rectTransform.anchorMax = new Vector2(0.58f, 1f);
            txt.rectTransform.offsetMin = new Vector2(12f, 0f);
            txt.rectTransform.offsetMax = new Vector2(0f, 0f);
            txt.raycastTarget = false;

            int[] qtys = { 1, 5, 20 };
            for (int i = 0; i < qtys.Length; i++)
            {
                int qty = qtys[i];
                float x = -10f - (qtys.Length - 1 - i) * 92f;
                var btn = CreateButton(row.transform, string.Format(LocalizationService.Get("maintenance.parts.btn_format"), qty),
                    new Vector2(x, 0f), new Vector2(85f, 40f),
                    new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
                UITheme.ApplySurface(btn.image, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.85f), UIShapePreset.Button);
                btn.colors = UITheme.CreateColorBlock(
                    UITheme.WithAlpha(UITheme.PrimaryAccent, 0.85f),
                    UITheme.PrimaryAccentHover,
                    UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f),
                    UITheme.WithAlpha(UITheme.PrimaryAccent, 0.85f),
                    UITheme.WithAlpha(UITheme.Border, 0.55f));

                int capturedQty = qty;
                var capturedType = type;
                btn.onClick.AddListener(() =>
                {
                    if (PartInventoryService.Instance != null)
                    {
                        PartInventoryService.Instance.OrderParts(capturedType, capturedQty);
                        _refreshTimer = RefreshInterval;
                    }
                });
            }

            return row;
        }

        void RefreshPending(PartInventoryService svc)
        {
            var sb = new StringBuilder();
            if (svc.PendingOrders.Count == 0)
            {
                sb.AppendLine(LocalizationService.Get("maintenance.parts.pending.empty"));
            }
            else
            {
                foreach (var po in svc.PendingOrders)
                {
                    var info = PartCatalog.Get(po.type);
                    sb.AppendLine(string.Format(LocalizationService.Get("maintenance.parts.pending.row_format"), po.quantity, info.displayName));
                    sb.AppendLine(string.Format(LocalizationService.Get("maintenance.parts.pending.row_eta_format"),
                        po.daysRemaining, (po.totalCostGroszy / 100f).ToString("F0")));
                    sb.AppendLine();
                }
            }

            _pendingText.text = sb.ToString();
        }

        void RefreshHistory(PartInventoryService svc)
        {
            var sb = new StringBuilder();
            var history = svc.History;
            if (history.Count == 0)
            {
                sb.AppendLine(LocalizationService.Get("maintenance.parts.history.empty"));
            }
            else
            {
                for (int i = history.Count - 1; i >= 0; i--)
                {
                    var h = history[i];
                    var info = PartCatalog.Get(h.type);
                    sb.AppendLine(string.Format(LocalizationService.Get("maintenance.parts.history.row_format"),
                        h.dateIso, h.quantity, info.displayName, (h.totalCostGroszy / 100f).ToString("F0")));
                }
            }

            _historyText.text = sb.ToString();
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("PartsPanelCanvas");
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
            UITheme.ApplySurface(bg, PanelBg, UIShapePreset.PanelLarge);

            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(_root.transform, false);
            var hRt = header.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0f, 1f);
            hRt.anchorMax = new Vector2(1f, 1f);
            hRt.pivot = new Vector2(0.5f, 1f);
            hRt.sizeDelta = new Vector2(0f, 60f);
            hRt.anchoredPosition = Vector2.zero;
            var hBg = header.AddComponent<Image>();
            UITheme.ApplySurface(hBg, HeaderBg, UIShapePreset.PanelLarge);

            AddText(header.transform, "Title",
                LocalizationService.Get("maintenance.parts.title"),
                22, TextAlignmentOptions.MidlineLeft, UIThemeTextRole.Primary);

            var closeBtn = CreateButton(header.transform, LocalizationService.Get("maintenance.parts.close_btn"),
                new Vector2(-20f, -5f), new Vector2(50f, 50f),
                new Vector2(1f, 1f), new Vector2(1f, 1f));
            closeBtn.onClick.AddListener(Hide);

            var center = new GameObject("Center", typeof(RectTransform));
            center.transform.SetParent(_root.transform, false);
            var cRt = center.GetComponent<RectTransform>();
            cRt.anchorMin = Vector2.zero;
            cRt.anchorMax = Vector2.one;
            cRt.offsetMin = new Vector2(20f, 20f);
            cRt.offsetMax = new Vector2(-20f, -80f);

            var leftCol = CreateColumn(center.transform, "PartsList", 0f, 0.65f);
            var listHeader = CreateSectionHeader(leftCol.transform, "ListHeader",
                LocalizationService.Get("maintenance.parts.list_header"));
            var listHeaderRt = listHeader.GetComponent<RectTransform>();
            listHeaderRt.anchorMin = new Vector2(0f, 1f);
            listHeaderRt.anchorMax = new Vector2(1f, 1f);
            listHeaderRt.pivot = new Vector2(0.5f, 1f);
            listHeaderRt.offsetMin = new Vector2(10f, -46f);
            listHeaderRt.offsetMax = new Vector2(-10f, -10f);

            var scroll = CreateScrollContent(leftCol.transform, out var listContent);
            var scrollRt = scroll.GetComponent<RectTransform>();
            scrollRt.offsetMin = new Vector2(8f, 8f);
            scrollRt.offsetMax = new Vector2(-8f, -52f);
            _listContent = listContent;

            var vlg = _listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UITheme.Spacing.Xs;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = false;
            _listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var rightCol = CreateColumn(center.transform, "PendingAndHistory", 0.67f, 1f);

            var pendingCol = new GameObject("Pending", typeof(RectTransform));
            pendingCol.transform.SetParent(rightCol.transform, false);
            var prt = pendingCol.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0f, 0.5f);
            prt.anchorMax = new Vector2(1f, 1f);
            prt.offsetMin = new Vector2(8f, 4f);
            prt.offsetMax = new Vector2(-8f, -4f);
            UITheme.ApplySurface(pendingCol.AddComponent<Image>(), SectionBodyBg, UIShapePreset.Inset);

            var pendingHeader = CreateSectionHeader(pendingCol.transform, "PendingHeader",
                LocalizationService.Get("maintenance.parts.pending.title"));
            var pendingHeaderRt = pendingHeader.GetComponent<RectTransform>();
            pendingHeaderRt.anchorMin = new Vector2(0f, 1f);
            pendingHeaderRt.anchorMax = new Vector2(1f, 1f);
            pendingHeaderRt.pivot = new Vector2(0.5f, 1f);
            pendingHeaderRt.offsetMin = new Vector2(10f, -42f);
            pendingHeaderRt.offsetMax = new Vector2(-10f, -8f);

            _pendingText = AddText(pendingCol.transform, "Txt",
                string.Empty, 13, TextAlignmentOptions.TopLeft, UIThemeTextRole.Primary);
            _pendingText.richText = true;
            _pendingText.textWrappingMode = TextWrappingModes.Normal;
            _pendingText.overflowMode = TextOverflowModes.Overflow;
            _pendingText.rectTransform.offsetMin = new Vector2(14f, 12f);
            _pendingText.rectTransform.offsetMax = new Vector2(-14f, -48f);

            var histCol = new GameObject("History", typeof(RectTransform));
            histCol.transform.SetParent(rightCol.transform, false);
            var hrt2 = histCol.GetComponent<RectTransform>();
            hrt2.anchorMin = new Vector2(0f, 0f);
            hrt2.anchorMax = new Vector2(1f, 0.5f);
            hrt2.offsetMin = new Vector2(8f, 4f);
            hrt2.offsetMax = new Vector2(-8f, -4f);
            UITheme.ApplySurface(histCol.AddComponent<Image>(), SectionBodyBg, UIShapePreset.Inset);

            var historyHeader = CreateSectionHeader(histCol.transform, "HistoryHeader",
                LocalizationService.Get("maintenance.parts.history.title"));
            var historyHeaderRt = historyHeader.GetComponent<RectTransform>();
            historyHeaderRt.anchorMin = new Vector2(0f, 1f);
            historyHeaderRt.anchorMax = new Vector2(1f, 1f);
            historyHeaderRt.pivot = new Vector2(0.5f, 1f);
            historyHeaderRt.offsetMin = new Vector2(10f, -42f);
            historyHeaderRt.offsetMax = new Vector2(-10f, -8f);

            _historyText = AddText(histCol.transform, "Txt",
                string.Empty, 12, TextAlignmentOptions.TopLeft, UIThemeTextRole.Primary);
            _historyText.richText = true;
            _historyText.textWrappingMode = TextWrappingModes.Normal;
            _historyText.overflowMode = TextOverflowModes.Overflow;
            _historyText.rectTransform.offsetMin = new Vector2(14f, 12f);
            _historyText.rectTransform.offsetMax = new Vector2(-14f, -48f);
        }

        static ScrollRect CreateScrollContent(Transform parent, out RectTransform content)
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(parent, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(5f, 5f);
            scrollRt.offsetMax = new Vector2(-5f, -5f);

            var maskImg = scrollGo.AddComponent<Image>();
            UITheme.ApplySurface(maskImg, ScrollBg, UIShapePreset.Inset);
            var mask = scrollGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var scroll = scrollGo.AddComponent<ScrollRect>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(scrollGo.transform, false);
            content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 0f);

            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;

            return scroll;
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

        static GameObject CreateSectionHeader(Transform parent, string name, string label)
        {
            var header = new GameObject(name, typeof(RectTransform));
            header.transform.SetParent(parent, false);
            UITheme.ApplySurface(header.AddComponent<Image>(), SectionHeaderBg, UIShapePreset.Inset);

            var text = AddText(header.transform, "Label", label, 14, TextAlignmentOptions.MidlineLeft, UIThemeTextRole.Accent);
            text.rectTransform.offsetMin = new Vector2(12f, 0f);
            text.rectTransform.offsetMax = new Vector2(-12f, 0f);
            return header;
        }

        static TextMeshProUGUI AddText(Transform parent, string name, string text, int fontSize,
                            TextAlignmentOptions alignment, UIThemeTextRole role)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var txt = go.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(txt, role);
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.richText = true;
            txt.raycastTarget = false;
            txt.text = text;
            return txt;
        }

        static Button CreateButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size,
                                    Vector2 anchorMin, Vector2 anchorMax)
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
            var btn = go.AddComponent<Button>();
            UITheme.ApplyButtonStyle(btn, img, UIButtonTone.Secondary, UIShapePreset.Button);

            var txt = AddText(go.transform, "Label", label, 13, TextAlignmentOptions.Center, UIThemeTextRole.Primary);
            txt.raycastTarget = false;
            return btn;
        }

        static string ToHtmlColor(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGB(color)}";
        }
    }
}
