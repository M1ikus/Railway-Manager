using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// B1: Wspólny helper do ładowania katalogu z JSONa w `StreamingAssets/Fleet/`.
    ///
    /// Wzorzec powtarzający się w ExternalWorkshopCatalog / ModernizationPathCatalog /
    /// VehicleModificationCatalog: try/catch + File.Exists + JsonUtility.FromJson +
    /// per-entry validation + Log.Info/Warn/Error. Wcześniej ~25 linii boilerplate
    /// per katalog, teraz jedno wywołanie.
    ///
    /// Konsumenci nadal trzymają własną listę (zachowanie static-class semantics).
    /// </summary>
    public static class JsonCatalogLoader
    {
        /// <summary>
        /// Ładuje JSON z `AppPaths.FleetCatalogDir/{fileName}` używając `TWrapper`.
        /// `selector(wrapper)` zwraca listę itemów. `isValid(item)` filtruje (np. non-empty key).
        /// Zwraca pustą listę gdy plik brak / parse fail (loguje przyczynę).
        /// </summary>
        public static List<TItem> LoadList<TWrapper, TItem>(
            string fileName,
            Func<TWrapper, List<TItem>> selector,
            Func<TItem, bool> isValid,
            string logTag)
            where TWrapper : class
            where TItem : class
        {
            var result = new List<TItem>();
            string path = Path.Combine(AppPaths.FleetCatalogDir, fileName);

            if (!File.Exists(path))
            {
                Log.Warn($"[{logTag}] File not found: {path}");
                return result;
            }

            try
            {
                var parsed = JsonUtility.FromJson<TWrapper>(File.ReadAllText(path));
                var items = parsed != null ? selector(parsed) : null;
                if (items == null) return result;

                foreach (var item in items)
                {
                    if (item == null) continue;
                    if (isValid != null && !isValid(item)) continue;
                    result.Add(item);
                }
                Log.Info($"[{logTag}] Loaded {result.Count} items from {fileName}");
            }
            catch (Exception e)
            {
                Log.Error($"[{logTag}] Load failed for {fileName}: {e.Message}");
            }

            return result;
        }
    }
}
