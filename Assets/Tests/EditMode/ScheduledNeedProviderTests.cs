using NUnit.Framework;
using RailwayManager.Personnel;
using RailwayManager.Personnel.Workflows;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>TD-034 Etap C: deterministyczny harmonogram czynności (pure) + role-gating przebierania.</summary>
    public class ScheduledNeedProviderTests
    {
        // Okno przykładowej zmiany porannej (06:00–14:00), span 8h = 28800 s.
        const long Start = 21600L;
        const long End = 50400L;
        const long Span = End - Start;

        [Test]
        public void RoleNeedsWorkClothes_OperationalTrue_OfficeFalse()
        {
            Assert.IsTrue(ScheduledNeedProvider.RoleNeedsWorkClothes(EmployeeRole.Mechanic));
            Assert.IsTrue(ScheduledNeedProvider.RoleNeedsWorkClothes(EmployeeRole.Driver));
            Assert.IsTrue(ScheduledNeedProvider.RoleNeedsWorkClothes(EmployeeRole.Conductor));
            Assert.IsTrue(ScheduledNeedProvider.RoleNeedsWorkClothes(EmployeeRole.Cleaner));
            Assert.IsTrue(ScheduledNeedProvider.RoleNeedsWorkClothes(EmployeeRole.WashBay));

            Assert.IsFalse(ScheduledNeedProvider.RoleNeedsWorkClothes(EmployeeRole.Office));
            Assert.IsFalse(ScheduledNeedProvider.RoleNeedsWorkClothes(EmployeeRole.Research));
            Assert.IsFalse(ScheduledNeedProvider.RoleNeedsWorkClothes(EmployeeRole.Dispatcher));
            Assert.IsFalse(ScheduledNeedProvider.RoleNeedsWorkClothes(EmployeeRole.TrafficController));
            Assert.IsFalse(ScheduledNeedProvider.RoleNeedsWorkClothes(EmployeeRole.TicketClerk));
        }

        [Test]
        public void BathroomCount_Is1Or2_AndDeterministic()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                int c = PersonalNeedSchedule.BathroomCount(seed);
                Assert.IsTrue(c == 1 || c == 2, $"seed {seed} → count {c} (oczekiwane 1 lub 2)");
                Assert.AreEqual(c, PersonalNeedSchedule.BathroomCount(seed), "Deterministyczne.");
            }
        }

        [Test]
        public void BathroomTimes_WithinWindow_AndOrdered()
        {
            const int seed = 12345;
            long t0 = PersonalNeedSchedule.PlannedBathroomTime(Start, End, seed, 0, 2);
            long t1 = PersonalNeedSchedule.PlannedBathroomTime(Start, End, seed, 1, 2);
            Assert.GreaterOrEqual(t0, Start); Assert.LessOrEqual(t0, End);
            Assert.GreaterOrEqual(t1, Start); Assert.LessOrEqual(t1, End);
            Assert.Less(t0, t1, "Pierwsza wizyta przed drugą.");
        }

        [Test]
        public void BreakTime_WithinWindow_NearMiddle()
        {
            const int seed = 777;
            long b = PersonalNeedSchedule.PlannedBreakTime(Start, End, seed);
            Assert.GreaterOrEqual(b, Start + (long)(Span * 0.40f), "Przerwa ~środek (dolna granica).");
            Assert.LessOrEqual(b, Start + (long)(Span * 0.60f), "Przerwa ~środek (górna granica).");
        }

        [Test]
        public void Schedule_Deterministic_SameSeedSameTimes()
        {
            const int seed = 9001;
            Assert.AreEqual(
                PersonalNeedSchedule.PlannedBreakTime(Start, End, seed),
                PersonalNeedSchedule.PlannedBreakTime(Start, End, seed));
            Assert.AreEqual(
                PersonalNeedSchedule.PlannedBathroomTime(Start, End, seed, 0, 2),
                PersonalNeedSchedule.PlannedBathroomTime(Start, End, seed, 0, 2));
        }

        [Test]
        public void DegenerateWindow_ReturnsStart()
        {
            Assert.AreEqual(Start, PersonalNeedSchedule.PlannedBreakTime(Start, Start, 5), "span<=0 → start.");
            Assert.AreEqual(Start, PersonalNeedSchedule.PlannedBathroomTime(Start, Start, 5, 0, 1), "span<=0 → start.");
        }
    }
}
