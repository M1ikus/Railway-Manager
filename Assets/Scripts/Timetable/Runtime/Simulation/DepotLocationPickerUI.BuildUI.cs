using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;

namespace RailwayManager.Timetable.Simulation
{
    public partial class DepotLocationPickerUI
    {
        private static readonly Color PickerRootBg = UITheme.AppBackground;
        private static readonly Color PickerTopBarBg = UITheme.WithAlpha(UITheme.TopBarBackground, 1f);
        private static readonly Color PickerPanelBg = UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f);
        private static readonly Color PickerSectionBg = UITheme.WithAlpha(UITheme.PrimarySurface, 0.88f);
        private static readonly Color PickerInsetBg = UITheme.WithAlpha(UITheme.SecondarySurface, 0.16f);
        private static readonly Color PickerBannerBg = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.9f);

        // ═══════════════════════════════════════════
        //  UI BUILDER — overlay panel + map picking banner
        // ═══════════════════════════════════════════

        void BuildUI()
        {
            var canvasGo = new GameObject("DepotLocationPickerCanvas");
            canvasGo.transform.SetParent(transform);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 500;
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
            // Pełnoekranowe nieprzezroczyste tło (całkowicie zasłania depot pod spodem)
            var rootBg = _root.AddComponent<Image>();
            rootBg.color = PickerRootBg;

            // Top bar — tytuł
            var topBar = new GameObject("TopBar", typeof(RectTransform));
            topBar.transform.SetParent(_root.transform, false);
            var tbRt = topBar.GetComponent<RectTransform>();
            tbRt.anchorMin = new Vector2(0f, 1f);
            tbRt.anchorMax = new Vector2(1f, 1f);
            tbRt.pivot = new Vector2(0.5f, 1f);
            tbRt.offsetMin = new Vector2(24f, -108f);
            tbRt.offsetMax = new Vector2(-24f, -24f);
            var tbBg = topBar.AddComponent<Image>();
            UITheme.ApplySurface(tbBg, PickerTopBarBg, UIShapePreset.Panel);
            AddText(topBar.transform, "Title",
                "<b>Wybierz lokalizację zajezdni</b>\n" +
                "<size=16>Wpisz nazwę stacji LUB wybierz z mapy — decyzja permanentna</size>",
                22, TextAlignmentOptions.Center, UIThemeTextRole.Primary);

            // Centered content box
            _centerBox = new GameObject("CenterBox", typeof(RectTransform));
            _centerBox.transform.SetParent(_root.transform, false);
            var cbRt = _centerBox.GetComponent<RectTransform>();
            cbRt.anchorMin = new Vector2(0.5f, 0.5f);
            cbRt.anchorMax = new Vector2(0.5f, 0.5f);
            cbRt.pivot = new Vector2(0.5f, 0.5f);
            cbRt.sizeDelta = new Vector2(760f, 670f);
            cbRt.anchoredPosition = new Vector2(0f, -12f);
            var cbBg = _centerBox.AddComponent<Image>();
            UITheme.ApplySurface(cbBg, PickerPanelBg, UIShapePreset.PanelLarge);

            // Header "Wyszukaj stację"
            var header = AddText(_centerBox.transform, "Header",
                "<b>Wyszukaj stację</b>", 18, TextAlignmentOptions.TopLeft, UIThemeTextRole.Accent);
            var hRt = header.rectTransform;
            hRt.anchorMin = new Vector2(0f, 1f);
            hRt.anchorMax = new Vector2(1f, 1f);
            hRt.pivot = new Vector2(0.5f, 1f);
            hRt.sizeDelta = new Vector2(-40f, 28f);
            hRt.anchoredPosition = new Vector2(0f, -20f);

            // Search row: [InputField] [Wybierz z mapy]
            var searchRow = new GameObject("SearchRow", typeof(RectTransform));
            searchRow.transform.SetParent(_centerBox.transform, false);
            var srRt = searchRow.GetComponent<RectTransform>();
            srRt.anchorMin = new Vector2(0f, 1f);
            srRt.anchorMax = new Vector2(1f, 1f);
            srRt.pivot = new Vector2(0.5f, 1f);
            srRt.sizeDelta = new Vector2(-40f, 48f);
            srRt.anchoredPosition = new Vector2(0f, -56f);

            // InputField (lewa część, 440px)
            var inputGo = new GameObject("Input", typeof(RectTransform));
            inputGo.transform.SetParent(searchRow.transform, false);
            var inRt = inputGo.GetComponent<RectTransform>();
            inRt.anchorMin = new Vector2(0f, 0f);
            inRt.anchorMax = new Vector2(0f, 1f);
            inRt.pivot = new Vector2(0f, 0.5f);
            inRt.sizeDelta = new Vector2(440f, 0f);
            inRt.anchoredPosition = new Vector2(0f, 0f);
            var inBg = inputGo.AddComponent<Image>();

            // TMP_InputField requires Viewport (RectMask2D) + Text/Placeholder as children of viewport.
            var viewportInputGo = new GameObject("Viewport", typeof(RectTransform));
            viewportInputGo.transform.SetParent(inputGo.transform, false);
            var vipRt = viewportInputGo.GetComponent<RectTransform>();
            vipRt.anchorMin = Vector2.zero;
            vipRt.anchorMax = Vector2.one;
            vipRt.offsetMin = new Vector2(12f, 4f);
            vipRt.offsetMax = new Vector2(-12f, -4f);
            viewportInputGo.AddComponent<RectMask2D>();

            _searchInput = inputGo.AddComponent<TMP_InputField>();

            var inputTextGo = new GameObject("Text", typeof(RectTransform));
            inputTextGo.transform.SetParent(viewportInputGo.transform, false);
            var itRt = inputTextGo.GetComponent<RectTransform>();
            itRt.anchorMin = Vector2.zero;
            itRt.anchorMax = Vector2.one;
            itRt.offsetMin = Vector2.zero;
            itRt.offsetMax = Vector2.zero;
            var inputText = inputTextGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(inputText, UIThemeTextRole.Primary);
            inputText.fontSize = 16;
            inputText.alignment = TextAlignmentOptions.MidlineLeft;
            inputText.textWrappingMode = TextWrappingModes.NoWrap;
            inputText.raycastTarget = false;

            var phGo = new GameObject("Placeholder", typeof(RectTransform));
            phGo.transform.SetParent(viewportInputGo.transform, false);
            var phRt = phGo.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;
            var phText = phGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(phText, UIThemeTextRole.Secondary);
            phText.fontSize = 16;
            phText.fontStyle = FontStyles.Italic;
            phText.alignment = TextAlignmentOptions.MidlineLeft;
            phText.textWrappingMode = TextWrappingModes.NoWrap;
            phText.raycastTarget = false;
            phText.text = "Wpisz nazwę stacji…";

            _searchInput.textViewport = vipRt;
            _searchInput.textComponent = inputText;
            _searchInput.placeholder = phText;
            _searchInput.lineType = TMP_InputField.LineType.SingleLine;
            _searchInput.targetGraphic = inBg;
            UITheme.ApplyTmpInputField(_searchInput, inBg, inputText, phText);
            _searchInput.onValueChanged.AddListener(OnSearchChanged);

            // Button "Wybierz z mapy" (prawa część)
            var btnGo = new GameObject("PickFromMap", typeof(RectTransform));
            btnGo.transform.SetParent(searchRow.transform, false);
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0f, 0f);
            btnRt.anchorMax = new Vector2(1f, 1f);
            btnRt.pivot = new Vector2(0f, 0.5f);
            btnRt.offsetMin = new Vector2(452f, 0f); // 440 + 12 spacing
            btnRt.offsetMax = new Vector2(0f, 0f);
            var btnBg = btnGo.AddComponent<Image>();
            _pickFromMapButton = btnGo.AddComponent<Button>();
            UITheme.ApplyButtonStyle(_pickFromMapButton, btnBg, UIButtonTone.Secondary, UIShapePreset.Button);
            _pickFromMapButton.onClick.AddListener(OnPickFromMapClicked);

            var btnLblGo = new GameObject("Label", typeof(RectTransform));
            btnLblGo.transform.SetParent(btnGo.transform, false);
            var blRt = btnLblGo.GetComponent<RectTransform>();
            blRt.anchorMin = Vector2.zero; blRt.anchorMax = Vector2.one;
            blRt.offsetMin = Vector2.zero; blRt.offsetMax = Vector2.zero;
            _pickFromMapLabel = btnLblGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(_pickFromMapLabel, UIThemeTextRole.Primary);
            _pickFromMapLabel.fontSize = 14;
            _pickFromMapLabel.alignment = TextAlignmentOptions.Center;
            _pickFromMapLabel.text = "Wybierz z mapy";
            _pickFromMapLabel.raycastTarget = false;

            var resultsHeader = AddText(_centerBox.transform, "ResultsHeader",
                "<b>Pasujace stacje</b>", 16, TextAlignmentOptions.TopLeft, UIThemeTextRole.Accent);
            var rhRt = resultsHeader.rectTransform;
            rhRt.anchorMin = new Vector2(0f, 1f);
            rhRt.anchorMax = new Vector2(1f, 1f);
            rhRt.pivot = new Vector2(0.5f, 1f);
            rhRt.sizeDelta = new Vector2(-40f, 24f);
            rhRt.anchoredPosition = new Vector2(0f, -118f);

            // Results panel (scroll) pod search row
            _resultsPanel = new GameObject("ResultsPanel", typeof(RectTransform));
            _resultsPanel.transform.SetParent(_centerBox.transform, false);
            var rpRt = _resultsPanel.GetComponent<RectTransform>();
            rpRt.anchorMin = new Vector2(0f, 1f);
            rpRt.anchorMax = new Vector2(1f, 1f);
            rpRt.pivot = new Vector2(0.5f, 1f);
            rpRt.sizeDelta = new Vector2(-40f, 232f);
            rpRt.anchoredPosition = new Vector2(0f, -146f);
            var rpBg = _resultsPanel.AddComponent<Image>();
            UITheme.ApplySurface(rpBg, PickerSectionBg, UIShapePreset.Panel);

            var scroll = _resultsPanel.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(_resultsPanel.transform, false);
            var vpRt = viewport.GetComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero; vpRt.offsetMax = Vector2.zero;
            var vpImg = viewport.AddComponent<Image>();
            UITheme.ApplySurface(vpImg, PickerInsetBg, UIShapePreset.Inset);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            _resultsContent = content.GetComponent<RectTransform>();
            _resultsContent.anchorMin = new Vector2(0f, 1f);
            _resultsContent.anchorMax = new Vector2(1f, 1f);
            _resultsContent.pivot = new Vector2(0.5f, 1f);
            // 2026-05-17: CRITICAL FIX — bez explicit sizeDelta, Unity default (100, 100)
            // z anchor stretch X [0..1] dawał content.width = viewport.width + 100, plus
            // pivot (0.5, 1) → content przesunięty 50px na lewo poza viewport. Mask clipował
            // → items "wycięte od lewej" w screen (root cause overlap raportowanego 2026-05-17).
            _resultsContent.sizeDelta = new Vector2(0f, 0f);
            _resultsContent.anchoredPosition = Vector2.zero;
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Xxs);
            vlg.spacing = UITheme.Spacing.Xxs;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = vpRt;
            scroll.content = _resultsContent;
            _resultsPanel.SetActive(false);

            // 2026-05-17: selectedHeader "Wybrana lokalizacja" USUNIĘTY — był 2× problem:
            //   v1 (Y=260): inside selectedBg [76, 288] → przykryty backgroundem
            //   v2 (Y=296): inside resultsPanel [292, 524] → nakładał się z list rows
            // Brak miejsca w layout (gap tylko 4px między selectedBg.top=288 a resultsPanel.bottom=292).
            // Header redundant — selectedInfoBox już ma duży "Elbląg" bold + "Stacja bazowa gracza"
            // accent jako wyraźny title. Cleaner bez header'a.

            // Selected info section
            var selectedBg = new GameObject("SelectedBg", typeof(RectTransform));
            selectedBg.transform.SetParent(_centerBox.transform, false);
            var sbRt = selectedBg.GetComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(0f, 0f);
            sbRt.anchorMax = new Vector2(1f, 0f);
            sbRt.pivot = new Vector2(0.5f, 0f);
            sbRt.sizeDelta = new Vector2(-40f, 212f);
            sbRt.anchoredPosition = new Vector2(0f, 76f);
            var sbBgImg = selectedBg.AddComponent<Image>();
            UITheme.ApplySurface(sbBgImg, PickerSectionBg, UIShapePreset.Panel);

            var infoGo = new GameObject("Info", typeof(RectTransform));
            infoGo.transform.SetParent(selectedBg.transform, false);
            var infoRt = infoGo.GetComponent<RectTransform>();
            infoRt.anchorMin = Vector2.zero; infoRt.anchorMax = Vector2.one;
            infoRt.offsetMin = new Vector2(16f, 12f); infoRt.offsetMax = new Vector2(-16f, -12f);
            _selectedInfoText = infoGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(_selectedInfoText, UIThemeTextRole.Primary);
            _selectedInfoText.fontSize = 14;
            _selectedInfoText.alignment = TextAlignmentOptions.TopLeft;
            _selectedInfoText.richText = true;
            _selectedInfoText.raycastTarget = false;
            _selectedInfoText.textWrappingMode = TextWrappingModes.Normal;
            _selectedInfoText.overflowMode = TextOverflowModes.Overflow;

            // Confirm button (dół centerbox)
            var confirmGo = new GameObject("Confirm", typeof(RectTransform));
            confirmGo.transform.SetParent(_centerBox.transform, false);
            var cRt = confirmGo.GetComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0f, 0f);
            cRt.anchorMax = new Vector2(1f, 0f);
            cRt.pivot = new Vector2(0.5f, 0f);
            cRt.sizeDelta = new Vector2(-40f, 52f);
            cRt.anchoredPosition = new Vector2(0f, 20f);
            var cBg = confirmGo.AddComponent<Image>();
            _confirmButton = confirmGo.AddComponent<Button>();
            UITheme.ApplyButtonStyle(_confirmButton, cBg, UIButtonTone.Primary, UIShapePreset.Pill);
            _confirmButton.onClick.AddListener(OnConfirmClicked);

            var cLblGo = new GameObject("Label", typeof(RectTransform));
            cLblGo.transform.SetParent(confirmGo.transform, false);
            var clRt = cLblGo.GetComponent<RectTransform>();
            clRt.anchorMin = Vector2.zero; clRt.anchorMax = Vector2.one;
            clRt.offsetMin = Vector2.zero; clRt.offsetMax = Vector2.zero;
            _confirmButtonLabel = cLblGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(_confirmButtonLabel, UIThemeTextRole.Inverse);
            _confirmButtonLabel.fontSize = 16;
            _confirmButtonLabel.alignment = TextAlignmentOptions.Center;
            _confirmButtonLabel.text = "Potwierdź wybór";
            _confirmButtonLabel.raycastTarget = false;
        }

        void BuildMapPickingBanner()
        {
            // Banner na górze ekranu gdy user jest na Map scene w trybie pickingu.
            // Żyje w tym samym Canvas co overlay — Canvas pozostaje zawsze enabled,
            // ale Root jest disabled w tym stanie, więc banner jako osobny child.
            _mapPickingBanner = new GameObject("MapPickingBanner", typeof(RectTransform));
            _mapPickingBanner.transform.SetParent(_canvas.transform, false);
            var rt = _mapPickingBanner.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(820f, 68f);
            rt.anchoredPosition = new Vector2(0f, -18f);
            var bg = _mapPickingBanner.AddComponent<Image>();
            UITheme.ApplySurface(bg, PickerBannerBg, UIShapePreset.PanelLarge);
            bg.raycastTarget = false;

            var txtGo = new GameObject("Text", typeof(RectTransform));
            txtGo.transform.SetParent(_mapPickingBanner.transform, false);
            var tRt = txtGo.GetComponent<RectTransform>();
            tRt.anchorMin = Vector2.zero; tRt.anchorMax = Vector2.one;
            tRt.offsetMin = new Vector2(24f, 6f); tRt.offsetMax = new Vector2(-24f, -6f);
            var txt = txtGo.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(txt, UIThemeTextRole.Inverse);
            txt.fontSize = 16;
            txt.alignment = TextAlignmentOptions.Center;
            txt.richText = true;
            txt.raycastTarget = false;
            txt.text = "<b>Kliknij stację na mapie aby wybrać lokalizację zajezdni</b>   <size=12>(ESC — anuluj)</size>";
        }

        static TextMeshProUGUI AddText(Transform parent, string name, string text, int fontSize, TextAlignmentOptions alignment, UIThemeTextRole role)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(20f, 5f);
            rt.offsetMax = new Vector2(-20f, -5f);
            var txt = go.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(txt, role);
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.richText = true;
            txt.raycastTarget = false;
            txt.text = text;
            return txt;
        }
    }
}
