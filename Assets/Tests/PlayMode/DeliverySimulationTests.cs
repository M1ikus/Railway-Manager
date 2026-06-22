using System.Collections;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// M9c-D: PlayMode testy symulacji ruchu dostawy — ładują MapScene (realny PathfindingGraph
    /// z init-state-pl.bin) i weryfikują pełną ścieżkę której EditMode nie dosięga:
    /// scheduled delivery run → spawn na mapie → ruch → despawn → handshake (wjazd/AtStation).
    ///
    /// UWAGA: te testy są CIĘŻKIE (ładowanie sceny + graf ~15s) i przeznaczone na CLI/CI,
    /// nie do szybkiej pętli. Headless: DepotMovementSimulator NIE istnieje (scena Depot
    /// nieładowana), więc wjazd do depot idzie fallbackiem InDepot — to akceptowalne, bo
    /// 3D-manewr i tak nie działa bez sceny Depot.
    ///
    /// Etap 1 (ten plik na start): tylko bootstrap — czy MapScene wstaje w batchmode i graf
    /// się buduje. Logika dostawy dochodzi po potwierdzeniu że scena się ładuje na CLI.
    /// </summary>
    public class DeliverySimulationTests
    {
        const float ReadyTimeoutSec = 120f; // hojnie — batchmode + fast-path build

        [UnityTest]
        public IEnumerator MapScene_LoadsAndGraphBecomesReady()
        {
            yield return LoadMapSceneAndWaitReady();

            var init = TimetableInitializer.Instance;
            Assert.That(init, Is.Not.Null, "TimetableInitializer powinien istnieć po załadowaniu MapScene.");
            Assert.That(init.IsReady, Is.True, "TimetableInitializer.IsReady powinno być true.");
            Assert.That(init.Graph, Is.Not.Null, "PathfindingGraph powinien być zbudowany.");
            Assert.That(init.Graph.NodeCount, Is.GreaterThan(0), "Graf powinien mieć węzły.");
            Assert.That(init.Stations, Is.Not.Null.And.Count.GreaterThan(0), "Powinny być załadowane stacje.");

            // TrainRunSimulator bootstrapuje się w MapScene (Awake) — potrzebny do spawnu dostawy.
            Assert.That(TrainRunSimulator.Instance, Is.Not.Null,
                "TrainRunSimulator.Instance powinien istnieć w MapScene.");

            Debug.Log($"[DeliverySimTest] MapScene ready: {init.Graph.NodeCount} nodes, " +
                      $"{init.Stations.Count} stations.");
        }

        [UnityTest]
        public IEnumerator ScheduledDelivery_SelfPropelled_DrivesToHomeAndEntersDepot()
        {
            yield return LoadMapSceneAndWaitReady();
            var init = TimetableInitializer.Instance;

            // Znajdź parę stacji połączonych krótką trasą (szybki przejazd w teście).
            var pair = FindShortConnectedStationPair(init, maxLengthM: 60000f);
            if (pair.fromNode < 0)
            {
                Assert.Ignore("Brak pary stacji z krótką osiągalną trasą w grafie — pomijam (środowisko bez mapy PL).");
                yield break;
            }

            int homeBackup = GameState.HomeDepotStationId;
            float scaleBackup = GameState.TimeScale;
            const int vid = 990101;
            RailwayManager.Fleet.FleetService.RemoveOwnedVehicle(vid);

            try
            {
                GameState.HomeDepotStationId = pair.homeNode;
                GameState.TimeScale = 500f; // przyspiesz przejazd

                var loc = VehicleLocationService.Instance ?? VehicleLocationService.EnsureExists();
                var svc = DeliveryService.Instance ?? DeliveryService.EnsureExists();

                // Pojazd samojezdny "kupiony" w stacji from, czeka na odbiór.
                var v = new RailwayManager.Fleet.FleetVehicleData
                {
                    id = vid, series = "TEST-EU", type = RailwayManager.Fleet.FleetVehicleType.ElectricLocomotive,
                    status = RailwayManager.Fleet.FleetVehicleStatus.AwaitingPickup,
                    lengthM = 20f,
                    supportedTractions = new System.Collections.Generic.List<RailwayManager.Fleet.TractionType>
                        { RailwayManager.Fleet.TractionType.Electric },
                    position = new RailwayManager.Fleet.VehiclePosition
                        { kind = RailwayManager.Fleet.VehicleLocationKind.None, externalLocation = pair.fromName }
                };
                RailwayManager.Fleet.FleetService.AddOwnedVehicle(v);

                bool ok = svc.RequestScheduledDelivery(v);
                Assert.That(ok, Is.True, "RequestScheduledDelivery powinno zaakceptować samojezdny pojazd.");
                Assert.That(v.status, Is.EqualTo(RailwayManager.Fleet.FleetVehicleStatus.MovingOnMap),
                    "Po zleceniu dostawy pojazd powinien być MovingOnMap.");

                // Pompuj symulację aż pojazd dojedzie i wjedzie do depot (fallback InDepot headless).
                float t0 = Time.realtimeSinceStartup;
                while (v.status != RailwayManager.Fleet.FleetVehicleStatus.StoppedInDepot)
                {
                    if (Time.realtimeSinceStartup - t0 > 180f)
                        Assert.Fail($"Pojazd nie dojechał do depot w 180s (status={v.status}, " +
                                    $"loc={loc.Get(vid)?.type}).");
                    yield return new WaitForFixedUpdate();
                }

                Assert.That(loc.Get(vid)?.type, Is.EqualTo(VehicleLocationType.InDepot),
                    "Po dostawie pojazd powinien być InDepot.");
                Assert.That(v.deliveryInProgress, Is.False, "deliveryInProgress powinno być wyczyszczone.");
                Debug.Log($"[DeliverySimTest] Dostawa OK: {pair.fromName} → {pair.homeName} → depot.");
            }
            finally
            {
                RailwayManager.Fleet.FleetService.RemoveOwnedVehicle(vid);
                VehicleLocationService.ResetAll();
                GameState.HomeDepotStationId = homeBackup;
                GameState.TimeScale = scaleBackup;
            }
        }

        [UnityTest]
        public IEnumerator AwaitingPickup_MaterializesAtPurchaseStationOnMap()
        {
            yield return LoadMapSceneAndWaitReady();
            var init = TimetableInitializer.Instance;

            // Wybierz dowolną stację z węzłem grafu jako punkt zakupu.
            RailwayStation purchaseStation = null;
            foreach (var s in init.Stations)
                if (s.pathNodeId >= 0) { purchaseStation = s; break; }
            if (purchaseStation == null) { Assert.Ignore("Brak stacji z węzłem grafu."); yield break; }

            int homeBackup = GameState.HomeDepotStationId;
            const int vid = 990202;
            RailwayManager.Fleet.FleetService.RemoveOwnedVehicle(vid);
            try
            {
                GameState.HomeDepotStationId = purchaseStation.pathNodeId; // dowolny ważny home
                var loc = VehicleLocationService.Instance ?? VehicleLocationService.EnsureExists();
                var svc = DeliveryService.Instance ?? DeliveryService.EnsureExists();

                var v = new RailwayManager.Fleet.FleetVehicleData
                {
                    id = vid, series = "TEST-MAT", type = RailwayManager.Fleet.FleetVehicleType.EMU,
                    status = RailwayManager.Fleet.FleetVehicleStatus.AwaitingPickup,
                    supportedTractions = new System.Collections.Generic.List<RailwayManager.Fleet.TractionType>
                        { RailwayManager.Fleet.TractionType.Electric },
                    position = new RailwayManager.Fleet.VehiclePosition
                        { kind = RailwayManager.Fleet.VehicleLocationKind.None, externalLocation = purchaseStation.name }
                };
                RailwayManager.Fleet.FleetService.AddOwnedVehicle(v);

                // F1: tick materializuje AwaitingPickup → AtStation(punkt zakupu) na mapie.
                svc.ProcessVehicle(v, GameState.GameTimeSeconds);

                var rec = loc.Get(vid);
                Assert.That(rec, Is.Not.Null, "Pojazd powinien mieć rekord lokalizacji po materializacji.");
                Assert.That(rec.type, Is.EqualTo(VehicleLocationType.AtStation),
                    "AwaitingPickup z rozwiązaną lokalizacją → AtStation na mapie (widoczny przez IdleVehicleVisualizer).");
                Assert.That(rec.stationId, Is.EqualTo(purchaseStation.pathNodeId),
                    "Pojazd materializuje się na węźle stacji punktu zakupu.");
                Debug.Log($"[DeliverySimTest] Materializacja OK: '{purchaseStation.name}' node#{rec.stationId}.");
            }
            finally
            {
                RailwayManager.Fleet.FleetService.RemoveOwnedVehicle(vid);
                VehicleLocationService.ResetAll();
                GameState.HomeDepotStationId = homeBackup;
            }
        }

        [UnityTest]
        public IEnumerator OwnLocoFetch_RoundTrip_BothVehiclesReachDepot()
        {
            yield return LoadMapSceneAndWaitReady();
            var init = TimetableInitializer.Instance;

            var pair = FindShortConnectedStationPair(init, maxLengthM: 60000f);
            if (pair.fromNode < 0)
            {
                Assert.Ignore("Brak pary stacji z krótką trasą — pomijam (środowisko bez mapy PL).");
                yield break;
            }

            int homeBackup = GameState.HomeDepotStationId;
            float scaleBackup = GameState.TimeScale;
            const int locoId = 990301, wagonId = 990302;
            RailwayManager.Fleet.FleetService.RemoveOwnedVehicle(locoId);
            RailwayManager.Fleet.FleetService.RemoveOwnedVehicle(wagonId);

            try
            {
                GameState.HomeDepotStationId = pair.homeNode;
                GameState.TimeScale = 500f;
                var loc = VehicleLocationService.Instance ?? VehicleLocationService.EnsureExists();
                var svc = DeliveryService.Instance ?? DeliveryService.EnsureExists();

                // Loco w home depot (InDepot — wymagane przez FindAvailableFetchLoco).
                var loco = new RailwayManager.Fleet.FleetVehicleData
                {
                    id = locoId, series = "TEST-EU-FETCH", lengthM = 20f,
                    type = RailwayManager.Fleet.FleetVehicleType.ElectricLocomotive,
                    status = RailwayManager.Fleet.FleetVehicleStatus.StoppedInDepot,
                    supportedTractions = new System.Collections.Generic.List<RailwayManager.Fleet.TractionType>
                        { RailwayManager.Fleet.TractionType.Electric }
                };
                RailwayManager.Fleet.FleetService.AddOwnedVehicle(loco);
                loc.SetInDepot(locoId, depotTrackId: 1);

                // Wagon pasywny w punkcie zakupu, czeka na odbiór.
                var wagon = new RailwayManager.Fleet.FleetVehicleData
                {
                    id = wagonId, series = "TEST-WAG-FETCH", lengthM = 24f,
                    type = RailwayManager.Fleet.FleetVehicleType.PassengerCar,
                    status = RailwayManager.Fleet.FleetVehicleStatus.AwaitingPickup,
                    supportedTractions = new System.Collections.Generic.List<RailwayManager.Fleet.TractionType>
                        { RailwayManager.Fleet.TractionType.None },
                    position = new RailwayManager.Fleet.VehiclePosition
                        { kind = RailwayManager.Fleet.VehicleLocationKind.None, externalLocation = pair.fromName }
                };
                RailwayManager.Fleet.FleetService.AddOwnedVehicle(wagon);

                bool ok = svc.RequestOwnLocoWagonDelivery(wagon);
                Assert.That(ok, Is.True, "RequestOwnLocoWagonDelivery powinno wystartować round-trip.");
                Assert.That(loco.status, Is.EqualTo(RailwayManager.Fleet.FleetVehicleStatus.MovingOnMap),
                    "Loco rusza w leg1 (home → punkt zakupu).");

                // Pompuj aż OBA pojazdy trafią do depot (leg1 → sprzęg → leg2 → wjazd).
                float t0 = Time.realtimeSinceStartup;
                while (loco.status != RailwayManager.Fleet.FleetVehicleStatus.StoppedInDepot
                       || wagon.status != RailwayManager.Fleet.FleetVehicleStatus.StoppedInDepot)
                {
                    if (Time.realtimeSinceStartup - t0 > 300f)
                        Assert.Fail($"Round-trip nie dokończył w 300s (loco={loco.status}, wagon={wagon.status}).");
                    yield return new WaitForFixedUpdate();
                }

                Assert.That(loc.Get(locoId)?.type, Is.EqualTo(VehicleLocationType.InDepot),
                    "Loco po round-tripie InDepot.");
                Assert.That(loc.Get(wagonId)?.type, Is.EqualTo(VehicleLocationType.InDepot),
                    "Wagon przywieziony przez loco InDepot.");
                Assert.That(loco.deliveryInProgress, Is.False);
                Assert.That(wagon.deliveryInProgress, Is.False);
                Debug.Log($"[DeliverySimTest] Round-trip OK: loco po wagon {pair.homeName}↔{pair.fromName}, oba w depot.");
            }
            finally
            {
                RailwayManager.Fleet.FleetService.RemoveOwnedVehicle(locoId);
                RailwayManager.Fleet.FleetService.RemoveOwnedVehicle(wagonId);
                VehicleLocationService.ResetAll();
                GameState.HomeDepotStationId = homeBackup;
                GameState.TimeScale = scaleBackup;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        struct StationPair { public int fromNode, homeNode; public string fromName, homeName; }

        /// <summary>
        /// Znajduje parę stacji (from, home) połączonych trasą krótszą niż maxLengthM.
        /// Zwraca fromNode=-1 gdy nie znaleziono (np. graf bez stacji z węzłami).
        /// </summary>
        static StationPair FindShortConnectedStationPair(TimetableInitializer init, float maxLengthM)
        {
            var result = new StationPair { fromNode = -1 };
            if (init?.Stations == null || init.Graph == null) return result;

            // Ogranicz przeszukiwanie — bierz stacje z węzłem grafu, próbuj pathfind do sąsiadów.
            int checasted = 0;
            foreach (var home in init.Stations)
            {
                if (home.pathNodeId < 0) continue;
                foreach (var from in init.Stations)
                {
                    if (from.pathNodeId < 0 || from.pathNodeId == home.pathNodeId) continue;
                    // Pre-filtr: tylko bliskie geograficznie (sqr), żeby nie pathfindować całej PL.
                    if ((from.position - home.position).sqrMagnitude > maxLengthM * maxLengthM) continue;
                    if (++checasted > 400) return result; // budget — nie wisimy

                    var path = RailwayPathfinder.FindPath(init.Graph, from.pathNodeId, home.pathNodeId);
                    if (path.success && path.totalLengthM > 0f && path.totalLengthM <= maxLengthM)
                        return new StationPair
                        {
                            fromNode = from.pathNodeId, homeNode = home.pathNodeId,
                            fromName = from.name, homeName = home.name
                        };
                }
            }
            return result;
        }

        // ── Shared bootstrap helper ──────────────────────────────────

        /// <summary>
        /// Ładuje MapScene (Single) i czeka aż TimetableInitializer zbuduje graf (IsReady).
        /// Wymusza EnsureBootstrapped + Initialize gdy auto-bootstrap nie wystartował
        /// (brak MapUIManager / batchmode timing). Failuje test gdy nie gotowe w timeout.
        /// </summary>
        protected static IEnumerator LoadMapSceneAndWaitReady()
        {
            var load = SceneManager.LoadSceneAsync("MapScene", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "LoadSceneAsync zwróciło null — czy MapScene jest w Build Settings?");
            while (!load.isDone) yield return null;

            // Auto-bootstrap (sceneLoaded hook) ma ~2s delay; w teście wymuszamy od razu.
            var init = TimetableInitializer.Instance;
            if (init == null)
                init = TimetableInitializer.EnsureBootstrapped();
            if (init != null && !init.IsReady)
                init.Initialize();

            float t0 = Time.realtimeSinceStartup;
            while (init != null && !init.IsReady)
            {
                if (Time.realtimeSinceStartup - t0 > ReadyTimeoutSec)
                    Assert.Fail($"TimetableInitializer nie osiągnął IsReady w {ReadyTimeoutSec}s " +
                                "(init-state-pl.bin brak? mapa nieskonfigurowana?).");
                yield return null;
            }
        }
    }
}
