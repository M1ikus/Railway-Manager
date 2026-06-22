using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class VehicleAssignmentModal
    {
        // ─────────────────────────────────────────────
        //  Refresh columns — days (left) + pool (right) + tile builders
        // ─────────────────────────────────────────────

        private void RefreshDays()
        {
            if (_daysContent == null || _target == null) return;
            foreach (Transform ch in _daysContent) Destroy(ch.gameObject);

            if (_activeDates.Count == 0)
            {
                var empty = new GameObject("Empty", typeof(RectTransform));
                empty.transform.SetParent(_daysContent, false);
                empty.AddComponent<LayoutElement>().preferredHeight = 40;
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
                tx.fontSize = 12;
                tx.alignment = TextAlignmentOptions.Center;
                tx.raycastTarget = false;
                tx.text = LocalizationService.Get("timetable.vehicle_assign.empty.no_active_dates");
                UITheme.ApplyTmpText(tx, UIThemeTextRole.Secondary);
                return;
            }

            foreach (var date in _activeDates)
                BuildDayRow(_daysContent, date);
        }

        private void BuildDayRow(Transform parent, System.DateTime date)
        {
            string dateIso = date.ToString("yyyy-MM-dd");
            var vehicles = _target.GetVehiclesForDate(dateIso);

            // Wrap row w VerticalLayoutGroup żeby można dodać consist-status line pod barem
            var wrapper = new GameObject("DayWrap_" + dateIso, typeof(RectTransform));
            wrapper.transform.SetParent(parent, false);
            UITheme.ApplySurface(wrapper.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Panel);
            var wrapLe = wrapper.AddComponent<LayoutElement>();
            wrapLe.preferredHeight = vehicles.Count > 0 ? 88 : 68;
            wrapLe.flexibleHeight = 0;
            var wrapVlg = wrapper.AddComponent<VerticalLayoutGroup>();
            wrapVlg.spacing = UITheme.Spacing.Xs;
            wrapVlg.padding = UITheme.Padding(UITheme.Spacing.Xs);
            wrapVlg.childForceExpandWidth = true;
            wrapVlg.childForceExpandHeight = false;

            var row = new GameObject("Day_" + dateIso, typeof(RectTransform));
            row.transform.SetParent(wrapper.transform, false);
            var rowLe = row.AddComponent<LayoutElement>();
            rowLe.preferredHeight = 60;
            rowLe.flexibleHeight = 0;
            var rowHlg = row.AddComponent<HorizontalLayoutGroup>();
            rowHlg.padding = UITheme.Padding(UITheme.Spacing.Sm);
            rowHlg.spacing = UITheme.Spacing.Sm;
            rowHlg.childForceExpandWidth = false;
            rowHlg.childForceExpandHeight = true;
            rowHlg.childAlignment = TextAnchor.MiddleLeft;

            // Data label
            var dateLbl = new GameObject("Date", typeof(RectTransform));
            dateLbl.transform.SetParent(row.transform, false);
            var dateLe = dateLbl.AddComponent<LayoutElement>();
            dateLe.preferredWidth = 108;
            var dateTx = dateLbl.AddComponent<TextMeshProUGUI>();
            dateTx.fontSize = 11;
            dateTx.alignment = TextAlignmentOptions.MidlineLeft;
            dateTx.raycastTarget = false;
            string[] dayKeys = {
                "timetable.vehicle_assign.day.names.mon",
                "timetable.vehicle_assign.day.names.tue",
                "timetable.vehicle_assign.day.names.wed",
                "timetable.vehicle_assign.day.names.thu",
                "timetable.vehicle_assign.day.names.fri",
                "timetable.vehicle_assign.day.names.sat",
                "timetable.vehicle_assign.day.names.sun"
            };
            int dayIdx = date.DayOfWeek == System.DayOfWeek.Sunday ? 6 : (int)date.DayOfWeek - 1;
            dateTx.text = string.Format(LocalizationService.Get("timetable.vehicle_assign.day.label_format"),
                LocalizationService.Get(dayKeys[dayIdx]), date.ToString("yyyy-MM-dd"));
            UITheme.ApplyTmpText(dateTx, UIThemeTextRole.Primary);

            // Bar przypisanych pojazdów (flex)
            var barObj = new GameObject("Bar", typeof(RectTransform));
            barObj.transform.SetParent(row.transform, false);
            var barLe = barObj.AddComponent<LayoutElement>();
            barLe.flexibleWidth = 1;
            barLe.minWidth = 200;
            var barHlg = barObj.AddComponent<HorizontalLayoutGroup>();
            barHlg.spacing = UITheme.Spacing.Sm;
            barHlg.childForceExpandWidth = false;
            barHlg.childForceExpandHeight = true;
            barHlg.childAlignment = TextAnchor.MiddleLeft;
            UITheme.ApplySurface(barObj.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);

            // Drop target na cały row
            var dropTarget = row.AddComponent<CirculationDayDropTarget>();
            dropTarget.dateIso = dateIso;
            dropTarget.rect = (RectTransform)row.transform;
            dropTarget.onDropReceived = OnVehicleDroppedOnDay;

            if (vehicles.Count == 0)
            {
                var phTxt = new GameObject("Ph", typeof(RectTransform));
                phTxt.transform.SetParent(barObj.transform, false);
                phTxt.AddComponent<LayoutElement>().preferredWidth = 200;
                var phTx = phTxt.AddComponent<TextMeshProUGUI>();
                phTx.fontSize = 10;
                phTx.alignment = TextAlignmentOptions.Center;
                phTx.raycastTarget = false;
                phTx.text = LocalizationService.Get("timetable.vehicle_assign.day.drop_hint");
                UITheme.ApplyTmpText(phTx, UIThemeTextRole.Secondary);
            }
            else
            {
                foreach (var vid in vehicles)
                    BuildVehicleBarTile(barObj.transform, dateIso, vid);
            }

            // [Kopiuj] button
            string captDate = dateIso;
            var copyBtn = new GameObject("Copy", typeof(RectTransform));
            copyBtn.transform.SetParent(row.transform, false);
            var copyLe = copyBtn.AddComponent<LayoutElement>();
            copyLe.preferredWidth = 90;
            var copyImg = copyBtn.AddComponent<Image>();
            Color copyBg = vehicles.Count > 0
                ? UITheme.PrimaryAccent
                : UITheme.WithAlpha(UITheme.Border, 0.65f);
            UITheme.ApplySurface(copyImg, copyBg, UIShapePreset.Pill);
            var copyBtnComp = copyBtn.AddComponent<Button>();
            copyBtnComp.targetGraphic = copyImg;
            copyBtnComp.interactable = vehicles.Count > 0;
            copyBtnComp.colors = UITheme.CreateColorBlock(
                copyBg,
                UITheme.Darken(copyBg, 0.05f),
                UITheme.Darken(copyBg, 0.12f),
                copyBg,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            copyBtnComp.onClick.AddListener(() => OnCopyToAllDays(captDate));
            var copyLbl = new GameObject("Lbl", typeof(RectTransform));
            copyLbl.transform.SetParent(copyBtn.transform, false);
            var clRt = (RectTransform)copyLbl.transform;
            clRt.anchorMin = Vector2.zero; clRt.anchorMax = Vector2.one;
            clRt.offsetMin = Vector2.zero; clRt.offsetMax = Vector2.zero;
            var clTx = copyLbl.AddComponent<TextMeshProUGUI>();
            clTx.fontSize = 10;
            clTx.alignment = TextAlignmentOptions.Center;
            clTx.raycastTarget = false;
            clTx.text = LocalizationService.Get("timetable.vehicle_assign.day.copy_btn");
            UITheme.ApplyTmpText(clTx, UIThemeTextRole.Inverse);

            // ── Consist status line (pod barem, tylko jeśli są pojazdy) ──
            if (vehicles.Count > 0)
            {
                var result = ConsistValidator.Validate(vehicles);
                var statusObj = new GameObject("ConsistStatus", typeof(RectTransform));
                statusObj.transform.SetParent(wrapper.transform, false);
                statusObj.AddComponent<LayoutElement>().preferredHeight = 20;
                UITheme.ApplySurface(statusObj.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.88f), UIShapePreset.Inset);

                // Text na osobnym GO (Image + Text na jednym GO → NRE, patrz commit 2a3907e)
                var statusTextObj = new GameObject("Text", typeof(RectTransform));
                statusTextObj.transform.SetParent(statusObj.transform, false);
                var stRt = (RectTransform)statusTextObj.transform;
                stRt.anchorMin = Vector2.zero;
                stRt.anchorMax = Vector2.one;
                stRt.offsetMin = new Vector2(6, 0);
                stRt.offsetMax = new Vector2(-6, 0);
                var stTx = statusTextObj.AddComponent<TextMeshProUGUI>();
                stTx.fontSize = 10;
                stTx.alignment = TextAlignmentOptions.MidlineLeft;
                stTx.raycastTarget = false;
                string prefix = result.severity == ConsistValidator.Severity.Error
                    ? LocalizationService.Get("timetable.vehicle_assign.consist_status.error_prefix")
                    : result.severity == ConsistValidator.Severity.Warning
                        ? LocalizationService.Get("timetable.vehicle_assign.consist_status.warn_prefix")
                        : LocalizationService.Get("timetable.vehicle_assign.consist_status.ok_prefix");
                stTx.text = prefix + result.message;
                UITheme.ApplyTmpText(
                    stTx,
                    result.severity == ConsistValidator.Severity.Error
                        ? UIThemeTextRole.Danger
                        : result.severity == ConsistValidator.Severity.Warning
                            ? UIThemeTextRole.Warning
                            : UIThemeTextRole.Success);
            }
        }

        private void BuildVehicleBarTile(Transform parent, string dateIso, int vehicleId)
        {
            FleetVehicleData vehicle = null;
            foreach (var v in FleetService.OwnedVehicles)
                if (v != null && v.id == vehicleId) { vehicle = v; break; }

            var tile = new GameObject("VBar_" + vehicleId, typeof(RectTransform));
            tile.transform.SetParent(parent, false);
            var tle = tile.AddComponent<LayoutElement>();
            tle.preferredWidth = 120;
            tle.preferredHeight = 46;

            // Sprawdź konflikt w tej dacie
            var conflict = CirculationService.GetVehicleConflictForDate(vehicleId, dateIso, _target.id);
            Color bgCol = conflict != null
                ? UITheme.WithAlpha(UITheme.Danger, 0.9f)
                : UITheme.WithAlpha(UITheme.Success, 0.42f);
            UITheme.ApplySurface(tile.AddComponent<Image>(), bgCol, UIShapePreset.Inset);

            // Text
            var txtObj = new GameObject("T", typeof(RectTransform));
            txtObj.transform.SetParent(tile.transform, false);
            var rt = (RectTransform)txtObj.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(6, 3);
            rt.offsetMax = new Vector2(-24, -3);
            var tx = txtObj.AddComponent<TextMeshProUGUI>();
            tx.fontSize = 10;
            tx.alignment = TextAlignmentOptions.MidlineLeft;
            tx.raycastTarget = false;
            if (vehicle != null)
                tx.text = string.Format(LocalizationService.Get("timetable.vehicle_assign.vehicle.tile_format"), vehicle.series, vehicle.number)
                          + (conflict != null ? string.Format(LocalizationService.Get("timetable.vehicle_assign.vehicle.tile_conflict_format"), conflict.id) : "");
            else
                tx.text = string.Format(LocalizationService.Get("timetable.vehicle_assign.vehicle.tile_unknown_format"), vehicleId);
            UITheme.ApplyTmpText(tx, UIThemeTextRole.Inverse);

            // Remove button ✕ w rogu
            int captId = vehicleId;
            string captDate = dateIso;
            var rmBtn = new GameObject("Rm", typeof(RectTransform));
            rmBtn.transform.SetParent(tile.transform, false);
            var rmRt = (RectTransform)rmBtn.transform;
            rmRt.anchorMin = new Vector2(1f, 0.5f);
            rmRt.anchorMax = new Vector2(1f, 0.5f);
            rmRt.pivot = new Vector2(1f, 0.5f);
            rmRt.sizeDelta = new Vector2(18, 18);
            rmRt.anchoredPosition = new Vector2(-2, 0);
            var rmImg = rmBtn.AddComponent<Image>();
            UITheme.ApplySurface(rmImg, UITheme.Danger, UIShapePreset.Pill);
            var rmBtnComp = rmBtn.AddComponent<Button>();
            rmBtnComp.targetGraphic = rmImg;
            rmBtnComp.colors = UITheme.CreateColorBlock(
                UITheme.Danger,
                UITheme.Darken(UITheme.Danger, 0.05f),
                UITheme.Darken(UITheme.Danger, 0.12f),
                UITheme.Danger,
                UITheme.WithAlpha(UITheme.Border, 0.55f));
            rmBtnComp.onClick.AddListener(() => OnRemoveVehicleFromDay(captDate, captId));
            var rmLbl = new GameObject("L", typeof(RectTransform));
            rmLbl.transform.SetParent(rmBtn.transform, false);
            var rmLRt = (RectTransform)rmLbl.transform;
            rmLRt.anchorMin = Vector2.zero; rmLRt.anchorMax = Vector2.one;
            rmLRt.offsetMin = Vector2.zero; rmLRt.offsetMax = Vector2.zero;
            var rmTx = rmLbl.AddComponent<TextMeshProUGUI>();
            rmTx.fontSize = 11;
            rmTx.alignment = TextAlignmentOptions.Center;
            rmTx.raycastTarget = false;
            rmTx.text = LocalizationService.Get("timetable.vehicle_assign.vehicle.remove_btn");
            UITheme.ApplyTmpText(rmTx, UIThemeTextRole.Inverse);
        }

        private void RefreshPool()
        {
            if (_poolContent == null) return;
            foreach (Transform ch in _poolContent) Destroy(ch.gameObject);

            // Filtr: pokaż tylko typy pojazdów pasujące do kompozycji rozkładów w obiegu
            var allowed = CirculationService.GetAllowedVehicleTypes(_target);

            // Info label o filtrze (pierwsze w liście)
            if (allowed != null)
            {
                string allowedStr = FormatAllowedTypes(allowed);
                var info = new GameObject("FilterInfo", typeof(RectTransform));
                info.transform.SetParent(_poolContent, false);
                info.AddComponent<LayoutElement>().preferredHeight = 32;
                UITheme.ApplySurface(info.AddComponent<Image>(), UITheme.WithAlpha(UITheme.RaisedSurface, 0.82f), UIShapePreset.Inset);
                var infoTxt = new GameObject("T", typeof(RectTransform));
                infoTxt.transform.SetParent(info.transform, false);
                var rt = (RectTransform)infoTxt.transform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(6, 2); rt.offsetMax = new Vector2(-6, -2);
                var itx = infoTxt.AddComponent<TextMeshProUGUI>();
                itx.fontSize = 10;
                itx.alignment = TextAlignmentOptions.MidlineLeft;
                itx.raycastTarget = false;
                itx.text = string.Format(LocalizationService.Get("timetable.vehicle_assign.pool.filter_format"), allowedStr);
                UITheme.ApplyTmpText(itx, UIThemeTextRole.Accent);
            }

            int shown = 0;
            foreach (var vehicle in FleetService.OwnedVehicles)
            {
                if (vehicle == null) continue;
                if (allowed != null && !allowed.Contains(vehicle.type)) continue;
                BuildPoolTile(_poolContent, vehicle);
                shown++;
            }

            if (shown == 0)
            {
                var empty = new GameObject("Empty", typeof(RectTransform));
                empty.transform.SetParent(_poolContent, false);
                empty.AddComponent<LayoutElement>().preferredHeight = 40;
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
                tx.text = allowed != null
                    ? LocalizationService.Get("timetable.vehicle_assign.empty.no_matching_vehicles")
                    : LocalizationService.Get("timetable.vehicle_assign.empty.no_fleet");
                UITheme.ApplyTmpText(tx, UIThemeTextRole.Secondary);
            }
        }

        private static string FormatAllowedTypes(HashSet<FleetVehicleType> types)
        {
            var parts = new List<string>();
            if (types.Contains(FleetVehicleType.ElectricLocomotive)
             || types.Contains(FleetVehicleType.DieselLocomotive))
                parts.Add(LocalizationService.Get("timetable.vehicle_assign.type.loco"));
            if (types.Contains(FleetVehicleType.PassengerCar))
                parts.Add(LocalizationService.Get("timetable.vehicle_assign.type.car"));
            if (types.Contains(FleetVehicleType.EMU))
                parts.Add(LocalizationService.Get("timetable.vehicle_assign.type.emu"));
            if (types.Contains(FleetVehicleType.DMU))
                parts.Add(LocalizationService.Get("timetable.vehicle_assign.type.dmu"));
            return string.Join(LocalizationService.Get("timetable.vehicle_assign.type.separator"), parts);
        }

        private void BuildPoolTile(Transform parent, FleetVehicleData vehicle)
        {
            var tile = new GameObject("Pool_" + vehicle.id, typeof(RectTransform));
            tile.transform.SetParent(parent, false);
            tile.AddComponent<LayoutElement>().preferredHeight = 68;
            var bg = tile.AddComponent<Image>();
            UITheme.ApplySurface(bg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.82f), UIShapePreset.Inset);
            bg.raycastTarget = true;

            // Draggable
            var drag = tile.AddComponent<VehicleDraggableTile>();
            drag.vehicleId = vehicle.id;
            drag.canvas = _rootCanvas;

            // Text
            var txtObj = new GameObject("T", typeof(RectTransform));
            txtObj.transform.SetParent(tile.transform, false);
            var rt = (RectTransform)txtObj.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(8, 5);
            rt.offsetMax = new Vector2(-8, -5);
            var tx = txtObj.AddComponent<TextMeshProUGUI>();
            tx.fontSize = 10;
            tx.alignment = TextAlignmentOptions.TopLeft;
            tx.raycastTarget = false;

            // Availability summary (w ilu dniach obiegu jest już zajęty przez inny obieg)
            string availability = ComputeAvailabilitySummary(vehicle.id);
            tx.text = string.Format(LocalizationService.Get("timetable.vehicle_assign.pool.tile_format"),
                vehicle.id, vehicle.series, vehicle.number, vehicle.conditionPercent.ToString("F0"), availability);
            UITheme.ApplyTmpText(tx, UIThemeTextRole.Primary);
        }

        private string ComputeAvailabilitySummary(int vehicleId)
        {
            if (_activeDates.Count == 0) return "";
            int busyDays = 0;
            Circulation firstConflict = null;
            foreach (var d in _activeDates)
            {
                string iso = d.ToString("yyyy-MM-dd");
                var c = CirculationService.GetVehicleConflictForDate(vehicleId, iso, _target.id);
                if (c != null)
                {
                    busyDays++;
                    if (firstConflict == null) firstConflict = c;
                }
            }
            if (busyDays == 0) return LocalizationService.Get("timetable.vehicle_assign.pool.availability_free");
            if (busyDays == _activeDates.Count) return string.Format(LocalizationService.Get("timetable.vehicle_assign.pool.availability_busy_format"), firstConflict.id);
            return string.Format(LocalizationService.Get("timetable.vehicle_assign.pool.availability_partial_format"), busyDays, _activeDates.Count);
        }
    }
}
