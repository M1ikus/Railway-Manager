using System;
using System.Collections.Generic;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-3 / D7: Seed-based generator kandydatow na rynek pracy.
    ///
    /// Dystrybucja skill (D7): 1★=35%, 2★=30%, 3★=20%, 4★=12%, 5★=3%.
    /// Wiek 25-60 (emeryci nie szukaja pracy).
    /// Oczekiwana pensja: base × (0.7 + 0.15×skill) × (0.9..1.1) — ±10% variance.
    /// Hire bonus: 5% szans, 5-25% rocznej pensji.
    ///
    /// Seed deterministyczny (D17 analog) — `(gameDay + cycleNumber).GetHashCode()`.
    /// Zapewnia reprodukowalnosc dla save/load i MP sync.
    ///
    /// Job Posting weights: aktywne ogloszenia zwiekszaja wage roli 3× w refresh pool.
    /// </summary>
    public static class PersonnelMarketGenerator
    {
        static int _nextCandidateId = 1;

        /// <summary>
        /// Reset counter (dla nowej gry / save load). Wywolane przez <see cref="CandidateMarketService.ResetAll"/>.
        /// </summary>
        public static void ResetIdCounter() => _nextCandidateId = 1;

        /// <summary>BUG-086: aktualny licznik candidateId dla save/load.</summary>
        public static int GetNextCandidateId() => _nextCandidateId;

        /// <summary>
        /// BUG-086: restore _nextCandidateId po load. Bez tego nowy refresh rynku pracy
        /// mogl wygenerowac kandydatow z ID kolidujacym z juz zapisanymi kandydatami.
        /// </summary>
        public static void RestoreNextCandidateId(int nextCandidateId)
        {
            _nextCandidateId = nextCandidateId > 0 ? nextCandidateId : 1;
        }

        /// <summary>
        /// Generuje poczatkowa pule — minimum 1 kandydat per rola (10 rol), reszta random.
        /// Zapewnia ze po pierwszym otwarciu gry gracz widzi wszystkie role.
        /// </summary>
        public static int GenerateInitialPool(List<EmployeeCandidate> market, int seed, int targetSize = 20)
        {
            market.Clear();
            var random = new Random(seed);

            // Balance: 1 kandydat per rola (10 total)
            foreach (EmployeeRole role in Enum.GetValues(typeof(EmployeeRole)))
            {
                market.Add(GenerateCandidate(random, role));
            }

            // Reszta losowa
            int remaining = Math.Max(0, targetSize - market.Count);
            for (int i = 0; i < remaining; i++)
            {
                var role = RollRole(random, null);
                market.Add(GenerateCandidate(random, role));
            }

            Log.Info($"[PersonnelMarketGenerator] Initial pool generated: {market.Count} candidates (seed={seed})");
            return market.Count;
        }

        /// <summary>
        /// Refresh pool — usuwa expired + dodaje nowych kandydatow.
        /// <paramref name="roleWeights"/> z <see cref="JobPostingService.GetActiveRoleWeights"/>.
        /// Bez weights = uniform distribution.
        /// </summary>
        public static int RefreshPool(
            List<EmployeeCandidate> market,
            int seed,
            string currentDateIso,
            Dictionary<EmployeeRole, int> roleWeights = null)
        {
            var random = new Random(seed);

            // Remove expired
            int removed = market.RemoveAll(c => IsExpired(c, currentDateIso));

            // Add new (up to refresh count, respecting max size)
            int toAdd = PersonnelBalanceConstants.CandidateMarketRefreshAddCount;
            int added = 0;
            for (int i = 0; i < toAdd && market.Count < PersonnelBalanceConstants.CandidateMarketMaxSize; i++)
            {
                var role = RollRole(random, roleWeights);
                market.Add(GenerateCandidate(random, role));
                added++;
            }

            if (removed > 0 || added > 0)
                Log.Info($"[PersonnelMarketGenerator] Refresh: -{removed} expired, +{added} new, total={market.Count}");

            return market.Count;
        }

        /// <summary>
        /// Tworzy jednego kandydata. Parametry <paramref name="forcedRole"/>/<paramref name="forcedSkill"/>
        /// override'uja losowanie (uzywane przez debug ContextMenu i Job Posting bias).
        /// </summary>
        public static EmployeeCandidate GenerateCandidate(
            Random random,
            EmployeeRole? forcedRole = null,
            int? forcedSkill = null)
        {
            var role = forcedRole ?? RollRole(random, null);
            int skill = forcedSkill ?? RollSkill(random);
            int age = 25 + random.Next(36); // 25-60

            var (first, last, _) = PolishNamesCatalog.GetRandomFullName(random);

            int baseSalary = RoleDefinitions.GetExpectedSalaryGroszy(role, skill);
            // Variance ±10%: factor in [0.9, 1.1]
            float variance = 1f - PersonnelBalanceConstants.CandidateSalaryVariance
                           + (float)random.NextDouble() * PersonnelBalanceConstants.CandidateSalaryVariance * 2f;
            int expectedSalary = (int)(baseSalary * variance);

            int hireBonus = 0;
            if (random.NextDouble() < PersonnelBalanceConstants.CandidateHireBonusChance)
            {
                float bonusRange = PersonnelBalanceConstants.CandidateHireBonusMaxPct
                                 - PersonnelBalanceConstants.CandidateHireBonusMinPct;
                float bonusPct = PersonnelBalanceConstants.CandidateHireBonusMinPct
                               + (float)random.NextDouble() * bonusRange;
                hireBonus = (int)(expectedSalary * 12 * bonusPct);
            }

            string validUntil = ComputeValidUntil();

            return new EmployeeCandidate
            {
                candidateId = _nextCandidateId++,
                firstName = first,
                lastName = last,
                age = age,
                role = role,
                skill = skill,
                expectedSalaryGroszy = expectedSalary,
                hireBonusGroszy = hireBonus,
                resumeNotes = CandidateFluffCatalog.GetRandomNotes(random, role, skill),
                availableUntilDateIso = validUntil
            };
        }

        // ── Helpers ───────────────────────────────────

        static int RollSkill(Random random)
        {
            double roll = random.NextDouble();
            double cum = PersonnelBalanceConstants.CandidateSkillDist1Star;
            if (roll < cum) return 1;
            cum += PersonnelBalanceConstants.CandidateSkillDist2Star;
            if (roll < cum) return 2;
            cum += PersonnelBalanceConstants.CandidateSkillDist3Star;
            if (roll < cum) return 3;
            cum += PersonnelBalanceConstants.CandidateSkillDist4Star;
            if (roll < cum) return 4;
            return 5;
        }

        /// <summary>
        /// Wazony wybor roli. Weights null/pusty = uniform. Weight 3 vs 1 = 3× prawdopodobienstwo tej roli.
        /// </summary>
        static EmployeeRole RollRole(Random random, Dictionary<EmployeeRole, int> weights)
        {
            var roles = (EmployeeRole[])Enum.GetValues(typeof(EmployeeRole));

            if (weights == null || weights.Count == 0)
                return roles[random.Next(roles.Length)];

            int total = 0;
            foreach (var w in weights.Values) total += w;
            if (total <= 0) return roles[random.Next(roles.Length)];

            int roll = random.Next(total);
            int cumulative = 0;
            foreach (var kv in weights)
            {
                cumulative += kv.Value;
                if (roll < cumulative) return kv.Key;
            }
            return roles[random.Next(roles.Length)]; // fallback (shouldn't reach)
        }

        static bool IsExpired(EmployeeCandidate c, string currentDateIso)
        {
            if (string.IsNullOrEmpty(c.availableUntilDateIso) || string.IsNullOrEmpty(currentDateIso))
                return false;
            return string.Compare(c.availableUntilDateIso, currentDateIso, StringComparison.Ordinal) < 0;
        }

        static string ComputeValidUntil()
        {
            try
            {
                return IsoTime.ParseDate(GameState.CurrentDateIso)
                    .AddDays(PersonnelBalanceConstants.CandidateValidityDays)
                    .ToString("yyyy-MM-dd");
            }
            catch
            {
                return "2099-12-31";
            }
        }
    }
}
