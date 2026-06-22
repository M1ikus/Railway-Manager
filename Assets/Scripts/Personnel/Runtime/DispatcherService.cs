using System;
using System.Collections.Generic;
using DepotSystem;
using DepotSystem.RoomLevel;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Timetable;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-7 / D27: Serwis zarzadzajacy auto-assignmentami personelu do pociagow i L4 replacements.
    ///
    /// <b>Capacity</b>: suma <c>50 + 5×(skill-1)</c> per aktywny Dispatcher.
    ///   1★=50, 3★=60, 5★=70 pracownikow ktorych moze obsadzic per dispatcher.
    ///
    /// <b>Headcount</b>: liczba wszystkich aktywnych pracownikow firmy MINUS Dispatchers
    /// (dispatcher nie obciaza samego siebie).
    ///
    /// <b>Statusy</b>:
    /// - <c>Normal</c>: headcount ≤ capacity → auto-akcje instant (status=Processing → Done od razu)
    /// - <c>Delayed</c>: 1.0-1.5× capacity → auto-akcje z delay 2-6h game time
    /// - <c>Critical</c>: &gt;1.5× capacity → random 20% akcji missed
    ///
    /// <b>Brak dyspozytora</b>: wszystko manualne — TryAutoAssign zwraca NoDispatcher.
    ///
    /// <b>Wybor zastepcy</b> (D30, weighted score):
    ///   0.4×proximity + 0.3×skillMatch + 0.3×restedness.
    ///   Skill 1★ dyspozytora — tylko proximity. 5★ — wszystkie wagi.
    /// </summary>
    public static class DispatcherService
    {
        static readonly List<PendingDispatchAction> _pending = new();
        static int _nextActionId = 1;

        // MP-9: deterministic RNG per-service
        static readonly DeterministicRng s_rng = RandomRegistry.GetRng("DispatcherService");

        public static event Action<PendingDispatchAction> OnActionCreated;
        public static event Action<PendingDispatchAction> OnActionCompleted;
        public static event Action<DispatcherWorkload> OnWorkloadChanged;

        public static IReadOnlyList<PendingDispatchAction> PendingActions => _pending;

        // ════════════════════════════════════════════════════════
        //  GetWorkload cache (audit punkt #8)
        // ════════════════════════════════════════════════════════
        // GetWorkload był wywoływany z każdego OnActionCreated/Completed event handlera w UI
        // (tab Dispatch) — trzy pełne scany PersonnelService.Employees per refresh
        // (GetActiveDispatchers + capacity loop + HeadcountForDispatchers). Przy 500+
        // pracowników i wielu UI events per klatka to ~1500 iteracji per refresh.
        //
        // Cache invalidowany przy mutacji _pending (Add/Remove) lub zmianie pracowników
        // (subscribe na PersonnelService.OnEmployeesChanged w Bootstrap nested class).
        static DispatcherWorkload _cachedWorkload;
        static bool _workloadDirty = true;

        public static void InvalidateWorkloadCache() => _workloadDirty = true;

        // ════════════════════════════════════════════════════════
        //  MM-6 — Onboarding delay (z Dispatcher room lvl, MM-D11/D20)
        // ════════════════════════════════════════════════════════


        /// <summary>Aktualny lvl pokoju Dispatcher (0 gdy brak), MM-D15 best-lvl-wins.</summary>
        public static int GetDispatcherLvl()
        {
            var svc = RoomLevelService.Instance;
            return svc == null ? 0 : svc.GetBestLevelForType(RoomType.Dispatcher);
        }

        /// <summary>
        /// MM-D11: czas trwania onboarding'u w minutach gry, w zależności od Dispatcher lvl.
        /// Brak dyspozytora w pokoju (lvl=0) → onboarding pominięty (0 min, instant OnShift).
        /// Mapping z spec'a 2.2:
        /// <list type="bullet">
        /// <item>lvl 0 → 0 min (brak dyspozytora — pracownik instant OnShift, defensywny default)</item>
        /// <item>lvl 1 → 2.0× = 30 min</item>
        /// <item>lvl 2 → 1.5× = 22.5 min</item>
        /// <item>lvl 3 → 1.0× = 15 min (baseline)</item>
        /// <item>lvl 4 → 0.7× = 10.5 min</item>
        /// <item>lvl 5 → 0.5× = 7.5 min</item>
        /// </list>
        /// </summary>
        public static float GetOnboardingMinutes()
        {
            int lvl = GetDispatcherLvl();
            float multiplier = lvl switch
            {
                0 => 0.0f,    // brak dyspozytora → bypass onboarding (gracz nie ma kogo "odprawić")
                1 => 2.0f,
                2 => 1.5f,
                3 => 1.0f,
                4 => 0.7f,
                5 => 0.5f,
                _ => 1.0f,
            };
            return PersonnelBalanceConstants.DispatcherBaseOnboardingMinutes * multiplier;
        }

        /// <summary>MM-5: max liczba dyspozytorów (delegate do RoleCaps).</summary>
        public static int GetMaxDispatcherHeadcount() => RoleCaps.DispatcherCapForLvl(GetDispatcherLvl());

        public static void ResetAll()
        {
            _pending.Clear();
            _nextActionId = 1;
            _workloadDirty = true;
            OnWorkloadChanged?.Invoke(GetWorkload());
        }

        // ═══ Capacity / Workload ═══

        public static List<Employee> GetActiveDispatchers()
        {
            var result = new List<Employee>();
            foreach (var e in PersonnelService.Employees)
                if (e.role == EmployeeRole.Dispatcher && e.IsActive) result.Add(e);
            return result;
        }

        public static int GetTotalCapacity()
        {
            int capacity = 0;
            foreach (var e in PersonnelService.Employees)
                if (e.role == EmployeeRole.Dispatcher && e.IsActive)
                    capacity += RoleDefinitions.GetDispatcherCapacity(e.skill);
            return capacity;
        }

        public static DispatcherWorkload GetWorkload()
        {
            if (!_workloadDirty) return _cachedWorkload;

            // Single-pass scan zamiast trzech (GetActiveDispatchers + capacity foreach +
            // HeadcountForDispatchers) — liczy razem dispatchers/capacity/headcount.
            int dispatcherCount = 0;
            int capacity = 0;
            int headcount = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (e == null || !e.IsActive) continue;
                if (e.role == EmployeeRole.Dispatcher)
                {
                    dispatcherCount++;
                    capacity += RoleDefinitions.GetDispatcherCapacity(e.skill);
                }
                else
                {
                    headcount++;
                }
            }

            _cachedWorkload = new DispatcherWorkload
            {
                totalCapacity = capacity,
                currentHeadcount = headcount,
                activeDispatcherCount = dispatcherCount,
                pendingActionsCount = _pending.Count,
                status = ComputeStatus(capacity, headcount, dispatcherCount)
            };
            _workloadDirty = false;
            return _cachedWorkload;
        }

        static DispatcherStatus ComputeStatus(int capacity, int headcount, int dispatcherCount)
        {
            if (dispatcherCount == 0) return DispatcherStatus.Critical;
            if (capacity == 0) return DispatcherStatus.Critical;
            float ratio = headcount / (float)capacity;
            if (ratio <= 1.0f) return DispatcherStatus.Normal;
            if (ratio <= PersonnelBalanceConstants.DispatcherCriticalOverThreshold)
                return DispatcherStatus.Delayed;
            return DispatcherStatus.Critical;
        }

        // ═══ Auto-assign ═══

        /// <summary>
        /// Probuje przypisac zastepcę dla vacancy (np. L4 maszynisty).
        /// Wynik + created action w kolejce pending (chyba ze NoDispatcher/NoCandidate).
        /// </summary>
        public static DispatchResult TryAutoAssignReplacement(CrewVacancyData vacancy)
        {
            if (vacancy == null) return DispatchResult.InvalidRequest;

            var workload = GetWorkload();

            if (workload.activeDispatcherCount == 0)
            {
                Log.Warn("[DispatcherService] No active dispatchers — auto-assign unavailable");
                return DispatchResult.NoDispatcher;
            }

            // Critical over-capacity: random miss
            if (workload.status == DispatcherStatus.Critical &&
                s_rng.Value < PersonnelBalanceConstants.DispatcherMissedActionChance)   // MP-9: seedowane
            {
                Log.Warn($"[DispatcherService] Critical over-capacity — missed action for " +
                         $"vacancy employee #{vacancy.employeeId}");
                return DispatchResult.Missed;
            }

            // Use best-skill dispatcher do quality decyzji (wagi weighted)
            var bestDispatcher = PickBestDispatcher(workload);
            int dispatcherSkill = bestDispatcher?.skill ?? 3;

            // Find replacement candidate
            var replacement = PickReplacement(vacancy, dispatcherSkill);
            if (replacement == null)
            {
                Log.Warn($"[DispatcherService] No candidate found for vacancy " +
                         $"({RoleDefinitions.GetDisplayNamePl(vacancy.role)} on {vacancy.affectedDateIso})");
                return DispatchResult.NoCandidateFound;
            }

            // Create pending action
            var status = workload.status == DispatcherStatus.Delayed
                ? PendingActionStatus.Delayed
                : PendingActionStatus.Processing;

            float delayHours = status == PendingActionStatus.Delayed
                ? s_rng.Range(
                    PersonnelBalanceConstants.DispatcherOverCapacityDelayHoursMin,
                    PersonnelBalanceConstants.DispatcherOverCapacityDelayHoursMax)   // MP-9: seedowane
                : 0f;

            var action = new PendingDispatchAction
            {
                actionId = _nextActionId++,
                vacancy = vacancy,
                replacementEmployeeId = replacement.employeeId,
                status = status,
                createdDateIso = GameState.CurrentDateIso,
                etaHoursRemaining = delayHours,
                dispatcherSkillUsed = dispatcherSkill
            };
            _pending.Add(action);
            _workloadDirty = true;
            OnActionCreated?.Invoke(action);
            OnWorkloadChanged?.Invoke(GetWorkload());

            // Processing = instant apply
            if (status == PendingActionStatus.Processing)
            {
                ApplyAction(action);
            }
            else
            {
                Log.Info($"[DispatcherService] Action #{action.actionId} DELAYED " +
                         $"({delayHours:F1}h): vacancy emp #{vacancy.employeeId} → replacement #{replacement.employeeId}");
            }

            return DispatchResult.Success;
        }

        /// <summary>
        /// Reczne przypisanie zastepcy przez gracza (z Notification manual modal lub DispatcherWorkloadUI).
        /// Pomija capacity — zawsze applied instant.
        /// </summary>
        public static bool ManualAssignReplacement(CrewVacancyData vacancy, int replacementEmployeeId)
        {
            if (vacancy == null) return false;
            var replacement = PersonnelService.GetById(replacementEmployeeId);
            if (replacement == null || !replacement.IsActive) return false;
            if (replacement.role != vacancy.role) return false;

            var action = new PendingDispatchAction
            {
                actionId = _nextActionId++,
                vacancy = vacancy,
                replacementEmployeeId = replacementEmployeeId,
                status = PendingActionStatus.Processing,
                createdDateIso = GameState.CurrentDateIso,
                etaHoursRemaining = 0f,
                dispatcherSkillUsed = 0 // manual = gracz
            };
            _pending.Add(action);
            _workloadDirty = true;
            ApplyAction(action);
            OnActionCreated?.Invoke(action);
            OnWorkloadChanged?.Invoke(GetWorkload());
            Log.Info($"[DispatcherService] Manual assign: vacancy emp #{vacancy.employeeId} → " +
                     $"replacement #{replacementEmployeeId} {replacement.DisplayFullName}");
            return true;
        }

        /// <summary>Anuluje pending action (gracz zmienia zdanie).</summary>
        public static bool CancelPendingAction(int actionId)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].actionId == actionId)
                {
                    _pending[i].status = PendingActionStatus.Cancelled;
                    var a = _pending[i];
                    _pending.RemoveAt(i);
                    _workloadDirty = true;
                    OnWorkloadChanged?.Invoke(GetWorkload());
                    Log.Info($"[DispatcherService] Action #{actionId} cancelled");
                    return true;
                }
            }
            return false;
        }

        // ═══ Daily tick — process Delayed actions ═══

        /// <summary>
        /// Wywolywane z <see cref="PersonnelDailyScheduler"/>. Dekrementuje eta dla Delayed,
        /// applies gdy osiaga 0 lub mniej, usuwa Done/Failed/Cancelled.
        ///
        /// Uproszczenie M8-7: kazdy tick = 24h. Delayed 2-6h → w praktyce zawsze applied w kolejnym dniu.
        /// (Post-EA: tick co godzine dla precyzji.)
        /// </summary>
        public static void ApplyDailyTick(string dateIso)
        {
            const float HoursPerTick = 24f;
            int applied = 0, removed = 0;

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var a = _pending[i];

                if (a.status == PendingActionStatus.Delayed)
                {
                    a.etaHoursRemaining -= HoursPerTick;
                    if (a.etaHoursRemaining <= 0f)
                    {
                        ApplyAction(a);
                        applied++;
                    }
                }

                // Cleanup — usun stare Done/Failed/Cancelled (starsze niz 1 dzien)
                if (a.status == PendingActionStatus.Done ||
                    a.status == PendingActionStatus.Failed ||
                    a.status == PendingActionStatus.Cancelled)
                {
                    _pending.RemoveAt(i);
                    removed++;
                }
            }

            if (applied > 0 || removed > 0)
            {
                _workloadDirty = true;
                OnWorkloadChanged?.Invoke(GetWorkload());
                Log.Debug($"[DispatcherService] Tick {dateIso}: applied={applied}, cleaned={removed}");
            }
        }

        static void ApplyAction(PendingDispatchAction action)
        {
            var replacement = PersonnelService.GetById(action.replacementEmployeeId);
            if (replacement == null || !replacement.IsActive)
            {
                action.status = PendingActionStatus.Failed;
                Log.Warn($"[DispatcherService] Action #{action.actionId} FAILED — replacement not available");
                return;
            }

            // M8-7: faktycznie przypisanie do CrewCirculation bedzie w M8-11.
            // Tutaj markujemy Done + log. Zdarzenie zostanie uchwycone przez M8-11 hook.
            action.status = PendingActionStatus.Done;
            OnActionCompleted?.Invoke(action);
            Log.Info($"[DispatcherService] Action #{action.actionId} APPLIED: " +
                     $"vacancy emp #{action.vacancy.employeeId} replaced by " +
                     $"#{replacement.employeeId} {replacement.DisplayFullName}");
        }

        // ═══ Candidate picking ═══

        static Employee PickBestDispatcher(DispatcherWorkload workload)
        {
            Employee best = null;
            int bestSkill = -1;
            foreach (var e in PersonnelService.Employees)
            {
                if (e.role != EmployeeRole.Dispatcher || !e.IsActive) continue;
                if (e.skill > bestSkill) { bestSkill = e.skill; best = e; }
            }
            return best;
        }

        /// <summary>
        /// BUG-068: tracking ostatnio dyspozycjonowanych pracowników dla rotation fairness.
        /// Bez tego ten sam pracownik (5★ + niski fatigue + wysoki morale) deterministycznie
        /// wygrywa każdy vacancy → spam jednego pracownika do wyczerpania, reszta zespołu
        /// nieużywana. Penalty -0.1 score za każdą ostatnią dyspozycję (max 5 zliczanych).
        /// </summary>
        static readonly System.Collections.Generic.Dictionary<int, int> _recentDispatchCount = new();

        /// <summary>
        /// Wybor zastepcy (D30 weighted score) modyfikowany przez <paramref name="dispatcherSkill"/>:
        /// - 1★: ignoruje restedness (tylko proximity + skillMatch, unmormalized)
        /// - 5★: pelne wagi
        /// Skill 2-4★: liniowa interpolacja.
        /// </summary>
        static Employee PickReplacement(CrewVacancyData vacancy, int dispatcherSkill)
        {
            var candidates = PersonnelService.GetAvailableFor(vacancy.role, vacancy.affectedDateIso);
            candidates.RemoveAll(c => c.employeeId == vacancy.employeeId);
            if (candidates.Count == 0) return null;

            // Dispatcher skill modulator: niski skill = mniej wag uwzglednionych
            float skillFactor = Math.Clamp((dispatcherSkill - 1) / 4f, 0f, 1f); // 0..1 (1★=0, 5★=1)

            Employee best = null;
            float bestScore = -1f;
            foreach (var c in candidates)
            {
                // Proximity: distance między home depot vacancy emp i candidate emp (D30).
                // Driver/Conductor mają homeStationNodeId set'owane przy Hire (post-EA UX),
                // dla legacy/innych ról = -1 → fallback 0.5 (neutralne).
                float proximity = ComputeProximity(vacancy, c);
                // SkillMatch: wyzszy skill = lepszy kandydat (max 1.0 dla 5★)
                float skillMatch = c.skill / 5f;
                // Restedness: 1 - fatigue/100
                float restedness = 1f - c.currentFatigue / 100f;

                // Wagi z Constants
                float wProx = PersonnelBalanceConstants.DispatcherWeightProximity;
                float wSkill = PersonnelBalanceConstants.DispatcherWeightSkillMatch;
                float wRest = PersonnelBalanceConstants.DispatcherWeightRestedness;

                // Slabszy dispatcher — ignoruje restedness
                float score = proximity * wProx
                            + skillMatch * wSkill * (0.5f + skillFactor * 0.5f)
                            + restedness * wRest * skillFactor;

                // Preferuj lepszy morale (mniej ryzyka strike)
                score += (c.currentMorale / 100f) * PersonnelBalanceConstants.DispatcherMoraleScoreWeight;

                // BUG-068: rotation fairness — penalty za ostatnie dyspozycje (cap 5).
                // Pierwsza dyspozycja: 0 penalty, druga: -0.1, ... piąta+: -0.5.
                int recentCount = _recentDispatchCount.TryGetValue(c.employeeId, out int rc) ? rc : 0;
                int penaltyCount = Math.Min(recentCount, PersonnelBalanceConstants.DispatcherRecentPenaltyMax);
                score -= penaltyCount * PersonnelBalanceConstants.DispatcherRecentPenaltyPerCount;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            // BUG-068: track ostatniego wybranego (decay przez ResetRecentDispatchCounts daily)
            if (best != null)
            {
                _recentDispatchCount[best.employeeId] = (_recentDispatchCount.TryGetValue(best.employeeId, out int cur) ? cur : 0) + 1;
            }
            return best;
        }

        /// <summary>
        /// BUG-068: reset rotation tracker (np. wywołane z OnDayEnded — daily refresh).
        /// Pozwala fair-rotation w skali dnia: pracownik użyty wczoraj nie ma penalty dziś.
        /// </summary>
        public static void ResetRecentDispatchCounts() => _recentDispatchCount.Clear();

        /// <summary>
        /// Subskrypcja na PersonnelService.OnEmployeesChanged dla invalidacji workload cache
        /// (capacity i headcount zależą od listy pracowników).
        /// </summary>
        private static class WorkloadCacheBootstrap
        {
            private static bool _subscribed;

            [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
            private static void Subscribe()
            {
                if (_subscribed) return;
                _subscribed = true;
                PersonnelService.OnEmployeesChanged += () => _workloadDirty = true;
            }
        }

        /// <summary>
        /// D30 proximity score 0..1 — bliżej = 1, daleko = 0.
        /// Distance liczone po <c>PathfindingGraph</c> z <see cref="TimetableInitializer"/>
        /// między <see cref="Employee.homeStationNodeId"/> nieobecnego (vacancy.employeeId)
        /// i kandydata. Gradient: 0..<see cref="PersonnelBalanceConstants.DispatcherMaxProximityDistance"/>m.
        ///
        /// Fallback 0.5 (neutralne) gdy:
        /// - Graph not loaded (depot scene before Map init)
        /// - vacancy.employeeId nie ma <c>homeStationNodeId</c> set (legacy permissive)
        /// - kandydat nie ma <c>homeStationNodeId</c> set
        /// - któryś nodeId out of range (corrupt save / loaded other map)
        /// </summary>
        static float ComputeProximity(CrewVacancyData vacancy, Employee candidate)
        {
            var graph = TimetableInitializer.Instance?.Graph;
            if (graph == null) return 0.5f;

            var vacancyEmp = PersonnelService.GetById(vacancy.employeeId);
            int vacancyNode = vacancyEmp?.homeStationNodeId ?? -1;
            int candidateNode = candidate.homeStationNodeId;

            if (vacancyNode < 0 || candidateNode < 0) return 0.5f;
            if (vacancyNode >= graph.NodeCount || candidateNode >= graph.NodeCount) return 0.5f;

            var posVacancy = graph.GetNode(vacancyNode).position;
            var posCandidate = graph.GetNode(candidateNode).position;
            float distance = UnityEngine.Vector2.Distance(posVacancy, posCandidate);

            float t = UnityEngine.Mathf.Clamp01(distance / PersonnelBalanceConstants.DispatcherMaxProximityDistance);
            return 1f - t;
        }
    }
}
