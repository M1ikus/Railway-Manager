using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class CirculationListUI
    {
        private void BuildCirculationRow(Transform parent, Circulation c)
        {
            bool expanded = _expandedIds.Contains(c.id);

            var root = new GameObject("Circ_" + c.id, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            UITheme.ApplySurface(root.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Panel);
            var rootLe = root.AddComponent<LayoutElement>();
            rootLe.flexibleWidth = 1;
            var rootVlg = root.AddComponent<VerticalLayoutGroup>();
            rootVlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            rootVlg.spacing = UITheme.Spacing.Xs;
            rootVlg.childForceExpandWidth = true;
            rootVlg.childForceExpandHeight = false;
            var rootCsf = root.AddComponent<ContentSizeFitter>();
            rootCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var header = MakeHRow(root.transform, 32);
            UITheme.ApplySurface(header.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.88f), UIShapePreset.Inset);
            int captId = c.id;
            MakeSmallBtn(
                header.transform,
                expanded ? "▼" : "▶",
                () => ToggleExpand(captId),
                new Color(0.2f, 0.25f, 0.3f),
                24);

            var nameBtnObj = new GameObject("NameBtn", typeof(RectTransform));
            nameBtnObj.transform.SetParent(header.transform, false);
            var nameBtnLe = nameBtnObj.AddComponent<LayoutElement>();
            nameBtnLe.flexibleWidth = 1;
            nameBtnLe.preferredHeight = 24;
            var nameBtnImg = nameBtnObj.AddComponent<Image>();
            UITheme.ApplySurface(nameBtnImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.32f), UIShapePreset.Inset);
            var nameBtn = nameBtnObj.AddComponent<Button>();
            nameBtn.targetGraphic = nameBtnImg;
            nameBtn.colors = UITheme.CreateColorBlock(
                UITheme.WithAlpha(UITheme.SecondarySurface, 0.32f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.72f),
                UITheme.WithAlpha(UITheme.Border, 0.88f),
                UITheme.WithAlpha(UITheme.RaisedSurface, 0.72f),
                UITheme.WithAlpha(UITheme.Border, 0.45f));
            nameBtn.onClick.AddListener(() => ToggleExpand(captId));

            var nameTxtObj = new GameObject("T", typeof(RectTransform));
            nameTxtObj.transform.SetParent(nameBtnObj.transform, false);
            var ntRt = (RectTransform)nameTxtObj.transform;
            ntRt.anchorMin = Vector2.zero;
            ntRt.anchorMax = Vector2.one;
            ntRt.offsetMin = new Vector2(4, 0);
            ntRt.offsetMax = Vector2.zero;
            var nameTx = nameTxtObj.AddComponent<TextMeshProUGUI>();
            nameTx.fontSize = 12;
            nameTx.text = c.name ?? LocalizationService.Get("timetable.circulations.row.no_name");
            nameTx.alignment = TextAlignmentOptions.MidlineLeft;
            nameTx.raycastTarget = false;
            UITheme.ApplyTmpText(nameTx, UIThemeTextRole.Primary);

            MakeStatusDropdown(header.transform, c.status, v => OnStatusChanged(captId, v));

            MakeSmallBtn(
                header.transform,
                LocalizationService.Get("timetable.circulations.row.button_options"),
                () => OnOptionsClicked(captId),
                new Color(0.35f, 0.4f, 0.5f),
                26);
            string vehicleStr = FormatVehicleShort(c.assignedVehicleIds);
            MakeSmallBtn(
                header.transform,
                vehicleStr,
                () => OnAssignVehicleClicked(captId),
                new Color(0.25f, 0.55f, 0.45f),
                110);
            MakeSmallBtn(
                header.transform,
                LocalizationService.Get("timetable.circulations.row.button_duplicate"),
                () => OnDuplicateClicked(captId),
                new Color(0.35f, 0.4f, 0.55f),
                36);
            MakeSmallBtn(
                header.transform,
                LocalizationService.Get("timetable.circulations.row.button_remove"),
                () => OnDeleteClicked(captId),
                new Color(0.6f, 0.25f, 0.25f),
                28);

            string routeStr = FormatRouteShort(c);
            string calStr = FormatDayMask(c.calendar);
            var infoCard = new GameObject("InfoCard", typeof(RectTransform));
            infoCard.transform.SetParent(root.transform, false);
            UITheme.ApplySurface(infoCard.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.44f), UIShapePreset.Inset);
            infoCard.AddComponent<LayoutElement>().preferredHeight = 36;
            var infoVlg = infoCard.AddComponent<VerticalLayoutGroup>();
            infoVlg.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Xs);
            infoVlg.spacing = UITheme.Spacing.Xxs;
            infoVlg.childForceExpandWidth = true;
            infoVlg.childForceExpandHeight = false;

            var infoLbl = MakeText(
                infoCard.transform,
                string.Format(
                    LocalizationService.Get("timetable.circulations.row.info_format"),
                    c.StepCount, routeStr, calStr),
                10,
                new Color(0.75f, 0.8f, 0.88f));
            infoLbl.gameObject.GetComponent<LayoutElement>().preferredHeight = 14;

            var hintLbl = MakeText(
                infoCard.transform,
                expanded
                    ? "Dodawaj rozklady z puli po prawej stronie i ukladaj kolejnosc krokow."
                    : "Rozwin obieg, aby zobaczyc kroki, przerwy i ostrzezenia.",
                9,
                new Color(0.55f, 0.6f, 0.68f));
            hintLbl.gameObject.GetComponent<LayoutElement>().preferredHeight = 12;

            var brokenVehicles = CirculationService.GetBrokenVehiclesInCirculation(c);
            if (brokenVehicles.Count > 0)
            {
                var sb = new StringBuilder(
                    LocalizationService.Get("timetable.circulations.row.broken_vehicles_prefix"));
                for (int i = 0; i < brokenVehicles.Count && i < 3; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var bv = brokenVehicles[i];
                    string reason = bv.status == FleetVehicleStatus.InRepair
                        ? LocalizationService.Get("timetable.circulations.row.broken_reason_repair")
                        : bv.status == FleetVehicleStatus.OutOfService
                            ? LocalizationService.Get("timetable.circulations.row.broken_reason_off")
                            : string.Format(
                                LocalizationService.Get("timetable.circulations.row.broken_reason_condition_format"),
                                bv.conditionPercent.ToString("F0"));
                    sb.Append(string.Format(
                        LocalizationService.Get("timetable.circulations.row.broken_vehicle_format"),
                        bv.series, bv.number, reason));
                }
                if (brokenVehicles.Count > 3)
                {
                    sb.Append(string.Format(
                        LocalizationService.Get("timetable.circulations.row.broken_more_format"),
                        brokenVehicles.Count - 3));
                }

                var warningCard = new GameObject("BrokenWarning", typeof(RectTransform));
                warningCard.transform.SetParent(root.transform, false);
                UITheme.ApplySurface(warningCard.AddComponent<Image>(), UITheme.WithAlpha(UITheme.Danger, 0.16f), UIShapePreset.Inset);
                warningCard.AddComponent<LayoutElement>().preferredHeight = 30;

                var brokenLbl = MakeText(warningCard.transform, sb.ToString(), 10, new Color(1f, 0.55f, 0.55f));
                var brokenRt = brokenLbl.GetComponent<RectTransform>();
                brokenRt.anchorMin = Vector2.zero;
                brokenRt.anchorMax = Vector2.one;
                brokenRt.offsetMin = new Vector2(8f, 4f);
                brokenRt.offsetMax = new Vector2(-8f, -4f);
            }

            if (!expanded)
            {
                AttachDropTarget(root, captId);
                return;
            }

            if (c.steps.Count == 0)
            {
                var placeholder = new GameObject("DropPlaceholder", typeof(RectTransform));
                placeholder.transform.SetParent(root.transform, false);
                placeholder.AddComponent<LayoutElement>().preferredHeight = 56;
                var phImg = placeholder.AddComponent<Image>();
                UITheme.ApplySurface(phImg, UITheme.WithAlpha(UITheme.RaisedSurface, 0.72f), UIShapePreset.Inset);
                MakeText(
                    placeholder.transform,
                    LocalizationService.Get("timetable.circulations.row.drop_first_schedule"),
                    11,
                    new Color(0.5f, 0.65f, 0.75f),
                    center: true);
            }
            else
            {
                for (int i = 0; i < c.steps.Count; i++)
                {
                    BuildStepBar(root.transform, c, i);
                    if (i < c.steps.Count - 1)
                        BuildGapLine(root.transform, c, i);
                }
            }

            var issues = CirculationValidator.ValidateSequence(c.steps);
            foreach (var iss in issues)
            {
                bool isErr = iss.severity == CirculationValidator.IssueSeverity.Error;
                Color color = isErr
                    ? new Color(1f, 0.4f, 0.4f)
                    : new Color(1f, 0.7f, 0.3f);
                string fmtKey = isErr
                    ? "timetable.circulations.warning.issue_error_format"
                    : "timetable.circulations.warning.issue_warn_format";
                var issueCard = new GameObject("IssueCard", typeof(RectTransform));
                issueCard.transform.SetParent(root.transform, false);
                UITheme.ApplySurface(
                    issueCard.AddComponent<Image>(),
                    UITheme.WithAlpha(isErr ? UITheme.Danger : UITheme.Warning, 0.14f),
                    UIShapePreset.Inset);
                issueCard.AddComponent<LayoutElement>().preferredHeight = 24;

                var warnTx = MakeText(
                    issueCard.transform,
                    string.Format(LocalizationService.Get(fmtKey), iss.stepIndex, iss.message),
                    10,
                    color);
                var warnRt = warnTx.GetComponent<RectTransform>();
                warnRt.anchorMin = Vector2.zero;
                warnRt.anchorMax = Vector2.one;
                warnRt.offsetMin = new Vector2(8f, 2f);
                warnRt.offsetMax = new Vector2(-8f, -2f);
            }

            AttachDropTarget(root, captId);
        }

        private void BuildStepBar(Transform parent, Circulation c, int stepIdx)
        {
            var step = c.steps[stepIdx];
            var tt = TimetableService.GetTimetable(step.timetableId);

            var bar = new GameObject("Bar_" + stepIdx, typeof(RectTransform));
            bar.transform.SetParent(parent, false);
            bar.AddComponent<LayoutElement>().preferredHeight = 32;
            var bg = bar.AddComponent<Image>();
            Color stepBg = step.kind == StepKind.Deadhead
                ? new Color(0.2f, 0.15f, 0.08f, 0.9f)
                : new Color(0.14f, 0.2f, 0.18f, 0.9f);
            UITheme.ApplySurface(bg, stepBg, UIShapePreset.Inset);
            var hh = bar.AddComponent<HorizontalLayoutGroup>();
            hh.spacing = UITheme.Spacing.Sm;
            hh.padding = UITheme.Padding(UITheme.Spacing.Sm, UITheme.Spacing.Sm, UITheme.Spacing.Xs, UITheme.Spacing.Xs);
            hh.childForceExpandWidth = false;
            hh.childForceExpandHeight = true;
            hh.childAlignment = TextAnchor.MiddleLeft;

            var txtObj = new GameObject("T");
            txtObj.transform.SetParent(bar.transform, false);
            var txtLe = txtObj.AddComponent<LayoutElement>();
            txtLe.flexibleWidth = 1;
            txtLe.preferredHeight = 24;
            var tx = txtObj.AddComponent<TextMeshProUGUI>();
            tx.fontSize = 11;
            tx.alignment = TextAlignmentOptions.MidlineLeft;
            tx.raycastTarget = false;
            if (tt != null)
            {
                string unknown = LocalizationService.Get("timetable.circulations.format.unknown");
                string kindTag = step.kind == StepKind.Deadhead
                    ? LocalizationService.Get("timetable.circulations.step_bar.deadhead_tag")
                    : "";
                string startTrack = FormatTrack(tt.FirstStop);
                string endTrack = FormatTrack(tt.LastStop);
                tx.text = string.Format(
                    LocalizationService.Get("timetable.circulations.step_bar.format"),
                    FmtHHMM(tt.StartMinutes),
                    TrimStr(tt.FirstStop?.stationName ?? unknown, 18),
                    startTrack,
                    FmtHHMM(tt.EndMinutes),
                    TrimStr(tt.LastStop?.stationName ?? unknown, 18),
                    endTrack,
                    tt.id,
                    kindTag);
            }
            else
            {
                tx.text = string.Format(
                    LocalizationService.Get("timetable.circulations.step_bar.missing_format"),
                    step.timetableId);
            }
            UITheme.ApplyTmpText(tx, tt == null ? UIThemeTextRole.Danger : UIThemeTextRole.Inverse);

            int captIdx = stepIdx;
            int captCircId = c.id;
            MakeSmallBtn(
                bar.transform,
                LocalizationService.Get("timetable.circulations.step_bar.btn_up"),
                () => OnMoveStepUp(captCircId, captIdx),
                new Color(0.3f, 0.4f, 0.5f),
                22);
            MakeSmallBtn(
                bar.transform,
                LocalizationService.Get("timetable.circulations.step_bar.btn_down"),
                () => OnMoveStepDown(captCircId, captIdx),
                new Color(0.3f, 0.4f, 0.5f),
                22);
            MakeSmallBtn(
                bar.transform,
                LocalizationService.Get("timetable.circulations.step_bar.btn_remove"),
                () => OnRemoveStepClicked(captCircId, captIdx),
                new Color(0.55f, 0.22f, 0.22f),
                22);
        }

        private void BuildGapLine(Transform parent, Circulation c, int stepIdx)
        {
            var current = TimetableService.GetTimetable(c.steps[stepIdx].timetableId);
            var next = TimetableService.GetTimetable(c.steps[stepIdx + 1].timetableId);
            if (current == null || next == null) return;

            int gap = next.StartMinutes - current.EndMinutes;
            if (gap < 0) gap += 24 * 60;

            Color col = gap < CirculationValidator.ReverseMarginEmuMinutes
                ? new Color(1f, 0.5f, 0.3f)
                : new Color(0.5f, 0.55f, 0.6f);
            var gapRow = new GameObject("GapRow", typeof(RectTransform));
            gapRow.transform.SetParent(parent, false);
            gapRow.AddComponent<LayoutElement>().preferredHeight = 18;
            UITheme.ApplySurface(gapRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.30f), UIShapePreset.Inset);

            var tx = MakeText(
                gapRow.transform,
                string.Format(LocalizationService.Get("timetable.circulations.gap.format"), gap),
                9,
                col,
                center: true);
            var txRt = tx.GetComponent<RectTransform>();
            txRt.anchorMin = Vector2.zero;
            txRt.anchorMax = Vector2.one;
            txRt.offsetMin = Vector2.zero;
            txRt.offsetMax = Vector2.zero;
        }

        private void BuildScheduleTile(Transform parent, Timetable tt)
        {
            var tile = new GameObject("Tile_" + tt.id, typeof(RectTransform));
            tile.transform.SetParent(parent, false);
            tile.AddComponent<LayoutElement>().preferredHeight = 52;
            var bg = tile.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Inset);
            bg.raycastTarget = true;

            var drag = tile.AddComponent<CirculationDraggableTile>();
            drag.timetableId = tt.id;
            drag.canvas = _rootCanvas;

            var txtObj = new GameObject("T");
            txtObj.transform.SetParent(tile.transform, false);
            var rt = txtObj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8, 5);
            rt.offsetMax = new Vector2(-8, -5);
            var tx = txtObj.AddComponent<TextMeshProUGUI>();
            tx.fontSize = 10;
            tx.alignment = TextAlignmentOptions.TopLeft;
            tx.raycastTarget = false;

            string nr = string.IsNullOrEmpty(tt.trainNumber)
                ? LocalizationService.Get("timetable.circulations.tile.no_train_number")
                : tt.trainNumber;
            string unknownTile = LocalizationService.Get("timetable.circulations.format.unknown");
            string startStation = tt.FirstStop?.stationName ?? unknownTile;
            string endStation = tt.LastStop?.stationName ?? unknownTile;
            string cal = FormatDayMask(tt.calendar);
            tx.text = string.Format(
                LocalizationService.Get("timetable.circulations.tile.format"),
                tt.id, nr,
                TrimStr(startStation, 16), TrimStr(endStation, 16),
                FmtHHMM(tt.StartMinutes), FmtHHMM(tt.EndMinutes), cal);
            UITheme.ApplyTmpText(tx, UIThemeTextRole.Primary);
        }

        private void AttachDropTarget(GameObject row, int circId)
        {
            var dt = row.GetComponent<CirculationDropTarget>();
            if (dt == null) dt = row.AddComponent<CirculationDropTarget>();
            dt.circulationId = circId;
            dt.rect = row.GetComponent<RectTransform>();
            dt.onDropReceived = OnScheduleDroppedOnCirculation;
        }
    }
}
