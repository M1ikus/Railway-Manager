using System;
using System.Collections.Generic;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-5: Daily tick ustalajacy <see cref="Employee.status"/> + <see cref="Employee.currentShift"/>
    /// na podstawie <see cref="EmployeeSchedule"/> (cykl bazowy + overrides).
    ///
    /// Kolejnosc decyzji per pracownik:
    /// 1. Retired/Fired/LongSick → bez zmian
    /// 2. Aktywny L4 (<see cref="Employee.sickUntilDateIso"/>) → <see cref="EmployeeStatus.Sick"/>
    /// 3. ScheduleOverride zachodzi na dzisiaj:
    ///    - Vacation/FreeDay/Training → Resting
    ///    - SickLeave → Sick
    ///    - ShiftSwap → OnShift + replacementShift
    ///    - ExtraDutyDay → OnShift + defaultShift
    /// 4. Baseline cycle (5+2, 4+2, 6+2, 7+7):
    ///    - Dzien pracujacy → OnShift + defaultShift
    ///    - Dzien wolny → Resting
    ///
    /// Dodatkowo tracker: ile dni z rzedu fatigue &gt; 80 (dla morale penalty
    /// w <see cref="FatigueMoraleTickService"/>, §5.1 D3).
    /// </summary>
    public static class ShiftManager
    {
        // Tracker: employeeId → dni z rzedu kiedy fatigue &gt; 80
        static readonly Dictionary<int, int> _fatigueOverDays = new();

        /// <summary>Pobierz licznik dni fatigue&gt;80 (dla FatigueMoraleTickService).</summary>
        public static int GetFatigueOverDays(int employeeId) =>
            _fatigueOverDays.TryGetValue(employeeId, out var d) ? d : 0;

        /// <summary>+1 do trackera (wywolane z FatigueMoraleTickService gdy fatigue zostal &gt; 80 po ticku).</summary>
        public static void IncrementFatigueOverDays(int employeeId)
        {
            _fatigueOverDays[employeeId] = _fatigueOverDays.TryGetValue(employeeId, out var d) ? d + 1 : 1;
        }

        /// <summary>Reset trackera gdy fatigue spadl &lt;= 80 lub pracownik odszedl.</summary>
        public static void ResetFatigueTracker(int employeeId) => _fatigueOverDays.Remove(employeeId);

        /// <summary>Reset wszystkich trackerow (new game / debug).</summary>
        public static void ResetAllTrackers() => _fatigueOverDays.Clear();

        // ═══ Daily tick ═══

        /// <summary>Wywolywane z <see cref="PersonnelDailyScheduler.OnDayEnded"/>.</summary>
        public static void ApplyDailyTick(string dateIso)
        {
            int changed = 0;
            int onboardingStarted = 0;
            float onboardingMin = DispatcherService.GetOnboardingMinutes();
            long currentGameTime = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;

            foreach (var e in PersonnelService.Employees)
            {
                if (e.status == EmployeeStatus.Fired || e.status == EmployeeStatus.Retired)
                    continue;
                if (e.status == EmployeeStatus.LongSick)
                    continue; // dluga choroba — stan trwaly az do manualnej zmiany

                var (newStatus, newShift) = ComputeStatusForDate(e, dateIso);
                if (e.status != newStatus || e.currentShift != newShift) changed++;

                // MM-D11/D20: każda transition Available/Resting/Sick → OnShift przechodzi
                // przez Onboarding state z timer'em. Czas zależy od Dispatcher lvl
                // (lvl 0 → 0 min = bypass; lvl 5 → 7.5 min). Kontynuacja OnShift→OnShift
                // (kolejny dzień pracy) NIE wpada w Onboarding.
                bool wasNotWorking = e.status != EmployeeStatus.OnShift
                                  && e.status != EmployeeStatus.Onboarding;
                bool transitionToOnShift = newStatus == EmployeeStatus.OnShift && wasNotWorking;

                if (transitionToOnShift && onboardingMin > 0f)
                {
                    e.status = EmployeeStatus.Onboarding;
                    e.currentShift = newShift;
                    e.onboardingFinishGameTime = currentGameTime + (long)(onboardingMin * 60f);
                    onboardingStarted++;
                }
                else
                {
                    e.status = newStatus;
                    e.currentShift = newShift;
                    if (newStatus != EmployeeStatus.Onboarding)
                        e.onboardingFinishGameTime = 0L;
                }

                // Clear sickUntilDateIso if L4 period past
                if (!string.IsNullOrEmpty(e.sickUntilDateIso))
                {
                    if (string.Compare(dateIso, e.sickUntilDateIso, StringComparison.Ordinal) > 0)
                    {
                        e.sickUntilDateIso = null;
                        if (e.status == EmployeeStatus.Sick)
                            e.status = EmployeeStatus.Available;
                    }
                }
            }
            if (changed > 0 || onboardingStarted > 0)
                Log.Debug($"[ShiftManager] Daily tick {dateIso}: {changed} status changes, " +
                          $"{onboardingStarted} onboarding started ({onboardingMin:F1} min @ Dispatcher lvl {DispatcherService.GetDispatcherLvl()})");
        }

        /// <summary>
        /// Czysta funkcja — oblicza stan bez mutacji. Dla UI preview ("co bedzie jutro").
        /// </summary>
        public static (EmployeeStatus status, ShiftType shift) ComputeStatusForDate(Employee e, string dateIso)
        {
            // 1) L4 aktywne (sickUntilDateIso)
            if (IsOnActiveSickLeave(e, dateIso))
                return (EmployeeStatus.Sick, e.currentShift);

            var sched = PersonnelService.GetSchedule(e.employeeId);
            if (sched == null)
                return (EmployeeStatus.Available, e.currentShift);

            // 2) ScheduleOverride zachodzi?
            foreach (var o in sched.overrides)
            {
                if (!IsDateInRange(dateIso, o.dateIsoStart, o.dateIsoEnd)) continue;
                switch (o.type)
                {
                    case ScheduleOverrideType.Vacation:
                    case ScheduleOverrideType.FreeDay:
                    case ScheduleOverrideType.Training:
                        return (EmployeeStatus.Resting, sched.defaultShift);
                    case ScheduleOverrideType.SickLeave:
                        return (EmployeeStatus.Sick, sched.defaultShift);
                    case ScheduleOverrideType.ShiftSwap:
                        var swapShift = o.hasReplacementShift ? o.replacementShift : sched.defaultShift;
                        return (EmployeeStatus.OnShift, swapShift);
                    case ScheduleOverrideType.ExtraDutyDay:
                        return (EmployeeStatus.OnShift, sched.defaultShift);
                }
            }

            // 3) Baseline cycle
            bool isWorkDay = IsWorkDayByBaseline(sched, dateIso);
            return isWorkDay
                ? (EmployeeStatus.OnShift, sched.defaultShift)
                : (EmployeeStatus.Resting, sched.defaultShift);
        }

        /// <summary>
        /// BUG-089: preview czy pracownik bylby na zmianie w danym dniu, ignorujac
        /// aktualny sickUntilDateIso. Uzywane przez SickLeaveService po ustawieniu L4,
        /// zeby wykryc wakat wg tej samej logiki cykli co daily ShiftManager.
        /// </summary>
        public static bool WouldBeOnShiftIgnoringSick(Employee e, string dateIso)
        {
            if (e == null) return false;
            var sched = PersonnelService.GetSchedule(e.employeeId);
            if (sched == null) return false;

            foreach (var o in sched.overrides)
            {
                if (!IsDateInRange(dateIso, o.dateIsoStart, o.dateIsoEnd)) continue;
                return o.type == ScheduleOverrideType.ShiftSwap
                    || o.type == ScheduleOverrideType.ExtraDutyDay;
            }

            return IsWorkDayByBaseline(sched, dateIso);
        }

        static bool IsOnActiveSickLeave(Employee e, string dateIso)
        {
            if (string.IsNullOrEmpty(e.sickUntilDateIso)) return false;
            return string.Compare(dateIso, e.sickUntilDateIso, StringComparison.Ordinal) <= 0;
        }

        static bool IsDateInRange(string iso, string rangeStart, string rangeEnd)
        {
            if (string.IsNullOrEmpty(rangeStart) || string.IsNullOrEmpty(rangeEnd)) return false;
            return string.Compare(iso, rangeStart, StringComparison.Ordinal) >= 0
                && string.Compare(iso, rangeEnd, StringComparison.Ordinal) <= 0;
        }

        static bool IsWorkDayByBaseline(EmployeeSchedule sched, string dateIso)
        {
            DateTime date;
            try { date = IsoTime.ParseDate(dateIso); } catch { return false; }

            // DayOfWeek 0=Sun..6=Sat → Pn=0..Nd=6
            int dayOfWeekMonday0 = ((int)date.DayOfWeek + 6) % 7;

            return sched.cycle switch
            {
                // Cycle5_2 (week-based, pn-pt praca / sb-nd wolne) — natural week
                WorkCyclePattern.Cycle5_2 => dayOfWeekMonday0 < 5,

                // BUG-066: Cycle4_2 = 4 pracy + 2 wolne = 6-dniowy ROLLING cykl od start gry.
                // Wcześniej `< 4` (week-based) dawało 3 dni wolne (pt-nd) zamiast 2.
                WorkCyclePattern.Cycle4_2 => RollingCyclePosition(date, 6) < 4,

                // BUG-066: Cycle6_2 = 6 pracy + 2 wolne = 8-dniowy ROLLING cykl od start gry.
                // Wcześniej `< 6` (week-based) dawało 1 dzień wolny (nd) zamiast 2.
                WorkCyclePattern.Cycle6_2 => RollingCyclePosition(date, 8) < 6,

                // Cycle7_7 (week-based parity) — synchronizowany dla wszystkich pracowników,
                // łatwiejsze do management. Design intent.
                WorkCyclePattern.Cycle7_7 => (GetIsoWeek(date) % 2 == 0),

                WorkCyclePattern.Custom => false, // custom = wszystkie dni przez override (brak baseline)
                _ => dayOfWeekMonday0 < 5
            };
        }

        /// <summary>
        /// BUG-066: pozycja w rolling cyklu (od start gry). Przykład Cycle4_2 (cycleLen=6):
        /// dzień 0 → pos=0 (praca), dzień 5 → pos=5 (wolne), dzień 6 → pos=0 (praca).
        /// </summary>
        static int RollingCyclePosition(DateTime date, int cycleLen)
        {
            DateTime epoch;
            try { epoch = IsoTime.ParseDate(RailwayManager.Core.GameState.GameStartDateIso); }
            catch { return 0; }
            int daysSinceEpoch = (int)(date - epoch).TotalDays;
            // Modulo z obsługą negatywów (gdyby data przed epoch'ą — defensive)
            int pos = daysSinceEpoch % cycleLen;
            if (pos < 0) pos += cycleLen;
            return pos;
        }

        static int GetIsoWeek(DateTime date)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return ci.Calendar.GetWeekOfYear(date,
                System.Globalization.CalendarWeekRule.FirstFourDayWeek,
                DayOfWeek.Monday);
        }
    }
}
