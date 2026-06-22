using System;
using TMPro;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
using RailwayManager.Timetable;
using DepotSystem;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-4: Drill-down overlay pracownika. Otwierany z <see cref="PersonnelMainTabUI"/> klik "Szczegóły".
    ///
    /// Sekcje (§14.3 spec):
    /// - Header: imie + nazwisko + rola + staz
    /// - Stats: skill, morale, fatigue, urlop pozostaly
    /// - Pensja: aktualna vs rynkowa + przyciski Podwyzka / Premia
    /// - Akcje: Ustaw zmiane | Udziel urlopu | Edytuj harmonogram | Zwolnij | Zamknij
    ///
    /// Severance preview przy "Zwolnij" — konfirmacja przez drugi klik.
    /// </summary>
    public class EmployeeDetailsUI : MonoBehaviour
    {
        public static EmployeeDetailsUI Instance { get; private set; }

        Canvas _canvas;
        GameObject _root;
        TextMeshProUGUI _headerText;
        TextMeshProUGUI _statsText;
        TextMeshProUGUI _salaryInfoText;
        TextMeshProUGUI _severancePreviewText;
        Button _fireBtn;
        TextMeshProUGUI _fireBtnLabel;
        Button _currentRunBtn;              // M-Windows P4: „Obecnie jedzie"
        TextMeshProUGUI _currentRunBtnLabel;
        TrainRun _currentRun;
        int _currentEmployeeId = -1;
        bool _fireConfirmPending;
        bool _isVisible;

        public static EmployeeDetailsUI EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("EmployeeDetailsUI");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<EmployeeDetailsUI>();
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

        void OnEnable() { PersonnelService.OnEmployeesChanged += OnEmployeesChanged; }
        void OnDisable() { PersonnelService.OnEmployeesChanged -= OnEmployeesChanged; }

        void OnEmployeesChanged()
        {
            if (_isVisible && _currentEmployeeId > 0) Refresh();
        }

        void Update()
        {
            if (!_isVisible) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Hide();
        }

        public void Show(int employeeId)
        {
            _currentEmployeeId = employeeId;
            _fireConfirmPending = false;
            _root.SetActive(true);
            _isVisible = true;
            Refresh();
        }

        public void Hide()
        {
            _root.SetActive(false);
            _isVisible = false;
            _currentEmployeeId = -1;
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("DetailsCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 110; // above main tab (90) + recruitment (100)
            // MUI-10: standard canvas scaler config (ref 1920×1080, match 0.5)
            UITheme.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Root");
            _root.transform.SetParent(canvasGo.transform, false);
            var rootRect = _root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero; rootRect.offsetMax = Vector2.zero;
            _root.AddComponent<Image>().color = UITheme.WithAlpha(Color.black, 0.74f);

            var panel = new GameObject("Panel");
            panel.transform.SetParent(_root.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f); panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(800, 650);
            panelRect.anchoredPosition = Vector2.zero;
            var panelImage = panel.AddComponent<Image>();
            UITheme.ApplySurface(panelImage, UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f), UIShapePreset.PanelLarge);

            var headerCard = UiHelper.CreatePanel(panel.transform, "HeaderCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            var headerCardRt = headerCard.GetComponent<RectTransform>();
            headerCardRt.anchorMin = new Vector2(0, 1); headerCardRt.anchorMax = new Vector2(1, 1);
            headerCardRt.pivot = new Vector2(0.5f, 1f);
            headerCardRt.anchoredPosition = new Vector2(0, -12);
            headerCardRt.sizeDelta = new Vector2(-24, 72);

            var statsCard = UiHelper.CreatePanel(panel.transform, "StatsCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);
            var statsCardRt = statsCard.GetComponent<RectTransform>();
            statsCardRt.anchorMin = new Vector2(0, 1); statsCardRt.anchorMax = new Vector2(0.5f, 1);
            statsCardRt.pivot = new Vector2(0, 1);
            statsCardRt.anchoredPosition = new Vector2(20, -96);
            statsCardRt.sizeDelta = new Vector2(-28, 210);

            var salaryCard = UiHelper.CreatePanel(panel.transform, "SalaryCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);
            var salaryCardRt = salaryCard.GetComponent<RectTransform>();
            salaryCardRt.anchorMin = new Vector2(0.5f, 1); salaryCardRt.anchorMax = new Vector2(1, 1);
            salaryCardRt.pivot = new Vector2(0, 1);
            salaryCardRt.anchoredPosition = new Vector2(8, -96);
            salaryCardRt.sizeDelta = new Vector2(-28, 210);

            var shiftCard = UiHelper.CreatePanel(panel.transform, "ShiftCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.28f), UIShapePreset.Panel);
            var shiftCardRt = shiftCard.GetComponent<RectTransform>();
            shiftCardRt.anchorMin = new Vector2(0, 0.5f); shiftCardRt.anchorMax = new Vector2(1, 0.5f);
            shiftCardRt.pivot = new Vector2(0.5f, 0.5f);
            shiftCardRt.anchoredPosition = new Vector2(0, -2);
            shiftCardRt.sizeDelta = new Vector2(-40, 72);

            var actionsCard = UiHelper.CreatePanel(panel.transform, "ActionsCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.28f), UIShapePreset.Panel);
            var actionsCardRt = actionsCard.GetComponent<RectTransform>();
            actionsCardRt.anchorMin = new Vector2(0.5f, 0.5f); actionsCardRt.anchorMax = new Vector2(0.5f, 0.5f);
            actionsCardRt.pivot = new Vector2(0.5f, 0.5f);
            actionsCardRt.anchoredPosition = new Vector2(0, -84);
            actionsCardRt.sizeDelta = new Vector2(440, 122);

            var footerCard = UiHelper.CreatePanel(panel.transform, "FooterCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.32f), UIShapePreset.Panel);
            var footerCardRt = footerCard.GetComponent<RectTransform>();
            footerCardRt.anchorMin = new Vector2(0, 0); footerCardRt.anchorMax = new Vector2(1, 0);
            footerCardRt.pivot = new Vector2(0.5f, 0f);
            footerCardRt.anchoredPosition = new Vector2(0, 12);
            footerCardRt.sizeDelta = new Vector2(-24, 82);

            // Header
            _headerText = UiHelper.CreateText(panel.transform, "Header", "—", 18, TextAlignmentOptions.TopLeft);
            var hr = _headerText.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
            hr.pivot = new Vector2(0.5f, 1);
            hr.anchoredPosition = new Vector2(0, -18);
            hr.sizeDelta = new Vector2(-180, 62);
            _headerText.alignment = TextAlignmentOptions.TopLeft;
            hr.offsetMin = new Vector2(28, hr.offsetMin.y);

            var closeBtn = UiHelper.CreateButton(panel.transform, "Close", LocalizationService.Get("personnel.details.close_btn"), Hide);
            var cbr = closeBtn.GetComponent<RectTransform>();
            cbr.anchorMin = new Vector2(1, 1); cbr.anchorMax = new Vector2(1, 1);
            cbr.pivot = new Vector2(1, 1);
            cbr.anchoredPosition = new Vector2(-24, -22);
            cbr.sizeDelta = new Vector2(132, 32);

            // M-Windows P4: „Obecnie jedzie" (maszynista/konduktor na aktywnym pociągu) → okno składu
            _currentRunBtn = UiHelper.CreateButton(panel.transform, "CurrentRun", "—", OnCurrentRunClicked);
            var crr = _currentRunBtn.GetComponent<RectTransform>();
            crr.anchorMin = new Vector2(1, 1); crr.anchorMax = new Vector2(1, 1);
            crr.pivot = new Vector2(1, 1);
            crr.anchoredPosition = new Vector2(-24, -56);
            crr.sizeDelta = new Vector2(220, 26);
            _currentRunBtnLabel = _currentRunBtn.GetComponentInChildren<TextMeshProUGUI>();
            var crImg = _currentRunBtn.GetComponent<Image>();
            if (crImg != null) UITheme.ApplySurface(crImg, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.85f), UIShapePreset.Pill);
            _currentRunBtn.gameObject.SetActive(false);

            // Stats
            _statsText = UiHelper.CreateText(panel.transform, "Stats", "", 14, TextAlignmentOptions.TopLeft);
            var sr = _statsText.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0, 1); sr.anchorMax = new Vector2(0.5f, 1);
            sr.pivot = new Vector2(0, 1);
            sr.anchoredPosition = new Vector2(30, -108);
            sr.sizeDelta = new Vector2(-48, 188);

            // Salary info + buttons
            _salaryInfoText = UiHelper.CreateText(panel.transform, "SalaryInfo", "", 14, TextAlignmentOptions.TopLeft);
            var sir = _salaryInfoText.GetComponent<RectTransform>();
            sir.anchorMin = new Vector2(0.5f, 1); sir.anchorMax = new Vector2(1, 1);
            sir.pivot = new Vector2(0, 1);
            sir.anchoredPosition = new Vector2(18, -108);
            sir.sizeDelta = new Vector2(-52, 116);

            var raiseBtn = UiHelper.CreateButton(panel.transform, "Raise10", LocalizationService.Get("personnel.details.buttons.raise10"), OnRaise10);
            var rbr = raiseBtn.GetComponent<RectTransform>();
            rbr.anchorMin = new Vector2(0.5f, 1); rbr.anchorMax = new Vector2(0.5f, 1);
            rbr.pivot = new Vector2(0, 1);
            rbr.anchoredPosition = new Vector2(18, -222);
            rbr.sizeDelta = new Vector2(164, 30);

            var bonus1Btn = UiHelper.CreateButton(panel.transform, "Bonus1k", LocalizationService.Get("personnel.details.buttons.bonus_1k"), () => OnBonus((int)PayrollConstants.QuickBonusSmallGroszy));
            var b1r = bonus1Btn.GetComponent<RectTransform>();
            b1r.anchorMin = new Vector2(0.5f, 1); b1r.anchorMax = new Vector2(0.5f, 1);
            b1r.pivot = new Vector2(0, 1);
            b1r.anchoredPosition = new Vector2(192, -222);
            b1r.sizeDelta = new Vector2(154, 30);

            var bonus5Btn = UiHelper.CreateButton(panel.transform, "Bonus5k", LocalizationService.Get("personnel.details.buttons.bonus_5k"), () => OnBonus((int)PayrollConstants.QuickBonusMediumGroszy));
            var b5r = bonus5Btn.GetComponent<RectTransform>();
            b5r.anchorMin = new Vector2(0.5f, 1); b5r.anchorMax = new Vector2(0.5f, 1);
            b5r.pivot = new Vector2(0, 1);
            b5r.anchoredPosition = new Vector2(18, -258);
            b5r.sizeDelta = new Vector2(164, 30);

            var bonus10Btn = UiHelper.CreateButton(panel.transform, "Bonus10k", LocalizationService.Get("personnel.details.buttons.bonus_10k"), () => OnBonus((int)PayrollConstants.QuickBonusLargeGroszy));
            var b10r = bonus10Btn.GetComponent<RectTransform>();
            b10r.anchorMin = new Vector2(0.5f, 1); b10r.anchorMax = new Vector2(0.5f, 1);
            b10r.pivot = new Vector2(0, 1);
            b10r.anchoredPosition = new Vector2(192, -258);
            b10r.sizeDelta = new Vector2(154, 30);

            // Shift buttons
            var shiftLabel = UiHelper.CreateText(panel.transform, "ShiftLabel", LocalizationService.Get("personnel.details.buttons.shift_label"), 13, TextAlignmentOptions.MidlineLeft);
            var slr = shiftLabel.GetComponent<RectTransform>();
            slr.anchorMin = new Vector2(0, 0.5f); slr.anchorMax = new Vector2(0, 0.5f);
            slr.pivot = new Vector2(0, 0.5f);
            slr.anchoredPosition = new Vector2(30, 0);
            slr.sizeDelta = new Vector2(150, 25);
            shiftLabel.color = UITheme.SecondaryText;

            var shiftMorn = UiHelper.CreateButton(panel.transform, "ShiftMorn", LocalizationService.Get("personnel.details.buttons.shift_morning"), () => OnSetShift(ShiftType.Morning));
            PlaceButton(shiftMorn, new Vector2(178, 0), new Vector2(126, 30));
            var shiftAft = UiHelper.CreateButton(panel.transform, "ShiftAft", LocalizationService.Get("personnel.details.buttons.shift_afternoon"), () => OnSetShift(ShiftType.Afternoon));
            PlaceButton(shiftAft, new Vector2(310, 0), new Vector2(164, 30));
            var shiftNight = UiHelper.CreateButton(panel.transform, "ShiftNight", LocalizationService.Get("personnel.details.buttons.shift_night"), () => OnSetShift(ShiftType.Night));
            PlaceButton(shiftNight, new Vector2(480, 0), new Vector2(134, 30));

            // Schedule editor link (lewa połowa)
            var schedBtn = UiHelper.CreateButton(panel.transform, "EditSchedule", LocalizationService.Get("personnel.details.buttons.edit_schedule"),
                () =>
                {
                    if (_currentEmployeeId > 0)
                        EmployeeScheduleEditorUI.EnsureExists().Show(_currentEmployeeId);
                });
            var scr = schedBtn.GetComponent<RectTransform>();
            scr.anchorMin = new Vector2(0.5f, 0.5f); scr.anchorMax = new Vector2(0.5f, 0.5f);
            scr.pivot = new Vector2(0.5f, 0.5f);
            scr.anchoredPosition = new Vector2(-98, -58);
            scr.sizeDelta = new Vector2(192, 34);

            // Qualifications popup link (prawa połowa) — BUG-010 cz.1+2 placeholder
            var qualBtn = UiHelper.CreateButton(panel.transform, "Qualifications",
                LocalizationService.Get("personnel.details.buttons.qualifications"),
                () =>
                {
                    if (_currentEmployeeId > 0)
                        EmployeeQualificationsUI.EnsureExists().Show(_currentEmployeeId);
                });
            var qbr = qualBtn.GetComponent<RectTransform>();
            qbr.anchorMin = new Vector2(0.5f, 0.5f); qbr.anchorMax = new Vector2(0.5f, 0.5f);
            qbr.pivot = new Vector2(0.5f, 0.5f);
            qbr.anchoredPosition = new Vector2(98, -58);
            qbr.sizeDelta = new Vector2(192, 34);

            // Vacation button
            var vacBtn = UiHelper.CreateButton(panel.transform, "GrantVacation7", LocalizationService.Get("personnel.details.buttons.vacation_7"),
                OnGrantVacation7);
            var vbr = vacBtn.GetComponent<RectTransform>();
            vbr.anchorMin = new Vector2(0.5f, 0.5f); vbr.anchorMax = new Vector2(0.5f, 0.5f);
            vbr.pivot = new Vector2(0.5f, 0.5f);
            vbr.anchoredPosition = new Vector2(0, -98);
            vbr.sizeDelta = new Vector2(280, 30);

            // Severance preview + Fire button
            _severancePreviewText = UiHelper.CreateText(panel.transform, "SeverancePreview", "", 12, TextAlignmentOptions.Center);
            _severancePreviewText.color = UITheme.Warning;
            var spr = _severancePreviewText.GetComponent<RectTransform>();
            spr.anchorMin = new Vector2(0, 0); spr.anchorMax = new Vector2(1, 0);
            spr.pivot = new Vector2(0.5f, 0);
            spr.anchoredPosition = new Vector2(0, 58);
            spr.sizeDelta = new Vector2(-40, 25);

            _fireBtn = UiHelper.CreateButton(panel.transform, "Fire", LocalizationService.Get("personnel.details.buttons.fire"), OnFire);
            var fbr = _fireBtn.GetComponent<RectTransform>();
            fbr.anchorMin = new Vector2(0.5f, 0); fbr.anchorMax = new Vector2(0.5f, 0);
            fbr.pivot = new Vector2(0.5f, 0);
            fbr.anchoredPosition = new Vector2(0, 22);
            fbr.sizeDelta = new Vector2(260, 38);
            _fireBtnLabel = _fireBtn.GetComponentInChildren<TextMeshProUGUI>();
            var fireImg = _fireBtn.GetComponent<Image>();
            if (fireImg != null) UITheme.ApplySurface(fireImg, UITheme.WithAlpha(UITheme.Danger, 0.92f), UIShapePreset.Pill);
        }

        void PlaceButton(Button b, Vector2 anchoredPos, Vector2 size)
        {
            var rt = b.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        // ═══ Refresh ═══

        void Refresh()
        {
            var e = PersonnelService.GetById(_currentEmployeeId);
            if (e == null) { Hide(); return; }

            // Header
            string stars = new string('★', e.skill) + new string('☆', 5 - e.skill);
            int tenureDays = PersonnelService.GetTenureDays(e);
            string tenure = tenureDays < 30
                ? string.Format(LocalizationService.Get("personnel.details.tenure_days_format"), tenureDays)
                : tenureDays < 365
                    ? string.Format(LocalizationService.Get("personnel.details.tenure_months_format"), tenureDays / 30, tenureDays % 30)
                    : string.Format(LocalizationService.Get("personnel.details.tenure_years_format"), tenureDays / 365, (tenureDays % 365) / 30);

            _headerText.text = string.Format(LocalizationService.Get("personnel.details.header_main_format"),
                e.employeeId, e.DisplayFullName,
                RoleDefinitions.GetDisplayNamePl(e.role), stars,
                e.age, tenure,
                PersonnelMainTabUI.GetStatusDisplayName(e.status));

            // M-Windows P4: „Obecnie jedzie" — pokaż gdy maszynista/konduktor na aktywnym pociągu
            _currentRun = CrewAssignmentService.GetCurrentTrainRunForEmployee(e.employeeId);
            if (_currentRunBtn != null)
            {
                if (_currentRun != null)
                {
                    string num = string.IsNullOrEmpty(_currentRun.trainNumberSnapshot)
                        ? ("#" + _currentRun.id) : _currentRun.trainNumberSnapshot;
                    if (_currentRunBtnLabel != null) _currentRunBtnLabel.text = "Jedzie: " + num + "  ›";
                    _currentRunBtn.gameObject.SetActive(true);
                }
                else
                {
                    _currentRunBtn.gameObject.SetActive(false);
                }
            }

            // Stats
            var sbS = new StringBuilder();
            sbS.AppendLine(string.Format(LocalizationService.Get("personnel.details.stats.morale_format"),
                MoraleBar(e.currentMorale), e.currentMorale));
            sbS.AppendLine(string.Format(LocalizationService.Get("personnel.details.stats.fatigue_format"),
                FatigueBar(e.currentFatigue), e.currentFatigue));
            sbS.AppendLine(string.Format(LocalizationService.Get("personnel.details.stats.shift_format"), e.currentShift));
            sbS.AppendLine(string.Format(LocalizationService.Get("personnel.details.stats.vacation_format"),
                e.vacationDaysRemaining, PersonnelBalanceConstants.VacationDaysPerYear));
            sbS.AppendLine(string.Format(LocalizationService.Get("personnel.details.stats.hire_format"), e.hireDateIso));
            if (!string.IsNullOrEmpty(e.sickUntilDateIso))
                sbS.AppendLine(string.Format(LocalizationService.Get("personnel.details.stats.sick_until_format"), e.sickUntilDateIso));
            _statsText.text = sbS.ToString();

            // Salary info
            int marketSalary = RoleDefinitions.GetExpectedSalaryGroszy(e.role, e.skill);
            float diff = (e.currentSalaryGroszy - marketSalary) / (float)Math.Max(marketSalary, 1) * 100f;
            string diffColor = diff >= 10 ? "#4ADE80" : diff <= -10 ? "#F87171" : "#FFFFFF";

            var sbSal = new StringBuilder();
            sbSal.AppendLine(string.Format(LocalizationService.Get("personnel.details.salary.current_format"), e.currentSalaryGroszy / 100));
            sbSal.AppendLine(string.Format(LocalizationService.Get("personnel.details.salary.market_format"), marketSalary / 100));
            sbSal.AppendFormat(LocalizationService.Get("personnel.details.salary.diff_format"),
                diffColor, diff.ToString("+0.0;-0.0"));
            if (e.missedPaymentsCount > 0)
                sbSal.AppendFormat(LocalizationService.Get("personnel.details.salary.missed_format"), e.missedPaymentsCount);
            _salaryInfoText.text = sbSal.ToString();

            // Severance preview
            int severance = PersonnelService.CalculateSeverancePay(e);
            if (e.IsActive)
            {
                _severancePreviewText.text = string.Format(LocalizationService.Get("personnel.details.severance_format"),
                    severance / 100, tenureDays);
                _fireBtn.interactable = true;
            }
            else
            {
                _severancePreviewText.text = string.Format(LocalizationService.Get("personnel.details.cant_fire_format"),
                    PersonnelMainTabUI.GetStatusDisplayName(e.status));
                _fireBtn.interactable = false;
            }

            // Fire button label (confirmation pattern)
            if (_fireBtnLabel != null)
                _fireBtnLabel.text = LocalizationService.Get(_fireConfirmPending
                    ? "personnel.details.buttons.fire_confirm"
                    : "personnel.details.buttons.fire");
        }

        // M-Windows P4: klik „Obecnie jedzie" → okno składu pociągu (chipy + drill-down do pojazdu)
        void OnCurrentRunClicked()
        {
            if (_currentRun == null) return;
            var run = _currentRun;
            string title = string.IsNullOrEmpty(run.trainNumberSnapshot) ? ("Pociąg #" + run.id) : run.trainNumberSnapshot;
            string status = run.runningVehicleIds != null && run.runningVehicleIds.Count > 0 ? "W trasie" : "—";
            var view = new ConsistView("train:" + run.id, title, "Obecnie jedzie", status, run.runningVehicleIds);
            ConsistWindowUI.Open(view);
        }

        static string MoraleBar(int value)
        {
            int bars = Mathf.Clamp(value / 10, 0, 10);
            string color = value >= 60 ? "#4ADE80" : value >= 30 ? "#FBBF24" : "#F87171";
            return $"<color={color}>{new string('█', bars)}{new string('░', 10 - bars)}</color>";
        }

        static string FatigueBar(int value)
        {
            int bars = Mathf.Clamp(value / 10, 0, 10);
            string color = value >= 80 ? "#F87171" : value >= 50 ? "#FBBF24" : "#4ADE80";
            return $"<color={color}>{new string('█', bars)}{new string('░', 10 - bars)}</color>";
        }

        // ═══ Actions ═══

        void OnRaise10()
        {
            var e = PersonnelService.GetById(_currentEmployeeId);
            if (e == null) return;
            int newSalary = (int)(e.currentSalaryGroszy * 1.10f);
            PersonnelService.SetSalary(_currentEmployeeId, newSalary);
        }

        void OnBonus(int amountGroszy)
        {
            if (_currentEmployeeId <= 0) return;
            if (GameState.Money * 100L < amountGroszy)
            {
                Log.Warn($"[EmployeeDetailsUI] Insufficient funds for bonus {amountGroszy / 100}zl");
                return;
            }
            PersonnelService.GrantBonus(_currentEmployeeId, amountGroszy, LocalizationService.Get("personnel.details.bonus_label"));
        }

        void OnSetShift(ShiftType shift)
        {
            if (_currentEmployeeId <= 0) return;
            PersonnelService.SetShift(_currentEmployeeId, shift);
        }

        void OnGrantVacation7()
        {
            var e = PersonnelService.GetById(_currentEmployeeId);
            if (e == null) return;
            if (e.vacationDaysRemaining < 7)
            {
                Log.Warn($"[EmployeeDetailsUI] Only {e.vacationDaysRemaining} vacation days remaining (need 7)");
                return;
            }
            try
            {
                var start = IsoTime.ParseDate(GameState.CurrentDateIso).AddDays(1);
                var end = start.AddDays(6);
                PersonnelService.GrantVacation(_currentEmployeeId,
                    start.ToString("yyyy-MM-dd"),
                    end.ToString("yyyy-MM-dd"),
                    7);
            }
            catch (Exception ex)
            {
                Log.Error($"[EmployeeDetailsUI] GrantVacation failed: {ex.Message}");
            }
        }

        void OnFire()
        {
            if (!_fireConfirmPending)
            {
                _fireConfirmPending = true;
                Refresh();
                return;
            }
            _fireConfirmPending = false;
            PersonnelService.Fire(_currentEmployeeId, paySeverance: true);
            Hide();
        }
    }
}
