using System;
using System.Collections.Generic;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Timetable;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-8 / D5: CRUD turnusow pracowniczych (<see cref="CrewCirculation"/>).
    ///
    /// Turnusy to byty niezalezne od obiegow taboru (M5 <c>Circulation</c>). Gracz moze
    /// tworzyc recznie albo automatycznie (<see cref="CrewCirculationAutoGenerator"/> w M8-9).
    ///
    /// Stany (<see cref="CirculationStatus"/>):
    /// - Draft: edytowalny, nie przypisuje runtime — brak emitu CrewAssignment
    /// - Active: runtime — przy kazdym TrainRun'ie referencedTrainRunId zostanie matched
    ///   przez <c>CrewAssignmentService</c> (M8-11). Draft→Active wymaga walidacji OK (errors=0).
    /// - Paused: tymczasowo wstrzymany (pracownik chory / urlop)
    /// - Archived: historyczny, nie tworzy nowych assignments
    ///
    /// Multi-day (D20): <see cref="CrewCirculation.durationDays"/> 1-3 w EA. Duties z dayOffset 0..durationDays-1.
    /// Nocleg miedzy dniami = <c>CrewDutyKind.Overnight</c> z przypisanym <see cref="HotelBooking"/>.
    /// </summary>
    public static class CrewCirculationService
    {
        public static List<CrewCirculation> All { get; } = new();
        static int _nextId = 1;

        /// <summary>
        /// Cache trainRunId → CrewCirculation (Active only). Hot path: <c>CrewAssignmentService</c>
        /// wywołuje <see cref="FindByTrainRun"/> przy każdym TrainRun spawn (M9 simulator) — przy
        /// 200+ aktywnych pociągów i 100+ active circulations × ~10 duties bez cache to 200k+
        /// operacji per spawn.
        ///
        /// Maintained przez <see cref="MarkCacheDirty"/> wywoływane przy operacjach które
        /// zmieniają duty.referencedTrainRunId lub status circulation. Rebuild lazy gdy następna
        /// query — semantycznie spójne i unika kaskady rebuild'ów przy batch operacjach
        /// (np. <see cref="RestoreFromSave"/>).
        /// </summary>
        static readonly Dictionary<int, CrewCirculation> _byTrainRunId = new();
        static bool _cacheDirty = true;

        public static event Action<CrewCirculation> OnCreated;
        public static event Action<CrewCirculation> OnUpdated;
        public static event Action<CrewCirculation> OnDeleted;
        public static event Action<CrewCirculation> OnStatusChanged;
        public static event Action OnAnyChange;

        public static void ResetAll()
        {
            All.Clear();
            _nextId = 1;
            MarkCacheDirty();
            OnAnyChange?.Invoke();
        }

        /// <summary>Invaliduje cache <see cref="_byTrainRunId"/> — rebuild lazy w <see cref="FindByTrainRun"/>.</summary>
        static void MarkCacheDirty() => _cacheDirty = true;

        static void RebuildByTrainRunCache()
        {
            _byTrainRunId.Clear();
            foreach (var c in All)
            {
                if (c == null || c.status != CirculationStatus.Active) continue;
                if (c.duties == null) continue;
                foreach (var duty in c.duties)
                {
                    if (duty == null || duty.kind != CrewDutyKind.Service) continue;
                    if (duty.referencedTrainRunId < 0) continue;
                    // Pierwsza wygrywa — historycznie też tak (foreach return c)
                    _byTrainRunId.TryAdd(duty.referencedTrainRunId, c);
                }
            }
            _cacheDirty = false;
        }

        /// <summary>BUG-081: aktualny licznik crew circulation ID dla save.</summary>
        public static int GetNextId() => _nextId;

        /// <summary>
        /// BUG-081: restore state z save z _nextId. Wcześniej PersonnelSavable robił
        /// All.Clear() + Add() bezpośrednio bez restore'u counter'a → nowy Create dostawał id=1
        /// (kolizja z istniejącym crew circulation).
        /// </summary>
        public static void RestoreFromSave(System.Collections.Generic.IList<CrewCirculation> circulations, int nextId)
        {
            All.Clear();
            if (circulations != null)
            {
                foreach (var c in circulations)
                    if (c != null) All.Add(c);
            }
            _nextId = nextId > 0 ? nextId : 1;
            MarkCacheDirty();
            OnAnyChange?.Invoke();
        }

        /// <summary>
        /// M8-8: Publiczny helper dla zewnetrznych mutacji (np. HotelBookingModal callback).
        /// Wywoluje OnAnyChange + OnUpdated dla UI refresh.
        /// </summary>
        public static void NotifyChanged(int circulationId)
        {
            var c = GetById(circulationId);
            if (c != null) OnUpdated?.Invoke(c);
            OnAnyChange?.Invoke();
        }

        // ═══ CRUD ═══

        public static CrewCirculation Create(string name, EmployeeRole role)
        {
            if (role != EmployeeRole.Driver && role != EmployeeRole.Conductor)
            {
                Log.Warn($"[CrewCirculationService] Role {role} cannot have crew circulation (only Driver/Conductor)");
                return null;
            }

            var c = new CrewCirculation
            {
                crewCirculationId = _nextId++,
                name = string.IsNullOrWhiteSpace(name) ? $"Turnus #{_nextId - 1}" : name,
                role = role,
                assignedEmployeeId = -1,
                calendarDays = DayMask.Daily(),
                specificDates = new List<string>(),
                duties = new List<CrewDuty>(),
                status = CirculationStatus.Draft,
                durationDays = 1,
                notes = ""
            };
            All.Add(c);
            OnCreated?.Invoke(c);
            OnAnyChange?.Invoke();
            Log.Info($"[CrewCirculationService] Created #{c.crewCirculationId} '{c.name}' ({role})");
            return c;
        }

        public static bool Delete(int circulationId)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].crewCirculationId == circulationId)
                {
                    var c = All[i];
                    if (c.status == CirculationStatus.Active)
                    {
                        Log.Warn($"[CrewCirculationService] Cannot delete active circulation #{circulationId} — archive first");
                        return false;
                    }
                    All.RemoveAt(i);
                    MarkCacheDirty();
                    OnDeleted?.Invoke(c);
                    OnAnyChange?.Invoke();
                    Log.Info($"[CrewCirculationService] Deleted #{circulationId}");
                    return true;
                }
            }
            return false;
        }

        public static CrewCirculation GetById(int circulationId)
        {
            foreach (var c in All)
                if (c.crewCirculationId == circulationId) return c;
            return null;
        }

        public static List<CrewCirculation> GetByRole(EmployeeRole role)
        {
            var result = new List<CrewCirculation>();
            foreach (var c in All)
                if (c.role == role) result.Add(c);
            return result;
        }

        public static List<CrewCirculation> GetByEmployee(int employeeId)
        {
            var result = new List<CrewCirculation>();
            foreach (var c in All)
                if (c.assignedEmployeeId == employeeId) result.Add(c);
            return result;
        }

        public static List<CrewCirculation> GetByStatus(CirculationStatus status)
        {
            var result = new List<CrewCirculation>();
            foreach (var c in All)
                if (c.status == status) result.Add(c);
            return result;
        }

        // ═══ Mutations ═══

        public static bool Rename(int circulationId, string newName)
        {
            var c = GetById(circulationId);
            if (c == null) return false;
            c.name = string.IsNullOrWhiteSpace(newName) ? $"Turnus #{circulationId}" : newName;
            OnUpdated?.Invoke(c);
            OnAnyChange?.Invoke();
            return true;
        }

        public static bool SetCalendarDays(int circulationId, DayMask calendar)
        {
            var c = GetById(circulationId);
            if (c == null) return false;
            c.calendarDays = calendar;
            OnUpdated?.Invoke(c);
            OnAnyChange?.Invoke();
            return true;
        }

        public static bool SetDurationDays(int circulationId, int days)
        {
            var c = GetById(circulationId);
            if (c == null) return false;
            c.durationDays = Math.Clamp(days, 1, PersonnelBalanceConstants.CrewMaxMultiDayDays);
            OnUpdated?.Invoke(c);
            OnAnyChange?.Invoke();
            return true;
        }

        public static bool AssignEmployee(int circulationId, int employeeId)
        {
            var c = GetById(circulationId);
            if (c == null) return false;
            var e = PersonnelService.GetById(employeeId);
            if (e == null) return false;
            if (e.role != c.role)
            {
                Log.Warn($"[CrewCirculationService] Employee role mismatch: turnus wymaga {c.role}, pracownik jest {e.role}");
                return false;
            }
            c.assignedEmployeeId = employeeId;
            OnUpdated?.Invoke(c);
            OnAnyChange?.Invoke();
            Log.Info($"[CrewCirculationService] Assigned #{employeeId} {e.DisplayFullName} to turnus #{circulationId}");
            return true;
        }

        public static bool UnassignEmployee(int circulationId)
        {
            var c = GetById(circulationId);
            if (c == null) return false;
            c.assignedEmployeeId = -1;
            OnUpdated?.Invoke(c);
            OnAnyChange?.Invoke();
            return true;
        }

        /// <summary>
        /// BUG-085: clear assignedEmployeeId we wszystkich CrewCirculations które referowały
        /// danego pracownika. Wzorzec analogiczny do FurnitureAssignmentService.ReleaseAndReassignFifo.
        /// Wywoływane przez bootstrap subscriber na PersonnelService.OnEmployeeFired
        /// + PersonnelEvents.OnEmployeeRetired.
        /// Wcześniej: stale reference zostawała → UI showed "obieg #X przypisany: Jan Kowalski"
        /// dla zwolnionego pracownika (visual zombie). CrewAssignmentService defended via
        /// `IsActive` check, więc no crash, ale UX wrong.
        /// </summary>
        public static int ClearAssignmentsForEmployee(int employeeId)
        {
            if (employeeId <= 0) return 0;
            int cleared = 0;
            foreach (var c in All)
            {
                if (c == null) continue;
                if (c.assignedEmployeeId == employeeId)
                {
                    c.assignedEmployeeId = -1;
                    OnUpdated?.Invoke(c);
                    cleared++;
                }
            }
            if (cleared > 0)
            {
                OnAnyChange?.Invoke();
                Log.Info($"[CrewCirculationService] Cleared {cleared} assignment(s) dla zwolnionego/przeszłego na emeryturę emp #{employeeId}");
            }
            return cleared;
        }

        // ═══ Duties ═══

        public static int AddDuty(int circulationId, CrewDuty duty)
        {
            var c = GetById(circulationId);
            if (c == null || duty == null) return -1;
            duty.dutyIndex = c.duties.Count;
            c.duties.Add(duty);
            if (c.status == CirculationStatus.Active) MarkCacheDirty();
            OnUpdated?.Invoke(c);
            OnAnyChange?.Invoke();
            return duty.dutyIndex;
        }

        public static bool RemoveDuty(int circulationId, int dutyIndex)
        {
            var c = GetById(circulationId);
            if (c == null || dutyIndex < 0 || dutyIndex >= c.duties.Count) return false;
            c.duties.RemoveAt(dutyIndex);
            // Reindex
            for (int i = 0; i < c.duties.Count; i++) c.duties[i].dutyIndex = i;
            if (c.status == CirculationStatus.Active) MarkCacheDirty();
            OnUpdated?.Invoke(c);
            OnAnyChange?.Invoke();
            return true;
        }

        public static bool MoveDutyUp(int circulationId, int dutyIndex)
        {
            var c = GetById(circulationId);
            if (c == null || dutyIndex <= 0 || dutyIndex >= c.duties.Count) return false;
            (c.duties[dutyIndex - 1], c.duties[dutyIndex]) = (c.duties[dutyIndex], c.duties[dutyIndex - 1]);
            for (int i = 0; i < c.duties.Count; i++) c.duties[i].dutyIndex = i;
            OnUpdated?.Invoke(c);
            OnAnyChange?.Invoke();
            return true;
        }

        public static bool MoveDutyDown(int circulationId, int dutyIndex)
        {
            var c = GetById(circulationId);
            if (c == null || dutyIndex < 0 || dutyIndex >= c.duties.Count - 1) return false;
            (c.duties[dutyIndex + 1], c.duties[dutyIndex]) = (c.duties[dutyIndex], c.duties[dutyIndex + 1]);
            for (int i = 0; i < c.duties.Count; i++) c.duties[i].dutyIndex = i;
            OnUpdated?.Invoke(c);
            OnAnyChange?.Invoke();
            return true;
        }

        // ═══ Status transitions ═══

        public static bool Activate(int circulationId)
        {
            var c = GetById(circulationId);
            if (c == null) return false;
            if (c.status == CirculationStatus.Active) return true;

            // Validate before activation (errors=0 wymagane)
            var validation = CrewCirculationValidator.Validate(c);
            if (!validation.IsValid)
            {
                Log.Warn($"[CrewCirculationService] Cannot activate #{circulationId} — {validation.Errors.Count} errors:");
                foreach (var err in validation.Errors)
                    Log.Warn($"  • {err}");
                return false;
            }

            c.status = CirculationStatus.Active;
            MarkCacheDirty();
            OnStatusChanged?.Invoke(c);
            OnAnyChange?.Invoke();
            Log.Info($"[CrewCirculationService] #{circulationId} '{c.name}' → Active");
            return true;
        }

        public static bool Pause(int circulationId)
        {
            var c = GetById(circulationId);
            if (c == null) return false;
            c.status = CirculationStatus.Paused;
            MarkCacheDirty();
            OnStatusChanged?.Invoke(c);
            OnAnyChange?.Invoke();
            return true;
        }

        public static bool Archive(int circulationId)
        {
            var c = GetById(circulationId);
            if (c == null) return false;
            c.status = CirculationStatus.Archived;
            MarkCacheDirty();
            OnStatusChanged?.Invoke(c);
            OnAnyChange?.Invoke();
            return true;
        }

        public static bool BackToDraft(int circulationId)
        {
            var c = GetById(circulationId);
            if (c == null) return false;
            c.status = CirculationStatus.Draft;
            MarkCacheDirty();
            OnStatusChanged?.Invoke(c);
            OnAnyChange?.Invoke();
            return true;
        }

        // ═══ Query helpers (dla runtime — M8-11) ═══

        /// <summary>Zwraca CrewCirculation przypisany do tego TrainRun w tej dacie (Active only).</summary>
        public static CrewCirculation FindByTrainRun(int trainRunId, string dateIso)
        {
            if (_cacheDirty) RebuildByTrainRunCache();
            return _byTrainRunId.TryGetValue(trainRunId, out var c) ? c : null;
        }
    }

    /// <summary>
    /// BUG-085: Bootstrap subscribing CrewCirculationService.ClearAssignmentsForEmployee
    /// na PersonnelService.OnEmployeeFired + PersonnelEvents.OnEmployeeRetired.
    /// Bez tego stale assignedEmployeeId persistował forever (UI visual zombie po fire,
    /// save/load preserved orphan reference).
    /// Wzorzec analogiczny do FurnitureAssignmentService bootstrap (subscriptions w PersonnelDispatcher3D).
    /// </summary>
    public static class CrewCirculationServiceBootstrap
    {
        // Idempotent guard — RuntimeInitializeOnLoadMethod może być wywołane wielokrotnie
        // w Editor przy "Disable Domain Reload" (popularny dla iteracji speed). Bez guarda
        // subskrypcje stackują się przy każdym Play → multiplikowane wywołania
        // ClearAssignmentsForEmployee per fire.
        private static bool _subscribed;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            PersonnelService.OnEmployeeFired += HandleEmployeeLost;
            PersonnelEvents.OnEmployeeRetired += HandleEmployeeLost;
        }

        private static void HandleEmployeeLost(Employee e)
        {
            if (e == null) return;
            CrewCirculationService.ClearAssignmentsForEmployee(e.employeeId);
        }
    }
}
