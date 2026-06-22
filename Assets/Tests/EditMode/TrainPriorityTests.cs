using System.Reflection;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M9a: testy priorytetyzacji ruchu (GetIrjBasePriority + IsRushHour) — decydują o pierwszeństwie
    /// na blokach/mijankach. To CZYSTE statyczne metody (zero stanu instancji), więc reflection
    /// na static omija ciężki TrainRunSimulator.Awake (DispatchService DontDestroyOnLoad rzuca w EditMode).
    ///
    /// Hierarchia: międzynarodowy(10) > ekspres(7) > pospieszny(5) > osobowy(3, →9 rush) > służbowy(2) > towar(1).
    /// </summary>
    public class TrainPriorityTests
    {
        static readonly MethodInfo BasePriorityMethod = typeof(TrainRunSimulator).GetMethod(
            "GetIrjBasePriority", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly MethodInfo RushHourMethod = typeof(TrainRunSimulator).GetMethod(
            "IsRushHour", BindingFlags.Static | BindingFlags.NonPublic);

        static int BasePriority(IrjGroup g) => (int)BasePriorityMethod.Invoke(null, new object[] { g });
        static bool IsRushHour() => (bool)RushHourMethod.Invoke(null, null);

        string _dateBackup;
        float _timeBackup;

        [SetUp]
        public void SetUp()
        {
            Assert.That(BasePriorityMethod, Is.Not.Null, "GetIrjBasePriority musi istnieć (reflection).");
            Assert.That(RushHourMethod, Is.Not.Null, "IsRushHour musi istnieć (reflection).");
            _dateBackup = GameState.GameStartDateIso;
            _timeBackup = GameState.GameTimeSeconds;
            GameState.GameDay = 0;
        }

        [TearDown]
        public void TearDown()
        {
            GameState.GameStartDateIso = _dateBackup;
            GameState.GameTimeSeconds = _timeBackup;
        }

        // ── Hierarchia priorytetów IRJ ───────────────────────────────

        [Test]
        public void Priority_International_Highest()
        {
            Assert.That(BasePriority(IrjGroup.ExpressInternational), Is.EqualTo(10));
            Assert.That(BasePriority(IrjGroup.InternationalFast), Is.EqualTo(10));
        }

        [Test]
        public void Priority_Hierarchy_Ordering()
        {
            int intl = BasePriority(IrjGroup.ExpressInternational);
            int express = BasePriority(IrjGroup.ExpressDomestic);
            int fast = BasePriority(IrjGroup.RegionalFast);
            int local = BasePriority(IrjGroup.RegionalLocal);
            int service = BasePriority(IrjGroup.EmptyPassenger);
            int freight = BasePriority(IrjGroup.FreightDomesticMass);

            Assert.That(intl, Is.GreaterThan(express), "Międzynarodowy > ekspres.");
            Assert.That(express, Is.GreaterThan(fast), "Ekspres > pospieszny.");
            Assert.That(fast, Is.GreaterThan(local), "Pospieszny > osobowy.");
            Assert.That(local, Is.GreaterThan(service), "Osobowy > służbowy.");
            Assert.That(service, Is.GreaterThan(freight), "Służbowy > towarowy (najniższy).");
        }

        [Test]
        public void Priority_ExpressDomestic_Is7()
        {
            Assert.That(BasePriority(IrjGroup.ExpressDomestic), Is.EqualTo(7));
        }

        [Test]
        public void Priority_RegionalLocal_BaseIs3()
        {
            // Osobowy bazowo 3 (boostowany do 9 w rush — patrz GetTrainPriority).
            Assert.That(BasePriority(IrjGroup.RegionalLocal), Is.EqualTo(3));
            Assert.That(BasePriority(IrjGroup.RegionalAgglomeration), Is.EqualTo(3));
        }

        [Test]
        public void Priority_Freight_Lowest()
        {
            Assert.That(BasePriority(IrjGroup.FreightDomesticMass), Is.EqualTo(1));
            Assert.That(BasePriority(IrjGroup.FreightIntlIntermodal), Is.EqualTo(1));
        }

        // ── Rush hour ────────────────────────────────────────────────

        [Test]
        public void RushHour_WeekdayMorning_True()
        {
            GameState.GameStartDateIso = "2026-06-01"; // poniedziałek
            GameState.GameDay = 0;
            GameState.GameTimeSeconds = 7 * 3600f; // 07:00 — w oknie 6-9
            Assert.That(IsRushHour(), Is.True, "Pn 07:00 = rush hour poranny.");
        }

        [Test]
        public void RushHour_WeekdayAfternoon_True()
        {
            GameState.GameStartDateIso = "2026-06-01";
            GameState.GameDay = 0;
            GameState.GameTimeSeconds = 14 * 3600f; // 14:00 — w oknie 13-16
            Assert.That(IsRushHour(), Is.True, "Pn 14:00 = rush hour popołudniowy.");
        }

        [Test]
        public void RushHour_WeekdayMidday_False()
        {
            GameState.GameStartDateIso = "2026-06-01";
            GameState.GameDay = 0;
            GameState.GameTimeSeconds = 11 * 3600f; // 11:00 — poza oknami
            Assert.That(IsRushHour(), Is.False, "Pn 11:00 = poza rush hour.");
        }

        [Test]
        public void RushHour_Weekend_AlwaysFalse()
        {
            GameState.GameStartDateIso = "2026-06-06"; // sobota
            GameState.GameDay = 0;
            GameState.GameTimeSeconds = 7 * 3600f; // 07:00 — byłby rush w dzień roboczy
            Assert.That(IsRushHour(), Is.False, "Weekend = brak rush hour mimo godziny szczytu.");
        }
    }
}
