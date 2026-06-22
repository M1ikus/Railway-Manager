using System.IO;
using UnityEngine;

namespace RailwayManager.Core
{
    /// <summary>
    /// Centralna prawda o ścieżkach plików w grze. Wcześniej hardcoded
    /// `Application.persistentDataPath` / `Application.streamingAssetsPath`
    /// rozsiane po 17+ plikach z duplikowanymi <c>"Fleet"</c> / <c>"Maps/Poland"</c>
    /// segmentami — refactor pojedynczego (np. dla Linux Steam Proton, dla testów
    /// z sandbox path) wymagał edycji w wielu miejscach.
    ///
    /// Konwencja:
    /// - <b>Persistent</b> (writable, user content + save'y): `SavesDir`, `LogsDir`,
    ///   `CustomFurnitureDir`, `CustomSchemasDir`
    /// - <b>Streaming</b> (read-only built-in content): `FleetCatalogDir`,
    ///   `EconomyCatalogDir`, `TimetableDataDir`, `BuiltinSchemasDir`,
    ///   `BuiltinFurnitureCatalogPath`, `MapsDir`, `PolandMapsDir`
    ///
    /// <see cref="EnsureCreated"/> wywołać raz na bootstrap — tworzy katalogi
    /// persistent jeśli nie istnieją. StreamingAssets są read-only (paczkowane
    /// w build), nie tworzymy.
    /// </summary>
    public static class AppPaths
    {
        // ── Root paths ─────────────────────────────────────────────────

        /// <summary>User-writable katalog (saves, logs, user content). Per-user, per-machine.</summary>
        public static string PersistentRoot => Application.persistentDataPath;

        /// <summary>Read-only built-in content paczkowany w build. <b>Nie pisz tutaj</b> — na Androidzie to .apk asset, nie file system.</summary>
        public static string StreamingRoot => Application.streamingAssetsPath;

        // ── User-writable (persistent) ────────────────────────────────

        /// <summary>Save'y `.rmsave` (LocalDiskStorage). Pre-EA: lokalne, M14 + Steam Cloud sync.</summary>
        public static string SavesDir => Path.Combine(PersistentRoot, "Saves");

        /// <summary>CSV z PerfStressBootstrap, diagnostyczne dumps.</summary>
        public static string LogsDir => Path.Combine(PersistentRoot, "Logs");

        /// <summary>User-created furniture catalogs (M-Furniture, post-1.0 mod support).</summary>
        public static string CustomFurnitureDir => Path.Combine(PersistentRoot, "CustomFurniture");

        /// <summary>User-created turnout schemas (M-DepotTools snapshot save).</summary>
        public static string CustomSchemasDir => Path.Combine(PersistentRoot, "CustomSchemas");

        // ── Built-in catalogs (StreamingAssets, read-only) ────────────

        /// <summary>Fleet JSON catalogs: fleet_catalog, decal_catalog, external_workshops, inspection_intervals, modernization_paths, paint_presets, vehicle_modifications.</summary>
        public static string FleetCatalogDir => Path.Combine(StreamingRoot, "Fleet");

        /// <summary>Economy: subsidy_rules, demand_overrides.</summary>
        public static string EconomyCatalogDir => Path.Combine(StreamingRoot, "Economy");

        /// <summary>Timetable data: station_tracks itd.</summary>
        public static string TimetableDataDir => Path.Combine(StreamingRoot, "TimetableData");

        /// <summary>Built-in turnout schemas (Ladder/Throat/Scissors/Trapez built-in JSON-y).</summary>
        public static string BuiltinSchemasDir => Path.Combine(StreamingRoot, "DepotSchemas", "Builtin");

        /// <summary>Built-in furniture catalog (pojedynczy plik, nie folder).</summary>
        public static string BuiltinFurnitureCatalogPath => Path.Combine(StreamingRoot, "Furniture", "builtin_catalog.json");

        /// <summary>Mapy OSM binary v7/v8 (root) — generic maps. Konkretne kraje w sub-katalogach.</summary>
        public static string MapsDir => Path.Combine(StreamingRoot, "Maps");

        /// <summary>Mapy Polski (poland-v8.bin aktualna + poland-v7.bin legacy, warminsko-mazurskie-v7.bin, init-state-pl.bin).</summary>
        public static string PolandMapsDir => Path.Combine(MapsDir, "Poland");

        // ── Utility ───────────────────────────────────────────────────

        /// <summary>
        /// Tworzy katalogi persistent jeśli nie istnieją. Wywołać raz na bootstrap
        /// (idealnie z Core/Bootstrap orchestrator gdy powstanie). Idempotent.
        ///
        /// StreamingAssets NIE są tworzone — to read-only build content.
        /// </summary>
        public static void EnsureCreated()
        {
            Directory.CreateDirectory(SavesDir);
            Directory.CreateDirectory(LogsDir);
            Directory.CreateDirectory(CustomFurnitureDir);
            Directory.CreateDirectory(CustomSchemasDir);
        }
    }
}
