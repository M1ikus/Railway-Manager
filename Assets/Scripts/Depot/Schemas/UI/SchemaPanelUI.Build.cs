using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;

namespace DepotSystem.Schemas.UI
{
    /// <summary>
    /// Partial: procedural UI construction (Canvas + Header + List panel + Params panel +
    /// AdvancedSection placeholder). Wywoływane raz w Awake przez BuildUI.
    /// </summary>
    public partial class SchemaPanelUI
    {
        // ════════════════════════════════════════
        //  UI CONSTRUCTION
        // ════════════════════════════════════════

        private void BuildUI()
        {
            // Find or create canvas
            // Dedicated Canvas dla SchemaPanelUI — żeby istniejący scenic Canvas (z depot
            // toolbar) z innym CanvasScaler nie wpłynął na rozmiar panelu. Wcześniejszy
            // DepotServices.Get<Canvas> wracał scenic Canvas, który mógł być w
            // ConstantPixelSize lub inny mode → panel skalował się niepoprawnie.
            var canvasGO = new GameObject("SchemaPanelCanvas");
            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;  // wyżej niż depot toolbar UI (~0-10), niżej niż save dialog (100)
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);
            canvasGO.AddComponent<GraphicRaycaster>();

            // Root panel
            _root = new GameObject("SchemaPanelUI_Root");
            _root.transform.SetParent(_canvas.transform, false);
            var rootRT = _root.AddComponent<RectTransform>();
            rootRT.sizeDelta = panelSize;
            rootRT.anchorMin = new Vector2(0.5f, 0.5f);
            rootRT.anchorMax = new Vector2(0.5f, 0.5f);
            rootRT.pivot = new Vector2(0.5f, 0.5f);
            rootRT.anchoredPosition = Vector2.zero;

            var rootBg = _root.AddComponent<Image>();
            UITheme.ApplySurface(rootBg, UITheme.OverlayPanelStrong, UIShapePreset.PanelLarge);

            // Header bar (na górze)
            BuildHeaderBar();

            // Lewa strona: lista
            BuildListPanel();

            // Prawa strona: parameters
            BuildParametersPanel();
        }

        private void BuildHeaderBar()
        {
            var headerGO = new GameObject("Header");
            headerGO.transform.SetParent(_root.transform, false);
            var rt = headerGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0, 64);
            rt.anchoredPosition = new Vector2(0, 0);

            var headerBg = headerGO.AddComponent<Image>();
            UITheme.ApplySurface(headerBg, UITheme.TopBarInset, UIShapePreset.Panel);

            var eyebrowGO = new GameObject("Eyebrow");
            eyebrowGO.transform.SetParent(headerGO.transform, false);
            var eyebrowRT = eyebrowGO.AddComponent<RectTransform>();
            eyebrowRT.anchorMin = new Vector2(0, 1);
            eyebrowRT.anchorMax = new Vector2(1, 1);
            eyebrowRT.offsetMin = new Vector2(20, -18);
            eyebrowRT.offsetMax = new Vector2(-70, -2);
            var eyebrow = eyebrowGO.AddComponent<TextMeshProUGUI>();
            eyebrow.text = "BIBLIOTEKA SCHEMATOW";
            eyebrow.fontSize = 9;
            eyebrow.fontStyle = FontStyles.Bold;
            eyebrow.alignment = TextAlignmentOptions.TopLeft;
            UITheme.ApplyTmpText(eyebrow, UIThemeTextRole.Accent);

