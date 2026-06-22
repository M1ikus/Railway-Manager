using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.Furniture
{
    /// <summary>
    /// Statyczny katalog obiektów furniture. Wzorowany na <c>FleetCatalog</c>
    /// i <c>TurnoutSchemaCatalog</c> — single-shot <see cref="LoadAll"/>.
    ///
    /// Multi-source loader (decyzja 2026-05-03):
    /// 1. <b>Built-in</b> — <c>StreamingAssets/Furniture/builtin_catalog.json</c>
    ///    (jeden plik z listą items, ship'owany z grą; gracz może edytować JSON i restart, ale zostanie nadpisany przy update)
    /// 2. <b>User custom</b> — <c>{persistentDataPath}/CustomFurniture/*.json</c>
    ///    (na Windows: <c>C:\Users\X\AppData\LocalLow\DefaultCompany\RailwayManager\CustomFurniture\</c>)
    ///    Każdy plik = JEDEN <see cref="FurnitureItem"/>. Pusty w EA, wypełniany przez user mods post-EA.
    /// 3. <b>Workshop</b> — placeholder w EA, podłączenie Steamworks SDK w M14
    ///
    /// Deduplikacja po <c>id</c>: built-in wygrywa konflikt z user custom / workshop.
    ///
    /// API:
    /// - <see cref="LoadAll"/> — single-shot load wszystkich źródeł (idempotentne)
    /// - <see cref="Reload"/> — wymusza reload (po dodaniu plików user custom)
    /// - <see cref="FindById"/> — lookup w <see cref="AllItems"/>
    /// - <see cref="FilterByCategory"/> — UI library filter (MF-3)
    /// - <see cref="FilterByCompatibility"/> — pokoje compatible (MF-4 placement)
    /// </summary>
    public static class FurnitureCatalog
    {
        public static IReadOnlyList<FurnitureItem> BuiltIn => _builtIn;
        public static IReadOnlyList<FurnitureItem> UserCustom => _userCustom;
        public static IReadOnlyList<FurnitureItem> Workshop => _workshop;

        /// <summary>Aggregate wszystkich źródeł (built-in + user + workshop, po deduplikacji).</summary>
        public static IReadOnlyList<FurnitureItem> AllItems => _allItems;

        public static bool IsLoaded { get; private set; }

        /// <summary>Rozszerzenie plików user custom items.</summary>
        public const string UserFileExtension = ".json";

        // Built-in path + user folder zarządzane przez <see cref="AppPaths"/>
        // (BuiltinFurnitureCatalogPath, CustomFurnitureDir).

        private static readonly List<FurnitureItem> _builtIn = new();
        private static readonly List<FurnitureItem> _userCustom = new();
        private static readonly List<FurnitureItem> _workshop = new();
        private static readonly List<FurnitureItem> _allItems = new();

        // ════════════════════════════════════════
        //  LOAD API
        // ════════════════════════════════════════

        /// <summary>
        /// Single-shot load wszystkich źródeł. Idempotentne — drugie wywołanie no-op
        /// (chyba że <see cref="Reload"/> wcześniej resetowało <see cref="IsLoaded"/>).
        /// </summary>
        public static void LoadAll()
        {
            if (IsLoaded) return;

            LoadBuiltIn();
            LoadUserCustom();
            LoadWorkshop();
            RebuildAggregate();

            IsLoaded = true;
            Log.Info($"[FurnitureCatalog] Loaded: {_builtIn.Count} built-in + {_userCustom.Count} user custom + {_workshop.Count} workshop = {_allItems.Count} total (after dedup)");
        }

        /// <summary>Wymusza reload wszystkich źródeł.</summary>
        public static void Reload()
        {
            IsLoaded = false;
            _builtIn.Clear();
            _userCustom.Clear();
            _workshop.Clear();
            _allItems.Clear();
            LoadAll();
        }

        // ════════════════════════════════════════
        //  PRIVATE — per-source loaders
        // ════════════════════════════════════════

        [Serializable]
        private class CatalogFile
        {
            public int schemaFormatVersion = 1;
            public List<FurnitureItem> items = new();
        }

        private static void LoadBuiltIn()
        {
            string path = AppPaths.BuiltinFurnitureCatalogPath;
            if (!File.Exists(path))
            {
                Log.Warn($"[FurnitureCatalog] Built-in catalog not found: {path}");
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var catalog = JsonUtility.FromJson<CatalogFile>(json);
                if (catalog == null || catalog.items == null)
                {
                    Log.Error($"[FurnitureCatalog] Built-in catalog parse failed or empty: {path}");
                    return;
                }
                foreach (var item in catalog.items)
                {
                    if (item == null || string.IsNullOrEmpty(item.id))
                    {
                        Log.Warn($"[FurnitureCatalog] Skipping built-in item with empty id");
                        continue;
                    }
                    _builtIn.Add(item);
                }
            }
            catch (Exception e)
            {
                Log.Error($"[FurnitureCatalog] Failed to load built-in catalog: {e.Message}");
            }
        }

        private static void LoadUserCustom()
        {
            string folder = AppPaths.CustomFurnitureDir;
            if (!Directory.Exists(folder))
            {
                // Folder nie musi istnieć w EA — gracz może jeszcze nie wrzucić żadnego custom
                // Tworzymy proaktywnie żeby user mods miały gdzie pisać post-EA
                try { Directory.CreateDirectory(folder); }
                catch (Exception e) { Log.Warn($"[FurnitureCatalog] Could not create user folder: {e.Message}"); }
                return;
            }

            var files = Directory.GetFiles(folder, "*" + UserFileExtension, SearchOption.TopDirectoryOnly);
            foreach (var path in files)
            {
                var item = LoadUserItemFile(path);
                if (item != null) _userCustom.Add(item);
            }
        }

        private static void LoadWorkshop()
        {
            // M14: skanuj <steamapps>/workshop/content/<appid>/*/furniture.json
            // EA: pusty placeholder. Gdy podłączymy Steamworks, dorzucimy tutaj scan.
        }

        private static FurnitureItem LoadUserItemFile(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                var item = FurnitureItem.FromJson(json);
                if (item == null)
                {
                    Log.Error($"[FurnitureCatalog] Failed to parse user item: {path}");
                    return null;
                }
                if (string.IsNullOrEmpty(item.id))
                {
                    Log.Warn($"[FurnitureCatalog] User item missing id: {path}");
                    return null;
                }
                return item;
            }
            catch (Exception e)
            {
                Log.Error($"[FurnitureCatalog] Failed to load user item '{path}': {e.Message}");
                return null;
            }
        }

        private static void RebuildAggregate()
        {
            _allItems.Clear();

            // Built-in najpierw (priorytet w deduplikacji), potem user, potem workshop.
            // W każdym source items pozostają w kolejności źródłowej (per-spec design).
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in _builtIn)
            {
                if (seen.Add(item.id)) _allItems.Add(item);
            }
            foreach (var item in _userCustom)
            {
                if (seen.Add(item.id)) _allItems.Add(item);
                else Log.Warn($"[FurnitureCatalog] User custom item '{item.id}' shadowed by built-in (dedup)");
            }
            foreach (var item in _workshop)
            {
                if (seen.Add(item.id)) _allItems.Add(item);
                else Log.Warn($"[FurnitureCatalog] Workshop item '{item.id}' shadowed by built-in/user (dedup)");
            }
        }

        // ════════════════════════════════════════
        //  LOOKUP API
        // ════════════════════════════════════════

        /// <summary>Lookup po id w aggregate (wszystkie źródła). Zwraca null gdy nie znaleziony.</summary>
        public static FurnitureItem FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _allItems.Count; i++)
                if (string.Equals(_allItems[i].id, id, StringComparison.OrdinalIgnoreCase))
                    return _allItems[i];
            return null;
        }

        /// <summary>Filtruje aggregate po kategorii. Używane przez UI library panel (MF-3).</summary>
        public static List<FurnitureItem> FilterByCategory(FurnitureCategory category)
        {
            var result = new List<FurnitureItem>();
            for (int i = 0; i < _allItems.Count; i++)
                if (_allItems[i].ParseCategory() == category)
                    result.Add(_allItems[i]);
            return result;
        }

        /// <summary>Filtruje aggregate po pokoju compatible (MF-4 placement library suggestions).</summary>
        public static List<FurnitureItem> FilterByCompatibility(string roomType)
        {
            var result = new List<FurnitureItem>();
            if (string.IsNullOrEmpty(roomType)) return result;
            for (int i = 0; i < _allItems.Count; i++)
                if (_allItems[i].IsCompatibleWith(roomType))
                    result.Add(_allItems[i]);
            return result;
        }

        // ════════════════════════════════════════
        //  HELPERS — paths (diagnostyka, smoke test)
        // ════════════════════════════════════════

        public static string GetBuiltInPath() => AppPaths.BuiltinFurnitureCatalogPath;
        public static string GetUserFolderPath() => AppPaths.CustomFurnitureDir;
    }
}
