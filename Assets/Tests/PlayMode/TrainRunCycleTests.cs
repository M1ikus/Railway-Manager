using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// M9a/M9c: cykl zycia TrainRun na mapie 2D + handshake VehicleLocationService.
    /// PlayMode bo wymaga MapScene (TrainRunSimulator + DepotMapHandshakeService + graf).
    ///
    /// Pokrywa: SpawnTrainFromVehicles -> run aktywny + OnRunSpawned + M9c handshake
    /// (DepotMapHandshakeService.HandleRunSpawned ustawia pojazd OnRoute) -> wymuszone
    /// completion -> CollectAndDespawnCompleted (FixedUpdate) -> OnRunDespawned + run usuniety.
    ///
    /// Limitacja: trasa syntetyczna (puste nodeIds, by uniknac GetNode NRE z realnym grafem)
    /// -> brak realnego advancement i block occupancy (wymagaja trasy na realnych wezlach mapy;
    /// pelny fake-TrainRun-generator deferred — patrz MP-1). Tu testujemy spawn/despawn lifecycle
    /// + event wiring + location handshake, nie ruch po torach.
    /// </summary>
    public class TrainRunCycleTests
    {
        const float ReadyTimeoutSec = 120f;
        const int RouteId = 990001, TtId = 990002, RunId = 990003, Vid = 990004;

        [UnityTest]
        public IEnumerator Spawn_SetsVehicleOnRoute_Then_ForcedComplete_Despawns()
        {
            yield return LoadMapSceneAndWaitReady();

            var sim = TrainRunSimulator.Instance;
            Assert.That(sim, Is.Not.Null, "TrainRunSimulator powinien istniec w MapScene.");
            var locSvc = VehicleLocationService.Instance ?? VehicleLocationService.EnsureExists();

            // Syntetyczna minimalna trasa + rozklad. nodeIds puste -> branch GetNode pominiety
            // (realny graf + fejkowe id rzucilby NRE). stations dostarczaja dystansow stopow.
            var route = new Route
            {
                id = RouteId,
                name = "RT Cycle Route",
                nodeIds = new List<int>(),
                totalLengthM = 1000f,
                stations = new List<RouteStation>
                {
                    new RouteStation { stationName = "A", distanceFromStartM = 0f },
                    new RouteStation { stationName = "B", distanceFromStartM = 1000f }
                }
            };
            var tt = new TimetableObj
            {
                id = TtId,
                name = "RT Cycle TT",
                routeId = RouteId,
                stops = new List<TimetableStop>
                {
                    new TimetableStop { stationName = "A", plannedArrivalSec = 0,   plannedDepartureSec = 0,   distanceFromStartM = 0f },
                    new TimetableStop { stationName = "B", plannedArrivalSec = 600, plannedDepartureSec = 600, distanceFromStartM = 1000f }
                }
            };
            TimetableService.Routes.Add(route);
            TimetableService.Timetables.Add(tt);

            var tr = new TrainRun
            {
                id = RunId,
                timetableId = TtId,
                trainNumberSnapshot = "99999",
                runDateIso = GameState.CurrentDateIso,
                isDeliveryRun = true // bypass CrewCheckHook (brak zalogi nie blokuje spawnu)
            };

            TrainRun spawned = null, despawned = null;
            System.Action<TrainRun> onS = r => { if (r.id == RunId) spawned = r; };
            System.Action<TrainRun> onD = r => { if (r.id == RunId) despawned = r; };
            TrainRunSimulator.OnRunSpawned += onS;
            TrainRunSimulator.OnRunDespawned += onD;
            float timeScaleBackup = GameState.TimeScale;
            try
            {
                GameState.TimeScale = 1f; // upewnij sie ze FixedUpdate tickuje symulacje

                int before = sim.ActiveTrainCount;
                bool ok = sim.SpawnTrainFromVehicles(tr, new List<int> { Vid }, Vector2.zero);

                Assert.That(ok, Is.True, "SpawnTrainFromVehicles powinien sie powiesc.");
                Assert.That(sim.IsActive(RunId), Is.True, "Run aktywny w _activeTrains.");
                Assert.That(sim.ActiveTrainCount, Is.EqualTo(before + 1), "Licznik aktywnych +1.");
                Assert.That(spawned, Is.Not.Null, "OnRunSpawned wyemitowany dla naszego runu.");

                // M9c handshake: DepotMapHandshakeService.HandleRunSpawned -> SetOnRoute
                var rec = locSvc.Get(Vid);
                Assert.That(rec, Is.Not.Null, "Handshake utworzyl rekord lokalizacji pojazdu.");
                Assert.That(rec.type, Is.EqualTo(VehicleLocationType.OnRoute),
                    "Spawn -> pojazd przechodzi w OnRoute (M9c handshake).");

                // Wymus completion -> sciezka despawn (CollectAndDespawnCompleted w FixedUpdate)
                sim.ActiveTrains[RunId].state = TrainState.Completed;
                for (int i = 0; i < 10 && sim.IsActive(RunId); i++)
                    yield return new WaitForFixedUpdate();

                Assert.That(sim.IsActive(RunId), Is.False, "Po Completed run zostal zdespawnowany.");
                Assert.That(despawned, Is.Not.Null, "OnRunDespawned wyemitowany.");
            }
            finally
            {
                TrainRunSimulator.OnRunSpawned -= onS;
                TrainRunSimulator.OnRunDespawned -= onD;
                GameState.TimeScale = timeScaleBackup;
                TimetableService.Routes.RemoveAll(r => r.id == RouteId);
                TimetableService.Timetables.RemoveAll(t => t.id == TtId);
                VehicleLocationService.ResetAll();
            }
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
