using System.Collections.Generic;
using formap;
using MapSystem;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Streaming processor — akumuluje AdminRegions per tile (incremental), deduplikuje po name.
    /// Używany przez <see cref="MapLoader.StreamAllTilesSync"/> w TimetableInitializer
    /// — alternative dla one-shot <see cref="AdminBoundaryLoader.LoadFrom"/> na pełnej Polsce
    /// gdzie tile cache nie mieści się w RAM.
    /// </summary>
    public class AdminBoundaryStreamProcessor
    {
        private readonly List<AdminRegion> _result = new();
        private readonly HashSet<string> _seen = new();
        private int _countries, _voivodeships;

        public void OnTile(Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> layers)
        {
            if (!layers.TryGetValue(BinaryFormat.LayerType.AdminBoundaries, out var features)) return;
            foreach (var feature in features)
            {
                if (feature?.Metadata == null || feature.Vertices == null || feature.Indices == null) continue;
                if (feature.Vertices.Count < 3 || feature.Indices.Count < 3) continue;

                feature.Metadata.TryGetValue("name", out var name);
                if (string.IsNullOrEmpty(name)) continue;
                if (!_seen.Add(name)) continue;

                feature.Metadata.TryGetValue("admin_level", out var levelStr);
                int.TryParse(levelStr, out int adminLevel);
                if (adminLevel != 2 && adminLevel != 4) continue;

                feature.Metadata.TryGetValue("ISO3166-1", out var iso1);
                feature.Metadata.TryGetValue("ISO3166-2", out var iso2);

                _result.Add(new AdminRegion
                {
                    name = name,
                    adminLevel = adminLevel,
                    iso3166_1 = iso1,
                    iso3166_2 = iso2,
                    boundingBox = feature.BoundingBox,
                    vertices = new List<UnityEngine.Vector2>(feature.Vertices),
                    indices = new List<int>(feature.Indices)
                });

                if (adminLevel == 2) _countries++;
                else _voivodeships++;
            }
        }

        public List<AdminRegion> Finalize()
        {
            Log.Info($"[AdminBoundaryStreamProcessor] Loaded {_result.Count} unique admin regions "
                     + $"({_countries} countries, {_voivodeships} voivodeships)");
            return _result;
        }
    }

    /// <summary>
    /// Ładuje granice administracyjne (AdminBoundaries layer) z .bin i deduplikuje.
    /// Formap replikuje każdy feature do wszystkich tile które BBox przecina, więc
    /// polygon województwa pojawia się wielokrotnie. Deduplikacja po name tagu
    /// (ewentualnie po osm_id gdyby był dodany w przyszłości).
    /// </summary>
    public static class AdminBoundaryLoader
    {
        /// <summary>
        /// Ładuje wszystkie AdminBoundaries features z MapLoader.GetLayer(AdminBoundaries),
        /// deduplikuje po nazwie i zwraca listę unikalnych AdminRegion.
        /// </summary>
        public static List<AdminRegion> LoadFrom(MapLoader mapLoader)
        {
            var result = new List<AdminRegion>();
            if (mapLoader == null)
            {
                Log.Warn("[AdminBoundaryLoader] MapLoader is null");
                return result;
            }

            var features = mapLoader.GetAllFeaturesAcrossTiles(BinaryFormat.LayerType.AdminBoundaries);
            if (features == null || features.Count == 0)
            {
                Log.Info("[AdminBoundaryLoader] No admin boundaries in current map");
                return result;
            }

            var seen = new HashSet<string>();
            int countries = 0, voivodeships = 0;

            foreach (var feature in features)
            {
                if (feature?.Metadata == null || feature.Vertices == null || feature.Indices == null)
                    continue;
                if (feature.Vertices.Count < 3 || feature.Indices.Count < 3)
                    continue;

                feature.Metadata.TryGetValue("name", out var name);
                if (string.IsNullOrEmpty(name))
                    continue;

                feature.Metadata.TryGetValue("admin_level", out var levelStr);
                int.TryParse(levelStr, out int adminLevel);
                if (adminLevel != 2 && adminLevel != 4)
                    continue; // tylko kraj i województwo

                feature.Metadata.TryGetValue("ISO3166-1", out var iso1);
                feature.Metadata.TryGetValue("ISO3166-2", out var iso2);

                // M-DLC foundation 2026-05-04: dedup per (iso3166_2 ?? name, adminLevel) zamiast
                // tylko po name. W multi-country setup (np. DLC Niemcy) drugi region "Bayern"
                // z germany.bin nie powinien zderzać się z nieistniejącym "Bayern" z poland.bin
                // — ale gdyby dwa kraje miały region o tej samej nazwie (np. Brześć w PL i BY),
                // ISO code ich rozróżnia. Fallback na name dla regionów bez ISO (rare).
                string dedupKey = !string.IsNullOrEmpty(iso2) ? iso2 : (iso1 + ":" + name);
                if (!seen.Add(dedupKey))
                    continue; // duplikat — już załadowane z innego tile

                // Kopia danych żeby nie blokować zwolnienia tile features
                var region = new AdminRegion
                {
                    name = name,
                    adminLevel = adminLevel,
                    iso3166_1 = iso1,
                    iso3166_2 = iso2,
                    boundingBox = feature.BoundingBox,
                    vertices = new List<UnityEngine.Vector2>(feature.Vertices),
                    indices = new List<int>(feature.Indices)
                };
                result.Add(region);

                if (adminLevel == 2) countries++;
                else voivodeships++;
            }

            Log.Info($"[AdminBoundaryLoader] Loaded {result.Count} unique admin regions "
                     + $"({countries} countries, {voivodeships} voivodeships)");
            return result;
        }
    }
}
