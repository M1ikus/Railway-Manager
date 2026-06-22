using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using DepotSystem;
using RailwayManager.Personnel;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M8: testy "czy dyspozytor odprawia wszystkich" + "czy dyżurny steruje ruchem na depot".
    /// EditMode. Logika decyzyjna obu serwisów (capacity, workload status, priorytety) — bez sceny.
    ///
    /// Dyspozytor (DispatcherService): capacity = 50 + 5×(skill-1) per dyspozytor. Status wg headcount/
    /// capacity — Normal (odprawia wszystkich instant), Delayed (1-1.5× opóźnienia), Critical (>1.5× część missed).
    /// Dyżurny (TrafficControlService): instaluje się jako DepotMovementSimulator.PriorityProvider gdy aktywny;
    /// priorytetyzuje wyjazdy (80) nad parking (40). Brak dyżurnego → provider null (FCFS).
    /// </summary>
    public class PersonnelDispatchTests
    {
        const int IdBase = 860000;
        readonly List<int> _added = new();

        [SetUp]
        public void SetUp()
        {
            DestroyTraffic();
            _added.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            PersonnelService.Employees.RemoveAll(e => _added.Contains(e.employeeId));
            _added.Clear();
            DestroyTraffic();
            DepotMovementSimulator.PriorityProvider = null;
        }

        static void DestroyTraffic()
        {
            foreach (var t in Resources.FindObjectsOfTypeAll<TrafficControlService>())
                Object.DestroyImmediate(t.gameObject);
        }

        void Add(EmployeeRole role, int skill = 1, EmployeeStatus status = EmployeeStatus.Available)
        {
            int id = IdBase + _added.Count;
            PersonnelService.Employees.Add(new Employee { employeeId = id, role = role, skill = skill, status = status });
            _added.Add(id);
        }

        // ── Capacity formulas (RoleDefinitions) ──────────────────────

        [Test]
        public void DispatcherCapacity_ScalesWithSkill()
        {
            Assert.That(RoleDefinitions.GetDispatcherCapacity(1), Is.EqualTo(50), "1★ dyspozytor odprawia 50.");
            Assert.That(RoleDefinitions.GetDispatcherCapacity(5), Is.EqualTo(70), "5★ → 50+4×5 = 70.");
            Assert.That(RoleDefinitions.GetDispatcherCapacity(5),
                Is.GreaterThan(RoleDefinitions.GetDispatcherCapacity(1)), "Wyższy skill = większa przepustowość.");
        }

        [Test]
        public void TrafficControllerCapacity_ScalesWithSkill()
        {
            Assert.That(RoleDefinitions.GetTrafficControllerCapacity(1), Is.EqualTo(10));
            Assert.That(RoleDefinitions.GetTrafficControllerCapacity(5), Is.EqualTo(30), "5★ → 10+4×5 = 30.");
        }

        [Test]
        public void Dispatcher_HandlesMoreThanTrafficController()
        {
            // Dyspozytor odprawia całą firmę (50), dyżurny tylko manewry w depot (10).
            Assert.That(RoleDefinitions.GetDispatcherCapacity(1),
                Is.GreaterThan(RoleDefinitions.GetTrafficControllerCapacity(1)));
        }

        // ── Dyspozytor: czy odprawia wszystkich (ComputeStatus) ──────

        static string DispatcherStatus(int capacity, int headcount, int dispatcherCount)
        {
            var m = typeof(DispatcherService).GetMethod("ComputeStatus", BindingFlags.Static | BindingFlags.NonPublic);
            return m.Invoke(null, new object[] { capacity, headcount, dispatcherCount }).ToString();
        }

        [Test]
        public void Dispatcher_NoDispatcher_Critical()
        {
            Assert.That(DispatcherStatus(0, 30, 0), Is.EqualTo("Critical"),
                "Brak dyspozytora → nikt nie odprawiony (Critical).");
        }

        [Test]
        public void Dispatcher_CapacitySufficient_Normal()
        {
            // capacity 50 ≥ headcount 30 → Normal (odprawia wszystkich instant).
            Assert.That(DispatcherStatus(50, 30, 1), Is.EqualTo("Normal"));
        }

        [Test]
        public void Dispatcher_SlightOverload_Delayed()
        {
            // headcount 60 / capacity 50 = 1.2 (≤1.5) → Delayed (odprawa z opóźnieniem).
            Assert.That(DispatcherStatus(50, 60, 1), Is.EqualTo("Delayed"));
        }

        [Test]
        public void Dispatcher_HeavyOverload_Critical()
        {
            // headcount 100 / capacity 50 = 2.0 (>1.5) → Critical (część akcji missed — NIE wszyscy odprawieni).
            Assert.That(DispatcherStatus(50, 100, 1), Is.EqualTo("Critical"));
        }

        [Test]
        public void Dispatcher_TotalCapacity_SumsActiveDispatchers()
        {
            Add(EmployeeRole.Dispatcher, skill: 1);            // 50
            Add(EmployeeRole.Dispatcher, skill: 5);            // 70
            Add(EmployeeRole.Dispatcher, skill: 1, status: EmployeeStatus.Fired); // nie liczy
            Assert.That(DispatcherService.GetTotalCapacity(), Is.EqualTo(120),
                "Suma capacity aktywnych dyspozytorów (50+70), Fired wykluczony.");
        }

        // ── Dyżurny: czy steruje ruchem (priority + provider) ────────

        static string TrafficStatus(int capacity, int activeTasks, int controllerCount)
        {
            var m = typeof(TrafficControlService).GetMethod("ComputeStatus", BindingFlags.Static | BindingFlags.NonPublic);
            return m.Invoke(null, new object[] { capacity, activeTasks, controllerCount }).ToString();
        }

        [Test]
        public void Traffic_NoController_Critical()
        {
            Assert.That(TrafficStatus(0, 0, 0), Is.EqualTo("Critical"),
                "Brak dyżurnego → Critical (fallback FCFS w simulatorze).");
        }

        [Test]
        public void Traffic_WithinCapacity_Normal()
        {
            Assert.That(TrafficStatus(10, 5, 1), Is.EqualTo("Normal"), "5 manewrów / cap 10 → Normal.");
        }

        [Test]
        public void Traffic_TotalCapacity_SumsActiveControllers()
        {
            Add(EmployeeRole.TrafficController, skill: 1);  // 10
            Add(EmployeeRole.TrafficController, skill: 5);  // 30
            Assert.That(TrafficControlService.GetTotalCapacity(), Is.EqualTo(40));
            Assert.That(TrafficControlService.GetActiveControllers().Count, Is.EqualTo(2));
        }

        [Test]
        public void Traffic_PrioritizesDeparturesOverParking()
        {
            var svc = CreateTraffic();
            var departure = new DepotMoveTask { exitAfterComplete = true };  // wyjazd rozkładowy
            var parking = new DepotMoveTask { exitAfterComplete = false };   // przestawienie

            int depPrio = svc.ComputePriority(departure);
            int parkPrio = svc.ComputePriority(parking);
            Assert.That(depPrio, Is.GreaterThan(parkPrio),
                "Dyżurny daje wyższy priorytet wyjazdom rozkładowym niż przestawieniom parkingowym.");
        }

        [Test]
        public void Traffic_InstallsAsProvider_WhenControllerActive()
        {
            // Dyżurny w firmie → TrafficControlService instaluje się jako provider priorytetów ruchu.
            Add(EmployeeRole.TrafficController, skill: 1);
            var svc = CreateTraffic(); // Awake → InstallAsProvider widzi aktywnego controllera

            Assert.That(DepotMovementSimulator.PriorityProvider, Is.SameAs(svc),
                "Z aktywnym dyżurnym — to on steruje priorytetami manewrów.");
        }

        [Test]
        public void Traffic_NoProvider_WhenNoController()
        {
            // Brak dyżurnego → provider null → simulator używa FCFS (kto pierwszy ten lepszy).
            CreateTraffic(); // Awake → InstallAsProvider, brak controllerów
            Assert.That(DepotMovementSimulator.PriorityProvider, Is.Null,
                "Bez dyżurnego ruch idzie FCFS (provider null).");
        }

        static TrafficControlService CreateTraffic()
        {
            // W EditMode AddComponent NIE wywołuje Awake (to callback play-mode) → wymuszamy
            // przez reflection, żeby InstallAsProvider się wykonał (jak w innych testach MonoBehaviour).
            var go = new GameObject("TrafficControlService_Test");
            var svc = go.AddComponent<TrafficControlService>();
            typeof(TrafficControlService).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(svc, null);
            return svc;
        }
    }
}
