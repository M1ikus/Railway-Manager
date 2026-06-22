using System;
using TMPro;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.Personnel.Furniture;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Personnel
{
    public partial class PersonnelMainTabUI
    {
        // ═══ My Staff content ═══

        TMP_Dropdown _filterRoleDropdown;
        TMP_Dropdown _filterStatusDropdown;
        TMP_InputField _filterSearchInput;
        RectTransform _staffListContent;
        TextMeshProUGUI _staffCountText;
        readonly List<GameObject> _staffRows = new();

        void BuildMyStaffContent()
        {
            var parent = _myStaffContent.transform;

            // Filter bar
            var filterBar = new GameObject("FilterBar");
            filterBar.transform.SetParent(parent, false);
            var fbRt = filterBar.AddComponent<RectTransform>();
            fbRt.anchorMin = new Vector2(0, 1); fbRt.anchorMax = new Vector2(1, 1);
            fbRt.pivot = new Vector2(0.5f, 1);
            fbRt.anchoredPosition = Vector2.zero;
            fbRt.sizeDelta = new Vector2(0, 40);
            var filterImg = filterBar.AddComponent<Image>();
            UITheme.ApplySurface(filterImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.36f), UIShapePreset.Panel);

            var hlg = filterBar.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = false;
            hlg.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Xs);
            hlg.spacing = UITheme.Spacing.Md;

            // Role filter
            var roleOptions = new List<string> { LocalizationService.Get("personnel.my_staff.filter_role_all") };
            foreach (EmployeeRole role in Enum.GetValues(typeof(EmployeeRole)))
                roleOptions.Add(RoleDefinitions.GetDisplayNamePl(role));
            _filterRoleDropdown = UiHelper.CreateDropdown(filterBar.transform, "RoleFilter", roleOptions);
            _filterRoleDropdown.onValueChanged.AddListener(_ => RefreshStaffList());
            var rdLe = _filterRoleDropdown.gameObject.AddComponent<LayoutElement>();
            rdLe.preferredWidth = 200; rdLe.preferredHeight = 30;

            // Status filter
            var statusOptions = new List<string> {
                LocalizationService.Get("personnel.my_staff.filter_status.all"),
                LocalizationService.Get("personnel.my_staff.filter_status.active"),
                LocalizationService.Get("personnel.my_staff.filter_status.fired"),
                LocalizationService.Get("personnel.my_staff.filter_status.retired"),
                LocalizationService.Get("personnel.my_staff.filter_status.sick")
            };
            _filterStatusDropdown = UiHelper.CreateDropdown(filterBar.transform, "StatusFilter", statusOptions);
            _filterStatusDropdown.onValueChanged.AddListener(_ => RefreshStaffList());
            var sdLe = _filterStatusDropdown.gameObject.AddComponent<LayoutElement>();
            sdLe.preferredWidth = 150; sdLe.preferredHeight = 30;

            // Count label
            _staffCountText = UiHelper.CreateText(filterBar.transform, "CountLabel",
                LocalizationService.Get("personnel.my_staff.count_default"), 13, TextAlignmentOptions.MidlineLeft);
            var ctLe = _staffCountText.gameObject.AddComponent<LayoutElement>();
            ctLe.flexibleWidth = 1; ctLe.preferredHeight = 30;
            _staffCountText.color = UITheme.SecondaryText;

            // Scroll list
            var scroll = new GameObject("StaffScroll");
            scroll.transform.SetParent(parent, false);
            var scRt = scroll.AddComponent<RectTransform>();
            scRt.anchorMin = new Vector2(0, 0); scRt.anchorMax = new Vector2(1, 1);
            scRt.offsetMin = new Vector2(10, 10); scRt.offsetMax = new Vector2(-10, -50);
            var scrollImg = scroll.AddComponent<Image>();
            UITheme.ApplySurface(scrollImg, UITheme.WithAlpha(UITheme.PrimarySurface, 0.3f), UIShapePreset.Panel);

            var sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scroll.transform, false);
            var vpRt = viewport.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(6, 6); vpRt.offsetMax = new Vector2(-6, -6);
            var viewportImg = viewport.AddComponent<Image>();
            UITheme.ApplySurface(viewportImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            viewport.AddComponent<Mask>().showMaskGraphic = true;
            sr.viewport = vpRt;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            _staffListContent = content.AddComponent<RectTransform>();
            _staffListContent.anchorMin = new Vector2(0, 1); _staffListContent.anchorMax = new Vector2(1, 1);
            _staffListContent.pivot = new Vector2(0.5f, 1);
            _staffListContent.anchoredPosition = Vector2.zero;
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = false; vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vlg.spacing = UITheme.Spacing.Sm;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = _staffListContent;
        }

        void RefreshStaffList()
        {
            if (_staffListContent == null) return;

            foreach (var r in _staffRows) if (r != null) Destroy(r);
            _staffRows.Clear();

            var filtered = FilterEmployees();
            _staffCountText.text = string.Format(LocalizationService.Get("personnel.my_staff.count_format"),
                filtered.Count, PersonnelService.Employees.Count);

            if (filtered.Count == 0)
            {
                var empty = UiHelper.CreateText(_staffListContent, "Empty",
                    LocalizationService.Get("personnel.my_staff.empty"), 14, TextAlignmentOptions.Center);
                empty.color = UITheme.SecondaryText;
                var er = empty.GetComponent<RectTransform>();
                er.sizeDelta = new Vector2(0, 40);
                var emptyBg = empty.gameObject.AddComponent<Image>();
                UITheme.ApplySurface(emptyBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);
                _staffRows.Add(empty.gameObject);
                return;
            }

            foreach (var e in filtered) AddStaffRow(e);
        }

        List<Employee> FilterEmployees()
        {
            var result = new List<Employee>();
            int roleIdx = _filterRoleDropdown != null ? _filterRoleDropdown.value : 0;
            int statusIdx = _filterStatusDropdown != null ? _filterStatusDropdown.value : 0;

            foreach (var e in PersonnelService.Employees)
            {
                // Role filter (0 = all)
                if (roleIdx > 0)
                {
                    var filterRole = (EmployeeRole)(roleIdx - 1);
                    if (e.role != filterRole) continue;
                }
                // Status filter
                if (statusIdx == 1 && !e.IsActive) continue;
                if (statusIdx == 2 && e.status != EmployeeStatus.Fired) continue;
                if (statusIdx == 3 && e.status != EmployeeStatus.Retired) continue;
                if (statusIdx == 4 && e.status != EmployeeStatus.Sick && e.status != EmployeeStatus.LongSick) continue;
                result.Add(e);
            }
            return result;
        }

        void AddStaffRow(Employee e)
        {
            var row = new GameObject($"Row_{e.employeeId}");
            row.transform.SetParent(_staffListContent, false);
            var rowRt = row.AddComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0, 64);
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Inset);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            hlg.spacing = UITheme.Spacing.Md;

            var info = UiHelper.CreateText(row.transform, "Info", BuildStaffRowInfo(e), 12, TextAlignmentOptions.MidlineLeft);
            info.richText = true;
            var le = info.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1; le.preferredHeight = 48;

            var detailsBtn = UiHelper.CreateButton(row.transform, "Details", LocalizationService.Get("personnel.my_staff.details_btn"), () => EmployeeDetailsUI.EnsureExists().Show(e.employeeId));
            var dLe = detailsBtn.gameObject.AddComponent<LayoutElement>();
            dLe.preferredWidth = 120; dLe.preferredHeight = 46;

            _staffRows.Add(row);
        }

        static string BuildStaffRowInfo(Employee e)
        {
            string stars = new string('★', e.skill) + new string('☆', 5 - e.skill);
            string statusColor = e.status switch
            {
                EmployeeStatus.Sick or EmployeeStatus.LongSick => "#F87171",
                EmployeeStatus.Retired => "#9CA3AF",
                EmployeeStatus.Fired => "#6B7280",
                EmployeeStatus.OnShift => "#4ADE80",
                _ => "#FFFFFF"
            };

            var sb = new StringBuilder();
            sb.AppendFormat(LocalizationService.Get("personnel.my_staff.row_main_format"),
                e.employeeId, e.DisplayFullName, e.age,
                RoleDefinitions.GetDisplayNamePl(e.role), stars,
                statusColor, GetStatusDisplayName(e.status));
            sb.AppendLine();
            sb.AppendFormat(LocalizationService.Get("personnel.my_staff.row_stats_format"),
                e.currentShift, e.currentMorale, e.currentFatigue,
                e.currentSalaryGroszy / 100);

            // MF-12: alert "Brak biurka" dla pracowników wymagających stanowiska bez przypisanego
            if (FurnitureAssignmentService.IsIdleWithoutFurniture(e))
            {
                sb.AppendLine();
                sb.Append("<color=#FBBF24>⚠ Brak biurka — funkcja zablokowana</color>");
            }

            // TD-025: pokaż aktualny workflow tylko gdy pracownik jest OnShift (= faktycznie
            // w depot). Dla Resting/Sick/Fired pole jest zawsze OffShift, nieinformacyjne.
            if (e.status == EmployeeStatus.OnShift
                && RoleDefinitions.SpawnsAsAgentInDepot(e.role))
            {
                sb.AppendLine();
                sb.Append($"<color=#9CA3AF>Czyni: {GetWorkflowStateDisplayName(e.workflowState)}</color>");
            }

            return sb.ToString();
        }

        /// <summary>TD-025: PL display name dla <see cref="EmployeeWorkflowState"/>. Inline
        /// (bez localization key) — to debug feature, full i18n post-EA.</summary>
        public static string GetWorkflowStateDisplayName(EmployeeWorkflowState s) => s switch
        {
            EmployeeWorkflowState.OffShift              => "poza zmianą",
            EmployeeWorkflowState.ComingToDepot         => "idzie do depot",
            EmployeeWorkflowState.ReportingToDispatcher => "meldunek u dyspozytora",
            EmployeeWorkflowState.GoingToWorkstation    => "idzie na stanowisko",
            EmployeeWorkflowState.WorkingAtStation      => "pracuje",
            EmployeeWorkflowState.WorkingMobile         => "pracuje (mobile)",
            EmployeeWorkflowState.AwaitingDeparture     => "czeka na wyjazd",
            EmployeeWorkflowState.GoingToVehicle        => "idzie do pojazdu",
            EmployeeWorkflowState.DrivingTrain          => "w trasie (pociąg)",
            EmployeeWorkflowState.GoingHome             => "wraca do domu",
            _ => s.ToString()
        };

        /// <summary>
        /// M13-4k-2: lokalizowana nazwa stanu pracownika (Available/OnShift/Sick/...).
        /// Klucze w `personnel.status.*`. Używane w MyStaff row + w innych UI gdzie
        /// pokazujemy status pracownika (np. EmployeeDetails).
        /// </summary>
        public static string GetStatusDisplayName(EmployeeStatus status) => LocalizationService.Get(status switch
        {
            EmployeeStatus.Available => "personnel.status.available",
            EmployeeStatus.OnShift   => "personnel.status.on_shift",
            EmployeeStatus.Resting   => "personnel.status.resting",
            EmployeeStatus.Sick      => "personnel.status.sick",
            EmployeeStatus.LongSick  => "personnel.status.long_sick",
            EmployeeStatus.Training  => "personnel.status.training",
            EmployeeStatus.Retired   => "personnel.status.retired",
            EmployeeStatus.Fired     => "personnel.status.fired",
            _ => status.ToString()
        });
    }
}
