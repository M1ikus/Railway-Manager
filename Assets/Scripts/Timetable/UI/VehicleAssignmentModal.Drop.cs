using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable
{
    public partial class VehicleAssignmentModal
    {
        // ─────────────────────────────────────────────
        //  Drop handling — drop / remove / copy / sort
        // ─────────────────────────────────────────────

        private void OnVehicleDroppedOnDay(string dateIso, int vehicleId)
        {
            if (_target == null) return;
            if (_target.vehicleAssignmentsPerDay == null)
                _target.vehicleAssignmentsPerDay = new Dictionary<string, List<int>>();

            if (!_target.vehicleAssignmentsPerDay.TryGetValue(dateIso, out var list) || list == null)
            {
                list = new List<int>();
                _target.vehicleAssignmentsPerDay[dateIso] = list;
            }
            if (list.Contains(vehicleId))
            {
                Log.Info($"[VehicleAssign] Pojazd #{vehicleId} już jest w dniu {dateIso}");
                return;
            }
            list.Add(vehicleId);
            SortConsist(list); // auto-sort: lok/ZT na przód, wagony na tył
            Log.Info($"[VehicleAssign] Dodano pojazd #{vehicleId} do dnia {dateIso} obiegu #{_target.id}");
            RefreshDays();
            RefreshPool();
            UpdateStatus();
        }

        /// <summary>
        /// Auto-sortuje listę tak że lokomotywy i zespoły trakcyjne (EMU/DMU) są na przodzie,
        /// wagony na końcu. Stabilny sort — zachowuje względną kolejność wewnątrz każdej grupy.
        /// User może drop'nąć wagon zanim doda lok — po drop lok'a reorder automatycznie.
        /// </summary>
        private static void SortConsist(List<int> list)
        {
            if (list == null || list.Count <= 1) return;
            var powers = new List<int>();
            var cars = new List<int>();
            foreach (var id in list)
            {
                FleetVehicleData v = null;
                foreach (var fv in FleetService.OwnedVehicles)
                    if (fv != null && fv.id == id) { v = fv; break; }
                if (v == null) { cars.Add(id); continue; }
                bool isPower = v.type == FleetVehicleType.ElectricLocomotive
                            || v.type == FleetVehicleType.DieselLocomotive
                            || v.type == FleetVehicleType.EMU
                            || v.type == FleetVehicleType.DMU;
                if (isPower) powers.Add(id);
                else cars.Add(id);
            }
            list.Clear();
            list.AddRange(powers);
            list.AddRange(cars);
        }

        private void OnRemoveVehicleFromDay(string dateIso, int vehicleId)
        {
            if (_target?.vehicleAssignmentsPerDay == null) return;
            if (_target.vehicleAssignmentsPerDay.TryGetValue(dateIso, out var list) && list != null)
            {
                list.Remove(vehicleId);
                if (list.Count == 0) _target.vehicleAssignmentsPerDay.Remove(dateIso);
                RefreshDays();
                RefreshPool();
                UpdateStatus();
            }
        }

        private void OnCopyToAllDays(string sourceDateIso)
        {
            if (_target?.vehicleAssignmentsPerDay == null) return;
            if (!_target.vehicleAssignmentsPerDay.TryGetValue(sourceDateIso, out var sourceList)
                || sourceList == null || sourceList.Count == 0) return;

            int copied = 0;
            foreach (var date in _activeDates)
            {
                string key = date.ToString("yyyy-MM-dd");
                if (key == sourceDateIso) continue;
                _target.vehicleAssignmentsPerDay[key] = new List<int>(sourceList);
                copied++;
            }
            Log.Info($"[VehicleAssign] Skopiowano skład z {sourceDateIso} do {copied} innych dni");
            RefreshDays();
            RefreshPool();
            UpdateStatus();
        }
    }
}
