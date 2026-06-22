using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Timetable;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// M4/M-TimetableUX: testy RailwayPathfinder (A* time-weighted) na realnym grafie PL.
    /// PlayMode bo PathfindingGraph budowany tylko z mapy OSM (AddEdge private) — ładuje MapScene.
    ///
    /// Pokrywa kontrakt: self-path (A→A), symetria długości (A→B == B→A), invalid node → Failure,
    /// znaleziona ścieżka ma spójne nodeIds (start..end) + dodatnią długość, blockedEdges respektowane.
    /// </summary>
    public class PathfinderTests
    {
        const float ReadyTimeoutSec = 120f;

        [UnityTest]
        public IEnumerator FindPath_BetweenConnectedStations_Succeeds()
        {
            yield return LoadMapSceneAndWaitReady();
            var init = TimetableInitializer.Instance;
            var pair = FindConnectedPair(init, maxLengthM: 60000f);
            if (pair.a < 0) { Assert.Ignore("Brak pary połączonych stacji — środowisko bez mapy PL."); yield break; }

            var r = RailwayPathfinder.FindPath(init.Graph, pair.a, pair.b);

            Assert.That(r.success, Is.True, "Ścieżka między połączonymi stacjami powinna istnieć.");
            Assert.That(r.totalLengthM, Is.GreaterThan(0f), "Niezerowa długość.");
            Assert.That(r.nodeIds, Is.Not.Null.And.Count.GreaterThanOrEqualTo(2));
            Assert.That(r.nodeIds[0], Is.EqualTo(pair.a), "Pierwszy node = start.");
            Assert.That(r.nodeIds[r.nodeIds.Count - 1], Is.EqualTo(pair.b), "Ostatni node = end.");
            // edgeIds = nodeIds - 1 (krawędzie między kolejnymi węzłami)
            Assert.That(r.edgeIds.Count, Is.EqualTo(r.nodeIds.Count - 1), "edgeIds = nodeIds-1.");
        }

        [UnityTest]
        public IEnumerator FindPath_IsSymmetricInLength()
        {
            yield return LoadMapSceneAndWaitReady();
            var init = TimetableInitializer.Instance;
            var pair = FindConnectedPair(init, maxLengthM: 60000f);
            if (pair.a < 0) { Assert.Ignore("Brak pary."); yield break; }

            var ab = RailwayPathfinder.FindPath(init.Graph, pair.a, pair.b);
            var ba = RailwayPathfinder.FindPath(init.Graph, pair.b, pair.a);

            Assert.That(ab.success && ba.success, Is.True);
            // Długość geograficzna powinna być ~symetryczna (time-weighting może dać minimalnie
            // inną trasę przy preferred_direction, więc tolerancja 5%).
            Assert.That(ba.totalLengthM, Is.EqualTo(ab.totalLengthM).Within(ab.totalLengthM * 0.05f),
                "Długość A→B ≈ B→A (±5%, directionPenalty może minimalnie różnić).");
        }

        [UnityTest]
        public IEnumerator FindPath_SameStartEnd_ReturnsTrivialPath()
        {
            yield return LoadMapSceneAndWaitReady();
            var init = TimetableInitializer.Instance;
            int node = FirstStationNode(init);
            if (node < 0) { Assert.Ignore("Brak stacji z węzłem."); yield break; }

            var r = RailwayPathfinder.FindPath(init.Graph, node, node);

            Assert.That(r.success, Is.True, "A→A zawsze sukces.");
            Assert.That(r.totalLengthM, Is.EqualTo(0f), "Dystans A→A = 0.");
            Assert.That(r.nodeIds.Count, Is.EqualTo(1), "Trywialna ścieżka = 1 węzeł.");
            Assert.That(r.edgeIds.Count, Is.EqualTo(0), "Brak krawędzi w trywialnej ścieżce.");
        }

        [UnityTest]
        public IEnumerator FindPath_InvalidNode_ReturnsFailure()
        {
            yield return LoadMapSceneAndWaitReady();
            var init = TimetableInitializer.Instance;
            int valid = FirstStationNode(init);
            if (valid < 0) { Assert.Ignore("Brak stacji."); yield break; }

            var negative = RailwayPathfinder.FindPath(init.Graph, -1, valid);
            var outOfRange = RailwayPathfinder.FindPath(init.Graph, valid, init.Graph.NodeCount + 100);

            Assert.That(negative.success, Is.False, "Ujemny node → Failure.");
            Assert.That(outOfRange.success, Is.False, "Node poza zakresem → Failure.");
        }

        [UnityTest]
        public IEnumerator FindPath_BlockedEdges_RoutesAroundOrFails()
        {
            yield return LoadMapSceneAndWaitReady();
            var init = TimetableInitializer.Instance;
            var pair = FindConnectedPair(init, maxLengthM: 60000f);
            if (pair.a < 0) { Assert.Ignore("Brak pary."); yield break; }

            var direct = RailwayPathfinder.FindPath(init.Graph, pair.a, pair.b);
            Assert.That(direct.success, Is.True);

            // Zablokuj wszystkie krawędzie z oryginalnej trasy → pathfinder musi szukać objazdu
            // albo zwrócić Failure (jeśli brak alternatywy). Oba akceptowalne — kluczowe że NIE
            // używa zablokowanych krawędzi gdy znajdzie ścieżkę.
            var blocked = new HashSet<int>(direct.edgeIds);
            var rerouted = RailwayPathfinder.FindPath(init.Graph, pair.a, pair.b, blockedEdges: blocked);

            if (rerouted.success)
            {
                foreach (int eid in rerouted.edgeIds)
                    Assert.That(blocked.Contains(eid), Is.False,
                        "Trasa objazdowa NIE może używać zablokowanych krawędzi.");
            }
            // jeśli !success — OK, brak alternatywnej trasy (single-track line)
        }

        [UnityTest]
        public IEnumerator BlockSections_AlongRoute_AreOrderedByEntryDistance()
        {
            yield return LoadMapSceneAndWaitReady();
            var init = TimetableInitializer.Instance;
            if (init.BlockSections == null || init.BlockSections.SectionCount == 0)
            { Assert.Ignore("Brak block sections w grafie."); yield break; }

            var pair = FindConnectedPair(init, maxLengthM: 60000f);
            if (pair.a < 0) { Assert.Ignore("Brak pary."); yield break; }

            var path = RailwayPathfinder.FindPath(init.Graph, pair.a, pair.b);
            Assert.That(path.success, Is.True);

            var blockInfo = init.BlockSections.GetSectionsForRoute(path.nodeIds, init.Graph);

            Assert.That(blockInfo.totalRouteDistance, Is.GreaterThan(0f), "Trasa ma niezerową długość.");
            // Sekcje muszą być uporządkowane rosnąco wg entryDistanceM (kolejność wzdłuż trasy).
            float prev = -1f;
            foreach (var s in blockInfo.sections)
            {
                Assert.That(s.entryDistanceM, Is.GreaterThanOrEqualTo(prev),
                    "Sekcje blokowe uporządkowane rosnąco wzdłuż trasy.");
                prev = s.entryDistanceM;
            }
            // entryDistance nie może przekraczać długości trasy.
            foreach (var s in blockInfo.sections)
                Assert.That(s.entryDistanceM, Is.LessThanOrEqualTo(blockInfo.totalRouteDistance + 1f),
                    "Wejście do sekcji mieści się w długości trasy.");

            Debug.Log($"[PathfinderTest] Route {pair.a}→{pair.b}: {blockInfo.sections.Count} block sections, " +
                      $"{blockInfo.totalRouteDistance:F0}m, unmapped={blockInfo.unmappedEdges}, missing={blockInfo.missingEdges}.");
        }

        [UnityTest]
        public IEnumerator BlockSections_EmptyRoute_ReturnsEmpty()
        {
            yield return LoadMapSceneAndWaitReady();
            var init = TimetableInitializer.Instance;
            if (init.BlockSections == null) { Assert.Ignore("Brak BlockSections."); yield break; }

            var empty = init.BlockSections.GetSectionsForRoute(new List<int>(), init.Graph);
            var single = init.BlockSections.GetSectionsForRoute(new List<int> { 0 }, init.Graph);

            Assert.That(empty.sections, Is.Empty, "Pusta trasa → brak sekcji.");
            Assert.That(single.sections, Is.Empty, "Trasa 1-węzłowa → brak sekcji (potrzeba ≥2).");
        }

        // ── Helpers ──────────────────────────────────────────────────

        struct Pair { public int a, b; }

        static int FirstStationNode(TimetableInitializer init)
        {
            if (init?.Stations == null) return -1;
            foreach (var s in init.Stations)
                if (s.pathNodeId >= 0) return s.pathNodeId;
            return -1;
        }

        /// <summary>Para stacji połączonych ścieżką krótszą niż maxLengthM (dla szybkiego A*).</summary>
        static Pair FindConnectedPair(TimetableInitializer init, float maxLengthM)
        {
            var result = new Pair { a = -1, b = -1 };
            if (init?.Stations == null || init.Graph == null) return result;

            // Budget na pathfind-calls (nie na pary kandydatów) — analogicznie do
            // DeliverySimulationTests.FindShortConnectedStationPair który działa z 60km.
            int pathfindCalls = 0;
            foreach (var sa in init.Stations)
            {
                if (sa.pathNodeId < 0) continue;
                foreach (var sb in init.Stations)
                {
                    if (sb.pathNodeId < 0 || sb.pathNodeId == sa.pathNodeId) continue;
                    if ((sa.position - sb.position).sqrMagnitude > maxLengthM * maxLengthM) continue;
                    if (++pathfindCalls > 1500) return result; // hojny budget — A* na 60km jest tani
                    var path = RailwayPathfinder.FindPath(init.Graph, sa.pathNodeId, sb.pathNodeId);
                    if (path.success && path.totalLengthM > 0f && path.totalLengthM <= maxLengthM)
                        return new Pair { a = sa.pathNodeId, b = sb.pathNodeId };
                }
            }
            return result;
        }

        static IEnumerator LoadMapSceneAndWaitReady()
        {
            if (SceneManager.GetActiveScene().name != "MapScene")
            {
                var load = SceneManager.LoadSceneAsync("MapScene", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
            }
            var init = TimetableInitializer.Instance ?? TimetableInitializer.EnsureBootstrapped();
            if (init != null && !init.IsReady) init.Initialize();

            float t0 = Time.realtimeSinceStartup;
            while (init != null && !init.IsReady)
            {
                if (Time.realtimeSinceStartup - t0 > ReadyTimeoutSec)
                    Assert.Fail($"TimetableInitializer nie gotowy w {ReadyTimeoutSec}s.");
                yield return null;
            }
        }
    }
}
