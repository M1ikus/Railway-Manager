using UnityEngine;
using UnityEngine.SceneManagement;
using RailwayManager.Core;
using DepotSystem;

namespace RailwayManager.Tests.PlayMode
{
    /// <summary>
    /// TD-038: izolacja PlayMode miedzy klasami testow.
    ///
    /// <see cref="DepotEntryTests"/> laduje <c>Depot.unity</c> (LoadSceneMode.Single), ktory bootstrapuje
    /// roj gameplay-singletonow oznaczonych <see cref="Object.DontDestroyOnLoad"/> (DeliveryService,
    /// PassengerManager, EconomyManager, ReputationManager, DispatchService, DepotMapHandshakeService...).
    /// Bootstrap idzie z <c>TrainRunSimulator.Awake</c>. Te obiekty PRZEZYWAJA scene unload i dalej tykaja
    /// Update/FixedUpdate w nastepnych klasach PlayMode.
    ///
    /// Konkretny objaw (TD-038, zdiagnozowany z logu): leftover <c>DeliveryService.Update ->
    /// TryParkInitialDepotFleet</c> parkuje wyciekla flote startowa na <see cref="DepotMovementSimulator.Instance"/>
    /// NASTEPNEJ klasy (ktora buduje wlasny graf+sim) -> fantomowe occupanty na swiezym torze testu ->
    /// mover "nie dojezdza" (dynamicStopCap mysli ze ktos stoi z przodu). Dlatego
    /// <c>DepotMovementPlayTests.MoverReachesClearTarget</c> pada TYLKO po <see cref="DepotEntryTests"/>,
    /// a solo / w parze z couplingiem przechodzi.
    ///
    /// <see cref="HardReset"/> niszczy wyciekle gameplay-singletony z warstwy DontDestroyOnLoad (rozpoznane
    /// po namespace) i zeruje wspoldzielone statyki/Time. NIE rusza Core infra (GameClock /
    /// VehicleLocationService) ani obiektow Unity Test Framework (inny namespace -> to one pompuja korutyny
    /// testow). No-op gdy nic nie wycieklo (klasa uruchomiona solo). Wolane w SetUp klas programmatic
    /// (Movement / Coupling) oraz w teardownie <see cref="DepotEntryTests"/> (sprzatanie u zrodla).
    /// </summary>
    public static class PlayModeSimTestIsolation
    {
        /// <summary>Twardy reset stanu, ktory wycieka miedzy klasami PlayMode. Idempotentny, no-op gdy czysto.</summary>
        public static void HardReset()
        {
            DestroyLeakedDontDestroyOnLoadGameplaySingletons();

            // Stale depot sim singleton: scena Depot zostawia Instance=null po OnDestroy, ale gdy inna klasa
            // (lub przerwany unload) zostawila zywy egzemplarz -> usun, inaczej duplicate-guard w Awake
            // zniszczylby swiezy sim budowany przez test (MissingReferenceException).
            if (DepotMovementSimulator.Instance != null)
                Object.DestroyImmediate(DepotMovementSimulator.Instance.gameObject);

            // Wspoldzielone statyki cross-scene.
            VehicleLocationService.ResetAll();   // wyczysc wyciekle rekordy floty (np. #1000/#1001 InDepot)
            PauseStack.Clear();                  // inna klasa moze zostawic ownera pauzy -> IsPaused stuck true
            GameState.IsPaused = false;
            GameState.TimeScale = 1f;            // DepotTimeScale = min(1,5) = 1
            DepotServices.InvalidateAll();       // lazy-cache MonoBehaviour-singletonow sceny Depot
        }

        static void DestroyLeakedDontDestroyOnLoadGameplaySingletons()
        {
            // Brak public API na uchwyt sceny DontDestroyOnLoad — temp GO przeniesiony tam ujawnia ja przez .scene.
            var probe = new GameObject("~td038_ddol_probe");
            Object.DontDestroyOnLoad(probe);
            Scene ddol = probe.scene;
            Object.DestroyImmediate(probe);
            if (!ddol.IsValid()) return;

            // GetRootGameObjects() zwraca snapshot — bezpiecznie niszczyc w trakcie iteracji.
            foreach (var root in ddol.GetRootGameObjects())
            {
                if (root != null && IsLeakedGameplaySingleton(root))
                    Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// True gdy GO ma komponent z naszej warstwy gameplay (RailwayManager.* poza Core, lub DepotSystem.*).
        /// Wyklucza Core infra (GameClock / VehicleLocationService — potrzebne i resetowane osobno) oraz
        /// obiekty Unity Test Framework (namespace UnityEngine.TestTools.* itp. -> musza przezyc).
        /// </summary>
        static bool IsLeakedGameplaySingleton(GameObject go)
        {
            var comps = go.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var mb in comps)
            {
                if (mb == null) continue; // missing script
                string ns = mb.GetType().Namespace;
                if (string.IsNullOrEmpty(ns)) continue;
                if (ns == "RailwayManager.Core" || ns.StartsWith("RailwayManager.Core.")) continue; // preserve infra
                if (ns.StartsWith("RailwayManager") || ns.StartsWith("DepotSystem")) return true;
            }
            return false;
        }
    }
}
