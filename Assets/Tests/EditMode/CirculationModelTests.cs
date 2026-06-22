using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M5: testy data modelu Circulation — helpery przypisań pojazdów i logika dat
    /// (DayMask × weeksValid × oneTime). Czysty POCO, EditMode. GetActiveDates kotwiczy
    /// na GameState.GameStartDateIso, więc test ustawia znaną datę.
    /// </summary>
    public class CirculationModelTests
    {
        string _startDateBackup;

        [SetUp]
        public void SetUp()
        {
            _startDateBackup = GameState.GameStartDateIso;
            GameState.GameStartDateIso = "2026-06-01"; // poniedziałek
        }

        [TearDown]
        public void TearDown() => GameState.GameStartDateIso = _startDateBackup;

        // ── Przypisania pojazdów ─────────────────────────────────────

        [Test]
        public void GetVehiclesForDate_ReturnsAssignment()
        {
            var c = new Circulation();
            c.vehicleAssignmentsPerDay["2026-06-01"] = new List<int> { 10, 20 };

            Assert.That(c.GetVehiclesForDate("2026-06-01"), Is.EquivalentTo(new[] { 10, 20 }));
        }

        [Test]
        public void GetVehiclesForDate_UnknownDate_ReturnsEmptyNotNull()
        {
            var c = new Circulation();
            var result = c.GetVehiclesForDate("2099-01-01");
            Assert.That(result, Is.Not.Null, "Nigdy nie zwraca null.");
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ContainsVehicle_ChecksAllDays()
        {
            var c = new Circulation();
            c.vehicleAssignmentsPerDay["2026-06-01"] = new List<int> { 5 };
            c.vehicleAssignmentsPerDay["2026-06-02"] = new List<int> { 7 };

            Assert.That(c.ContainsVehicle(5), Is.True);
            Assert.That(c.ContainsVehicle(7), Is.True);
            Assert.That(c.ContainsVehicle(99), Is.False);
        }

        [Test]
        public void HasVehicle_TrueWhenAnyDayHasAssignment()
        {
            var empty = new Circulation();
            Assert.That(empty.HasVehicle, Is.False, "Obieg bez przypisań → Draft, brak pojazdu.");

            var assigned = new Circulation();
            assigned.vehicleAssignmentsPerDay["2026-06-01"] = new List<int> { 1 };
            Assert.That(assigned.HasVehicle, Is.True);
        }

        [Test]
        public void GetDatesForVehicle_ReturnsAllDatesForVehicle()
        {
            var c = new Circulation();
            c.vehicleAssignmentsPerDay["2026-06-01"] = new List<int> { 5, 8 };
            c.vehicleAssignmentsPerDay["2026-06-02"] = new List<int> { 5 };
            c.vehicleAssignmentsPerDay["2026-06-03"] = new List<int> { 8 };

            var datesFor5 = c.GetDatesForVehicle(5);
            Assert.That(datesFor5, Is.EquivalentTo(new[] { "2026-06-01", "2026-06-02" }));
        }

        // ── Cykliczność / daty ───────────────────────────────────────

        [Test]
        public void GetActiveDates_Daily_OneWeek_HasSevenDays()
        {
            var c = new Circulation { calendar = DayMask.Daily(), weeksValid = 1 };
            var dates = c.GetActiveDates();
            Assert.That(dates.Count, Is.EqualTo(7), "Codziennie × 1 tydzień = 7 dni.");
        }

        [Test]
        public void GetActiveDates_Weekdays_OneWeek_HasFiveDays()
        {
            var c = new Circulation { calendar = DayMask.OnlyWeekdays(), weeksValid = 1 };
            var dates = c.GetActiveDates();
            Assert.That(dates.Count, Is.EqualTo(5), "Tylko dni robocze × 1 tydzień = 5 dni.");
            // Start 2026-06-01 to poniedziałek → daty pn-pt (01-05 czerwca).
            Assert.That(dates.All(d => d.DayOfWeek != System.DayOfWeek.Saturday
                                    && d.DayOfWeek != System.DayOfWeek.Sunday), Is.True,
                "Żadna data nie wypada w weekend.");
        }

        [Test]
        public void GetActiveDates_Weekend_OneWeek_HasTwoDays()
        {
            var c = new Circulation { calendar = DayMask.OnlyWeekend(), weeksValid = 1 };
            var dates = c.GetActiveDates();
            Assert.That(dates.Count, Is.EqualTo(2), "Tylko weekend × 1 tydzień = 2 dni.");
        }

        [Test]
        public void GetActiveDates_OneTime_SingleDate()
        {
            var c = new Circulation { isOneTime = true, oneTimeDateIso = "2026-06-15" };
            var dates = c.GetActiveDates();
            Assert.That(dates.Count, Is.EqualTo(1), "Jednorazowy = 1 data.");
            Assert.That(dates[0].ToString("yyyy-MM-dd"), Is.EqualTo("2026-06-15"));
        }

        [Test]
        public void GetActiveDates_OneTime_InvalidDate_Empty()
        {
            var c = new Circulation { isOneTime = true, oneTimeDateIso = "" };
            Assert.That(c.GetActiveDates(), Is.Empty, "Jednorazowy bez daty → pusta lista.");
        }

        [Test]
        public void GetActiveDates_DailyTwoWeeks_HasFourteenDays()
        {
            var c = new Circulation { calendar = DayMask.Daily(), weeksValid = 2 };
            Assert.That(c.GetActiveDates().Count, Is.EqualTo(14), "Codziennie × 2 tygodnie = 14 dni.");
        }

        [Test]
        public void GetActiveDates_WeeksValidZero_DefaultsTo4Weeks()
        {
            // weeksValid=0 (bezterminowo) → default 28 dni do wyświetlenia.
            var c = new Circulation { calendar = DayMask.Daily(), weeksValid = 0 };
            Assert.That(c.GetActiveDates().Count, Is.EqualTo(28), "weeksValid=0 → fallback 4 tygodnie.");
        }

        // ── Helpery ──────────────────────────────────────────────────

        [Test]
        public void IsTrivial_TrueForSingleStep()
        {
            var c = new Circulation();
            c.steps.Add(new CirculationStep(1));
            Assert.That(c.IsTrivial, Is.True, "1-krokowy obieg (z FleetPanelUI) = trywialny.");

            c.steps.Add(new CirculationStep(2));
            Assert.That(c.IsTrivial, Is.False, "2+ kroki = nietrywialny.");
        }

        [Test]
        public void StepCount_ReflectsSteps()
        {
            var c = new Circulation();
            Assert.That(c.StepCount, Is.EqualTo(0));
            c.steps.Add(new CirculationStep(1));
            c.steps.Add(new CirculationStep(2));
            Assert.That(c.StepCount, Is.EqualTo(2));
        }
    }
}
