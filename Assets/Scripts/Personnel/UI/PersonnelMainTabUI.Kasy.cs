using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Personnel
{
    public partial class PersonnelMainTabUI
    {
        // ═══ Kasy content (M8-15) ═══

        TextMeshProUGUI _kasySummaryText;
        RectTransform _kasyListContent;
        readonly List<GameObject> _kasyRows = new();

        void BuildKasyContent()
        {
            var parent = _kasyContent.transform;

            var summaryCard = UiHelper.CreatePanel(parent, "KasySummaryCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            var summaryRt = summaryCard.GetComponent<RectTransform>();
            summaryRt.anchorMin = new Vector2(0, 1); summaryRt.anchorMax = new Vector2(1, 1);
            summaryRt.pivot = new Vector2(0.5f, 1f);
            summaryRt.anchoredPosition = new Vector2(0, -8);
            summaryRt.sizeDelta = new Vector2(-20, 132);

            _kasySummaryText = UiHelper.CreateText(summaryCard.transform, "KasySum", "", 13, TextAlignmentOptions.TopLeft);
            _kasySummaryText.richText = true;
            var sr = _kasySummaryText.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0, 0); sr.anchorMax = new Vector2(1, 1);
            sr.offsetMin = new Vector2(18, 16); sr.offsetMax = new Vector2(-18, -16);

            var actionCard = UiHelper.CreatePanel(parent, "KasyActionCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);
            var actionRt = actionCard.GetComponent<RectTransform>();
            actionRt.anchorMin = new Vector2(0, 1); actionRt.anchorMax = new Vector2(1, 1);
            actionRt.pivot = new Vector2(0.5f, 1f);
            actionRt.anchoredPosition = new Vector2(0, -148);
            actionRt.sizeDelta = new Vector2(-20, 52);

            var autoBtn = UiHelper.CreateButton(actionCard.transform, "AutoAssign",
                LocalizationService.Get("personnel.kasy.auto_assign_btn"),
                () => TicketClerkService.Instance?.DebugAutoAssignSampleStations());
            var ar = autoBtn.GetComponent<RectTransform>();
            ar.anchorMin = new Vector2(0, 0.5f); ar.anchorMax = new Vector2(1, 0.5f);
            ar.pivot = new Vector2(0.5f, 0.5f);
            ar.anchoredPosition = Vector2.zero;
            ar.sizeDelta = new Vector2(-24, 34);

            var scroll = new GameObject("KasyScroll");
            scroll.transform.SetParent(parent, false);
            var scRt = scroll.AddComponent<RectTransform>();
            scRt.anchorMin = new Vector2(0, 0); scRt.anchorMax = new Vector2(1, 1);
            scRt.offsetMin = new Vector2(10, 10); scRt.offsetMax = new Vector2(-10, -210);
            var scrollImg = scroll.AddComponent<Image>();
            UITheme.ApplySurface(scrollImg, UITheme.WithAlpha(UITheme.PrimarySurface, 0.32f), UIShapePreset.Panel);

            var srComp = scroll.AddComponent<ScrollRect>();
            srComp.horizontal = false; srComp.vertical = true;

            var vp = new GameObject("Viewport");
            vp.transform.SetParent(scroll.transform, false);
            var vprt = vp.AddComponent<RectTransform>();
            vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
            vprt.offsetMin = new Vector2(6, 6); vprt.offsetMax = new Vector2(-6, -6);
            var viewportImg = vp.AddComponent<Image>();
            UITheme.ApplySurface(viewportImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.9f), UIShapePreset.Inset);
            vp.AddComponent<Mask>().showMaskGraphic = true;
            srComp.viewport = vprt;

            var content = new GameObject("Content");
            content.transform.SetParent(vp.transform, false);
            _kasyListContent = content.AddComponent<RectTransform>();
            _kasyListContent.anchorMin = new Vector2(0, 1); _kasyListContent.anchorMax = new Vector2(1, 1);
            _kasyListContent.pivot = new Vector2(0.5f, 1);
            _kasyListContent.anchoredPosition = Vector2.zero;
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = false; vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vlg.spacing = UITheme.Spacing.Sm;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            srComp.content = _kasyListContent;
        }

        void RefreshKasyContent()
        {
            if (_kasySummaryText == null) return;

            int clerks = PersonnelService.CountActiveByRole(EmployeeRole.TicketClerk);
            int assigned = 0;
            foreach (var e in PersonnelService.Employees)
                if (e.role == EmployeeRole.TicketClerk && e.IsActive && e.assignedStationId >= 0)
                    assigned++;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(LocalizationService.Get("personnel.kasy.summary_title"));
            sb.AppendLine();
            sb.AppendLine(string.Format(LocalizationService.Get("personnel.kasy.stats_format"),
                clerks, assigned, clerks - assigned));
            sb.AppendLine();
            sb.AppendLine(string.Format(LocalizationService.Get("personnel.kasy.bonus_format"),
                (PersonnelBalanceConstants.TicketClerkRevenueBonus * 100).ToString("F0")));
            _kasySummaryText.text = sb.ToString();

            foreach (var r in _kasyRows) if (r != null) Destroy(r);
            _kasyRows.Clear();

            if (TicketClerkService.AllAssignments.Count == 0)
            {
                var empty = UiHelper.CreateText(_kasyListContent, "Empty",
                    LocalizationService.Get("personnel.kasy.empty"),
                    12, TextAlignmentOptions.Center);
                empty.color = UITheme.SecondaryText;
                var er = empty.GetComponent<RectTransform>();
                er.sizeDelta = new Vector2(0, 50);
                var emptyImg = empty.gameObject.AddComponent<Image>();
                UITheme.ApplySurface(emptyImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);
                _kasyRows.Add(empty.gameObject);
                return;
            }

            foreach (var kv in TicketClerkService.AllAssignments)
                AddKasyRow(kv.Value);
        }

        void AddKasyRow(StationAssignment a)
        {
            var row = new GameObject($"Kasy_{a.stationId}");
            row.transform.SetParent(_kasyListContent, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 50);
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Inset);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childControlHeight = true; hlg.childControlWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.padding = UITheme.Padding(UITheme.Spacing.Md, UITheme.Spacing.Sm);
            hlg.spacing = UITheme.Spacing.Sm;

            var emp = PersonnelService.GetById(a.employeeId);
            string empText = emp != null ? emp.DisplayFullName : LocalizationService.Get("personnel.kasy.unknown_employee");
            string skillText = emp != null ? new string('★', emp.skill) + new string('☆', 5 - emp.skill) : "";

            var info = UiHelper.CreateText(row.transform, "Info",
                string.Format(LocalizationService.Get("personnel.kasy.row_format"),
                    a.stationId, empText, skillText, a.shift, a.assignedSinceDateIso),
                11, TextAlignmentOptions.MidlineLeft);
            info.richText = true;
            var ile = info.gameObject.AddComponent<LayoutElement>();
            ile.flexibleWidth = 1;

            int stationId = a.stationId;
            var unassignBtn = UiHelper.CreateButton(row.transform, "Unassign", LocalizationService.Get("personnel.kasy.unassign_btn"),
                () => TicketClerkService.Unassign(stationId));
            var uble = unassignBtn.gameObject.AddComponent<LayoutElement>();
            uble.preferredWidth = 92;
            var ubImg = unassignBtn.GetComponent<Image>();
            if (ubImg != null) UITheme.ApplySurface(ubImg, UITheme.WithAlpha(UITheme.Danger, 0.92f), UIShapePreset.Button);

            _kasyRows.Add(row);
        }
    }
}
