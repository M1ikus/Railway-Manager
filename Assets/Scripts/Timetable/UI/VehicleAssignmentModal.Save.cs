using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;

namespace RailwayManager.Timetable
{
    public partial class VehicleAssignmentModal
    {
        // ─────────────────────────────────────────────
        //  Save + validation + status update
        // ─────────────────────────────────────────────

        private void OnSaveClicked()
        {
            if (_target == null) return;

            // Walidacja 1: konflikty z innymi obiegami
            int conflicts = CountConflicts();
            if (conflicts > 0)
            {
                if (_statusText != null)
                {
                    _statusText.text = $"⚠ {conflicts} konfliktów pojazdu z innymi obiegami — usuń je zanim zapiszesz";
                    _statusText.color = UITheme.Danger;
                }
                return;
            }

            // Walidacja 2: skład (consist) — lokomotywa + wagony vs EMU/DMU itp.
            int consistErrors = CountConsistErrors();
            if (consistErrors > 0)
            {
                if (_statusText != null)
                {
                    _statusText.text = $"⚠ {consistErrors} dni z niepoprawnym składem (np. wagon bez lokomotywy, mix EMU/DMU)";
                    _statusText.color = UITheme.Danger;
                }
                return;
            }

            // Zaktualizuj też legacy assignedVehicleIds (union wszystkich dni) — do query helperów
            var union = new HashSet<int>();
            foreach (var list in _target.vehicleAssignmentsPerDay.Values)
                if (list != null) foreach (var id in list) union.Add(id);
            _target.assignedVehicleIds = new List<int>(union);

            // Ustaw assignedCirculationId w FleetVehicleData (primary = ten obieg)
            foreach (var vid in union)
            {
                foreach (var v in FleetService.OwnedVehicles)
                {
                    if (v != null && v.id == vid)
                    {
                        v.assignedCirculationId = _target.id;
                        break;
                    }
                }
            }

            Log.Info($"[VehicleAssign] Zapisano przypisania: obieg #{_target.id} ma {_target.vehicleAssignmentsPerDay.Count} dni przypisanych, "
                     + $"łącznie {union.Count} unikalnych pojazdów");
            Close();
        }

        private int CountConflicts()
        {
            if (_target?.vehicleAssignmentsPerDay == null) return 0;
            int conflicts = 0;
            foreach (var kvp in _target.vehicleAssignmentsPerDay)
            {
                foreach (var vid in kvp.Value)
                {
                    var other = CirculationService.GetVehicleConflictForDate(vid, kvp.Key, _target.id);
                    if (other != null) conflicts++;
                }
            }
            return conflicts;
        }

        /// <summary>
        /// Liczba dni ze stanem składu (consist) nieprawidłowym — np. wagon bez lokomotywy
        /// lub mieszanka EMU+DMU. Używane do blokowania Save.
        /// </summary>
        private int CountConsistErrors()
        {
            if (_target?.vehicleAssignmentsPerDay == null) return 0;
            int errors = 0;
            foreach (var kvp in _target.vehicleAssignmentsPerDay)
            {
                if (kvp.Value == null || kvp.Value.Count == 0) continue;
                var result = ConsistValidator.Validate(kvp.Value);
                if (result.IsBlocking) errors++;
            }
            return errors;
        }

        private void UpdateStatus()
        {
            if (_statusText == null || _target == null) return;
            int assignedDays = _target.vehicleAssignmentsPerDay?.Count ?? 0;
            int totalDays = _activeDates.Count;
            int conflicts = CountConflicts();
            int consistErrors = CountConsistErrors();

            if (assignedDays == 0)
            {
                _statusText.text = LocalizationService.Get("timetable.vehicle_assign.status.drag_hint");
                _statusText.color = UITheme.SecondaryText;
                return;
            }

            var parts = new List<string> { string.Format(LocalizationService.Get("timetable.vehicle_assign.status.assigned_format"), assignedDays, totalDays) };
            if (conflicts > 0) parts.Add(string.Format(LocalizationService.Get("timetable.vehicle_assign.status.conflicts_format"), conflicts));
            if (consistErrors > 0) parts.Add(string.Format(LocalizationService.Get("timetable.vehicle_assign.status.consist_errors_format"), consistErrors));

            if (conflicts > 0 || consistErrors > 0)
            {
                _statusText.text = string.Join(LocalizationService.Get("timetable.vehicle_assign.status.separator"), parts);
                _statusText.color = UITheme.Warning;
            }
            else
            {
                _statusText.text = string.Format(LocalizationService.Get("timetable.vehicle_assign.status.ok_prefix_format"), parts[0]);
                _statusText.color = UITheme.Success;
            }
        }
    }
}
