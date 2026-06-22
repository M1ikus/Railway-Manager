using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RailwayManager.Core;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-12: Runner migrujący JObject z sourceVersion → targetVersion przez chain
    /// pojedynczych <see cref="IMigrator"/> instancji.
    ///
    /// Auto-discovery: w pierwszym wywołaniu <see cref="Migrate"/> skanuje wszystkie
    /// załadowane assemblies za typami implementującymi IMigrator. Tworzy instancje
    /// (parameterless ctor) i grupuje per ModuleId.
    ///
    /// Algorytm: znajdź migrator z SourceVersion == current → wywołaj Migrate →
    /// current = migrator.TargetVersion. Powtarzaj dopóki current < target.
    /// Jeśli brakuje migratora z aktualnego current (gap), rzuca
    /// <see cref="MigrationGapException"/>.
    ///
    /// Pre-EA policy: gap → SaveOrchestrator wraca PartialLoad + log warning.
    /// Post-EA: gap = hard error (release procedure musi mieć migratory).
    /// </summary>
    public static class MigrationRunner
    {
        // moduleId → list of migrators (sorted by SourceVersion ascending)
        private static Dictionary<string, List<IMigrator>> _migratorsByModule;

        /// <summary>Migruje JObject z sourceVersion do targetVersion przez chain.
        /// Wraca zmodyfikowany JObject. Throws MigrationGapException jeśli brak migratora
        /// dla pewnego skoku.</summary>
        public static JObject Migrate(string moduleId, int sourceVersion, int targetVersion, JObject input)
        {
            if (sourceVersion == targetVersion) return input;
            if (sourceVersion > targetVersion)
            {
                Log.Warn($"[MigrationRunner] Module '{moduleId}' source > target ({sourceVersion} > {targetVersion}) " +
                         $"— save z nowszej wersji niż gra. Brak downgrade migrators (forward-only).");
                return input;
            }

            EnsureDiscovered();

            var data = input;
            int current = sourceVersion;
            int safetyLimit = 100; // anti infinite loop

            while (current < targetVersion && safetyLimit-- > 0)
            {
                var migrator = FindMigrator(moduleId, current);
                if (migrator == null)
                {
                    throw new MigrationGapException(moduleId, current, targetVersion);
                }
                Log.Info($"[MigrationRunner] Module '{moduleId}' v{current} → v{migrator.TargetVersion}");
                data = migrator.Migrate(data);
                current = migrator.TargetVersion;
            }

            if (safetyLimit <= 0)
                throw new InvalidOperationException(
                    $"[MigrationRunner] Module '{moduleId}' migration loop didn't terminate (safety limit reached)");

            return data;
        }

        /// <summary>Czy moduł ma jakieś zarejestrowane migratory. Diagnostyka.</summary>
        public static bool HasMigratorsFor(string moduleId)
        {
            EnsureDiscovered();
            return _migratorsByModule.ContainsKey(moduleId);
        }

        /// <summary>Pełna lista migratorów dla modułu. Diagnostyka.</summary>
        public static IReadOnlyList<IMigrator> GetMigratorsFor(string moduleId)
        {
            EnsureDiscovered();
            return _migratorsByModule.TryGetValue(moduleId, out var list) ? list : new List<IMigrator>();
        }

        /// <summary>Re-discovery (dla testów lub po hot-reload edytora).</summary>
        public static void Reset()
        {
            _migratorsByModule = null;
        }

        // ── Internal ─────────────────────────────────────────────────

        private static void EnsureDiscovered()
        {
            if (_migratorsByModule != null) return;
            _migratorsByModule = new Dictionary<string, List<IMigrator>>();

            int found = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null) continue;
                    if (type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(IMigrator).IsAssignableFrom(type)) continue;

                    IMigrator instance;
                    try { instance = (IMigrator)Activator.CreateInstance(type); }
                    catch (Exception e)
                    {
                        Log.Warn($"[MigrationRunner] Can't instantiate {type.Name}: {e.Message}");
                        continue;
                    }

                    if (!_migratorsByModule.TryGetValue(instance.ModuleId, out var list))
                    {
                        list = new List<IMigrator>();
                        _migratorsByModule[instance.ModuleId] = list;
                    }
                    list.Add(instance);
                    found++;
                }
            }

            // Sort each module's list by SourceVersion (ascending) — chain traversal expects this
            foreach (var kv in _migratorsByModule)
                kv.Value.Sort((a, b) => a.SourceVersion.CompareTo(b.SourceVersion));

            // TD-012: wykryj duplikaty (ModuleId, SourceVersion). FindMigrator robi silent first-wins,
            // więc bez tego ostrzeżenia drugi migrator dla tej samej wersji byłby cicho ignorowany
            // (subtelny bug post-EA gdy ktoś doda nowy migrator obok istniejącego). Lista jest już
            // posortowana po SourceVersion, więc duplikaty są sąsiednie.
            foreach (var kv in _migratorsByModule)
            {
                var list = kv.Value;
                for (int i = 1; i < list.Count; i++)
                {
                    if (list[i].SourceVersion == list[i - 1].SourceVersion)
                    {
                        Log.Warn($"[MigrationRunner] DUPLIKAT migratora dla modułu '{kv.Key}' " +
                                 $"SourceVersion={list[i].SourceVersion}: '{list[i - 1].GetType().Name}' " +
                                 $"oraz '{list[i].GetType().Name}'. FindMigrator użyje pierwszego (po sort) — " +
                                 $"drugi zignorowany. Usuń jeden.");
                    }
                }
            }

            Log.Info($"[MigrationRunner] Discovered {found} migrator(s) across {_migratorsByModule.Count} module(s)");
        }

        private static IMigrator FindMigrator(string moduleId, int currentVersion)
        {
            if (!_migratorsByModule.TryGetValue(moduleId, out var list)) return null;
            foreach (var m in list)
                if (m.SourceVersion == currentVersion) return m;
            return null;
        }
    }

    /// <summary>Brak migratora w chain'ie dla pewnego skoku wersji.
    /// SaveOrchestrator łapie + obsługuje jako PartialLoad failure.</summary>
    public class MigrationGapException : Exception
    {
        public string ModuleId { get; }
        public int FromVersion { get; }
        public int TargetVersion { get; }

        public MigrationGapException(string moduleId, int fromVersion, int targetVersion)
            : base($"No migrator from v{fromVersion} for module '{moduleId}' (target v{targetVersion})")
        {
            ModuleId = moduleId;
            FromVersion = fromVersion;
            TargetVersion = targetVersion;
        }
    }
}
