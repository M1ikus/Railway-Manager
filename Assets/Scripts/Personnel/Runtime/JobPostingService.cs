using System;
using System.Collections.Generic;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-3 / D7: Platne ogloszenia rekrutacyjne.
    /// Tworzenie: koszt = <see cref="PersonnelBalanceConstants.JobPostingBaseCost"/>
    /// + per-star multiplier. Max 3 aktywne.
    ///
    /// Wplyw: aktywne ogloszenie = weight 3× dla danej roli w <see cref="PersonnelMarketGenerator.RefreshPool"/>
    /// (kandydaci tej roli trafiaja na rynek czesciej przez 14 dni).
    ///
    /// Daily tick (<see cref="OnDayEndedTick"/>) wywolany z <see cref="CandidateMarketService"/>.
    ///
    /// Koszt oplacany natychmiast przez <see cref="EconomyManager.AddCost"/> (category="Recruitment").
    /// </summary>
    public static class JobPostingService
    {
        // BUG-036: encapsulacja — backing field private, public access read-only.
        private static readonly List<JobPosting> _activePostings = new();
        public static IReadOnlyList<JobPosting> ActivePostings => _activePostings;
        static int _nextJobPostingId = 1;

        public static event Action OnPostingsChanged;

        /// <summary>BUG-036: load snapshot z save (clear + addrange + 1× notify).</summary>
        public static void LoadSnapshot(IList<JobPosting> snapshot)
        {
            _activePostings.Clear();
            if (snapshot != null) _activePostings.AddRange(snapshot);
            OnPostingsChanged?.Invoke();
        }

        /// <summary>BUG-080: aktualny licznik postingId dla save.</summary>
        public static int GetNextPostingId() => _nextJobPostingId;

        /// <summary>
        /// BUG-080: restore state z save z _nextJobPostingId. Wcześniej LoadSnapshot
        /// nie restorował counter'a → nowy CreatePosting dostawał id=1 (kolizja).
        /// </summary>
        public static void RestoreFromSave(IList<JobPosting> snapshot, int nextPostingId)
        {
            _activePostings.Clear();
            if (snapshot != null) _activePostings.AddRange(snapshot);
            _nextJobPostingId = nextPostingId > 0 ? nextPostingId : 1;
            OnPostingsChanged?.Invoke();
        }

        /// <summary>Tworzy platne ogloszenie. Zwraca null gdy limit osiagniety lub brak srodkow.</summary>
        public static JobPosting CreatePosting(EmployeeRole role, int skillTarget)
        {
            if (ActivePostings.Count >= PersonnelBalanceConstants.JobPostingMaxActive)
            {
                Log.Warn($"[JobPostingService] Max active postings ({PersonnelBalanceConstants.JobPostingMaxActive}) reached");
                return null;
            }

            skillTarget = Math.Clamp(skillTarget, 1, 5);
            int cost = PersonnelBalanceConstants.JobPostingBaseCost
                     + PersonnelBalanceConstants.JobPostingSpecialistMultPerStar * (skillTarget - 1);

            // Sprawdz czy staac gracza (Money w zl, koszt w groszach)
            if (GameState.Money * 100L < cost)
            {
                Log.Warn($"[JobPostingService] Insufficient funds: need {cost / 100}zl, have {GameState.Money}zl");
                return null;
            }

            var posting = new JobPosting
            {
                jobPostingId = _nextJobPostingId++,
                role = role,
                skillTarget = skillTarget,
                costGroszy = cost,
                daysRemaining = PersonnelBalanceConstants.JobPostingDurationDays,
                createdDateIso = GameState.CurrentDateIso
            };
            _activePostings.Add(posting);

            // Charge economy
            var econ = EconomyManager.Instance;
            if (econ != null)
            {
                econ.AddCost(-1, cost, "Recruitment",
                    $"Job posting #{posting.jobPostingId} {RoleDefinitions.GetDisplayNamePl(role)} {skillTarget}*");
            }
            else
            {
                // Fallback gdy EconomyManager not bootstrapped (tests/early scene)
                GameState.Money -= cost / 100;
                Log.Warn("[JobPostingService] EconomyManager null — deducted from GameState.Money directly");
            }

            OnPostingsChanged?.Invoke();
            Log.Info($"[JobPostingService] Created #{posting.jobPostingId}: " +
                     $"{RoleDefinitions.GetDisplayNamePl(role)} {skillTarget}* cost={cost / 100}zl, {posting.daysRemaining} days");
            return posting;
        }

        public static bool CancelPosting(int postingId)
        {
            for (int i = 0; i < ActivePostings.Count; i++)
            {
                if (ActivePostings[i].jobPostingId == postingId)
                {
                    Log.Info($"[JobPostingService] Cancelled posting #{postingId}");
                    _activePostings.RemoveAt(i);
                    OnPostingsChanged?.Invoke();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Zwraca mape role → weight dla <see cref="PersonnelMarketGenerator.RefreshPool"/>.
        /// Role bez ogloszenia: weight 1. Role z aktywnym ogloszeniem: weight 3 (3× prawdopodobienstwo).
        /// </summary>
        public static Dictionary<EmployeeRole, int> GetActiveRoleWeights()
        {
            var weights = new Dictionary<EmployeeRole, int>();
            foreach (EmployeeRole role in Enum.GetValues(typeof(EmployeeRole)))
                weights[role] = 1;

            foreach (var p in ActivePostings)
                weights[p.role] = 3;

            return weights;
        }

        /// <summary>Daily tick — dec daysRemaining, remove expired. Wywolywane z <see cref="CandidateMarketService.OnDayEnded"/>.</summary>
        public static void OnDayEndedTick(string dateIso)
        {
            bool changed = false;
            for (int i = ActivePostings.Count - 1; i >= 0; i--)
            {
                ActivePostings[i].daysRemaining--;
                if (ActivePostings[i].daysRemaining <= 0)
                {
                    Log.Info($"[JobPostingService] Posting #{ActivePostings[i].jobPostingId} expired");
                    _activePostings.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed) OnPostingsChanged?.Invoke();
        }

        public static void ResetAll()
        {
            _activePostings.Clear();
            _nextJobPostingId = 1;
            OnPostingsChanged?.Invoke();
        }

        // ── Helpers dla UI ────────────────────────────

        /// <summary>Oblicz koszt ogloszenia przed utworzeniem (dla UI preview).</summary>
        public static int ComputeCost(int skillTarget)
        {
            skillTarget = Math.Clamp(skillTarget, 1, 5);
            return PersonnelBalanceConstants.JobPostingBaseCost
                 + PersonnelBalanceConstants.JobPostingSpecialistMultPerStar * (skillTarget - 1);
        }
    }
}
