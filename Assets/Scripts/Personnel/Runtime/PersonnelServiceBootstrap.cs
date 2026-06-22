using System;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-2: Bootstrap MonoBehaviour dla <see cref="PersonnelService"/>.
    /// Przeznaczony do wrzucenia jako GameObject na scene Depot lub DontDestroyOnLoad
    /// przez <see cref="EnsureExists"/> (analogicznie do <c>FleetMarketRefreshService</c>).
    ///
    /// Udostepnia ContextMenu do testow manualnych M8-2 deliverable:
    /// "mozna zatrudnic pracownika via context menu, zobaczyc go w liscie".
    ///
    /// W Awake wywoluje <see cref="PersonnelService.Initialize"/> — bezpieczne wielokrotnie.
    /// </summary>
    public class PersonnelServiceBootstrap : MonoBehaviour
    {
        public static PersonnelServiceBootstrap Instance { get; private set; }

        /// <summary>Lazy bootstrap — jeden GameObject z tym komponentem, DontDestroyOnLoad.</summary>
        public static PersonnelServiceBootstrap EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PersonnelServiceBootstrap");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PersonnelServiceBootstrap>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            PersonnelService.Initialize();

            // M8-3: ensure market + posting services
            CandidateMarketService.EnsureExists();

            // M8-5: daily scheduler (shift/fatigue/morale/payroll)
            PersonnelDailyScheduler.EnsureExists();

            // M8-6: notification toasts (L4 / retirement / crew vacancy)
            PersonnelNotificationToastUI.EnsureExists();

            // M8-10: 3D visuals runtime
            EmployeeWalkSimulator.EnsureExists();
            PersonnelDispatcher3D.EnsureExists();

            // M8-11: Traffic controller service + M9b priority hook
            TrafficControlService.EnsureExists();

            // M8-12: Workshop assignments + M7 hooks
            WorkshopAssignmentService.EnsureExists();

            // M8-13: Crew assignment (TrainRunSimulator.CrewCheckHook)
            CrewAssignmentService.EnsureExists();

            // M8-14: Cleaning + washing auto-assign
            CleaningService.EnsureExists();

            // M8-15: Office + R&D + Ticket clerks
            ResearchService.EnsureExists();
            TicketClerkService.EnsureExists();

            // M-TimetableUX F1.15: subscribe na TimetableWorkflowOrchestrator (cross-asmdef)
            RailwayManager.Personnel.Suggestions.CrewSwapSuggestionService.Bootstrap();

            Log.Info("[PersonnelServiceBootstrap] Bootstrapped");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ═══ ContextMenu debug — M8-2 deliverable ═══

        [ContextMenu("Debug: Hire Dummy Driver (3*)")]
        public void DebugHireDummyDriver() => DebugHireDummy(EmployeeRole.Driver, 3);

        [ContextMenu("Debug: Hire Dummy Conductor (2*)")]
        public void DebugHireDummyConductor() => DebugHireDummy(EmployeeRole.Conductor, 2);

        [ContextMenu("Debug: Hire Dummy Mechanic (4*)")]
        public void DebugHireDummyMechanic() => DebugHireDummy(EmployeeRole.Mechanic, 4);

        [ContextMenu("Debug: Hire Dummy Dispatcher (3*)")]
        public void DebugHireDummyDispatcher() => DebugHireDummy(EmployeeRole.Dispatcher, 3);

        [ContextMenu("Debug: Hire Dummy TrafficController (3*)")]
        public void DebugHireDummyTrafficController() => DebugHireDummy(EmployeeRole.TrafficController, 3);

        [ContextMenu("Debug: Hire 10 Random Employees (mixed roles)")]
        public void DebugHire10Random()
        {
            var random = new System.Random();
            var roles = (EmployeeRole[])Enum.GetValues(typeof(EmployeeRole));
            for (int i = 0; i < 10; i++)
            {
                var role = roles[random.Next(roles.Length)];
                int skill = 1 + random.Next(5);
                DebugHireDummy(role, skill, random);
            }
            Log.Info("[PersonnelServiceBootstrap] Hired 10 random employees.");
        }

        [ContextMenu("Debug: List All Employees")]
        public void DebugListAll()
        {
            if (PersonnelService.Employees.Count == 0)
            {
                Log.Info("[PersonnelServiceBootstrap] No employees hired yet.");
                return;
            }
            Log.Info($"[PersonnelServiceBootstrap] Employees ({PersonnelService.Employees.Count}):");
            foreach (var e in PersonnelService.Employees)
            {
                Log.Info($"  #{e.employeeId,-3} {e.DisplayFullName,-25} " +
                         $"{RoleDefinitions.GetDisplayNamePl(e.role),-18} {e.skill}* " +
                         $"[{e.status}] morale={e.currentMorale} fatigue={e.currentFatigue} " +
                         $"salary={e.currentSalaryGroszy / 100}zl shift={e.currentShift}");
            }
        }

        [ContextMenu("Debug: List Counts By Role")]
        public void DebugCountsByRole()
        {
            Log.Info($"[PersonnelServiceBootstrap] Active counts by role:");
            foreach (EmployeeRole role in Enum.GetValues(typeof(EmployeeRole)))
            {
                int c = PersonnelService.CountActiveByRole(role);
                if (c > 0)
                    Log.Info($"  {RoleDefinitions.GetDisplayNamePl(role)}: {c}");
            }
            Log.Info($"  TOTAL active: {PersonnelService.TotalActiveCount()}");
        }

        [ContextMenu("Debug: Fire random active employee")]
        public void DebugFireRandom()
        {
            var active = PersonnelService.GetActive();
            if (active.Count == 0)
            {
                Log.Warn("[PersonnelServiceBootstrap] No active employees to fire.");
                return;
            }
            var random = new System.Random();
            var victim = active[random.Next(active.Count)];
            PersonnelService.Fire(victim.employeeId);
        }

        [ContextMenu("Debug: Reset all (clear employees)")]
        public void DebugResetAll()
        {
            int count = PersonnelService.Employees.Count;
            PersonnelService.ResetAll();
            Log.Info($"[PersonnelServiceBootstrap] Reset done ({count} employees cleared).");
        }

        // ═══ M8-3: Recruitment UI + market ═══

        [ContextMenu("Show Personnel Main Panel (M8-4)")]
        public void ShowMainPanel()
        {
            PersonnelMainTabUI.EnsureExists().Show();
        }

        [ContextMenu("Show Recruitment UI")]
        public void ShowRecruitmentUI()
        {
            var ui = RecruitmentUI.EnsureExists();
            ui.Show();
        }

        [ContextMenu("Debug: Show Details for first employee")]
        public void DebugShowFirstEmployeeDetails()
        {
            if (PersonnelService.Employees.Count == 0)
            {
                Log.Warn("[PersonnelServiceBootstrap] No employees — hire someone first.");
                return;
            }
            EmployeeDetailsUI.EnsureExists().Show(PersonnelService.Employees[0].employeeId);
        }

        [ContextMenu("Debug: Show Schedule editor for first employee")]
        public void DebugShowFirstEmployeeSchedule()
        {
            if (PersonnelService.Employees.Count == 0)
            {
                Log.Warn("[PersonnelServiceBootstrap] No employees — hire someone first.");
                return;
            }
            EmployeeScheduleEditorUI.EnsureExists().Show(PersonnelService.Employees[0].employeeId);
        }

        // ═══ M8-6: Retirement + SickLeave debug ═══

        [ContextMenu("Debug: Force sick leave on first employee (3 days)")]
        public void DebugForceSickFirst()
        {
            if (PersonnelService.Employees.Count == 0)
            {
                Log.Warn("[PersonnelServiceBootstrap] No employees.");
                return;
            }
            SickLeaveService.DebugForceSick(PersonnelService.Employees[0].employeeId, 3);
        }

        [ContextMenu("Debug: Age first employee to 65 + force retirement roll")]
        public void DebugForceRetirementFirst()
        {
            if (PersonnelService.Employees.Count == 0)
            {
                Log.Warn("[PersonnelServiceBootstrap] No employees.");
                return;
            }
            var e = PersonnelService.Employees[0];
            e.age = 65;
            try
            {
                var now = IsoTime.ParseDate(RailwayManager.Core.GameState.CurrentDateIso);
                e.birthDateIso = now.AddYears(-65).ToString("yyyy-MM-dd");
            }
            catch { }
            Log.Info($"[PersonnelServiceBootstrap] Aged #{e.employeeId} to 65. Next daily tick will roll retirement.");
        }

        // ═══ M8-7: Dispatcher debug ═══

        [ContextMenu("Debug: Hire 2 dispatchers (3* each)")]
        public void DebugHire2Dispatchers()
        {
            DebugHireDummy(EmployeeRole.Dispatcher, 3);
            DebugHireDummy(EmployeeRole.Dispatcher, 3);
            Log.Info("[PersonnelServiceBootstrap] Hired 2 dispatchers — capacity = 60+60 = 120 workers");
        }

        [ContextMenu("Debug: Show dispatcher workload")]
        public void DebugShowWorkload()
        {
            var w = DispatcherService.GetWorkload();
            Log.Info($"[DispatcherService] Workload: status={w.status}, " +
                     $"capacity={w.totalCapacity}, headcount={w.currentHeadcount}, " +
                     $"ratio={w.CapacityRatio * 100f:F0}%, " +
                     $"dispatchers={w.activeDispatcherCount}, pending={w.pendingActionsCount}");
        }

        // ═══ M8-8: Turnusy pracownicze debug ═══

        [ContextMenu("Show Crew Circulation list (M8-8)")]
        public void ShowCrewCirculationList()
        {
            CrewCirculationListUI.EnsureExists().Show();
        }

        [ContextMenu("Show Auto-generator modal (M8-9)")]
        public void ShowAutoGeneratorModal()
        {
            CrewAutoGeneratorModal.EnsureExists().Show();
        }

        // ═══ M8-15: Office / Research / TicketClerk debug ═══

        [ContextMenu("Debug: Hire 3 office clerks (skills 2/3/4)")]
        public void DebugHire3OfficeClerks()
        {
            DebugHireDummy(EmployeeRole.Office, 2);
            DebugHireDummy(EmployeeRole.Office, 3);
            DebugHireDummy(EmployeeRole.Office, 4);
            float red = OfficeService.GetFixedCostReduction();
            Log.Info($"[PersonnelServiceBootstrap] Fixed cost reduction now: {red * 100:F1}%");
        }

        [ContextMenu("Debug: Hire 3 researchers (skills 3/4/5)")]
        public void DebugHire3Researchers()
        {
            DebugHireDummy(EmployeeRole.Research, 3);
            DebugHireDummy(EmployeeRole.Research, 4);
            DebugHireDummy(EmployeeRole.Research, 5);
            Log.Info("[PersonnelServiceBootstrap] Hired 3 researchers (skill 3/4/5)");
        }

        [ContextMenu("Debug: Start 'Lepsze przeglady' research")]
        public void DebugStartResearch1()
        {
            ResearchService.StartResearch("better_inspections");
        }

        [ContextMenu("Debug: Hire 3 ticket clerks")]
        public void DebugHire3TicketClerks()
        {
            DebugHireDummy(EmployeeRole.TicketClerk, 2);
            DebugHireDummy(EmployeeRole.TicketClerk, 3);
            DebugHireDummy(EmployeeRole.TicketClerk, 4);
            Log.Info("[PersonnelServiceBootstrap] Hired 3 ticket clerks");
        }

        [ContextMenu("Debug: Auto-assign clerks to sample stations")]
        public void DebugAutoAssignTicketClerks()
        {
            TicketClerkService.Instance?.DebugAutoAssignSampleStations();
        }

        [ContextMenu("Debug: Report Office + Research + TicketClerks state")]
        public void DebugOfficeResearchReport()
        {
            Log.Info($"[Office] reduction: {OfficeService.GetFixedCostReduction() * 100:F1}% " +
                     $"(max {OfficeService.MaxReduction * 100:F0}%), " +
                     $"market refresh cycle: {OfficeService.GetAdjustedMarketRefreshDays()}d");
            ResearchService.Instance?.DebugReport();
            TicketClerkService.Instance?.DebugReport();
        }

        // ═══ M8-14: Cleaning + washing debug ═══

        [ContextMenu("Debug: Hire 2 cleaners + 1 wash worker")]
        public void DebugHireCleaningCrew()
        {
            DebugHireDummy(EmployeeRole.Cleaner, 2);
            DebugHireDummy(EmployeeRole.Cleaner, 3);
            DebugHireDummy(EmployeeRole.WashBay, 3);
            Log.Info("[PersonnelServiceBootstrap] Hired 2 cleaners + 1 wash worker");
        }

        [ContextMenu("Debug: Toggle auto-cleaning")]
        public void DebugToggleAutoCleaning()
        {
            CleaningService.Instance?.DebugToggleCleaning();
        }

        [ContextMenu("Debug: Dirty all depot vehicles (cleanliness=30)")]
        public void DebugDirtyDepot()
        {
            CleaningService.Instance?.DebugDirtyAll();
        }

        [ContextMenu("Debug: Force cleaning tick")]
        public void DebugForceCleaning()
        {
            CleaningService.Instance?.DebugForceTick();
        }

        [ContextMenu("Debug: Report cleanliness")]
        public void DebugCleanlinessReport()
        {
            CleaningService.Instance?.DebugReport();
        }

        // ═══ M8-13: Crew assignment debug ═══

        [ContextMenu("Debug: Toggle RequireCrewForCirculation flag")]
        public void DebugToggleCrewRequired()
        {
            CrewAssignmentService.Instance?.DebugToggleFlag();
        }

        [ContextMenu("Debug: Report crew coverage")]
        public void DebugCrewReport()
        {
            CrewAssignmentService.Instance?.DebugReport();
        }

        // ═══ M8-12: Workshop assignment debug ═══

        [ContextMenu("Debug: Hire 3 mechanics (mixed skill)")]
        public void DebugHire3Mechanics()
        {
            DebugHireDummy(EmployeeRole.Mechanic, 2);
            DebugHireDummy(EmployeeRole.Mechanic, 4);
            DebugHireDummy(EmployeeRole.Mechanic, 3);
            Log.Info("[PersonnelServiceBootstrap] Hired 3 mechanics (skill 2/4/3)");
        }

        [ContextMenu("Debug: Auto-assign mechanic to first slot")]
        public void DebugAutoAssignMechanic()
        {
            WorkshopAssignmentService.Instance?.DebugAutoAssign();
        }

        [ContextMenu("Debug: Report workshop assignments")]
        public void DebugWorkshopReport()
        {
            WorkshopAssignmentService.Instance?.DebugReport();
        }

        // ═══ M8-11: Traffic controller debug ═══

        [ContextMenu("Debug: Hire 3 traffic controllers (3* each, shifts M/A/N)")]
        public void DebugHire3TrafficControllers()
        {
            var shifts = new[] { ShiftType.Morning, ShiftType.Afternoon, ShiftType.Night };
            foreach (var shift in shifts)
            {
                var random = new System.Random();
                var (first, last, _) = PolishNamesCatalog.GetRandomFullName(random);
                int age = 30 + random.Next(20);
                string birthDate;
                try { birthDate = IsoTime.ParseDate(RailwayManager.Core.GameState.CurrentDateIso).AddYears(-age).ToString("yyyy-MM-dd"); }
                catch { birthDate = "1990-01-01"; }

                var terms = new HireTerms
                {
                    firstName = first, lastName = last, age = age,
                    birthDateIso = birthDate, role = EmployeeRole.TrafficController, skill = 3,
                    negotiatedSalaryGroszy = RoleDefinitions.GetExpectedSalaryGroszy(EmployeeRole.TrafficController, 3),
                    initialShift = shift, initialCycle = WorkCyclePattern.Cycle5_2
                };
                PersonnelService.Hire(terms);
            }
            Log.Info("[PersonnelServiceBootstrap] Hired 3 traffic controllers (24/7 coverage D35)");
        }

        [ContextMenu("Debug: Traffic workload report")]
        public void DebugTrafficReport()
        {
            TrafficControlService.Instance?.DebugReport();
        }

        // ═══ M8-10: 3D cykl dnia debug ═══

        [ContextMenu("Debug: Force 3D resolve (spawn OnShift employees)")]
        public void Debug3DResolve()
        {
            PersonnelDispatcher3D.Instance?.ResolveAllForDay();
        }

        [ContextMenu("Debug: Report workflow state distribution (TD-025)")]
        public void DebugReportWorkflows()
        {
            PersonnelDispatcher3D.Instance?.DebugReportWorkflows();
        }

        [ContextMenu("Debug: Despawn all 3D visuals")]
        public void DebugDespawnAll3D()
        {
            EmployeeWalkSimulator.Instance?.DespawnAll();
        }

        [ContextMenu("Debug: Report 3D stats")]
        public void Debug3DStats()
        {
            var sim = EmployeeWalkSimulator.Instance;
            if (sim == null) { Log.Warn("[Bootstrap] WalkSimulator not bootstrapped."); return; }
            Log.Info($"[Bootstrap] 3D visuals: {sim.VisualCount}, queued tasks: {sim.QueuedTasks}, " +
                     $"gate position: {DepotGateMarker.GetPosition()}");
        }

        [ContextMenu("Debug: Auto-gen from fake timetable (commit)")]
        public void DebugAutoGenCommit()
        {
            var settings = new CrewAutoGenSettings { namePrefix = "AG" };
            var inputs = CrewCirculationAutoGenerator.DebugCreateSampleTimetable(RailwayManager.Core.GameState.CurrentDateIso);
            var preview = CrewCirculationAutoGenerator.Generate(inputs, settings);
            int committed = CrewCirculationAutoGenerator.Commit(preview);
            Log.Info($"[PersonnelServiceBootstrap] Auto-gen committed {committed} turnuses");
        }

        [ContextMenu("Debug: Create sample driver turnus")]
        public void DebugCreateSampleDriverTurnus()
        {
            var c = CrewCirculationService.Create("Sample Driver M-KR-01", EmployeeRole.Driver);
            if (c == null) return;

            CrewCirculationService.AddDuty(c.crewCirculationId, new CrewDuty
            {
                kind = CrewDutyKind.Service,
                dayOffset = 0,
                startTimeIso = "05:30:00", endTimeIso = "09:30:00",
                startStationName = "Krakow Glowny", endStationName = "Warszawa Zachodnia",
                referencedTrainRunId = -1
            });
            CrewCirculationService.AddDuty(c.crewCirculationId, new CrewDuty
            {
                kind = CrewDutyKind.Break,
                dayOffset = 0,
                startTimeIso = "09:30:00", endTimeIso = "11:00:00",
                startStationName = "Warszawa Zachodnia", endStationName = "Warszawa Zachodnia"
            });
            CrewCirculationService.AddDuty(c.crewCirculationId, new CrewDuty
            {
                kind = CrewDutyKind.Service,
                dayOffset = 0,
                startTimeIso = "11:00:00", endTimeIso = "15:00:00",
                startStationName = "Warszawa Zachodnia", endStationName = "Olsztyn Glowny",
                referencedTrainRunId = -1
            });

            // Assign first Driver if available
            foreach (var e in PersonnelService.Employees)
            {
                if (e.role == EmployeeRole.Driver && e.IsActive)
                {
                    CrewCirculationService.AssignEmployee(c.crewCirculationId, e.employeeId);
                    break;
                }
            }

            Log.Info($"[PersonnelServiceBootstrap] Sample turnus #{c.crewCirculationId} created with 3 duties");
        }

        [ContextMenu("Debug: Trigger auto-assign for first sick employee")]
        public void DebugAutoAssignFirst()
        {
            Employee sick = null;
            foreach (var e in PersonnelService.Employees)
            {
                if (e.status == EmployeeStatus.Sick) { sick = e; break; }
            }
            if (sick == null)
            {
                Log.Warn("[PersonnelServiceBootstrap] No sick employee — run 'Force sick leave' first");
                return;
            }
            var vacancy = new CrewVacancyData
            {
                employeeId = sick.employeeId,
                role = sick.role,
                affectedDateIso = RailwayManager.Core.GameState.CurrentDateIso,
                reason = CrewVacancyReason.SickLeave
            };
            var result = DispatcherService.TryAutoAssignReplacement(vacancy);
            Log.Info($"[PersonnelServiceBootstrap] Dispatch result: {result}");
        }

        [ContextMenu("Debug: Force immediate retirement on first employee")]
        public void DebugForceRetireNow()
        {
            if (PersonnelService.Employees.Count == 0)
            {
                Log.Warn("[PersonnelServiceBootstrap] No employees.");
                return;
            }
            var e = PersonnelService.Employees[0];
            e.age = 70;
            try
            {
                var now = IsoTime.ParseDate(RailwayManager.Core.GameState.CurrentDateIso);
                e.birthDateIso = now.AddYears(-70).ToString("yyyy-MM-dd");
            }
            catch { }
            // Trigger next tick immediately
            PersonnelDailyScheduler.Instance?.DebugForceTick();
        }

        [ContextMenu("Debug: Force market refresh now")]
        public void DebugForceMarketRefresh()
        {
            var mkt = CandidateMarketService.Instance ?? CandidateMarketService.EnsureExists();
            mkt.DebugForceRefresh();
        }

        [ContextMenu("Debug: List candidates on market")]
        public void DebugListCandidates()
        {
            var mkt = CandidateMarketService.Instance ?? CandidateMarketService.EnsureExists();
            mkt.DebugList();
        }

        [ContextMenu("Debug: Create Driver 3* job posting")]
        public void DebugCreatePostingDriver3()
        {
            JobPostingService.CreatePosting(EmployeeRole.Driver, 3);
        }

        [ContextMenu("Debug: Reset market + postings")]
        public void DebugResetMarket()
        {
            CandidateMarketService.ResetAll();
            JobPostingService.ResetAll();
            var mkt = CandidateMarketService.Instance ?? CandidateMarketService.EnsureExists();
            mkt.DebugRegenerateInitial();
            Log.Info("[PersonnelServiceBootstrap] Market + postings reset, initial pool regenerated.");
        }

        // ═══ Internal helper ═══

        void DebugHireDummy(EmployeeRole role, int skill, System.Random random = null)
        {
            random ??= new System.Random();
            var (first, last, isMale) = PolishNamesCatalog.GetRandomFullName(random);
            int age = 25 + random.Next(35); // 25-59

            string birthDate;
            try
            {
                birthDate = IsoTime.ParseDate(GameState.CurrentDateIso)
                    .AddYears(-age).ToString("yyyy-MM-dd");
            }
            catch
            {
                birthDate = "1990-01-01"; // safe fallback
            }

            var terms = new HireTerms
            {
                firstName = first,
                lastName = last,
                age = age,
                birthDateIso = birthDate,
                role = role,
                skill = skill,
                negotiatedSalaryGroszy = RoleDefinitions.GetExpectedSalaryGroszy(role, skill),
                initialShift = ShiftType.Morning,
                initialCycle = WorkCyclePattern.Cycle5_2
            };
            PersonnelService.Hire(terms);
        }
    }
}
