using System;
using System.Collections.Generic;
using RailwayManager.Core;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-6: Statyczny registry modułów uczestniczących w save/load.
    ///
    /// Każdy moduł (Fleet/Timetable/Personnel/...) rejestruje się w bootstrapie
    /// (PersonnelServiceBootstrap, TimetableInitializer, etc.) przez
    /// <see cref="Register"/>. SaveOrchestrator iteruje po wszystkich
    /// zarejestrowanych podczas SaveAsync / LoadAsync.
    ///
    /// Static singleton wystarczy — nie ma DI w projekcie. Auto-clear w
    /// edytorze (RuntimeInitializeOnLoadMethod) + manual <see cref="Clear"/>
    /// dla testów.
    /// </summary>
    public static class SaveRegistry
    {
        private static readonly Dictionary<string, ISavable> _modules =
            new Dictionary<string, ISavable>();

        private static readonly string[] ModuleOrder =
        {
            "world",
            "fleet",
            "maintenance",
            "maintenanceJobs",
            "timetable",
            "circulations",
            "trainruns",     // TD-037: po circulations (runy odwołują się do obiegów), przed personnel (CrewDuty linkuje po run-id)
            "passengers",    // TD-037: po trainruns (agenci OnTrain odwołują się do runów)
            "personnel",
            "depot_3d",
            "economy",
            "shared_ui"
        };

        /// <summary>Liczba zarejestrowanych modułów. Diagnostyka.</summary>
        public static int Count => _modules.Count;

        /// <summary>
        /// Lista wszystkich zarejestrowanych modułów w deterministycznej kolejności.
        /// RuntimeInitializeOnLoadMethod nie gwarantuje kolejności rejestracji, a restore
        /// ma zależności cross-module (world/RNG przed innymi, fleet przed maintenance itd.).
        /// </summary>
        public static IEnumerable<ISavable> All => EnumerateOrderedModules();

        /// <summary>Rejestruje moduł. Idempotentne (re-register tego samego ModuleId
        /// zastępuje poprzedni). Wraca true gdy nowy, false gdy zastąpiony.</summary>
        public static bool Register(ISavable module)
        {
            if (module == null)
            {
                Log.Warn("[SaveRegistry] Register called with null");
                return false;
            }
            if (string.IsNullOrEmpty(module.ModuleId))
            {
                Log.Warn($"[SaveRegistry] Register {module.GetType().Name}: empty ModuleId");
                return false;
            }

            bool wasNew = !_modules.ContainsKey(module.ModuleId);
            _modules[module.ModuleId] = module;
            if (wasNew)
                Log.Info($"[SaveRegistry] Registered '{module.ModuleId}' (v{module.SchemaVersion})");
            return wasNew;
        }

        /// <summary>Wyrejestrowuje moduł po ModuleId. Wraca true jeśli był, false jeśli nie był.</summary>
        public static bool Unregister(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return false;
            return _modules.Remove(moduleId);
        }

        /// <summary>Wyszukuje moduł po ModuleId. null jeśli nie zarejestrowany.</summary>
        public static ISavable Get(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId)) return null;
            return _modules.TryGetValue(moduleId, out var m) ? m : null;
        }

        /// <summary>Czyści wszystkie rejestracje. Dla testów + bootstrap reset.</summary>
        public static void Clear()
        {
            int n = _modules.Count;
            _modules.Clear();
            if (n > 0)
                Log.Info($"[SaveRegistry] Cleared {n} module(s)");
        }

        /// <summary>
        /// Wykrywa moduły z <see cref="ModuleOrder"/> które NIE są zarejestrowane.
        /// Wywołać po bootstrap (gdy wszystkie BeforeSceneLoad initializery się wykonały) —
        /// brakujący moduł = save zapisze się bez jego state'u, gracz traci dane bez ostrzeżenia.
        /// Typowy bug: zakomentowany `[RuntimeInitializeOnLoadMethod]` w bootstrapper'ze przy debugowaniu.
        /// </summary>
        public static IReadOnlyList<string> GetMissingFromModuleOrder()
        {
            var missing = new List<string>();
            foreach (var moduleId in ModuleOrder)
            {
                if (!_modules.ContainsKey(moduleId))
                    missing.Add(moduleId);
            }
            return missing;
        }

        private static IEnumerable<ISavable> EnumerateOrderedModules()
        {
            var emitted = new HashSet<string>();
            foreach (var moduleId in ModuleOrder)
            {
                if (_modules.TryGetValue(moduleId, out var module))
                {
                    emitted.Add(moduleId);
                    yield return module;
                }
            }

            var unknownIds = new List<string>();
            foreach (var moduleId in _modules.Keys)
            {
                if (!emitted.Contains(moduleId))
                    unknownIds.Add(moduleId);
            }

            unknownIds.Sort(StringComparer.Ordinal);
            foreach (var moduleId in unknownIds)
                yield return _modules[moduleId];
        }
    }
}
