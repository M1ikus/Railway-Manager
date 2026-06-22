using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Economy;
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// M-PaxV2 Faza A.2: end-to-end boarding -> cena per KLASA x dystans -> przychod EconomyManager.
    /// Domyka gap #1 (integracja boarding<->ekonomia) na nowym modelu klas. Sprawdza tez limit
    /// pojemnosci PER KLASA (1. klasa ograniczona seatBreakdown). EditMode + debug-hooki PassengerManagera.
    /// </summary>
    public class PassengerBoardingRevenueTests
    {
        const int NodeA = 100, NodeB = 200, StationA = 1, StationB = 2;
        const int Vid = 95001, TtId = 9001, RunId = 9701, CircId = 77;

        PassengerManager _pm;
        GameObject _pmGo;
        float _timeBackup;

        [SetUp]
        public void SetUp()
        {
            _timeBackup = GameState.GameTimeSeconds;
            GameState.GameTimeSeconds = 12f * 3600f;

            EnsureSingleton<EconomyManager>();
            EconomyManager.Instance.ResetRuntime();

            _pmGo = new GameObject("PM_BoardingTest");
            _pm = _pmGo.AddComponent<PassengerManager>(); // brak Awake w EditMode; pola field-init
            _pm.DebugSetStationMapping(new Dictionary<int, int> { { NodeA, StationA }, { NodeB, StationB } });

            // Kategoria z cennikiem per-klasa: 2kl 6+0.1/km, 1kl 12+0.2/km. Na 50 km: 2kl=1100gr, 1kl=2200gr.
            var cat = new CommercialCategory
            {
                id = "ic_test",
                classFares = new List<ClassFare>
                {
                    new ClassFare { zone = SeatZoneType.SecondClassOpen, basePriceZl = 6f,  pricePerKmZl = 0.1f },
                    new ClassFare { zone = SeatZoneType.FirstClassOpen,  basePriceZl = 12f, pricePerKmZl = 0.2f },
                }
            };
            TimetableService.CommercialCategories.Add(cat);

            var tt = new TimetableObj
            {
                id = TtId,
                commercialCategoryId = "ic_test",
                stops = new List<TimetableStop>
                {
                    new TimetableStop { stationNodeId = NodeA, distanceFromStartM = 0f,     stopType = StopType.PH },
                    new TimetableStop { stationNodeId = NodeB, distanceFromStartM = 50000f, stopType = StopType.PH },
                }
            };
            TimetableService.Timetables.Add(tt);

            // Pojazd: 100 miejsc 2. klasy + 2 miejsca 1. klasy.
            FleetService.RemoveOwnedVehicle(Vid);
            FleetService.AddOwnedVehicle(new FleetVehicleData
            {
                id = Vid,
                passengerSeats = 102,
                seatBreakdown = new List<SeatCount>
                {
                    new SeatCount { type = SeatZoneType.SecondClassOpen, count = 100 },
                    new SeatCount { type = SeatZoneType.FirstClassOpen,  count = 2 },
                }
            });
        }

        [TearDown]
        public void TearDown()
        {
            FleetService.RemoveOwnedVehicle(Vid);
            TimetableService.Timetables.RemoveAll(t => t.id == TtId);
            TimetableService.CommercialCategories.RemoveAll(c => c.id == "ic_test");
            EconomyManager.Instance?.ResetRuntime();
            if (_pmGo != null) Object.DestroyImmediate(_pmGo);
            GameState.GameTimeSeconds = _timeBackup;
        }

        [Test]
        public void Board_PerClassFare_CountedInRevenue_AndCapacityRespected()
        {
            var run = new TrainRun { id = RunId, timetableId = TtId, circulationId = CircId, runningVehicleIds = new List<int> { Vid } };

            // 5 pasażerów 2. klasy + 5 chcących 1. klasy (ale tylko 2 miejsca 1. klasy). Portfel duzy.
            _pm.DebugSpawnWaiting(StationA, StationB, SeatZoneType.SecondClassOpen, walletGroszy: 100000, count: 5);
            _pm.DebugSpawnWaiting(StationA, StationB, SeatZoneType.FirstClassOpen,  walletGroszy: 100000, count: 5);

            // Pociąg przyjeżdża na A (stopIndex 0) — wsiadanie do B.
            _pm.DebugSimulateArrival(run, 0, NodeA);

            // 50 km: 2kl=(6+5)×100=1100gr, 1kl=(12+10)×100=2200gr.
            // Boarduje 5×2kl (cap 100 ok) + 2×1kl (cap 2). Przychod = 5×1100 + 2×2200 = 9900gr.
            Assert.That(EconomyManager.Instance.RevenueTodayGroszy, Is.EqualTo(9900L),
                "Przychod = suma cen per-klasa × liczba wsiadlych (limit 1. klasy = 2 miejsca).");

            // 3 pasażerów 1. klasy nie zmiescilo sie (brak miejsc w ich klasie) -> dalej czekaja.
            Assert.That(_pm.CountWaitingAt(StationA), Is.EqualTo(3),
                "Pojemnosc PER KLASA wymuszona — nadmiarowi 1. klasy zostaja na peronie (nie downgrade w A.2).");
        }

        // ── Helpers ──────────────────────────────────────────────────
        static void EnsureSingleton<T>() where T : MonoBehaviour
        {
            if (Resources.FindObjectsOfTypeAll<T>().Length > 0) return;
            var go = new GameObject(typeof(T).Name + "_Test");
            var comp = go.AddComponent<T>();
            typeof(T).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(comp, null);
        }
    }
}
