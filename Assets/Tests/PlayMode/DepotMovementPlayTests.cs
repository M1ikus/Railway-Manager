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
    /// TD-031 PlayMode: realny ruch w zajezdni (kinematyka + zajętość pozycyjna) na minimalnym,
    /// zbudowanym programowo torze, w pełnym <see cref="DepotMovementSimulator"/>. Weryfikuje to,
    /// czego EditMode nie obejmie:
    /// - konwencja center ± L/2: dojazd do styku ≈ ContactGapM, ZERO przenikania,
    /// - normalny dojazd do celu gracza (brak blokera → stop na celu),
    /// - reguła napędu (skład bez napędu odrzucony).
    /// Graf wstrzykiwany przez RestoreParkedVisualsFromGraph; ruch napędzany WaitForFixedUpdate.
    /// </summary>
    public class DepotMovementPlayTests
    {
        GameObject _graphGo, _simGo;
        TrackGraph _graph;
        DepotMovementSimulator _sim;
        int _trackId;
        Vector3 _endWorld;
        readonly List<int> _seeded = new();

        const int MoverConsist = 501;
        const int BlockerConsist = 500;
        const float TrackLen = 150f;

        [SetUp]
        public void SetUp()
        {
            // nographics: tworzenie materiałów/cube'ów loguje warningi/errory renderowe — nieistotne,
            // asercje są na zajętości grafu (logika), nie na renderze.
            LogAssert.ignoreFailingMessages = true;

            // TD-038: twardy reset izolacji między klasami PlayMode. DepotEntryTests ładuje Depot.unity i
            // zostawia rój DontDestroyOnLoad sim-singletonów (zwł. DeliveryService) który dalej tyka Update
            // i parkuje wyciekłą flotę startową na DepotMovementSimulator.Instance NASZEGO świeżego sim →
            // fantomowe occupanty na torze testu → mover nie dojeżdża. HardReset niszczy wyciekłe singletony
            // + zeruje stale Instance / PauseStack / GameState / Time. No-op gdy klasa biegnie solo.
            PlayModeSimTestIsolation.HardReset();

            _graphGo = new GameObject("TestTrackGraph");
            _graph = _graphGo.AddComponent<TrackGraph>();

            // Prosty prosty tor 0 → 150 m wzdłuż +Z (2 node'y, 1 krawędź, 1 tor).
            int nA = _graph.AddNode(new Vector3(0f, 0f, 0f));
            int nB = _graph.AddNode(new Vector3(0f, 0f, TrackLen));
            int edge = _graph.AddEdgeWithPolyline(nA, nB,
                new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, TrackLen) });
            _trackId = _graph.AddTrack("TestTrack", DepotTrackType.Parking, new List<int> { edge });
            _endWorld = new Vector3(0f, 0f, TrackLen);

            _simGo = new GameObject("TestDepotSim");
            _sim = _simGo.AddComponent<DepotMovementSimulator>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (int id in _seeded) FleetService.RemoveOwnedVehicle(id);
            _seeded.Clear();
            if (_simGo != null) Object.DestroyImmediate(_simGo);
            if (_graphGo != null) Object.DestroyImmediate(_graphGo);
            LogAssert.ignoreFailingMessages = false;
        }

        int SeedVehicle(FleetVehicleType type, int powerKw, float lengthM)
        {
            int id = 970000 + _seeded.Count;
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

        IEnumerator DriveUntilDone(int consistId, int maxFixed = 8000)
        {
            int i = 0;
            while (_sim.HasTaskForConsist(consistId) && i < maxFixed)
            {
                yield return new WaitForFixedUpdate();
                i++;
            }
        }

        [UnityTest]
        public IEnumerator MoverStopsAtContactBehindBlocker()
        {
            int bVeh = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);

            // Blocker A stoi przy [120,140]; Mover B parkuje przy [0,20] (visual disambiguuje kierunek).
            _graph.SetOccupantInterval(_trackId, BlockerConsist, new List<int> { 999999 }, 120f, 140f, 1);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { bVeh }, 0f, 20f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph); // inject graf + spawn visuali z occupancy

            bool ok = _sim.EnqueueMove(MoverConsist, new List<int> { bVeh }, _trackId, _trackId, _endWorld);
            Assert.That(ok, Is.True, "EnqueueMove powinno przyjąć ruch składu z napędem.");

            yield return DriveUntilDone(MoverConsist);

            Assert.That(_sim.HasTaskForConsist(MoverConsist), Is.False,
                "Mover powinien zakończyć ruch przy styku za stojącym blokerem.");

            var a = _graph.GetOccupant(_trackId, BlockerConsist);
            var b = _graph.GetOccupant(_trackId, MoverConsist);
            Assert.That(a, Is.Not.Null, "Blocker occupant istnieje.");
            Assert.That(b, Is.Not.Null, "Mover occupant istnieje.");

            float gap = a.FrontDistM - b.RearDistM; // luka między nosem B a krawędzią A
            Assert.That(gap, Is.GreaterThanOrEqualTo(-0.01f),
                $"ZERO przenikania — gap={gap:F3}m (B.Rear={b.RearDistM:F2}, A.Front={a.FrontDistM:F2}).");
            Assert.That(gap, Is.EqualTo(DepotOccupancyConstants.ContactGapM).Within(0.2f),
                $"Dojazd do styku ≈ {DepotOccupancyConstants.ContactGapM}m (walidacja konwencji center ±L/2); gap={gap:F3}m.");
        }

        [UnityTest]
        public IEnumerator MoverReachesClearTarget()
        {
            int bVeh = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { bVeh }, 0f, 20f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);

            // Cel ~75 m, brak blokera → ma dojechać do celu (cap = +inf).
            bool ok = _sim.EnqueueMove(MoverConsist, new List<int> { bVeh }, _trackId, _trackId, new Vector3(0f, 0f, 75f));
            Assert.That(ok, Is.True);

            yield return DriveUntilDone(MoverConsist);

            var b = _graph.GetOccupant(_trackId, MoverConsist);
            Assert.That(b, Is.Not.Null);
            float center = (b.FrontDistM + b.RearDistM) * 0.5f;
            Assert.That(center, Is.EqualTo(75f).Within(1.0f),
                $"Bez blokera dojeżdża do celu ~75m; center={center:F2}m.");
        }

        [UnityTest]
        public IEnumerator NoTractionConsistRejected()
        {
            int wagon = SeedVehicle(FleetVehicleType.PassengerCar, 0, 20f);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { wagon }, 0f, 20f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);

            bool ok = _sim.EnqueueMove(MoverConsist, new List<int> { wagon }, _trackId, _trackId, _endWorld);
            Assert.That(ok, Is.False, "Skład bez napędu nie może jechać sam (reguła TD-031).");
            Assert.That(_sim.HasTaskForConsist(MoverConsist), Is.False, "Brak taska po odrzuceniu.");
            yield return null;
        }

        IEnumerator DriveFixed(int n)
        {
            for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator ReRouteSameDirection_StopsAtNewTarget()
        {
            int bVeh = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { bVeh }, 0f, 20f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);

            Assert.That(_sim.EnqueueMove(MoverConsist, new List<int> { bVeh }, _trackId, _trackId, new Vector3(0f, 0f, 140f)), Is.True);
            yield return DriveFixed(60); // ruszył (mid-move, speed > 0.1)
            Assert.That(_sim.GetActiveTask(MoverConsist), Is.Not.Null, "W trakcie ruchu przed re-route.");

            // Re-route do BLIŻSZEGO celu, ten sam kierunek (forward) → branch same-dir continuation.
            Assert.That(_sim.EnqueueMove(MoverConsist, new List<int> { bVeh }, _trackId, _trackId, new Vector3(0f, 0f, 60f)), Is.True);
            yield return DriveUntilDone(MoverConsist);

            var b = _graph.GetOccupant(_trackId, MoverConsist);
            Assert.That(b, Is.Not.Null);
            float center = (b.FrontDistM + b.RearDistM) * 0.5f;
            Assert.That(center, Is.EqualTo(60f).Within(2f), $"Re-route same-dir → stop na NOWYM celu ~60m; center={center:F2}.");
            Assert.That(_graph.GetOccupants(_trackId).Count, Is.EqualTo(1), "Brak ghost-occupantów po re-route (tylko mover).");
        }

        [UnityTest]
        public IEnumerator ReRouteOppositeDirection_Reverses()
        {
            int bVeh = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { bVeh }, 60f, 80f, 1); // start w środku
            _sim.RestoreParkedVisualsFromGraph(_graph);

            Assert.That(_sim.EnqueueMove(MoverConsist, new List<int> { bVeh }, _trackId, _trackId, new Vector3(0f, 0f, 130f)), Is.True);
            yield return DriveFixed(60); // jedzie do przodu

            // Re-route DO TYŁU (cel za składem) → branch opposite-direction (brake + pending + reverse).
            Assert.That(_sim.EnqueueMove(MoverConsist, new List<int> { bVeh }, _trackId, _trackId, new Vector3(0f, 0f, 20f)), Is.True);
            yield return DriveUntilDone(MoverConsist, 12000);

            var b = _graph.GetOccupant(_trackId, MoverConsist);
            Assert.That(b, Is.Not.Null);
            float center = (b.FrontDistM + b.RearDistM) * 0.5f;
            Assert.That(center, Is.EqualTo(20f).Within(3f), $"Re-route opposite → cofa do ~20m; center={center:F2}.");
        }

        [UnityTest]
        public IEnumerator SaveLoad_ResumesMove_PositionPreserved()
        {
            int bVeh = SeedVehicle(FleetVehicleType.ElectricLocomotive, 2000, 20f);
            _graph.SetOccupantInterval(_trackId, MoverConsist, new List<int> { bVeh }, 0f, 20f, 1);
            _sim.RestoreParkedVisualsFromGraph(_graph);

            Assert.That(_sim.EnqueueMove(MoverConsist, new List<int> { bVeh }, _trackId, _trackId, new Vector3(0f, 0f, 100f)), Is.True);
            yield return DriveFixed(120); // mid-move

            var taskBefore = _sim.GetActiveTask(MoverConsist);
            Assert.That(taskBefore, Is.Not.Null, "W trakcie ruchu przed 'save'.");
            int savedTo = taskBefore.toTrackId;
            Vector3? savedTarget = taskBefore.targetWorldPos;
            var occBefore = _graph.GetOccupant(_trackId, MoverConsist);
            Assert.That(occBefore, Is.Not.Null);
            float centerBefore = (occBefore.FrontDistM + occBefore.RearDistM) * 0.5f;

            // Symulacja save→load: occupancy (footprint) trwa w grafie = persisted; RestoreParkedVisualsFromGraph
            // = ścieżka load (clear tasks + rebuild visuali z occupancy). Task NIE jest persistowany.
            _sim.RestoreParkedVisualsFromGraph(_graph);
            Assert.That(_sim.HasTaskForConsist(MoverConsist), Is.False, "Po 'load' task ruchu nie istnieje (runtime).");
            var occLoaded = _graph.GetOccupant(_trackId, MoverConsist);
            Assert.That(occLoaded, Is.Not.Null);
            float centerLoaded = (occLoaded.FrontDistM + occLoaded.RearDistM) * 0.5f;
            Assert.That(centerLoaded, Is.EqualTo(centerBefore).Within(0.5f),
                $"Pozycja DOKŁADNIE zachowana przez save/load: {centerBefore:F2}→{centerLoaded:F2}.");

            // Resume (= DepotSavable depotMoveTasks → RestoreActiveMove).
            Assert.That(_sim.RestoreActiveMove(MoverConsist, new List<int> { bVeh }, savedTo, savedTarget, false, false), Is.True);
            yield return DriveUntilDone(MoverConsist);

            var occFinal = _graph.GetOccupant(_trackId, MoverConsist);
            Assert.That(occFinal, Is.Not.Null);
            float centerFinal = (occFinal.FrontDistM + occFinal.RearDistM) * 0.5f;
            Assert.That(centerFinal, Is.EqualTo(100f).Within(2f), $"Po resume dojeżdża do celu ~100m; center={centerFinal:F2}.");
        }
    }
}
