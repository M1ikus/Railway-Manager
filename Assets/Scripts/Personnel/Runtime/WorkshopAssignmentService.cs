using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.Maintenance;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-12: Serwis przypisan mechanikow do slotow warsztatowych (M7 <see cref="WorkshopManager"/>).
    ///
    /// Funkcjonalnosc:
    /// - Trzyma mapping <c>slotId → List&lt;mechanicId&gt;</c>
    /// - Wstawia hooki w M7: <see cref="WorkshopManager.SlotSpeedMultiplierHook"/> +
    ///   <see cref="BreakdownService.SelfRepairBonusHook"/>
    /// - Oblicza:
    ///   - Slot speed multiplier: wzor D2 <c>1/(0.5 + avgSkill/5)</c> (1★=1.66×, 5★=0.6×)
    ///   - Self-repair bonus: <c>baseChance × (0.7 + avgSkill/10)</c>
    ///
    /// Pensje mechanikow (M8-5 PayrollService) juz dziala przez <see cref="EmployeeRole.Mechanic"/> —
    /// placeholder 'Internal workshop cost' w M7 nie jest usuwany (to parts + overhead),
    /// a pensje dochodza jako osobna kategoria 'Personnel'.
    ///
    /// Warstwa abstrakcji — Personnel wie o M7, M7 nie wie o Personnel (hook-based DI).
    /// </summary>
    public class WorkshopAssignmentService : MonoBehaviour
    {
        public static WorkshopAssignmentService Instance { get; private set; }

        /// <summary>slotId → lista mechanicIds przypisanych.</summary>
        static readonly Dictionary<int, List<int>> _slotMechanics = new();

        public static event Action OnAssignmentsChanged;

        public static WorkshopAssignmentService EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("WorkshopAssignmentService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<WorkshopAssignmentService>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            InstallHooks();
            Log.Info("[WorkshopAssignmentService] Bootstrapped + hooks installed (M7 WorkshopManager + BreakdownService)");
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                // Uninstall hooks
                WorkshopManager.SlotSpeedMultiplierHook = null;
                BreakdownService.SelfRepairBonusHook = null;
            }
        }

        void OnEnable()
        {
            PersonnelService.OnEmployeesChanged += OnEmployeesChanged;
        }

        void OnDisable()
        {
            PersonnelService.OnEmployeesChanged -= OnEmployeesChanged;
        }

        void OnEmployeesChanged()
        {
            // Cleanup: remove fired/retired mechanics from assignments
            var toCleanup = new List<(int slotId, int mechanicId)>();
            foreach (var kv in _slotMechanics)
            {
                foreach (var mid in kv.Value)
                {
                    var e = PersonnelService.GetById(mid);
                    if (e == null || !e.IsActive || e.role != EmployeeRole.Mechanic)
                        toCleanup.Add((kv.Key, mid));
                }
            }
            foreach (var (slotId, mechanicId) in toCleanup)
                Unassign(mechanicId, slotId);
        }

        void InstallHooks()
        {
            WorkshopManager.SlotSpeedMultiplierHook = ComputeSpeedMultiplierForSlot;
            BreakdownService.SelfRepairBonusHook = ComputeSelfRepairBonus;
        }

        // ═══ Assignments ═══

        /// <summary>Przypisuje mechanika do slotu. Max 2 mechanikow per slot (D4).</summary>
        public static bool Assign(int mechanicId, int slotId)
        {
            var e = PersonnelService.GetById(mechanicId);
            if (e == null || e.role != EmployeeRole.Mechanic || !e.IsActive)
            {
                Log.Warn($"[WorkshopAssignmentService] Assign: invalid mechanic #{mechanicId}");
                return false;
            }

            if (!_slotMechanics.TryGetValue(slotId, out var list))
            {
                list = new List<int>();
                _slotMechanics[slotId] = list;
            }
            if (list.Contains(mechanicId)) return true; // already assigned
            if (list.Count >= 2)
            {
                Log.Warn($"[WorkshopAssignmentService] Slot #{slotId} full (max 2 mechanics)");
                return false;
            }

            list.Add(mechanicId);
            // Update employee assignments
            e.assignedWorkshopSlotIds ??= new List<int>();
            if (!e.assignedWorkshopSlotIds.Contains(slotId))
                e.assignedWorkshopSlotIds.Add(slotId);

            OnAssignmentsChanged?.Invoke();
            PersonnelService.NotifyStatusChanged(e);
            Log.Info($"[WorkshopAssignmentService] Assigned mechanic #{mechanicId} {e.DisplayFullName} → slot #{slotId}");
            return true;
        }

        public static bool Unassign(int mechanicId, int slotId)
        {
            if (!_slotMechanics.TryGetValue(slotId, out var list)) return false;
            if (!list.Remove(mechanicId)) return false;

            var e = PersonnelService.GetById(mechanicId);
            if (e != null) e.assignedWorkshopSlotIds.Remove(slotId);

            OnAssignmentsChanged?.Invoke();
            if (e != null) PersonnelService.NotifyStatusChanged(e);
            Log.Info($"[WorkshopAssignmentService] Unassigned mechanic #{mechanicId} from slot #{slotId}");
            return true;
        }

        public static List<int> GetMechanicsForSlot(int slotId)
        {
            return _slotMechanics.TryGetValue(slotId, out var list) ? new List<int>(list) : new List<int>();
        }

        public static bool IsMechanicAssigned(int mechanicId)
        {
            foreach (var list in _slotMechanics.Values)
                if (list.Contains(mechanicId)) return true;
            return false;
        }

        public static Dictionary<int, List<int>> GetAssignmentsSnapshot()
        {
            var snapshot = new Dictionary<int, List<int>>();
            foreach (var kv in _slotMechanics)
                snapshot[kv.Key] = new List<int>(kv.Value);
            return snapshot;
        }

        public static void RestoreFromSave(Dictionary<int, List<int>> assignments)
        {
            _slotMechanics.Clear();

            foreach (var e in PersonnelService.Employees)
            {
                if (e == null) continue;
                e.assignedWorkshopSlotIds ??= new List<int>();
                e.assignedWorkshopSlotIds.Clear();
            }

            if (assignments != null)
            {
                foreach (var kv in assignments)
                {
                    int slotId = kv.Key;
                    if (slotId < 0 || kv.Value == null) continue;

                    var list = new List<int>();
                    foreach (var mechanicId in kv.Value)
                    {
                        if (list.Count >= 2) break;
                        if (list.Contains(mechanicId)) continue;

                        var e = PersonnelService.GetById(mechanicId);
                        if (e == null || !e.IsActive || e.role != EmployeeRole.Mechanic) continue;

                        list.Add(mechanicId);
                        e.assignedWorkshopSlotIds ??= new List<int>();
                        if (!e.assignedWorkshopSlotIds.Contains(slotId))
                            e.assignedWorkshopSlotIds.Add(slotId);
                    }

                    if (list.Count > 0)
                        _slotMechanics[slotId] = list;
                }
            }

            OnAssignmentsChanged?.Invoke();
        }

        public static void ClearAll()
        {
            _slotMechanics.Clear();
            foreach (var e in PersonnelService.Employees)
            {
                if (e == null) continue;
                e.assignedWorkshopSlotIds ??= new List<int>();
                e.assignedWorkshopSlotIds.Clear();
            }
            OnAssignmentsChanged?.Invoke();
        }

        // ═══ Hook implementations ═══

        /// <summary>
        /// Slot speed multiplier — <c>1/(0.5 + avgSkill/5)</c> (D2):
        /// - 0 mechanikow: 1.0× (baseline, ale slot "idle" logicznie)
        /// - 1★ skill: 1.66× (dluzej)
        /// - 3★ skill: 1.00× (baseline)
        /// - 5★ skill: 0.60× (szybciej)
        ///
        /// Multi-mechanic: uzywamy avg skilla (min 1 aktywnych).
        /// </summary>
        static float ComputeSpeedMultiplierForSlot(int slotId)
        {
            if (!_slotMechanics.TryGetValue(slotId, out var list) || list.Count == 0)
                return 1.0f;

            int totalSkill = 0, activeCount = 0;
            foreach (var mid in list)
            {
                var e = PersonnelService.GetById(mid);
                if (e == null || !e.IsActive) continue;
                if (e.status != EmployeeStatus.OnShift && e.status != EmployeeStatus.Available) continue;
                totalSkill += e.skill;
                activeCount++;
            }
            if (activeCount == 0) return 1.0f;

            float avgSkill = totalSkill / (float)activeCount;
            return 1f / (0.5f + avgSkill / 5f);
        }

        /// <summary>
        /// Self-repair bonus — <c>baseChance × (0.7 + avgSkill/10)</c>:
        /// - 1★: ×0.8 (obniza z 50% do 40% przy 100% health)
        /// - 3★: ×1.0 (baseline)
        /// - 5★: ×1.2 (podnosi do 60%)
        ///
        /// Mechanik "z najblizszego depotu" — w M8-12 MVP: avg skill wszystkich Available mechanikow
        /// (depot scope w post-M2.6 multi-depot).
        /// </summary>
        static float ComputeSelfRepairBonus(int vehicleId, float baseChance)
        {
            int totalSkill = 0, count = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (e.role != EmployeeRole.Mechanic) continue;
                if (!e.IsActive) continue;
                if (e.status != EmployeeStatus.OnShift && e.status != EmployeeStatus.Available) continue;
                totalSkill += e.skill;
                count++;
            }
            if (count == 0) return baseChance;

            float avgSkill = totalSkill / (float)count;
            float bonus = 0.7f + avgSkill / 10f;
            return baseChance * bonus;
        }

        // ═══ Debug ═══

        [ContextMenu("Debug: Report slot assignments")]
        public void DebugReport()
        {
            if (_slotMechanics.Count == 0)
            {
                Log.Info("[WorkshopAssignmentService] No slot assignments.");
                return;
            }
            Log.Info($"[WorkshopAssignmentService] Assignments ({_slotMechanics.Count} slots):");
            foreach (var kv in _slotMechanics)
            {
                float mult = ComputeSpeedMultiplierForSlot(kv.Key);
                Log.Info($"  Slot #{kv.Key}: {kv.Value.Count} mechanic(s), speed multiplier={mult:F2}");
                foreach (var mid in kv.Value)
                {
                    var e = PersonnelService.GetById(mid);
                    if (e != null)
                        Log.Info($"    - #{mid} {e.DisplayFullName} {e.skill}★ [{e.status}]");
                }
            }
        }

        [ContextMenu("Debug: Auto-assign first idle mechanic to first slot")]
        public void DebugAutoAssign()
        {
            var wm = WorkshopManager.Instance;
            if (wm == null || wm.Slots.Count == 0)
            {
                Log.Warn("[WorkshopAssignmentService] No workshop slots (build workshop in Depot).");
                return;
            }

            Employee mechanic = null;
            foreach (var e in PersonnelService.Employees)
            {
                if (e.role == EmployeeRole.Mechanic && e.IsActive && !IsMechanicAssigned(e.employeeId))
                {
                    mechanic = e;
                    break;
                }
            }
            if (mechanic == null)
            {
                Log.Warn("[WorkshopAssignmentService] No idle mechanic — hire some.");
                return;
            }

            Assign(mechanic.employeeId, wm.Slots[0].slotId);
        }
    }
}
