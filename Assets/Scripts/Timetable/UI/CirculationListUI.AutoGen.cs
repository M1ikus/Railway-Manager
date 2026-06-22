using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class CirculationListUI
    {
        private void RunAutoGenWithSettings(CirculationAutoGenerator.GeneratorSettings settings)
        {
            var result = CirculationAutoGenerator.Generate(settings);
            if (result == null || result.proposedCirculations.Count == 0)
            {
                FlashError("Auto-gen: brak propozycji dla wybranych rozkladow i ustawien");
                RefreshWarnings();
                return;
            }

            _pendingAutoGen = result;
            OpenAutoGenModal();
        }

        private void OpenAutoGenModal()
        {
            if (_autoGenModal == null) BuildAutoGenModal();
            _autoGenModal.SetActive(true);
            foreach (Transform ch in _autoGenListContent) Destroy(ch.gameObject);

            int assignedCount = 0;
            foreach (var p in _pendingAutoGen.proposedCirculations)
            {
                var row = new GameObject("Pr", typeof(RectTransform));
                row.transform.SetParent(_autoGenListContent, false);
                row.AddComponent<LayoutElement>().preferredHeight = 36;
                UITheme.ApplySurface(row.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);

                var txtObj = new GameObject("T", typeof(RectTransform));
                txtObj.transform.SetParent(row.transform, false);
                var txtRt = (RectTransform)txtObj.transform;
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.offsetMin = new Vector2(6, 2);
                txtRt.offsetMax = new Vector2(-6, -2);

                var tx = txtObj.AddComponent<TextMeshProUGUI>();
                tx.fontSize = 11;
                tx.alignment = TextAlignmentOptions.TopLeft;
                tx.raycastTarget = false;

                string cal = FormatDayMask(new DayMask { bits = p.dayMaskBits });
                string vehicleLine = string.IsNullOrEmpty(p.vehicleAssignmentInfo)
                    ? LocalizationService.Get("timetable.circulations.modal.auto_gen_preview.vehicle_undef")
                    : (p.suggestedVehicleId >= 0
                        ? string.Format(LocalizationService.Get("timetable.circulations.modal.auto_gen_preview.vehicle_ok_format"), p.vehicleAssignmentInfo)
                        : string.Format(LocalizationService.Get("timetable.circulations.modal.auto_gen_preview.vehicle_warn_format"), p.vehicleAssignmentInfo));
                tx.text = string.Format(LocalizationService.Get("timetable.circulations.modal.auto_gen_preview.row_main_format"),
                    p.suggestedName, p.timetableIds.Count, p.totalDurationMinutes, cal, vehicleLine);
                UITheme.ApplyTmpText(tx, UIThemeTextRole.Primary);

                if (p.suggestedVehicleId >= 0) assignedCount++;
            }

            int total = _pendingAutoGen.proposedCirculations.Count;
            _autoGenSummary.text = string.Format(LocalizationService.Get("timetable.circulations.modal.auto_gen_preview.summary_format"), total, assignedCount);
        }

        private void CloseAutoGenModal()
        {
            if (_autoGenModal != null) _autoGenModal.SetActive(false);
            _pendingAutoGen = null;
        }

        private void OnAutoGenAcceptClicked()
        {
            if (_pendingAutoGen == null) return;
            CirculationAutoGenerator.ApplyAll(_pendingAutoGen);
            CloseAutoGenModal();
            Refresh();
        }

        private void OpenAutoGenSettingsModal()
        {
            if (_autoGenSettingsModal == null) BuildAutoGenSettingsModal();
            _autoGenSettingsModal.SetActive(true);
            RefreshAutoGenTimetableCheckboxes();

            if (_autoGenMinGapInput != null) _autoGenMinGapInput.text = "0";
            if (_autoGenRespectCompToggle != null) _autoGenRespectCompToggle.isOn = true;
            if (_autoGenRespectClassToggle != null) _autoGenRespectClassToggle.isOn = true;
            if (_autoGenAutoAssignToggle != null) _autoGenAutoAssignToggle.isOn = true;
        }

        private void CloseAutoGenSettingsModal()
        {
            if (_autoGenSettingsModal != null) _autoGenSettingsModal.SetActive(false);
        }

        private void OnAutoGenSettingsGenerateClicked()
        {
            var settings = new CirculationAutoGenerator.GeneratorSettings();

            int minGap = 0;
            if (_autoGenMinGapInput != null) int.TryParse(_autoGenMinGapInput.text, out minGap);
            settings.minGapMinutes = Mathf.Max(0, minGap);

            settings.respectCompositionMode = _autoGenRespectCompToggle?.isOn ?? true;
            settings.respectServiceClass = _autoGenRespectClassToggle?.isOn ?? true;
            settings.autoAssignVehicles = _autoGenAutoAssignToggle?.isOn ?? true;

            var selected = new HashSet<int>();
            foreach (var kvp in _autoGenTimetableToggles)
            {
                if (kvp.Value != null && kvp.Value.isOn)
                    selected.Add(kvp.Key);
            }
            if (selected.Count == 0)
            {
                FlashError(LocalizationService.Get("timetable.circulations.modal.auto_gen_settings.no_selection_flash"));
                RefreshWarnings();
                return;
            }
            settings.allowedTimetableIds = selected;

            CloseAutoGenSettingsModal();
            RunAutoGenWithSettings(settings);
        }

        private void RefreshAutoGenTimetableCheckboxes()
        {
            if (_autoGenTimetablesContent == null) return;
            foreach (Transform ch in _autoGenTimetablesContent) Destroy(ch.gameObject);
            _autoGenTimetableToggles.Clear();

            var inAnyCirculation = new HashSet<int>();
            foreach (var c in CirculationService.Circulations)
            {
                if (c?.steps == null) continue;
                foreach (var s in c.steps) inAnyCirculation.Add(s.timetableId);
            }

            int count = 0;
            foreach (var tt in TimetableService.Timetables)
            {
                if (tt == null) continue;
                if (tt.status != TimetableStatus.Active) continue;
                if (inAnyCirculation.Contains(tt.id)) continue;

                var row = new GameObject("T_" + tt.id, typeof(RectTransform));
                row.transform.SetParent(_autoGenTimetablesContent, false);
                row.AddComponent<LayoutElement>().preferredHeight = 22;
                var rowHh = row.AddComponent<HorizontalLayoutGroup>();
                rowHh.spacing = UITheme.Spacing.Sm;
                rowHh.padding = UITheme.Padding(UITheme.Spacing.Xs, 0f);
                rowHh.childForceExpandWidth = false;
                rowHh.childForceExpandHeight = true;
                rowHh.childAlignment = TextAnchor.MiddleLeft;

                var cb = new GameObject("Cb", typeof(RectTransform));
                cb.transform.SetParent(row.transform, false);
                var cbLe = cb.AddComponent<LayoutElement>();
                cbLe.preferredWidth = 14;
                cbLe.preferredHeight = 14;
                cbLe.flexibleWidth = 0;
                var cbBg = cb.AddComponent<Image>();
                UITheme.ApplySurface(cbBg, UITheme.TopBarInset, UIShapePreset.Inset);

                var check = new GameObject("Ch", typeof(RectTransform));
                check.transform.SetParent(cb.transform, false);
                var chrt = (RectTransform)check.transform;
                chrt.anchorMin = new Vector2(0.15f, 0.15f);
                chrt.anchorMax = new Vector2(0.85f, 0.85f);
                chrt.offsetMin = Vector2.zero;
                chrt.offsetMax = Vector2.zero;
                var chImg = check.AddComponent<Image>();
                UITheme.ApplySurface(chImg, UITheme.PrimaryAccent, UIShapePreset.Inset);

                var tgl = cb.AddComponent<Toggle>();
                tgl.isOn = true;
                tgl.targetGraphic = cbBg;
                tgl.graphic = chImg;
                tgl.colors = UITheme.CreateColorBlock(
                    UITheme.TopBarInset,
                    UITheme.SecondarySurface,
                    UITheme.RaisedSurface,
                    UITheme.SecondarySurface,
                    UITheme.WithAlpha(UITheme.Border, 0.45f));
                _autoGenTimetableToggles[tt.id] = tgl;

                var lbl = new GameObject("L", typeof(RectTransform));
                lbl.transform.SetParent(row.transform, false);
                var lblLe = lbl.AddComponent<LayoutElement>();
                lblLe.flexibleWidth = 1;
                lblLe.preferredHeight = 18;
                var lblTx = lbl.AddComponent<TextMeshProUGUI>();
                lblTx.fontSize = 10;
                lblTx.alignment = TextAlignmentOptions.MidlineLeft;
                lblTx.raycastTarget = false;

                string unknown = LocalizationService.Get("timetable.circulations.format.unknown");
                string start = tt.FirstStop?.stationName ?? unknown;
                string end = tt.LastStop?.stationName ?? unknown;
                string nr = string.IsNullOrEmpty(tt.trainNumber) ? LocalizationService.Get("timetable.circulations.tile.no_train_number") : tt.trainNumber;
                lblTx.text = string.Format(LocalizationService.Get("timetable.circulations.modal.auto_gen_settings.tt_row_format"),
                    tt.id, nr, start, end, FmtHHMM(tt.StartMinutes), FmtHHMM(tt.EndMinutes), FormatDayMask(tt.calendar));
                UITheme.ApplyTmpText(lblTx, UIThemeTextRole.Primary);
                count++;
            }

            if (count == 0)
            {
                var empty = new GameObject("Empty", typeof(RectTransform));
                empty.transform.SetParent(_autoGenTimetablesContent, false);
                empty.AddComponent<LayoutElement>().preferredHeight = 30;
                UITheme.ApplySurface(empty.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.72f), UIShapePreset.Inset);

                // Text na osobnym GO (Image + Text na jednym GO → NRE, patrz commit 2a3907e)
                var emptyTextObj = new GameObject("Text", typeof(RectTransform));
                emptyTextObj.transform.SetParent(empty.transform, false);
                var emptyRt = (RectTransform)emptyTextObj.transform;
                emptyRt.anchorMin = Vector2.zero;
                emptyRt.anchorMax = Vector2.one;
                emptyRt.offsetMin = Vector2.zero;
                emptyRt.offsetMax = Vector2.zero;
                var tx = emptyTextObj.AddComponent<TextMeshProUGUI>();
                tx.fontSize = 11;
                tx.alignment = TextAlignmentOptions.Center;
                tx.raycastTarget = false;
                tx.text = LocalizationService.Get("timetable.circulations.modal.auto_gen_settings.no_unassigned");
                UITheme.ApplyTmpText(tx, UIThemeTextRole.Secondary);
            }
        }

        private void BuildAutoGenSettingsModal()
        {
            var canvas = _panel.transform.parent;
            _autoGenSettingsModal = new GameObject("AutoGenSettingsModal", typeof(RectTransform));
            _autoGenSettingsModal.transform.SetParent(canvas, false);
            var mrt = (RectTransform)_autoGenSettingsModal.transform;
            mrt.anchorMin = new Vector2(0.2f, 0.1f);
            mrt.anchorMax = new Vector2(0.8f, 0.9f);
            mrt.offsetMin = Vector2.zero;
            mrt.offsetMax = Vector2.zero;
            UITheme.ApplySurface(
                _autoGenSettingsModal.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f),
                UIShapePreset.PanelLarge);
            var vlg = _autoGenSettingsModal.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Lg);
            vlg.spacing = UITheme.Spacing.Sm;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var titleRow = MakeHRow(_autoGenSettingsModal.transform, 30);
            UITheme.ApplySurface(titleRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            MakeText(titleRow.transform, LocalizationService.Get("timetable.circulations.modal.auto_gen_settings.title"), 14, Color.white, 0);

            var gapRow = MakeHRow(_autoGenSettingsModal.transform, 26);
            UITheme.ApplySurface(gapRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            MakeText(gapRow.transform, LocalizationService.Get("timetable.circulations.modal.auto_gen_settings.min_gap_label"), 11, new Color(0.7f, 0.7f, 0.7f), 240);
            _autoGenMinGapInput = MakeInputField(gapRow.transform, "0", 60, "0");
            MakeText(gapRow.transform, LocalizationService.Get("timetable.circulations.modal.auto_gen_settings.min_gap_hint"), 10, new Color(0.5f, 0.5f, 0.5f), 200);

            _autoGenRespectCompToggle = MakeSimpleToggle(_autoGenSettingsModal.transform,
                LocalizationService.Get("timetable.circulations.modal.auto_gen_settings.respect_composition"), true);
            _autoGenRespectClassToggle = MakeSimpleToggle(_autoGenSettingsModal.transform,
                LocalizationService.Get("timetable.circulations.modal.auto_gen_settings.respect_class"), true);
            _autoGenAutoAssignToggle = MakeSimpleToggle(_autoGenSettingsModal.transform,
                LocalizationService.Get("timetable.circulations.modal.auto_gen_settings.auto_assign"), true);

            MakeText(_autoGenSettingsModal.transform, LocalizationService.Get("timetable.circulations.modal.auto_gen_settings.select_label"), 11, new Color(0.55f, 0.75f, 1f), 0);

            var scroll = new GameObject("TtScroll", typeof(RectTransform));
            scroll.transform.SetParent(_autoGenSettingsModal.transform, false);
            var sle = scroll.AddComponent<LayoutElement>();
            sle.flexibleHeight = 1;
            sle.minHeight = 150;
            var sImg = scroll.AddComponent<Image>();
            UITheme.ApplySurface(sImg, UITheme.WithAlpha(UITheme.PrimarySurface, 0.38f), UIShapePreset.Panel);
            var sr = scroll.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Elastic;

            var vp = new GameObject("Viewport", typeof(RectTransform));
            vp.transform.SetParent(scroll.transform, false);
            var vpRt = (RectTransform)vp.transform;
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = Vector2.zero;
            vpRt.offsetMax = Vector2.zero;
            UITheme.ApplySurface(vp.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.14f), UIShapePreset.Inset);
            vp.AddComponent<Mask>().showMaskGraphic = false;

            var cnt = new GameObject("Content", typeof(RectTransform));
            cnt.transform.SetParent(vp.transform, false);
            var cRt = (RectTransform)cnt.transform;
            cRt.anchorMin = new Vector2(0, 1);
            cRt.anchorMax = new Vector2(1, 1);
            cRt.pivot = new Vector2(0.5f, 1f);
            var cVlg = cnt.AddComponent<VerticalLayoutGroup>();
            cVlg.padding = UITheme.Padding(UITheme.Spacing.Xs);
            cVlg.spacing = UITheme.Spacing.Xxs;
            cVlg.childForceExpandWidth = true;
            cVlg.childForceExpandHeight = false;
            var csf = cnt.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vpRt;
            sr.content = cRt;
            _autoGenTimetablesContent = cnt.transform;

            var quickRow = MakeHRow(_autoGenSettingsModal.transform, 26);
            quickRow.GetComponent<LayoutElement>().flexibleHeight = 0;
            UITheme.ApplySurface(quickRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            MakeBtn(quickRow.transform, LocalizationService.Get("timetable.circulations.modal.auto_gen_settings.select_all"), () => SetAllAutoGenCheckboxes(true), new Color(0.3f, 0.4f, 0.5f), 150);
            MakeBtn(quickRow.transform, LocalizationService.Get("timetable.circulations.modal.auto_gen_settings.deselect_all"), () => SetAllAutoGenCheckboxes(false), new Color(0.3f, 0.4f, 0.5f), 150);
            var quickSpacer = new GameObject("Sp", typeof(RectTransform));
            quickSpacer.transform.SetParent(quickRow.transform, false);
            quickSpacer.AddComponent<LayoutElement>().flexibleWidth = 1;

            var btnRow = MakeHRow(_autoGenSettingsModal.transform, 32);
            btnRow.GetComponent<LayoutElement>().flexibleHeight = 0;
            UITheme.ApplySurface(btnRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            var btnSpacer = new GameObject("Sp", typeof(RectTransform));
            btnSpacer.transform.SetParent(btnRow.transform, false);
            btnSpacer.AddComponent<LayoutElement>().flexibleWidth = 1;
            MakeBtn(btnRow.transform, LocalizationService.Get("timetable.circulations.modal.common.cancel"), CloseAutoGenSettingsModal, new Color(0.3f, 0.3f, 0.4f), 120);
            MakeBtn(btnRow.transform, LocalizationService.Get("timetable.circulations.modal.auto_gen_settings.generate_btn"), OnAutoGenSettingsGenerateClicked, new Color(0.2f, 0.7f, 0.3f), 120);

            _autoGenSettingsModal.SetActive(false);
        }

        private void SetAllAutoGenCheckboxes(bool state)
        {
            foreach (var kvp in _autoGenTimetableToggles)
                if (kvp.Value != null) kvp.Value.isOn = state;
        }

        private void BuildAutoGenModal()
        {
            var canvas = _panel.transform.parent;
            _autoGenModal = new GameObject("AutoGenModal", typeof(RectTransform));
            _autoGenModal.transform.SetParent(canvas, false);
            var mrt = (RectTransform)_autoGenModal.transform;
            mrt.anchorMin = new Vector2(0.2f, 0.2f);
            mrt.anchorMax = new Vector2(0.8f, 0.8f);
            mrt.offsetMin = Vector2.zero;
            mrt.offsetMax = Vector2.zero;
            UITheme.ApplySurface(
                _autoGenModal.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f),
                UIShapePreset.PanelLarge);
            var vlg = _autoGenModal.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Lg);
            vlg.spacing = UITheme.Spacing.Sm;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            var titleRow = MakeHRow(_autoGenModal.transform, 30);
            UITheme.ApplySurface(titleRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            MakeText(titleRow.transform, LocalizationService.Get("timetable.circulations.modal.auto_gen_preview.title"), 14, Color.white);

            var scroll = new GameObject("Scroll", typeof(RectTransform));
            scroll.transform.SetParent(_autoGenModal.transform, false);
            var sle = scroll.AddComponent<LayoutElement>();
            sle.flexibleHeight = 1;
            sle.minHeight = 120;
            UITheme.ApplySurface(scroll.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Panel);
            var svlg = scroll.AddComponent<VerticalLayoutGroup>();
            svlg.spacing = UITheme.Spacing.Xxs;
            svlg.childForceExpandWidth = true;
            svlg.childForceExpandHeight = false;
            _autoGenListContent = scroll.transform;

            _autoGenSummary = MakeText(_autoGenModal.transform, "", 11, new Color(0.6f, 0.85f, 0.6f));
            UITheme.ApplySurface(_autoGenSummary.gameObject.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);

            var btnRow = MakeHRow(_autoGenModal.transform, 32);
            UITheme.ApplySurface(btnRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            MakeBtn(btnRow.transform, LocalizationService.Get("timetable.circulations.modal.common.cancel"), CloseAutoGenModal, new Color(0.3f, 0.3f, 0.4f));
            MakeBtn(btnRow.transform, LocalizationService.Get("timetable.circulations.modal.auto_gen_preview.accept_all_btn"), OnAutoGenAcceptClicked, new Color(0.2f, 0.7f, 0.3f));

            _autoGenModal.SetActive(false);
        }
    }
}
