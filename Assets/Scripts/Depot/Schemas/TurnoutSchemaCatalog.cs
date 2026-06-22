using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DepotSystem.Schemas.Generators;
using RailwayManager.Core;

namespace DepotSystem.Schemas
{
    /// <summary>
    /// Statyczny katalog schematów głowic rozjazdowych. Wzorowany na <c>FleetCatalog</c>.
    ///
    /// Multi-source loader (decyzja C2 z spec'a):
    /// 1. <b>Built-in</b> — <c>StreamingAssets/DepotSchemas/Builtin/*.rmschema.json</c>
    ///    (ship'owane z grą, nieedytowalne in-place; gracz może tweak'nąć i save jako custom)
    /// 2. <b>User custom</b> — <c>{persistentDataPath}/CustomSchemas/*.rmschema.json</c>
    ///    (na Windows: <c>C:\Users\X\AppData\LocalLow\DefaultCompany\RailwayManager\CustomSchemas\</c>)
    /// 3. <b>Workshop</b> — placeholder w EA, podłączenie Steamworks SDK w M14
    ///
    /// API:
    /// - <see cref="LoadAll"/> — single-shot load wszystkich źródeł (wzorzec FleetCatalog.LoadAll)
    /// - <see cref="Reload"/> — wymusza reload (po SaveUser/DeleteUser)
    /// - <see cref="SaveUser"/> / <see cref="DeleteUser"/> — pisze/usuwa user custom JSON
    /// - <see cref="FindById"/> — lookup w aggregate AllSchemas
    /// </summary>
    public static class TurnoutSchemaCatalog
    {
        public static IReadOnlyList<TurnoutSchemaDefinition> BuiltIn => _builtIn;
        public static IReadOnlyList<TurnoutSchemaDefinition> UserCustom => _userCustom;
        public static IReadOnlyList<TurnoutSchemaDefinition> Workshop => _workshop;

        /// <summary>Aggregate wszystkich źródeł. Built-in najpierw, potem user, potem workshop.</summary>
        public static IReadOnlyList<TurnoutSchemaDefinition> AllSchemas => _allSchemas;

        public static bool IsLoaded { get; private set; }

        // Built-in path + user folder zarządzane przez <see cref="AppPaths"/>
        // (BuiltinSchemasDir, CustomSchemasDir).

        /// <summary>Rozszerzenie plików schematów.</summary>
        public const string FileExtension = ".rmschema.json";

        private static readonly List<TurnoutSchemaDefinition> _builtIn = new();
        private static readonly List<TurnoutSchemaDefinition> _userCustom = new();
        private static readonly List<TurnoutSchemaDefinition> _workshop = new();
        private static readonly List<TurnoutSchemaDefinition> _allSchemas = new();

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
            Log.Info($"[TurnoutSchemaCatalog] Loaded: {_builtIn.Count} built-in + {_userCustom.Count} user custom + {_workshop.Count} workshop = {_allSchemas.Count} total");
        }

        /// <summary>
        /// Wymusza reload wszystkich źródeł (np. po SaveUser/DeleteUser zewnątrznym).
        /// </summary>
        public static void Reload()
        {
            IsLoaded = false;
            _builtIn.Clear();
            _userCustom.Clear();
            _workshop.Clear();
            _allSchemas.Clear();
            LoadAll();
        }

        // ════════════════════════════════════════
        //  PRIVATE — per-source loaders
        // ════════════════════════════════════════

        private static void LoadBuiltIn()
        {
            string folder = AppPaths.BuiltinSchemasDir;
            if (!Directory.Exists(folder))
            {
                Log.Warn($"[TurnoutSchemaCatalog] Built-in folder not found: {folder}");
                return;
            }

            var files = Directory.GetFiles(folder, "*" + FileExtension, SearchOption.TopDirectoryOnly);
            foreach (var path in files)
            {
                var def = LoadSchemaFile(path, source: "built-in");
                if (def != null) _builtIn.Add(def);
            }
        }

