using System;
using TMPro;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-4: Edytor indywidualnego harmonogramu — kalendarz miesiaca z override'ami
    /// (urlop/L4/ShiftSwap/ExtraDuty/FreeDay).
    ///
    /// Widok miesieczny (D24). Nav prev/next month + date picker.
    /// Kolory per typ dnia:
    /// - Zielony: zmiana ranna (Morning)
    /// - Niebieski: popoludniowa (Afternoon)
    /// - Czarny: nocna (Night)
    /// - Szary: dzien wolny (off-cycle)
    /// - Fioletowy: urlop (Vacation)
    /// - Czerwony: L4 (SickLeave)
    /// - Pomaranczowy: ShiftSwap (+replacementShift label)
    /// - Zolty: Training (POST-EA, D11)
    ///
    /// Klik dnia → menu override: urlop / shift swap / extra duty / free day / usun.
    ///
    /// Cycle dropdown (5+2, 4+2, 6+2, 7+7) — zmienia baseline.
    /// </summary>
    public class EmployeeScheduleEditorUI : MonoBehaviour
    {
        public static EmployeeScheduleEditorUI Instance { get; private set; }

        Canvas _canvas;
        GameObject _root;
        TextMeshProUGUI _headerText;
        TextMeshProUGUI _cycleInfoText;
        TMP_Dropdown _cycleDropdown;
        TMP_Dropdown _defaultShiftDropdown;

        // Month nav
        int _viewYear;
        int _viewMonth;
        TextMeshProUGUI _monthHeaderText;

        // Day grid (7x6 = 42 cells)
        readonly List<Button> _dayButtons = new();
        readonly List<TextMeshProUGUI> _dayLabels = new();
        readonly List<Image> _dayBackgrounds = new();

        // Context menu
        GameObject _contextMenu;
        string _contextMenuDateIso;

        int _currentEmployeeId = -1;
        bool _isVisible;

        public static EmployeeScheduleEditorUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("EmployeeScheduleEditorUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<EmployeeScheduleEditorUI>();
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

        void OnEnable() { PersonnelService.OnEmployeesChanged += OnDataChanged; }
        void OnDisable() { PersonnelService.OnEmployeesChanged -= OnDataChanged; }
        void OnDataChanged() { if (_isVisible) RefreshCalendar(); }

        void Update()
        {
            if (!_isVisible) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (_contextMenu != null && _contextMenu.activeSelf)
                    _contextMenu.SetActive(false);
                else
                    Hide();
            }
        }

        public void Show(int employeeId)
        {
            _currentEmployeeId = employeeId;

            // Domyslnie biezacy miesiac gry
            try
            {
                var now = IsoTime.ParseDate(GameState.CurrentDateIso);
                _viewYear = now.Year;
                _viewMonth = now.Month;
            }
            catch
            {
                _viewYear = 2026; _viewMonth = 4;
            }

            _root.SetActive(true);
            _isVisible = true;
            RefreshCalendar();
        }

        public void Hide()
        {
            _root.SetActive(false);
            _isVisible = false;
            if (_contextMenu != null) _contextMenu.SetActive(false);
        }

        // ═══ Build UI ═══

        void BuildUI()
        {
            var canvasGo = new GameObject("ScheduleCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 120; // above details (110)
            // MUI-10: standard canvas scaler config (ref 1920×1080, match 0.5)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root");
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRect = _root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero; rootRect.offsetMax = Vector2.zero;
            _root.AddComponent<Image>().color = UITheme.WithAlpha(Color.black, 0.78f);

            var panel = new GameObject("Panel");
            panel.transform.SetParent(_root.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f); panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(900, 720);
            panelRect.anchoredPosition = Vector2.zero;
            var panelImg = panel.AddComponent<Image>();
            UITheme.ApplySurface(panelImg, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.PanelLarge);

            var headerCard = UiHelper.CreatePanel(panel.transform, "HeaderCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            PlaceRect(headerCard.GetComponent<RectTransform>(),
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                new Vector2(0, -12), new Vector2(-24, 54));

            var controlsCard = UiHelper.CreatePanel(panel.transform, "ControlsCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);
            PlaceRect(controlsCard.GetComponent<RectTransform>(),
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                new Vector2(0, -76), new Vector2(-24, 62));

            var monthCard = UiHelper.CreatePanel(panel.transform, "MonthCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.28f), UIShapePreset.Panel);
            PlaceRect(monthCard.GetComponent<RectTransform>(),
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                new Vector2(0, -146), new Vector2(-24, 56));

            var gridCard = UiHelper.CreatePanel(panel.transform, "GridCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.26f), UIShapePreset.Panel);
            PlaceRect(gridCard.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0, -34), new Vector2(860, 460));

            var legendCard = UiHelper.CreatePanel(panel.transform, "LegendCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.28f), UIShapePreset.Panel);
            PlaceRect(legendCard.GetComponent<RectTransform>(),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
                new Vector2(0, 12), new Vector2(-24, 42));

            // Header
            _headerText = UiHelper.CreateText(panel.transform, "Header",
                LocalizationService.Get("personnel.schedule.title"), 18, TextAlignmentOptions.MidlineLeft);
            var hr = _headerText.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
            hr.pivot = new Vector2(0, 1);
            hr.anchoredPosition = new Vector2(28, -20);
            hr.sizeDelta = new Vector2(-180, 40);

            var closeBtn = UiHelper.CreateButton(panel.transform, "Close", LocalizationService.Get("personnel.schedule.close_btn"), Hide);
            var cbr = closeBtn.GetComponent<RectTransform>();
            cbr.anchorMin = new Vector2(1, 1); cbr.anchorMax = new Vector2(1, 1);
            cbr.pivot = new Vector2(1, 1);
            cbr.anchoredPosition = new Vector2(-24, -22);
            cbr.sizeDelta = new Vector2(132, 32);

            // Cycle + default shift dropdowns
            var cycleLabel = UiHelper.CreateText(panel.transform, "CycleLabel", LocalizationService.Get("personnel.schedule.cycle_label"), 13, TextAlignmentOptions.MidlineLeft);
            PlaceRect(cycleLabel.GetComponent<RectTransform>(),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(20, -60), new Vector2(60, 28));

            _cycleDropdown = UiHelper.CreateDropdown(panel.transform, "CycleDd",
                new List<string> {
                    LocalizationService.Get("personnel.schedule.cycle.5_2"),
                    LocalizationService.Get("personnel.schedule.cycle.4_2"),
                    LocalizationService.Get("personnel.schedule.cycle.6_2"),
                    LocalizationService.Get("personnel.schedule.cycle.7_7"),
                    LocalizationService.Get("personnel.schedule.cycle.custom")
                });
            PlaceRect(_cycleDropdown.GetComponent<RectTransform>(),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(85, -55), new Vector2(190, 28));
            _cycleDropdown.onValueChanged.AddListener(OnCycleChanged);

            var shiftLabel = UiHelper.CreateText(panel.transform, "ShiftLabel", LocalizationService.Get("personnel.schedule.default_shift_label"), 13, TextAlignmentOptions.MidlineLeft);
            PlaceRect(shiftLabel.GetComponent<RectTransform>(),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(290, -60), new Vector2(130, 28));

            _defaultShiftDropdown = UiHelper.CreateDropdown(panel.transform, "ShiftDd",
                new List<string> {
                    LocalizationService.Get("personnel.schedule.shift.morning"),
                    LocalizationService.Get("personnel.schedule.shift.afternoon"),
                    LocalizationService.Get("personnel.schedule.shift.night")
                });
            PlaceRect(_defaultShiftDropdown.GetComponent<RectTransform>(),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(420, -55), new Vector2(190, 28));
            _defaultShiftDropdown.onValueChanged.AddListener(OnDefaultShiftChanged);

            _cycleInfoText = UiHelper.CreateText(panel.transform, "CycleInfo", "", 11, TextAlignmentOptions.MidlineRight);
            _cycleInfoText.color = UITheme.SecondaryText;
            PlaceRect(_cycleInfoText.GetComponent<RectTransform>(),
                new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-15, -55), new Vector2(-20, 28));

            // Month nav
            var prevBtn = UiHelper.CreateButton(panel.transform, "PrevMonth", LocalizationService.Get("personnel.schedule.nav_prev"), () => NavMonth(-1));
            PlaceRect(prevBtn.GetComponent<RectTransform>(),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(20, -105), new Vector2(40, 32));

            _monthHeaderText = UiHelper.CreateText(panel.transform, "MonthHdr", "", 16, TextAlignmentOptions.Center);
            _monthHeaderText.fontStyle = FontStyles.Bold;
            PlaceRect(_monthHeaderText.GetComponent<RectTransform>(),
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                new Vector2(0, -105), new Vector2(-120, 32));

            var nextBtn = UiHelper.CreateButton(panel.transform, "NextMonth", LocalizationService.Get("personnel.schedule.nav_next"), () => NavMonth(+1));
            PlaceRect(nextBtn.GetComponent<RectTransform>(),
                new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-20, -105), new Vector2(40, 32));

            // Day header row (Pn Wt Śr Cz Pt Sb Nd) — reused z timetable.creator.day_short.*
            string[] dayKeys = {
                "timetable.creator.day_short.mon",
                "timetable.creator.day_short.tue",
                "timetable.creator.day_short.wed",
                "timetable.creator.day_short.thu",
                "timetable.creator.day_short.fri",
                "timetable.creator.day_short.sat",
                "timetable.creator.day_short.sun"
            };
            for (int i = 0; i < 7; i++)
            {
                var hdr = UiHelper.CreateText(panel.transform, $"DayHdr{i}", LocalizationService.Get(dayKeys[i]), 12, TextAlignmentOptions.Center);
                hdr.color = UITheme.SecondaryText;
                hdr.fontStyle = FontStyles.Bold;
                float x = 25 + i * 120;
                PlaceRect(hdr.GetComponent<RectTransform>(),
                    new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(x, -150), new Vector2(115, 25));
            }

            // Day grid (7 x 6 = 42)
            for (int row = 0; row < 6; row++)
            {
                for (int col = 0; col < 7; col++)
                {
                    int idx = row * 7 + col;
                    var btn = UiHelper.CreateButton(panel.transform, $"Day{idx}", "", () => OnDayClicked(idx));
                    float x = 25 + col * 120;
                    float y = -180 - row * 75;
                    PlaceRect(btn.GetComponent<RectTransform>(),
                        new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1),
                        new Vector2(x, y), new Vector2(115, 70));

                    _dayButtons.Add(btn);
                    _dayBackgrounds.Add(btn.GetComponent<Image>());
                    _dayLabels.Add(btn.GetComponentInChildren<TextMeshProUGUI>());
                }
            }

            // Legend
            var legend = UiHelper.CreateText(panel.transform, "Legend",
                LocalizationService.Get("personnel.schedule.legend"),
                11, TextAlignmentOptions.Center);
            PlaceRect(legend.GetComponent<RectTransform>(),
                new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0),
                new Vector2(0, 15), new Vector2(-40, 25));

            BuildContextMenu(canvasGo.transform);
        }

        void BuildContextMenu(Transform parent)
        {
            _contextMenu = new GameObject("ContextMenu");
            _contextMenu.transform.SetParent(parent, false);
            var cmRt = _contextMenu.AddComponent<RectTransform>();
            cmRt.anchorMin = new Vector2(0.5f, 0.5f); cmRt.anchorMax = new Vector2(0.5f, 0.5f);
            cmRt.sizeDelta = new Vector2(280, 340);
            cmRt.anchoredPosition = Vector2.zero;
            var cmImg = _contextMenu.AddComponent<Image>();
            UITheme.ApplySurface(cmImg, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.PanelLarge);

            var title = UiHelper.CreateText(_contextMenu.transform, "Title", LocalizationService.Get("personnel.schedule.context.title"), 14, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            PlaceRect(title.GetComponent<RectTransform>(),
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                new Vector2(0, -10), new Vector2(-10, 25));

            AddContextBtn(LocalizationService.Get("personnel.schedule.context.vacation_1"), () => AddOverride(ScheduleOverrideType.Vacation, 1, false, ShiftType.Morning), 0);
            AddContextBtn(LocalizationService.Get("personnel.schedule.context.vacation_7"), () => AddOverride(ScheduleOverrideType.Vacation, 7, false, ShiftType.Morning), 1);
            AddContextBtn(LocalizationService.Get("personnel.schedule.context.swap_morning"), () => AddOverride(ScheduleOverrideType.ShiftSwap, 1, true, ShiftType.Morning), 2);
            AddContextBtn(LocalizationService.Get("personnel.schedule.context.swap_afternoon"), () => AddOverride(ScheduleOverrideType.ShiftSwap, 1, true, ShiftType.Afternoon), 3);
            AddContextBtn(LocalizationService.Get("personnel.schedule.context.swap_night"), () => AddOverride(ScheduleOverrideType.ShiftSwap, 1, true, ShiftType.Night), 4);
            AddContextBtn(LocalizationService.Get("personnel.schedule.context.extra_duty"), () => AddOverride(ScheduleOverrideType.ExtraDutyDay, 1, false, ShiftType.Morning), 5);
            AddContextBtn(LocalizationService.Get("personnel.schedule.context.free_day"), () => AddOverride(ScheduleOverrideType.FreeDay, 1, false, ShiftType.Morning), 6);
            AddContextBtn(LocalizationService.Get("personnel.schedule.context.remove"), () => RemoveOverrideAtContextDate(), 7);
            AddContextBtn(LocalizationService.Get("personnel.schedule.context.cancel"), () => _contextMenu.SetActive(false), 8);

            _contextMenu.SetActive(false);
        }

        void AddContextBtn(string label, Action onClick, int index)
        {
            var b = UiHelper.CreateButton(_contextMenu.transform, $"Ctx_{index}", label, () =>
            {
                onClick?.Invoke();
                _contextMenu.SetActive(false);
            });
            PlaceRect(b.GetComponent<RectTransform>(),
                new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1),
                new Vector2(0, -45 - index * 32), new Vector2(-20, 28));
        }

        static void PlaceRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        // ═══ Refresh ═══

        void RefreshCalendar()
        {
            var e = PersonnelService.GetById(_currentEmployeeId);
            if (e == null) { Hide(); return; }

            var sched = PersonnelService.GetSchedule(_currentEmployeeId);
            if (sched == null) { Hide(); return; }

            _headerText.text = string.Format(LocalizationService.Get("personnel.schedule.header_format"),
                e.employeeId, e.DisplayFullName, RoleDefinitions.GetDisplayNamePl(e.role));

            _cycleDropdown.SetValueWithoutNotify((int)sched.cycle);
            _defaultShiftDropdown.SetValueWithoutNotify((int)sched.defaultShift);

            _monthHeaderText.text = string.Format(LocalizationService.Get("personnel.schedule.month_header_format"),
                MonthNamePl(_viewMonth), _viewYear);
            _cycleInfoText.text = string.Format(LocalizationService.Get("personnel.schedule.vacation_remaining_format"),
                e.vacationDaysRemaining, PersonnelBalanceConstants.VacationDaysPerYear);

            PaintCalendar(sched);
        }

        void PaintCalendar(EmployeeSchedule sched)
        {
            DateTime firstOfMonth;
            try { firstOfMonth = new DateTime(_viewYear, _viewMonth, 1); }
            catch { return; }

            // DayOfWeek in .NET: Sunday=0 .. Saturday=6. Konwersja na Pn=0..Nd=6:
            int dayOfWeek = ((int)firstOfMonth.DayOfWeek + 6) % 7;
            int daysInMonth = DateTime.DaysInMonth(_viewYear, _viewMonth);

            for (int i = 0; i < _dayButtons.Count; i++)
            {
                int dayIdx = i - dayOfWeek; // 0-based day in month (może być ujemny = prev month)
                if (dayIdx < 0 || dayIdx >= daysInMonth)
                {
                    _dayLabels[i].text = "";
                    _dayBackgrounds[i].color = UITheme.WithAlpha(UITheme.PrimarySurface, 0.92f);
                    _dayButtons[i].interactable = false;
                    continue;
                }

                int dayNumber = dayIdx + 1;
                var date = new DateTime(_viewYear, _viewMonth, dayNumber);
                string iso = date.ToString("yyyy-MM-dd");

                var (color, labelText) = ResolveDayAppearance(sched, date, iso);
                _dayLabels[i].text = labelText;
                _dayBackgrounds[i].color = color;
                _dayButtons[i].interactable = true;
            }
        }

        (Color, string) ResolveDayAppearance(EmployeeSchedule sched, DateTime date, string iso)
        {
            string fmt = LocalizationService.Get("personnel.schedule.day_format");

            // Priority: overrides > baseline cycle
            foreach (var o in sched.overrides)
            {
                if (IsDateInRange(iso, o.dateIsoStart, o.dateIsoEnd))
                {
                    return o.type switch
                    {
                        ScheduleOverrideType.Vacation =>
                            (UITheme.WithAlpha(new Color(0.66f, 0.33f, 0.95f, 1f), 0.92f), string.Format(fmt, date.Day, LocalizationService.Get("personnel.schedule.day_label.vacation"))),
                        ScheduleOverrideType.SickLeave =>
                            (UITheme.WithAlpha(UITheme.Danger, 0.92f), string.Format(fmt, date.Day, LocalizationService.Get("personnel.schedule.day_label.sick"))),
                        ScheduleOverrideType.Training =>
                            (UITheme.WithAlpha(UITheme.Warning, 0.92f), string.Format(fmt, date.Day, LocalizationService.Get("personnel.schedule.day_label.training"))),
                        ScheduleOverrideType.ShiftSwap =>
                            (UITheme.WithAlpha(UITheme.Warning, 0.92f), string.Format(fmt, date.Day,
                                string.Format(LocalizationService.Get("personnel.schedule.day_label.swap_format"), ShortShift(o.replacementShift)))),
                        ScheduleOverrideType.ExtraDutyDay =>
                            (UITheme.WithAlpha(UITheme.Success, 0.92f), string.Format(fmt, date.Day, LocalizationService.Get("personnel.schedule.day_label.extra"))),
                        ScheduleOverrideType.FreeDay =>
                            (UITheme.WithAlpha(UITheme.Border, 0.92f), string.Format(fmt, date.Day, LocalizationService.Get("personnel.schedule.day_label.free"))),
                        _ => (UITheme.WithAlpha(UITheme.SecondarySurface, 0.92f), $"<b>{date.Day}</b>")
                    };
                }
            }

            // Baseline cycle
            bool isWorkDay = IsWorkDayByBaseline(sched, date);
            if (!isWorkDay)
                return (UITheme.WithAlpha(UITheme.Border, 0.92f), string.Format(fmt, date.Day, LocalizationService.Get("personnel.schedule.day_label.free")));

            return sched.defaultShift switch
            {
                ShiftType.Morning =>
                    (UITheme.WithAlpha(UITheme.Success, 0.92f), string.Format(fmt, date.Day, LocalizationService.Get("personnel.schedule.day_label.morning"))),
                ShiftType.Afternoon =>
                    (UITheme.WithAlpha(UITheme.PrimaryAccent, 0.92f), string.Format(fmt, date.Day, LocalizationService.Get("personnel.schedule.day_label.afternoon"))),
                ShiftType.Night =>
                    (UITheme.WithAlpha(UITheme.PrimarySurface, 0.96f), string.Format(fmt, date.Day, LocalizationService.Get("personnel.schedule.day_label.night"))),
                _ => (UITheme.WithAlpha(UITheme.SecondarySurface, 0.92f), $"<b>{date.Day}</b>")
            };
        }

        static string ShortShift(ShiftType s) => LocalizationService.Get(s switch
        {
            ShiftType.Morning   => "personnel.schedule.shift_short.morning",
            ShiftType.Afternoon => "personnel.schedule.shift_short.afternoon",
            ShiftType.Night     => "personnel.schedule.shift_short.night",
            _                   => "personnel.schedule.shift_short.unknown"
        });

        static bool IsWorkDayByBaseline(EmployeeSchedule sched, DateTime date)
        {
            // Simple approx: 5+2 = pn-pt work, sb-nd off. Post-EA pełny cycleStartDayOffset.
            int dayOfWeekMonday0 = ((int)date.DayOfWeek + 6) % 7; // 0=Pn..6=Nd
            return sched.cycle switch
            {
                WorkCyclePattern.Cycle5_2 => dayOfWeekMonday0 < 5,
                WorkCyclePattern.Cycle4_2 => dayOfWeekMonday0 < 4,
                WorkCyclePattern.Cycle6_2 => dayOfWeekMonday0 < 6,
                WorkCyclePattern.Cycle7_7 =>
                    // dzien parzysty tygodnia od startu roku: week%2 == 0 → pracuje
                    (GetIsoWeek(date) % 2 == 0),
                WorkCyclePattern.Custom => false, // custom = bez baseline, wszystko przez override
                _ => dayOfWeekMonday0 < 5
            };
        }

        static int GetIsoWeek(DateTime date)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return ci.Calendar.GetWeekOfYear(date,
                System.Globalization.CalendarWeekRule.FirstFourDayWeek,
                DayOfWeek.Monday);
        }

        static bool IsDateInRange(string iso, string rangeStart, string rangeEnd)
        {
            if (string.IsNullOrEmpty(rangeStart) || string.IsNullOrEmpty(rangeEnd)) return false;
            return string.Compare(iso, rangeStart, StringComparison.Ordinal) >= 0
                && string.Compare(iso, rangeEnd, StringComparison.Ordinal) <= 0;
        }

        static string MonthNamePl(int m) => (m >= 1 && m <= 12)
            ? LocalizationService.Get($"personnel.schedule.month.{m}")
            : m.ToString();

        // ═══ Actions ═══

        void NavMonth(int delta)
        {
            _viewMonth += delta;
            if (_viewMonth < 1) { _viewMonth = 12; _viewYear--; }
            else if (_viewMonth > 12) { _viewMonth = 1; _viewYear++; }
            RefreshCalendar();
        }

        void OnCycleChanged(int value)
        {
            var sched = PersonnelService.GetSchedule(_currentEmployeeId);
            if (sched == null) return;
            sched.cycle = (WorkCyclePattern)Mathf.Clamp(value, 0, 4);
            PersonnelService.NotifyEmployeeDataChanged();
        }

        void OnDefaultShiftChanged(int value)
        {
            var sched = PersonnelService.GetSchedule(_currentEmployeeId);
            if (sched == null) return;
            sched.defaultShift = (ShiftType)Mathf.Clamp(value, 0, 2);
            // Also update current employee shift aktualnie
            var e = PersonnelService.GetById(_currentEmployeeId);
            if (e != null) e.currentShift = sched.defaultShift;
            PersonnelService.NotifyEmployeeDataChanged();
        }

        void OnDayClicked(int cellIndex)
        {
            // Compute date for this cell
            DateTime firstOfMonth;
            try { firstOfMonth = new DateTime(_viewYear, _viewMonth, 1); }
            catch { return; }
            int dayOfWeek = ((int)firstOfMonth.DayOfWeek + 6) % 7;
            int dayIdx = cellIndex - dayOfWeek;
            int daysInMonth = DateTime.DaysInMonth(_viewYear, _viewMonth);
            if (dayIdx < 0 || dayIdx >= daysInMonth) return;

            var date = new DateTime(_viewYear, _viewMonth, dayIdx + 1);
            _contextMenuDateIso = date.ToString("yyyy-MM-dd");
            _contextMenu.SetActive(true);
        }

        void AddOverride(ScheduleOverrideType type, int spanDays, bool useReplacementShift, ShiftType replaceShift)
        {
            var sched = PersonnelService.GetSchedule(_currentEmployeeId);
            var e = PersonnelService.GetById(_currentEmployeeId);
            if (sched == null || e == null) return;
            if (string.IsNullOrEmpty(_contextMenuDateIso)) return;

            // Vacation uses GrantVacation (drains vacation days)
            if (type == ScheduleOverrideType.Vacation)
            {
                DateTime start, end;
                try
                {
                    start = IsoTime.ParseDate(_contextMenuDateIso);
                    end = start.AddDays(spanDays - 1);
                }
                catch { return; }
                PersonnelService.GrantVacation(
                    _currentEmployeeId,
                    start.ToString("yyyy-MM-dd"),
                    end.ToString("yyyy-MM-dd"),
                    spanDays);
            }
            else
            {
                string endIso = _contextMenuDateIso;
                try
                {
                    endIso = IsoTime.ParseDate(_contextMenuDateIso).AddDays(spanDays - 1).ToString("yyyy-MM-dd");
                }
                catch { /* fallback: single day */ }

                sched.overrides.Add(new ScheduleOverride
                {
                    dateIsoStart = _contextMenuDateIso,
                    dateIsoEnd = endIso,
                    type = type,
                    replacementShift = replaceShift,
                    hasReplacementShift = useReplacementShift,
                    notes = ""
                });
                PersonnelService.NotifyEmployeeDataChanged();
                Log.Info($"[EmployeeScheduleEditorUI] Added override {type} {_contextMenuDateIso}..{endIso} for #{_currentEmployeeId}");
            }

            RefreshCalendar();
        }

        void RemoveOverrideAtContextDate()
        {
            var sched = PersonnelService.GetSchedule(_currentEmployeeId);
            if (sched == null || string.IsNullOrEmpty(_contextMenuDateIso)) return;

            var e = PersonnelService.GetById(_currentEmployeeId);
            int removed = 0;
            int vacationDaysRefunded = 0;

            for (int i = sched.overrides.Count - 1; i >= 0; i--)
            {
                var o = sched.overrides[i];
                if (IsDateInRange(_contextMenuDateIso, o.dateIsoStart, o.dateIsoEnd))
                {
                    // Refund vacation days if applicable
                    if (o.type == ScheduleOverrideType.Vacation && e != null)
                    {
                        int span = ComputeSpanDays(o.dateIsoStart, o.dateIsoEnd);
                        vacationDaysRefunded += span;
                    }
                    sched.overrides.RemoveAt(i);
                    removed++;
                }
            }

            if (removed > 0)
            {
                if (vacationDaysRefunded > 0 && e != null)
                {
                    e.vacationDaysRemaining = Math.Min(
                        PersonnelBalanceConstants.VacationDaysPerYear,
                        e.vacationDaysRemaining + vacationDaysRefunded);
                    e.vacationDaysUsedThisYear = Math.Max(0, e.vacationDaysUsedThisYear - vacationDaysRefunded);
                }
                PersonnelService.NotifyEmployeeDataChanged();
                Log.Info($"[EmployeeScheduleEditorUI] Removed {removed} override(s) at {_contextMenuDateIso} " +
                         (vacationDaysRefunded > 0 ? $"(+{vacationDaysRefunded} vacation days refunded)" : ""));
            }

            RefreshCalendar();
        }

        static int ComputeSpanDays(string startIso, string endIso)
        {
            try
            {
                var s = IsoTime.ParseDate(startIso);
                var e = IsoTime.ParseDate(endIso);
                return Math.Max(1, (int)(e - s).TotalDays + 1);
            }
            catch { return 1; }
        }
    }
}
