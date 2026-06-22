using System.Collections.Generic;
using formap;
using MapSystem;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Streaming processor — akumuluje CityPlace per tile (incremental), deduplikuje po name+type.
    /// Voivodeship resolved w <see cref="Finalize"/> (resolver zbudowany po stream completion).
    /// </summary>
    public class PlaceStreamProcessor
    {
        private readonly List<CityPlace> _result = new();
        private readonly HashSet<string> _seen = new();
        private int _cities, _towns, _villages;

        public void OnTile(Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> layers)
        {
            if (!layers.TryGetValue(BinaryFormat.LayerType.Places, out var features)) return;
            foreach (var feature in features)
            {
                if (feature?.Metadata == null || feature.Vertices == null || feature.Vertices.Count == 0) continue;

                feature.Metadata.TryGetValue("place", out var placeStr);
                if (!TryParsePlaceType(placeStr, out var placeType)) continue;

                feature.Metadata.TryGetValue("name", out var name);
                if (string.IsNullOrEmpty(name)) continue;

                string key = $"{name}|{placeType}";
                if (!_seen.Add(key)) continue;

                feature.Metadata.TryGetValue("population", out var popStr);
                int.TryParse(popStr, out int population);

                _result.Add(new CityPlace
                {
                    name = name,
                    position = feature.Vertices[0],
                    type = placeType,
                    population = population
                    // voivodeship resolved w Finalize (resolver dostępny później)
                });

                if (placeType == PlaceType.City) _cities++;
                else if (placeType == PlaceType.Town) _towns++;
                else _villages++;
            }
        }

        public List<CityPlace> Finalize(VoivodeshipResolver resolver = null)
        {
            if (resolver != null)
            {
                foreach (var p in _result)
                {
                    p.voivodeship = resolver.GetVoivodeship(p.position);
                    // M-DLC foundation: wypelnij countryCode (ISO 3166-1) dla filtrowania per active DLC
                    p.countryCode = resolver.GetCountryCode(p.position);
                }
            }

            Log.Info($"[PlaceStreamProcessor] Loaded {_result.Count} unique places "
                     + $"({_cities} cities, {_towns} towns, {_villages} villages)");
            return _result;
        }

        private static bool TryParsePlaceType(string raw, out PlaceType type)
        {
            switch (raw)
            {
                case "city":    type = PlaceType.City;    return true;
                case "town":    type = PlaceType.Town;    return true;
                case "village": type = PlaceType.Village; return true;
                default:        type = default;           return false;
            }
        }
    }

    /// <summary>
    /// Ładuje miasta/miasteczka/wsie z warstwy Places .bin (place=city|town|village).
    /// Deduplikacja po nazwie (formap replikuje feature do każdego tile który BBox przecina).
    /// Opcjonalnie wypełnia pole voivodeship przez VoivodeshipResolver.
    /// </summary>
    public static class PlaceLoader
    {
        public static List<CityPlace> LoadFrom(MapLoader mapLoader, VoivodeshipResolver resolver = null)
        {
            var result = new List<CityPlace>();
            if (mapLoader == null)
            {
                Log.Warn("[PlaceLoader] MapLoader is null");
                return result;
            }

            var features = mapLoader.GetAllFeaturesAcrossTiles(BinaryFormat.LayerType.Places);
            if (features == null || features.Count == 0)
            {
                Log.Info("[PlaceLoader] No places in current map");
                return result;
            }

            var seen = new HashSet<string>();
            int cities = 0, towns = 0, villages = 0;

            foreach (var feature in features)
            {
                if (feature?.Metadata == null || feature.Vertices == null || feature.Vertices.Count == 0)
                    continue;

                feature.Metadata.TryGetValue("place", out var placeStr);
                if (!TryParsePlaceType(placeStr, out var placeType))
                    continue;

                feature.Metadata.TryGetValue("name", out var name);
                if (string.IsNullOrEmpty(name))
                    continue;

                // Deduplication key: name + placeType (bo np. "Góra" może być village w jednym
                // województwie i town w innym — pozwalamy obu)
                string key = $"{name}|{placeType}";
                if (!seen.Add(key)) continue;

                feature.Metadata.TryGetValue("population", out var popStr);
                int.TryParse(popStr, out int population);

                var cp = new CityPlace
                {
                    name = name,
                    position = feature.Vertices[0],
                    type = placeType,
                    population = population,
                    voivodeship = resolver != null ? resolver.GetVoivodeship(feature.Vertices[0]) : null,
                    countryCode = resolver != null ? resolver.GetCountryCode(feature.Vertices[0]) : null
                };
                result.Add(cp);

                if (placeType == PlaceType.City) cities++;
                else if (placeType == PlaceType.Town) towns++;
                else villages++;
            }

            Log.Info($"[PlaceLoader] Loaded {result.Count} unique places "
                     + $"({cities} cities, {towns} towns, {villages} villages)");
            return result;
        }

        private static bool TryParsePlaceType(string raw, out PlaceType type)
        {
            switch (raw)
            {
                case "city":    type = PlaceType.City;    return true;
                case "town":    type = PlaceType.Town;    return true;
                case "village": type = PlaceType.Village; return true;
                default:        type = default;           return false;
            }
        }
    }
}
