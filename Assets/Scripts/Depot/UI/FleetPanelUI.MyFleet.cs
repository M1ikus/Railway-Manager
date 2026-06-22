using System.Linq;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Partial FleetPanelUI — zakladka "Moj tabor" (MyFleet).
    /// Filter bar (search + type), column headers z sortowaniem,
    /// PopulateMyFleet z filtrowaniem/sortowaniem, BuildVehicleRow.
    /// </summary>
    public partial class FleetPanelUI
    {
        // ── FILTER BAR ──────────────────────────────

        private void BuildFilterBar()
        {
            _filterBarGO = NewGO("FilterBar", _root.transform);
            var filterBarImage = _filterBarGO.AddComponent<Image>();
            UITheme.ApplySurface(filterBarImage, FilterBarBg, UIShapePreset.Inset);
            var rt = _filterBarGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -(TopBarH + TabBarH));
            rt.sizeDelta = new Vector2(0f, FilterBarH);

            var hl = _filterBarGO.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Sm);
            hl.spacing  = UITheme.Spacing.Md;
            hl.childAlignment      = TextAnchor.MiddleLeft;
            hl.childControlWidth   = false;
            hl.childControlHeight  = false;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // Search icon
            var iconLbl = MakeTMP("SearchIcon", _filterBarGO.transform);
            iconLbl.text      = LocalizationService.Get("fleet.my_fleet.search_label");
            iconLbl.fontSize  = 16;
            iconLbl.color     = TextMuted;
            iconLbl.raycastTarget = false;
            iconLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = 70f;

            // Search input
            var inputGO = NewGO("SearchInput", _filterBarGO.transform);
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

            _searchField = inputGO.AddComponent<TMP_InputField>();
            _searchField.textComponent = inputText;
            _searchField.placeholder   = placeholder;
            _searchField.textViewport  = textArea.GetComponent<RectTransform>();
            _searchField.onValueChanged.AddListener(OnSearchChanged);

            // Separator
            var sep = NewGO("Sep", _filterBarGO.transform);
            sep.GetComponent<RectTransform>().sizeDelta = new Vector2(1f, 28f);
            sep.AddComponent<Image>().color = UITheme.WithAlpha(UITheme.TopBarDivider, 0.7f);
            sep.AddComponent<LayoutElement>().preferredWidth = 1f;

            // Type filter buttons
            CreateTypeFilterButton(LocalizationService.Get("fleet.vehicle_type.filter.all"),  null);
            CreateTypeFilterButton(LocalizationService.Get("fleet.vehicle_type.filter.loco"), FleetVehicleType.ElectricLocomotive);
            CreateTypeFilterButton(LocalizationService.Get("fleet.vehicle_type.filter.emu"),  FleetVehicleType.EMU);
            CreateTypeFilterButton(LocalizationService.Get("fleet.vehicle_type.filter.dmu"),  FleetVehicleType.DMU);
            CreateTypeFilterButton(LocalizationService.Get("fleet.vehicle_type.filter.car"),  FleetVehicleType.PassengerCar);
        }

        private void CreateTypeFilterButton(string label, FleetVehicleType? type)
        {
            var go = NewGO($"TypeFilter_{label}", _filterBarGO.transform);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(80f, 30f);

            var img = go.AddComponent<Image>();
            bool isActive = type == null ? _activeTypeFilters.Count == 0 : _activeTypeFilters.Contains(type.Value);
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
            btn.onClick.AddListener(() => OnTypeFilterClicked(captured));
            // "All" filter (type==null) jest szerszy żeby zmieścić "Wszystkie"/"All"
            go.AddComponent<LayoutElement>().preferredWidth = type == null ? 100f : 70f;

            var lbl = MakeTMP("Lbl", go.transform);
            lbl.text      = label;
            lbl.fontSize  = 14;
            lbl.color     = TextPrimary;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false;
            FillRT(lbl.gameObject);

            _typeFilterButtons.Add((type, img, lbl));
        }

        private void OnTypeFilterClicked(FleetVehicleType? type)
        {
            if (type == null)
            {
                // "Wszystkie" — clear all filters
                _activeTypeFilters.Clear();
            }
            else
            {
                // Toggle this type
                if (!_activeTypeFilters.Remove(type.Value))
                    _activeTypeFilters.Add(type.Value);
            }

            UpdateTypeFilterVisuals();
            PopulateContent();
        }

        private void UpdateTypeFilterVisuals()
        {
            bool allActive = _activeTypeFilters.Count == 0;
            foreach (var (type, bg, lbl) in _typeFilterButtons)
            {
                bool isActive = type == null ? allActive : _activeTypeFilters.Contains(type.Value);
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

        private void OnSortClicked(string field)
        {
            if (_sortField == field)
                _sortAscending = !_sortAscending;
            else
            {
                _sortField = field;
                _sortAscending = true;
            }

            UpdateSortArrows();
            PopulateContent();
        }

        private void UpdateSortArrows()
        {
            string hexActive   = ColorUtility.ToHtmlStringRGB(ArrowActive);
            string hexInactive = ColorUtility.ToHtmlStringRGB(ArrowInactive);

            foreach (var (field, lbl, _) in _sortArrows)
            {
                // Extract original label text from parent GO name ("H_series|Seria / Numer")
                string goName = lbl.transform.parent != null ? lbl.transform.parent.name : lbl.gameObject.name;
                string labelText = goName.Contains("|")
                    ? goName.Substring(goName.IndexOf('|') + 1)
                    : field;

                bool isActive = field == _sortField;
                string upColor   = isActive && _sortAscending  ? hexActive : hexInactive;
                string downColor = isActive && !_sortAscending ? hexActive : hexInactive;

                lbl.text = $"{labelText} <size=9><color=#{upColor}>\u25B2</color><color=#{downColor}>\u25BC</color></size>";
            }
        }

        // ── COLUMN HEADER (built inside scroll content for perfect alignment) ──

        private void BuildColumnHeaderRow()
        {
            _sortArrows.Clear();

            _columnHeaderGO = NewGO("ColumnHeader", _contentParent);
            var headerImage = _columnHeaderGO.AddComponent<Image>();
            UITheme.ApplySurface(headerImage, HeaderRowBg, UIShapePreset.Inset);
            _columnHeaderGO.AddComponent<LayoutElement>().preferredHeight = 36f;

            var hl = _columnHeaderGO.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Xs);
            hl.spacing  = UITheme.Spacing.Sm;
            hl.childAlignment      = TextAnchor.MiddleCenter;
            hl.childControlWidth   = true;
            hl.childControlHeight  = true;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = true;

            MakeHeaderPlain(_columnHeaderGO.transform,   "",              COL_THUMB);
            MakeSortableHeader(_columnHeaderGO.transform, LocalizationService.Get("fleet.my_fleet.columns.name"),    COL_NAME,    "series");
            MakeSortableHeader(_columnHeaderGO.transform, LocalizationService.Get("fleet.my_fleet.columns.status"),  COL_STATUS,  "status");
            MakeSortableHeader(_columnHeaderGO.transform, LocalizationService.Get("fleet.my_fleet.columns.task"),    COL_TASK,    "task");
            MakeSortableHeader(_columnHeaderGO.transform, LocalizationService.Get("fleet.my_fleet.columns.consist"), COL_CONSIST, "consist");
            MakeSortableHeader(_columnHeaderGO.transform, LocalizationService.Get("fleet.my_fleet.columns.mileage"), COL_MILEAGE, "mileage");
            if (ShouldShowSeatsColumn())
                MakeSortableHeader(_columnHeaderGO.transform, LocalizationService.Get("fleet.my_fleet.columns.seats"), COL_SEATS, "seats");
            if (ShowInspectionColumn)
                MakeSortableHeader(_columnHeaderGO.transform, LocalizationService.Get("fleet.my_fleet.columns.inspection"), COL_INSPECTION, "inspection");
            MakeSortableHeader(_columnHeaderGO.transform, LocalizationService.Get("fleet.my_fleet.columns.condition"), COL_COND, "condition");

            // MUI re-skin: akcentowy separator pod nagłówkiem (oddziela nagłówek od wierszy)
            var headerSep = NewGO("HeaderSep", _columnHeaderGO.transform);
            headerSep.AddComponent<LayoutElement>().ignoreLayout = true;
            var headerSepImg = headerSep.AddComponent<Image>();
            headerSepImg.color = UITheme.WithAlpha(UITheme.PrimaryAccent, 0.4f);
            headerSepImg.raycastTarget = false;
            var headerSepRT = headerSep.GetComponent<RectTransform>();
            headerSepRT.anchorMin = new Vector2(0f, 0f);
            headerSepRT.anchorMax = new Vector2(1f, 0f);
            headerSepRT.pivot     = new Vector2(0.5f, 0f);
            headerSepRT.sizeDelta = new Vector2(0f, 2f);
            headerSepRT.anchoredPosition = Vector2.zero;

            StretchRowColumns(_columnHeaderGO.transform);
            UpdateSortArrows();
        }

        /// <summary>Nagłówek bez sortowania (miniaturka, zadanie, skład).</summary>
        private void MakeHeaderPlain(Transform parent, string text, float width)
        {
            var lbl = MakeTMP($"H_{text}", parent);
            lbl.text      = text;
            lbl.fontSize  = 13;
            lbl.fontStyle = FontStyles.Bold;
            lbl.color     = TextMuted;
            lbl.alignment = TextAlignmentOptions.Left;
            lbl.raycastTarget = false;
            lbl.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
        }

        /// <summary>Nagłówek z sortowaniem — klikalny TMP ze strzałkami ▲▼ jako rich text.</summary>
        private void MakeSortableHeader(Transform parent, string text, float width, string sortField)
        {
            // Container GO with Image as raycast target — covers full cell height
            var go = NewGO($"H_{sortField}|{text}", parent);

            var img = go.AddComponent<Image>();
            UITheme.ApplySurface(img, UITheme.WithAlpha(UITheme.TopBarInset, 0f), UIShapePreset.Inset);

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth  = width;
            le.flexibleHeight = 1f; // fill full row height

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
            btn.onClick.AddListener(() => OnSortClicked(captured));

            // TMP label inside — visual only, not a raycast target
            var lbl = MakeTMP("Lbl", go.transform);
            lbl.fontSize  = 13;
            lbl.fontStyle = FontStyles.Bold;
            lbl.color     = TextMuted;
            lbl.alignment = TextAlignmentOptions.Left;
            lbl.richText  = true;
            lbl.raycastTarget = false;
            FillRT(lbl.gameObject);

            _sortArrows.Add((sortField, lbl, lbl));
        }

        // ── MY FLEET ─────────────────────────────────

        private void PopulateMyFleet()
        {
            // Column header as first row in same container = perfect alignment
            BuildColumnHeaderRow();

            string filter = _searchText.Trim().ToLowerInvariant();

            var filtered = _vehicles.Where(v =>
            {
                // Type filter (empty set = show all)
                if (_activeTypeFilters.Count > 0)
                {
                    // "Lok" (ElectricLocomotive) covers both electric and diesel
                    bool matchesType = _activeTypeFilters.Contains(v.type);
                    if (!matchesType && v.type == FleetVehicleType.DieselLocomotive)
                        matchesType = _activeTypeFilters.Contains(FleetVehicleType.ElectricLocomotive);
                    if (!matchesType)
                        return false;
                }

                // Text filter
                if (filter.Length > 0 &&
                    !v.series.ToLowerInvariant().Contains(filter) &&
                    !v.number.ToLowerInvariant().Contains(filter))
                    return false;

                return true;
            });

            // Sort
            filtered = _sortField switch
            {
                "series"    => _sortAscending ? filtered.OrderBy(v => v.series).ThenBy(v => v.number)
                                              : filtered.OrderByDescending(v => v.series).ThenByDescending(v => v.number),
                "status"    => _sortAscending ? filtered.OrderBy(v => (int)v.status)
                                              : filtered.OrderByDescending(v => (int)v.status),
                "task"      => _sortAscending ? filtered.OrderBy(v => v.currentTask ?? "\uFFFF")
                                              : filtered.OrderByDescending(v => v.currentTask ?? ""),
                "consist"   => _sortAscending ? filtered.OrderBy(v => v.assignedConsist ?? "\uFFFF")
                                              : filtered.OrderByDescending(v => v.assignedConsist ?? ""),
                "mileage"   => _sortAscending ? filtered.OrderBy(v => v.mileageKm)
                                              : filtered.OrderByDescending(v => v.mileageKm),
                "seats"     => _sortAscending ? filtered.OrderBy(v => v.passengerSeats)
                                              : filtered.OrderByDescending(v => v.passengerSeats),
                "inspection"=> _sortAscending ? filtered.OrderByDescending(v => GetInspectionUrgency(v))
                                              : filtered.OrderBy(v => GetInspectionUrgency(v)),
                "condition" => _sortAscending ? filtered.OrderBy(v => v.conditionPercent)
                                              : filtered.OrderByDescending(v => v.conditionPercent),
                _           => filtered
            };

            var list = filtered.ToList();

            if (list.Count == 0)
            {
                _emptyLbl.text = filter.Length > 0
                    ? LocalizationService.Get("fleet.my_fleet.empty_no_match_format", _searchText)
                    : LocalizationService.Get("fleet.my_fleet.empty_no_vehicles");
                _emptyLbl.gameObject.SetActive(true);
                return;
            }

            for (int i = 0; i < list.Count; i++)
                BuildVehicleRow(list[i], i);
        }

        private void BuildVehicleRow(FleetVehicleData v, int rowIndex)
        {
            var row = NewGO(v.number, _contentParent);
            row.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, RowH);
            var rowImg = row.AddComponent<Image>();
            // MUI re-skin: zebra striping — czytelna struktura tabeli zamiast jednolitych „klocków"
            Color rowBase = (rowIndex % 2 == 0)
                ? UITheme.WithAlpha(UITheme.PrimarySurface, 0.45f)
                : UITheme.WithAlpha(UITheme.SecondarySurface, 0.38f);
            UITheme.ApplySurface(rowImg, rowBase, UIShapePreset.Inset);
            row.AddComponent<LayoutElement>().preferredHeight = RowH;

            // Flat HLG — childControl=true so LayoutElement sizes are respected
            var hl = row.AddComponent<HorizontalLayoutGroup>();
            hl.padding  = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Sm);
            hl.spacing  = UITheme.Spacing.Sm;
            hl.childAlignment      = TextAnchor.MiddleCenter;
            hl.childControlWidth   = true;
            hl.childControlHeight  = true;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            // ── Col 1: Thumbnail — przyciemniony „chip typu" (subtelny tint + kolorowy label) ──
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

            // ── Col 2: Seria / Numer ──
            var nameLbl = MakeTMP("Name", row.transform);
            string mutedHex = ColorUtility.ToHtmlStringRGB(UITheme.SecondaryText);
            nameLbl.text = $"<b><size=18>{v.series}</size></b>\n<size=13><color=#{mutedHex}>{v.number}</color></size>";
            nameLbl.fontSize    = 18;
            nameLbl.color       = TextPrimary;
            nameLbl.raycastTarget = false;
            nameLbl.richText    = true;
            nameLbl.overflowMode = TextOverflowModes.Ellipsis;
            nameLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = COL_NAME;

            // ── Col 3: Status ──
            var statusLbl = MakeTMP("Status", row.transform);
            Color sc = GetStatusColor(v.status);
            string hexCol = ColorUtility.ToHtmlStringRGB(sc);
            statusLbl.text = $"<color=#{hexCol}>\u25CF</color>  {GetStatusText(v.status)}";
            statusLbl.fontSize  = 14;
            statusLbl.color     = GetStatusColor(v.status);
            statusLbl.raycastTarget = false;
            statusLbl.richText  = true;
            statusLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = COL_STATUS;

            // ── Col 4: Zadanie / Lokalizacja (M9c VehicleLocationService) ──
            var taskLbl = MakeTMP("Task", row.transform);
            string taskText = GetLocationText(v);
            taskLbl.text      = taskText ?? "\u2014";
            taskLbl.fontSize  = 14;
            taskLbl.color     = taskText != null ? TextPrimary : TextMuted;
            taskLbl.raycastTarget = false;
            taskLbl.richText  = true;
            taskLbl.overflowMode = TextOverflowModes.Ellipsis;
            taskLbl.textWrappingMode = TextWrappingModes.NoWrap;
            taskLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = COL_TASK;

            // ── Col 5: Skład / Obieg ──
            // Priorytet: przypisanie do obiegu (M5) > legacy assignedConsist > "—"
            string consistText;
            Color consistColor;
            if (v.assignedCirculationId >= 0)
            {
                consistText = LocalizationService.Get("fleet.my_fleet.consist_format", v.assignedCirculationId);
                consistColor = TextAccent;
            }
            else if (!string.IsNullOrEmpty(v.assignedConsist))
            {
                consistText = v.assignedConsist;
                consistColor = TextAccent;
            }
            else
            {
                consistText = "\u2014";
                consistColor = TextMuted;
            }
            var consistLbl = MakeTMP("Consist", row.transform);
            consistLbl.text      = consistText;
            consistLbl.fontSize  = 14;
            consistLbl.color     = consistColor;
            consistLbl.raycastTarget = false;
            consistLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = COL_CONSIST;

            // ── Col 6: Przebieg ──
            var mileageLbl = MakeTMP("Mileage", row.transform);
            mileageLbl.text      = $"<mspace=0.6em>{v.mileageKm:N0}</mspace> km";
            mileageLbl.richText  = true;
            mileageLbl.fontSize  = 14;
            mileageLbl.color     = TextPrimary;
            mileageLbl.alignment = TextAlignmentOptions.Left;
            mileageLbl.raycastTarget = false;
            mileageLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = COL_MILEAGE;

            // ── Col 7: Miejsca (opcjonalny — tylko gdy filtr pasażerski) ──
            if (ShouldShowSeatsColumn())
            {
                var seatsLbl = MakeTMP("Seats", row.transform);
                seatsLbl.richText = true;
                if (PassengerTypes.Contains(v.type))
                    seatsLbl.text = v.passengerSeats > 0 ? $"<mspace=0.6em>{v.passengerSeats}</mspace>" : "\u2014";
                else
                    seatsLbl.text = "\u2014";
                seatsLbl.fontSize  = 14;
                seatsLbl.color     = PassengerTypes.Contains(v.type) && v.passengerSeats > 0 ? TextPrimary : TextMuted;
                seatsLbl.raycastTarget = false;
                seatsLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = COL_SEATS;
            }

            // ── Col 8: Przegląd (opcjonalny) ──
            if (ShowInspectionColumn)
            {
                var inspLbl = MakeTMP("Inspection", row.transform);
                var inspStatus = v.inspections.GetMostUrgent(NowGameTime, v.mileageKm);
                Color inspColor = GetInspectionColorFromProgress(inspStatus.progress);
                string inspHex = ColorUtility.ToHtmlStringRGB(inspColor);
                inspLbl.text = $"<color=#{inspHex}>{FormatInspectionCompact(inspStatus)}</color>";
                inspLbl.fontSize  = 13;
                inspLbl.color     = TextPrimary;
                inspLbl.richText  = true;
                inspLbl.raycastTarget = false;
                inspLbl.textWrappingMode = TextWrappingModes.NoWrap;
                inspLbl.gameObject.AddComponent<LayoutElement>().preferredWidth = COL_INSPECTION;
            }

            // ── Col 8: Stan — % + bar ──
            var condGO = NewGO("Cond", row.transform);
            var condLE = condGO.AddComponent<LayoutElement>();
            condLE.preferredWidth  = COL_COND;
            condLE.preferredHeight = 40f;

            // Condition % label (top half)
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

            // Condition bar background (bottom part)
            var barBg = NewGO("BarBg", condGO.transform);
            var barBgRT = barBg.GetComponent<RectTransform>();
            barBgRT.anchorMin = new Vector2(0f, 0.15f);
            barBgRT.anchorMax = new Vector2(0.85f, 0.35f);
            barBgRT.offsetMin = Vector2.zero;
            barBgRT.offsetMax = Vector2.zero;
            var conditionBarBg = barBg.AddComponent<Image>();
            UITheme.ApplySurface(conditionBarBg, UITheme.WithAlpha(UITheme.TopBarInset, 0.95f), UIShapePreset.Inset);

            // Condition bar fill
            var barFill = NewGO("BarFill", barBg.transform);
            var barFillRT = barFill.GetComponent<RectTransform>();
            barFillRT.anchorMin = Vector2.zero;
            barFillRT.anchorMax = new Vector2(v.conditionPercent / 100f, 1f);
            barFillRT.offsetMin = Vector2.zero;
            barFillRT.offsetMax = Vector2.zero;
            var conditionBarFill = barFill.AddComponent<Image>();
            UITheme.ApplySurface(conditionBarFill, GetConditionColor(v.conditionPercent), UIShapePreset.Inset);

            // Cały wiersz klikalny → otwiera szczegóły pojazdu
            var rowBtn = row.AddComponent<Button>();
            rowBtn.targetGraphic = rowImg;
            rowBtn.transition = Selectable.Transition.None;
            var capturedVehicle = v;
            rowBtn.onClick.AddListener(() => ShowOwnedDetailWindow(capturedVehicle));

            // Hover effect
            row.AddComponent<HoverImageColor>().Init(rowImg, rowBase, UITheme.WithAlpha(UITheme.RaisedSurface, 0.85f));

            StretchRowColumns(row.transform);
        }

        /// <summary>MUI re-skin: kolumny poza pierwszą (chip) pochłaniają luz (flexibleWidth=1) —
        /// tabela wypełnia szerokość panelu zamiast zostawiać pustkę po prawej. Nagłówek i wiersze
        /// dostają to samo, więc kolumny pozostają wyrównane. Wspólne dla „Mój tabor" i „Kup tabor".</summary>
        private static void StretchRowColumns(Transform row)
        {
            for (int i = 1; i < row.childCount; i++)
            {
                var child = row.GetChild(i);
                var le = child.GetComponent<LayoutElement>();
                if (le == null || le.ignoreLayout) continue;
                le.flexibleWidth = 1f;
                // MUI (request usera 2026-06-19): wartości i nagłówki wyśrodkowane w kolumnie — równy
                // rytm, brak „lgnięcia" do lewej z luką po prawej. Spójne w Mój tabor + Kup tabor.
                var tmp = child.GetComponentInChildren<TextMeshProUGUI>();
                if (tmp != null) tmp.alignment = TextAlignmentOptions.Center;
            }
        }

        /// <summary>
        /// M9c: zwraca tekst lokalizacji pojazdu dla kolumny "Zadanie".
        /// Źródło: <see cref="VehicleLocationService"/>. Fallback: legacy v.currentTask.
        /// </summary>
        private static string GetLocationText(FleetVehicleData v)
        {
            var svc = VehicleLocationService.Instance;
            if (svc == null) return v.currentTask;

            var rec = svc.Get(v.id);
            if (rec == null) return v.currentTask; // pojazd jeszcze nie zarejestrowany

            string successHex = ColorUtility.ToHtmlStringRGB(UITheme.Success);
            string warningHex = ColorUtility.ToHtmlStringRGB(UITheme.Warning);
            string accentHex = ColorUtility.ToHtmlStringRGB(UITheme.PrimaryAccent);
            string primaryHex = ColorUtility.ToHtmlStringRGB(UITheme.PrimaryText);
            string secondaryHex = ColorUtility.ToHtmlStringRGB(UITheme.SecondaryText);
            string dangerHex = ColorUtility.ToHtmlStringRGB(UITheme.Danger);

            switch (rec.type)
            {
                case VehicleLocationType.InDepot:
                    return rec.depotTrackId >= 0
                        ? $"<color=#{successHex}>W zajezdni</color> <size=11><color=#{secondaryHex}>tor #{rec.depotTrackId}</color></size>"
                        : $"<color=#{successHex}>W zajezdni</color>";

                case VehicleLocationType.ExitingDepot:
                    return $"<color=#{warningHex}>Wyjazd z zajezdni</color>";

                case VehicleLocationType.OnRoute:
                    return rec.currentTrainRunId >= 0
                        ? $"<color=#{accentHex}>W trasie</color> <size=11><color=#{secondaryHex}>kurs #{rec.currentTrainRunId}</color></size>"
                        : $"<color=#{accentHex}>W trasie</color>";

                case VehicleLocationType.AtStation:
                    return rec.stationId >= 0
                        ? $"<color=#{primaryHex}>Stacja #{rec.stationId}</color>"
                        : $"<color=#{primaryHex}>Na peronie</color>";

                case VehicleLocationType.EnteringDepot:
                    return $"<color=#{warningHex}>Wjazd do zajezdni</color>";

                case VehicleLocationType.InTransit:
                    return $"<color=#{dangerHex}>Między stacjami</color>";

                default:
                    return v.currentTask;
            }
        }
    }
}