        private static void LoadUserCustom()
        {
            string folder = AppPaths.CustomSchemasDir;
            if (!Directory.Exists(folder))
            {
                // Folder nie musi istnieć w EA — gracz może jeszcze nie zapisać żadnego custom
                // Tworzymy proaktywnie żeby SaveUser miał gdzie pisać
                try { Directory.CreateDirectory(folder); }
                catch (Exception e) { Log.Warn($"[TurnoutSchemaCatalog] Could not create user folder: {e.Message}"); }
                return;
            }

            var files = Directory.GetFiles(folder, "*" + FileExtension, SearchOption.TopDirectoryOnly);
            foreach (var path in files)
            {
                var def = LoadSchemaFile(path, source: "user");
                if (def != null) _userCustom.Add(def);
            }
        }

        private static void LoadWorkshop()
        {
            // M14: skanuj <steamapps>/workshop/content/<appid>/*/schema.json
            // EA: pusty placeholder. Gdy podłączymy Steamworks, dorzucimy tutaj scan.
        }

        private static TurnoutSchemaDefinition LoadSchemaFile(string path, string source)
        {
            try
            {
                string json = File.ReadAllText(path);
                var def = TurnoutSchemaDefinition.FromJson(json);
                if (def == null)
                {
                    Log.Error($"[TurnoutSchemaCatalog] Failed to parse {source}: {path}");
                    return null;
                }

                // Normalize parameters (expand shorthand → array) per generator
                if (def.IsGenerative && def.parameters != null)
                {
                    var generator = TurnoutSchemaGeneratorRegistry.Get(def.ParseCategory());
                    if (generator != null)
                    {
                        int turnoutCount = generator.ComputeTurnoutCount(def.parameters.trackCount);
                        def.parameters.Normalize(turnoutCount);
                    }
                    else
                    {
                        Log.Warn($"[TurnoutSchemaCatalog] No generator for category '{def.category}' in '{def.id}' — parameters not normalized");
                    }
                }

                return def;
            }
            catch (Exception e)
            {
                Log.Error($"[TurnoutSchemaCatalog] Failed to load {source} '{path}': {e.Message}");
                return null;
            }
        }

        private static void RebuildAggregate()
        {
            _allSchemas.Clear();
            // Order: built-in najpierw (by appearance w UI), potem user (najnowsze on top by date),
            // potem workshop. Per source by name alphabetical.
            _builtIn.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            _userCustom.Sort((a, b) => string.Compare(b.modifiedAt, a.modifiedAt, StringComparison.Ordinal));
            _workshop.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            _allSchemas.AddRange(_builtIn);
            _allSchemas.AddRange(_userCustom);
            _allSchemas.AddRange(_workshop);
        }

        // ════════════════════════════════════════
        //  USER CUSTOM SAVE/DELETE
        // ════════════════════════════════════════

