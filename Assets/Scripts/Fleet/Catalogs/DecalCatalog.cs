using System;
using System.Collections.Generic;
using System.IO;
using RailwayManager.Core;
using UnityEngine;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-6: Katalog symbolów (decals) dostępnych do umieszczenia w paint editor (M-FC-7).
    /// Ładowany z <c>StreamingAssets/Fleet/decal_catalog.json</c>.
    /// Pre-EA: ~30 symbolów (cyfry, litery, ikony info/warning, strzałki).
    /// </summary>
    public static class DecalCatalog
    {
        public static List<DecalDef> Decals { get; private set; } = new();
        public static bool IsLoaded { get; private set; }

        [Serializable] private class Wrapper { public List<DecalDef> decals = new(); }

        public static void Load()
        {
            if (IsLoaded) return;

            string path = Path.Combine(AppPaths.FleetCatalogDir, "decal_catalog.json");
            if (!File.Exists(path))
            {
                Log.Warn($"[DecalCatalog] Not found: {path}");
                IsLoaded = true;
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var wrapper = JsonUtility.FromJson<Wrapper>(json);
                Decals = wrapper?.decals ?? new List<DecalDef>();
                IsLoaded = true;
                Log.Info($"[DecalCatalog] Loaded {Decals.Count} decals");
            }
            catch (Exception e)
            {
                Log.Error($"[DecalCatalog] Load failed: {e.Message}");
                Decals = new List<DecalDef>();
                IsLoaded = true;
            }
        }

        public static DecalDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var d in Decals) if (d.id == id) return d;
            return null;
        }

        /// <summary>Filtruje decals po kategorii ("info", "warning", "digit", "letter", "arrow", "logo").</summary>
        public static List<DecalDef> ByCategory(string category)
        {
            var result = new List<DecalDef>();
            foreach (var d in Decals)
                if (d.category == category) result.Add(d);
            return result;
        }
    }
}
