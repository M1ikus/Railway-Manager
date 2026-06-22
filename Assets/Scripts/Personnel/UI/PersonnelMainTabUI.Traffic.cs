using System;
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
        // ═══ Traffic content (M8-11 Nastawnia) ═══

        TextMeshProUGUI _trafficHeaderText;
        TextMeshProUGUI _trafficControllersListText;
        TextMeshProUGUI _trafficBarText;
        TMP_InputField _priWorkshopInput;
        TMP_InputField _priDepartureInput;
        TMP_InputField _priWashBayInput;
        TMP_InputField _priParkingInput;

        void BuildTrafficContent()
        {
            var parent = _trafficContent.transform;

            var summaryCard = UiHelper.CreatePanel(parent, "TrafficSummaryCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            var summaryRt = summaryCard.GetComponent<RectTransform>();
            summaryRt.anchorMin = new Vector2(0, 1); summaryRt.anchorMax = new Vector2(1, 1);
            summaryRt.pivot = new Vector2(0.5f, 1f);
            summaryRt.anchoredPosition = new Vector2(0, -8);
            summaryRt.sizeDelta = new Vector2(-20, 106);

            _trafficHeaderText = UiHelper.CreateText(summaryCard.transform, "THdr", "", 16, TextAlignmentOptions.TopLeft);
            _trafficHeaderText.richText = true;
            var hr = _trafficHeaderText.GetComponent<RectTransform>();
            hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1);
            hr.pivot = new Vector2(0, 1);
            hr.anchoredPosition = new Vector2(18, -16);
            hr.sizeDelta = new Vector2(-36, 58);

            _trafficBarText = UiHelper.CreateText(summaryCard.transform, "TBar", "", 14, TextAlignmentOptions.MidlineLeft);
            var br = _trafficBarText.GetComponent<RectTransform>();
            br.anchorMin = new Vector2(0, 1); br.anchorMax = new Vector2(1, 1);
            br.pivot = new Vector2(0, 1);
            br.anchoredPosition = new Vector2(18, -74);
            br.sizeDelta = new Vector2(-36, 20);

            var controllersCard = UiHelper.CreatePanel(parent, "ControllersCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);
            var ctrlCardRt = controllersCard.GetComponent<RectTransform>();
            ctrlCardRt.anchorMin = new Vector2(0, 0); ctrlCardRt.anchorMax = new Vector2(0.55f, 1);
            ctrlCardRt.offsetMin = new Vector2(10, 100); ctrlCardRt.offsetMax = new Vector2(-5, -125);

            // Controllers list (left)
            var ctrlLbl = UiHelper.CreateText(controllersCard.transform, "CtrlLbl",
                LocalizationService.Get("personnel.traffic.controllers_label"), 13, TextAlignmentOptions.TopLeft);
            ctrlLbl.fontStyle = FontStyles.Bold;
            var cll = ctrlLbl.GetComponent<RectTransform>();
            cll.anchorMin = new Vector2(0, 1); cll.anchorMax = new Vector2(1, 1);
            cll.pivot = new Vector2(0, 1);
            cll.anchoredPosition = new Vector2(16, -14);
            cll.sizeDelta = new Vector2(-32, 24);

            _trafficControllersListText = UiHelper.CreateText(controllersCard.transform, "CtrlList", "", 12, TextAlignmentOptions.TopLeft);
            _trafficControllersListText.richText = true;
            _trafficControllersListText.color = UITheme.PrimaryText;
            var ctl = _trafficControllersListText.GetComponent<RectTransform>();
            ctl.anchorMin = new Vector2(0, 0); ctl.anchorMax = new Vector2(1, 1);
            ctl.pivot = new Vector2(0, 1);
            ctl.anchoredPosition = new Vector2(16, -42);
            ctl.sizeDelta = new Vector2(-32, -58);

            var prioritiesCard = UiHelper.CreatePanel(parent, "PrioritiesCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);
            var priCardRt = prioritiesCard.GetComponent<RectTransform>();
            priCardRt.anchorMin = new Vector2(0.55f, 0); priCardRt.anchorMax = new Vector2(1, 1);
            priCardRt.offsetMin = new Vector2(5, 100); priCardRt.offsetMax = new Vector2(-10, -125);

            // Priority sliders (right)
            var priLbl = UiHelper.CreateText(prioritiesCard.transform, "PriLbl",
                LocalizationService.Get("personnel.traffic.priorities_label"), 13, TextAlignmentOptions.TopLeft);
            priLbl.fontStyle = FontStyles.Bold;
            var pll = priLbl.GetComponent<RectTransform>();
            pll.anchorMin = new Vector2(0, 1); pll.anchorMax = new Vector2(1, 1);
            pll.pivot = new Vector2(0, 1);
            pll.anchoredPosition = new Vector2(16, -14);
            pll.sizeDelta = new Vector2(-32, 24);

            BuildPrioritySlider(prioritiesCard.transform, LocalizationService.Get("personnel.traffic.pri_workshop"), -54, out _priWorkshopInput);
            BuildPrioritySlider(prioritiesCard.transform, LocalizationService.Get("personnel.traffic.pri_departure"), -96, out _priDepartureInput);
            BuildPrioritySlider(prioritiesCard.transform, LocalizationService.Get("personnel.traffic.pri_wash_bay"), -138, out _priWashBayInput);
            BuildPrioritySlider(prioritiesCard.transform, LocalizationService.Get("personnel.traffic.pri_parking"), -180, out _priParkingInput);

            var applyBtn = UiHelper.CreateButton(prioritiesCard.transform, "ApplyPri", LocalizationService.Get("personnel.traffic.apply_btn"),
                OnApplyPriorities);
            var abr = applyBtn.GetComponent<RectTransform>();
            abr.anchorMin = new Vector2(0, 1); abr.anchorMax = new Vector2(0, 1);
            abr.pivot = new Vector2(0, 1);
            abr.anchoredPosition = new Vector2(16, -236);
            abr.sizeDelta = new Vector2(180, 34);

            var resetBtn = UiHelper.CreateButton(prioritiesCard.transform, "ResetPri", LocalizationService.Get("personnel.traffic.reset_btn"),
                () => { TrafficControlService.ResetPrioritiesToDefault(); RefreshTrafficContent(); });
            var rbr = resetBtn.GetComponent<RectTransform>();
            rbr.anchorMin = new Vector2(0, 1); rbr.anchorMax = new Vector2(0, 1);
            rbr.pivot = new Vector2(0, 1);
            rbr.anchoredPosition = new Vector2(206, -236);
            rbr.sizeDelta = new Vector2(164, 34);

            var infoCard = UiHelper.CreatePanel(parent, "InfoCard",
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.3f), UIShapePreset.Panel);
            var infoCardRt = infoCard.GetComponent<RectTransform>();
            infoCardRt.anchorMin = new Vector2(0, 0); infoCardRt.anchorMax = new Vector2(1, 0);
            infoCardRt.pivot = new Vector2(0.5f, 0f);
            infoCardRt.anchoredPosition = new Vector2(0, 10);
            infoCardRt.sizeDelta = new Vector2(-20, 80);

            var info = UiHelper.CreateText(infoCard.transform, "InfoBox",
                LocalizationService.Get("personnel.traffic.info_box"),
                11, TextAlignmentOptions.TopLeft);
            info.color = UITheme.SecondaryText;
            var ir = info.GetComponent<RectTransform>();
            ir.anchorMin = new Vector2(0, 0); ir.anchorMax = new Vector2(1, 1);
            ir.offsetMin = new Vector2(16, 14); ir.offsetMax = new Vector2(-16, -14);
        }

        void BuildPrioritySlider(Transform parent, string labelText, float yOffset, out TMP_InputField inputOut)
        {
            var row = UiHelper.CreatePanel(parent, $"PriRow_{labelText}",
                UITheme.WithAlpha(UITheme.PrimarySurface, 0.28f), UIShapePreset.Inset);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = new Vector2(0, 1); rowRt.anchorMax = new Vector2(1, 1);
            rowRt.pivot = new Vector2(0, 1);
            rowRt.anchoredPosition = new Vector2(16, yOffset);
            rowRt.sizeDelta = new Vector2(-32, 34);

            var lbl = UiHelper.CreateText(row.transform, $"Pri_{labelText}", labelText, 12, TextAlignmentOptions.MidlineLeft);
            var lr = lbl.GetComponent<RectTransform>();
            lr.anchorMin = new Vector2(0, 0); lr.anchorMax = new Vector2(1, 1);
            lr.pivot = new Vector2(0, 1);
            lr.offsetMin = new Vector2(12, 4);
            lr.offsetMax = new Vector2(-110, -4);

            inputOut = UiHelper.CreateInputField(row.transform, $"In_{labelText}", "0");
            inputOut.contentType = TMP_InputField.ContentType.IntegerNumber;
            var ir = inputOut.GetComponent<RectTransform>();
            ir.anchorMin = new Vector2(1, 0.5f); ir.anchorMax = new Vector2(1, 0.5f);
            ir.pivot = new Vector2(1, 0.5f);
            ir.anchoredPosition = new Vector2(-12, 0);
            ir.sizeDelta = new Vector2(88, 26);
        }

        void OnApplyPriorities()
        {
            int workshop = ParseIntOrDefault(_priWorkshopInput?.text, TrafficControlService.PriorityWorkshopOverdue);
            int dep = ParseIntOrDefault(_priDepartureInput?.text, TrafficControlService.PriorityScheduledDeparture);
            int wash = ParseIntOrDefault(_priWashBayInput?.text, TrafficControlService.PriorityWashBayPlanned);
            int park = ParseIntOrDefault(_priParkingInput?.text, TrafficControlService.PriorityParkingReshuffle);
            TrafficControlService.UpdatePrioritySliders(workshop, dep, wash, park);
            RefreshTrafficContent();
        }

        static int ParseIntOrDefault(string s, int defaultVal)
        {
            if (string.IsNullOrWhiteSpace(s)) return defaultVal;
            return int.TryParse(s.Trim(), out var v) ? v : defaultVal;
        }

        void RefreshTrafficContent()
        {
            if (_trafficHeaderText == null) return;

            var w = TrafficControlService.GetWorkload();

            string statusColor = w.status switch
            {
                TrafficControllerStatus.Normal => "#4ADE80",
                TrafficControllerStatus.Queued => "#FBBF24",
                TrafficControllerStatus.Critical => "#F87171",
                _ => "#FFFFFF"
            };
            string statusLabel = w.status switch
            {
                TrafficControllerStatus.Normal => LocalizationService.Get("personnel.traffic.status.normal"),
                TrafficControllerStatus.Queued => LocalizationService.Get("personnel.traffic.status.queued"),
                TrafficControllerStatus.Critical => w.noControllerFallback
                    ? LocalizationService.Get("personnel.traffic.status.critical_no_workers")
                    : LocalizationService.Get("personnel.traffic.status.critical"),
                _ => LocalizationService.Get("personnel.traffic.status.unknown")
            };

            _trafficHeaderText.text = string.Format(LocalizationService.Get("personnel.traffic.header_format"),
                statusColor, statusLabel,
                w.activeControllerCount, w.totalCapacity,
                w.activeTasksCount, w.pendingTasksCount);

            int bars = Mathf.Clamp(Mathf.RoundToInt(w.CapacityRatio * 10f), 0, 15);
            int shown = Math.Min(10, bars);
            string filled = new string('█', shown);
            string over = bars > 10 ? new string('▓', Math.Min(5, bars - 10)) : "";
            string empty = new string('░', Math.Max(0, 10 - shown));
            _trafficBarText.text = string.Format(LocalizationService.Get("personnel.traffic.capacity_format"),
                statusColor, filled, over, empty,
                (w.CapacityRatio * 100f).ToString("F0"));

            var sb = new System.Text.StringBuilder();
            var ctrls = TrafficControlService.GetActiveControllers();
            if (ctrls.Count == 0)
            {
                sb.Append(LocalizationService.Get("personnel.traffic.no_controllers"));
            }
            else
            {
                foreach (var c in ctrls)
                {
                    string stars = new string('★', c.skill) + new string('☆', 5 - c.skill);
                    int cap = RoleDefinitions.GetTrafficControllerCapacity(c.skill);
                    sb.AppendLine(string.Format(LocalizationService.Get("personnel.traffic.controller_row_format"),
                        c.employeeId, c.DisplayFullName, stars, cap, c.currentShift));
                }
                if (ctrls.Count < 3)
                {
                    sb.AppendLine();
                    sb.AppendLine(string.Format(LocalizationService.Get("personnel.traffic.warn_need_more_format"),
                        3 - ctrls.Count, ctrls.Count));
                }
            }
            _trafficControllersListText.text = sb.ToString();

            // Update priority inputs
            if (_priWorkshopInput != null) _priWorkshopInput.SetTextWithoutNotify(TrafficControlService.PriorityWorkshopOverdue.ToString());
            if (_priDepartureInput != null) _priDepartureInput.SetTextWithoutNotify(TrafficControlService.PriorityScheduledDeparture.ToString());
            if (_priWashBayInput != null) _priWashBayInput.SetTextWithoutNotify(TrafficControlService.PriorityWashBayPlanned.ToString());
            if (_priParkingInput != null) _priParkingInput.SetTextWithoutNotify(TrafficControlService.PriorityParkingReshuffle.ToString());
        }
    }
}
