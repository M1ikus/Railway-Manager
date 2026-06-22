using System;
using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Personnel
{
    public partial class PersonnelMainTabUI
    {
        // ═══ Dispatch content (M8-7) ═══

        TextMeshProUGUI _dispatchHeaderText;
        TextMeshProUGUI _dispatchCapacityBarText;
        TextMeshProUGUI _dispatchDispatchersListText;
        RectTransform _dispatchPendingListContent;
        readonly List<GameObject> _dispatchPendingRows = new();

        void BuildDispatchContent()
        {
            var parent = _dispatchContent.transform;

            var summaryCard = UiHelper.CreatePanel(parent, "DispatchSummaryCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            var summaryRt = summaryCard.GetComponent<RectTransform>();
            summaryRt.anchorMin = new Vector2(0, 1); summaryRt.anchorMax = new Vector2(1, 1);
            summaryRt.pivot = new Vector2(0.5f, 1f);
            summaryRt.anchoredPosition = new Vector2(0, -8);
            summaryRt.sizeDelta = new Vector2(-20, 106);

            // Header (status + capacity)
            _dispatchHeaderText = UiHelper.CreateText(summaryCard.transform, "DispHeader", "", 16, TextAlignmentOptions.TopLeft);
            _dispatchHeaderText.richText = true;
            var hr = _dispatchHeaderText.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
            hr.pivot = new Vector2(0, 1);
            hr.anchoredPosition = new Vector2(18, -16);
            hr.sizeDelta = new Vector2(-36, 58);

            // Capacity bar text
            _dispatchCapacityBarText = UiHelper.CreateText(summaryCard.transform, "CapBar", "", 14, TextAlignmentOptions.MidlineLeft);
            var cbr = _dispatchCapacityBarText.GetComponent<RectTransform>();
            cbr.anchorMin = new Vector2(0, 1); cbr.anchorMax = new Vector2(1, 1);
            cbr.pivot = new Vector2(0, 1);
            cbr.anchoredPosition = new Vector2(18, -74);
            cbr.sizeDelta = new Vector2(-36, 20);

            var dispatchersCard = UiHelper.CreatePanel(parent, "DispatchersCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);
            var dcRt = dispatchersCard.GetComponent<RectTransform>();
            dcRt.anchorMin = new Vector2(0, 0); dcRt.anchorMax = new Vector2(0.5f, 1);
            dcRt.offsetMin = new Vector2(10, 10); dcRt.offsetMax = new Vector2(-5, -125);

            // Dispatchers list
            var dispLabel = UiHelper.CreateText(dispatchersCard.transform, "DispListLabel",
                LocalizationService.Get("personnel.dispatch.dispatchers_label"), 13, TextAlignmentOptions.TopLeft);
            dispLabel.fontStyle = FontStyles.Bold;
            var dll = dispLabel.GetComponent<RectTransform>();
            dll.anchorMin = new Vector2(0, 1); dll.anchorMax = new Vector2(1, 1);
            dll.pivot = new Vector2(0, 1);
            dll.anchoredPosition = new Vector2(16, -14);
            dll.sizeDelta = new Vector2(-32, 24);

            _dispatchDispatchersListText = UiHelper.CreateText(dispatchersCard.transform, "DispListTxt", "", 12, TextAlignmentOptions.TopLeft);
            _dispatchDispatchersListText.richText = true;
            _dispatchDispatchersListText.color = UITheme.PrimaryText;
            var dlt = _dispatchDispatchersListText.GetComponent<RectTransform>();
            dlt.anchorMin = new Vector2(0, 0); dlt.anchorMax = new Vector2(1, 1);
            dlt.pivot = new Vector2(0, 1);
            dlt.anchoredPosition = new Vector2(16, -42);
            dlt.sizeDelta = new Vector2(-32, -58);

            var pendingCard = UiHelper.CreatePanel(parent, "PendingCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);
            var pcRt = pendingCard.GetComponent<RectTransform>();
            pcRt.anchorMin = new Vector2(0.5f, 0); pcRt.anchorMax = new Vector2(1, 1);
            pcRt.offsetMin = new Vector2(5, 10); pcRt.offsetMax = new Vector2(-10, -125);

            // Pending actions (prawa kolumna)
            var pendLabel = UiHelper.CreateText(pendingCard.transform, "PendLabel",
                LocalizationService.Get("personnel.dispatch.pending_label"), 13, TextAlignmentOptions.TopLeft);
            pendLabel.fontStyle = FontStyles.Bold;
            var pll = pendLabel.GetComponent<RectTransform>();
            pll.anchorMin = new Vector2(0, 1); pll.anchorMax = new Vector2(1, 1);
            pll.pivot = new Vector2(0, 1);
            pll.anchoredPosition = new Vector2(16, -14);
            pll.sizeDelta = new Vector2(-32, 24);

            var scroll = new GameObject("PendScroll");
            scroll.transform.SetParent(pendingCard.transform, false);
            var scRt = scroll.AddComponent<RectTransform>();
            scRt.anchorMin = new Vector2(0, 0); scRt.anchorMax = new Vector2(1, 1);
            scRt.pivot = new Vector2(0.5f, 0.5f);
            scRt.offsetMin = new Vector2(14, 14); scRt.offsetMax = new Vector2(-14, -44);
            var scrollImg = scroll.AddComponent<Image>();
            UITheme.ApplySurface(scrollImg, UITheme.WithAlpha(UITheme.PrimarySurface, 0.34f), UIShapePreset.Inset);
            var sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scroll.transform, false);
            var vpRt = viewport.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = new Vector2(6, 6); vpRt.offsetMax = new Vector2(-6, -6);
            var viewportImg = viewport.AddComponent<Image>();
            UITheme.ApplySurface(viewportImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.9f), UIShapePreset.Inset);
            viewport.AddComponent<Mask>().showMaskGraphic = true;
            sr.viewport = vpRt;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            _dispatchPendingListContent = content.AddComponent<RectTransform>();
            _dispatchPendingListContent.anchorMin = new Vector2(0, 1);
            _dispatchPendingListContent.anchorMax = new Vector2(1, 1);
            _dispatchPendingListContent.pivot = new Vector2(0.5f, 1);
            _dispatchPendingListContent.anchoredPosition = Vector2.zero;
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = false; vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vlg.spacing = UITheme.Spacing.Sm;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = _dispatchPendingListContent;
        }

        void RefreshDispatchContent()
        {
            if (_dispatchHeaderText == null) return;

            var workload = DispatcherService.GetWorkload();

            string statusColor = workload.status switch
            {
                DispatcherStatus.Normal => "#4ADE80",
                DispatcherStatus.Delayed => "#FBBF24",
                DispatcherStatus.Critical => "#F87171",
                _ => "#FFFFFF"
            };
            string statusLabel = workload.status switch
            {
                DispatcherStatus.Normal => LocalizationService.Get("personnel.dispatch.status.normal"),
                DispatcherStatus.Delayed => LocalizationService.Get("personnel.dispatch.status.delayed"),
                DispatcherStatus.Critical => workload.activeDispatcherCount == 0
                    ? LocalizationService.Get("personnel.dispatch.status.critical_no_workers")
                    : LocalizationService.Get("personnel.dispatch.status.critical"),
                _ => LocalizationService.Get("personnel.dispatch.status.unknown")
            };

            _dispatchHeaderText.text = string.Format(LocalizationService.Get("personnel.dispatch.header_format"),
                statusColor, statusLabel,
                workload.activeDispatcherCount, workload.totalCapacity,
                workload.currentHeadcount, workload.pendingActionsCount);

            // Capacity bar
            int bars = Mathf.Clamp(Mathf.RoundToInt(workload.CapacityRatio * 10f), 0, 15);
            int shown = Math.Min(10, bars);
            string filled = new string('█', shown);
            string over = bars > 10 ? new string('▓', Math.Min(5, bars - 10)) : "";
            string empty = new string('░', Math.Max(0, 10 - shown));
            _dispatchCapacityBarText.text = string.Format(LocalizationService.Get("personnel.dispatch.capacity_format"),
                statusColor, filled, over, empty,
                (workload.CapacityRatio * 100f).ToString("F0"));

            // Dispatchers list
            var sb = new System.Text.StringBuilder();
            var active = DispatcherService.GetActiveDispatchers();
            if (active.Count == 0)
            {
                sb.Append(LocalizationService.Get("personnel.dispatch.no_dispatchers"));
            }
            else
            {
                foreach (var d in active)
                {
                    string stars = new string('★', d.skill) + new string('☆', 5 - d.skill);
                    int cap = RoleDefinitions.GetDispatcherCapacity(d.skill);
                    sb.AppendLine(string.Format(LocalizationService.Get("personnel.dispatch.dispatcher_row_format"),
                        d.employeeId, d.DisplayFullName, stars, cap, d.currentShift));
                }
                sb.AppendLine();
                sb.AppendLine(string.Format(LocalizationService.Get("personnel.dispatch.weights_format"),
                    PersonnelBalanceConstants.DispatcherWeightProximity.ToString("F1"),
                    PersonnelBalanceConstants.DispatcherWeightSkillMatch.ToString("F1"),
                    PersonnelBalanceConstants.DispatcherWeightRestedness.ToString("F1")));
            }
            _dispatchDispatchersListText.text = sb.ToString();

            // Pending list
            foreach (var r in _dispatchPendingRows) if (r != null) Destroy(r);
            _dispatchPendingRows.Clear();

            var pending = DispatcherService.PendingActions;
            if (pending.Count == 0)
            {
                var empty2 = UiHelper.CreateText(_dispatchPendingListContent, "Empty",
                    LocalizationService.Get("personnel.dispatch.empty_pending"), 12, TextAlignmentOptions.Center);
                empty2.color = UITheme.SecondaryText;
                var er = empty2.GetComponent<RectTransform>();
                er.sizeDelta = new Vector2(0, 40);
                var emptyBg = empty2.gameObject.AddComponent<Image>();
                UITheme.ApplySurface(emptyBg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);
                _dispatchPendingRows.Add(empty2.gameObject);
            }
            else
            {
                foreach (var a in pending) AddPendingRow(a);
            }
        }

        void AddPendingRow(PendingDispatchAction a)
        {
            var row = new GameObject($"Pending_{a.actionId}");
            row.transform.SetParent(_dispatchPendingListContent, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 60);

            Color rowColor = a.status switch
            {
                PendingActionStatus.Processing => UITheme.WithAlpha(UITheme.PrimaryAccent, 0.18f),
                PendingActionStatus.Delayed => UITheme.WithAlpha(UITheme.Warning, 0.2f),
                PendingActionStatus.Done => UITheme.WithAlpha(UITheme.Success, 0.2f),
                PendingActionStatus.Failed => UITheme.WithAlpha(UITheme.Danger, 0.24f),
                _ => UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f)
            };
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, rowColor, UIShapePreset.Inset);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            hlg.spacing = UITheme.Spacing.Sm;

            var vacEmp = PersonnelService.GetById(a.vacancy != null ? a.vacancy.employeeId : -1);
            var replEmp = PersonnelService.GetById(a.replacementEmployeeId);

            string unknownEmp = LocalizationService.Get("personnel.dispatch.unknown_employee");
            var sbInfo = new System.Text.StringBuilder();
            sbInfo.AppendFormat(LocalizationService.Get("personnel.dispatch.pending_main_format"),
                a.actionId, a.status);
            if (a.status == PendingActionStatus.Delayed)
                sbInfo.AppendFormat(LocalizationService.Get("personnel.dispatch.pending_eta_format"),
                    a.etaHoursRemaining.ToString("F1"));
            sbInfo.AppendLine();
            sbInfo.AppendFormat(LocalizationService.Get("personnel.dispatch.pending_vacancy_format"),
                vacEmp != null ? vacEmp.DisplayFullName : unknownEmp,
                RoleDefinitions.GetDisplayNamePl(a.vacancy?.role ?? EmployeeRole.Driver));
            sbInfo.AppendFormat(LocalizationService.Get("personnel.dispatch.pending_replacement_format"),
                replEmp != null ? replEmp.DisplayFullName : unknownEmp,
                replEmp != null ? $"{replEmp.skill}★" : "");
            if (a.dispatcherSkillUsed > 0)
                sbInfo.AppendFormat(LocalizationService.Get("personnel.dispatch.pending_dispatcher_used_format"),
                    a.dispatcherSkillUsed);

            var info = UiHelper.CreateText(row.transform, "Info", sbInfo.ToString(), 11, TextAlignmentOptions.MidlineLeft);
            info.richText = true;
            var ile = info.gameObject.AddComponent<LayoutElement>();
            ile.flexibleWidth = 1;

            if (a.status == PendingActionStatus.Processing || a.status == PendingActionStatus.Delayed)
            {
                var cancelBtn = UiHelper.CreateButton(row.transform, "Cancel", LocalizationService.Get("personnel.dispatch.cancel_btn"),
                    () => DispatcherService.CancelPendingAction(a.actionId));
                var cle = cancelBtn.gameObject.AddComponent<LayoutElement>();
                cle.preferredWidth = 90;
            }

            _dispatchPendingRows.Add(row);
        }
    }
}
