using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Maintenance
{
    public partial class WorkshopsPanelUI
    {
        private static readonly Color PanelBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
        private static readonly Color HeaderBg = UITheme.WithAlpha(UITheme.TopBarBackground, 1f);
        private static readonly Color ColumnBg = UITheme.WithAlpha(UITheme.PrimarySurface, 0.9f);
        private static readonly Color ScrollBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.16f);
        private static readonly Color SectionHeaderBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f);
        private static readonly Color SectionBodyBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.22f);
        private static readonly Color DialogBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
        private static readonly Color DimmerBg = UITheme.WithAlpha(Color.black, 0.72f);

        // ═══════════════════════════════════════════
        //  UI BUILDER — main canvas + 2 columns + external picker modal + helpers
        // ═══════════════════════════════════════════

        void BuildUI()
        {
            var canvasGo = new GameObject("WorkshopsPanelCanvas");
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
            bg.color = DimmerBg;

            var shell = new GameObject("Shell", typeof(RectTransform));
            shell.transform.SetParent(_root.transform, false);
            var shellRt = shell.GetComponent<RectTransform>();
            shellRt.anchorMin = Vector2.zero;
            shellRt.anchorMax = Vector2.one;
            shellRt.offsetMin = new Vector2(24f, 24f);
            shellRt.offsetMax = new Vector2(-24f, -24f);
            var shellBg = shell.AddComponent<Image>();
            UITheme.ApplySurface(shellBg, PanelBg, UIShapePreset.PanelLarge);

            // Header
            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(shell.transform, false);
            var hRt = header.GetComponent<RectTransform>();
            hRt.anchorMin = new Vector2(0f, 1f);
            hRt.anchorMax = new Vector2(1f, 1f);
            hRt.pivot = new Vector2(0.5f, 1f);
            hRt.offsetMin = new Vector2(18f, -74f);
            hRt.offsetMax = new Vector2(-18f, -18f);
            var hBg = header.AddComponent<Image>();
            UITheme.ApplySurface(hBg, HeaderBg, UIShapePreset.Panel);

            var title = AddText(header.transform, "Title",
                LocalizationService.Get("maintenance.workshops.title"),
                22, TextAlignmentOptions.MidlineLeft, UIThemeTextRole.Primary);
            title.rectTransform.offsetMin = new Vector2(18f, 0f);
            title.rectTransform.offsetMax = new Vector2(-180f, 0f);

            // Close button
            var closeBtn = CreateButton(header.transform, LocalizationService.Get("maintenance.workshops.close_btn"),
                new Vector2(-12f, -8f), new Vector2(120f, 38f),
                new Vector2(1f, 1f), new Vector2(1f, 1f));
            closeBtn.onClick.AddListener(Hide);

            // Center box — 2 kolumny: sloty | overdue
            var center = new GameObject("Center", typeof(RectTransform));
            center.transform.SetParent(shell.transform, false);
            var cRt = center.GetComponent<RectTransform>();
            cRt.anchorMin = Vector2.zero;
            cRt.anchorMax = Vector2.one;
            cRt.offsetMin = new Vector2(18f, 18f);
            cRt.offsetMax = new Vector2(-18f, -84f);

            // Lewa kolumna (sloty) 0..0.48
            var leftCol = CreateColumn(center.transform, "Slots", 0f, 0.48f);
            var slotsHeader = CreateSectionHeader(leftCol.transform, "SlotsHeader",
                LocalizationService.Get("maintenance.workshops.slots.header"));
            var slotsHeaderRt = slotsHeader.GetComponent<RectTransform>();
            slotsHeaderRt.anchorMin = new Vector2(0f, 1f);
            slotsHeaderRt.anchorMax = new Vector2(1f, 1f);
            slotsHeaderRt.pivot = new Vector2(0.5f, 1f);
            slotsHeaderRt.offsetMin = new Vector2(12f, -52f);
            slotsHeaderRt.offsetMax = new Vector2(-12f, -12f);

            _slotsScroll = CreateScrollContent(leftCol.transform, out var slotsContent);
            var slotsScrollRt = _slotsScroll.GetComponent<RectTransform>();
            slotsScrollRt.offsetMin = new Vector2(12f, 12f);
            slotsScrollRt.offsetMax = new Vector2(-12f, -60f);
            _slotsText = AddText(slotsContent.transform, "SlotsText",
                "", 13, TextAlignmentOptions.TopLeft, UIThemeTextRole.Primary);
            _slotsText.richText = true;
            _slotsText.textWrappingMode = TextWrappingModes.Normal;
            _slotsText.overflowMode = TextOverflowModes.Overflow;
            var slotsRt = _slotsText.rectTransform;
            slotsRt.anchorMin = new Vector2(0f, 1f);
            slotsRt.anchorMax = new Vector2(1f, 1f);
            slotsRt.pivot = new Vector2(0.5f, 1f);
            slotsRt.sizeDelta = new Vector2(0f, 2000f); // wysokie dla ContentSizeFitter
            var slotsFitter = slotsContent.gameObject.AddComponent<ContentSizeFitter>();
            slotsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Prawa kolumna (overdue) 0.52..1
            var rightCol = CreateColumn(center.transform, "Overdue", 0.52f, 1f);
            var overdueHeader = CreateSectionHeader(rightCol.transform, "OverdueHeader",
                LocalizationService.Get("maintenance.workshops.overdue.header").Trim());
            var overdueHeaderRt = overdueHeader.GetComponent<RectTransform>();
            overdueHeaderRt.anchorMin = new Vector2(0f, 1f);
            overdueHeaderRt.anchorMax = new Vector2(1f, 1f);
            overdueHeaderRt.pivot = new Vector2(0.5f, 1f);
            overdueHeaderRt.offsetMin = new Vector2(12f, -52f);
            overdueHeaderRt.offsetMax = new Vector2(-12f, -12f);

            _overdueScroll = CreateScrollContent(rightCol.transform, out var overdueContent);
            var overdueScrollRt = _overdueScroll.GetComponent<RectTransform>();
            overdueScrollRt.offsetMin = new Vector2(12f, 12f);
            overdueScrollRt.offsetMax = new Vector2(-12f, -60f);
            _overdueContent = overdueContent;

            // Header text (zachowany przez nazwę "Header" w czyszczeniu rzędów)
            var headerRow = new GameObject("Header", typeof(RectTransform));
            headerRow.transform.SetParent(_overdueContent, false);
            var headerRowRt = headerRow.GetComponent<RectTransform>();
            headerRowRt.anchorMin = new Vector2(0f, 1f);
            headerRowRt.anchorMax = new Vector2(1f, 1f);
            headerRowRt.pivot = new Vector2(0.5f, 1f);
            headerRowRt.sizeDelta = new Vector2(0f, 52f);
            var headerRowBg = headerRow.AddComponent<Image>();
            UITheme.ApplySurface(headerRowBg, SectionBodyBg, UIShapePreset.Inset);
            _overdueText = AddText(headerRow.transform, "OverdueText",
                "", 13, TextAlignmentOptions.TopLeft, UIThemeTextRole.Primary);
            _overdueText.richText = true;
            _overdueText.rectTransform.offsetMin = new Vector2(12f, 8f);
            _overdueText.rectTransform.offsetMax = new Vector2(-12f, -8f);

            // VerticalLayoutGroup — rzędy overdue układają się pod sobą
            var vlg = _overdueContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UITheme.Spacing.Sm;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Md);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = false;

            var overdueFitter = _overdueContent.gameObject.AddComponent<ContentSizeFitter>();
            overdueFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildExternalPicker();
        }

        /// <summary>M7-5: modalna nakładka do wyboru zewnętrznego zakładu.</summary>
        void BuildExternalPicker()
        {
            _externalPickerPanel = new GameObject("ExternalPickerPanel", typeof(RectTransform));
            _externalPickerPanel.transform.SetParent(_root.transform, false);
            var prt = _externalPickerPanel.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;

            // Dimmer
            var dim = _externalPickerPanel.AddComponent<Image>();
            dim.color = DimmerBg;

            // Dialog box
            var dialog = new GameObject("Dialog", typeof(RectTransform));
            dialog.transform.SetParent(_externalPickerPanel.transform, false);
            var drt = dialog.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0.5f, 0.5f);
            drt.anchorMax = new Vector2(0.5f, 0.5f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.sizeDelta = new Vector2(760f, 640f);
            var dbg = dialog.AddComponent<Image>();
            UITheme.ApplySurface(dbg, DialogBg, UIShapePreset.PanelLarge);

            // Title
            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(dialog.transform, false);
            var headerRt = header.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.offsetMin = new Vector2(14f, -66f);
            headerRt.offsetMax = new Vector2(-14f, -14f);
            var headerBg = header.AddComponent<Image>();
            UITheme.ApplySurface(headerBg, HeaderBg, UIShapePreset.Panel);
            _externalPickerTitle = AddText(header.transform, "Title",
                "", 16, TextAlignmentOptions.MidlineLeft, UIThemeTextRole.Accent);
            _externalPickerTitle.rectTransform.offsetMin = new Vector2(20f, 0f);
            _externalPickerTitle.rectTransform.offsetMax = new Vector2(-72f, 0f);

            // Close button
            var closeBtn = CreateButton(dialog.transform, "✕",
                new Vector2(-10f, -5f), new Vector2(40f, 40f),
                new Vector2(1f, 1f), new Vector2(1f, 1f));
            closeBtn.transform.SetParent(header.transform, false);
            var closeRt = closeBtn.GetComponent<RectTransform>();
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.sizeDelta = new Vector2(42f, 36f);
            closeRt.anchoredPosition = new Vector2(-10f, -6f);
            var closeLabel = closeBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (closeLabel != null) closeLabel.text = "X";
            closeBtn.onClick.AddListener(HideExternalPicker);

            // Scroll content for rows
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(dialog.transform, false);
            var scrt = scrollGo.GetComponent<RectTransform>();
            scrt.anchorMin = new Vector2(0f, 0f);
            scrt.anchorMax = new Vector2(1f, 1f);
            scrt.offsetMin = new Vector2(14f, 14f);
            scrt.offsetMax = new Vector2(-14f, -74f);

            var maskImg = scrollGo.AddComponent<Image>();
            UITheme.ApplySurface(maskImg, ScrollBg, UIShapePreset.Inset);
            var mask = scrollGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            var scroll = scrollGo.AddComponent<ScrollRect>();

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(scrollGo.transform, false);
            _externalPickerContent = content.GetComponent<RectTransform>();
            _externalPickerContent.anchorMin = new Vector2(0f, 1f);
            _externalPickerContent.anchorMax = new Vector2(1f, 1f);
            _externalPickerContent.pivot = new Vector2(0.5f, 1f);

            var contentVlg = content.AddComponent<VerticalLayoutGroup>();
            contentVlg.spacing = UITheme.Spacing.Sm;
            contentVlg.padding = UITheme.Padding(UITheme.Spacing.Md);
            contentVlg.childForceExpandWidth = true;
            contentVlg.childForceExpandHeight = false;
            contentVlg.childControlHeight = false;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = _externalPickerContent;
            scroll.horizontal = false;
            scroll.vertical = true;

            _externalPickerPanel.SetActive(false);
        }

        // ── UI helpers ────────────────────────────────────

        static ScrollRect CreateScrollContent(Transform parent, out RectTransform content)
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(parent, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(6f, 6f);
            scrollRt.offsetMax = new Vector2(-6f, -6f);

            // Mask
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

            var txt = AddText(go.transform, "Label", label, 14, TextAlignmentOptions.Center, UIThemeTextRole.Primary);
            txt.raycastTarget = false;
            return btn;
        }
    }
}
