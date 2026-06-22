using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Personnel;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-Modernization MM-5/6/6b: testy RoleCaps — limity zatrudnienia per rola wg lvla pomieszczeń.
    /// Czyste funkcje cap-per-lvl + shared counting (Office+Research dzielą cap) + field roles bez capu.
    /// EditMode. Pełny GetMaxForRole dla Office/Dispatcher wymaga RoomLevelService (scena) — tu pure części.
    /// </summary>
    public class RoleCapsTests
    {
        const int IdBase = 870000;
        readonly List<int> _added = new();

        [TearDown]
        public void TearDown()
        {
            PersonnelService.Employees.RemoveAll(e => _added.Contains(e.employeeId));
            _added.Clear();
        }

        void AddEmployee(EmployeeRole role, EmployeeStatus status = EmployeeStatus.Available)
        {
            int id = IdBase + _added.Count;
            PersonnelService.Employees.Add(new Employee { employeeId = id, role = role, status = status });
            _added.Add(id);
        }

        // ── Cap-per-lvl (czyste) ─────────────────────────────────────

        [Test]
        public void OfficeCapForLvl_MatchesSpec()
        {
            Assert.That(RoleCaps.OfficeCapForLvl(1), Is.EqualTo(2));
            Assert.That(RoleCaps.OfficeCapForLvl(2), Is.EqualTo(4));
            Assert.That(RoleCaps.OfficeCapForLvl(3), Is.EqualTo(6));
            Assert.That(RoleCaps.OfficeCapForLvl(4), Is.EqualTo(8));
            Assert.That(RoleCaps.OfficeCapForLvl(5), Is.EqualTo(12));
        }

        [Test]
        public void OfficeCapForLvl_NoRoom_IsZero()
        {
            Assert.That(RoleCaps.OfficeCapForLvl(0), Is.EqualTo(0), "Brak biura (lvl 0) → 0 (nie można hire biurowych).");
            Assert.That(RoleCaps.OfficeCapForLvl(99), Is.EqualTo(0), "Nieprawidłowy lvl → 0.");
        }

        [Test]
        public void OfficeCap_IsMonotonic()
        {
            Assert.That(RoleCaps.OfficeCapForLvl(1), Is.LessThan(RoleCaps.OfficeCapForLvl(3)));
            Assert.That(RoleCaps.OfficeCapForLvl(3), Is.LessThan(RoleCaps.OfficeCapForLvl(5)),
                "Wyższy lvl biura = większy cap.");
        }

        [Test]
        public void DispatcherAndTrafficCap_OnePerLevel()
        {
            for (int lvl = 1; lvl <= 5; lvl++)
            {
                Assert.That(RoleCaps.DispatcherCapForLvl(lvl), Is.EqualTo(lvl), $"Dispatcher lvl{lvl} → cap {lvl}.");
                Assert.That(RoleCaps.TrafficCapForLvl(lvl), Is.EqualTo(lvl), $"Traffic lvl{lvl} → cap {lvl}.");
            }
            Assert.That(RoleCaps.DispatcherCapForLvl(0), Is.EqualTo(0));
            Assert.That(RoleCaps.TrafficCapForLvl(0), Is.EqualTo(0));
        }

        // ── IsRoleSharedWithOffice ───────────────────────────────────

        [Test]
        public void OfficeAndResearch_ShareOfficeCap()
        {
            Assert.That(RoleCaps.IsRoleSharedWithOffice(EmployeeRole.Office), Is.True);
            Assert.That(RoleCaps.IsRoleSharedWithOffice(EmployeeRole.Research), Is.True,
                "R&D dzieli cap z biurowymi (oba w Office room).");
            Assert.That(RoleCaps.IsRoleSharedWithOffice(EmployeeRole.Dispatcher), Is.False);
            Assert.That(RoleCaps.IsRoleSharedWithOffice(EmployeeRole.Driver), Is.False);
        }

        // ── Field roles — brak capu (niezależnie od sceny) ───────────

        [Test]
        public void FieldRoles_HaveNoCap()
        {
            // Driver/Conductor/Mechanic/Cleaner/WashBay/TicketClerk → int.MaxValue zawsze (default w switch).
            Assert.That(RoleCaps.GetMaxForRole(EmployeeRole.Driver), Is.EqualTo(int.MaxValue));
            Assert.That(RoleCaps.GetMaxForRole(EmployeeRole.Conductor), Is.EqualTo(int.MaxValue));
            Assert.That(RoleCaps.GetMaxForRole(EmployeeRole.Mechanic), Is.EqualTo(int.MaxValue));
            Assert.That(RoleCaps.GetMaxForRole(EmployeeRole.TicketClerk), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void FieldRole_NeverAtCap()
        {
            Assert.That(RoleCaps.IsAtCap(EmployeeRole.Driver), Is.False, "Rola bez capu nigdy nie 'at cap'.");
        }

        // ── Headcount + shared counting ──────────────────────────────

        [Test]
        public void Headcount_OfficeAndResearch_CountedTogether()
        {
            AddEmployee(EmployeeRole.Office);
            AddEmployee(EmployeeRole.Office);
            AddEmployee(EmployeeRole.Research);

            // Office i Research dzielą cap → headcount dla obu ról zwraca łączną sumę (3).
            Assert.That(RoleCaps.GetCurrentHeadcountForRole(EmployeeRole.Office), Is.EqualTo(3));
            Assert.That(RoleCaps.GetCurrentHeadcountForRole(EmployeeRole.Research), Is.EqualTo(3),
                "Office i Research liczone razem (dzielą cap biura).");
        }

        [Test]
        public void Headcount_NonOfficeRole_CountedSeparately()
        {
            AddEmployee(EmployeeRole.Office);
            AddEmployee(EmployeeRole.Driver);
            AddEmployee(EmployeeRole.Driver);

            Assert.That(RoleCaps.GetCurrentHeadcountForRole(EmployeeRole.Driver), Is.EqualTo(2),
                "Driver liczony osobno (nie miesza się z biurowymi).");
        }

        [Test]
        public void Headcount_ExcludesInactiveEmployees()
        {
            AddEmployee(EmployeeRole.Driver, EmployeeStatus.Available);
            AddEmployee(EmployeeRole.Driver, EmployeeStatus.Fired);
            AddEmployee(EmployeeRole.Driver, EmployeeStatus.Retired);

            Assert.That(RoleCaps.GetCurrentHeadcountForRole(EmployeeRole.Driver), Is.EqualTo(1),
                "Fired/Retired nie liczą się do headcount (tylko aktywni).");
        }
    }
}
