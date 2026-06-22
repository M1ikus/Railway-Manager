using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;
using UnityEngine;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M9c-D F6: testy TrainRunSimulator.ShouldStart — decyzja czy auto-spawnować kurs.
    /// KRYTYCZNE dla audytu F6: pojazd niedostarczony (InProduction/InTransit/AwaitingPickup)
    /// MUSI blokować spawn (inaczej pociąg "teleportuje się" do home mimo że taboru fizycznie nie ma).
    /// Pojazd InDepot blokuje + akumuluje delay (czeka na handshake "Wyjedź z depot").
    ///
    /// ShouldStart jest private → reflection (wzorzec z VehicleLocationServiceTests/GameClock).
    /// Czysta funkcja decyzyjna zależna od GameState/Fleet/VehicleLocation/Circulation — bez grafu.
    /// </summary>
    public class TrainRunShouldStartTests
    {
        const int Vid = 980001;
        const int CircId = 970001;
        const string Today = "2026-03-15";

        long _moneyBackup;
        float _timeBackup, _scaleBackup;
        string _startDateBackup;
        int _dayBackup;

        TrainRunSimulator _sim;
        MethodInfo _shouldStart;

        [SetUp]
        public void SetUp()
        {
            _moneyBackup = GameState.Money;
            _timeBackup = GameState.GameTimeSeconds;
            _scaleBackup = GameState.TimeScale;
            _startDateBackup = GameState.GameStartDateIso;
            _dayBackup = GameState.GameDay;

            GameState.GameStartDateIso = Today;
            GameState.GameDay = 0;
            GameState.GameTimeSeconds = 12 * 3600f; // 12:00 — dobrze po odjeździe testowego kursu (08:00)

            DestroyExisting<VehicleLocationService>();
            DestroyExisting<TrainRunSimulator>();

            // VehicleLocationService ma lekki Awake (tylko Instance=this) — OK w EditMode.
            CreateServiceWithAwake<VehicleLocationService>();
            VehicleLocationService.ResetAll();

            // TrainRunSimulator.Awake jest CIĘŻKI (DispatchService.DontDestroyOnLoad rzuca w EditMode)
            // → tworzymy komponent BEZ Awake. ShouldStart nie dotyka pól instancji (czyta tylko
            // statyczne serwisy: GameState/FleetService/VehicleLocationService/CirculationService),
            // więc reflection-invoke działa na niezainicjalizowanej instancji.
            var go = new GameObject("TrainRunSimulator_Test");
            go.SetActive(false); // zapobiega wywołaniu Awake przez Unity przy AddComponent
            _sim = go.AddComponent<TrainRunSimulator>();

            _shouldStart = typeof(TrainRunSimulator).GetMethod(
                "ShouldStart", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(_shouldStart, Is.Not.Null, "ShouldStart(TrainRun,string) musi istnieć (reflection).");

            FleetService.RemoveOwnedVehicle(Vid);
            CleanupCirculation();
        }

        [TearDown]
        public void TearDown()
        {
            FleetService.RemoveOwnedVehicle(Vid);
            CleanupCirculation();
            VehicleLocationService.ResetAll();
            DestroyExisting<TrainRunSimulator>();
            DestroyExisting<VehicleLocationService>();

            GameState.Money = _moneyBackup;
            GameState.GameTimeSeconds = _timeBackup;
            GameState.TimeScale = _scaleBackup;
            GameState.GameStartDateIso = _startDateBackup;
            GameState.GameDay = _dayBackup;
        }

        bool ShouldStart(TrainRun tr) => (bool)_shouldStart.Invoke(_sim, new object[] { tr, Today });

        /// <summary>Tworzy obieg z 1 pojazdem przypisanym na dziś + TrainRun startujący 08:00.</summary>
        TrainRun SetupRunWithVehicle(FleetVehicleStatus vehicleStatus, VehicleLocationType? loc)
        {
            var v = new FleetVehicleData
            {
                id = Vid, series = "TEST", type = FleetVehicleType.EMU, status = vehicleStatus,
                supportedTractions = new List<TractionType> { TractionType.Electric }
            };
            FleetService.AddOwnedVehicle(v);

            var circ = new Circulation { id = CircId, name = "TestCirc", status = CirculationStatus.Active };
            circ.vehicleAssignmentsPerDay[Today] = new List<int> { Vid };
            CirculationService.Circulations.Add(circ);

            if (loc.HasValue)
            {
                var svc = VehicleLocationService.Instance;
                switch (loc.Value)
                {
                    case VehicleLocationType.InDepot: svc.SetInDepot(Vid, -1); break;
                    case VehicleLocationType.AtStation: svc.SetAtStation(Vid, 100, Vector2.zero); break;
                    default: break;
                }
            }

            return new TrainRun
            {
                id = 960001, timetableId = 1, circulationId = CircId, circulationStepIndex = 0,
                runDateIso = Today, startMinutesFromMidnight = 8 * 60, // 08:00
                trainNumberSnapshot = "TEST-1"
            };
        }

        [Test]
        public void InProductionVehicle_BlocksSpawn_NoDelayAccrued()
        {
            var run = SetupRunWithVehicle(FleetVehicleStatus.InProduction, loc: null);
            int delayBefore = run.currentDelaySec;

            bool result = ShouldStart(run);

            Assert.That(result, Is.False, "Pojazd w produkcji → kurs NIE startuje (brak fizycznego taboru).");
            Assert.That(run.currentDelaySec, Is.EqualTo(delayBefore),
                "Niedostarczony pojazd to nie opóźnienie ruchu — delay NIE akumuluje się.");
        }

        [Test]
        public void InTransitVehicle_BlocksSpawn()
        {
            var run = SetupRunWithVehicle(FleetVehicleStatus.InTransit, loc: null);
            Assert.That(ShouldStart(run), Is.False, "Pojazd w dostawie → kurs nie startuje.");
        }

        [Test]
        public void AwaitingPickupVehicle_BlocksSpawn()
        {
            var run = SetupRunWithVehicle(FleetVehicleStatus.AwaitingPickup, loc: null);
            Assert.That(ShouldStart(run), Is.False, "Pojazd oczekujący na odbiór → kurs nie startuje.");
        }

        [Test]
        public void InDepotVehicle_BlocksSpawn_AccruesDelay()
        {
            // Pojazd dostarczony (StoppedInDepot) ale fizycznie InDepot → czeka na handshake
            // "Wyjedź z depot", delay rośnie (propagacja do następnych kroków obiegu).
            var run = SetupRunWithVehicle(FleetVehicleStatus.StoppedInDepot, loc: VehicleLocationType.InDepot);

            bool result = ShouldStart(run);

            Assert.That(result, Is.False, "Pojazd InDepot → kurs czeka na wyjazd z depot.");
            Assert.That(run.currentDelaySec, Is.GreaterThan(0),
                "InDepot = realne opóźnienie odjazdu → delay akumuluje się (propagacja kaskady).");
        }

        [Test]
        public void DeliveredVehicleAtStation_AllowsSpawn()
        {
            // Pojazd dostarczony i AtStation (na peronie home) → kurs może wystartować.
            var run = SetupRunWithVehicle(FleetVehicleStatus.StoppedOnMap, loc: VehicleLocationType.AtStation);
            Assert.That(ShouldStart(run), Is.True,
                "Pojazd fizycznie na stacji → auto-spawn dozwolony.");
        }

        [Test]
        public void BeforeDepartureTime_DoesNotStart()
        {
            var run = SetupRunWithVehicle(FleetVehicleStatus.StoppedOnMap, loc: VehicleLocationType.AtStation);
            GameState.GameTimeSeconds = 6 * 3600f; // 06:00 — przed odjazdem 08:00
            Assert.That(ShouldStart(run), Is.False, "Przed godziną odjazdu kurs nie startuje.");
        }

        // ── Helpers ──────────────────────────────────────────────────

        static void CleanupCirculation()
        {
            var c = CirculationService.GetCirculation(CircId);
            if (c != null) CirculationService.Circulations.Remove(c);
        }

        static T CreateServiceWithAwake<T>() where T : MonoBehaviour
        {
            var go = new GameObject(typeof(T).Name + "_Test");
            var comp = go.AddComponent<T>();
            typeof(T).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(comp, null);
            return comp;
        }

        static void DestroyExisting<T>() where T : MonoBehaviour
        {
            foreach (var c in Resources.FindObjectsOfTypeAll<T>())
                Object.DestroyImmediate(c.gameObject);
        }
    }
}
