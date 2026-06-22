using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-13 / §7.6: Runtime assignment mapowania pracownikow do TrainRun'ow.
    ///
    /// Zrodla informacji:
    /// 1. <see cref="CrewCirculationService.FindByTrainRun"/> — aktywne turnusy
    /// 2. <see cref="DispatcherService.TryAutoAssignReplacement"/> — ad-hoc (gdy brak turnusu)
    ///
    /// Hook w <see cref="TrainRunSimulator.CrewCheckHook"/> — instalowany w Awake bootstrap.
    /// Zwraca false gdy <see cref="RequireCrewForCirculation"/> ON i brak maszynisty → pociag
    /// opoznia start (TrainRunSimulator.SpawnTrain kumuluje currentDelaySec).
    ///
    /// Feature flag <see cref="RequireCrewForCirculation"/>: default OFF (M8 dev mode — pociagi
    /// zawsze startuja). Gracz / debug moga wlaczyc przez toggle.
    ///
    /// Deadhead (D5) — pracownik jako pasazer w innym pociagu: post-M8 visualization.
    /// W M8-13: tylko walidacja (CrewDutyKind.Deadhead liczy sie w godziny pracy).
    /// </summary>
    public class CrewAssignmentService : MonoBehaviour
    {
        public static CrewAssignmentService Instance { get; private set; }

        /// <summary>
        /// Feature flag — default OFF (M8 dev). Gdy ON: brak maszynisty = pociag nie startuje.
        /// Toggle przez debug ContextMenu + settings UI (M13).
        /// </summary>
        public static bool RequireCrewForCirculation { get; set; } = false;

        public static event Action<bool> OnFlagChanged;

        public static CrewAssignmentService EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("CrewAssignmentService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CrewAssignmentService>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // Install hook
            TrainRunSimulator.CrewCheckHook = CheckCrewAvailability;
            Log.Info("[CrewAssignmentService] Bootstrapped + CrewCheckHook installed on TrainRunSimulator");
        }

        void OnDestroy()
        {
            if (Instance == this && TrainRunSimulator.CrewCheckHook == CheckCrewAvailability)
                TrainRunSimulator.CrewCheckHook = null;
        }

        // ═══ M-Windows P4: pracownik → bieżący aktywny TrainRun ═══

        /// <summary>
        /// Bieżący aktywny TrainRun maszynisty/konduktora (gdy jedzie / zaraz wyjeżdża). Skrót przez
        /// transient <c>workflowTargetId</c> (ustawiany przez DriverConductorWorkflow przy wejściu na
        /// pociąg). Zwraca null dla ról bez obiegu lub gdy pracownik nie jest aktualnie przy pociągu.
        /// </summary>
        public static TrainRun GetCurrentTrainRunForEmployee(int employeeId)
        {
            var e = PersonnelService.GetById(employeeId);
            if (e == null || !e.CanHaveCrewCirculation) return null;
            if (e.workflowTargetId <= 0) return null;
            bool onTrain = e.workflowState == EmployeeWorkflowState.DrivingTrain
                        || e.workflowState == EmployeeWorkflowState.AwaitingDeparture
                        || e.workflowState == EmployeeWorkflowState.GoingToVehicle;
            if (!onTrain) return null;
            var runs = TimetableService.TrainRuns;
            for (int i = 0; i < runs.Count; i++)
                if (runs[i].id == e.workflowTargetId) return runs[i];
            return null;
        }

        // ═══ Hook: crew check ═══

        /// <summary>
        /// Hook dla <see cref="TrainRunSimulator.SpawnTrain"/>. Zwraca true jesli zaloga obsadzona
        /// (lub flag OFF — backward compat).
        /// </summary>
        bool CheckCrewAvailability(int trainRunId, string dateIso)
        {
            if (!RequireCrewForCirculation) return true; // M8 dev mode: zawsze startuj

            var driver = GetDriverForTrainRun(trainRunId, dateIso);
            if (driver == null)
            {
                // Sprobuj dispatcher auto-assign ad-hoc
                TrainRun tr = null;
                foreach (var t in TimetableService.TrainRuns)
                {
                    if (t.id == trainRunId) { tr = t; break; }
                }
                if (tr != null)
                {
                    var vacancy = new CrewVacancyData
                    {
                        employeeId = -1, // ad-hoc, nie vacancy konkretnej osoby
                        role = EmployeeRole.Driver,
                        affectedDateIso = dateIso,
                        reason = CrewVacancyReason.Unknown,
                        customMessage = $"Ad-hoc TR#{trainRunId} — brak turnusu/maszynisty"
                    };
                    var result = DispatcherService.TryAutoAssignReplacement(vacancy);
                    if (result == DispatchResult.Success)
                    {
                        Log.Info($"[CrewAssignmentService] Dispatcher auto-assigned driver dla TR#{trainRunId}");
                        return true;
                    }
                }

                // Brak dispatchera lub no candidate → blokuj spawn
                return false;
            }

            return true;
        }

        // ═══ Public API ═══

        /// <summary>
        /// Znajdz maszyniste przypisanego do TrainRun w danej dacie.
        /// Iteruje aktywne CrewCirculation, szuka duty.referencedTrainRunId == trainRunId.
        /// </summary>
        public static Employee GetDriverForTrainRun(int trainRunId, string dateIso)
        {
            return GetCrewMemberForTrainRun(trainRunId, dateIso, EmployeeRole.Driver);
        }

        public static Employee GetConductorForTrainRun(int trainRunId, string dateIso)
        {
            return GetCrewMemberForTrainRun(trainRunId, dateIso, EmployeeRole.Conductor);
        }

        static Employee GetCrewMemberForTrainRun(int trainRunId, string dateIso, EmployeeRole role)
        {
            foreach (var c in CrewCirculationService.All)
            {
                if (c.status != CirculationStatus.Active) continue;
                if (c.role != role) continue;
                if (c.assignedEmployeeId <= 0) continue;

                // Check if this turnus runs on dateIso (calendarDays or specificDates)
                if (!RunsOnDate(c, dateIso)) continue;

                // Find matching duty
                foreach (var duty in c.duties)
                {
                    if (duty.kind == CrewDutyKind.Service && duty.referencedTrainRunId == trainRunId)
                    {
                        var emp = PersonnelService.GetById(c.assignedEmployeeId);
                        if (emp != null && emp.IsActive && emp.status != EmployeeStatus.Sick)
                            return emp;
                    }
                }
            }
            return null;
        }

        static bool RunsOnDate(CrewCirculation c, string dateIso)
        {
            if (c.specificDates != null && c.specificDates.Count > 0)
            {
                return c.specificDates.Contains(dateIso);
            }
            // Use calendar days
            try
            {
                var date = IsoTime.ParseDate(dateIso);
                int dayOfWeekMonday0 = ((int)date.DayOfWeek + 6) % 7;
                return c.calendarDays.Runs(dayOfWeekMonday0);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Zwraca wszystkie CrewCirculation przypisane do pracownika (do UI Details).
        /// Re-export <see cref="CrewCirculationService.GetByEmployee"/> dla wygody.
        /// </summary>
        public static List<CrewCirculation> GetEmployeeAssignments(int employeeId)
            => CrewCirculationService.GetByEmployee(employeeId);

        /// <summary>
        /// Sprawdz czy dany TrainRun wymaga konduktora (D16/D31): &gt;3 wagony LUB &gt;1 EMU LUB &gt;1 DMU.
        /// M8-13: uproszczenie — bez TrainRun.composition integracji, zwracamy false (bedzie pelne po M8-13 finalization).
        /// Wywolywane z M9 przy Spawn dla warunkowej walidacji konduktora.
        /// </summary>
        public static bool IsConductorRequired(int trainRunId)
        {
            // BUG-010 cz.1 (post-EA): real composition check via TimetableService.GetTrainRun.
            // Decyzja user'a 2026-05-07: w EA pracownicy NIE potrzebują kwalifikacji
            // (każdy maszynista jeździ EU07 i SM42, każdy konduktor obsługuje IC i lokalne).
            // Placeholder data + UI okno: <see cref="EmployeeQualifications"/> /
            // <see cref="EmployeeQualificationsUI"/>. Runtime omija check do post-EA milestone.
            return false;
        }

        public static void SetRequireCrewFlag(bool value)
        {
            RequireCrewForCirculation = value;
            OnFlagChanged?.Invoke(value);
            Log.Info($"[CrewAssignmentService] RequireCrewForCirculation = {value}");
        }

        // ═══ Debug ═══

        [ContextMenu("Debug: Toggle RequireCrewForCirculation")]
        public void DebugToggleFlag()
        {
            SetRequireCrewFlag(!RequireCrewForCirculation);
        }

        [ContextMenu("Debug: Report crew coverage")]
        public void DebugReport()
        {
            int active = 0, assigned = 0, unassigned = 0;
            foreach (var c in CrewCirculationService.All)
            {
                if (c.status != CirculationStatus.Active) continue;
                active++;
                if (c.assignedEmployeeId > 0) assigned++; else unassigned++;
            }
            Log.Info($"[CrewAssignmentService] Coverage: flag={RequireCrewForCirculation}, " +
                     $"turnuses active={active}, with employee={assigned}, without={unassigned}");
        }
    }
}
