using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DepotSystem;
using DepotSystem.Nav;
using RailwayManager.Core;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// TD-033 PlayMode: integracja DepotNavService z occupancy (train-yield) + end-to-end BuildRoute.
    /// Routing math + builder pokryte EditMode (NavObstacle/VisibilityGraphRouter/NavObstacleSetBuilder);
    /// tu weryfikujemy to, czego EditMode nie obejmie: yield przed zajętym torem (TD-031 occupancy) + wiring.
    /// </summary>
    public class DepotNavPlayTests
    {
        GameObject _graphGo, _navGo;
        TrackGraph _graph;
        DepotNavService _nav;
        int _trackId;
        const int Consist = 700;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            GameState.TimeScale = 1f;
            PauseStack.Clear();
            GameState.IsPaused = false;
            // Izolacja: poprzedni DepotNavService singleton mógł przeżyć → duplicate-guard zniszczyłby świeży.
            if (DepotNavService.Instance != null) Object.DestroyImmediate(DepotNavService.Instance.gameObject);

            _graphGo = new GameObject("TestTrackGraph");
            _graph = _graphGo.AddComponent<TrackGraph>();
            int nA = _graph.AddNode(new Vector3(0f, 0f, 0f));
            int nB = _graph.AddNode(new Vector3(0f, 0f, 100f));
            int edge = _graph.AddEdgeWithPolyline(nA, nB,
                new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 100f) });
            _trackId = _graph.AddTrack("T", DepotTrackType.Parking, new List<int> { edge });

            _navGo = new GameObject("TestDepotNav");
            _nav = _navGo.AddComponent<DepotNavService>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_navGo != null) Object.DestroyImmediate(_navGo);
            if (_graphGo != null) Object.DestroyImmediate(_graphGo);
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator BuildRoute_EmptyDepot_DirectRoute()
        {
            var route = _nav.BuildRoute(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Assert.IsNotNull(route);
            Assert.GreaterOrEqual(route.Count, 2, "Pusta zajezdnia → trasa bezpośrednia (≥2 punkty), bez NRE.");
            Assert.AreEqual(10f, route[route.Count - 1].x, 1e-3f, "Kończy w celu.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Yield_OccupiedTrack_BlocksThenClears()
        {
            var onTrack = new Vector3(0f, 0f, 50f);
            Assert.IsFalse(_nav.IsBlockedByConsist(onTrack), "Tor wolny → brak yield.");

            _graph.SetOccupantInterval(_trackId, Consist, new List<int> { 999 }, 40f, 60f, 1);
            Assert.IsTrue(_nav.IsBlockedByConsist(onTrack), "Skład [40,60] na torze → yield w punkcie 50.");
            Assert.IsFalse(_nav.IsBlockedByConsist(new Vector3(10f, 0f, 50f)),
                "10 m w bok od osi toru → brak yield (lateral > clear).");

            _graph.RemoveConsistEverywhere(Consist);
            Assert.IsFalse(_nav.IsBlockedByConsist(onTrack), "Po zjeździe składu → brak yield.");
            yield return null;
        }
    }
}
