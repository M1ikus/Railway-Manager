using RailwayManager.Core;

namespace RailwayManager.Personnel.Workflows
{
    /// <summary>
    /// TD-034 C: implementacja <see cref="IPersonalNeedProvider"/> na teraz — deterministyczny
    /// harmonogram (seed z employeeId + start zmiany). LockerIn na początku zmiany (role-gated),
    /// 1-2× łazienka, 1× przerwa (czasy z <see cref="PersonalNeedSchedule"/>).
    ///
    /// <para>Decyzja TD-034 #1: lekka diegetyczna symulacja, wymienialna w przyszłości na
    /// SimulatedNeedProvider (liczniki potrzeb) bez ruszania workflowów.</para>
    /// </summary>
    public class ScheduledNeedProvider : IPersonalNeedProvider
    {
        /// <summary>
        /// Czy rola przebiera się w ubranie robocze przy szafce (TD-034 #3).
        /// Operacyjni/mundur/brudna robota: Mechanic, Driver, Conductor, Cleaner, WashBay.
        /// Biurowi (Office/Research/Dispatcher/TrafficController/TicketClerk) — nie.
        /// </summary>
        public static bool RoleNeedsWorkClothes(EmployeeRole role) => role switch
        {
            EmployeeRole.Mechanic  => true,
            EmployeeRole.Driver    => true,
            EmployeeRole.Conductor => true,
            EmployeeRole.Cleaner   => true,
            EmployeeRole.WashBay   => true,
            _ => false
        };

        public PersonalActivity? GetDueActivity(Employee e, long now)
        {
            if (e == null) return null;
            if (!TryShiftWindow(e.currentShift, now, out long start, out long end)) return null;
            if (now < start || now >= end) return null;

            // Seed stabilny przez całą zmianę (start nie zmienia się w trakcie) — odporne na rollover doby.
            int seed = unchecked(e.employeeId * 31 + (int)(start / 60));

            // 1) LockerIn — przebranie na początku zmiany (role-gated, przed pracą)
            if (RoleNeedsWorkClothes(e.role) && !e.wearingWorkClothes)
                return new PersonalActivity { kind = PersonalActivityKind.LockerIn, dueAt = start };

            // 2) Łazienka (1-2 / zmianę)
            int bc = PersonalNeedSchedule.BathroomCount(seed);
            for (int i = 0; i < bc; i++)
            {
                long at = PersonalNeedSchedule.PlannedBathroomTime(start, end, seed, i, bc);
                if (now >= at && e.lastBathroomGameTime < at)
                    return new PersonalActivity { kind = PersonalActivityKind.Bathroom, dueAt = at };
            }

            // 3) Przerwa (1 / zmianę)
            long bAt = PersonalNeedSchedule.PlannedBreakTime(start, end, seed);
            if (now >= bAt && e.lastBreakGameTime < bAt)
                return new PersonalActivity { kind = PersonalActivityKind.Break, dueAt = bAt };

            return null;
        }

        /// <summary>
        /// Okno zmiany w absolutnym game-time wg currentShift + GameState.GameDay. Night rozwiązuje
        /// rollover doby (gdy now już po północy → użyj wczorajszego okna nocnego).
        /// </summary>
        static bool TryShiftWindow(ShiftType shift, long now, out long start, out long end)
        {
            long dayBase = (long)GameState.GameDay * 86400L;
            switch (shift)
            {
                case ShiftType.Morning:
                    start = dayBase + PersonnelBalanceConstants.ShiftMorningStartSec;
                    end   = dayBase + PersonnelBalanceConstants.ShiftMorningEndSec;
                    return true;
                case ShiftType.Afternoon:
                    start = dayBase + PersonnelBalanceConstants.ShiftAfternoonStartSec;
                    end   = dayBase + PersonnelBalanceConstants.ShiftAfternoonEndSec;
                    return true;
                case ShiftType.Night:
                    start = dayBase + PersonnelBalanceConstants.ShiftNightStartSec;          // dziś 22:00
                    end   = dayBase + 86400L + PersonnelBalanceConstants.ShiftNightEndSec;    // jutro 06:00
                    if (now < start) { start -= 86400L; end -= 86400L; }                      // jesteśmy w nocnej z wczoraj
                    return true;
                default:
                    start = end = 0L;
                    return false;
            }
        }
    }
}
