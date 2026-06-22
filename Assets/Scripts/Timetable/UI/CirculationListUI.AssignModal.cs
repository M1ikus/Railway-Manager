using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class CirculationListUI
    {
        private void OpenAssignModal(Circulation target)
        {
            _assignTarget = target;
            if (_assignModal == null) BuildAssignModal();
            _assignModal.SetActive(true);

            var opts = new List<TMP_Dropdown.OptionData> { new(LocalizationService.Get("timetable.circulations.modal.assign.select_prompt")) };
            foreach (var v in FleetService.OwnedVehicles)
            {
                if (v == null) continue;
                opts.Add(new TMP_Dropdown.OptionData(string.Format(LocalizationService.Get("timetable.circulations.modal.assign.vehicle_option_format"),
                    v.series, v.number, v.conditionPercent.ToString("F0"))));
            }
            _assignVehicleDropdown.options = opts;
            _assignVehicleDropdown.value = 0;
            _assignVehicleDropdown.RefreshShownValue();
            UpdateAssignStatus();
        }

        private void CloseAssignModal()
        {
            if (_assignModal != null) _assignModal.SetActive(false);
            _assignTarget = null;
        }

        private void UpdateAssignStatus()
        {
            if (_assignStatusText == null || _assignTarget == null) return;
            int idx = _assignVehicleDropdown.value;
            if (idx <= 0 || idx - 1 >= FleetService.OwnedVehicles.Count)
            {
                _assignStatusText.text = LocalizationService.Get("timetable.circulations.modal.assign.status.select");
                _assignStatusText.color = UITheme.SecondaryText;
                return;
            }

            var vehicle = FleetService.OwnedVehicles[idx - 1];
            var conflicts = CirculationService.CheckVehicleAssignmentConflicts(vehicle.id, _assignTarget);
            if (conflicts.Count == 0)
            {
                _assignStatusText.text = string.Format(LocalizationService.Get("timetable.circulations.modal.assign.status.ok_format"), vehicle.series, vehicle.number);
                _assignStatusText.color = UITheme.Success;
            }
            else
            {
                var names = new List<string>();
                foreach (var c in conflicts) names.Add($"#{c.id}");
                _assignStatusText.text = string.Format(LocalizationService.Get("timetable.circulations.modal.assign.status.conflict_format"), string.Join(", ", names));
                _assignStatusText.color = UITheme.Warning;
            }
        }

        private void OnAssignConfirmClicked()
        {
            if (_assignTarget == null) return;
            int idx = _assignVehicleDropdown.value;
            if (idx <= 0 || idx - 1 >= FleetService.OwnedVehicles.Count) return;

            var vehicle = FleetService.OwnedVehicles[idx - 1];
            var conflicts = CirculationService.CheckVehicleAssignmentConflicts(vehicle.id, _assignTarget);
            if (conflicts.Count > 0) return;

            if (_assignTarget.assignedVehicleIds == null)
                _assignTarget.assignedVehicleIds = new List<int>();
            if (!_assignTarget.assignedVehicleIds.Contains(vehicle.id))
                _assignTarget.assignedVehicleIds.Add(vehicle.id);

            vehicle.assignedCirculationId = _assignTarget.id;
            Log.Info($"[CirculationList] Pojazd {vehicle.id} przypisany do obiegu #{_assignTarget.id}");
            CloseAssignModal();
            Refresh();
        }

        private void BuildAssignModal()
        {
            var canvas = _panel.transform.parent;
            _assignModal = new GameObject("AssignModal", typeof(RectTransform));
            _assignModal.transform.SetParent(canvas, false);

            var mrt = (RectTransform)_assignModal.transform;
            mrt.anchorMin = new Vector2(0.3f, 0.35f);
            mrt.anchorMax = new Vector2(0.7f, 0.65f);
            mrt.offsetMin = Vector2.zero;
            mrt.offsetMax = Vector2.zero;

            UITheme.ApplySurface(
                _assignModal.AddComponent<Image>(),
                UITheme.WithAlpha(UITheme.OverlayPanelStrong, 0.98f),
                UIShapePreset.PanelLarge);

            var vlg = _assignModal.AddComponent<VerticalLayoutGroup>();
            vlg.padding = UITheme.Padding(UITheme.Spacing.Xl, UITheme.Spacing.Lg);
            vlg.spacing = UITheme.Spacing.Md;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var titleCard = MakeHRow(_assignModal.transform, 30);
            UITheme.ApplySurface(titleCard.AddComponent<Image>(), UITheme.WithAlpha(UITheme.SecondarySurface, 0.34f), UIShapePreset.Panel);
            MakeText(titleCard.transform, LocalizationService.Get("timetable.circulations.modal.assign.title"), 14, Color.white);

            var ddRow = MakeHRow(_assignModal.transform, 28);
            UITheme.ApplySurface(ddRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            MakeText(ddRow.transform, LocalizationService.Get("timetable.circulations.modal.assign.vehicle_label"), 11, UITheme.SecondaryText, preferredWidth: 60);
            _assignVehicleDropdown = MakeDropdown(ddRow.transform, 340);
            _assignVehicleDropdown.onValueChanged.AddListener(_ => UpdateAssignStatus());

            _assignStatusText = MakeText(_assignModal.transform, "", 11, UITheme.SecondaryText);
            var statusBg = _assignStatusText.gameObject.AddComponent<Image>();
            UITheme.ApplySurface(statusBg, UITheme.WithAlpha(UITheme.TopBarInset, 0.88f), UIShapePreset.Inset);

            var btnRow = MakeHRow(_assignModal.transform, 30);
            UITheme.ApplySurface(btnRow.AddComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.92f), UIShapePreset.Inset);
            MakeBtn(btnRow.transform, LocalizationService.Get("timetable.circulations.modal.common.cancel"), CloseAssignModal, new Color(0.3f, 0.3f, 0.4f));
            MakeBtn(btnRow.transform, LocalizationService.Get("timetable.circulations.modal.assign.confirm_btn"), OnAssignConfirmClicked, new Color(0.2f, 0.7f, 0.3f));

            _assignModal.SetActive(false);
        }
    }
}
