using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Core;
using RailwayManager.Fleet;
using DepotSystem;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// M9c-D: PlayMode testy wjazdu do depot przez DepotMovementSimulator (realny manewr 3D,
    /// nie headless-fallback). Ładuje Depot.unity — scena z pełnym 3D/URP/TrackGraph.
    ///
    /// UWAGA: to PRÓBA wykonalności headless. Depot.unity jest cięższa niż MapScene
    /// (catenary, meble, kamera orbit, URP renderer). Jeśli scena nie wstaje w batchmode
    /// -nographics, test to ujawni (Ignore/Fail z diagnozą), a nie udajemy pokrycia.
    ///
    /// Etap 1: bootstrap — czy scena wstaje + TrackGraph + DepotMovementSimulator + tory permanentne.
    /// </summary>
    public class DepotEntryTests
    {
        const float ReadyTimeoutSec = 60f;
        int _cleanupCounter;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            // DepotEntryTests ładuje Depot.unity (LoadSceneMode.Single). Bez sprzątania scena oraz jej
            // singletony (DepotMovementSimulator.Instance, DepotServices cache) PRZEŻYWAJĄ i zatruwają
            // kolejne klasy PlayMode (programmatic sim testy → MissingReferenceException / brak ruchu).
            // Zostaw czystą, pustą scenę aktywną; UnloadSceneAsync(Depot) wywoła OnDestroy singletonów.
            var depot = SceneManager.GetSceneByName("Depot");
            if (depot.IsValid() && depot.isLoaded)
            {
                var empty = SceneManager.CreateScene("PostDepotClean" + (_cleanupCounter++));
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(depot);
            }

            // TD-038: unload sceny NIE sprząta DontDestroyOnLoad sim-singletonów (DeliveryService itd.)
            // które Depot.unity zbootstrapował przez TrainRunSimulator.Awake — przeżywają i tykają Update
            // w kolejnych klasach (DeliveryService parkuje wyciekłą flotę na ich sim). Sprzątamy u źródła,
            // żeby następna klasa PlayMode startowała na czysto niezależnie od własnego SetUp.
            PlayModeSimTestIsolation.HardReset();
        }

        [UnityTest]
        public IEnumerator DepotScene_Loads_WithTrackGraphAndMovementSimulator()
        {
            var load = SceneManager.LoadSceneAsync("Depot", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "LoadSceneAsync('Depot') null — scena w Build Settings?");
            while (!load.isDone) yield return null;

            // Daj DepotManager.Start czas na FindReferences + GenerateExternalTracks.
            float t0 = Time.realtimeSinceStartup;
            DepotMovementSimulator sim = null;
            TrackGraph graph = null;
            while (Time.realtimeSinceStartup - t0 < ReadyTimeoutSec)
            {
                sim = DepotMovementSimulator.Instance;
                graph = DepotServices.Get<TrackGraph>();
                if (sim != null && graph != null && graph.Tracks != null && graph.Tracks.Count > 0)
                    break;
                yield return null;
            }

            Assert.That(sim, Is.Not.Null, "DepotMovementSimulator.Instance powinien istnieć w Depot.unity.");
            Assert.That(graph, Is.Not.Null, "TrackGraph powinien istnieć (DepotServices).");
            Assert.That(graph.Tracks, Is.Not.Null.And.Count.GreaterThan(0),
                "Powinny być wygenerowane tory (GenerateExternalTracks w DepotManager.Start).");

            // Czy jest tor permanentny z outside endpoint? SpawnConsistAtEntry tego wymaga.
            int permanentCount = 0;
            foreach (var kv in graph.Tracks)
                if (kv.Value != null && kv.Value.IsPermanent) permanentCount++;

            Debug.Log($"[DepotEntryTest] Depot.unity ready: {graph.Tracks.Count} tracks " +
                      $"({permanentCount} permanent), sim={(sim != null)}.");
            Assert.That(permanentCount, Is.GreaterThan(0),
                "Powinien być >=1 tor permanentny (zewnętrzny) — punkt wjazdu/wyjazdu.");
        }

        [UnityTest]
        public IEnumerator SpawnConsistAtEntry_ManeuversToGate_FiresEnteredEvent()
        {
            yield return WaitDepotReady();
            var sim = DepotMovementSimulator.Instance;
            if (sim == null) { Assert.Ignore("DepotMovementSimulator nie wstał — pomijam."); yield break; }

            float scaleBackup = GameState.TimeScale;
            bool pausedBackup = GameState.IsPaused;
            const int consistId = 970777;
            int enteredConsist = -1;
            System.Action<int, System.Collections.Generic.List<int>> handler =
                (cid, _) => { if (cid == consistId) enteredConsist = cid; };

            try
            {
                GameState.IsPaused = false;
                GameState.TimeScale = 5f; // DepotTimeScale cap = x5 (manewr 3D)
                DepotMovementSimulator.OnConsistEnteredDepot += handler;

                // Realny spawn na zewnętrznym torze + auto-manewr do bramy (NIE fallback).
                bool spawned = sim.SpawnConsistAtEntry(consistId, new System.Collections.Generic.List<int> { 12345 });
                Assert.That(spawned, Is.True,
                    "SpawnConsistAtEntry powinno się powieść (jest tor permanentny outside+inside).");

                // Pompuj FixedUpdate aż consist dojedzie do bramy i strzeli OnConsistEnteredDepot.
                float t0 = Time.realtimeSinceStartup;
                while (enteredConsist != consistId)
                {
                    if (Time.realtimeSinceStartup - t0 > 120f)
                        Assert.Fail("Consist nie dotarł do bramy w 120s (OnConsistEnteredDepot nie strzelił).");
                    yield return new WaitForFixedUpdate();
                }

                Debug.Log($"[DepotEntryTest] SpawnConsistAtEntry → manewr → OnConsistEnteredDepot OK (consist#{consistId}).");
            }
            finally
            {
                DepotMovementSimulator.OnConsistEnteredDepot -= handler;
                GameState.TimeScale = scaleBackup;
                GameState.IsPaused = pausedBackup;
            }
        }

        [UnityTest]
        public IEnumerator EnterThenExit_ManeuversOut_FiresExitedEvent()
        {
            // Świeża zajezdnia jest PUSTA (GenerateDefaultLayout zostawia tylko tory permanentne) —
            // ParkConsistOnFreeTrack nie ma na czym parkować. Realny scenariusz: consist wjeżdża
            // (SpawnConsistAtEntry, tor permanentny), staje przy bramie, potem gracz go zawraca →
            // EnqueueExit → manewr na zewnątrz → OnConsistExitedDepot. Testuje exit flow bez torów gracza.
            yield return WaitDepotReady();
            var sim = DepotMovementSimulator.Instance;
            if (sim == null) { Assert.Ignore("DepotMovementSimulator nie wstał — pomijam."); yield break; }

            float scaleBackup = GameState.TimeScale;
            bool pausedBackup = GameState.IsPaused;
            int consistId = sim.GenerateConsistId();
            int enteredConsist = -1, exitedConsist = -1;
            System.Action<int, System.Collections.Generic.List<int>> onEnter =
                (cid, _) => { if (cid == consistId) enteredConsist = cid; };
            System.Action<int, System.Collections.Generic.List<int>> onExit =
                (cid, _) => { if (cid == consistId) exitedConsist = cid; };

            try
            {
                GameState.IsPaused = false;
                GameState.TimeScale = 5f;
                DepotMovementSimulator.OnConsistEnteredDepot += onEnter;
                DepotMovementSimulator.OnConsistExitedDepot += onExit;

                var ids = new System.Collections.Generic.List<int> { 54321 };

                // TD-038: wyjazd to ruch WŁASNYM napędem (isSelfMove) — EnqueueExit wymaga
                // ConsistHasTraction. Wjazd (SpawnConsistAtEntry) jest system-driven (isSelfMove:false)
                // więc przechodzi bez danych floty, ale exit nie. Fałszywy vehicleId 54321 nie istniał
                // w FleetService → ConsistHasTraction=false → EnqueueExit zwracało false. To NIE limitacja
                // headless tylko brak danych floty — nadaj składowi lokomotywę żeby testować realny wyjazd.
                // Status MovingInDepot: DeliveryService.TryParkInitialDepotFleet bierze tylko StoppedInDepot
                // (bez lokalizacji), więc nie próbuje go równolegle parkować.
                FleetService.AddOwnedVehicle(new FleetVehicleData
                {
                    id = 54321,
                    type = FleetVehicleType.ElectricLocomotive,
                    series = "EU07",
                    powerKw = 2000,
                    lengthM = 20f,
                    status = FleetVehicleStatus.MovingInDepot,
                    supportedTractions = new List<TractionType> { TractionType.Electric }
                });

                bool spawned = sim.SpawnConsistAtEntry(consistId, ids);
                Assert.That(spawned, Is.True, "SpawnConsistAtEntry powinno się powieść.");

                // Poczekaj aż wjedzie i stanie przy bramie (visual gotowy do EnqueueExit).
                float t0 = Time.realtimeSinceStartup;
                while (enteredConsist != consistId)
                {
                    if (Time.realtimeSinceStartup - t0 > 120f)
                        Assert.Fail("Consist nie wjechał do bramy w 120s.");
                    yield return new WaitForFixedUpdate();
                }

                // Teraz zawróć — wyjazd z depot.
                bool exit = sim.EnqueueExit(consistId, ids);
                Assert.That(exit, Is.True, "EnqueueExit powinno zakolejkować wyjazd (jest tor zewnętrzny).");

                t0 = Time.realtimeSinceStartup;
                while (exitedConsist != consistId)
                {
                    if (Time.realtimeSinceStartup - t0 > 120f)
                        Assert.Fail("Consist nie opuścił depot w 120s (OnConsistExitedDepot nie strzelił).");
                    yield return new WaitForFixedUpdate();
                }

                Debug.Log($"[DepotEntryTest] Enter → EnqueueExit → manewr → OnConsistExitedDepot OK (consist#{consistId}).");
            }
            finally
            {
                DepotMovementSimulator.OnConsistEnteredDepot -= onEnter;
                DepotMovementSimulator.OnConsistExitedDepot -= onExit;
                GameState.TimeScale = scaleBackup;
                GameState.IsPaused = pausedBackup;
                FleetService.RemoveOwnedVehicle(54321); // TD-038: sprzątnij seed napędu
            }
        }

        // ── Helper ───────────────────────────────────────────────────

        /// <summary>Ładuje Depot.unity (jeśli trzeba) i czeka aż TrackGraph + sim gotowe.</summary>
        static IEnumerator WaitDepotReady()
        {
            if (SceneManager.GetActiveScene().name != "Depot")
            {
                var load = SceneManager.LoadSceneAsync("Depot", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
            }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < ReadyTimeoutSec)
            {
                var g = DepotServices.Get<TrackGraph>();
                if (DepotMovementSimulator.Instance != null && g != null && g.Tracks != null && g.Tracks.Count > 0)
                    yield break;
                yield return null;
            }
        }
    }
}