            // Title
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(headerGO.transform, false);
            var titleRT = titleGO.AddComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0, 0);
            titleRT.anchorMax = new Vector2(1, 1);
            titleRT.offsetMin = new Vector2(20, 14);
            titleRT.offsetMax = new Vector2(-70, -10);

            var title = titleGO.AddComponent<TextMeshProUGUI>();
            title.text = "Schematy głowic rozjazdowych";
            title.fontSize = 20;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.fontStyle = FontStyles.Bold;
            UITheme.ApplyTmpText(title, UIThemeTextRole.Primary);

            var subtitleGO = new GameObject("Subtitle");
            subtitleGO.transform.SetParent(headerGO.transform, false);
            var subtitleRT = subtitleGO.AddComponent<RectTransform>();
            subtitleRT.anchorMin = new Vector2(0, 0);
            subtitleRT.anchorMax = new Vector2(1, 1);
            subtitleRT.offsetMin = new Vector2(20, 4);
            subtitleRT.offsetMax = new Vector2(-70, -34);
            var subtitle = subtitleGO.AddComponent<TextMeshProUGUI>();
            subtitle.text = "Wybierz preset, dopracuj parametry i od razu przejdz do stawiania.";
            subtitle.fontSize = 11;
            subtitle.alignment = TextAlignmentOptions.BottomLeft;
            subtitle.fontStyle = FontStyles.Italic;
            UITheme.ApplyTmpText(subtitle, UIThemeTextRole.Secondary);

            // Close button (X)
            var closeGO = new GameObject("CloseBtn");
            closeGO.transform.SetParent(headerGO.transform, false);
            var closeRT = closeGO.AddComponent<RectTransform>();
            closeRT.anchorMin = new Vector2(1, 0.5f);
            closeRT.anchorMax = new Vector2(1, 0.5f);
            closeRT.pivot = new Vector2(1, 0.5f);
            closeRT.sizeDelta = new Vector2(40, 40);
            closeRT.anchoredPosition = new Vector2(-10, 0);

            var closeImg = closeGO.AddComponent<Image>();
            UITheme.ApplySurface(closeImg, UITheme.SecondarySurface, UIShapePreset.Button);

            _closeButton = closeGO.AddComponent<Button>();
            _closeButton.onClick.AddListener(Hide);

            var closeLabelGO = new GameObject("Label");
            closeLabelGO.transform.SetParent(closeGO.transform, false);
            var closeLabelRT = closeLabelGO.AddComponent<RectTransform>();
            closeLabelRT.anchorMin = Vector2.zero;
            closeLabelRT.anchorMax = Vector2.one;
            closeLabelRT.offsetMin = closeLabelRT.offsetMax = Vector2.zero;
            var closeLabel = closeLabelGO.AddComponent<TextMeshProUGUI>();
            closeLabel.text = "X";
            closeLabel.fontSize = 18;
            closeLabel.alignment = TextAlignmentOptions.Center;
            closeLabel.fontStyle = FontStyles.Bold;
            UITheme.ApplyTmpText(closeLabel, UIThemeTextRole.Primary);
        }

        private void BuildListPanel()
        {
            var listGO = new GameObject("ListPanel");
            listGO.transform.SetParent(_root.transform, false);
            var rt = listGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(listWidthRatio, 1);
            rt.offsetMin = new Vector2(10, 60);
            rt.offsetMax = new Vector2(-5, -69);

            var listBg = listGO.AddComponent<Image>();
            UITheme.ApplySurface(listBg, UITheme.OverlayPanel, UIShapePreset.Panel);

            // MD-10: search + filter + create snapshot button (top bar nad listą)
            BuildListTopBar(listGO.transform);

            // ScrollView
            var scrollGO = new GameObject("ScrollView");
            scrollGO.transform.SetParent(listGO.transform, false);
            var scrollRT = scrollGO.AddComponent<RectTransform>();
            scrollRT.anchorMin = Vector2.zero;
            scrollRT.anchorMax = Vector2.one;
            scrollRT.offsetMin = new Vector2(5, 5);
            scrollRT.offsetMax = new Vector2(-5, -110);  // -110 zostawia miejsce na top bar (search + filter + button)

            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;

            // Viewport
            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);
            var viewportRT = viewportGO.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = viewportRT.offsetMax = Vector2.zero;
            viewportGO.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);  // przezroczyste tło żeby raycast działał
            viewportGO.AddComponent<Mask>().showMaskGraphic = false;

            scroll.viewport = viewportRT;

            // Content
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.sizeDelta = new Vector2(0, 0);

            var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UITheme.Spacing.Xs;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Xs);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;

            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = contentRT;
            _listContent = contentGO;
        }

        private void BuildListTopBar(Transform parent)
        {
            // Container nad ScrollView (top 100px)
            var topBarGO = new GameObject("ListTopBar");
            topBarGO.transform.SetParent(parent, false);
            var rt = topBarGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.sizeDelta = new Vector2(0, 100);
            rt.anchoredPosition = new Vector2(0, -5);
            rt.offsetMin = new Vector2(5, -105);
            rt.offsetMax = new Vector2(-5, -5);

            var topBarBg = topBarGO.AddComponent<Image>();
            UITheme.ApplySurface(topBarBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Panel);

            var vlg = topBarGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UITheme.Spacing.Sm;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Md);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;

            _listSummaryLabel = CreateLabel(topBarGO.transform, "Przegladaj gotowe i wlasne schematy.", 11, FontStyles.Italic);
            _listSummaryLabel.color = UITheme.SecondaryText;
            _listSummaryLabel.fontStyle = FontStyles.Italic;

            // Search input
            _searchInput = CreateSearchInput(topBarGO.transform);
            _searchInput.onValueChanged.AddListener(OnSearchChanged);

            // Filter dropdown
            var filterOptions = new List<string>(CategoryFilterOptions);
            _categoryFilterDropdown = CreateDropdown(topBarGO.transform, filterOptions, 0);
            _categoryFilterDropdown.onValueChanged.AddListener(OnCategoryFilterChanged);

            // "Utwórz nowy snapshot" button
            var snapshotBtn = CreateButton(topBarGO.transform, "+ Utwórz nowy snapshot", OnCreateSnapshotClicked);
            SetLayoutPreferredHeight(snapshotBtn.gameObject, 28);
        }

        private TMP_InputField CreateSearchInput(Transform parent)
        {
            var go = new GameObject("SearchInput");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 28);

            var bg = go.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.SecondarySurface, UIShapePreset.Inset);

            var input = go.AddComponent<TMP_InputField>();
            input.targetGraphic = bg;

            var taGO = new GameObject("TextArea");
            taGO.transform.SetParent(go.transform, false);
            var taRT = taGO.AddComponent<RectTransform>();
            taRT.anchorMin = Vector2.zero;
            taRT.anchorMax = Vector2.one;
            taRT.offsetMin = new Vector2(8, 4);
            taRT.offsetMax = new Vector2(-8, -4);
            taGO.AddComponent<RectMask2D>();

            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(taGO.transform, false);
            var phRT = phGO.AddComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero;
            phRT.anchorMax = Vector2.one;
            phRT.offsetMin = phRT.offsetMax = Vector2.zero;
            var ph = phGO.AddComponent<TextMeshProUGUI>();
            ph.text = "Szukaj po nazwie...";
            ph.fontSize = 12;
            ph.color = new Color(1, 1, 1, 0.4f);
            ph.alignment = TextAlignmentOptions.MidlineLeft;
            ph.fontStyle = FontStyles.Italic;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(taGO.transform, false);
            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = textRT.offsetMax = Vector2.zero;
            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.fontSize = 12;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            input.textViewport = taRT;
            input.textComponent = text;
            input.placeholder = ph;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 28;

            return input;
        }

        private void BuildParametersPanel()
        {
            var rightGO = new GameObject("ParametersPanel");
            rightGO.transform.SetParent(_root.transform, false);
            var rt = rightGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(listWidthRatio, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(5, 60);
            rt.offsetMax = new Vector2(-10, -69);

            var bg = rightGO.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.OverlayPanel, UIShapePreset.Panel);

            _paramsContent = rightGO;

            // Vertical layout dla parametrów
            var vlg = rightGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UITheme.Spacing.Md;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Lg);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;

            // Header (nazwa schematu)
            var headerCard = new GameObject("SelectionHeaderCard");
            headerCard.transform.SetParent(rightGO.transform, false);
            var headerCardBg = headerCard.AddComponent<Image>();
            UITheme.ApplySurface(headerCardBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Panel);
            var headerCardLE = headerCard.AddComponent<LayoutElement>();
            headerCardLE.preferredHeight = 94f;
            var headerCardLayout = headerCard.AddComponent<VerticalLayoutGroup>();
            headerCardLayout.spacing = UITheme.Spacing.Xs;
            headerCardLayout.padding = UITheme.Padding(UITheme.Spacing.Lg);
            headerCardLayout.childForceExpandWidth = true;
            headerCardLayout.childForceExpandHeight = false;
            headerCardLayout.childAlignment = TextAnchor.UpperLeft;

            var headerEyebrow = CreateLabel(headerCard.transform, "WYBRANY SCHEMAT", 9, FontStyles.Bold);
            headerEyebrow.color = UITheme.PrimaryAccent;

            _selectedHeaderLabel = CreateLabel(headerCard.transform, "Wybierz schemat z listy", 18, FontStyles.Bold);
            _selectedDescLabel = CreateLabel(headerCard.transform, "", 12, FontStyles.Italic);
            _selectedDescLabel.textWrappingMode = TextWrappingModes.Normal;

            CreateLabel(rightGO.transform, "Parametry geometrii", 11, FontStyles.Bold);

            // Track count
            CreateLabel(rightGO.transform, "Liczba torów:", 14);
            var trackCountRow = CreateHorizontalRow(rightGO.transform);
            _trackCountSlider = CreateSlider(trackCountRow.transform, 2, 8, 4);
            _trackCountSlider.wholeNumbers = true;
            _trackCountSlider.onValueChanged.AddListener(OnTrackCountChanged);
            _trackCountValueLabel = CreateLabel(trackCountRow.transform, "4", 14);
            SetLayoutPreferredWidth(_trackCountValueLabel.gameObject, 40);

            // Track spacing
            CreateLabel(rightGO.transform, "Międzytorze (m):", 14);
            var spacingRow = CreateHorizontalRow(rightGO.transform);
            _spacingSlider = CreateSlider(spacingRow.transform,
                SchemaParameters.MinSpacing, SchemaParameters.MaxSpacing, SchemaParameters.DefaultSpacing);
            _spacingSlider.wholeNumbers = false;
            _spacingSlider.onValueChanged.AddListener(OnSpacingChanged);
            _spacingValueLabel = CreateLabel(spacingRow.transform, "5.0", 14);
            SetLayoutPreferredWidth(_spacingValueLabel.gameObject, 50);

            // Turnout type
            CreateLabel(rightGO.transform, "Typ rozjazdu:", 14);
            _turnoutTypeDropdown = CreateDropdown(rightGO.transform, new List<string>(TurnoutTypeOptions), 0);
            _turnoutTypeDropdown.onValueChanged.AddListener(OnTurnoutTypeChanged);

            // Mirror toggle
            _mirrorToggle = CreateToggle(rightGO.transform, "Lustrzane odbicie (mirror)", false);
            _mirrorToggle.onValueChanged.AddListener(OnMirrorToggled);

            // Advanced toggle (placeholder w MD-5 MVP)
            _advancedToggle = CreateToggle(rightGO.transform, "Zaawansowane (per-pair / per-rozjazd)", false);
            _advancedToggle.onValueChanged.AddListener(OnAdvancedToggled);

            _advancedSection = CreateAdvancedSectionPlaceholder(rightGO.transform);
            _advancedSection.SetActive(false);

            // Spacer
            var spacer = new GameObject("Spacer");
            spacer.transform.SetParent(rightGO.transform, false);
            var spacerLE = spacer.AddComponent<LayoutElement>();
            spacerLE.flexibleHeight = 1;

            // Action buttons row (Stawiaj + Zapisz jako preset)
            var actionRow = CreateHorizontalRow(rightGO.transform);
            SetLayoutPreferredHeight(actionRow, 50);
            _placeButton = CreateButton(actionRow.transform, "Stawiaj", OnPlaceClicked);
            _saveAsButton = CreateButton(actionRow.transform, "Zapisz jako preset...", OnSaveAsClicked);
        }

        private GameObject CreateAdvancedSectionPlaceholder(Transform parent)
        {
            // TD-002: real foldout z N-1 wierszami per-pair (trackSpacings + turnoutTypes).
            // Rows generowane przez RebuildAdvancedRows() w Params.cs gdy gracz wybiera
            // schemat lub zmienia trackCount.
            var go = new GameObject("AdvancedSection");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 80);

            var bg = go.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.SecondarySurface, UIShapePreset.Inset);

            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = UITheme.Spacing.Xs;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Md);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperLeft;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Empty state — gdy schemat niezaznaczony lub nie obsługuje per-pair (np. Scissors z 1 row).
            // Initially visible; RebuildAdvancedRows() ukrywa gdy rows generated.
            _advancedEmptyLabel = CreateLabel(go.transform,
                "Wybierz schemat z listy żeby skonfigurować per-pair międzytorza i typy rozjazdów.",
                11, FontStyles.Italic);
            _advancedEmptyLabel.color = UITheme.SecondaryText;
            _advancedEmptyLabel.textWrappingMode = TextWrappingModes.Normal;

            // Rows container — destroyed/recreated przez RebuildAdvancedRows() per selection/trackCount change.
            _advancedRowsContainer = new GameObject("RowsContainer");
            _advancedRowsContainer.transform.SetParent(go.transform, false);
            var rcRT = _advancedRowsContainer.AddComponent<RectTransform>();
            var rcVlg = _advancedRowsContainer.AddComponent<VerticalLayoutGroup>();
            rcVlg.spacing = UITheme.Spacing.Xs;
            rcVlg.childForceExpandWidth = true;
            rcVlg.childForceExpandHeight = false;
            rcVlg.childAlignment = TextAnchor.UpperLeft;
            var rcFitter = _advancedRowsContainer.AddComponent<ContentSizeFitter>();
            rcFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _advancedRowsContainer.SetActive(false);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 80;
            le.flexibleHeight = 1;

            return go;
        }
    }
}
