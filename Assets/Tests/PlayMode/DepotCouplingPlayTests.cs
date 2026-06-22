using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DepotSystem;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// TD-032 PlayMode: fizyczne sprzęganie/rozprzęganie składów w pełnym <see cref="DepotMovementSimulator"/>
    /// na programowo zbudowanym torze. Weryfikuje to, czego EditMode (ConsistCouplingMathTests = czysta
    /// algebra) nie obejmie — integrację occupancy grafu + visuali + FleetConsistData:
    /// - couple: 2 occupanty → 1 (survivor=mover), kolejność nos→tył wg geometrii, span≈suma, B visual znika, FCD scalone,
    /// - decouple: 1 occupant → 2 (front zachowuje id, tail nowy), partycja = oryginał, 2 visuale, FCD split,
    /// - stationary guard (couple/decouple odrzucone w ruchu), 1-pojazd / zły cut,
    /// - wagon-only couple OK ale merged self-move odrzucony (brak napędu),
    /// - save/load 1:1 (occupancy round-trip przez graf → RestoreParkedVisualsFromGraph).
    /// </summary>
    public class DepotCouplingPlayTests
    {
        GameObject _graphGo, _simGo;
        TrackGraph _graph;
        DepotMovementSimulator _sim;
        int _trackId;
        readonly List<int> _seeded = new();

        const int MoverConsist = 601;
        const int BlockerConsist = 600;
        const float TrackLen = 200f;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true; // nographics: spawn cube/material loguje render warningi

            // TD-038: twardy reset izolacji między klasami PlayMode — patrz PlayModeSimTestIsolation.
            // Niszczy wyciekłe DontDestroyOnLoad sim-singletony (DeliveryService itd.) + stale Instance /
            // PauseStack / GameState / Time zanim zbudujemy własny graf+sim. No-op gdy klasa biegnie solo.
            PlayModeSimTestIsolation.HardReset();

            _graphGo = new GameObject("TestTrackGraph");
            _graph = _graphGo.AddComponent<TrackGraph>();

            int nA = _graph.AddNode(new Vector3(0f, 0f, 0f));
            int nB = _graph.AddNode(new Vector3(0f, 0f, TrackLen));
            int edge = _graph.AddEdgeWithPolyline(nA, nB,
                new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, TrackLen) });
            _trackId = _graph.AddTrack("TestTrack", DepotTrackType.Parking, new List<int> { edge });

            _simGo = new GameObject("TestDepotSim");
            _sim = _simGo.AddComponent<DepotMovementSimulator>();
        }

        [TearDown]
        public void TearDown()
        {
            // Najpierw usuń consisty testowe (couple/decouple mogły je modyfikować/dodawać), potem pojazdy.
            var seededSet = new HashSet<int>(_seeded);
            FleetService.RemoveConsists(c => c?.vehicleIds != null && c.vehicleIds.Exists(id => seededSet.Contains(id)));
            foreach (int id in _seeded) FleetService.RemoveOwnedVehicle(id);
            _seeded.Clear();
            if (_simGo != null) Object.DestroyImmediate(_simGo);
            if (_graphGo != null) Object.DestroyImmediate(_graphGo);
            LogAssert.ignoreFailingMessages = false;
        }

        int SeedVehicle(FleetVehicleType type, int powerKw, float lengthM)
        {
            int id = 980000 + _seeded.Count;
            FleetService.AddOwnedVehicle(new FleetVehicleData
            {
                id = id,
                type = type,
                series = type.ToString(),
                powerKw = powerKw,
                lengthM = lengthM,
                supportedTractions = new List<TractionType> { powerKw > 0 ? TractionType.Electric : TractionType.None }
            });
            _seeded.Add(id);
            return id;
        }

        void AddConsist(int consistName, List<int> vehicleIds)
            => FleetService.AddConsist(new FleetConsistData { name = $"Skład {consistName}", vehicleIds = new List<int>(vehicleIds) });

        IEnumerator DriveFixed(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

        // ── COUPLE ───────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Couple_MergesIntoSurvivor_GeometryAndFleet()
        {
            int loco = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            int car = SeedVehicle(FleetVehicleType.PassengerCar, 0, 20f);
            // Mover [0,20] z tyłu, blocker [20,40] z przodu (oba dir+1). Stykają się w 20 m.
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { loco }, 0f, 20f, 1);
            _graph.SetOccupantInterval(_trackId, BlockerConsist, new List<int> { car }, 20f, 40f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);
            AddConsist(1, new List<int> { loco });
            AddConsist(2, new List<int> { car });

            bool ok = _sim.CoupleConsists(MoverConsist, BlockerConsist);
            Assert.That(ok, Is.True, "Couple dwóch stojących na wspólnym torze powinien się powieść.");

            // Occupancy: 1 occupant (survivor=mover), blocker zniknął.
            Assert.That(_graph.GetOccupants(_trackId).Count, Is.EqualTo(1), "Po couple 1 occupant na torze.");
            var m = _graph.GetOccupant(_trackId, MoverConsist);
            Assert.That(m, Is.Not.Null, "Survivor occupant pod id movera.");
            Assert.That(_graph.GetOccupant(_trackId, BlockerConsist), Is.Null, "Blocker occupant usunięty.");
            // Kolejność nos→tył wg geometrii: blocker (nos przy 40) z przodu → mergedIds[0]==car.
            Assert.That(m.VehicleIds, Is.EqualTo(new List<int> { car, loco }), "Kolejność nos→tył = [przedni, tylny].");
            // Span ≈ suma długości (styk domknięty do 0).
            Assert.That(m.RearDistM - m.FrontDistM, Is.EqualTo(40f).Within(0.5f), "Span scalony ≈ suma długości (40 m).");

            // Visuals: blocker zniszczony, survivor obecny.
            Assert.That(_sim.HasConsistVisual(BlockerConsist), Is.False, "Visual blockera zniszczony.");
            Assert.That(_sim.HasConsistVisual(MoverConsist), Is.True, "Visual survivora obecny.");

            // FleetConsistData: jeden scalony skład z oboma pojazdami; osobny skład blockera zniknął.
            int withLoco = 0; FleetConsistData merged = null;
            foreach (var c in FleetService.Consists)
                if (c?.vehicleIds != null && c.vehicleIds.Contains(loco)) { withLoco++; merged = c; }
            Assert.That(withLoco, Is.EqualTo(1), "Dokładnie 1 FleetConsistData zawiera locomotywę.");
            Assert.That(merged.vehicleIds, Is.EquivalentTo(new[] { loco, car }), "Scalony FCD ma oba pojazdy.");
            int singleCar = 0;
            foreach (var c in FleetService.Consists)
                if (c?.vehicleIds != null && c.vehicleIds.Count == 1 && c.vehicleIds.Contains(car)) singleCar++;
            Assert.That(singleCar, Is.EqualTo(0), "Osobny 1-pojazdowy skład blockera usunięty.");
            yield return null;
        }

        // ── DECOUPLE ──────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Decouple_FrontKeepsId_TailNew_PartitionPreserved()
        {
            int v0 = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            int v1 = SeedVehicle(FleetVehicleType.PassengerCar, 0, 20f);
            int v2 = SeedVehicle(FleetVehicleType.PassengerCar, 0, 20f);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { v0, v1, v2 }, 0f, 60f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);
            AddConsist(1, new List<int> { v0, v1, v2 });

            bool ok = _sim.DecoupleConsist(MoverConsist, 2); // front=[v0,v1] (nos), tail=[v2]
            Assert.That(ok, Is.True, "Decouple z prawidłowym cutIndex powinien się powieść.");

            var occs = _graph.GetOccupants(_trackId);
            Assert.That(occs.Count, Is.EqualTo(2), "Po decouple 2 occupanty.");
            var front = _graph.GetOccupant(_trackId, MoverConsist);
            Assert.That(front, Is.Not.Null, "Front zachowuje oryginalny consistId.");
            Assert.That(front.VehicleIds, Is.EqualTo(new List<int> { v0, v1 }), "Front = pojazdy [0..cut) nos→tył.");

            TrackOccupant tail = null;
            foreach (var o in occs) if (o.ConsistId != MoverConsist) tail = o;
            Assert.That(tail, Is.Not.Null, "Tail = nowy occupant.");
            Assert.That(tail.ConsistId, Is.Not.EqualTo(MoverConsist), "Tail dostał nowy consistId.");
            Assert.That(tail.VehicleIds, Is.EqualTo(new List<int> { v2 }), "Tail = pojazdy [cut..).");

            // Partycja = oryginał (suma = {v0,v1,v2}, brak duplikatów).
            var union = new List<int>(front.VehicleIds); union.AddRange(tail.VehicleIds);
            Assert.That(union, Is.EquivalentTo(new[] { v0, v1, v2 }), "Partycja front+tail = oryginalny skład.");

            // Visuals: 2 (front + tail).
            Assert.That(_sim.HasConsistVisual(MoverConsist), Is.True, "Visual front.");
            Assert.That(_sim.HasConsistVisual(tail.ConsistId), Is.True, "Visual tail.");

            // FleetConsistData: front [v0,v1], tail [v2] (osobny nowy skład).
            FleetConsistData fFront = null, fTail = null;
            foreach (var c in FleetService.Consists)
            {
                if (c?.vehicleIds == null) continue;
                if (c.vehicleIds.Contains(v0)) fFront = c;
                else if (c.vehicleIds.Contains(v2)) fTail = c;
            }
            Assert.That(fFront, Is.Not.Null); Assert.That(fTail, Is.Not.Null);
            Assert.That(fFront.vehicleIds, Is.EquivalentTo(new[] { v0, v1 }), "FCD front = [v0,v1].");
            Assert.That(fTail.vehicleIds, Is.EquivalentTo(new[] { v2 }), "FCD tail = [v2].");
            yield return null;
        }

        // ── GUARDY ────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Couple_RejectedWhileMoving()
        {
            int mv = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            int bv = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { mv }, 0f, 20f, 1);
            _graph.SetOccupantInterval(_trackId, BlockerConsist, new List<int> { bv }, 150f, 170f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);

            Assert.That(_sim.EnqueueMove(MoverConsist, new List<int> { mv }, _trackId, _trackId, new Vector3(0f, 0f, 90f)), Is.True);
            yield return DriveFixed(40); // mid-move
            Assert.That(_sim.HasTaskForConsist(MoverConsist), Is.True, "Mover w ruchu.");

            Assert.That(_sim.CoupleConsists(MoverConsist, BlockerConsist), Is.False, "Couple odrzucony gdy mover w ruchu.");
            Assert.That(_graph.GetOccupants(_trackId).Count, Is.EqualTo(2), "Oba składy nietknięte po odrzuceniu.");
        }

        [UnityTest]
        public IEnumerator Decouple_RejectedWhileMoving()
        {
            int v0 = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            int v1 = SeedVehicle(FleetVehicleType.PassengerCar, 0, 20f);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { v0, v1 }, 0f, 40f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);

            Assert.That(_sim.EnqueueMove(MoverConsist, new List<int> { v0, v1 }, _trackId, _trackId, new Vector3(0f, 0f, 120f)), Is.True);
            yield return DriveFixed(40);
            Assert.That(_sim.HasTaskForConsist(MoverConsist), Is.True);

            Assert.That(_sim.DecoupleConsist(MoverConsist, 1), Is.False, "Decouple odrzucony gdy skład w ruchu.");
            Assert.That(_graph.GetOccupants(_trackId).Count, Is.EqualTo(1), "Skład nietknięty po odrzuceniu.");
        }

        [UnityTest]
        public IEnumerator Decouple_RejectsSingleVehicleAndBadCut()
        {
            int v0 = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { v0 }, 0f, 20f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);
            Assert.That(_sim.DecoupleConsist(MoverConsist, 1), Is.False, "1-pojazdowy skład: brak rozprzęgu.");

            int v1 = SeedVehicle(FleetVehicleType.PassengerCar, 0, 20f);
            _graph.SetOccupantInterval(_trackId, BlockerConsist, new List<int> { v0, v1 }, 100f, 140f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);
            Assert.That(_sim.DecoupleConsist(BlockerConsist, 0), Is.False, "cut=0 nieprawidłowy.");
            Assert.That(_sim.DecoupleConsist(BlockerConsist, 2), Is.False, "cut==count nieprawidłowy.");
            Assert.That(_sim.DecoupleConsist(BlockerConsist, 1), Is.True, "cut=1 prawidłowy → rozprzęga.");
            yield return null;
        }

        // ── WAGON-ONLY ────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator WagonOnlyCouple_OK_ButMergedSelfMoveRejected()
        {
            int w0 = SeedVehicle(FleetVehicleType.PassengerCar, 0, 20f);
            int w1 = SeedVehicle(FleetVehicleType.PassengerCar, 0, 20f);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { w0 }, 0f, 20f, 1);
            _graph.SetOccupantInterval(_trackId, BlockerConsist, new List<int> { w1 }, 20f, 40f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);

            Assert.That(_sim.CoupleConsists(MoverConsist, BlockerConsist), Is.True,
                "Sprzęg 2 wagonów OK (couple nie wymaga napędu — to manewr ręczny/lokomotywą).");
            var m = _graph.GetOccupant(_trackId, MoverConsist);
            Assert.That(m, Is.Not.Null);
            Assert.That(m.VehicleIds.Count, Is.EqualTo(2));

            Assert.That(_sim.EnqueueMove(MoverConsist, m.VehicleIds, _trackId, _trackId, new Vector3(0f, 0f, 120f)), Is.False,
                "Sprzęgnięte wagony bez napędu nie pojadą same (reguła napędu TD-031).");
            yield return null;
        }

        // ── SAVE / LOAD 1:1 ───────────────────────────────────────────

        [UnityTest]
        public IEnumerator SaveLoad_AfterCouple_OneOccupant_Preserved()
        {
            int loco = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            int car = SeedVehicle(FleetVehicleType.PassengerCar, 0, 20f);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { loco }, 0f, 20f, 1);
            _graph.SetOccupantInterval(_trackId, BlockerConsist, new List<int> { car }, 20f, 40f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);
            Assert.That(_sim.CoupleConsists(MoverConsist, BlockerConsist), Is.True);

            var before = _graph.GetOccupant(_trackId, MoverConsist);
            float f = before.FrontDistM, r = before.RearDistM;
            var ids = new List<int>(before.VehicleIds);

            // „save→load": occupancy (footprint) trwa w grafie = persisted; Restore = ścieżka load (rebuild visuali).
            _sim.RestoreParkedVisualsFromGraph(_graph);

            Assert.That(_graph.GetOccupants(_trackId).Count, Is.EqualTo(1), "Po load nadal 1 occupant (scalony).");
            var after = _graph.GetOccupant(_trackId, MoverConsist);
            Assert.That(after, Is.Not.Null);
            Assert.That(after.FrontDistM, Is.EqualTo(f).Within(0.01f), "Footprint front 1:1 po load.");
            Assert.That(after.RearDistM, Is.EqualTo(r).Within(0.01f), "Footprint rear 1:1 po load.");
            Assert.That(after.VehicleIds, Is.EqualTo(ids), "Kolejność pojazdów 1:1 po load.");
            Assert.That(_sim.HasConsistVisual(MoverConsist), Is.True, "Visual scalonego po load.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SaveLoad_AfterDecouple_TwoOccupants_Preserved()
        {
            int v0 = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            int v1 = SeedVehicle(FleetVehicleType.PassengerCar, 0, 20f);
            int v2 = SeedVehicle(FleetVehicleType.PassengerCar, 0, 20f);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { v0, v1, v2 }, 0f, 60f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);
            Assert.That(_sim.DecoupleConsist(MoverConsist, 2), Is.True);

            var occsBefore = _graph.GetOccupants(_trackId);
            Assert.That(occsBefore.Count, Is.EqualTo(2));
            // Snapshot footprintów po consistId.
            var snap = new Dictionary<int, (float f, float r, int count)>();
            foreach (var o in occsBefore) snap[o.ConsistId] = (o.FrontDistM, o.RearDistM, o.VehicleIds.Count);

            _sim.RestoreParkedVisualsFromGraph(_graph);

            var occsAfter = _graph.GetOccupants(_trackId);
            Assert.That(occsAfter.Count, Is.EqualTo(2), "Po load nadal 2 occupanty (rozprzęgnięte).");
            foreach (var o in occsAfter)
            {
                Assert.That(snap.ContainsKey(o.ConsistId), $"Occupant #{o.ConsistId} zachowany po load.");
                var s = snap[o.ConsistId];
                Assert.That(o.FrontDistM, Is.EqualTo(s.f).Within(0.01f), $"#{o.ConsistId} front 1:1.");
                Assert.That(o.RearDistM, Is.EqualTo(s.r).Within(0.01f), $"#{o.ConsistId} rear 1:1.");
                Assert.That(o.VehicleIds.Count, Is.EqualTo(s.count), $"#{o.ConsistId} liczba pojazdów 1:1.");
                Assert.That(_sim.HasConsistVisual(o.ConsistId), Is.True, $"Visual #{o.ConsistId} po load.");
            }
            yield return null;
        }

        // ── COUPLE-ADJACENT (post-load, brak eventu dojazdu) ──────────

        [UnityTest]
        public IEnumerator FindAdjacent_AndCouple_AfterLoadTouching()
        {
            int a = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            int b = SeedVehicle(FleetVehicleType.PassengerCar, 0, 20f);
            // Dwa OSOBNE składy stykające się (jak po wczytaniu save'a): [0,20] + [20,40], oba stoją.
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { a }, 0f, 20f, 1);
            _graph.SetOccupantInterval(_trackId, BlockerConsist, new List<int> { b }, 20f, 40f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);

            // FindAdjacent znajduje sąsiada symetrycznie.
            Assert.That(_sim.FindAdjacentCouplableConsist(MoverConsist), Is.EqualTo(BlockerConsist));
            Assert.That(_sim.FindAdjacentCouplableConsist(BlockerConsist), Is.EqualTo(MoverConsist));

            // Ręczny couple (selected=mover survivor) → 1 occupant.
            Assert.That(_sim.CoupleConsists(MoverConsist, BlockerConsist), Is.True);
            Assert.That(_graph.GetOccupants(_trackId).Count, Is.EqualTo(1));
            // Po sprzęgu brak sąsiada do dalszego łączenia.
            Assert.That(_sim.FindAdjacentCouplableConsist(MoverConsist), Is.EqualTo(-1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FindAdjacent_NoneWhenFarApart()
        {
            int a = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            int b = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { a }, 0f, 20f, 1);
            _graph.SetOccupantInterval(_trackId, BlockerConsist, new List<int> { b }, 100f, 120f, 1); // daleko
            _sim.RestoreParkedVisualsFromGraph(_graph);
            Assert.That(_sim.FindAdjacentCouplableConsist(MoverConsist), Is.EqualTo(-1),
                "Składy oddalone (gap 80 m) — brak sąsiada do sprzęgu.");
            yield return null;
        }
    }
}
