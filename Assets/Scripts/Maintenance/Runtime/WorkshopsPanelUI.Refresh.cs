using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Maintenance
{
    public partial class WorkshopsPanelUI
    {
        // ═══════════════════════════════════════════
        //  REFRESH — slots (lewa kolumna) + overdue (prawa kolumna z przyciskami)
        // ═══════════════════════════════════════════

        void RefreshSlots(WorkshopManager wm)
        {
            var sb = new StringBuilder();
            string accent = ToHtmlColor(UITheme.PrimaryAccent);
            string muted = ToHtmlColor(UITheme.SecondaryText);
            string success = ToHtmlColor(UITheme.Success);
            string warning = ToHtmlColor(UITheme.Warning);

            if (wm.Slots.Count == 0)
            {
                sb.AppendLine(LocalizationService.Get("maintenance.workshops.slots.no_halls"));
                sb.AppendLine();
                sb.AppendLine(LocalizationService.Get("maintenance.workshops.slots.no_halls_hint"));
                _slotsText.text = sb.ToString();
                return;
            }

            // Grupuj po roomId — hala = grupa slotów
            var byRoom = new Dictionary<int, List<WorkshopSlot>>();
            foreach (var s in wm.Slots)
            {
                if (!byRoom.TryGetValue(s.roomId, out var list))
                {
                    list = new List<WorkshopSlot>();
                    byRoom[s.roomId] = list;
                }
                list.Add(s);
            }

            long nowGT = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;

            int hallIndex = 1;
            foreach (var kvp in byRoom)
            {
                var list = kvp.Value;
                if (list.Count == 0) continue;
                var level = list[0].level;

                sb.AppendLine($"<color={accent}><b>{string.Format(LocalizationService.Get("maintenance.workshops.slots.hall_name_format"), hallIndex, level.DisplayName())}</b></color>");
                sb.AppendLine($"<color={muted}>{string.Format(LocalizationService.Get("maintenance.workshops.slots.hall_meta_format"), GetMaxInspLevelIndex(level) + 1, list.Count)}</color>");
                sb.AppendLine();

                for (int i = 0; i < list.Count; i++)
                {
                    var slot = list[i];
                    if (slot.occupyingVehicleId < 0)
                    {
                        sb.AppendLine($"<color={success}>{string.Format(LocalizationService.Get("maintenance.workshops.slots.slot_free_format"), slot.slotId)}</color>");
                    }
                    else
                    {
                        var v = FleetService.GetOwnedById(slot.occupyingVehicleId);
                        string vName = v != null ? $"#{v.id} {v.seriesId}"
                            : string.Format(LocalizationService.Get("maintenance.workshops.slots.vehicle_fallback_format"), slot.occupyingVehicleId);
                        long remaining = slot.finishesGameTime - nowGT;
                        string eta = InspectionSchedule.FormatRemainingTime(remaining);
                        sb.AppendLine($"<color={warning}>{string.Format(LocalizationService.Get("maintenance.workshops.slots.slot_busy_format"), slot.slotId, slot.currentInspection, vName)}</color>");
                        sb.AppendLine($"<color={muted}>{string.Format(LocalizationService.Get("maintenance.workshops.slots.slot_eta_format"), eta)}</color>");
                    }
                }
                sb.AppendLine();
                hallIndex++;
            }

            // MM-13 / MM-D23: sekcja modernizacji internal (Hall lvl5) z MM-10
            if (ModernizationJobService.ActiveJobs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"<color={accent}><b>Modernizacje (wlasny warsztat)</b></color>");
                sb.AppendLine();
                foreach (var j in ModernizationJobService.ActiveJobs)
                {
                    if (j.mode != ModernizationMode.Internal) continue;
                    var v = FleetService.GetOwnedById(j.vehicleId);
                    var path = ModernizationPathCatalog.GetByPathId(j.pathId);
                    string vName = v != null ? $"#{v.id} {v.seriesId}" : $"#{j.vehicleId}";
                    string pathName = path != null ? path.displayName : j.pathId;
                    long remaining = j.completionGameTime - nowGT;
                    if (remaining < 0) remaining = 0;
                    sb.AppendLine($"• {vName} — {pathName}");
                    sb.AppendLine($"   ETA {InspectionSchedule.FormatRemainingTime(remaining)}, koszt {j.costPlnTotal / 1_000_000f:F1}M zł, slot ServicePit#{j.internalServicePitInstanceId}");
                }
            }

            // MM-13 / MM-D23: sekcja modyfikacji posiadanych internal z MM-11
            if (VehicleModificationJobService.ActiveJobs.Count > 0)
            {
                bool anyInternal = false;
                foreach (var j in VehicleModificationJobService.ActiveJobs)
                    if (j.mode == ModernizationMode.Internal) { anyInternal = true; break; }
                if (anyInternal)
                {
                    sb.AppendLine();
                sb.AppendLine($"<color={accent}><b>Modyfikacje (wlasny warsztat)</b></color>");
                    sb.AppendLine();
                    foreach (var j in VehicleModificationJobService.ActiveJobs)
                    {
                        if (j.mode != ModernizationMode.Internal) continue;
                        var v = FleetService.GetOwnedById(j.vehicleId);
                        var mod = VehicleModificationCatalog.GetByModId(j.modId);
                        string vName = v != null ? $"#{v.id} {v.seriesId}" : $"#{j.vehicleId}";
                        string modName = mod != null ? mod.displayName : j.modId;
                        long remaining = j.completionGameTime - nowGT;
                        if (remaining < 0) remaining = 0;
                        sb.AppendLine($"• {vName} — {modName}");
                        sb.AppendLine($"   ETA {InspectionSchedule.FormatRemainingTime(remaining)}, koszt {j.costPlnTotal / 1_000_000f:F2}M zł");
                    }
                }
            }

            // MM-13 / MM-D23: sekcja self-paint (paint_bay) z MM-12
            if (SelfPaintingService.ActiveJobs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"<color={accent}><b>Self-paint (paint_bay)</b></color>");
                sb.AppendLine();
                foreach (var j in SelfPaintingService.ActiveJobs)
                {
                    var v = FleetService.GetOwnedById(j.vehicleId);
                    string vName = v != null ? $"#{v.id} {v.seriesId}" : $"#{j.vehicleId}";
                    long remaining = j.completionGameTime - nowGT;
                    if (remaining < 0) remaining = 0;
                    sb.AppendLine($"• {vName} — malowanie @ paint_bay#{j.paintBayInstanceId}");
                    sb.AppendLine($"   ETA {InspectionSchedule.FormatRemainingTime(remaining)}, koszt {j.costPln / 1000f:F0}k zł");
                }
            }

            // M7-5: Sekcja zadań zewnętrznych (ZNTK)
            if (wm.ExternalJobs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(LocalizationService.Get("maintenance.workshops.external.header"));
                sb.AppendLine();
                foreach (var j in wm.ExternalJobs)
                {
                    var v = FleetService.GetOwnedById(j.vehicleId);
                    var w = ExternalWorkshopCatalog.GetById(j.workshopId);
                    string vName = v != null ? $"#{v.id} {v.seriesId}"
                        : string.Format(LocalizationService.Get("maintenance.workshops.slots.vehicle_fallback_format"), j.vehicleId);
                    string wName = w != null ? w.name : j.workshopId;

                    long remaining;
                    string phaseLabel;
                    string phaseColor;
                    if (j.phase == ExternalJobPhase.DeliveringOut)
                    {
                        remaining = j.deliveryOutFinishGT - nowGT;
                        phaseLabel = LocalizationService.Get("maintenance.workshops.external.phase_out");
                        phaseColor = ToHtmlColor(UITheme.PrimaryAccent);
                    }
                    else if (j.phase == ExternalJobPhase.InInspection)
                    {
                        remaining = j.inspectionFinishGT - nowGT;
                        phaseLabel = LocalizationService.Get("maintenance.workshops.external.phase_inspection");
                        phaseColor = ToHtmlColor(UITheme.Warning);
                    }
                    else
                    {
                        remaining = j.deliveryBackFinishGT - nowGT;
                        phaseLabel = LocalizationService.Get("maintenance.workshops.external.phase_back");
                        phaseColor = ToHtmlColor(UITheme.PrimaryAccent);
                    }
                    if (remaining < 0) remaining = 0;

                    sb.AppendLine(string.Format(LocalizationService.Get("maintenance.workshops.external.row_format"),
                        phaseColor, vName, j.level, wName));
                    sb.AppendLine(string.Format(LocalizationService.Get("maintenance.workshops.external.row_eta_format"),
                        phaseLabel,
                        InspectionSchedule.FormatRemainingTime(remaining),
                        (j.totalCostGroszy / 100f).ToString("F0")));
                }
            }

            _slotsText.text = sb.ToString();
        }

        static int GetMaxInspLevelIndex(WorkshopLevel level) => level switch
        {
            // MM-4: nowy mapping (Hall lvl 1 = P1 only, Hall lvl 5 = P1-P5)
            WorkshopLevel.Lvl1 => 0,           // P1 → max index 0
            WorkshopLevel.Lvl2 => 1,           // P1-P2 → max index 1
            WorkshopLevel.Lvl3 => 2,           // P1-P3 → max index 2
            WorkshopLevel.Lvl4 => 3,           // P1-P4 → max index 3
            WorkshopLevel.Lvl5 => 4,           // P1-P5 → max index 4
            _ => -1,
        };

        void RefreshOverdue(WorkshopManager wm)
        {
            // Wyczyść stare przyciski
            foreach (var b in _assignButtons)
                if (b != null) Destroy(b.gameObject);
            _assignButtons.Clear();

            // Wyczyść stare wpisy (pod tekstem "overdue")
            for (int i = _overdueContent.childCount - 1; i >= 0; i--)
            {
                var child = _overdueContent.GetChild(i);
                if (child.name != "Header") Destroy(child.gameObject);
            }

            var overdue = wm.GetOverdueVehicles(0.8f);
            string overdueHeader = LocalizationService.Get("maintenance.workshops.overdue.header");
            if (overdue.Count == 0)
            {
                _overdueText.text = overdueHeader + LocalizationService.Get("maintenance.workshops.overdue.all_current");
                return;
            }

            _overdueText.text = overdueHeader + string.Format(LocalizationService.Get("maintenance.workshops.overdue.header_with_count_format"), overdue.Count);

            // Lista wpisów z przyciskami "Przydziel"
            foreach (var entry in overdue)
            {
                CreateOverdueRow(entry, wm);
            }
        }

        void CreateOverdueRow(WorkshopManager.OverdueEntry entry, WorkshopManager wm)
        {
            var row = new GameObject($"Row_{entry.vehicle.id}", typeof(RectTransform));
            row.transform.SetParent(_overdueContent, false);
            var rt = row.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 88f);

            var bg = row.AddComponent<Image>();
            UITheme.ApplySurface(bg,
                entry.progress >= 1f
                    ? UITheme.WithAlpha(UITheme.Danger, 0.3f)
                    : UITheme.WithAlpha(UITheme.Warning, 0.28f),
                UIShapePreset.Inset);

            // Tekst po lewej — info o pojeździe
            string urgencyColor = ToHtmlColor(entry.progress >= 1f ? UITheme.Danger : UITheme.Warning);
            string progressText = $"{entry.progress * 100f:F0}%";
            string info = string.Format(LocalizationService.Get("maintenance.workshops.overdue.row_info_format"),
                entry.vehicle.id, entry.vehicle.seriesId, entry.level, urgencyColor, progressText, entry.vehicle.mileageKm.ToString("F0"));

            var txt = AddText(row.transform, "Info", info, 13, TextAlignmentOptions.MidlineLeft, UIThemeTextRole.Primary);
            txt.rectTransform.anchorMin = new Vector2(0f, 0.34f);
            txt.rectTransform.anchorMax = new Vector2(0.56f, 1f);
            txt.rectTransform.offsetMin = new Vector2(12f, 10f);
            txt.rectTransform.offsetMax = new Vector2(-8f, -10f);
            txt.raycastTarget = false;

            // Przycisk "Przydziel" (własny warsztat) po prawej — kolumna 1
            var avail = wm.GetAvailableSlots(entry.level);
            string missingParts = WorkshopManager.GetMissingPartsLabel(entry.level);
            bool partsOk = missingParts == null;

            string availabilityText = avail.Count == 0
                ? "Brak wolnych slotow w hali."
                : $"Dostepne sloty: {avail.Count}.";
            string partsText = partsOk
                ? "Czesci gotowe do przydzialu."
                : $"Brakuje: {missingParts}.";

            var subTxt = AddText(row.transform, "Hint",
                $"{availabilityText}  {partsText}",
                11, TextAlignmentOptions.TopLeft, UIThemeTextRole.Secondary);
            subTxt.rectTransform.anchorMin = new Vector2(0f, 0f);
            subTxt.rectTransform.anchorMax = new Vector2(0.56f, 0.38f);
            subTxt.rectTransform.offsetMin = new Vector2(12f, 8f);
            subTxt.rectTransform.offsetMax = new Vector2(-8f, -6f);
            subTxt.raycastTarget = false;

            string btnLabel;
            if (avail.Count == 0) btnLabel = LocalizationService.Get("maintenance.workshops.overdue.btn_no_slot");
            else if (!partsOk) btnLabel = LocalizationService.Get("maintenance.workshops.overdue.btn_no_parts");
            else btnLabel = LocalizationService.Get("maintenance.workshops.overdue.btn_assign");

            var btn = CreateButton(row.transform, btnLabel,
                new Vector2(-164f, 0f), new Vector2(148f, 48f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            if (avail.Count > 0 && partsOk)
            {
                UITheme.ApplySurface(btn.image, UITheme.WithAlpha(UITheme.Success, 0.78f), UIShapePreset.Button);
                btn.colors = UITheme.CreateColorBlock(
                    UITheme.WithAlpha(UITheme.Success, 0.78f),
                    UITheme.Success,
                    UITheme.Darken(UITheme.Success, 0.18f),
                    UITheme.WithAlpha(UITheme.Success, 0.78f),
                    UITheme.WithAlpha(UITheme.Border, 0.55f));
                int vid = entry.vehicle.id;
                var lvl = entry.level;
                int slotId = avail[0].slotId;
                btn.onClick.AddListener(() =>
                {
                    if (WorkshopManager.Instance != null)
                    {
                        bool ok = WorkshopManager.Instance.AssignVehicle(vid, lvl, slotId);
                        if (ok) _refreshTimer = RefreshInterval; // force refresh
                    }
                });
            }
            else
            {
                UITheme.ApplySurface(btn.image, UITheme.WithAlpha(UITheme.Danger, 0.5f), UIShapePreset.Button);
                btn.interactable = false;
            }
            _assignButtons.Add(btn);

            // M7-5: Przycisk "ZNTK" (external) — kolumna 2
            var externalBtn = CreateButton(row.transform, LocalizationService.Get("maintenance.workshops.overdue.btn_external"),
                new Vector2(-12f, 0f), new Vector2(148f, 48f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            UITheme.ApplySurface(externalBtn.image, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.78f), UIShapePreset.Button);
            externalBtn.colors = UITheme.CreateColorBlock(
                UITheme.WithAlpha(UITheme.PrimaryAccent, 0.78f),
                UITheme.PrimaryAccentHover,
                UITheme.Darken(UITheme.PrimaryAccentHover, 0.18f),
                UITheme.WithAlpha(UITheme.PrimaryAccent, 0.78f),
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            int capturedVid = entry.vehicle.id;
            var capturedLvl = entry.level;
            var capturedType = entry.vehicle.type;
            externalBtn.onClick.AddListener(() => ShowExternalPicker(capturedVid, capturedLvl, capturedType));
            _assignButtons.Add(externalBtn);
        }

        static string ToHtmlColor(Color color)
        {
            return $"#{ColorUtility.ToHtmlStringRGB(color)}";
        }
    }
}
