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
        // ═══ Workshops content (M8-12) ═══

        TextMeshProUGUI _workshopsHeaderText;
        RectTransform _workshopsListContent;
        readonly List<GameObject> _workshopRows = new();

        void BuildWorkshopsContent()
        {
            var parent = _workshopsContent.transform;

            var summaryCard = UiHelper.CreatePanel(parent, "WorkshopsSummaryCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            var summaryRt = summaryCard.GetComponent<RectTransform>();
            summaryRt.anchorMin = new Vector2(0, 1); summaryRt.anchorMax = new Vector2(1, 1);
            summaryRt.pivot = new Vector2(0.5f, 1f);
            summaryRt.anchoredPosition = new Vector2(0, -8);
            summaryRt.sizeDelta = new Vector2(-20, 92);

            _workshopsHeaderText = UiHelper.CreateText(summaryCard.transform, "WsHdr",
                LocalizationService.Get("personnel.workshops.title_default"), 14, TextAlignmentOptions.TopLeft);
            _workshopsHeaderText.richText = true;
            var hr = _workshopsHeaderText.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
            hr.pivot = new Vector2(0, 1);
            hr.anchoredPosition = new Vector2(18, -16);
            hr.sizeDelta = new Vector2(-36, 58);

            var scroll = new GameObject("WsScroll");
            scroll.transform.SetParent(parent, false);
            var scRt = scroll.AddComponent<RectTransform>();
            scRt.anchorMin = new Vector2(0, 0); scRt.anchorMax = new Vector2(1, 1);
            scRt.offsetMin = new Vector2(10, 10); scRt.offsetMax = new Vector2(-10, -110);
            var scrollImg = scroll.AddComponent<Image>();
            UITheme.ApplySurface(scrollImg, UITheme.WithAlpha(UITheme.PrimarySurface, 0.32f), UIShapePreset.Panel);

            var sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;

            var vp = new GameObject("Viewport");
            vp.transform.SetParent(scroll.transform, false);
            var vprt = vp.AddComponent<RectTransform>();
            vprt.anchorMin = Vector2.zero; vprt.anchorMax = Vector2.one;
            vprt.offsetMin = new Vector2(6, 6); vprt.offsetMax = new Vector2(-6, -6);
            var viewportImg = vp.AddComponent<Image>();
            UITheme.ApplySurface(viewportImg, UITheme.WithAlpha(UITheme.TopBarInset, 0.9f), UIShapePreset.Inset);
            vp.AddComponent<Mask>().showMaskGraphic = true;
            sr.viewport = vprt;

            var content = new GameObject("Content");
            content.transform.SetParent(vp.transform, false);
            _workshopsListContent = content.AddComponent<RectTransform>();
            _workshopsListContent.anchorMin = new Vector2(0, 1); _workshopsListContent.anchorMax = new Vector2(1, 1);
            _workshopsListContent.pivot = new Vector2(0.5f, 1);
            _workshopsListContent.anchoredPosition = Vector2.zero;
            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = false; vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            vlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            vlg.spacing = UITheme.Spacing.Sm;
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = _workshopsListContent;
        }

        void RefreshWorkshopsContent()
        {
            if (_workshopsListContent == null) return;

            foreach (var r in _workshopRows) if (r != null) Destroy(r);
            _workshopRows.Clear();

            var wm = RailwayManager.Maintenance.WorkshopManager.Instance;
            if (wm == null || wm.Slots.Count == 0)
            {
                _workshopsHeaderText.text = LocalizationService.Get("personnel.workshops.no_slots");
                return;
            }

            int mechanicCount = PersonnelService.CountActiveByRole(EmployeeRole.Mechanic);
            int assignedCount = 0;
            foreach (var e in PersonnelService.Employees)
                if (e.role == EmployeeRole.Mechanic && e.IsActive && WorkshopAssignmentService.IsMechanicAssigned(e.employeeId))
                    assignedCount++;

            _workshopsHeaderText.text = string.Format(LocalizationService.Get("personnel.workshops.header_format"),
                wm.Slots.Count, mechanicCount, assignedCount, mechanicCount - assignedCount);

            foreach (var slot in wm.Slots)
                AddWorkshopSlotRow(slot);
        }

        void AddWorkshopSlotRow(RailwayManager.Maintenance.WorkshopSlot slot)
        {
            var row = new GameObject($"Slot_{slot.slotId}");
            row.transform.SetParent(_workshopsListContent, false);
            var rt = row.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, 96);
            var rowImg = row.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Inset);

            // Info text
            var mechanics = WorkshopAssignmentService.GetMechanicsForSlot(slot.slotId);
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat(LocalizationService.Get("personnel.workshops.slot_main_format"), slot.slotId, slot.level);
            sb.Append(slot.occupyingVehicleId < 0
                ? LocalizationService.Get("personnel.workshops.slot_free")
                : string.Format(LocalizationService.Get("personnel.workshops.slot_busy_format"), slot.occupyingVehicleId, slot.currentInspection));
            sb.AppendLine();

            if (mechanics.Count == 0)
            {
                sb.AppendLine(LocalizationService.Get("personnel.workshops.no_mechanics"));
            }
            else
            {
                float mult = mechanics.Count == 0 ? 1f : ComputeVisualMultiplier(mechanics);
                string multColor = mult < 0.85f ? "#4ADE80" : mult > 1.15f ? "#F87171" : "#FFFFFF";
                sb.AppendFormat(LocalizationService.Get("personnel.workshops.mechanics_prefix_format"), mechanics.Count);
                for (int i = 0; i < mechanics.Count; i++)
                {
                    var m = PersonnelService.GetById(mechanics[i]);
                    if (m == null) continue;
                    sb.Append($"{m.DisplayFullName} {m.skill}★");
                    if (i < mechanics.Count - 1) sb.Append(", ");
                }
                sb.AppendLine();
                sb.AppendFormat(LocalizationService.Get("personnel.workshops.multiplier_format"),
                    multColor, mult.ToString("F2"));
            }

            var info = UiHelper.CreateText(row.transform, "Info", sb.ToString(), 11, TextAlignmentOptions.TopLeft);
            info.richText = true;
            var irt = info.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0, 0); irt.anchorMax = new Vector2(0.6f, 1);
            irt.offsetMin = new Vector2(14, 8); irt.offsetMax = new Vector2(-8, -8);

            // Unassign buttons
            float btnY = -12;
            foreach (var mid in mechanics)
            {
                var m = PersonnelService.GetById(mid);
                if (m == null) continue;
                int localMid = mid;
                int localSlot = slot.slotId;
                var unassignBtn = UiHelper.CreateButton(row.transform, $"Unassign_{localMid}",
                    string.Format(LocalizationService.Get("personnel.workshops.unassign_btn_format"), m.DisplayShortName),
                    () => WorkshopAssignmentService.Unassign(localMid, localSlot));
                var ubr = unassignBtn.GetComponent<RectTransform>();
                ubr.anchorMin = new Vector2(0.6f, 1); ubr.anchorMax = new Vector2(0.8f, 1);
                ubr.pivot = new Vector2(0, 1);
                ubr.anchoredPosition = new Vector2(8, btnY);
                ubr.sizeDelta = new Vector2(-8, 28);
                var ubImg = unassignBtn.GetComponent<Image>();
                if (ubImg != null) UITheme.ApplySurface(ubImg, UITheme.WithAlpha(UITheme.Danger, 0.92f), UIShapePreset.Button);
                btnY -= 32;
            }

            // Assign dropdown
            if (mechanics.Count < 2)
            {
                var freeIds = GetFreeMechanicIds();
                if (freeIds.Count > 0)
                {
                    var opts = new List<string> { LocalizationService.Get("personnel.workshops.assign_dd_prompt") };
                    foreach (var mid in freeIds)
                    {
                        var m = PersonnelService.GetById(mid);
                        if (m != null) opts.Add($"{m.DisplayFullName} {m.skill}★");
                    }

                    var dd = UiHelper.CreateDropdown(row.transform, "AssignDd", opts);
                    var ddr = dd.GetComponent<RectTransform>();
                    ddr.anchorMin = new Vector2(0.8f, 1); ddr.anchorMax = new Vector2(1, 1);
                    ddr.pivot = new Vector2(1, 1);
                    ddr.anchoredPosition = new Vector2(-12, -12);
                    ddr.sizeDelta = new Vector2(-12, 32);
                    int localSlot = slot.slotId;
                    var capturedFreeIds = new List<int>(freeIds);
                    dd.onValueChanged.AddListener(v =>
                    {
                        if (v <= 0) return;
                        int mechanicId = capturedFreeIds[v - 1];
                        WorkshopAssignmentService.Assign(mechanicId, localSlot);
                    });
                }
                else
                {
                    var noFree = UiHelper.CreateText(row.transform, "NoFree",
                        LocalizationService.Get("personnel.workshops.no_free_mechanics"),
                        10, TextAlignmentOptions.MidlineRight);
                    noFree.color = UITheme.SecondaryText;
                    var nfr = noFree.GetComponent<RectTransform>();
                    nfr.anchorMin = new Vector2(0.8f, 1); nfr.anchorMax = new Vector2(1, 1);
                    nfr.pivot = new Vector2(1, 1);
                    nfr.anchoredPosition = new Vector2(-12, -12);
                    nfr.sizeDelta = new Vector2(-12, 32);
                }
            }

            _workshopRows.Add(row);
        }

        static float ComputeVisualMultiplier(List<int> mechanicIds)
        {
            int totalSkill = 0, count = 0;
            foreach (var mid in mechanicIds)
            {
                var e = PersonnelService.GetById(mid);
                if (e == null || !e.IsActive) continue;
                totalSkill += e.skill;
                count++;
            }
            if (count == 0) return 1f;
            float avg = totalSkill / (float)count;
            return 1f / (0.5f + avg / 5f);
        }

        static List<int> GetFreeMechanicIds()
        {
            var result = new List<int>();
            foreach (var e in PersonnelService.Employees)
            {
                if (e.role != EmployeeRole.Mechanic) continue;
                if (!e.IsActive) continue;
                if (WorkshopAssignmentService.IsMechanicAssigned(e.employeeId)) continue;
                result.Add(e.employeeId);
            }
            return result;
        }
    }
}
