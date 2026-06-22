using System;
using System.Collections.Generic;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-2: Centralny runtime state personelu — zatrudnieni pracownicy + ich indywidualne
    /// harmonogramy. Statyczny singleton w stylu <c>FleetService</c>, przezywa sceny.
    ///
    /// Zycie pracownika:
    /// - Hire: status=Available, morale=70, fatigue=0, schedule=default (5+2 Morning)
    /// - Sluzba dniowa: status=OnShift (wg ShiftManager w M8-5)
    /// - Dzien wolny: status=Resting
    /// - L4: status=Sick + sickUntilDateIso (SickLeaveService w M8-6)
    /// - Emerytura: status=Retired po 30 dniach zapowiedzi (M8-6)
    /// - Zwolnienie: status=Fired (zachowywany w liscie dla save/load historii)
    ///
    /// Na tym etapie (M8-2): tylko Hire/Fire/Get. Brak ticks, brak events dniowych,
    /// brak integracji z ekonomia. To w kolejnych podetapach.
    /// </summary>
    public static class PersonnelService
    {
        // ═══ State ═══

        /// <summary>Wszyscy pracownicy firmy (historyczni Fired/Retired tez tu zostaja dla save/load).</summary>
        public static List<Employee> Employees { get; } = new();

        /// <summary>
        /// O(1) lookup po employeeId. Maintained przez Hire/Fire + RebuildIndexes (po save/load
        /// direct Add do Employees). Zawiera WSZYSTKICH (w tym Fired/Retired) bo GetById musi
        /// zwracać też zwolnionych (UI historii, severance calc, save/load).
        /// </summary>
        private static readonly Dictionary<int, Employee> _byId = new();

        /// <summary>
        /// Cache pracowników aktywnie widocznych w 3D depocie (status=OnShift AND
        /// rola spawnuje się w depocie). Hot path dla <c>PersonnelDispatcher3D.FixedUpdate</c>
        /// (tick co 1s) — zamiast scanować pełną listę Employees (która zawiera dziesiątki/setki
        /// Fired+Retired+Resting), iterujemy tylko tych co naprawdę robią.
        ///
        /// Maintained incremental w <see cref="NotifyStatusChanged"/> i <see cref="Fire"/> dla
        /// ad-hoc zmian. Mass-update z <see cref="ShiftManager"/> używa <see cref="RebuildOnShiftCache"/>
        /// (rebuild tańszy niż 200 eventów per daily tick).
        /// </summary>
        private static readonly HashSet<int> _onShiftIds = new();
        private static readonly List<Employee> _onShiftAgents = new();

        /// <summary>
        /// Pracownicy aktualnie OnShift których rola spawnuje agent 3D w depocie.
        /// Używane przez <c>PersonnelDispatcher3D.FixedUpdate</c> jako hot-path iteration.
        /// </summary>
        public static IReadOnlyList<Employee> OnShiftAgents => _onShiftAgents;

        /// <summary>Harmonogramy indywidualne keyed by employeeId. Tworzone przy Hire().</summary>
        public static Dictionary<int, EmployeeSchedule> Schedules { get; } = new();

        /// <summary>
        /// Kwalifikacje pracownika (post-EA gameplay; EA: placeholder, każdy ma wszystkie uprawnienia).
        /// Tworzone lazy w <see cref="GetQualifications"/> — domyślnie pusta lista per pracownik.
        /// </summary>
        public static Dictionary<int, EmployeeQualifications> Qualifications { get; } = new();

        private static int _nextEmployeeId = 1;
        public static bool IsInitialized { get; private set; }

        // ═══ Events ═══

        public static event Action<Employee> OnEmployeeHired;
        public static event Action<Employee> OnEmployeeFired;
        public static event Action<Employee> OnEmployeeStatusChanged;
        /// <summary>Generic — dowolna zmiana w liscie (hire/fire/skill/morale). Dla UI refresh.</summary>
        public static event Action OnEmployeesChanged;

        // ═══ Initialization ═══

        /// <summary>
        /// Reset state — wywolane przez bootstrap lub przy nowej grze (save/load load).
        /// Bezpieczne wielokrotne wywolanie (early-exit gdy IsInitialized).
        /// </summary>
        public static void Initialize()
        {
            if (IsInitialized) return;
            Employees.Clear();
            _byId.Clear();
            _onShiftIds.Clear();
            _onShiftAgents.Clear();
            Schedules.Clear();
            Qualifications.Clear(); // BUG-023: clear stale quals z poprzedniej sesji
            _nextEmployeeId = 1;
            IsInitialized = true;
            Log.Info("[PersonnelService] Initialized");
        }

        /// <summary>Reset twardy — dla nowej gry (load z save lub wipe dla testów).</summary>
        public static void ResetAll()
        {
            Employees.Clear();
            _byId.Clear();
            _onShiftIds.Clear();
            _onShiftAgents.Clear();
            Schedules.Clear();
            Qualifications.Clear(); // BUG-023: clear stale quals (employeeId reuse safe)
            _nextEmployeeId = 1;
            IsInitialized = true;
            OnEmployeesChanged?.Invoke();
            Log.Info("[PersonnelService] Reset all state");
        }

        /// <summary>
        /// Rebuild indeksów (_byId + _onShiftAgents) z aktualnej zawartości <see cref="Employees"/>.
        /// Wywoływane przez <c>PersonnelSavable.Deserialize</c> po direct Add do listy
        /// (save/load bypassuje <see cref="Hire"/> która normalnie utrzymuje indeksy).
        /// </summary>
        public static void RebuildIndexes()
        {
            _byId.Clear();
            _onShiftIds.Clear();
            _onShiftAgents.Clear();
            foreach (var e in Employees)
            {
                if (e == null) continue;
                _byId[e.employeeId] = e;
                if (IsOnShiftSpawnable(e))
                {
                    _onShiftIds.Add(e.employeeId);
                    _onShiftAgents.Add(e);
                }
            }
        }

        /// <summary>
        /// Rebuild tylko <see cref="OnShiftAgents"/> cache. Wywoływane po <c>ShiftManager.ApplyDailyTick</c>
        /// (mass update statusów per pracownik — tańsze rebuild niż 200 eventów per dzień).
        /// </summary>
        public static void RebuildOnShiftCache()
        {
            _onShiftIds.Clear();
            _onShiftAgents.Clear();
            foreach (var e in Employees)
            {
                if (e == null) continue;
                if (IsOnShiftSpawnable(e))
                {
                    _onShiftIds.Add(e.employeeId);
                    _onShiftAgents.Add(e);
                }
            }
        }

        private static bool IsOnShiftSpawnable(Employee e)
        {
            return e.status == EmployeeStatus.OnShift
                && RoleDefinitions.SpawnsAsAgentInDepot(e.role);
        }

        private static void TryAddOnShift(Employee e)
        {
            if (_onShiftIds.Add(e.employeeId)) _onShiftAgents.Add(e);
        }

        private static void TryRemoveOnShift(Employee e)
        {
            if (_onShiftIds.Remove(e.employeeId)) _onShiftAgents.Remove(e);
        }

        /// <summary>BUG-079: aktualny licznik employeeId dla save (żeby nie kolizjować po load).</summary>
        public static int GetNextEmployeeId() => _nextEmployeeId;

        /// <summary>
        /// BUG-079: restore _nextEmployeeId po load. Wywołane przez PersonnelSavable.Deserialize
        /// po direct Add do Employees. Wcześniej _nextEmployeeId zostawał 1 → nowy hire dostawał
        /// id=1 → kolizja z istniejącym pracownikiem.
        /// </summary>
        public static void RestoreNextEmployeeId(int nextEmployeeId)
        {
            _nextEmployeeId = nextEmployeeId > 0 ? nextEmployeeId : 1;
        }

        // ═══ Hire / Fire ═══

        /// <summary>
        /// Zatrudnia pracownika wg <see cref="HireTerms"/>. Tworzy domyslny harmonogram
        /// (cykl + shift z terms). Emituje <see cref="OnEmployeeHired"/>.
        ///
        /// <see cref="HireTerms.negotiatedSalaryGroszy"/>=0 → uzywa <see cref="RoleDefinitions.GetExpectedSalaryGroszy"/>.
        ///
        /// M8-2: bez integracji z ekonomia (hire bonus nie jest jeszcze wyplacany).
        /// Hook do M8-3 w <c>CandidateMarketService</c>.
        /// </summary>
        public static Employee Hire(HireTerms terms)
        {
            if (!IsInitialized) Initialize();
            if (terms == null) throw new ArgumentNullException(nameof(terms));

            // BUG-059: walidacja terms.firstName/lastName non-empty + skill clamp przed dalszymi
            // checkami (defensive — legacy save z corrupt data nie powinien crashować).
            if (string.IsNullOrEmpty(terms.firstName) || string.IsNullOrEmpty(terms.lastName))
            {
                Log.Warn("[PersonnelService] Hire: terms.firstName/lastName empty — rejected");
                return null;
            }
            int skill = Math.Clamp(terms.skill, 1, 5);

            // MM-5: walidacja cap headcount per rola (Office lvl, MM-6/MM-6b dorzuci
            // Dispatcher/TrafficController). Office lvl 0 (brak biura) → cap=0 → blokada.
            // Returns null żeby callers (RecruitmentUI, PersonnelServiceBootstrap.DebugHire*)
            // mogli wyświetlić tooltip i nie zerwać UI flow.
            int max = RoleCaps.GetMaxForRole(terms.role);
            if (max != int.MaxValue)
            {
                int current = RoleCaps.GetCurrentHeadcountForRole(terms.role);
                if (current >= max)
                {
                    string roleLabel = RoleDefinitions.GetDisplayNamePl(terms.role);
                    Log.Warn($"[PersonnelService] Hire {roleLabel} ({terms.firstName} {terms.lastName}) " +
                             $"odrzucony — cap {max} osiągnięty (current {current}). " +
                             "Awansuj odpowiedni pokój (Biuro/Dyspozytor/Nastawnia) aby zwiększyć limit.");
                    return null;
                }
            }
            int salary = terms.negotiatedSalaryGroszy > 0
                ? terms.negotiatedSalaryGroszy
                : RoleDefinitions.GetExpectedSalaryGroszy(terms.role, skill);

            var e = new Employee
            {
                employeeId = _nextEmployeeId++,
                firstName = terms.firstName,
                lastName = terms.lastName,
                age = terms.age,
                birthDateIso = terms.birthDateIso,
                hireDateIso = GameState.CurrentDateIso,
                role = terms.role,
                skill = skill,
                status = EmployeeStatus.Available,
                currentShift = terms.initialShift,
                currentMorale = PersonnelBalanceConstants.MoraleStartNewHire,
                // BUG-060 v2: per-source breakdown z legacy MoraleStartNewHire (70)
                moraleBreakdown = MoraleBreakdown.FromLegacyMorale(PersonnelBalanceConstants.MoraleStartNewHire),
                currentFatigue = 0,
                currentSalaryGroszy = salary,
                lastPaidDateIso = GameState.CurrentDateIso,
                missedPaymentsCount = 0,
                vacationDaysRemaining = PersonnelBalanceConstants.VacationDaysPerYear,
                vacationDaysUsedThisYear = 0,
                assignedCrewCirculationIdToday = -1,
                assignedStationId = -1,
                skillXp = 0,
                isOnTraining = false
            };
            Employees.Add(e);
            _byId[e.employeeId] = e;

            // Domyslny harmonogram
            var sched = new EmployeeSchedule
            {
                employeeId = e.employeeId,
                defaultShift = terms.initialShift,
                cycle = terms.initialCycle,
                cycleStartDayOffset = 0,
                overrides = new List<ScheduleOverride>()
            };
            Schedules[e.employeeId] = sched;

            OnEmployeeHired?.Invoke(e);
            OnEmployeesChanged?.Invoke();

            Log.Info($"[PersonnelService] Hired #{e.employeeId} {e.DisplayFullName} " +
                     $"({RoleDefinitions.GetDisplayNamePl(e.role)} {e.skill}*, " +
                     $"salary={salary / 100}zl, shift={terms.initialShift})");
            return e;
        }

        /// <summary>
        /// Zwalnia pracownika. Status=Fired, pozostaje w liscie dla historii.
        /// Nie usuwa ze <see cref="Schedules"/> (save/load consistency).
        ///
        /// M8-4: liczy i wyplaca odprawe (severance) wg stazu (<see cref="CalculateSeverancePay"/>).
        /// Dodatkowo: koledzy tej samej roli dostaja morale -2 (D5/§5.1 "kolega zwolniony").
        ///
        /// <paramref name="paySeverance"/>=false dla emerytur (osobna odprawa retirement = 3×) lub debug.
        /// </summary>
        public static bool Fire(int employeeId, bool paySeverance = true)
        {
            var e = GetById(employeeId);
            if (e == null)
            {
                Log.Warn($"[PersonnelService] Fire: employeeId={employeeId} not found");
                return false;
            }
            if (e.status == EmployeeStatus.Fired || e.status == EmployeeStatus.Retired)
            {
                Log.Warn($"[PersonnelService] Fire: employeeId={employeeId} already inactive ({e.status})");
                return false;
            }

            if (paySeverance)
            {
                int severance = CalculateSeverancePay(e);
                if (severance > 0)
                {
                    var econ = EconomyManager.Instance;
                    if (econ != null)
                        econ.AddCost(-1, severance, "Severance",
                            $"Severance for #{e.employeeId} {e.DisplayFullName}");
                    else
                        GameState.Money -= severance / 100;
                    Log.Info($"[PersonnelService] Severance paid: {severance / 100}zl");
                }

                // Collateral morale dla kolegow tej samej roli (D5/§5.1: -2 per kolega zwolniony, 3 dni)
                // M8-4: jednorazowe -2 (decay przez ShiftManager w M8-5)
                foreach (var other in Employees)
                {
                    if (other.role == e.role && other.employeeId != e.employeeId && other.IsActive)
                    {
                        // BUG-060 v2: colleague fired → rooms bucket (społeczny aspekt — pracownik
                        // smutny bo kolega odszedł). Jeśli rooms już 0, nic dalej (cap 0).
                        if (other.moraleBreakdown == null) other.moraleBreakdown = MoraleBreakdown.FromLegacyMorale(other.currentMorale);
                        other.moraleBreakdown.ApplyDeltaToRoom(PersonnelBalanceConstants.MoraleColleagueFired);
                        other.currentMorale = other.moraleBreakdown.Total;
                    }
                }
            }

            e.status = EmployeeStatus.Fired;
            TryRemoveOnShift(e);  // zwolniony nie jest już agentem 3D
            // BUG-026: usuń quals przy Fire — nie potrzebujemy ich dla Fired/Retired
            // (zostają tylko dla save/load historii w Employees list).
            Qualifications.Remove(employeeId);
            OnEmployeeFired?.Invoke(e);
            OnEmployeesChanged?.Invoke();
            Log.Info($"[PersonnelService] Fired #{e.employeeId} {e.DisplayFullName}");
            return true;
        }

        /// <summary>
        /// M8-4: Oblicza odprawe wg stazu w firmie (hireDateIso → CurrentDateIso w dniach).
        ///
        /// Wzor (D10, <see cref="PersonnelBalanceConstants.Severance*"/>):
        /// - &lt;1 miesiac (30d): 0
        /// - 1-6 miesiecy (30-180d): 1× pensja miesieczna
        /// - 6-24 miesiecy (180-730d): 2×
        /// - &gt;2 lata (730d+): 3×
        /// </summary>
        public static int CalculateSeverancePay(Employee e)
        {
            if (e == null || string.IsNullOrEmpty(e.hireDateIso)) return 0;

            if (!IsoTime.TryParseDate(e.hireDateIso, out var hire))
            {
                Log.Warn($"[PersonnelService] CalculateSeverancePay: invalid hireDateIso '{e.hireDateIso}' " +
                         $"for #{e.employeeId} {e.DisplayFullName} — severance=0");
                return 0;
            }
            if (!IsoTime.TryParseDate(GameState.CurrentDateIso, out var now))
            {
                Log.Warn($"[PersonnelService] CalculateSeverancePay: invalid GameState.CurrentDateIso " +
                         $"'{GameState.CurrentDateIso}' — severance=0");
                return 0;
            }

            double days = (now - hire).TotalDays;
            int monthsMult;
            if (days < 30) monthsMult = PersonnelBalanceConstants.SeveranceUnder1Month;
            else if (days < 180) monthsMult = PersonnelBalanceConstants.Severance1To6Months;
            else if (days < 730) monthsMult = PersonnelBalanceConstants.Severance6To24Months;
            else monthsMult = PersonnelBalanceConstants.SeveranceOver2Years;

            return e.currentSalaryGroszy * monthsMult;
        }

        /// <summary>M8-4: Staz w dniach (dla UI details + severance calculation).</summary>
        public static int GetTenureDays(Employee e)
        {
            if (e == null || string.IsNullOrEmpty(e.hireDateIso)) return 0;

            if (!IsoTime.TryParseDate(e.hireDateIso, out var hire))
            {
                Log.Warn($"[PersonnelService] GetTenureDays: invalid hireDateIso '{e.hireDateIso}' " +
                         $"for #{e.employeeId} {e.DisplayFullName} — tenure=0");
                return 0;
            }
            if (!IsoTime.TryParseDate(GameState.CurrentDateIso, out var now)) return 0;
            return Math.Max(0, (int)(now - hire).TotalDays);
        }

        /// <summary>M8-4: Przyznaj premie jednorazowa (+morale).</summary>
        public static bool GrantBonus(int employeeId, int amountGroszy, string reason = "Premia")
        {
            var e = GetById(employeeId);
            if (e == null) return false;
            if (amountGroszy <= 0) return false;

            var econ = EconomyManager.Instance;
            if (econ != null)
                econ.AddCost(-1, amountGroszy, "Personnel",
                    $"Bonus for #{e.employeeId} {e.DisplayFullName}: {reason}");
            else
                GameState.Money -= amountGroszy / 100;

            // BUG-050 fix: float division + round zamiast integer division × integer division.
            // Wcześniej `amountGroszy/100/1000` traciło reszty: 1990 zł → 1× zamiast 2× per-thousand.
            float bonusPln = amountGroszy / 100f;
            int thousandsRounded = (int)Math.Round(bonusPln / 1000.0);
            int moraleGain = Math.Min(
                PersonnelBalanceConstants.MoraleBonusMaxPerEvent,
                thousandsRounded * PersonnelBalanceConstants.MoraleBonusPerThousand);
            // BUG-060 v2: bonus → salary bucket (ekonomiczne wynagrodzenie)
            if (e.moraleBreakdown == null) e.moraleBreakdown = MoraleBreakdown.FromLegacyMorale(e.currentMorale);
            e.moraleBreakdown.ApplyDeltaToSalary(moraleGain);
            e.currentMorale = e.moraleBreakdown.Total;

            OnEmployeeStatusChanged?.Invoke(e);
            OnEmployeesChanged?.Invoke();
            Log.Info($"[PersonnelService] Bonus {amountGroszy / 100}zl to #{e.employeeId} " +
                     $"{e.DisplayFullName} (morale +{moraleGain})");
            return true;
        }

        /// <summary>M8-4: Podwyzka pensji (nowa pensja w groszach, monthly). Dodaje morale za +10%+ od obecnej.</summary>
        public static bool SetSalary(int employeeId, int newSalaryGroszy)
        {
            var e = GetById(employeeId);
            if (e == null) return false;
            if (newSalaryGroszy <= 0) return false;

            // BUG-058: górna granica salary — defense przeciw save corruption / int.MaxValue.
            // Cap = 5× expected salary dla danej roli i skill. Realistyczny jeśli gracz chce
            // płacić więcej niż rynek, ale < int.MaxValue/100 = 21.4M zł/mies (avoid budget wipe).
            int expectedForRole = RoleDefinitions.GetExpectedSalaryGroszy(e.role, e.skill);
            int salaryCap = expectedForRole * 5;
            if (newSalaryGroszy > salaryCap)
            {
                Log.Warn($"[PersonnelService] SetSalary employeeId={employeeId} {newSalaryGroszy} groszy " +
                         $"przekracza cap {salaryCap} (5× expected dla {e.role}/{e.skill}★) — clamped");
                newSalaryGroszy = salaryCap;
            }

            int oldSalary = e.currentSalaryGroszy;
            float changePct = (newSalaryGroszy - oldSalary) / (float)Math.Max(oldSalary, 1);
            e.currentSalaryGroszy = newSalaryGroszy;

            if (changePct >= 0.10f)
            {
                // BUG-060 v2: raise +10% morale → salary bucket
                if (e.moraleBreakdown == null) e.moraleBreakdown = MoraleBreakdown.FromLegacyMorale(e.currentMorale);
                e.moraleBreakdown.ApplyDeltaToSalary(10);
                e.currentMorale = e.moraleBreakdown.Total;
            }

            OnEmployeeStatusChanged?.Invoke(e);
            OnEmployeesChanged?.Invoke();
            Log.Info($"[PersonnelService] Salary change for #{e.employeeId} {e.DisplayFullName}: " +
                     $"{oldSalary / 100}zl -> {newSalaryGroszy / 100}zl ({changePct * 100f:+0.0;-0.0}%)");
            return true;
        }

        /// <summary>M8-4: Udziel urlopu — dodaje ScheduleOverride typu Vacation.</summary>
        public static bool GrantVacation(int employeeId, string dateIsoStart, string dateIsoEnd, int daysCount)
        {
            var e = GetById(employeeId);
            if (e == null) return false;
            if (daysCount <= 0) return false;
            if (e.vacationDaysRemaining < daysCount)
            {
                Log.Warn($"[PersonnelService] GrantVacation: #{employeeId} has only {e.vacationDaysRemaining} days left (requested {daysCount})");
                return false;
            }

            var sched = GetSchedule(employeeId);
            if (sched == null)
            {
                Log.Warn($"[PersonnelService] GrantVacation: no schedule for #{employeeId}");
                return false;
            }

            sched.overrides.Add(new ScheduleOverride
            {
                dateIsoStart = dateIsoStart,
                dateIsoEnd = dateIsoEnd,
                type = ScheduleOverrideType.Vacation,
                notes = "Urlop wypoczynkowy"
            });

            e.vacationDaysRemaining -= daysCount;
            e.vacationDaysUsedThisYear += daysCount;

            OnEmployeesChanged?.Invoke();
            Log.Info($"[PersonnelService] Vacation granted to #{employeeId}: {dateIsoStart}..{dateIsoEnd} ({daysCount}d)");
            return true;
        }

        /// <summary>M8-4: Zmien shift pracownika (prosty update bez schedule override).</summary>
        public static bool SetShift(int employeeId, ShiftType newShift)
        {
            var e = GetById(employeeId);
            if (e == null) return false;
            e.currentShift = newShift;
            var sched = GetSchedule(employeeId);
            if (sched != null) sched.defaultShift = newShift;

            OnEmployeeStatusChanged?.Invoke(e);
            OnEmployeesChanged?.Invoke();
            Log.Info($"[PersonnelService] Shift change for #{e.employeeId}: {newShift}");
            return true;
        }

        // ═══ Getters ═══

        /// <summary>O(1) lookup po id (Dictionary). Zwraca null dla nieznanego id.</summary>
        public static Employee GetById(int id)
        {
            return _byId.TryGetValue(id, out var e) ? e : null;
        }

        /// <summary>Wszyscy pracownicy (w tym Fired/Retired). Dla UI filtrujacego.</summary>
        public static IReadOnlyList<Employee> GetAll() => Employees;

        /// <summary>Tylko aktywni (nie Fired/Retired/LongSick).</summary>
        public static List<Employee> GetActive()
        {
            var result = new List<Employee>();
            foreach (var e in Employees)
                if (e.IsActive) result.Add(e);
            return result;
        }

        /// <summary>Aktywni pracownicy danej roli.</summary>
        public static List<Employee> GetByRole(EmployeeRole role)
        {
            var result = new List<Employee>();
            foreach (var e in Employees)
                if (e.role == role && e.IsActive) result.Add(e);
            return result;
        }

        /// <summary>
        /// Pracownicy dostepni do przypisania na dany dzien + zmiane.
        /// M8-2: filter (role + IsActive + nie chory + brak blokujacego override w schedule).
        /// BUG-010 fix (cz.3): respektuje urlop/L4/training z <see cref="EmployeeSchedule.overrides"/>.
        /// </summary>
        public static List<Employee> GetAvailableFor(EmployeeRole role, string dateIso, ShiftType? shift = null)
        {
            var result = new List<Employee>();
            foreach (var e in Employees)
            {
                if (e.role != role) continue;
                if (!e.IsActive) continue;
                if (e.status == EmployeeStatus.Sick || e.status == EmployeeStatus.LongSick) continue;
                if (shift.HasValue && e.currentShift != shift.Value) continue;
                if (!IsAvailableOnDate(e.employeeId, dateIso)) continue;
                result.Add(e);
            }
            return result;
        }

        /// <summary>
        /// Czy pracownik jest dostepny w danym dniu — sprawdza <see cref="EmployeeSchedule.overrides"/>.
        /// Blokujace overrides: <see cref="ScheduleOverrideType.Vacation"/>,
        /// <see cref="ScheduleOverrideType.SickLeave"/>, <see cref="ScheduleOverrideType.Training"/>.
        /// Niblokujace (pracownik dalej pracuje, tylko inaczej): <see cref="ScheduleOverrideType.ShiftSwap"/>,
        /// <see cref="ScheduleOverrideType.ExtraWorkDay"/>.
        ///
        /// BUG-010 fix (cz.3): pracownik na urlopie/L4 NIE moze byc na sluzbie. Decyzja user'a 2026-05-07.
        ///
        /// ISO date strings ("YYYY-MM-DD") — porownanie leksykograficzne == chronologiczne.
        /// Brak schedule (np. nowy pracownik) = available (zwracamy true).
        /// </summary>
        public static bool IsAvailableOnDate(int employeeId, string dateIso)
        {
            if (string.IsNullOrEmpty(dateIso)) return true;

            var schedule = GetSchedule(employeeId);
            if (schedule == null || schedule.overrides == null) return true;

            // BUG-030: normalizacja do 10-char ISO date (YYYY-MM-DD). Inputy mogą być
            // 19-char datetime (YYYY-MM-DDTHH:MM:SS) — bez normalizacji lex compare daje
            // błędne wyniki przy mieszanych formatach.
            string day = NormalizeIsoDate(dateIso);

            foreach (var ov in schedule.overrides)
            {
                if (!IsBlockingOverride(ov.type)) continue;
                string startDay = NormalizeIsoDate(ov.dateIsoStart);
                string endDay = NormalizeIsoDate(ov.dateIsoEnd);
                if (string.IsNullOrEmpty(startDay) || string.IsNullOrEmpty(endDay)) continue;
                // Date in [start, end] inclusive (10-char lex == chronologiczne)
                if (string.Compare(day, startDay, StringComparison.Ordinal) < 0) continue;
                if (string.Compare(day, endDay, StringComparison.Ordinal) > 0) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Normalizuje string ISO date/datetime do 10-char date ("YYYY-MM-DD").
        /// Zwraca null/empty bez zmian. Krótsze niż 10 char też pass-through.
        /// </summary>
        private static string NormalizeIsoDate(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return iso;
            return iso.Length >= 10 ? iso.Substring(0, 10) : iso;
        }

        /// <summary>True dla override'ow ktore blokuja sluzbe (urlop/L4/training).</summary>
        private static bool IsBlockingOverride(ScheduleOverrideType type)
        {
            return type == ScheduleOverrideType.Vacation
                || type == ScheduleOverrideType.SickLeave
                || type == ScheduleOverrideType.Training;
        }

        public static EmployeeSchedule GetSchedule(int employeeId)
        {
            return Schedules.TryGetValue(employeeId, out var s) ? s : null;
        }

        /// <summary>
        /// Shared empty instance dla nieaktywnych pracowników — czytany przez UI/runtime
        /// gdy pracownik Fired/Retired, ale nigdy nie mutowany. Eliminuje alokację per call.
        /// </summary>
        private static readonly EmployeeQualifications _emptyQualifications = new();

        /// <summary>
        /// Kwalifikacje pracownika — lazy create (puste w EA).
        /// Post-EA: training events ustawiają uprawnienia tutaj.
        ///
        /// BUG-026: dla nieaktywnych (Fired/Retired) zwraca **shared empty**
        /// bez mutacji dictionary — żeby nie powstawały zombie entries.
        /// </summary>
        public static EmployeeQualifications GetQualifications(int employeeId)
        {
            if (Qualifications.TryGetValue(employeeId, out var q))
                return q;

            // Nie lazy-create dla nieaktywnych — zwróć shared empty instance (readonly contract).
            var emp = GetById(employeeId);
            if (emp == null || !emp.IsActive)
                return _emptyQualifications;

            q = new EmployeeQualifications();
            Qualifications[employeeId] = q;
            return q;
        }

        // ═══ Notifications (do wykorzystania przez inne services) ═══

        /// <summary>Wywolane gdy status pracownika zmienil sie zewnetrznie (ShiftManager, SickLeaveService).</summary>
        public static void NotifyStatusChanged(Employee e)
        {
            if (e == null) return;
            // Incremental update _onShiftAgents — tani O(1) HashSet check + ew. O(n) List.Remove
            // (lista mała: ~aktualnie OnShift, nie wszyscy zatrudnieni).
            if (IsOnShiftSpawnable(e)) TryAddOnShift(e);
            else TryRemoveOnShift(e);

            OnEmployeeStatusChanged?.Invoke(e);
            OnEmployeesChanged?.Invoke();
        }

        public static void NotifyEmployeeDataChanged()
        {
            OnEmployeesChanged?.Invoke();
        }

        // ═══ Counters (dla UI / DispatcherWorkload) ═══

        public static int CountActiveByRole(EmployeeRole role)
        {
            int c = 0;
            foreach (var e in Employees)
                if (e.role == role && e.IsActive) c++;
            return c;
        }

        /// <summary>Laczna liczba aktywnych pracownikow (bez Fired/Retired). Uzywane przez DispatcherWorkload (headcount).</summary>
        public static int TotalActiveCount()
        {
            int c = 0;
            foreach (var e in Employees)
                if (e.IsActive) c++;
            return c;
        }

        /// <summary>Aktywni pracownicy wszystkich rol WYLACZAJAC dispatcherow (oni nie obciazaja sami siebie, D27).</summary>
        public static int HeadcountForDispatchers()
        {
            int c = 0;
            foreach (var e in Employees)
                if (e.IsActive && e.role != EmployeeRole.Dispatcher) c++;
            return c;
        }
    }
}
