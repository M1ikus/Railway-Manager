using System;
using System.Collections.Generic;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1: Glowny byt pracownika. Pelne runtime state + perzystowalne dane.
    ///
    /// Stany przejscia:
    /// - Zatrudnienie: hire() → status=Available, morale=70, fatigue=0
    /// - Shift start: status=OnShift, fatigue rosnie wg RoleDefinitions.GetFatigueRatePerHour
    /// - Shift end: status=Resting, fatigue regen w dzien wolny
    /// - L4 roll: status=Sick, sickUntilDateIso ustawiony
    /// - Emerytura: status=Retired po 30-dniowym wypowiedzeniu
    /// - Zwolnienie: status=Fired (pozostaje w historii, dla save/load consistency)
    ///
    /// Wzory:
    /// - Pensja: baseSalary × (0.7 + 0.15×skill) (D2)
    /// - Performance: baseTime / (0.5 + skill/5) (D2)
    /// - Error rate: baseErrorRate × (1 / (0.5 + skill/5))
    /// </summary>
    [Serializable]
    public class Employee
    {
        // ── Identyfikacja ─────────────────────────────

        public int employeeId;
        public string firstName;
        public string lastName;
        /// <summary>Wiek w latach. Recompute z birthDateIso w ticku co dzien.</summary>
        public int age;
        /// <summary>Data urodzenia ISO (yyyy-MM-dd). Potrzebne do emerytur (D13).</summary>
        public string birthDateIso;
        /// <summary>Data zatrudnienia ISO. Potrzebne do odpraw (staz) i statystyk.</summary>
        public string hireDateIso;

        // ── Rola + skill ──────────────────────────────

        public EmployeeRole role;
        /// <summary>Skill 1-5 gwiazdek (D2). Wplyw na czas/error/pensje.</summary>
        public int skill = 1;

        // ── Stan dzienny ──────────────────────────────

        public EmployeeStatus status = EmployeeStatus.Available;
        /// <summary>Biezaca zmiana (moze byc nadpisana przez ScheduleOverride.ShiftSwap).</summary>
        public ShiftType currentShift = ShiftType.Morning;

        /// <summary>
        /// Morale 0-100, start 70 (D3). Wplyw: wydajnosc, szanse odejscia, strajki.
        ///
        /// BUG-060 v2: backing field jest w <see cref="moraleBreakdown"/> (per-source bucketing).
        /// `currentMorale` jest publiczny dla legacy compat (Read API), ale **set** powinien
        /// być przez `moraleBreakdown.ApplyDeltaToSalary` / `ApplyDeltaToRoom` (external events)
        /// lub `FatigueMoraleTickService` (daily recompute).
        ///
        /// Direct `e.currentMorale = X` lub `e.currentMorale +=/-= X` są legacy patterns —
        /// **NIE używać w nowym kodzie**. Save/load v4+ używa moraleBreakdown jako primary.
        /// </summary>
        public int currentMorale = 70;

        /// <summary>
        /// BUG-060 v2: per-source morale buckets (4 × cap = 100 total).
        /// Zostaje null dla legacy save (v3); migration w PersonnelSavable Deserialize tworzy
        /// z legacy `currentMorale`. Nowi pracownicy (Hire) dostają default breakdown.
        /// </summary>
        public MoraleBreakdown moraleBreakdown;

        /// <summary>Fatigue 0-100, regen w dni wolne (D3). &gt;80 = penalty wydajnosci.</summary>
        public int currentFatigue = 0;

        /// <summary>Data do ktorej jest na L4 (null/empty = Healthy).</summary>
        public string sickUntilDateIso;

        /// <summary>
        /// MM-D11/D20: czas (game time seconds) do którego pracownik jest w stanie
        /// <see cref="EmployeeStatus.Onboarding"/>. Po przekroczeniu, ShiftManager
        /// (lub PersonnelDailyScheduler) przesuwa do <see cref="EmployeeStatus.OnShift"/>.
        /// 0 = brak aktywnego onboarding'u. Default 0 dla nowych pracowników.
        /// </summary>
        public long onboardingFinishGameTime = 0L;

        /// <summary>
        /// TD-025: aktualny stan 3D workflow w depot (ortogonalny do <see cref="status"/>).
        /// NOT persisted — rebuild on load przez <see cref="PersonnelDispatcher3D"/>
        /// w pierwszym tick'u (default <see cref="EmployeeWorkflowState.OffShift"/>).
        ///
        /// <see cref="Newtonsoft.Json.JsonIgnoreAttribute"/> bo Newtonsoft ignoruje
        /// <c>[System.NonSerialized]</c> — to attribute działa tylko z BinaryFormatter.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public EmployeeWorkflowState workflowState = EmployeeWorkflowState.OffShift;

        /// <summary>
        /// TD-025: game time (seconds) kiedy bieżący <see cref="workflowState"/> się
        /// kończy. 0 = bez timera. Używane w ReportingToDispatcher i WorkingMobile.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public long workflowStateFinishGameTime = 0L;

        /// <summary>
        /// TD-025: opcjonalny ID celu workflow (vehicleId / slotId / trainRunId / jobId
        /// w zależności od kontekstu). -1 = brak.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public int workflowTargetId = -1;

        // ── TD-034: czynności osobiste (transient, [JsonIgnore], rebuild on load) ──

        /// <summary>TD-034: czy pracownik jest przebrany w ubranie robocze (po wizycie przy szafce).
        /// Dotyczy tylko ról operacyjnych (Mechanic/Driver/Conductor/Cleaner/WashBay) —
        /// patrz <c>ScheduledNeedProvider.RoleNeedsWorkClothes</c>. Transient: po load domyślnie
        /// false → przebierze się ponownie na początku zmiany.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool wearingWorkClothes = false;

        /// <summary>TD-034: absolutny game-time (s) ostatniej wizyty w łazience — anty-re-trigger harmonogramu.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public long lastBathroomGameTime = 0L;

        /// <summary>TD-034: absolutny game-time (s) ostatniej przerwy — anty-re-trigger harmonogramu.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public long lastBreakGameTime = 0L;

        /// <summary>TD-034: rodzaj trwającej czynności osobistej jako int (= (int)PersonalActivityKind),
        /// -1 = brak. Pozwala odróżnić LockerIn/LockerOut/Bathroom/Break w kontynuacji workflow.
        /// Int (nie enum) by Data nie zależało od namespace Workflows. Transient.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public int activeActivityKind = -1;

        // ── Wynagrodzenie ─────────────────────────────

        /// <summary>Biezaca pensja miesieczna BRUTTO PRACOWNIKA w groszach. Moze byc podwyzszona przez modal.
        /// Total cost firmy = currentSalaryGroszy × <see cref="PayrollConstants.GetEmployerCostMultiplier"/>(countryCode).</summary>
        public int currentSalaryGroszy;

        /// <summary>Kraj pracownika (PL/DE/CZ/SK). Determinuje skladki pracodawcy + tradycje placowe.
        /// W EA: zawsze "PL". Post-EA DLC: dziedziczone z kraju depotu zatrudnienia.</summary>
        public string countryCode = PayrollConstants.DefaultCountryEA;

        /// <summary>Data ostatniej wyplaty (pierwszy dzien miesiaca).</summary>
        public string lastPaidDateIso;

        /// <summary>Ile miesiecy z rzedu nie otrzymal pensji (brak srodkow). Po 3 → odejscie.</summary>
        public int missedPaymentsCount;

        // ── Urlopy ────────────────────────────────────

        /// <summary>Dni urlopu pozostale w biezacym roku (max 26, rollover 10 na nastepny rok, D13).</summary>
        public int vacationDaysRemaining = 26;
        /// <summary>Dni urlopu wykorzystane w tym roku (do rollover calculation).</summary>
        public int vacationDaysUsedThisYear;

        // ── Przypisania ───────────────────────────────

        /// <summary>ID obiegu pracowniczego dzisiaj (-1 = brak). Wiele dni = wiele values historycznych, tu tylko dzis.</summary>
        public int assignedCrewCirculationIdToday = -1;

        /// <summary>Stacja przypisana (tylko TicketClerk). -1 = brak.</summary>
        public int assignedStationId = -1;

        /// <summary>
        /// M-TimetableUX F1.13a polish: home depot station node ID (path graph). Per-employee
        /// dla Driver/Conductor — używany przez CrewSwapSuggestionService.CheckDirectionTowardHomeDepot
        /// żeby sprawdzić czy crew swap odpośnie drużynę bliżej domu (avoid hotel night).
        /// Default -1 = no specific home (legacy behavior, EA-mode permissive — wszystkie kierunki OK).
        /// Post-EA: assignment przy Hire (gracz wybiera home depot z dropdown w RecruitmentUI).
        /// </summary>
        public int homeStationNodeId = -1;

        /// <summary>Sloty warsztatowe przypisane (tylko Mechanic). Moze byc wielokrotne.</summary>
        public List<int> assignedWorkshopSlotIds = new();

        /// <summary>MF-10: ID przypisanej instancji furniture (PlacedFurnitureItem.instanceId).
        /// -1 = brak (np. role nie wymagajace biurka, lub brak wolnego). Auto-assign przy
        /// OnHire (FurnitureAssignmentService.AssignBestFurniture). FIFO reassign przy OnFire/OnRetired.</summary>
        public int assignedFurnitureId = -1;

        // ── Emerytura (M8-6, D13) ─────────────────────

        /// <summary>Data ogloszenia emerytury (ISO) — 30 dni przed faktycznym odejsciem. Empty = nie ogloszono.</summary>
        public string retirementAnnouncedDateIso;

        /// <summary>Data faktycznego odejscia na emeryture (ISO) = announced + 30 dni. Empty = nie ogloszono.</summary>
        public string retirementEndDateIso;

        // ── Szkolenia (STUB — post-EA, D11) ───────────

        /// <summary>Experience points dla awansu przez doswiadczenie (POST-EA). Stub = 0.</summary>
        public int skillXp;

        /// <summary>Czy aktualnie na szkoleniu (POST-EA). Stub = false.</summary>
        public bool isOnTraining;

        // ── Helpery ───────────────────────────────────

        /// <summary>Pelne imie + nazwisko dla UI.</summary>
        public string DisplayFullName => $"{firstName} {lastName}";

        /// <summary>Krotkie imie + inicjal nazwiska dla floating labels w 3D ("Jan K.").</summary>
        public string DisplayShortName
        {
            get
            {
                if (string.IsNullOrEmpty(lastName)) return firstName ?? "";
                return $"{firstName} {lastName[0]}.";
            }
        }

        /// <summary>
        /// Czy moze byc przypisany do pracy (Available, OnShift, Resting, Onboarding).
        /// MM-6: Onboarding wliczone — pracownik w trakcie odprawy jest realnym headcountem
        /// (płaci payroll, zajmuje cap, niedługo zacznie pracować).
        /// </summary>
        public bool IsActive =>
            status == EmployeeStatus.Available ||
            status == EmployeeStatus.OnShift ||
            status == EmployeeStatus.Resting ||
            status == EmployeeStatus.Onboarding;

        /// <summary>Czy pracownik moze miec CrewCirculation (Driver/Conductor).</summary>
        public bool CanHaveCrewCirculation =>
            RoleDefinitions.CanHaveCrewCirculation(role);
    }
}
