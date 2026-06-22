using System.Linq;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Partial FleetPanelUI — zakladka "Kup tabor" → sub-tab "Uzywane" (Market).
    /// Zawiera: sub-tab bar (Uzywane/Nowe), filter bar dla rynku, kolumny i wiersze
    /// rynku, filtrowanie i sortowanie. Konfigurator nowych pojazdow w osobnym partial.
    /// </summary>
    public partial class FleetPanelUI
    {
        // ── MARKET SUB-TAB BAR (Używane / Nowe) ─────

        private void BuildMarketSubTabBar()
        {
            _marketSubTabBarGO = NewGO("MarketSubTabBar", _root.transform);
            var subTabBarImage = _marketSubTabBarGO.AddComponent<Image>();
            UITheme.ApplySurface(subTabBarImage, MarketSubTabBg, UIShapePreset.Inset);
            var rt = _marketSubTabBarGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -(TopBarH + TabBarH));
            rt.sizeDelta = new Vector2(0f, MarketSubTabH);

            var hl = _marketSubTabBarGO.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Xs);
            hl.spacing  = UITheme.Spacing.Sm;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            hl.childControlWidth   = false;
            hl.childControlHeight  = false;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            CreateMarketSubTab(MarketSubTab.Used,            LocalizationService.Get("fleet.market.sub_tabs.used"));
            CreateMarketSubTab(MarketSubTab.NewConfigurator, LocalizationService.Get("fleet.market.sub_tabs.new"));

            _marketSubTabBarGO.SetActive(false);
        }

        private void CreateMarketSubTab(MarketSubTab tab, string label)
        {
            var go = NewGO($"MTab_{tab}", _marketSubTabBarGO.transform);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(160f, 32f);
            var img = go.AddComponent<Image>();
            bool isActive = tab == _marketSubTab;
            UITheme.ApplySurface(img, isActive ? TabActive : TabNormal, isActive ? UIShapePreset.Pill : UIShapePreset.Button);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = UITheme.CreateColorBlock(
                isActive ? TabActive : TabNormal,
                isActive ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface,
                isActive ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border,
                isActive ? TabActive : TabNormal,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            var captured = tab;
            btn.onClick.AddListener(() => SwitchMarketSubTab(captured));
            go.AddComponent<LayoutElement>().preferredWidth = 160f;

            var lbl = MakeTMP("Lbl", go.transform);
            lbl.text      = label;
            lbl.fontSize  = 15;
            lbl.fontStyle = FontStyles.Bold;
            lbl.color     = TextPrimary;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false;
            FillRT(lbl.gameObject);

            _marketSubTabButtons.Add((tab, img, lbl));
        }

        private void SwitchMarketSubTab(MarketSubTab tab)
        {
            _marketSubTab = tab;

            foreach (var (t, bg, lbl) in _marketSubTabButtons)
            {
                bool isActive = t == _marketSubTab;
                bg.color = isActive ? TabActive : TabNormal;
                if (lbl != null)
                    lbl.color = isActive ? UITheme.InverseText : TextPrimary;

                var button = bg != null ? bg.GetComponent<Button>() : null;
                if (button != null)
                {
                    button.colors = UITheme.CreateColorBlock(
                        isActive ? TabActive : TabNormal,
                        isActive ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface,
                        isActive ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border,
                        isActive ? TabActive : TabNormal,
                        UITheme.WithAlpha(UITheme.Border, 0.55f));
                }
            }

            bool isUsed = tab == MarketSubTab.Used;

            _marketFilterBarGO.SetActive(isUsed);
            _viewportGO.SetActive(isUsed);
            _scrollRectGO.SetActive(isUsed);
            _configuratorGO.SetActive(!isUsed);

            if (isUsed)
            {
                float topOffset = TopBarH + TabBarH + MarketSubTabH + FilterBarH;
                _viewportGO.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -topOffset);
                _scrollRectGO.GetComponent<RectTransform>().offsetMax = new Vector2(0f, -topOffset);
                PopulateContent();
            }
        }

        // ── MARKET FILTER BAR ────────────────────────

        private void BuildMarketFilterBar()
        {
            _marketFilterBarGO = NewGO("MarketFilterBar", _root.transform);
            var filterBarImage = _marketFilterBarGO.AddComponent<Image>();
            UITheme.ApplySurface(filterBarImage, FilterBarBg, UIShapePreset.Inset);
            var rt = _marketFilterBarGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -(TopBarH + TabBarH + MarketSubTabH));
            rt.sizeDelta = new Vector2(0f, FilterBarH);

            var hl = _marketFilterBarGO.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Sm);
            hl.spacing  = UITheme.Spacing.Md;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            hl.childControlWidth   = false;
            hl.childControlHeight  = false;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // Search label
            var iconLbl = MakeTMP("SearchIcon", _marketFilterBarGO.transform);
            iconLbl.text      = LocalizationService.Get("fleet.my_fleet.search_label");
            iconLbl.fontSize  = 16;
            iconLbl.color     = TextMuted;
            iconLbl.raycastTarget = false;
            iconLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 70f;

            // Search input
            var inputGO = NewGO("SearchInput", _marketFilterBarGO.transform);
            inputGO.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 34f);
            var inputImage = inputGO.AddComponent<Image>();
            UITheme.ApplySurface(inputImage, InputBg, UIShapePreset.Inset);
            inputGO.AddComponent<LayoutElement>().preferredWidth = 300f;

            var textArea = NewGO("Text Area", inputGO.transform);
            FillRT(textArea);
            var textAreaRT = textArea.GetComponent<RectTransform>();
            textAreaRT.offsetMin = new Vector2(8f, 2f);
            textAreaRT.offsetMax = new Vector2(-8f, -2f);

            var placeholder = MakeTMP("Placeholder", textArea.transform);
            placeholder.text      = LocalizationService.Get("fleet.my_fleet.search_placeholder");
            placeholder.fontSize  = 16;
            placeholder.color     = TextMuted;
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.raycastTarget = false;
            FillRT(placeholder.gameObject);

            var inputText = MakeTMP("Text", textArea.transform);
            inputText.fontSize = 16;
            inputText.color    = TextPrimary;
            inputText.raycastTarget = false;
            FillRT(inputText.gameObject);

            _marketSearchField = inputGO.AddComponent<TMP_InputField>();
            _marketSearchField.textComponent = inputText;
            _marketSearchField.placeholder   = placeholder;
            _marketSearchField.textViewport  = textArea.GetComponent<RectTransform>();
            _marketSearchField.onValueChanged.AddListener(OnMarketSearchChanged);

            // Separator
            var sep = NewGO("Sep", _marketFilterBarGO.transform);
            sep.GetComponent<RectTransform>().sizeDelta = new Vector2(1f, 28f);
            sep.AddComponent<Image>().color = UITheme.WithAlpha(UITheme.TopBarDivider, 0.7f);
            sep.AddComponent<LayoutElement>().preferredWidth = 1f;

            // Type filter buttons
            CreateMarketTypeFilterButton(LocalizationService.Get("fleet.vehicle_type.filter.all"),  null);
            CreateMarketTypeFilterButton(LocalizationService.Get("fleet.vehicle_type.filter.loco"), FleetVehicleType.ElectricLocomotive);
            CreateMarketTypeFilterButton(LocalizationService.Get("fleet.vehicle_type.filter.emu"),  FleetVehicleType.EMU);
            CreateMarketTypeFilterButton(LocalizationService.Get("fleet.vehicle_type.filter.dmu"),  FleetVehicleType.DMU);
            CreateMarketTypeFilterButton(LocalizationService.Get("fleet.vehicle_type.filter.car"),  FleetVehicleType.PassengerCar);

            _marketFilterBarGO.SetActive(false);
        }

        private void CreateMarketTypeFilterButton(string label, FleetVehicleType? type)
        {
            var go = NewGO($"MTypeFilter_{label}", _marketFilterBarGO.transform);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(80f, 30f);

            var img = go.AddComponent<Image>();
            bool isActive = type == null;
            UITheme.ApplySurface(img, isActive ? TabActive : BtnSecondary, isActive ? UIShapePreset.Pill : UIShapePreset.Button);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = UITheme.CreateColorBlock(
                isActive ? TabActive : BtnSecondary,
                isActive ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface,
                isActive ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border,
                isActive ? TabActive : BtnSecondary,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            var captured = type;
            btn.onClick.AddListener(() => OnMarketTypeFilterClicked(captured));
            // "All" filter (type==null) jest szerszy żeby zmieścić "Wszystkie"/"All"
            go.AddComponent<LayoutElement>().preferredWidth = type == null ? 100f : 70f;

            var lbl = MakeTMP("Lbl", go.transform);
            lbl.text      = label;
            lbl.fontSize  = 14;
            lbl.color     = TextPrimary;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false;
            FillRT(lbl.gameObject);

            _marketTypeFilterButtons.Add((type, img, lbl));
        }

        // ── MARKET TAB (Używane) ─────────────────────

        private bool MarketShouldShowSeatsColumn()
        {
            if (_marketTypeFilters.Count == 0) return true;
            return _marketTypeFilters.Overlaps(PassengerTypes);
        }

        private void PopulateMarket()
        {
            _marketSortArrows.Clear();

            // Column header
            BuildMarketColumnHeader();

            string filter = _marketSearchText.Trim().ToLowerInvariant();

            var filtered = _marketVehicles.Where(v =>
            {
                if (_marketTypeFilters.Count > 0)
                {
                    bool matchesType = _marketTypeFilters.Contains(v.type);
                    if (!matchesType && v.type == FleetVehicleType.DieselLocomotive)
                        matchesType = _marketTypeFilters.Contains(FleetVehicleType.ElectricLocomotive);
                    if (!matchesType) return false;
                }
                if (filter.Length > 0 &&
                    !v.series.ToLowerInvariant().Contains(filter) &&
                    !v.number.ToLowerInvariant().Contains(filter))
                    return false;
                return true;
            });

            filtered = _marketSortField switch
            {
                "series"     => _marketSortAscending ? filtered.OrderBy(v => v.series).ThenBy(v => v.number)
                                                     : filtered.OrderByDescending(v => v.series).ThenByDescending(v => v.number),
                "mileage"    => _marketSortAscending ? filtered.OrderBy(v => v.mileageKm)
                                                     : filtered.OrderByDescending(v => v.mileageKm),
                "seats"      => _marketSortAscending ? filtered.OrderBy(v => v.passengerSeats)
                                                     : filtered.OrderByDescending(v => v.passengerSeats),
                "inspection" => _marketSortAscending ? filtered.OrderByDescending(v => GetInspectionUrgency(v))
                                                     : filtered.OrderBy(v => GetInspectionUrgency(v)),
                "condition"  => _marketSortAscending ? filtered.OrderBy(v => v.conditionPercent)
                                                     : filtered.OrderByDescending(v => v.conditionPercent),
                "price"      => _marketSortAscending ? filtered.OrderBy(v => v.price)
                                                     : filtered.OrderByDescending(v => v.price),
                _            => filtered
            };

            var list = filtered.ToList();

            if (list.Count == 0)
            {
                _emptyLbl.text = filter.Length > 0
                    ? LocalizationService.Get("fleet.market.empty_no_match_format", _marketSearchText)
                    : LocalizationService.Get("fleet.market.empty_no_market");
                _emptyLbl.gameObject.SetActive(true);
                return;
            }

            for (int i = 0; i < list.Count; i++)
                BuildMarketRow(list[i], i);

            UpdateMarketSortArrows();
        }

        private void BuildMarketColumnHeader()
        {
            var header = NewGO("MarketColumnHeader", _contentParent);
            var headerImage = header.AddComponent<Image>();
            UITheme.ApplySurface(headerImage, HeaderRowBg, UIShapePreset.Inset);
            header.AddComponent<LayoutElement>().preferredHeight = 36f;

            var hl = header.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Xs);
            hl.spacing  = UITheme.Spacing.Sm;
            hl.childAlignment      = TextAnchor.MiddleCenter;
            hl.childControlWidth   = true;
            hl.childControlHeight  = true;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = true;

            MakeHeaderPlain(header.transform,          "",              COL_THUMB);
            MakeMarketSortableHeader(header.transform, LocalizationService.Get("fleet.my_fleet.columns.name"),    COL_NAME,    "series");
            MakeMarketSortableHeader(header.transform, LocalizationService.Get("fleet.my_fleet.columns.mileage"), COL_MILEAGE, "mileage");
            if (MarketShouldShowSeatsColumn())
                MakeMarketSortableHeader(header.transform, LocalizationService.Get("fleet.my_fleet.columns.seats"), COL_SEATS, "seats");
            if (ShowInspectionColumn)
                MakeMarketSortableHeader(header.transform, LocalizationService.Get("fleet.my_fleet.columns.inspection"), COL_INSPECTION, "inspection");
            MakeMarketSortableHeader(header.transform, LocalizationService.Get("fleet.my_fleet.columns.condition"), COL_COND, "condition");
            MakeHeaderPlain(header.transform,          LocalizationService.Get("fleet.market.columns.location"),    COL_LOCATION);
            MakeMarketSortableHeader(header.transform, LocalizationService.Get("fleet.market.columns.price"), COL_PRICE, "price");

            StretchRowColumns(header.transform);
        }

        private void MakeMarketSortableHeader(Transform parent, string text, float width, string sortField)
        {
            var go = NewGO($"MH_{sortField}|{text}", parent);

            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, UITheme.WithAlpha(UITheme.TopBarInset, 0f), UIShapePreset.Inset);

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleHeight = 1f;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;
            btn.colors = UITheme.CreateColorBlock(
                UITheme.WithAlpha(UITheme.TopBarInset, 0f),
                UITheme.WithAlpha(UITheme.TopBarInset, 0.82f),
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.92f),
                UITheme.WithAlpha(UITheme.TopBarInset, 0.82f),
                UITheme.WithAlpha(UITheme.Border, 0.55f));

            string captured = sortField;
            btn.onClick.AddListener(() => OnMarketSortClicked(captured));

            var lbl = MakeTMP("Lbl", go.transform);
            lbl.fontSize  = 13;
            lbl.fontStyle = FontStyles.Bold;
            lbl.color     = TextMuted;
            lbl.alignment = TextAlignmentOptions.Left;
            lbl.richText  = true;
            lbl.raycastTarget = false;
            FillRT(lbl.gameObject);

            _marketSortArrows.Add((sortField, lbl, lbl));
        }

        private void BuildMarketRow(FleetMarketVehicle v, int rowIndex)
        {
            var row = NewGO(v.number, _contentParent);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, RowH);
            var rowImg = row.AddComponent<Image>();
            // MUI re-skin: zebra striping (spójnie z Mój tabor)
            Color rowBase = (rowIndex % 2 == 0)
                ? UITheme.WithAlpha(UITheme.PrimarySurface, 0.45f)
                : UITheme.WithAlpha(UITheme.SecondarySurface, 0.38f);
            UITheme.ApplySurface(rowImg, rowBase, UIShapePreset.Inset);
            row.AddComponent<LayoutElement>().preferredHeight = RowH;

            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Sm);
            hl.spacing  = UITheme.Spacing.Sm;
            hl.childAlignment      = TextAnchor.MiddleCenter;
            hl.childControlWidth   = true;
            hl.childControlHeight  = true;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // ── Thumbnail — przyciemniony „chip typu" (tint + kolorowy label), spójnie z Mój tabor ──
            var thumbGO = NewGO("Thumb", row.transform);
            Color typeColor = GetThumbnailColor(v.type);
            var thumbImage = thumbGO.AddComponent<Image>();
            UITheme.ApplySurface(thumbImage, UITheme.WithAlpha(typeColor, 0.32f), UIShapePreset.Button);
            var thumbLE = thumbGO.AddComponent<LayoutElement>();
            thumbLE.preferredWidth  = COL_THUMB;
            thumbLE.preferredHeight = 40f;

            var typeLbl = MakeTMP("TypeLbl", thumbGO.transform);
            typeLbl.text      = GetTypeShortLabel(v.type);
            typeLbl.fontSize  = 12;
            typeLbl.color     = Color.Lerp(typeColor, Color.white, 0.3f);
            typeLbl.alignment = TextAlignmentOptions.Center;
            typeLbl.enableAutoSizing = true;
            typeLbl.fontSizeMin = 9;
            typeLbl.fontSizeMax = 12;
            typeLbl.raycastTarget = false;
            FillRT(typeLbl.gameObject);

            // Seria / Numer
            var nameLbl = MakeTMP("Name", row.transform);
            string mutedHex = ColorUtility.ToHtmlStringRGB(UITheme.SecondaryText);
            nameLbl.text = $"<b><size=18>{v.series}</size></b>\n<size=13><color=#{mutedHex}>{v.number}</color></size>";
            nameLbl.fontSize    = 18;
            nameLbl.color       = TextPrimary;
            nameLbl.raycastTarget = false;
            nameLbl.richText    = true;
            nameLbl.overflowMode = TextOverflowModes.Ellipsis;
            nameLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = COL_NAME;

            // Przebieg
            var mileageLbl = MakeTMP("Mileage", row.transform);
            mileageLbl.text      = $"<mspace=0.6em>{v.mileageKm:N0}</mspace> km";
            mileageLbl.richText  = true;
            mileageLbl.fontSize  = 14;
            mileageLbl.color     = TextPrimary;
            mileageLbl.alignment = TextAlignmentOptions.Left;
            mileageLbl.raycastTarget = false;
            mileageLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = COL_MILEAGE;

            // Miejsca (opcjonalnie)
            if (MarketShouldShowSeatsColumn())
            {
                var seatsLbl = MakeTMP("Seats", row.transform);
                seatsLbl.richText = true;
                seatsLbl.text = PassengerTypes.Contains(v.type) && v.passengerSeats > 0
                    ? $"<mspace=0.6em>{v.passengerSeats}</mspace>" : LocalizationService.Get("fleet.market.dash");
                seatsLbl.fontSize  = 14;
                seatsLbl.color     = PassengerTypes.Contains(v.type) && v.passengerSeats > 0 ? TextPrimary : TextMuted;
                seatsLbl.raycastTarget = false;
                seatsLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = COL_SEATS;
            }

            // Przegląd (opcjonalnie)
            if (ShowInspectionColumn)
            {
                var inspLbl = MakeTMP("Inspection", row.transform);
                var inspStatus = v.inspections.GetMostUrgent(NowGameTime, v.mileageKm);
                Color inspColor = GetInspectionColorFromProgress(inspStatus.progress);
                string inspHex = ColorUtility.ToHtmlStringRGB(inspColor);
                // Inspection format z i18n key — colored "remaining" + level. Trzymamy HTML/rich
                // text bezpośrednio bo to UI styling, nie translatable text.
                inspLbl.text = $"<color=#{inspHex}>{FormatInspectionCompact(inspStatus)}</color>";
                inspLbl.fontSize  = 13;
                inspLbl.color     = TextPrimary;
                inspLbl.richText  = true;
                inspLbl.raycastTarget = false;
                inspLbl.textWrappingMode = TextWrappingModes.NoWrap;
                inspLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = COL_INSPECTION;
            }

            // Stan — % + bar
            var condGO = NewGO("Cond", row.transform);
            var condLE = condGO.AddComponent<LayoutElement>();
            condLE.preferredWidth  = COL_COND;
            condLE.preferredHeight = 40f;

            var condLbl = MakeTMP("CondLbl", condGO.transform);
            condLbl.text      = $"<mspace=0.6em>{v.conditionPercent:F0}</mspace>%";
            condLbl.richText  = true;
            condLbl.fontSize  = 14;
            condLbl.color     = GetConditionColor(v.conditionPercent);
            condLbl.alignment = TextAlignmentOptions.Left;
            condLbl.raycastTarget = false;
            var condLblRT = condLbl.GetComponent<RectTransform>();
            condLblRT.anchorMin = new Vector2(0f, 0.4f);
            condLblRT.anchorMax = new Vector2(1f, 1f);
            condLblRT.offsetMin = Vector2.zero;
            condLblRT.offsetMax = Vector2.zero;

            var barBg = NewGO("BarBg", condGO.transform);
            var barBgRT = barBg.GetComponent<RectTransform>();
            barBgRT.anchorMin = new Vector2(0f, 0.15f);
            barBgRT.anchorMax = new Vector2(0.85f, 0.35f);
            barBgRT.offsetMin = Vector2.zero;
            barBgRT.offsetMax = Vector2.zero;
            var conditionBarBg = barBg.AddComponent<Image>();
            UITheme.ApplySurface(conditionBarBg, UITheme.WithAlpha(UITheme.TopBarInset, 0.95f), UIShapePreset.Inset);

            var barFill = NewGO("BarFill", barBg.transform);
            var barFillRT = barFill.GetComponent<RectTransform>();
            barFillRT.anchorMin = Vector2.zero;
            barFillRT.anchorMax = new Vector2(v.conditionPercent / 100f, 1f);
            barFillRT.offsetMin = Vector2.zero;
            barFillRT.offsetMax = Vector2.zero;
            var conditionBarFill = barFill.AddComponent<Image>();
            UITheme.ApplySurface(conditionBarFill, GetConditionColor(v.conditionPercent), UIShapePreset.Inset);

            // Lokalizacja
            var locLbl = MakeTMP("Location", row.transform);
            locLbl.text      = v.location;
            locLbl.fontSize  = 13;
            locLbl.color     = TextMuted;
            locLbl.raycastTarget = false;
            locLbl.overflowMode = TextOverflowModes.Ellipsis;
            locLbl.textWrappingMode = TextWrappingModes.NoWrap;
            locLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = COL_LOCATION;

            // Cena
            var priceLbl = MakeTMP("Price", row.transform);
            priceLbl.text      = NumberFormatService.FormatCurrency(v.price);
            priceLbl.fontSize  = 15;
            priceLbl.fontStyle = FontStyles.Bold;
            priceLbl.color     = PriceColor;
            priceLbl.alignment = TextAlignmentOptions.Right;
            priceLbl.raycastTarget = false;
            priceLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = COL_PRICE;

            // Cały wiersz klikalny → otwiera szczegóły
            var rowBtn = row.AddComponent<Button>();
            rowBtn.targetGraphic = rowImg;
            rowBtn.transition = Selectable.Transition.None; // hover handled by HoverImageColor
            var capturedVehicle = v;
            rowBtn.onClick.AddListener(() => ShowMarketDetailPopup(capturedVehicle));

            // Hover
            row.AddComponent<HoverImageColor>().Init(rowImg, rowBase, UITheme.WithAlpha(UITheme.RaisedSurface, 0.85f));

            StretchRowColumns(row.transform);
        }

        // ── Market event handlers ────────────────────

        private void OnMarketSearchChanged(string value)
        {
            _marketSearchText = value;
            PopulateContent();
        }

        private void OnMarketSortClicked(string field)
        {
            if (_marketSortField == field)
                _marketSortAscending = !_marketSortAscending;
            else
            {
                _marketSortField = field;
                _marketSortAscending = true;
            }
            UpdateMarketSortArrows();
            PopulateContent();
        }

        private void UpdateMarketSortArrows()
        {
            string hexActive   = ColorUtility.ToHtmlStringRGB(ArrowActive);
            string hexInactive = ColorUtility.ToHtmlStringRGB(ArrowInactive);

            foreach (var (field, lbl, _) in _marketSortArrows)
            {
                string goName = lbl.transform.parent != null ? lbl.transform.parent.name : lbl.gameObject.name;
                string labelText = goName.Contains("|")
                    ? goName.Substring(goName.IndexOf('|') + 1)
                    : field;

                bool isActive = field == _marketSortField;
                string upColor   = isActive && _marketSortAscending  ? hexActive : hexInactive;
                string downColor = isActive && !_marketSortAscending ? hexActive : hexInactive;

                lbl.text = $"{labelText} <size=9><color=#{upColor}>\u25B2</color><color=#{downColor}>\u25BC</color></size>";
            }
        }

        private void OnMarketTypeFilterClicked(FleetVehicleType? type)
        {
            if (type == null)
                _marketTypeFilters.Clear();
            else
            {
                if (!_marketTypeFilters.Remove(type.Value))
                    _marketTypeFilters.Add(type.Value);
            }
            UpdateMarketTypeFilterVisuals();
            PopulateContent();
        }

        private void UpdateMarketTypeFilterVisuals()
        {
            bool allActive = _marketTypeFilters.Count == 0;
            foreach (var (type, bg, lbl) in _marketTypeFilterButtons)
            {
                bool isActive = type == null ? allActive : _marketTypeFilters.Contains(type.Value);
                bg.color = isActive ? TabActive : BtnSecondary;
                if (lbl != null)
                    lbl.color = isActive ? UITheme.InverseText : TextPrimary;

                var button = bg != null ? bg.GetComponent<Button>() : null;
                if (button != null)
                {
                    button.colors = UITheme.CreateColorBlock(
                        isActive ? TabActive : BtnSecondary,
                        isActive ? UITheme.PrimaryAccentHover : UITheme.RaisedSurface,
                        isActive ? UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f) : UITheme.Border,
                        isActive ? TabActive : BtnSecondary,
                        UITheme.WithAlpha(UITheme.Border, 0.55f));
                }
            }
        }
    }
}
