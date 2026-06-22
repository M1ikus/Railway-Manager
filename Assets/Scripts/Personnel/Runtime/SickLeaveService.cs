using System;
using System.Collections.Generic;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-6 / D13: Krotkoterminowe L4 (1-3 dni) z probalistyka base 0.5%/dzien
    /// modifikowana przez fatigue &gt; 50 (<see cref="PersonnelBalanceConstants.SickLeaveFatigueModifier"/>).
    ///
    /// Tick per Healthy active employee:
    /// 1. fatigueOver50 = max(0, fatigue - 50)
    /// 2. chance = 0.005 × (1 + fatigueOver50 × 0.02) = np. fatigue=80 → 0.005 × 1.6 = 0.8%
    /// 3. Roll → jesli hit: sickUntilDateIso = today + random(1,3) days
    /// 4. <see cref="PersonnelEvents.RaiseEmployeeGotSick"/>
    /// 5. Jesli pracownik ma byc OnShift dzis/jutro → <see cref="PersonnelEvents.RaiseCrewVacancyDetected"/>
    ///
    /// Recovery: <see cref="ShiftManager"/> auto-czysci <see cref="Employee.sickUntilDateIso"/>
    /// gdy data minie — tu tylko emit recovered event.
    ///
    /// Dlugie choroby (LongSick 7-14 dni): POST-EA (D13).
    /// </summary>
    public static class SickLeaveService
    {
        // MP-9: deterministic RNG per-service
        static readonly DeterministicRng s_rng = RandomRegistry.GetRng("SickLeaveService");

        public static void ApplyDailyTick(string dateIso)
        {
            DateTime today;
            try { today = IsoTime.ParseDate(dateIso); } catch { return; }

            int gotSickCount = 0, recoveredCount = 0;

            foreach (var e in PersonnelService.Employees)
            {
                if (e.status == EmployeeStatus.Fired || e.status == EmployeeStatus.Retired) continue;
                if (e.status == EmployeeStatus.LongSick) continue;

                // 1) Recovery detection — pracownik ma sickUntilDateIso wygaslym, status=Sick
                if (!string.IsNullOrEmpty(e.sickUntilDateIso))
                {
                    if (string.Compare(dateIso, e.sickUntilDateIso, StringComparison.Ordinal) > 0)
                    {
                        e.sickUntilDateIso = null;
                        if (e.status == EmployeeStatus.Sick)
                            e.status = EmployeeStatus.Available;
                        PersonnelEvents.RaiseEmployeeRecovered(e);
                        recoveredCount++;
                    }
                    continue; // jeszcze chorowal, nie roll na nowo
                }

                // Skip jesli juz chory lub nieaktywny
                if (e.status == EmployeeStatus.Sick) continue;
                if (!e.IsActive) continue;

                // 2) Roll
                float fatigueOver50 = Math.Max(0, e.currentFatigue - 50);
                float chance = PersonnelBalanceConstants.SickLeaveChancePerDay
                             * (1f + fatigueOver50 * PersonnelBalanceConstants.SickLeaveFatigueModifier);

                if (s_rng.Value < chance)   // MP-9: seedowane
                {
                    // BUG-061 (clarity): Range(min, max+1) — `+1` bo Range max-exclusive.
                    // Range(1, 4) zwraca 1/2/3 (3 możliwości) — odpowiada SickLeaveMinDays=1,
                    // MaxDays=3. AddDays(daysSick-1) bo dzień bieżący już liczy się jako 1 dzień
                    // L4 (np. daysSick=1 → endDate=today, employee chory tylko dzisiaj).
                    int daysSick = s_rng.Range(
                        PersonnelBalanceConstants.SickLeaveMinDays,
                        PersonnelBalanceConstants.SickLeaveMaxDays + 1);   // MP-9: seedowane

                    var endDate = today.AddDays(daysSick - 1);
                    e.sickUntilDateIso = endDate.ToString("yyyy-MM-dd");
                    e.status = EmployeeStatus.Sick;
                    gotSickCount++;

                    PersonnelEvents.RaiseEmployeeGotSick(e);

                    // Detect CrewVacancy — jesli pracownik mial byc OnShift (wg ShiftManager compute)
                    TryRaiseVacancy(e, today);

                    Log.Info($"[SickLeaveService] #{e.employeeId} {e.DisplayFullName} " +
                             $"got sick until {e.sickUntilDateIso} ({daysSick}d)");
                }
            }

            if (gotSickCount > 0 || recoveredCount > 0)
                Log.Debug($"[SickLeaveService] Tick {dateIso}: sick={gotSickCount}, recovered={recoveredCount}");
        }

        /// <summary>
        /// Sprawdza czy pracownik mial byc OnShift dzisiaj lub jutro — raise vacancy event dla notification UI.
        /// W M8-6: bez CrewCirculation kontekstu (null). W M8-8+ CrewAssignmentService doda crewCircId.
        /// </summary>
        static void TryRaiseVacancy(Employee e, DateTime today)
        {
            string todayIso = today.ToString("yyyy-MM-dd");
            string tomorrowIso = today.AddDays(1).ToString("yyyy-MM-dd");

            bool todayOnShift = WouldBeOnShift(e, todayIso);
            bool tomorrowOnShift = WouldBeOnShift(e, tomorrowIso);

            if (!todayOnShift && !tomorrowOnShift) return;

            string affectedIso = todayOnShift ? todayIso : tomorrowIso;

            var data = new CrewVacancyData
            {
                employeeId = e.employeeId,
                role = e.role,
                affectedDateIso = affectedIso,
                crewCirculationId = null, // M8-8 hook
                affectedDutyIndex = null,
                reason = CrewVacancyReason.SickLeave
            };
            PersonnelEvents.RaiseCrewVacancyDetected(data);
        }

        static bool WouldBeOnShift(Employee e, string dateIso)
        {
            // Compute bez mutacji — pracownik dopiero co dostal status Sick, ale helper
            // zwraca "co by robil gdyby nie byl chory" i uzywa rolling cycles z ShiftManager.
            return ShiftManager.WouldBeOnShiftIgnoringSick(e, dateIso);
        }

        /// <summary>Debug helper: recznie postaw pracownika na L4.</summary>
        public static bool DebugForceSick(int employeeId, int days)
        {
            var e = PersonnelService.GetById(employeeId);
            if (e == null) return false;
            if (!e.IsActive) return false;

            try
            {
                var today = IsoTime.ParseDate(GameState.CurrentDateIso);
                var end = today.AddDays(Math.Max(1, days) - 1);
                e.sickUntilDateIso = end.ToString("yyyy-MM-dd");
                e.status = EmployeeStatus.Sick;
                PersonnelEvents.RaiseEmployeeGotSick(e);
                TryRaiseVacancy(e, today);
                PersonnelService.NotifyStatusChanged(e);
                Log.Info($"[SickLeaveService] DEBUG force sick #{employeeId} until {e.sickUntilDateIso}");
                return true;
            }
            catch { return false; }
        }
    }
}
