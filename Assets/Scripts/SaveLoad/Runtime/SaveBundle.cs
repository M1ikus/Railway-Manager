using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-6: In-memory model save bundle'a.
    ///
    /// Format na dysku to gzipped tarball-style: pojedynczy `.rmsave` (gzip)
    /// zawiera serialized JSON manifestu + per-moduł JObject sections.
    /// W pamięci wszystko trzymamy jako JObject (stringly-typed)
    /// żeby migrator chain mógł operować bez deserialize/reserialize.
    ///
    /// Atomowy zapis: pisz do `.tmp` → fsync → rename → atomic. (LocalDiskStorage)
    /// </summary>
    public class SaveBundle
    {
        public SaveManifest Manifest { get; set; } = new SaveManifest();
        public Dictionary<string, JObject> Modules { get; set; } = new Dictionary<string, JObject>();

        public void AddModule(string moduleId, int schemaVersion, JObject json)
        {
            Modules[moduleId] = json;
            Manifest.ModuleVersions[moduleId] = schemaVersion;
        }

        public bool TryGetModule(string moduleId, out JObject json, out int sourceVersion)
        {
            json = null;
            sourceVersion = 0;
            if (!Modules.TryGetValue(moduleId, out json)) return false;
            Manifest.ModuleVersions.TryGetValue(moduleId, out sourceVersion);
            return true;
        }
    }

    /// <summary>
    /// Manifest bundle'a — meta-info + lista modułów + ich wersje + HMAC podpis.
    /// Serializowane jako `manifest.json` w bundle (pierwszy plik dekompresji).
    /// </summary>
    public class SaveManifest
    {
        /// <summary>Wersja gry która zapisała bundle. "0.13.0-alpha" itp.</summary>
        public string GameVersion { get; set; } = "0.0.0";

        /// <summary>Wersja całego formatu bundle'a (nie konkretnego modułu).
        /// Bumpowane gdy zmienia się sama struktura bundle (np. dodano stat.json).</summary>
        public int BundleSchemaVersion { get; set; } = 1;

        /// <summary>Łączny czas gry w sekundach (real-time playtime, do wyświetlenia w SaveLoadUI).</summary>
        public double Playtime { get; set; }

        /// <summary>Aktualna data w grze w momencie zapisu (ISO format).</summary>
        public string GameTimeIso { get; set; } = "";

        /// <summary>Timestamp UTC kiedy save został zapisany (do sortowania w SaveLoadUI).</summary>
        public string SavedAt { get; set; } = "";

        /// <summary>Typ save'a — patrz <see cref="SaveTypes"/> dla const'ów.</summary>
        public string SaveType { get; set; } = SaveTypes.Manual;

        /// <summary>Nazwa wyświetlana gracza (np. "Przed reformą rozkładu"). Może być pusty
        /// dla auto-saves (UI generuje "Auto-save 2026-12-23 20:15").</summary>
        public string SlotName { get; set; } = "";

        /// <summary>Per-module schemaVersion w momencie zapisu. Klucz = moduleId, value = wersja.
        /// Używane przez MigrationRunner do określenia czy potrzebna migracja.</summary>
        public Dictionary<string, int> ModuleVersions { get; set; } = new Dictionary<string, int>();

        /// <summary>HMAC SHA256 podpis bundle'a. Modyfikacja jakiegokolwiek pliku → mismatch
        /// → ostrzeżenie "modified save". Wyliczane na końcu Save (po wszystkich AddModule).</summary>
        public string Hmac { get; set; } = "";
    }
}
