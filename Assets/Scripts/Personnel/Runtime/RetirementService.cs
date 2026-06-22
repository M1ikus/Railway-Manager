using System;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-6 / D13: Emerytury age-based z 30-dniowym okresem wypowiedzenia.
    ///
    /// Tick:
    /// 1. Aktualizuj wiek wszystkich Active (recompute z birthDateIso vs CurrentDateIso)
    /// 2. Wiek 70+ → force retire natychmiast
    /// 3. Wiek 65+ → 20%/dzien chance ogloszenia
    /// 4. Wiek 60-64 → 5%/miesiac chance (sprawdzane 1. dnia miesiaca)
    /// 5. Jesli emerytura juz ogloszona: sprawdz czy minelo 30 dni → faktyczne odejscie
    ///
    /// Przy faktycznym odejsciu:
    /// - Fire(paySeverance=false) — zwykla odprawa NIE
    /// - Zamiast tego osobna odprawa emerytalna: 3× miesieczna pensja, category "Retirement"
    /// - Emit <see cref="PersonnelEvents.RaiseEmployeeRetired"/>
    ///
    /// Pracownik z ogloszonym retirement'em moze nadal pracowac przez 30 dni — daje graczowi
    /// czas na znalezienie zastepcy (Job Posting). Status Available/OnShift normalnie.
    /// </summary>
    public static class RetirementService
    {
        public static void ApplyDailyTick(string dateIso)
        {
            DateTime today;
            try { today = IsoTime.ParseDate(dateIso); } catch { return; }

            int retiredCount = 0, announcedCount = 0;

            // RetireNow ustawia status=Retired bez Remove z Employees → bezpieczna iteracja bez snapshot.
            foreach (var e in PersonnelService.Employees)
            {
                if (e.status == EmployeeStatus.Fired || e.status == EmployeeStatus.Retired) continue;

                // 1) Update age
                RecomputeAge(e, today);

                // 2) Check if retirement already announced — apply on endDate
                if (!string.IsNullOrEmpty(e.retirementEndDateIso))
                {
                    if (string.Compare(dateIso, e.retirementEndDateIso, StringComparison.Ordinal) >= 0)
                    {
                        RetireNow(e);
                        retiredCount++;
                    }
                    continue; // juz w wypowiedzeniu, nie roll
                }

                // 3) Wiek 70 — force immediate retirement
                if (e.age >= PersonnelBalanceConstants.RetirementAgeForce)
                {
                    AnnounceRetirement(e, today, immediateEnd: true);
                    RetireNow(e);
                    retiredCount++;
                    continue;
                }

                // 4) Wiek 65+ — 20%/dzien roll
                if (e.age >= PersonnelBalanceConstants.RetirementAgeMax)
                {
                    if (RollChance(PersonnelBalanceConstants.RetirementChance65plusPerDay))
                    {
                        AnnounceRetirement(e, today, immediateEnd: false);
                        announcedCount++;
                    }
                    continue;
                }

                // 5) Wiek 60-64 — 5%/miesiac (sprawdzamy 1. dnia)
                if (e.age >= PersonnelBalanceConstants.RetirementAgeMin && today.Day == 1)
                {
                    if (RollChance(PersonnelBalanceConstants.RetirementChance60to64PerMonth))
                    {
                        AnnounceRetirement(e, today, immediateEnd: false);
                        announcedCount++;
                    }
                }
            }

            if (retiredCount > 0 || announcedCount > 0)
                Log.Info($"[RetirementService] Tick {dateIso}: announced={announcedCount}, retired={retiredCount}");
        }

        static void RecomputeAge(Employee e, DateTime today)
        {
            if (string.IsNullOrEmpty(e.birthDateIso)) return;
            try
            {
                var birth = IsoTime.ParseDate(e.birthDateIso);
                int age = today.Year - birth.Year;
                if (today.Month < birth.Month || (today.Month == birth.Month && today.Day < birth.Day))
                    age--;
                if (age < 0) age = 0;
                e.age = age;
            }
            catch { /* ignore */ }
        }

        static void AnnounceRetirement(Employee e, DateTime today, bool immediateEnd)
        {
            e.retirementAnnouncedDateIso = today.ToString("yyyy-MM-dd");
            if (immediateEnd)
            {
                e.retirementEndDateIso = today.ToString("yyyy-MM-dd");
            }
            else
            {
                var end = today.AddDays(PersonnelBalanceConstants.RetirementNoticeDays);
                e.retirementEndDateIso = end.ToString("yyyy-MM-dd");
            }

            PersonnelEvents.RaiseRetirementAnnounced(e);
            Log.Info($"[RetirementService] #{e.employeeId} {e.DisplayFullName} " +
                     $"({RoleDefinitions.GetDisplayNamePl(e.role)}, age {e.age}) announced retirement " +
                     $"for {e.retirementEndDateIso}");
        }

        static void RetireNow(Employee e)
        {
            // Wyplac emerytalna odprawe (3× pensja miesieczna) — osobna category.
            int severance = e.currentSalaryGroszy * PersonnelBalanceConstants.RetirementSeveranceMonths;
            if (severance > 0)
            {
                var econ = EconomyManager.Instance;
                if (econ != null)
                    econ.AddCost(-1, severance, "Retirement",
                        $"Retirement severance #{e.employeeId} {e.DisplayFullName}");
                else
                    GameState.Money -= severance / 100;
            }

            // Bezposrednio status=Retired (pomijamy Fire bo Fire liczy severance wg stazu — tu osobna logika)
            e.status = EmployeeStatus.Retired;
            ShiftManager.ResetFatigueTracker(e.employeeId);
            PersonnelEvents.RaiseEmployeeRetired(e);
            PersonnelService.NotifyStatusChanged(e);

            Log.Info($"[RetirementService] #{e.employeeId} {e.DisplayFullName} retired " +
                     $"(severance {severance / 100}zl)");
        }

        // MP-9: deterministic RNG per-service
        static readonly DeterministicRng s_rng = RandomRegistry.GetRng("RetirementService");

        static bool RollChance(float probability)
        {
            return s_rng.Value < probability;   // MP-9: seedowane
        }
    }
}
