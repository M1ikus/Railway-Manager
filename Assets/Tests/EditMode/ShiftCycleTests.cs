using System.Reflection;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Personnel;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M8: testy grafików/zmian pracowników — cykl pracy (WorkCyclePattern) + resolve statusu na dzień.
    /// IsWorkDayByBaseline (private static — reflection) to czysty rdzeń cykli: 5+2 tygodniowy,
    /// 4+2/6+2 rolling od startu gry, 7+7 parzystość tygodnia. ComputeStatusForDate (public) — sick + brak grafiku.
    /// Rolling cykle kotwiczą na GameState.GameStartDateIso, więc test ustawia znaną datę.
    /// </summary>
    public class ShiftCycleTests
    {
        static readonly MethodInfo IsWorkDayMethod = typeof(ShiftManager).GetMethod(
            "IsWorkDayByBaseline", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(EmployeeSchedule), typeof(string) }, null);

        static bool IsWorkDay(EmployeeSchedule sched, string dateIso)
            => (bool)IsWorkDayMethod.Invoke(null, new object[] { sched, dateIso });

        static EmployeeSchedule Sched(WorkCyclePattern cycle)
            => new EmployeeSchedule { cycle = cycle, defaultShift = ShiftType.Morning };

        string _startDateBackup;

        [SetUp]
        public void SetUp()
        {
            Assert.That(IsWorkDayMethod, Is.Not.Null, "IsWorkDayByBaseline(EmployeeSchedule,string) musi istnieć.");
            _startDateBackup = GameState.GameStartDateIso;
            GameState.GameStartDateIso = "2026-06-01"; // poniedziałek — epoka dla rolling cykli
        }

        [TearDown]
        public void TearDown() => GameState.GameStartDateIso = _startDateBackup;

        // ── Cycle5_2 (tygodniowy pn-pt) ──────────────────────────────

        [Test]
        public void Cycle5_2_WorksWeekdays_RestsWeekend()
        {
            var s = Sched(WorkCyclePattern.Cycle5_2);
            Assert.That(IsWorkDay(s, "2026-06-01"), Is.True, "Poniedziałek = praca.");
            Assert.That(IsWorkDay(s, "2026-06-05"), Is.True, "Piątek = praca.");
            Assert.That(IsWorkDay(s, "2026-06-06"), Is.False, "Sobota = wolne.");
            Assert.That(IsWorkDay(s, "2026-06-07"), Is.False, "Niedziela = wolne.");
        }

        // ── Cycle4_2 (rolling 6-dniowy: 4 pracy + 2 wolne) ───────────

        [Test]
        public void Cycle4_2_RollingFromGameStart()
        {
            var s = Sched(WorkCyclePattern.Cycle4_2);
            Assert.That(IsWorkDay(s, "2026-06-01"), Is.True, "Dzień 0 (pos 0) = praca.");
            Assert.That(IsWorkDay(s, "2026-06-04"), Is.True, "Dzień 3 (pos 3) = praca (ostatni roboczy).");
            Assert.That(IsWorkDay(s, "2026-06-05"), Is.False, "Dzień 4 (pos 4) = wolne.");
            Assert.That(IsWorkDay(s, "2026-06-06"), Is.False, "Dzień 5 (pos 5) = wolne.");
            Assert.That(IsWorkDay(s, "2026-06-07"), Is.True, "Dzień 6 (pos 0) = praca (nowy cykl).");
        }

        // ── Cycle6_2 (rolling 8-dniowy: 6 pracy + 2 wolne) ───────────

        [Test]
        public void Cycle6_2_RollingFromGameStart()
        {
            var s = Sched(WorkCyclePattern.Cycle6_2);
            Assert.That(IsWorkDay(s, "2026-06-06"), Is.True, "Dzień 5 (pos 5) = praca (ostatni roboczy).");
            Assert.That(IsWorkDay(s, "2026-06-07"), Is.False, "Dzień 6 (pos 6) = wolne.");
            Assert.That(IsWorkDay(s, "2026-06-08"), Is.False, "Dzień 7 (pos 7) = wolne.");
            Assert.That(IsWorkDay(s, "2026-06-09"), Is.True, "Dzień 8 (pos 0) = praca (nowy cykl).");
        }

        // ── Cycle7_7 (parzystość tygodnia) ───────────────────────────

        [Test]
        public void Cycle7_7_AlternatesByWeek()
        {
            var s = Sched(WorkCyclePattern.Cycle7_7);
            // Tydzień i tydzień+7 dni mają przeciwną parzystość → przeciwny stan praca/wolne.
            bool week1 = IsWorkDay(s, "2026-06-01");
            bool week2 = IsWorkDay(s, "2026-06-08"); // +7 dni = następny tydzień
            Assert.That(week1, Is.Not.EqualTo(week2), "7+7: kolejne tygodnie naprzemiennie praca/wolne.");
            // Ten sam tydzień = ten sam stan.
            Assert.That(IsWorkDay(s, "2026-06-01"), Is.EqualTo(IsWorkDay(s, "2026-06-03")),
                "Dni w tym samym tygodniu mają ten sam stan (7+7 tygodniowy rytm).");
        }

        // ── Custom (brak baseline — wszystko przez override) ─────────

        [Test]
        public void Custom_NeverWorksByBaseline()
        {
            var s = Sched(WorkCyclePattern.Custom);
            Assert.That(IsWorkDay(s, "2026-06-01"), Is.False, "Custom = brak baseline (praca tylko przez override).");
            Assert.That(IsWorkDay(s, "2026-06-15"), Is.False);
        }

        // ── ComputeStatusForDate (public — sick / brak grafiku) ──────

        [Test]
        public void ComputeStatus_ActiveSickLeave_ReturnsSick()
        {
            var e = new Employee { employeeId = 90001, role = EmployeeRole.Driver, sickUntilDateIso = "2026-06-10" };
            var (status, _) = ShiftManager.ComputeStatusForDate(e, "2026-06-05"); // <= sickUntil
            Assert.That(status, Is.EqualTo(EmployeeStatus.Sick), "Data w okresie L4 → Sick.");
        }

        [Test]
        public void ComputeStatus_AfterSickLeave_NotSick()
        {
            var e = new Employee { employeeId = 90002, role = EmployeeRole.Driver, sickUntilDateIso = "2026-06-10" };
            var (status, _) = ShiftManager.ComputeStatusForDate(e, "2026-06-15"); // > sickUntil
            Assert.That(status, Is.Not.EqualTo(EmployeeStatus.Sick), "Po zakończeniu L4 → już nie Sick.");
        }

        [Test]
        public void ComputeStatus_NoScheduleRegistered_ReturnsAvailable()
        {
            // Brak zarejestrowanego grafiku (GetSchedule null) → Available (nie crashuje).
            var e = new Employee { employeeId = 90003, role = EmployeeRole.Office };
            var (status, _) = ShiftManager.ComputeStatusForDate(e, "2026-06-05");
            Assert.That(status, Is.EqualTo(EmployeeStatus.Available),
                "Bez grafiku pracownik jest Available (nie przypisany do zmiany).");
        }
    }
}
