using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class TimetableCreatorUI
    {
        public void BuildUI(Transform canvas)
        {
            _raycaster = canvas.GetComponent<GraphicRaycaster>();

            // Panel jak Fleet — margines na tab bar (70px lewy) i top bar (40px górny)
            _panel = new GameObject("TimetableCreator");
            _panel.transform.SetParent(canvas, false);
            var prt = _panel.AddComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(75, 0);
            prt.offsetMax = new Vector2(0, -42);
            var panelImg = _panel.AddComponent<Image>();
            UITheme.ApplySurface(panelImg, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.PanelLarge);

            var vlg = _panel.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Lg);
            vlg.spacing = UITheme.Spacing.Sm;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var header = Row(_panel.transform);
            header.GetComponent<LayoutElement>().preferredHeight = 56;
            UITheme.ApplySurface(header.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            var headerLayout = header.GetComponent<HorizontalLayoutGroup>();
            headerLayout.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);

            var titleStack = new GameObject("TitleStack");
            titleStack.transform.SetParent(header.transform, false);
            titleStack.AddComponent<RectTransform>();
            titleStack.AddComponent<LayoutElement>().flexibleWidth = 1;
            var titleStackVlg = titleStack.AddComponent<VerticalLayoutGroup>();
            titleStackVlg.spacing = UITheme.Spacing.Xxs;
            titleStackVlg.childControlWidth = true;
            titleStackVlg.childControlHeight = true; // bez tego eyebrow + tytul nachodza na siebie
            titleStackVlg.childForceExpandWidth = true;
            titleStackVlg.childForceExpandHeight = false;
            titleStackVlg.childAlignment = TextAnchor.MiddleLeft;
            Lbl(titleStack.transform, "KREATOR ROZKLADU", 10, UITheme.PrimaryAccent);
            Lbl(titleStack.transform, LocalizationService.Get("timetable.creator.title"), 18, Color.white);
            var spacer = new GameObject("Spacer");
            spacer.transform.SetParent(header.transform, false);
            spacer.AddComponent<LayoutElement>().preferredWidth = 8;
            Btn(
                header.transform,
                LocalizationService.Get("timetable.creator.button.close"),
                CancelAll,
                new Color(0.6f, 0.2f, 0.2f),
                104);
            Sep(_panel.transform);

            SectionHeader(
                _panel.transform,
                "TRASA",
                LocalizationService.Get("timetable.creator.section.route"),
                "Wybierz stacje poczatkowa i koncowa, a potem doprecyzuj przebieg waypointami.");

            var r1 = Row(_panel.transform);
            StyleRow(r1);
            Lbl(r1.transform, LocalizationService.Get("timetable.creator.route.label_a"), 12, new Color(0.7f, 0.7f, 0.7f));
            _startInput = Inp(r1.transform, "", 200, LocalizationService.Get("timetable.creator.route.input_placeholder"));
            _startInput.onValueChanged.AddListener(OnStartInputChanged);
            _startTrackDropdown = MakeDropdown(r1.transform);
            _startTrackDropdown.GetComponent<LayoutElement>().preferredWidth = 90;
            _startTrackDropdown.GetComponent<LayoutElement>().flexibleWidth = 0;
            _startTrackDropdown.onValueChanged.AddListener(OnStartTrackChanged);
            Btn(
                r1.transform,
                LocalizationService.Get("timetable.creator.button.pick_on_map"),
                () => PickOnMap(true),
                new Color(0.25f, 0.5f, 0.8f),
                75);

            _startSuggestions = MakeSuggestionsContainer(_panel.transform);

            var r2 = Row(_panel.transform);
            StyleRow(r2);
            Lbl(r2.transform, LocalizationService.Get("timetable.creator.route.label_b"), 12, new Color(0.7f, 0.7f, 0.7f));
            _endInput = Inp(r2.transform, "", 200, LocalizationService.Get("timetable.creator.route.input_placeholder"));
            _endInput.onValueChanged.AddListener(OnEndInputChanged);
            _endTrackDropdown = MakeDropdown(r2.transform);
            _endTrackDropdown.GetComponent<LayoutElement>().preferredWidth = 90;
            _endTrackDropdown.GetComponent<LayoutElement>().flexibleWidth = 0;
            _endTrackDropdown.onValueChanged.AddListener(OnEndTrackChanged);
            Btn(
                r2.transform,
                LocalizationService.Get("timetable.creator.button.pick_on_map"),
                () => PickOnMap(false),
                new Color(0.25f, 0.5f, 0.8f),
                75);

            _endSuggestions = MakeSuggestionsContainer(_panel.transform);

            // M-TimetableUX 2026-05-11: ScrollView dla waypoints (max-height 240px ~ 6 rows widocznych).
            // Struktura kopiowana 1:1 z stopsScrollView (linia 319+) — działający wzór ScrollRect.
            var wpScrollView = new GameObject("WaypointsScrollView", typeof(RectTransform));
            wpScrollView.transform.SetParent(_panel.transform, false);
            var wpScrollLe = wpScrollView.AddComponent<LayoutElement>();
            wpScrollLe.preferredHeight = 240;
            wpScrollLe.flexibleHeight = 0;
            var wpScrollImg = wpScrollView.AddComponent<Image>();
            UITheme.ApplySurface(wpScrollImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Panel);
            wpScrollImg.raycastTarget = true;
            var wpScrollRect = wpScrollView.AddComponent<ScrollRect>();
            wpScrollRect.horizontal = false;
            wpScrollRect.vertical = true;
            wpScrollRect.movementType = ScrollRect.MovementType.Elastic;
            wpScrollRect.scrollSensitivity = 25f;

            var wpViewport = new GameObject("Viewport", typeof(RectTransform));
            wpViewport.transform.SetParent(wpScrollView.transform, false);
            var wpVpRt = (RectTransform)wpViewport.transform;
            wpVpRt.anchorMin = Vector2.zero;
            wpVpRt.anchorMax = Vector2.one;
            wpVpRt.offsetMin = Vector2.zero;
            wpVpRt.offsetMax = Vector2.zero;
            var wpVpImg = wpViewport.AddComponent<Image>();
            UITheme.ApplySurface(wpVpImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.14f), UIShapePreset.Inset);
            var wpMask = wpViewport.AddComponent<Mask>();
            wpMask.showMaskGraphic = false;

            var wpContentGo = new GameObject("Content", typeof(RectTransform));
            wpContentGo.transform.SetParent(wpViewport.transform, false);
            var wpCRt = (RectTransform)wpContentGo.transform;
            wpCRt.anchorMin = new Vector2(0, 1);
            wpCRt.anchorMax = new Vector2(1, 1);
            wpCRt.pivot = new Vector2(0.5f, 1f);
            wpCRt.anchoredPosition = Vector2.zero;
            wpCRt.sizeDelta = Vector2.zero;
            var wpVlg = wpContentGo.AddComponent<VerticalLayoutGroup>();
            wpVlg.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            wpVlg.spacing = UITheme.Spacing.Xs;
            wpVlg.childForceExpandWidth = true;
            wpVlg.childForceExpandHeight = false;
            wpVlg.childAlignment = TextAnchor.UpperLeft;
            var wpCsf = wpContentGo.AddComponent<ContentSizeFitter>();
            wpCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            wpScrollRect.viewport = wpVpRt;
            wpScrollRect.content = wpCRt;

            _waypointsContainer = wpContentGo.transform;

            Btn(
                _panel.transform,
                LocalizationService.Get("timetable.creator.button.add_waypoint"),
                AddWaypoint,
                new Color(0.2f, 0.35f, 0.55f));

            _routeInfoText = Lbl(_panel.transform, "", 11, new Color(0.6f, 0.9f, 0.6f));
            UITheme.ApplySurface(
                _routeInfoText.gameObject.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.TopBarInset, 0.88f),
                UIShapePreset.Inset);
            // M-TimetableUX 2026-05-11: linie kolejowe aktualnej trasy (lk9+lk204 format).
            _routeLinesText = Lbl(_panel.transform, "", 11, new Color(0.7f, 0.85f, 1f));
            UITheme.ApplySurface(
                _routeLinesText.gameObject.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.TopBarInset, 0.88f),
                UIShapePreset.Inset);

            // M-TimetableUX 2026-05-11: panel alternatyw (K-shortest paths). Auto-background trigger
            // po wpisaniu A/B (debounced 500ms). Buttony "lk9+lk204 (153km)" — klik wybiera trasę.
            var altObj = new GameObject("RouteAlternatives", typeof(RectTransform));
            altObj.transform.SetParent(_panel.transform, false);
            altObj.AddComponent<Image>().color = new Color(0, 0, 0, 0); // transparent bg
            var altVlg = altObj.AddComponent<VerticalLayoutGroup>();
            altVlg.padding = UITheme.Padding(UITheme.Spacing.Xs, UITheme.Spacing.Xxs);
            altVlg.spacing = UITheme.Spacing.Xxs;
            altVlg.childForceExpandWidth = true;
            altVlg.childForceExpandHeight = false;
            altObj.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _routeAlternativesContainer = altObj.transform;

            Sep(_panel.transform);

            SectionHeader(
                _panel.transform,
                "USTAWIENIA",
                LocalizationService.Get("timetable.creator.section.parameters"),
                "Ustal parametry kursu, dni obowiazywania i sposob numeracji przed wygenerowaniem postoju.");

            // M-TimetableUX F1.16: progressive disclosure mode toggle (Basic/Advanced/Expert).
            // Service layer (PlayerProgressService) tracks unlock conditions + per-save preference.
            // Pre-F1.16 polish: dropdown samodzielne (UI gating per-element TBD).
            var rMode = Row(_panel.transform);
            StyleRow(rMode);
            Lbl(rMode.transform, "Tryb interfejsu:", 12, new Color(0.7f, 0.7f, 0.7f));
            BuildUIModeToggle(rMode.transform);

            var r3 = Row(_panel.transform);
            StyleRow(r3);
            Lbl(r3.transform, LocalizationService.Get("timetable.creator.params.category"), 12, new Color(0.7f, 0.7f, 0.7f));
            _categoryDropdown = MakeDropdown(r3.transform);

            var r4 = Row(_panel.transform);
            StyleRow(r4);
            Lbl(r4.transform, LocalizationService.Get("timetable.creator.params.mode"), 12, new Color(0.7f, 0.7f, 0.7f));
            _emuToggle = MakeToggle(r4.transform, LocalizationService.Get("timetable.creator.params.emu_dmu"), true);
            _emuToggle.onValueChanged.AddListener(v =>
                _compositionMode = v ? CompositionMode.MultipleUnit : CompositionMode.LocoWithCars);

            Lbl(r4.transform, LocalizationService.Get("timetable.creator.params.consist"), 12, new Color(0.7f, 0.7f, 0.7f));
            _assignmentDropdown = MakeAssignmentDropdown(r4.transform);
            _assignmentDropdown.onValueChanged.AddListener(v =>
            {
                _compositionAssignment = v == 0
                    ? CompositionAssignment.Symbolic
                    : CompositionAssignment.Concrete;
                if (_compositionAssignment == CompositionAssignment.Concrete)
                    Log.Info("[TimetableCreator] Concrete composition wybrane — pełne przypisanie taboru w M5 Obiegi.");
            });

            var r5 = Row(_panel.transform);
            StyleRow(r5);
            Lbl(r5.transform, LocalizationService.Get("timetable.creator.params.vmax"), 12, new Color(0.7f, 0.7f, 0.7f));
            _vmaxInput = Inp(r5.transform, "120", 55);
            Lbl(r5.transform, LocalizationService.Get("timetable.creator.params.kmh"), 12, new Color(0.5f, 0.5f, 0.5f));

            var r5b = Row(_panel.transform);
            StyleRow(r5b);
            Lbl(r5b.transform, LocalizationService.Get("timetable.creator.params.start_date"), 12, new Color(0.7f, 0.7f, 0.7f));
            _startDateInput = Inp(r5b.transform, DefaultStartDate(), 110, LocalizationService.Get("timetable.creator.params.start_date_placeholder"));
            Lbl(r5b.transform, LocalizationService.Get("timetable.creator.params.start_time"), 12, new Color(0.7f, 0.7f, 0.7f));
            _startTimeInput = Inp(r5b.transform, "06:00", 65, LocalizationService.Get("timetable.creator.params.start_time_placeholder"));
            Lbl(r5b.transform, LocalizationService.Get("timetable.creator.params.weeks_valid"), 12, new Color(0.7f, 0.7f, 0.7f));
            _weeksValidInput = Inp(r5b.transform, "4", 40);
            Lbl(r5b.transform, LocalizationService.Get("timetable.creator.params.weeks_valid_hint"), 11, new Color(0.5f, 0.5f, 0.5f));

            // M-TimetableUX 2026-05-11: settings dla K-shortest paths (liczba alternatyw).
            var rAlt = Row(_panel.transform);
            StyleRow(rAlt);
            Lbl(rAlt.transform, "Alternatywy tras:", 12, new Color(0.7f, 0.7f, 0.7f));
            var altKInput = Inp(rAlt.transform, _alternativesK.ToString(), 45);
            altKInput.onValueChanged.AddListener(v => {
                if (int.TryParse(v, out int k) && k >= 1 && k <= 10) {
                    _alternativesK = k;
                    ScheduleBackgroundAlternativesGeneration();
                }
            });
            Lbl(rAlt.transform, "max długość:", 11, new Color(0.7f, 0.7f, 0.7f));
            var altRatioInput = Inp(rAlt.transform, _alternativesMaxRatio.ToString("F2"), 50);
            altRatioInput.onValueChanged.AddListener(v => {
                if (float.TryParse(v, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float r) && r >= 1.0f && r <= 5f) {
                    _alternativesMaxRatio = r;
                    ScheduleBackgroundAlternativesGeneration();
                }
            });
            Lbl(rAlt.transform, "× najkrótszej", 11, new Color(0.5f, 0.5f, 0.5f));

            var rTakt = Row(_panel.transform);
            StyleRow(rTakt);
            Lbl(rTakt.transform, LocalizationService.Get("timetable.creator.params.takt_label"), 12, new Color(0.7f, 0.7f, 0.7f));
            _taktToggle = MakeToggle(rTakt.transform, LocalizationService.Get("timetable.creator.params.takt_toggle"), false);
            _taktToggle.onValueChanged.AddListener(OnTaktToggleChanged);

            _taktRow = Row(_panel.transform);
            StyleRow(_taktRow);
            Lbl(_taktRow.transform, LocalizationService.Get("timetable.creator.params.takt_every"), 11, new Color(0.7f, 0.7f, 0.7f));
            _taktIntervalInput = Inp(_taktRow.transform, "60", 45);
            Lbl(_taktRow.transform, LocalizationService.Get("timetable.creator.params.takt_until"), 11, new Color(0.7f, 0.7f, 0.7f));
            _taktLastInput = Inp(_taktRow.transform, "22:00", 60, LocalizationService.Get("timetable.creator.params.takt_until_placeholder"));
            _taktIntervalInput.onValueChanged.AddListener(_ => UpdateTaktSummary());
            _taktLastInput.onValueChanged.AddListener(_ => UpdateTaktSummary());
            _taktSummary = Lbl(_taktRow.transform, "", 10, new Color(0.6f, 0.85f, 0.6f));
            var taktSumLe = _taktSummary.gameObject.GetComponent<LayoutElement>();
            if (taktSumLe != null)
            {
                taktSumLe.preferredWidth = 0;
                taktSumLe.flexibleWidth = 1;
            }
            UITheme.ApplySurface(
                _taktSummary.gameObject.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.28f),
                UIShapePreset.Inset);
            _taktRow.SetActive(false);

            var rNum = Row(_panel.transform);
            StyleRow(rNum);
            Lbl(rNum.transform, LocalizationService.Get("timetable.creator.params.train_number"), 12, new Color(0.7f, 0.7f, 0.7f));
            _trainNumberInput = Inp(rNum.transform, "", 100, LocalizationService.Get("timetable.creator.params.train_number_placeholder"));
            _trainNumberInput.onValueChanged.AddListener(_ =>
            {
                _trainNumberOverrideAccepted = false;
                ValidateTrainNumberUI();
            });
            Btn(
                rNum.transform,
                LocalizationService.Get("timetable.creator.params.train_number_auto"),
                AutoGenTrainNumber,
                new Color(0.3f, 0.5f, 0.7f),
                50);
            _trainNumberStatus = Lbl(rNum.transform, "", 10, new Color(0.6f, 0.6f, 0.6f));
            var statusLe = _trainNumberStatus.gameObject.GetComponent<LayoutElement>();
            if (statusLe != null)
            {
                statusLe.preferredWidth = 0;
                statusLe.flexibleWidth = 1;
            }
            UITheme.ApplySurface(
                _trainNumberStatus.gameObject.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.28f),
                UIShapePreset.Inset);

            var rDays = Row(_panel.transform);
            StyleRow(rDays);
            Lbl(rDays.transform, LocalizationService.Get("timetable.creator.params.days_label"), 12, new Color(0.7f, 0.7f, 0.7f));
            string[] dayKeys =
            {
                "timetable.creator.day_short.mon",
                "timetable.creator.day_short.tue",
                "timetable.creator.day_short.wed",
                "timetable.creator.day_short.thu",
                "timetable.creator.day_short.fri",
                "timetable.creator.day_short.sat",
                "timetable.creator.day_short.sun"
            };
            for (int i = 0; i < 7; i++)
                _dayToggles[i] = MakeDayToggle(rDays.transform, LocalizationService.Get(dayKeys[i]), true);

            Btn(
                rDays.transform,
                LocalizationService.Get("timetable.creator.params.preset_weekdays"),
                () => SetDayMask(DayMask.Weekdays),
                new Color(0.25f, 0.4f, 0.55f),
                60);
            Btn(
                rDays.transform,
                LocalizationService.Get("timetable.creator.params.preset_weekend"),
                () => SetDayMask(DayMask.Weekend),
                new Color(0.25f, 0.4f, 0.55f),
                60);
            Btn(
                rDays.transform,
                LocalizationService.Get("timetable.creator.params.preset_everyday"),
                () => SetDayMask(DayMask.EveryDay),
                new Color(0.25f, 0.4f, 0.55f),
                50);

            var actionRow = Row(_panel.transform);
            StyleRow(actionRow);
            _generateRouteBtn = Btn(
                actionRow.transform,
                LocalizationService.Get("timetable.creator.button.generate_route"),
                GenerateRoute,
                new Color(0.3f, 0.5f, 0.8f));
            _generateRouteBtn.interactable = false;
            _computeBtn = Btn(
                actionRow.transform,
                LocalizationService.Get("timetable.creator.button.compute_stops"),
                ComputeStops,
                new Color(0.2f, 0.6f, 0.4f));
            _computeBtn.interactable = false;
            Sep(_panel.transform);

            SectionHeader(
                _panel.transform,
                "POSTOJE",
                LocalizationService.Get("timetable.creator.button.compute_stops"),
                "Tutaj pojawi sie lista postojow, czasy przejazdu i ewentualne konflikty do poprawienia.");

            var stopsScrollView = new GameObject("StopsScrollView", typeof(RectTransform));
            stopsScrollView.transform.SetParent(_panel.transform, false);
            var stopsLe = stopsScrollView.AddComponent<LayoutElement>();
            stopsLe.flexibleHeight = 1;
            stopsLe.minHeight = 120;
            var stopsImg = stopsScrollView.AddComponent<Image>();
            UITheme.ApplySurface(stopsImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Panel);
            stopsImg.raycastTarget = true;
            var stopsScrollRect = stopsScrollView.AddComponent<ScrollRect>();
            stopsScrollRect.horizontal = false;
            stopsScrollRect.vertical = true;
            stopsScrollRect.movementType = ScrollRect.MovementType.Elastic;
            stopsScrollRect.scrollSensitivity = 25f;

            var stopsViewport = new GameObject("Viewport", typeof(RectTransform));
            stopsViewport.transform.SetParent(stopsScrollView.transform, false);
            var stopsVpRt = (RectTransform)stopsViewport.transform;
            stopsVpRt.anchorMin = Vector2.zero;
            stopsVpRt.anchorMax = Vector2.one;
            stopsVpRt.offsetMin = Vector2.zero;
            stopsVpRt.offsetMax = Vector2.zero;
            var stopsVpImg = stopsViewport.AddComponent<Image>();
            UITheme.ApplySurface(stopsVpImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.14f), UIShapePreset.Inset);
            var stopsMask = stopsViewport.AddComponent<Mask>();
            stopsMask.showMaskGraphic = false;

            var stopsContentGo = new GameObject("Content", typeof(RectTransform));
            stopsContentGo.transform.SetParent(stopsViewport.transform, false);
            var stopsCRt = (RectTransform)stopsContentGo.transform;
            stopsCRt.anchorMin = new Vector2(0, 1);
            stopsCRt.anchorMax = new Vector2(1, 1);
            stopsCRt.pivot = new Vector2(0.5f, 1f);
            stopsCRt.anchoredPosition = Vector2.zero;
            stopsCRt.sizeDelta = Vector2.zero;
            var stopsCVlg = stopsContentGo.AddComponent<VerticalLayoutGroup>();
            stopsCVlg.padding = UITheme.Padding(UITheme.Spacing.Xs);
            stopsCVlg.spacing = UITheme.Spacing.Xxs;
            stopsCVlg.childForceExpandWidth = true;
            stopsCVlg.childForceExpandHeight = false;
            stopsCVlg.childAlignment = TextAnchor.UpperLeft;
            var stopsCsf = stopsContentGo.AddComponent<ContentSizeFitter>();
            stopsCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            stopsScrollRect.viewport = stopsVpRt;
            stopsScrollRect.content = stopsCRt;
            _stopsContent = stopsContentGo.transform;

            Sep(_panel.transform);
            _summaryText = Lbl(_panel.transform, "", 12, new Color(0.6f, 0.9f, 0.6f));
            UITheme.ApplySurface(
                _summaryText.gameObject.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.TopBarInset, 0.88f),
                UIShapePreset.Inset);

            var bRow = Row(_panel.transform);
            StyleRow(bRow);
            Btn(
                bRow.transform,
                LocalizationService.Get("timetable.creator.button.back_to_list"),
                CancelAll,
                new Color(0.3f, 0.3f, 0.4f));
            _confirmBtn = Btn(
                bRow.transform,
                LocalizationService.Get("timetable.creator.button.confirm"),
                Confirm,
                new Color(0.2f, 0.7f, 0.3f));
            _confirmBtn.interactable = false;
            _forceConfirmBtn = Btn(
                bRow.transform,
                LocalizationService.Get("timetable.creator.button.force_confirm"),
                Confirm,
                new Color(0.8f, 0.5f, 0.1f));
            _forceConfirmBtn.gameObject.SetActive(false);

            _panel.SetActive(false);
        }

        GameObject SectionHeader(Transform parent, string eyebrow, string title, string description)
        {
            var card = new GameObject("SectionHeader");
            card.transform.SetParent(parent, false);
            card.AddComponent<RectTransform>();
            UITheme.ApplySurface(
                card.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f),
                UIShapePreset.Panel);
            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Md);
            vlg.spacing = UITheme.Spacing.Xxs;
            // childControlHeight MUSI byc true — inaczej VLG nie ustawia wysokosci labelom
            // (rect ~0) i eyebrow/tytul/opis renderuja sie na sobie (bug nakladania sekcji).
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var le = card.AddComponent<LayoutElement>();
            // Wysokosc dopasowana do realnie ulozonej tresci (eyebrow+tytul[+opis]+padding+spacing).
            le.preferredHeight = string.IsNullOrEmpty(description) ? 62 : 84;

            var eye = Lbl(card.transform, eyebrow, 10, UITheme.PrimaryAccent);
            eye.fontStyle = FontStyles.Bold;
            var titleText = Lbl(card.transform, title, 13, Color.white);
            titleText.fontStyle = FontStyles.Bold;

            if (!string.IsNullOrEmpty(description))
            {
                var body = Lbl(card.transform, description, 11, UITheme.SecondaryText);
                body.textWrappingMode = TextWrappingModes.Normal;
            }

            return card;
        }

        Transform MakeSuggestionsContainer(Transform parent)
        {
            var obj = new GameObject("Suggestions");
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();
            var vlg2 = obj.AddComponent<VerticalLayoutGroup>();
            vlg2.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vlg2.spacing = UITheme.Spacing.Xs;
            vlg2.childControlWidth = true;
            vlg2.childControlHeight = true; // ContentSizeFitter + brak nakladania wierszy sugestii
            vlg2.childForceExpandWidth = true;
            vlg2.childForceExpandHeight = false;
            var csf = obj.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var bg = obj.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.92f), UIShapePreset.Panel);
            obj.SetActive(false);
            return obj.transform;
        }

        TextMeshProUGUI Lbl(Transform p, string t, int sz, Color c)
        {
            var o = new GameObject("L");
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredHeight = sz + 6;
            le.flexibleHeight = 0;
            var tx = o.AddComponent<TextMeshProUGUI>();
            tx.fontSize = sz;
            tx.text = t;
            tx.textWrappingMode = TextWrappingModes.Normal;
            tx.overflowMode = TextOverflowModes.Overflow;
            UITheme.ApplyTmpText(tx, c == Color.white ? UIThemeTextRole.Primary : UIThemeTextRole.Secondary);
            tx.color = c;
            int len = t?.Length ?? 0;
            le.preferredWidth = Mathf.Max(16f, len * sz * 0.55f + 8f);
            return tx;
        }

        GameObject Row(Transform p)
        {
            var o = new GameObject("R");
            o.transform.SetParent(p, false);
            o.AddComponent<RectTransform>();
            var h = o.AddComponent<HorizontalLayoutGroup>();
            h.spacing = UITheme.Spacing.Sm;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;
            var le = o.AddComponent<LayoutElement>();
            le.preferredHeight = 32;
            le.flexibleHeight = 0;
            return o;
        }

        void StyleRow(GameObject row)
        {
            if (row == null)
                return;

            UITheme.ApplySurface(
                row.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.TopBarInset, 0.92f),
                UIShapePreset.Inset);

            var h = row.GetComponent<HorizontalLayoutGroup>();
            if (h != null)
                h.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Xs);

            var le = row.GetComponent<LayoutElement>();
            if (le != null && le.preferredHeight < 34)
                le.preferredHeight = 34;
        }

        void Sep(Transform p)
        {
            var o = new GameObject("S");
            o.transform.SetParent(p, false);
            o.AddComponent<LayoutElement>().preferredHeight = 1;
            var img = o.AddComponent<Image>();
            img.color = UITheme.WithAlpha(UITheme.Border, 0.45f);
        }

        Button Btn(Transform p, string label, System.Action onClick, Color bg, float w = -1)
        {
            var o = new GameObject(label);
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            if (w > 0) le.preferredWidth = w;
            else le.flexibleWidth = 1;
            le.preferredHeight = 32;
            var img = o.AddComponent<Image>();
            var btn = o.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());
            UITheme.ApplyButtonStyle(btn, img, UIButtonTone.Primary, UIShapePreset.Pill);
            UITheme.ApplySurface(img, bg, UIShapePreset.Pill);
            var l = new GameObject("L");
            l.transform.SetParent(o.transform, false);
            var lrt = l.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            var tx = l.AddComponent<TextMeshProUGUI>();
            tx.fontSize = 12;
            tx.alignment = TextAlignmentOptions.Center;
            tx.text = label;
            UITheme.ApplyTmpText(tx, UIThemeTextRole.Inverse);
            return btn;
        }

        TMP_InputField Inp(Transform p, string def, float w, string placeholder = null)
        {
            var o = new GameObject("I");
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 30;
            var bg = o.AddComponent<Image>();

            // TMP_InputField requires Viewport (RectMask2D) + Text/Placeholder as children of viewport.
            var viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(o.transform, false);
            var viewportRt = viewportObj.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = new Vector2(4, 0);
            viewportRt.offsetMax = new Vector2(-4, 0);
            viewportObj.AddComponent<RectMask2D>();

            var t = new GameObject("T");
            t.transform.SetParent(viewportObj.transform, false);
            var trt = t.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var tx = t.AddComponent<TextMeshProUGUI>();
            tx.fontSize = 12;
            tx.alignment = TextAlignmentOptions.MidlineLeft;
            tx.textWrappingMode = TextWrappingModes.NoWrap;
            tx.raycastTarget = false;

            var inp = o.AddComponent<TMP_InputField>();
            inp.textViewport = viewportRt;
            inp.textComponent = tx;
            inp.lineType = TMP_InputField.LineType.SingleLine;
            inp.text = def;

            if (!string.IsNullOrEmpty(placeholder))
            {
                var ph = new GameObject("PH");
                ph.transform.SetParent(viewportObj.transform, false);
                var phrt = ph.AddComponent<RectTransform>();
                phrt.anchorMin = Vector2.zero;
                phrt.anchorMax = Vector2.one;
                phrt.offsetMin = Vector2.zero;
                phrt.offsetMax = Vector2.zero;
                var phtx = ph.AddComponent<TextMeshProUGUI>();
                phtx.fontSize = 12;
                phtx.fontStyle = FontStyles.Italic;
                phtx.alignment = TextAlignmentOptions.MidlineLeft;
                phtx.textWrappingMode = TextWrappingModes.NoWrap;
                phtx.raycastTarget = false;
                phtx.text = placeholder;
                inp.placeholder = phtx;
                UITheme.ApplyTmpInputField(inp, bg, tx, phtx);
            }
            else
            {
                UITheme.ApplyTmpInputField(inp, bg, tx, null);
            }

            return inp;
        }

        TMP_Dropdown MakeTrackDropdown(Transform parent, List<StationTrackInfo> tracks, float w)
        {
            var o = new GameObject("TrackDD");
            o.transform.SetParent(parent, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 28;
            UITheme.ApplySurface(o.AddComponent<Image>(), UITheme.TopBarInset, UIShapePreset.Inset);

            var cap = new GameObject("Cap");
            cap.transform.SetParent(o.transform, false);
            var crt = cap.AddComponent<RectTransform>();
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(3, 0);
            crt.offsetMax = new Vector2(-12, 0);
            var ctx = cap.AddComponent<TextMeshProUGUI>();
            ctx.fontSize = 10;
            ctx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(ctx, UIThemeTextRole.Primary);

            const float itemH = 20f;
            int maxVisible = tracks.Count > 15 ? 12 : 8;
            float dropH = Mathf.Min(tracks.Count * itemH, maxVisible * itemH);

            var tmpl = new GameObject("Template");
            tmpl.transform.SetParent(o.transform, false);
            var trt = tmpl.AddComponent<RectTransform>();
            trt.anchorMin = new Vector2(0, 0);
            trt.anchorMax = new Vector2(1, 0);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(0, dropH);
            UITheme.ApplySurface(tmpl.AddComponent<Image>(), UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.Panel);

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(tmpl.transform, false);
            var vrt = viewport.AddComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
            UITheme.ApplySurface(viewport.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.1f), UIShapePreset.Inset);
            var mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var cnrt = content.AddComponent<RectTransform>();
            cnrt.anchorMin = new Vector2(0, 1);
            cnrt.anchorMax = new Vector2(1, 1);
            cnrt.pivot = new Vector2(0.5f, 1f);
            cnrt.sizeDelta = new Vector2(0, itemH);

            var item = new GameObject("Item");
            item.transform.SetParent(content.transform, false);
            var irt = item.AddComponent<RectTransform>();
            irt.anchorMin = new Vector2(0, 0.5f);
            irt.anchorMax = new Vector2(1, 0.5f);
            irt.sizeDelta = new Vector2(0, itemH);
            var itemBg = new GameObject("Item Background");
            itemBg.transform.SetParent(item.transform, false);
            var ibgRt = itemBg.AddComponent<RectTransform>();
            ibgRt.anchorMin = Vector2.zero;
            ibgRt.anchorMax = Vector2.one;
            ibgRt.offsetMin = Vector2.zero;
            ibgRt.offsetMax = Vector2.zero;
            var itemBgImage = itemBg.AddComponent<Image>();
            UITheme.ApplySurface(itemBgImage, UITheme.WithAlpha(UITheme.SecondarySurface, 0.7f), UIShapePreset.Inset);
            var toggle = item.AddComponent<Toggle>();
            toggle.targetGraphic = itemBgImage;
            toggle.isOn = true;
            toggle.colors = UITheme.CreateColorBlock(
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.7f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.92f),
                UITheme.WithAlpha(UITheme.Border, 0.94f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.92f),
                UITheme.WithAlpha(UITheme.Border, 0.45f));

            var ilbl = new GameObject("Item Label");
            ilbl.transform.SetParent(item.transform, false);
            var ilrt = ilbl.AddComponent<RectTransform>();
            ilrt.anchorMin = Vector2.zero;
            ilrt.anchorMax = Vector2.one;
            ilrt.offsetMin = new Vector2(4, 0);
            ilrt.offsetMax = Vector2.zero;
            var iltx = ilbl.AddComponent<TextMeshProUGUI>();
            iltx.fontSize = 10;
            iltx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(iltx, UIThemeTextRole.Primary);

            var sr = tmpl.AddComponent<ScrollRect>();
            sr.viewport = vrt;
            sr.content = cnrt;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 15f;

            tmpl.SetActive(false);

            var dd = o.AddComponent<TMP_Dropdown>();
            dd.captionText = ctx;
            dd.itemText = iltx;
            dd.template = trt;

            var opts = new List<TMP_Dropdown.OptionData>();
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                string label = $"tor {t.trackRef}";
                if (t.hasPlatform) label += " (per.)";
                opts.Add(new TMP_Dropdown.OptionData(label));
            }
            dd.options = opts;
            dd.value = 0;
            dd.RefreshShownValue();
            return dd;
        }

        Toggle MakeDayToggle(Transform p, string label, bool isOn)
        {
            var o = new GameObject("DayTgl");
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredWidth = 32;
            le.preferredHeight = 26;
            var hh = o.AddComponent<HorizontalLayoutGroup>();
            hh.spacing = UITheme.Spacing.Xxs;
            hh.padding = UITheme.Padding(0f, UITheme.Spacing.Xxs, 0f, 0f);
            hh.childForceExpandWidth = false;
            hh.childForceExpandHeight = true;
            hh.childAlignment = TextAnchor.MiddleLeft;

            var bg = new GameObject("Bg");
            bg.transform.SetParent(o.transform, false);
            bg.AddComponent<LayoutElement>().preferredWidth = 12;
            var bgRt = bg.GetComponent<RectTransform>();
            bgRt.sizeDelta = new Vector2(12, 12);
            var bgImg = bg.AddComponent<Image>();
            UITheme.ApplySurface(bgImg, UITheme.TopBarInset, UIShapePreset.Inset);

            var ch = new GameObject("Ch");
            ch.transform.SetParent(bg.transform, false);
            var chRt = ch.AddComponent<RectTransform>();
            chRt.anchorMin = new Vector2(0.15f, 0.15f);
            chRt.anchorMax = new Vector2(0.85f, 0.85f);
            chRt.offsetMin = Vector2.zero;
            chRt.offsetMax = Vector2.zero;
            var chImg = ch.AddComponent<Image>();
            UITheme.ApplySurface(chImg, UITheme.PrimaryAccent, UIShapePreset.Inset);

            var lb = new GameObject("L");
            lb.transform.SetParent(o.transform, false);
            lb.AddComponent<LayoutElement>().preferredWidth = 16;
            var lx = lb.AddComponent<TextMeshProUGUI>();
            lx.fontSize = 10;
            lx.text = label;
            lx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(lx, UIThemeTextRole.Secondary);

            var tgl = o.AddComponent<Toggle>();
            tgl.isOn = isOn;
            tgl.targetGraphic = bgImg;
            tgl.graphic = chImg;
            tgl.colors = UITheme.CreateColorBlock(
                UITheme.TopBarInset,
                UITheme.SecondarySurface,
                UITheme.RaisedSurface,
                UITheme.SecondarySurface,
                UITheme.WithAlpha(UITheme.Border, 0.45f));
            return tgl;
        }

        /// <summary>
        /// M-TimetableUX F1.4: Dropdown dla StopType per stop (PH/PT/ZD/Transit).
        /// Replaces 2-state toggle. Filtered per <see cref="StopTypeValidator.GetAllowedTypes"/>
        /// (per location × hasPlatform).
        /// </summary>
        TMP_Dropdown MakeStopTypeDropdown(Transform parent, System.Collections.Generic.IReadOnlyList<StopType> allowed, StopType current, float w = 60f)
        {
            var o = new GameObject("StopTypeDD");
            o.transform.SetParent(parent, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredWidth = w;
            le.preferredHeight = 24;
            UITheme.ApplySurface(o.AddComponent<Image>(), UITheme.TopBarInset, UIShapePreset.Inset);

            var cap = new GameObject("Cap");
            cap.transform.SetParent(o.transform, false);
            var crt = cap.AddComponent<RectTransform>();
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(3, 0);
            crt.offsetMax = new Vector2(-12, 0);
            var ctx = cap.AddComponent<TextMeshProUGUI>();
            ctx.fontSize = 10;
            ctx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(ctx, UIThemeTextRole.Primary);

            const float itemH = 22f;
            float dropH = allowed.Count * itemH;

            var tmpl = new GameObject("Template");
            tmpl.transform.SetParent(o.transform, false);
            var trt = tmpl.AddComponent<RectTransform>();
            trt.anchorMin = new Vector2(0, 0);
            trt.anchorMax = new Vector2(1, 0);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(0, dropH);
            UITheme.ApplySurface(tmpl.AddComponent<Image>(), UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.Panel);

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(tmpl.transform, false);
            var vrt = viewport.AddComponent<RectTransform>();
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = Vector2.zero;
            vrt.offsetMax = Vector2.zero;
            viewport.AddComponent<Image>(); // mask graphic
            viewport.AddComponent<UnityEngine.UI.Mask>();

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var cort = content.AddComponent<RectTransform>();
            cort.anchorMin = new Vector2(0, 1);
            cort.anchorMax = new Vector2(1, 1);
            cort.pivot = new Vector2(0.5f, 1f);
            cort.sizeDelta = new Vector2(0, dropH);

            var item = new GameObject("Item");
            item.transform.SetParent(content.transform, false);
            var irt = item.AddComponent<RectTransform>();
            irt.anchorMin = new Vector2(0, 0.5f);
            irt.anchorMax = new Vector2(1, 0.5f);
            irt.sizeDelta = new Vector2(0, itemH);
            var itoggle = item.AddComponent<Toggle>();
            itoggle.targetGraphic = item.AddComponent<Image>();
            UITheme.ApplySurface(itoggle.targetGraphic as Image, UITheme.WithAlpha(UITheme.Border, 0.0f), UIShapePreset.Inset);

            var itemBg = new GameObject("ItemBg");
            itemBg.transform.SetParent(item.transform, false);
            var ibgRt = itemBg.AddComponent<RectTransform>();
            ibgRt.anchorMin = Vector2.zero;
            ibgRt.anchorMax = Vector2.one;
            ibgRt.offsetMin = Vector2.zero;
            ibgRt.offsetMax = Vector2.zero;
            var ibgImg = itemBg.AddComponent<Image>();
            UITheme.ApplySurface(ibgImg, UITheme.WithAlpha(UITheme.RaisedSurface, 0.6f), UIShapePreset.Inset);
            itoggle.graphic = ibgImg;

            var itemLabel = new GameObject("Label");
            itemLabel.transform.SetParent(item.transform, false);
            var ilrt = itemLabel.AddComponent<RectTransform>();
            ilrt.anchorMin = Vector2.zero;
            ilrt.anchorMax = Vector2.one;
            ilrt.offsetMin = new Vector2(6, 0);
            ilrt.offsetMax = Vector2.zero;
            var iltxt = itemLabel.AddComponent<TextMeshProUGUI>();
            iltxt.fontSize = 10;
            iltxt.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(iltxt, UIThemeTextRole.Primary);

            tmpl.SetActive(false);

            var dd = o.AddComponent<TMP_Dropdown>();
            dd.targetGraphic = o.GetComponent<Image>();
            dd.captionText = ctx;
            dd.template = trt;
            dd.itemText = iltxt;

            var optList = new System.Collections.Generic.List<TMP_Dropdown.OptionData>();
            int currentIdx = 0;
            for (int i = 0; i < allowed.Count; i++)
            {
                optList.Add(new TMP_Dropdown.OptionData(StopTypeValidator.ShortCode(allowed[i])));
                if (allowed[i] == current) currentIdx = i;
            }
            dd.options = optList;
            dd.value = currentIdx;
            dd.RefreshShownValue();

            return dd;
        }

        void MakeStopRow(Transform p, string text, bool hl)
        {
            var o = new GameObject("Stop");
            o.transform.SetParent(p, false);
            o.AddComponent<LayoutElement>().preferredHeight = 20;
            var bg = o.AddComponent<Image>();
            UITheme.ApplySurface(
                bg,
                hl ? UITheme.WithAlpha(UITheme.RaisedSurface, 0.82f) : UITheme.WithAlpha(UITheme.SecondarySurface, 0.32f),
                UIShapePreset.Inset);

            // Text na osobnym GO (Image + Text na jednym GO → NRE, patrz commit 2a3907e)
            var txtObj = new GameObject("Text", typeof(RectTransform));
            txtObj.transform.SetParent(o.transform, false);
            var txtRt = (RectTransform)txtObj.transform;
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(6, 0);
            txtRt.offsetMax = new Vector2(-6, 0);
            var tx = txtObj.AddComponent<TextMeshProUGUI>();
            tx.fontSize = 11;
            tx.text = text;
            tx.alignment = TextAlignmentOptions.MidlineLeft;
            tx.raycastTarget = false;
            UITheme.ApplyTmpText(tx, hl ? UIThemeTextRole.Primary : UIThemeTextRole.Secondary);
        }

        TMP_Dropdown MakeDropdown(Transform p)
        {
            var o = new GameObject("DD");
            o.transform.SetParent(p, false);
            o.AddComponent<LayoutElement>().flexibleWidth = 1;
            UITheme.ApplySurface(o.AddComponent<Image>(), UITheme.TopBarInset, UIShapePreset.Inset);

            var cap = new GameObject("Cap");
            cap.transform.SetParent(o.transform, false);
            var crt = cap.AddComponent<RectTransform>();
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(4, 0);
            crt.offsetMax = new Vector2(-16, 0);
            var ctx = cap.AddComponent<TextMeshProUGUI>();
            ctx.fontSize = 12;
            ctx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(ctx, UIThemeTextRole.Primary);

            var tmpl = new GameObject("Tmpl");
            tmpl.transform.SetParent(o.transform, false);
            var trt = tmpl.AddComponent<RectTransform>();
            trt.anchorMin = new Vector2(0, 0);
            trt.anchorMax = new Vector2(1, 0);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(0, 90);
            UITheme.ApplySurface(tmpl.AddComponent<Image>(), UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.Panel);
            tmpl.SetActive(false);

            var item = new GameObject("Item");
            item.transform.SetParent(tmpl.transform, false);
            var irt = item.AddComponent<RectTransform>();
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = new Vector2(1, 0);
            irt.sizeDelta = new Vector2(0, 22);
            var itemBg = item.AddComponent<Image>();
            UITheme.ApplySurface(itemBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);
            var toggle = item.AddComponent<Toggle>();
            toggle.targetGraphic = itemBg;
            toggle.colors = UITheme.CreateColorBlock(
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.9f),
                UITheme.WithAlpha(UITheme.Border, 0.92f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.9f),
                UITheme.WithAlpha(UITheme.Border, 0.45f));

            var ilbl = new GameObject("IL");
            ilbl.transform.SetParent(item.transform, false);
            var ilrt = ilbl.AddComponent<RectTransform>();
            ilrt.anchorMin = Vector2.zero;
            ilrt.anchorMax = Vector2.one;
            ilrt.offsetMin = new Vector2(4, 0);
            ilrt.offsetMax = Vector2.zero;
            var iltx = ilbl.AddComponent<TextMeshProUGUI>();
            iltx.fontSize = 12;
            iltx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(iltx, UIThemeTextRole.Primary);

            var dd = o.AddComponent<TMP_Dropdown>();
            dd.captionText = ctx;
            dd.itemText = iltx;
            dd.template = trt;

            var opts = new List<TMP_Dropdown.OptionData>();
            foreach (var c in TimetableService.CommercialCategories)
                opts.Add(new TMP_Dropdown.OptionData($"{c.shortCode} — {c.displayName}"));
            dd.options = opts;
            return dd;
        }

        TMP_Dropdown MakeAssignmentDropdown(Transform p)
        {
            var o = new GameObject("AssignmentDD");
            o.transform.SetParent(p, false);
            var le = o.AddComponent<LayoutElement>();
            le.preferredWidth = 110;
            le.preferredHeight = 30;
            UITheme.ApplySurface(o.AddComponent<Image>(), UITheme.TopBarInset, UIShapePreset.Inset);

            var cap = new GameObject("Cap");
            cap.transform.SetParent(o.transform, false);
            var crt = cap.AddComponent<RectTransform>();
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(4, 0);
            crt.offsetMax = new Vector2(-12, 0);
            var ctx = cap.AddComponent<TextMeshProUGUI>();
            ctx.fontSize = 11;
            ctx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(ctx, UIThemeTextRole.Primary);

            var tmpl = new GameObject("Tmpl");
            tmpl.transform.SetParent(o.transform, false);
            var trt = tmpl.AddComponent<RectTransform>();
            trt.anchorMin = new Vector2(0, 0);
            trt.anchorMax = new Vector2(1, 0);
            trt.pivot = new Vector2(0.5f, 1f);
            trt.sizeDelta = new Vector2(0, 50);
            UITheme.ApplySurface(tmpl.AddComponent<Image>(), UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.Panel);
            tmpl.SetActive(false);

            var item = new GameObject("Item");
            item.transform.SetParent(tmpl.transform, false);
            var irt = item.AddComponent<RectTransform>();
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = new Vector2(1, 0);
            irt.sizeDelta = new Vector2(0, 22);
            var itemBg = item.AddComponent<Image>();
            UITheme.ApplySurface(itemBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);
            var toggle = item.AddComponent<Toggle>();
            toggle.targetGraphic = itemBg;
            toggle.colors = UITheme.CreateColorBlock(
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.9f),
                UITheme.WithAlpha(UITheme.Border, 0.92f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.9f),
                UITheme.WithAlpha(UITheme.Border, 0.45f));

            var ilbl = new GameObject("IL");
            ilbl.transform.SetParent(item.transform, false);
            var ilrt = ilbl.AddComponent<RectTransform>();
            ilrt.anchorMin = Vector2.zero;
            ilrt.anchorMax = Vector2.one;
            ilrt.offsetMin = new Vector2(4, 0);
            ilrt.offsetMax = Vector2.zero;
            var iltx = ilbl.AddComponent<TextMeshProUGUI>();
            iltx.fontSize = 11;
            iltx.alignment = TextAlignmentOptions.MidlineLeft;
            UITheme.ApplyTmpText(iltx, UIThemeTextRole.Primary);

            var dd = o.AddComponent<TMP_Dropdown>();
            dd.captionText = ctx;
            dd.itemText = iltx;
            dd.template = trt;
            dd.options = new List<TMP_Dropdown.OptionData>
            {
                new(LocalizationService.Get("timetable.creator.params.consist_symbolic")),
                new(LocalizationService.Get("timetable.creator.params.consist_concrete"))
            };
            dd.value = 0;
            return dd;
        }

        /// <summary>
        /// M-TimetableUX F1.16: build UIMode toggle (Basic/Advanced/Expert) z visibility per
        /// PlayerProgressService unlock conditions. Locked modes nie są w options
        /// (cleaner UX zamiast disabled+grayed).
        /// </summary>
        private void BuildUIModeToggle(Transform parent)
        {
            var dd = MakeAssignmentDropdown(parent); // reuse styling — 110×30 inset

            var options = new System.Collections.Generic.List<TMP_Dropdown.OptionData>();
            var modeMap = new System.Collections.Generic.List<RailwayManager.SharedUI.UIMode>();

            options.Add(new TMP_Dropdown.OptionData("Basic"));
            modeMap.Add(RailwayManager.SharedUI.UIMode.Basic);

            if (RailwayManager.SharedUI.PlayerProgressService.IsAdvancedUnlocked)
            {
                options.Add(new TMP_Dropdown.OptionData("Advanced"));
                modeMap.Add(RailwayManager.SharedUI.UIMode.Advanced);
            }

            if (RailwayManager.SharedUI.PlayerProgressService.IsExpertUnlocked)
            {
                options.Add(new TMP_Dropdown.OptionData("Expert"));
                modeMap.Add(RailwayManager.SharedUI.UIMode.Expert);
            }

            dd.options = options;

            // Initial value = current effective mode (mapped do options index)
            var current = RailwayManager.SharedUI.PlayerProgressService.GetEffectiveMode();
            int initialIdx = modeMap.IndexOf(current);
            if (initialIdx < 0) initialIdx = 0; // fallback Basic
            dd.value = initialIdx;
            dd.RefreshShownValue();

            dd.onValueChanged.AddListener(val =>
            {
                if (val < 0 || val >= modeMap.Count) return;
                RailwayManager.SharedUI.PlayerProgressService.SetPlayerMode(modeMap[val]);
            });

            // Append unlock hint label dla locked modes (Advanced + Expert)
            var hintParts = new System.Collections.Generic.List<string>();
            if (!RailwayManager.SharedUI.PlayerProgressService.IsAdvancedUnlocked)
            {
                int needed = RailwayManager.SharedUI.PlayerProgressService.AdvancedUnlockTimetableCount
                             - RailwayManager.SharedUI.PlayerProgressService.TimetablesCreated;
                hintParts.Add($"Advanced odblokowuje się po {needed} rozkładach");
            }
            if (!RailwayManager.SharedUI.PlayerProgressService.IsExpertUnlocked
                && RailwayManager.SharedUI.PlayerProgressService.IsAdvancedUnlocked)
            {
                hintParts.Add($"Expert: ukończ tutorial lub graj {RailwayManager.SharedUI.PlayerProgressService.ExpertUnlockHours:F0}h");
            }
            if (hintParts.Count > 0)
            {
                var hint = Lbl(parent, string.Join(" • ", hintParts), 10, new Color(0.55f, 0.55f, 0.55f));
                hint.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            }
        }

        Toggle MakeToggle(Transform p, string label, bool on)
        {
            int labelChars = label?.Length ?? 0;
            float labelWidth = Mathf.Max(20f, labelChars * 6.2f + 4f);
            float totalWidth = 18f + 6f + labelWidth;

            var o = new GameObject("Tgl", typeof(RectTransform));
            o.transform.SetParent(p, false);
            var oLe = o.AddComponent<LayoutElement>();
            oLe.preferredWidth = totalWidth;
            oLe.preferredHeight = 28;
            oLe.flexibleWidth = 0;
            oLe.flexibleHeight = 0;

            var hh = o.AddComponent<HorizontalLayoutGroup>();
            hh.spacing = UITheme.Spacing.Sm;
            hh.childForceExpandWidth = false;
            hh.childForceExpandHeight = false;
            hh.childAlignment = TextAnchor.MiddleLeft;

            var bg = new GameObject("Bg", typeof(RectTransform));
            bg.transform.SetParent(o.transform, false);
            var bgLe = bg.AddComponent<LayoutElement>();
            bgLe.preferredWidth = 18;
            bgLe.preferredHeight = 18;
            bgLe.flexibleWidth = 0;
            bgLe.flexibleHeight = 0;
            var bgi = bg.AddComponent<Image>();
            UITheme.ApplySurface(bgi, UITheme.TopBarInset, UIShapePreset.Inset);

            var ch = new GameObject("Ch", typeof(RectTransform));
            ch.transform.SetParent(bg.transform, false);
            var chrt = (RectTransform)ch.transform;
            chrt.anchorMin = new Vector2(0.2f, 0.2f);
            chrt.anchorMax = new Vector2(0.8f, 0.8f);
            chrt.offsetMin = Vector2.zero;
            chrt.offsetMax = Vector2.zero;
            var chi = ch.AddComponent<Image>();
            UITheme.ApplySurface(chi, UITheme.PrimaryAccent, UIShapePreset.Inset);

            var tgl = o.AddComponent<Toggle>();
            tgl.isOn = on;
            tgl.targetGraphic = bgi;
            tgl.graphic = chi;
            tgl.colors = UITheme.CreateColorBlock(
                UITheme.TopBarInset,
                UITheme.SecondarySurface,
                UITheme.RaisedSurface,
                UITheme.SecondarySurface,
                UITheme.WithAlpha(UITheme.Border, 0.45f));

            var lb = new GameObject("L", typeof(RectTransform));
            lb.transform.SetParent(o.transform, false);
            var lbLe = lb.AddComponent<LayoutElement>();
            lbLe.preferredWidth = labelWidth;
            lbLe.preferredHeight = 18;
            lbLe.flexibleWidth = 0;
            var lx = lb.AddComponent<TextMeshProUGUI>();
            lx.fontSize = 11;
            lx.text = label;
            lx.alignment = TextAlignmentOptions.MidlineLeft;
            lx.textWrappingMode = TextWrappingModes.NoWrap;
            UITheme.ApplyTmpText(lx, UIThemeTextRole.Primary);
            return tgl;
        }
    }
}
