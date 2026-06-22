using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-15 / §3.7: Serwis badan i rozwoju. Zarzadza jedna aktywna sciezka R&amp;D naraz
    /// (EA ograniczenie — post-M12d wielorownolegly research tree).
    ///
    /// Daily tick:
    /// - Jesli aktywna sciezka i requirements met (N badaczy &gt;= skillMin, Available/OnShift):
    ///   daysRemaining-- → po osiagnieciu 0: <see cref="Complete"/> → apply effect w <see cref="ResearchUnlocks.Global"/>
    /// - Jesli requirements nie met: status=Interrupted, progress stoi
    ///
    /// Aktywacja sciezki: <see cref="StartResearch(string)"/> — daje sciezke do Available → InProgress.
    /// Mozna anulowac (progress traci).
    /// </summary>
    public class ResearchService : MonoBehaviour
    {
        public static ResearchService Instance { get; private set; }

        public static List<ResearchPath> AllPaths { get; } = new();

        public static ResearchPath Active { get; private set; }

        public static event Action<ResearchPath> OnResearchStarted;
        public static event Action<ResearchPath> OnResearchProgress;
        public static event Action<ResearchPath> OnResearchCompleted;
        public static event Action OnPathsChanged;

        public static ResearchService EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("ResearchService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ResearchService>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            LoadCatalog();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        static void LoadCatalog()
        {
            if (AllPaths.Count == 0)
                AllPaths.AddRange(ResearchPathCatalog.CreateAll());
        }

        /// <summary>BUG-088: snapshot mutable R&amp;D state dla save/load.</summary>
        public static IReadOnlyList<ResearchPath> GetPathsSnapshot()
        {
            LoadCatalog();
            return AllPaths;
        }

        /// <summary>
        /// BUG-088: restore R&amp;D state po load. Katalog traktujemy jako source of truth
        /// dla definicji sciezek, a z save'a przenosimy stan/progress.
        /// </summary>
        public static void RestoreFromSave(IList<ResearchPath> savedPaths)
        {
            AllPaths.Clear();
            AllPaths.AddRange(ResearchPathCatalog.CreateAll());
            Active = null;

            if (savedPaths != null)
            {
                foreach (var saved in savedPaths)
                {
                    if (saved == null || string.IsNullOrEmpty(saved.pathId)) continue;

                    var target = GetById(saved.pathId);
                    if (target == null)
                    {
                        AllPaths.Add(saved);
                        target = saved;
                    }
                    else
                    {
                        target.daysRemaining = saved.daysRemaining;
                        target.progressTenthsAccumulated = saved.progressTenthsAccumulated;
                        target.status = saved.status;
                    }

                    if (target.status == ResearchPathStatus.InProgress ||
                        target.status == ResearchPathStatus.Interrupted)
                    {
                        Active ??= target;
                    }
                }
            }

            OnPathsChanged?.Invoke();
        }

        public static void ResetAll()
        {
            AllPaths.Clear();
            AllPaths.AddRange(ResearchPathCatalog.CreateAll());
            Active = null;
            OnPathsChanged?.Invoke();
        }

        // ═══ Public API ═══

        public static ResearchPath GetById(string pathId)
        {
            foreach (var p in AllPaths)
                if (p.pathId == pathId) return p;
            return null;
        }

        public static bool StartResearch(string pathId)
        {
            if (Active != null)
            {
                Log.Warn($"[ResearchService] Cannot start — already active: {Active.pathId}");
                return false;
            }

            var p = GetById(pathId);
            if (p == null)
            {
                Log.Warn($"[ResearchService] Path not found: {pathId}");
                return false;
            }
            if (p.status == ResearchPathStatus.Completed)
            {
                Log.Warn($"[ResearchService] Already completed: {pathId}");
                return false;
            }

            p.status = ResearchPathStatus.InProgress;
            p.daysRemaining = p.durationDays;
            Active = p;
            OnResearchStarted?.Invoke(p);
            OnPathsChanged?.Invoke();
            Log.Info($"[ResearchService] Started: {p.displayName} ({p.durationDays}d, req {p.requiredResearchers}× ≥{p.minSkill}★)");
            return true;
        }

        public static bool CancelActive()
        {
            if (Active == null) return false;
            var p = Active;
            p.status = ResearchPathStatus.Available;
            p.daysRemaining = p.durationDays;
            Active = null;
            OnPathsChanged?.Invoke();
            Log.Info($"[ResearchService] Cancelled: {p.displayName}");
            return true;
        }

        // ═══ Daily tick ═══

        public static void ApplyDailyTick(string dateIso)
        {
            if (Active == null) return;
            if (Active.status == ResearchPathStatus.Completed) { Active = null; return; }

            // Sprawdz requirements
            int qualified = CountQualifiedResearchers(Active.minSkill);

            // MM-13 diagnostic: warning gdy mismatch (researcher bez biurka liczy się
            // na razie do qualified, ale powinien siedzieć przy desku żeby produktywnie pracować).
            // Full enforcement → post-EA (M-Balance refaktor researcherów at desks).
            int atDesks = CountResearchersAtDesks(Active.minSkill);
            if (qualified > 0 && atDesks < qualified)
            {
                Log.Debug($"[ResearchService] MM-13 diagnostic: {qualified - atDesks}/{qualified} " +
                          $"researcherów bez biurka (assignedFurnitureId<0). Postaw więcej desk_office " +
                          "lub awansuj Office (MM-5).");
            }

            if (qualified < Active.requiredResearchers)
            {
                if (Active.status != ResearchPathStatus.Interrupted)
                {
                    Active.status = ResearchPathStatus.Interrupted;
                    OnPathsChanged?.Invoke();
                    Log.Warn($"[ResearchService] {Active.displayName}: INTERRUPTED " +
                             $"(have {qualified}/{Active.requiredResearchers} researchers ≥{Active.minSkill}★)");
                }
                return;
            }

            // Progress
            if (Active.status != ResearchPathStatus.InProgress)
            {
                Active.status = ResearchPathStatus.InProgress;
                OnPathsChanged?.Invoke();
                Log.Info($"[ResearchService] {Active.displayName}: resumed progress");
            }

            // MM-5: R&D speed multiplier z Office lvl (MM-D22 — drabinka unlocków
            // wycięta, zostaje tylko speed ×). Office lvl 0 → speedMult 0 → research stoi.
            // Office lvl 5 → 2.2× → progres 2-3× szybszy niż baseline.
            float speedMult = OfficeService.GetResearchSpeedMultiplier();
            int tenthsThisTick = Mathf.RoundToInt(speedMult * 10f);
            if (tenthsThisTick <= 0)
            {
                // Office lvl 0 (brak biura) — research stoi mimo że researchers spełniają wymagania.
                // To jest degenerate case: gracz ma badaczy ale nie ma biura → log raz.
                if (Active.status != ResearchPathStatus.Interrupted)
                {
                    Active.status = ResearchPathStatus.Interrupted;
                    OnPathsChanged?.Invoke();
                    Log.Warn($"[ResearchService] {Active.displayName}: INTERRUPTED " +
                             "(brak biura — Office lvl 0, R&D speed=0)");
                }
                return;
            }

            Active.progressTenthsAccumulated += tenthsThisTick;
            int wholeDays = Active.progressTenthsAccumulated / 10;
            if (wholeDays > 0)
            {
                Active.daysRemaining -= wholeDays;
                Active.progressTenthsAccumulated %= 10;
            }
            OnResearchProgress?.Invoke(Active);

            if (Active.daysRemaining <= 0)
                Complete(Active);
        }

        static void Complete(ResearchPath p)
        {
            p.status = ResearchPathStatus.Completed;
            p.daysRemaining = 0;
            ResearchUnlocks.Global.Apply(p.effectKey, p.effectValue);
            Active = null;
            OnResearchCompleted?.Invoke(p);
            OnPathsChanged?.Invoke();
            Log.Info($"[ResearchService] ✓ COMPLETED: {p.displayName} " +
                     $"(unlocked: {p.effectKey}={p.effectValue})");
        }

        static int CountQualifiedResearchers(int minSkill)
        {
            int count = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (e.role != EmployeeRole.Research) continue;
                if (!e.IsActive) continue;
                if (e.status != EmployeeStatus.OnShift && e.status != EmployeeStatus.Available) continue;
                if (e.skill < minSkill) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// MM-13: liczba researcherów którzy SIEDZĄ przy biurku (assignedFurnitureId &gt;= 0).
        /// W EA tylko diagnostic/warning — full enforcement (researcher musi siedzieć
        /// żeby się liczył) odłożone do post-EA / M-Balance, bo przerwie balans w MM-5.
        /// </summary>
        public static int CountResearchersAtDesks(int minSkill)
        {
            int count = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (e.role != EmployeeRole.Research) continue;
                if (!e.IsActive) continue;
                if (e.status != EmployeeStatus.OnShift && e.status != EmployeeStatus.Available) continue;
                if (e.skill < minSkill) continue;
                if (e.assignedFurnitureId < 0) continue;  // brak biurka
                count++;
            }
            return count;
        }

        // ═══ Debug ═══

        [ContextMenu("Debug: Start 'Lepsze przeglady'")]
        public void DebugStartBetterInspections() => StartResearch("better_inspections");

        [ContextMenu("Debug: Start 'Optymalizacja trakcji'")]
        public void DebugStartTractionOpt() => StartResearch("traction_optimization");

        [ContextMenu("Debug: Cancel active")]
        public void DebugCancel() => CancelActive();

        [ContextMenu("Debug: Report research state")]
        public void DebugReport()
        {
            if (Active == null)
            {
                Log.Info($"[ResearchService] No active research. Available: {AllPaths.Count}, " +
                         $"researchers: {PersonnelService.CountActiveByRole(EmployeeRole.Research)}");
            }
            else
            {
                int qualified = CountQualifiedResearchers(Active.minSkill);
                Log.Info($"[ResearchService] Active: {Active.displayName} [{Active.status}] " +
                         $"{Active.daysRemaining}/{Active.durationDays}d remaining, " +
                         $"researchers {qualified}/{Active.requiredResearchers} ≥{Active.minSkill}★");
            }

            Log.Info("[ResearchService] Unlocks:");
            foreach (var kv in ResearchUnlocks.Global.AllEffects)
                Log.Info($"  {kv.Key} = {kv.Value}");
        }
    }
}
