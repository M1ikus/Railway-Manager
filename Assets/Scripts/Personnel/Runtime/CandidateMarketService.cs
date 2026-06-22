using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-3: Watchdog rynku pracy. Co <see cref="PersonnelBalanceConstants.CandidateMarketRefreshDays"/>
    /// (7 dni gry) wywoluje <see cref="PersonnelMarketGenerator.RefreshPool"/> — usuwa expired +
    /// dodaje 4 nowych (max 30).
    ///
    /// Subskrybuje <see cref="GameState.OnDayEnded"/>:
    /// - Inkrementuje <see cref="_daysSinceLastRefresh"/>
    /// - Przy progu → refresh pool
    /// - Dodatkowo: tick Job Postings (decrement daysRemaining, expire)
    ///
    /// Pierwsze wywolanie w <see cref="Awake"/>: generuje initial pool 20 kandydatow
    /// (min 1 per rola dla wszystkich 10 rol, reszta random).
    ///
    /// Bootstrap: <see cref="EnsureExists"/> — DontDestroyOnLoad GameObject.
    /// </summary>
    public class CandidateMarketService : MonoBehaviour
    {
        public static CandidateMarketService Instance { get; private set; }

        /// <summary>Wszyscy aktywni kandydaci na rynku.</summary>
        public static List<EmployeeCandidate> Candidates { get; } = new();

        /// <summary>Event: lista kandydatow sie zmienila (hire/refresh/expire).</summary>
        public static event Action OnMarketChanged;

        int _cycleNumber;
        int _daysSinceLastRefresh;

        public static CandidateMarketService EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("CandidateMarketService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CandidateMarketService>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (Candidates.Count == 0)
                GenerateInitialPool();

            Log.Info($"[CandidateMarketService] Bootstrapped, pool={Candidates.Count} candidates");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable() { GameState.OnDayEnded += OnDayEnded; }
        void OnDisable() { GameState.OnDayEnded -= OnDayEnded; }

        void OnDayEnded(string dateIsoJustEnded)
        {
            // 1) Tick Job Postings (dec days, expire)
            JobPostingService.OnDayEndedTick(dateIsoJustEnded);

            // 2) Refresh market co N dni
            _daysSinceLastRefresh++;
            if (_daysSinceLastRefresh < PersonnelBalanceConstants.CandidateMarketRefreshDays)
                return;

            DoRefresh();
        }

        void GenerateInitialPool()
        {
            _cycleNumber = 0;
            int seed = ComputeSeed();
            PersonnelMarketGenerator.GenerateInitialPool(Candidates, seed, 20);
            OnMarketChanged?.Invoke();
        }

        void DoRefresh()
        {
            _daysSinceLastRefresh = 0;
            _cycleNumber++;

            int seed = ComputeSeed();
            var weights = JobPostingService.GetActiveRoleWeights();
            PersonnelMarketGenerator.RefreshPool(Candidates, seed, GameState.CurrentDateIso, weights);
            OnMarketChanged?.Invoke();
        }

        /// <summary>Seed deterministyczny dla save/load i MP sync (D17 analog).</summary>
        int ComputeSeed()
        {
            // GameDay + cycleNumber + stabilny mixer
            unchecked
            {
                int h = 31;
                h = h * 131 + GameState.GameDay;
                h = h * 131 + _cycleNumber;
                return h;
            }
        }

        // ── Public API ────────────────────────────────

        /// <summary>Usuwa kandydata z rynku (np. po zatrudnieniu).</summary>
        public static bool RemoveCandidate(int candidateId)
        {
            for (int i = 0; i < Candidates.Count; i++)
            {
                if (Candidates[i].candidateId == candidateId)
                {
                    Candidates.RemoveAt(i);
                    OnMarketChanged?.Invoke();
                    return true;
                }
            }
            return false;
        }

        public static EmployeeCandidate GetById(int candidateId)
        {
            foreach (var c in Candidates)
                if (c.candidateId == candidateId) return c;
            return null;
        }

        /// <summary>Twardy reset — dla nowej gry. Wyzeruje ID counter + pool.</summary>
        public static void ResetAll()
        {
            Candidates.Clear();
            PersonnelMarketGenerator.ResetIdCounter();
            OnMarketChanged?.Invoke();
            Log.Info("[CandidateMarketService] Reset all");
        }

        // ── Debug (ContextMenu) ───────────────────────

        [ContextMenu("Debug: Force refresh market now")]
        public void DebugForceRefresh() => DoRefresh();

        [ContextMenu("Debug: Regenerate initial pool")]
        public void DebugRegenerateInitial()
        {
            Candidates.Clear();
            PersonnelMarketGenerator.ResetIdCounter();
            GenerateInitialPool();
        }

        [ContextMenu("Debug: List candidates")]
        public void DebugList()
        {
            if (Candidates.Count == 0) { Log.Info("[CandidateMarketService] Market empty."); return; }
            Log.Info($"[CandidateMarketService] Candidates ({Candidates.Count}):");
            foreach (var c in Candidates)
            {
                Log.Info($"  #{c.candidateId,-3} {c.firstName} {c.lastName,-15} " +
                         $"{RoleDefinitions.GetDisplayNamePl(c.role),-18} {c.skill}* " +
                         $"age={c.age} salary={c.expectedSalaryGroszy / 100}zl " +
                         (c.hireBonusGroszy > 0 ? $"bonus={c.hireBonusGroszy / 100}zl " : "") +
                         $"until={c.availableUntilDateIso}");
            }
        }
    }
}
