# Personnel — przewodnik

> Dokumentacja modułu `Assets/Scripts/Personnel/`.

## Asmdef + warstwa

**`RailwayManager.Personnel`** (rootNamespace: `RailwayManager.Personnel`),
refs: `Core, Depot, Fleet, Timetable, SharedUI` (+ TMP + InputSystem).

Drugi największy moduł po Timetable. Pełen spec M8 — 10 ról
(maszynista/konduktor/mechanik/sprzątacz/myjnia/biurowy/R&D/kasjer/dyspozytor/dyżurny),
obiegi pracownicze (turnusy), skille 1-5★, morale/fatigue, sick leave, retirement,
3D life loop w zajezdni.

**Konsumuje Timetable** dla cross-system glue (hook'i do `CrewCheckHook`,
`WorkshopManager.SlotSpeedMultiplierHook`, `DepotMovementSimulator.PriorityProvider`).
Inwersja: Depot/Timetable nie referują Personnel — Personnel subskrybuje ich eventy
i instaluje hook'i imperatywnie w bootstrap (`PersonnelServiceBootstrap`).

## Sub-systemy

| Sub-system | Pliki | Po co |
|---|---|---|
| **Data** | `Employee`, `EmployeeSchedule`, `ScheduleOverride`, `CrewCirculation`, `CrewDuty`, `EmployeeCandidate`, `Training`, `StationAssignment`, `HotelBooking`, `DispatcherWorkload`, `TrafficControllerWorkload`, `HireTerms`, `JobPosting`, `DispatcherTypes`, `AutoGeneratorTypes`, `EmployeeQualifications`, `ResearchPath`, `ResearchUnlocks`, `PersonnelEnums` (EmployeeStatus + EmployeeRole + …), `MoraleBreakdown`, `EmployeeWorkflowState` (TD-025 enum 10 stanów, `[JsonIgnore]`) | DTO. `CrewDuty.IsImminent` używane przez DriverConductorWorkflow (M8 LifeLoop 2026-05-11). `Employee.workflowState` rebuild on load. |
| **Catalogs** | `PolishNamesCatalog` (imię+nazwisko PL), `CandidateFluffCatalog` (biografie), `ResearchPathCatalog`, `RoleDefinitions` (per-role caps + salary + skills), `PayrollConstants`, `PersonnelBalanceConstants` (M6.5 + TD-025 `CrewReportLeadMinutes`, `WorkflowTickIntervalSec`, `MeldunekDurationSec`, `HotelCostStandardPerNight`) | Static loaders + balance constants. |
| **Core services** | `PersonnelService` (singleton static — Employees/Schedules/Qualifications), `PersonnelDailyScheduler` (per dzień: kto OnShift/Resting/Sick), `ShiftManager`, `FatigueMoraleTickService`, `SickLeaveService`, `RetirementService`, `PersonnelMarketGenerator`, `CandidateMarketService`, `JobPostingService`, `PayrollService`, `PersonnelServiceBootstrap` (instaluje hook'i w bootstrap) | Lifecycle pracownika: hire → schedule → daily ticks → events. Wszystko subskrybuje `GameState.OnDayEnded`. |
| **Załoga (M5×M8)** | `CrewAssignmentService` (instaluje `TrainRunSimulator.CrewCheckHook`), `CrewCirculationService`, `CrewCirculationAutoGenerator`, `CrewCirculationValidator`, `HotelBookingService` | Obiegi pracownicze (turnusy) niezależne od obiegów taboru, multi-day z hotelami. |
| **Warsztat (M7×M8)** | `WorkshopAssignmentService` (instaluje `WorkshopManager.SlotSpeedMultiplierHook` + `BreakdownService.SelfRepairBonusHook`) | Mechanic skill → szybszy inspection slot + bonus self-repair. |
| **Pozostałe role services** | `DispatcherService`, `TrafficControlService` (instaluje `DepotMovementSimulator.PriorityProvider`), `OfficeService`, `ResearchService`, `TicketClerkService`, `CleaningService` (M-Modernization MM-9: per-pojazd 60s walk, fallback instant gdy brak Cleaner OnShift) | Per-role lifecycle + UI hooks. |
| **3D life loop (TD-025 ✅ 2026-05-11)** | `PersonnelDispatcher3D` (orchestrator, FixedUpdate sub-sampled co 1s), `EmployeeWalkPathfinder` (PathGraph wrapper + cache singleton), `EmployeeWalkSimulator`, `EmployeeWalkTask` (z `nextTask` chain), `EmployeeVisual` (placeholder kapsuła + sway+bob animacja, polyline interpolation), `Workflows/IEmployeeWorkflow` + 5 implementacji (DriverConductor/Mechanic/Cleaner/WashBay/StaticDesk), `Workflows/WorkflowLocations`, `PersonnelLifeLoopSmokeTest` (6 ContextMenu) | Cykl dnia pracownika 3D w zajezdni. Skala ~200 postaci, animacja placeholder (M-Models swap). |
| **M-Modernization integration** | `DispatchActionService` (4 typy akcji, manual fallback), `MoraleBonusService` (Supervisor/Social/Bathroom), `OnboardingTickService` (`EmployeeStatus.Onboarding`), `MM18MovementHooksInstaller`, `DepotGateMarker`, `RoleCaps` | MM-1..MM-14 integration. |
| **Furniture integration** | `FurnitureAssignmentService` (mapping rola→ObjectFunction: Office/Research/Dispatcher→WorkstationOffice, TrafficController→WorkstationTraffic, Driver/Conductor/Mechanic/Cleaner/WashBay→null) + FIFO reassign przy OnFire/OnRetired, `FurnitureMilestoneSmokeTest` | M-Furniture bridge. |
| **Suggestions** | `Suggestions/CrewSwapSuggestionService` | M-TimetableUX cross-asmdef event hook (Personnel CrewSwap konsumowany przez TimetableWorkflowOrchestrator). |
| **UI** | `PersonnelMainTabUI` (root) + 8 partials (MyStaff/Recruitment/Dispatch/Traffic/Turnuses/Workshops/Office/Kasy), `RecruitmentUI`, `EmployeeDetailsUI`, `EmployeeScheduleEditorUI`, `EmployeeQualificationsUI`, `CrewCirculationListUI`, `CrewCirculationEditorUI`, `HotelBookingModal`, `CrewAutoGeneratorModal`, `PersonnelNotificationToastUI`, `UiHelper` | 9 zakładek w `PersonnelMainTabUI`. "Czyni: {stan}" kolumna w MyStaff (TD-025 debug UI). |
| **Smoke tests** | `PersonnelLifeLoopSmokeTest`, `FurnitureMilestoneSmokeTest`, `MModernizationSmokeTest`, `OfficeMM5SmokeTest` | ContextMenu-driven, NIE Unity Test Framework. |

## Cross-system glue

- **Hooks installed w `PersonnelServiceBootstrap`** (imperatywnie, nie via DI):
  - `CrewAssignmentService` → `TrainRunSimulator.CrewCheckHook`
  - `WorkshopAssignmentService` → `WorkshopManager.SlotSpeedMultiplierHook` + `BreakdownService.SelfRepairBonusHook`
  - `TrafficControlService` → `DepotMovementSimulator.PriorityProvider`
- **`FurnitureAssignmentService`** subskrybuje `FurniturePlacer.OnPlacementStateChanged` (Depot).
- **`EmployeeWalkPathfinder`** deleguje do Depot **`DepotNavService.BuildRoute`** (TD-033 2026-06-09: visibility-graph nav + omijanie przeszkód + wejście przez drzwi + train-yield); malowany `PathGraph` = opcjonalna preferencja. Legacy BFS+straight-line tylko gdy brak nav-service (scena bez bootstrapu/test). `EmployeeVisual.Tick` robi train-yield (occupancy TD-031), `EmployeeWalkSimulator.FixedUpdate` soft separation NPC (`NavSeparation`).
- **TD-034 czynności osobiste (2026-06-09):** `Workflows/PersonalActivities` (silnik walk→reserve→timer→release→resume) + `ScheduledNeedProvider`/`PersonalNeedSchedule` (deterministyczny harmonogram, seam `IPersonalNeedProvider` pod przyszłe potrzeby) + `MeldunekFlow` (meldunek urealniony: obecność dyspozytora + kolejka). Rezerwacja mebli przez Depot **`FurnitureOccupancyService`** (sloty per-mebel). Stany activity (`GoingToPersonal/ChangingClothes/UsingBathroom/OnBreak/QueuingForDispatcher`) + pola (`wearingWorkClothes/lastBathroom/lastBreak/activeActivityKind`) = **transient `[JsonIgnore]`** (rebuild on load). Przebranie przy szafce role-gated (Mechanic/Driver/Conductor/Cleaner/WashBay), tint w `EmployeeVisual.SetWorkClothes`. Workflowy wołają `PersonalActivities.Tick` + `MeldunekFlow.Tick` na górze `Tick` i `Begin`/`MaybeStartMidShift` z bezpiecznych stanów.
- **`DriverConductorWorkflow`** subskrybuje `TrainRunSimulator.OnRunSpawned/OnRunDespawned` (Timetable) — hidden capsule embed w pojezdzie.
- **`PayrollService`** subskrybuje `GameState.OnDayEnded` → wypłaty wpływają na `EconomyManager` (Timetable).
- **`CrewSwapSuggestionService`** publikuje sugestie do `TimetableWorkflowOrchestrator` (Timetable).

## Gotchas

- **`Employee.workflowState [JsonIgnore]`** — NIE persistowane. Rebuild on load z `Employee.status` przez `PersonnelDispatcher3D`. Pomyłka = stale state po save/load.
- **TD-025 Cleaning fallback gdy brak Cleaner** — `CleaningService.ApplyDailyTick` zmienione: gdy jest `Cleaner` OnShift, daily tick nie czyści. `CleanerWorkflow` per-pojazd 60s. Fallback dla graczy bez cleanera = stary instant clean (zachowane gameplay).
- **Universal meldunek u dyspozytora 8s** — wszyscy oprócz `Cleaner`/`TicketClerk`/`Dispatcher` (self) idą najpierw do biurka dyspozytora. `MeldunekDurationSec` constant.
- **`EmployeeStatus` vs `EmployeeWorkflowState`** — to **dwa różne enumy** ortogonalne. Status = lifecycle (Available/OnShift/Resting/Sick/Retired/Fired/Onboarding). WorkflowState = aktualne miejsce w cyklu dnia (Idle/Walking/AtDispatcher/AwaitingDuty/WorkingAtStation/…).
- **Lead time 30 min stały** (`CrewReportLeadMinutes`). Future hook na morale — pracownik zmotywowany przychodzi wcześniej (post-EA).
- **Driver/Conductor embed w pojezdzie**: kapsuła hidden gdy `OnRunSpawned`, reappear gdy `OnRunDespawned`. NIE despawn'uj kapsuły — Visual lookup via `EmployeeVisualsContainer.GetChildById`.
- **PathGraph fallback "straight-line z końca chodnika"** gdy brak grafu — warning log raz per employee, akceptowalne dla MVP EA.
- **Idle pracownik w `RoomType.Social` random cell deterministic seed** (`employeeId`) — gdy nie ma Social room, zostaje na 1 cell w preferred room.
- **Anti-skrót cleaning fallback (post-MM-18 lesson)** — CleaningService.fallback został świadomie zachowany jako gameplay design dla niezatrudniających, nie hack.

## Powiązane docs

- `docs/design/m8-personnel.md` — pełny spec M8 (10 ról, turnusy, skille)
- `docs/TECH_DEBT.md` — TD-025 (DONE 2026-05-11 full scope)
- `docs/design/balance-constants.md` — `PersonnelBalanceConstants` + `PayrollConstants`
