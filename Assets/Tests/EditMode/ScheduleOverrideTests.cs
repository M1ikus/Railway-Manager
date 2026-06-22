using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Personnel;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M8-4: testy warstwy override grafiku (urlop/L4/zamiana zmiany) nad cyklem baseline.
    /// ComputeStatusForDate z zarejestrowanym EmployeeSchedule — override MA PIERWSZEŃSTWO nad cyklem.
    /// Grafik wstawiany bezpośrednio do PersonnelService.Schedules, sprzątany w TearDown.
    /// </summary>
    public class ScheduleOverrideTests
    {
        const int Eid = 880001;
        string _startDateBackup;

        [SetUp]
        public void SetUp()
        {
            _startDateBackup = GameState.GameStartDateIso;
            GameState.GameStartDateIso = "2026-06-01"; // poniedziałek
            PersonnelService.Schedules.Remove(Eid);
        }

        [TearDown]
        public void TearDown()
        {
            PersonnelService.Schedules.Remove(Eid);
            GameState.GameStartDateIso = _startDateBackup;
        }

        static Employee Emp() => new Employee { employeeId = Eid, role = EmployeeRole.Office };

        /// <summary>Rejestruje grafik 5+2 (pn-pt praca) z podanymi override'ami.</summary>
        static void RegisterSchedule(params ScheduleOverride[] overrides)
        {
            var sched = new EmployeeSchedule
            {
                employeeId = Eid,
                cycle = WorkCyclePattern.Cycle5_2,
                defaultShift = ShiftType.Morning,
                overrides = new List<ScheduleOverride>(overrides)
            };
            PersonnelService.Schedules[Eid] = sched;
        }

        [Test]
        public void Baseline_NoOverride_WorkdayIsOnShift()
        {
            RegisterSchedule(); // brak override
            // 2026-06-01 poniedziałek, cykl 5+2 → praca.
            var (status, shift) = ShiftManager.ComputeStatusForDate(Emp(), "2026-06-01");
            Assert.That(status, Is.EqualTo(EmployeeStatus.OnShift));
            Assert.That(shift, Is.EqualTo(ShiftType.Morning), "Zmiana wg defaultShift gdy brak override.");
        }

        [Test]
        public void Vacation_OverridesWorkday_ToResting()
        {
            RegisterSchedule(new ScheduleOverride
            {
                dateIsoStart = "2026-06-01", dateIsoEnd = "2026-06-05",
                type = ScheduleOverrideType.Vacation
            });
            // Pn (normalnie praca) ale urlop → Resting (override > cykl).
            var (status, _) = ShiftManager.ComputeStatusForDate(Emp(), "2026-06-03");
            Assert.That(status, Is.EqualTo(EmployeeStatus.Resting), "Urlop nadpisuje dzień roboczy → Resting.");
        }

        [Test]
        public void SickLeaveOverride_ToSick()
        {
            RegisterSchedule(new ScheduleOverride
            {
                dateIsoStart = "2026-06-02", dateIsoEnd = "2026-06-04",
                type = ScheduleOverrideType.SickLeave
            });
            var (status, _) = ShiftManager.ComputeStatusForDate(Emp(), "2026-06-03");
            Assert.That(status, Is.EqualTo(EmployeeStatus.Sick), "L4-override → Sick.");
        }

        [Test]
        public void ShiftSwap_ChangesShiftKeepsOnShift()
        {
            RegisterSchedule(new ScheduleOverride
            {
                dateIsoStart = "2026-06-03", dateIsoEnd = "2026-06-03",
                type = ScheduleOverrideType.ShiftSwap,
                hasReplacementShift = true, replacementShift = ShiftType.Night
            });
            var (status, shift) = ShiftManager.ComputeStatusForDate(Emp(), "2026-06-03");
            Assert.That(status, Is.EqualTo(EmployeeStatus.OnShift), "Zamiana zmiany → nadal pracuje.");
            Assert.That(shift, Is.EqualTo(ShiftType.Night), "Zmiana nadpisana na nocną.");
        }

        [Test]
        public void ExtraDutyDay_OnWeekend_ToOnShift()
        {
            RegisterSchedule(new ScheduleOverride
            {
                dateIsoStart = "2026-06-06", dateIsoEnd = "2026-06-06", // sobota (normalnie wolne)
                type = ScheduleOverrideType.ExtraDutyDay
            });
            var (status, _) = ShiftManager.ComputeStatusForDate(Emp(), "2026-06-06");
            Assert.That(status, Is.EqualTo(EmployeeStatus.OnShift),
                "Dzień dodatkowy w weekend → praca (override nad cyklem wolnym).");
        }

        [Test]
        public void OverrideOutsideRange_FallsBackToBaseline()
        {
            // Override na 06-10 nie dotyczy 06-03 → baseline cykl decyduje (pn = praca).
            RegisterSchedule(new ScheduleOverride
            {
                dateIsoStart = "2026-06-10", dateIsoEnd = "2026-06-10",
                type = ScheduleOverrideType.Vacation
            });
            var (status, _) = ShiftManager.ComputeStatusForDate(Emp(), "2026-06-03");
            Assert.That(status, Is.EqualTo(EmployeeStatus.OnShift),
                "Poza zakresem override → baseline (środa = praca w 5+2).");
        }

        [Test]
        public void WeekendBaseline_NoOverride_IsResting()
        {
            RegisterSchedule();
            // Sobota, cykl 5+2, brak override → Resting.
            var (status, _) = ShiftManager.ComputeStatusForDate(Emp(), "2026-06-06");
            Assert.That(status, Is.EqualTo(EmployeeStatus.Resting));
        }
    }
}
