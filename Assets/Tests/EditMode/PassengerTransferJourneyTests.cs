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
    /// M-PaxV2 Faza C.2c: end-to-end PRZESIADKA. Pasażer A→C bez bezpośredniego kursu jedzie
    /// A→B (kurs „os") z przesiadką w B na B→C (kurs „ic"). Sprawdza: maszynę stanów (board →
    /// alight-na-węźle = transfer + WaitingAtStation → board leg2 → Arrived) ORAZ through-fare
    /// (base RAZ + per-km per odcinek) z podziałem przychodu per obieg. EditMode + debug-hooki.
    /// </summary>
    public class PassengerTransferJourneyTests
    {
        const int NodeA = 100, NodeB = 200, NodeC = 300;
        const int StA = 1, StB = 2, StC = 3;
        const int Vid1 = 96001, Vid2 = 96002;
        const int TtA = 9101, TtB = 9102;
        const int RunA = 9801, RunB = 9802, CircA = 81, CircB = 82;

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

            _pmGo = new GameObject("PM_TransferTest");
            _pm = _pmGo.AddComponent<PassengerManager>();

            var nodeToStation = new Dictionary<int, int> { { NodeA, StA }, { NodeB, StB }, { NodeC, StC } };
            _pm.DebugSetStationMapping(nodeToStation);

            // Kategorie (stawka 2. klasy = base+perKm na poziomie kategorii, GetClassRate fallback):
            // os: 6 + 0.1/km; ic: 12 + 0.2/km.
            TimetableService.CommercialCategories.Add(new CommercialCategory { id = "os", basePriceZl = 6f,  pricePerKmZl = 0.1f });
            TimetableService.CommercialCategories.Add(new CommercialCategory { id = "ic", basePriceZl = 12f, pricePerKmZl = 0.2f });

            // TtA: A→B 30 km (os). TtB: B→C 40 km (ic).
            var ttA = new TimetableObj
            {
                id = TtA, commercialCategoryId = "os",
                stops = new List<TimetableStop>
                {
                    new TimetableStop { stationNodeId = NodeA, distanceFromStartM = 0f,     stopType = StopType.PH },
                    new TimetableStop { stationNodeId = NodeB, distanceFromStartM = 30000f, stopType = StopType.PH },
                }
            };
            var ttB = new TimetableObj
            {
                id = TtB, commercialCategoryId = "ic",
                stops = new List<TimetableStop>
                {
                    new TimetableStop { stationNodeId = NodeB, distanceFromStartM = 0f,     stopType = StopType.PH },
                    new TimetableStop { stationNodeId = NodeC, distanceFromStartM = 40000f, stopType = StopType.PH },
                }
            };
            TimetableService.Timetables.Add(ttA);
            TimetableService.Timetables.Add(ttB);

            // Graf osiągalności z rozkładów (normalnie TryBuildODMatrix; w EditMode wstrzykujemy).
            var graph = JourneyGraphBuilder.Build(new[] { ttA, ttB }, nodeToStation);
            _pm.DebugSetReachGraph(graph);

            FleetService.RemoveOwnedVehicle(Vid1);
            FleetService.RemoveOwnedVehicle(Vid2);
            FleetService.AddOwnedVehicle(MakeVehicle(Vid1));
            FleetService.AddOwnedVehicle(MakeVehicle(Vid2));
        }

        [TearDown]
        public void TearDown()
        {
            FleetService.RemoveOwnedVehicle(Vid1);
            FleetService.RemoveOwnedVehicle(Vid2);
            TimetableService.Timetables.RemoveAll(t => t.id == TtA || t.id == TtB);
            TimetableService.CommercialCategories.RemoveAll(c => c.id == "os" || c.id == "ic");
            EconomyManager.Instance?.ResetRuntime();
            if (_pmGo != null) Object.DestroyImmediate(_pmGo);
            GameState.GameTimeSeconds = _timeBackup;
        }

        [Test]
        public void Transfer_TwoLegs_ThroughFareSplitPerCirculation_AndArrives()
        {
            var runA = new TrainRun { id = RunA, timetableId = TtA, circulationId = CircA, runningVehicleIds = new List<int> { Vid1 } };
            var runB = new TrainRun { id = RunB, timetableId = TtB, circulationId = CircB, runningVehicleIds = new List<int> { Vid2 } };

            // 1 pasażer A→C, 2. klasa, duży portfel. Brak bezpośredniego kursu → przesiadka w B.
            _pm.DebugSpawnWaiting(StA, StC, SeatZoneType.SecondClassOpen, walletGroszy: 100000, count: 1);

            // ── Krok 1: kurs „os" na A — wsiada na odcinek 1 (A→B). ──
            _pm.DebugSimulateArrival(runA, 0, NodeA);
            // Through-fare: base raz = max(6,12)=12 (ic/leg2). leg1 = per-km 0.1×30 = 3 zł = 300 gr.
            Assert.That(_pm.CountWaitingAt(StA), Is.EqualTo(0), "Wsiadł na A (odcinek 1).");
            Assert.That(LineRev(CircA), Is.EqualTo(300), "Obieg A (os): wkład odcinka 1 = per-km 30 km = 300 gr.");

            // ── Krok 2: kurs „os" na B (terminal) — wysiada = PRZESIADKA, czeka w B. ──
            _pm.DebugSimulateArrival(runA, 1, NodeB);
            Assert.That(_pm.CountWaitingAt(StB), Is.EqualTo(1), "Przesiadka: czeka w B na odcinek 2.");

            // ── Krok 3: kurs „ic" na B — wsiada na odcinek 2 (B→C). ──
            _pm.DebugSimulateArrival(runB, 0, NodeB);
            Assert.That(_pm.CountWaitingAt(StB), Is.EqualTo(0), "Wsiadł na B (odcinek 2).");
            // leg2 = per-km 0.2×40 = 8 zł + base 12 (najwyższy, raz) = 20 zł = 2000 gr.
            Assert.That(LineRev(CircB), Is.EqualTo(2000), "Obieg B (ic): wkład odcinka 2 = per-km 40 km + base raz = 2000 gr.");

            // ── Krok 4: kurs „ic" na C (terminal) — DOTARŁ. ──
            _pm.DebugSimulateArrival(runB, 1, NodeC);

            // Suma = through-fare (2300 gr); split: A=300, B=2000. Spójność: ile zapłacił == ile do obiegów.
            Assert.That(EconomyManager.Instance.RevenueTodayGroszy, Is.EqualTo(2300L),
                "Through-fare całość = 2300 gr (base raz 12 + per-km 3+8).");
            Assert.That(LineRev(CircA) + LineRev(CircB), Is.EqualTo(2300),
                "Przychód podzielony na 2 obiegi sumuje się do through-fare.");
        }

        // ── Helpers ──────────────────────────────────────────────────
        static long LineRev(int circId)
            => EconomyManager.Instance.LineBalances.TryGetValue(circId, out var lb) ? lb.revenueGroszy : 0L;

        static FleetVehicleData MakeVehicle(int id) => new FleetVehicleData
        {
            id = id,
            passengerSeats = 20,
            seatBreakdown = new List<SeatCount> { new SeatCount { type = SeatZoneType.SecondClassOpen, count = 20 } },
        };

        static void EnsureSingleton<T>() where T : MonoBehaviour
        {
            if (Resources.FindObjectsOfTypeAll<T>().Length > 0) return;
            var go = new GameObject(typeof(T).Name + "_Test");
            var comp = go.AddComponent<T>();
            typeof(T).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(comp, null);
        }
    }
}
