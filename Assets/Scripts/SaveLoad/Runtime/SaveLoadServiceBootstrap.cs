using UnityEngine;
using RailwayManager.Core;
using System.Collections.Generic;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-11: Centralny bootstrap dla całego stacka Save/Load.
    ///
    /// Tworzy w odpowiedniej kolejności:
    /// 1. LocalDiskStorage (lub SteamCloudStorage gdy M14)
    /// 2. SaveOrchestrator (z reference na storage)
    /// 3. AutoSaveService (z reference na orchestrator)
    /// 4. LoadingScreenManager (singleton)
    ///
    /// SaveRegistry moduły (WorldSavable, FleetSavable, ...) rejestrują się same
    /// w swoich BeforeSceneLoad initializerach.
    ///
    /// Wywoływane RuntimeInitializeOnLoadMethod(AfterSceneLoad) — po SaveRegistry
    /// jest zapełniony, ale przed jakimkolwiek SaveAsync.
    /// </summary>
    public static class SaveLoadServiceBootstrap
    {
        public static SaveOrchestrator Orchestrator { get; private set; }
        public static ISaveStorage Storage { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Orchestrator != null) return; // already bootstrapped

            Storage = new LocalDiskStorage();
            Orchestrator = new SaveOrchestrator(Storage, Application.version);
            AutoSaveService.EnsureExists(Orchestrator);
            LoadingScreenManager.EnsureExists();

            // Walidacja kompletności rejestracji — wykryj brakujące moduły z ModuleOrder.
            // Brakujący moduł = save zapisze się bez jego state'u (gracz traci dane bez
            // ostrzeżenia). Typowy bug: zakomentowany [RuntimeInitializeOnLoadMethod]
            // w bootstrapper'ze podczas debugowania.
            var missing = SaveRegistry.GetMissingFromModuleOrder();
            if (missing.Count > 0)
            {
                Log.Error($"[SaveLoadServiceBootstrap] BRAKUJĄCE moduły w SaveRegistry: " +
                          $"[{string.Join(", ", missing)}]. Save'y NIE będą zawierały ich state'u! " +
                          $"Sprawdź czy [RuntimeInitializeOnLoadMethod] w *SavableBootstrap nie jest zakomentowany.");
            }

            Log.Info($"[SaveLoadServiceBootstrap] Stack ready. Modules registered: {SaveRegistry.Count}, " +
                     $"Save folder: {((LocalDiskStorage)Storage).SaveFolder}");
        }

        public static void ResetRegisteredModulesForNewGame()
        {
            int ok = 0;
            int failed = 0;
            var modules = new List<ISavable>(SaveRegistry.All);
            foreach (var module in modules)
            {
                if (module == null) continue;
                try
                {
                    module.InitializeDefault();
                    ok++;
                }
                catch (System.Exception e)
                {
                    failed++;
                    Log.Error($"[SaveLoadServiceBootstrap] InitializeDefault('{module.ModuleId}') for new game threw: {e.Message}");
                }
            }

            Log.Info($"[SaveLoadServiceBootstrap] New game runtime reset complete: {ok} module(s), {failed} failed.");
        }
    }
}
