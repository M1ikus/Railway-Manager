using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-15 / §3.8 + D19: Serwis przypisan kasjerow do stacji.
    ///
    /// <b>D19:</b> kasjer NIE spawnuje sie jako agent 3D — widoczny tylko w popupie stacji
    /// (UI M9a station marker popup).
    ///
    /// Revenue bonus (D3.8): +<see cref="PersonnelBalanceConstants.TicketClerkRevenueBonus"/> (8%)
    /// z danej stacji gdy obsadzona. M6 Economy hook:
    /// <c>stationRevenue × (1 + TicketClerkService.GetRevenueBonus(stationId))</c>.
    ///
    /// Mapping: stationId → employeeId (1 kasjer per stacja per shift w EA).
    /// Post-EA: multi-kasjer dla multi-shift coverage (24/7 stacja).
    /// </summary>
    public class TicketClerkService : MonoBehaviour
    {
        public static TicketClerkService Instance { get; private set; }

        /// <summary>Mapping stationId → StationAssignment (1 kasjer per stacja w M8-15 MVP).</summary>
        static readonly Dictionary<int, StationAssignment> _assignments = new();

        public static event Action OnAssignmentsChanged;

        public static TicketClerkService EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("TicketClerkService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<TicketClerkService>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable() { PersonnelService.OnEmployeesChanged += OnEmployeesChanged; }
        void OnDisable() { PersonnelService.OnEmployeesChanged -= OnEmployeesChanged; }

        void OnEmployeesChanged()
        {
            // Cleanup: zwolnieni/emeryci kasjerzy usuniaci z mapping
            var toRemove = new List<int>();
            foreach (var kv in _assignments)
            {
                var e = PersonnelService.GetById(kv.Value.employeeId);
                if (e == null || !e.IsActive || e.role != EmployeeRole.TicketClerk)
                    toRemove.Add(kv.Key);
            }
            foreach (var sid in toRemove) Unassign(sid);
        }

        // ═══ API ═══

        public static bool Assign(int employeeId, int stationId, ShiftType shift = ShiftType.Morning)
        {
            var e = PersonnelService.GetById(employeeId);
            if (e == null || e.role != EmployeeRole.TicketClerk || !e.IsActive)
            {
                Log.Warn($"[TicketClerkService] Assign: invalid ticket clerk #{employeeId}");
                return false;
            }

            // Jesli juz przypisany do innej stacji, najpierw unassign
            int oldStationId = GetStationForEmployee(employeeId);
            if (oldStationId >= 0 && oldStationId != stationId)
                Unassign(oldStationId);

            _assignments[stationId] = new StationAssignment
            {
                stationId = stationId,
                employeeId = employeeId,
                assignedSinceDateIso = GameState.CurrentDateIso,
                shift = shift
            };
            e.assignedStationId = stationId;

            OnAssignmentsChanged?.Invoke();
            PersonnelService.NotifyStatusChanged(e);
            Log.Info($"[TicketClerkService] Assigned clerk #{employeeId} {e.DisplayFullName} → station #{stationId} (shift {shift})");
            return true;
        }

        public static bool Unassign(int stationId)
        {
            if (!_assignments.TryGetValue(stationId, out var a)) return false;
            _assignments.Remove(stationId);

            var e = PersonnelService.GetById(a.employeeId);
            if (e != null)
            {
                e.assignedStationId = -1;
                PersonnelService.NotifyStatusChanged(e);
            }

            OnAssignmentsChanged?.Invoke();
            Log.Info($"[TicketClerkService] Unassigned clerk from station #{stationId}");
            return true;
        }

        public static StationAssignment GetAssignmentForStation(int stationId)
        {
            return _assignments.TryGetValue(stationId, out var a) ? a : null;
        }

        public static int GetStationForEmployee(int employeeId)
        {
            foreach (var kv in _assignments)
                if (kv.Value.employeeId == employeeId) return kv.Key;
            return -1;
        }

        public static IReadOnlyDictionary<int, StationAssignment> AllAssignments => _assignments;

        public static List<StationAssignment> GetAssignmentsSnapshot()
        {
            return new List<StationAssignment>(_assignments.Values);
        }

        public static void RestoreFromSave(IList<StationAssignment> assignments)
        {
            _assignments.Clear();

            foreach (var e in PersonnelService.Employees)
            {
                if (e != null && e.role == EmployeeRole.TicketClerk)
                    e.assignedStationId = -1;
            }

            if (assignments != null)
            {
                foreach (var a in assignments)
                {
                    if (a == null || a.stationId < 0 || a.employeeId <= 0) continue;

                    var e = PersonnelService.GetById(a.employeeId);
                    if (e == null || !e.IsActive || e.role != EmployeeRole.TicketClerk) continue;

                    _assignments[a.stationId] = a;
                    e.assignedStationId = a.stationId;
                }
            }

            OnAssignmentsChanged?.Invoke();
        }

        /// <summary>
        /// Revenue bonus dla stacji (0.00-0.08). 0 gdy brak obsadzonego kasjera.
        /// M6 Economy hook: <c>revenue × (1 + GetRevenueBonus(stationId))</c>.
        /// </summary>
        public static float GetRevenueBonus(int stationId)
        {
            if (!_assignments.TryGetValue(stationId, out var a)) return 0f;
            var e = PersonnelService.GetById(a.employeeId);
            if (e == null || !e.IsActive) return 0f;
            if (e.status != EmployeeStatus.OnShift && e.status != EmployeeStatus.Available) return 0f;
            return PersonnelBalanceConstants.TicketClerkRevenueBonus;
        }

        public static void ResetAll()
        {
            _assignments.Clear();
            foreach (var e in PersonnelService.Employees)
            {
                if (e != null && e.role == EmployeeRole.TicketClerk)
                    e.assignedStationId = -1;
            }
            OnAssignmentsChanged?.Invoke();
        }

        // ═══ Debug ═══

        [ContextMenu("Debug: Auto-assign clerks to first N stations")]
        public void DebugAutoAssignSampleStations()
        {
            int stationCounter = 1000;
            int assigned = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (e.role != EmployeeRole.TicketClerk || !e.IsActive) continue;
                if (GetStationForEmployee(e.employeeId) >= 0) continue;
                Assign(e.employeeId, stationCounter++);
                assigned++;
            }
            Log.Info($"[TicketClerkService] Auto-assigned {assigned} clerks to sample stations #{1000}..#{stationCounter - 1}");
        }

        [ContextMenu("Debug: Report assignments")]
        public void DebugReport()
        {
            if (_assignments.Count == 0)
            {
                Log.Info("[TicketClerkService] No assignments.");
                return;
            }
            Log.Info($"[TicketClerkService] Assignments ({_assignments.Count}):");
            foreach (var kv in _assignments)
            {
                var e = PersonnelService.GetById(kv.Value.employeeId);
                float bonus = GetRevenueBonus(kv.Key);
                Log.Info($"  Station #{kv.Key}: {(e != null ? e.DisplayFullName : "?")} " +
                         $"(shift {kv.Value.shift}, bonus +{bonus * 100:F0}%)");
            }
        }
    }
}