        /// <summary>
        /// Zapisuje user custom schemat do <c>{persistentDataPath}/CustomSchemas/{id}.rmschema.json</c>.
        /// Dodaje do <see cref="UserCustom"/> i odświeża <see cref="AllSchemas"/>.
        ///
        /// Zwraca true gdy zapis się udał. Wymaga niepustego <c>id</c>.
        /// </summary>
        public static bool SaveUser(TurnoutSchemaDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.id))
            {
                Log.Error("[TurnoutSchemaCatalog] SaveUser: definition is null or has no id");
                return false;
            }

            // Zaktualizuj modifiedAt
            def.modifiedAt = DateTime.UtcNow.ToString("o");
            if (string.IsNullOrEmpty(def.createdAt)) def.createdAt = def.modifiedAt;

            string folder = AppPaths.CustomSchemasDir;
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                string filename = SanitizeFilename(def.id) + FileExtension;
                string path = Path.Combine(folder, filename);
                string json = def.ToJson();
                File.WriteAllText(path, json);

                // Update in-memory list — replace existing or add new
                int existing = _userCustom.FindIndex(d => d.id == def.id);
                if (existing >= 0) _userCustom[existing] = def;
                else _userCustom.Add(def);

                RebuildAggregate();
                Log.Info($"[TurnoutSchemaCatalog] SaveUser: '{def.id}' written to {path}");
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[TurnoutSchemaCatalog] SaveUser '{def.id}' failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Usuwa user custom schemat (z dysku + listy). Built-in i workshop nie da się usunąć.
        /// Zwraca true gdy usunięcie się udało.
        /// </summary>
        public static bool DeleteUser(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                Log.Error("[TurnoutSchemaCatalog] DeleteUser: id is empty");
                return false;
            }

            int idx = _userCustom.FindIndex(d => d.id == id);
            if (idx < 0)
            {
                Log.Warn($"[TurnoutSchemaCatalog] DeleteUser: '{id}' not found in user custom list");
                return false;
            }

            string folder = AppPaths.CustomSchemasDir;
            string filename = SanitizeFilename(id) + FileExtension;
            string path = Path.Combine(folder, filename);

            try
            {
                if (File.Exists(path)) File.Delete(path);
                _userCustom.RemoveAt(idx);
                RebuildAggregate();
                Log.Info($"[TurnoutSchemaCatalog] DeleteUser: '{id}' removed");
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"[TurnoutSchemaCatalog] DeleteUser '{id}' failed: {e.Message}");
                return false;
            }
        }

        // ════════════════════════════════════════
        //  LOOKUP API
        // ════════════════════════════════════════

        /// <summary>
        /// Lookup po id w aggregate (wszystkie źródła).
        /// Zwraca null gdy nie znaleziony.
        /// </summary>
        public static TurnoutSchemaDefinition FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _allSchemas.Count; i++)
                if (_allSchemas[i].id == id) return _allSchemas[i];
            return null;
        }

        /// <summary>
        /// Lookup tylko w built-in.
        /// </summary>
        public static TurnoutSchemaDefinition FindBuiltIn(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _builtIn.Count; i++)
                if (_builtIn[i].id == id) return _builtIn[i];
            return null;
        }

        /// <summary>
        /// Lookup tylko w user custom.
        /// </summary>
        public static TurnoutSchemaDefinition FindUserCustom(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _userCustom.Count; i++)
                if (_userCustom[i].id == id) return _userCustom[i];
            return null;
        }

        /// <summary>
        /// Filtruje aggregate po kategorii. Używane przez UI list/filter.
        /// </summary>
        public static List<TurnoutSchemaDefinition> FilterByCategory(TurnoutSchemaCategory category)
        {
            var result = new List<TurnoutSchemaDefinition>();
            for (int i = 0; i < _allSchemas.Count; i++)
                if (_allSchemas[i].ParseCategory() == category)
                    result.Add(_allSchemas[i]);
            return result;
        }

        // ════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════

        /// <summary>
        /// Oczyszcza id z znaków niedozwolonych w nazwie pliku Windows/Linux/Mac.
        /// Zastępuje invalid chars na '_'.
        /// </summary>
        public static string SanitizeFilename(string id)
        {
            if (string.IsNullOrEmpty(id)) return "schema";
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(id.Length);
            foreach (char c in id)
            {
                bool isInvalid = false;
                for (int i = 0; i < invalid.Length; i++)
                    if (invalid[i] == c) { isInvalid = true; break; }
                sb.Append(isInvalid ? '_' : c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Zwraca pełną ścieżkę folderu user custom. Używane przez SmokeTest do otwarcia w eksploratorze.
        /// </summary>
        public static string GetUserFolderPath()
        {
            return AppPaths.CustomSchemasDir;
        }

        /// <summary>
        /// Zwraca pełną ścieżkę folderu built-in. Tylko do diagnostyki.
        /// </summary>
        public static string GetBuiltInFolderPath()
        {
            return AppPaths.BuiltinSchemasDir;
        }
    }
}
