using System.Collections.Generic;
using UnityEngine;
using formap;
using MapSystem;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Lekka reprezentacja stacji kolejowej — do użytku w kreatorze tras.
    /// Źródłem są POI z tagiem railway=station|halt w warstwie POIs pliku .bin.
    /// </summary>
    public class RailwayStation
    {
        public int stationId;
        public string name;
        public Vector2 position;
        public bool isMajorStation;        // railway=station (true) vs railway=halt (false)
        public int pathNodeId = -1;        // przypisany węzeł grafu pathfindingu (lub -1)

        /// <summary>Województwo (z VoivodeshipResolver, wypełniane opcjonalnie w LoadFrom).</summary>
        public string voivodeship;

        /// <summary>Nazwa miasta do którego stacja należy (heurystyka: prefix przed pierwszą spacją).</summary>
        public string cityName;

        /// <summary>
        /// M-DLC foundation 2026-05-04: ISO 3166-1 alpha-2 kod kraju ("PL", "DE", "CZ"...).
        /// Wypełniany w <see cref="StationStreamProcessor.Finalize"/> przez <see cref="VoivodeshipResolver.GetCountryCode"/>.
        /// Używane do filtrowania content per <see cref="GameState.ActiveDlcCountries"/>.
        /// Patrz: docs/design/dlc-multi-country.md
        /// </summary>
        public string countryCode;
    }

    /// <summary>
    /// Streaming processor — akumuluje POIs (railway=station|halt) per tile, deduplikuje
    /// po name+pozycji. Snap do graph + voivodeship resolved w <see cref="Finalize"/>.
    /// </summary>
    public class StationStreamProcessor
    {
        private readonly List<(string name, Vector2 pos, bool isMajor)> _raw = new();
        private readonly HashSet<string> _seen = new();

        public void OnTile(Dictionary<BinaryFormat.LayerType, List<MeshGeometry>> layers)
        {
            if (!layers.TryGetValue(BinaryFormat.LayerType.POIs, out var features)) return;
            foreach (var feature in features)
            {
                if (feature?.Vertices == null || feature.Vertices.Count == 0) continue;
                if (feature.Metadata == null) continue;
                if (!feature.Metadata.TryGetValue("railway", out var railwayType)) continue;

                bool isMajor = railwayType == "station";
                bool isHalt = railwayType == "halt";
                if (!isMajor && !isHalt) continue;

                feature.Metadata.TryGetValue("name", out var name);
                Vector2 pos = feature.Vertices[0];

                string key = $"{name}|{pos.x:F0}|{pos.y:F0}";
                if (!_seen.Add(key)) continue;

                _raw.Add((name ?? "(bez nazwy)", pos, isMajor));
            }
        }

        public List<RailwayStation> Finalize(PathfindingGraph pathGraph, float maxSnapRadiusM, VoivodeshipResolver resolver)
        {
            var result = new List<RailwayStation>(_raw.Count);
            int nextId = 1;
            int unmatched = 0;
            int withVoivodeship = 0;

            foreach (var (name, pos, isMajor) in _raw)
            {
                int nodeId = pathGraph.FindNearestNode(pos, maxSnapRadiusM);
                if (nodeId < 0) unmatched++;

                var station = new RailwayStation
                {
                    stationId = nextId++,
                    name = name,
                    position = pos,
                    isMajorStation = isMajor,
                    pathNodeId = nodeId,
                    cityName = ExtractCityNamePublic(name)
                };

                if (resolver != null)
                {
                    station.voivodeship = resolver.GetVoivodeship(pos);
                    if (station.voivodeship != null) withVoivodeship++;
                    // M-DLC foundation: wypelnij countryCode (ISO 3166-1) dla filtrowania per active DLC
                    station.countryCode = resolver.GetCountryCode(pos);
                }

                result.Add(station);
            }

            Log.Info($"[StationStreamProcessor] Loaded {result.Count} stations ({unmatched} without nearby track, "
                     + (resolver != null ? $"{withVoivodeship} with voivodeship" : "no resolver") + ")");
            return result;
        }

        private static string ExtractCityNamePublic(string stationName)
        {
            if (string.IsNullOrEmpty(stationName)) return null;
            int spaceIdx = stationName.IndexOf(' ');
            return spaceIdx < 0 ? stationName : stationName.Substring(0, spaceIdx);
        }
    }

    /// <summary>
    /// Ładuje stacje kolejowe z warstwy POIs pliku .bin i przypina je do PathfindingGraph.
    /// </summary>
    public static class StationLoader
    {
        /// <summary>
        /// Ładuje stacje z warstwy POIs, filtruje po railway=station|halt,
        /// przypisuje każdej najbliższy węzeł pathfindingu w promieniu maxSnapRadiusM.
        /// Opcjonalnie wypełnia pole voivodeship przez VoivodeshipResolver i cityName
        /// przez heurystykę prefix.
        /// </summary>
        public static List<RailwayStation> LoadFrom(
            MapLoader mapLoader,
            PathfindingGraph pathGraph,
            float maxSnapRadiusM = 200f,
            VoivodeshipResolver resolver = null)
        {
            var result = new List<RailwayStation>();
            if (mapLoader == null || pathGraph == null)
            {
                Log.Warn("[StationLoader] Map loader or path graph is null");
                return result;
            }

            var pois = mapLoader.GetAllFeaturesAcrossTiles(BinaryFormat.LayerType.POIs);
            if (pois == null || pois.Count == 0)
            {
                Log.Info("[StationLoader] No POIs in current map");
                return result;
            }

            int nextId = 1;
            int unmatched = 0;
            int withVoivodeship = 0;
            var seen = new HashSet<string>();

            foreach (var feature in pois)
            {
                if (feature.Vertices == null || feature.Vertices.Count == 0) continue;
                if (feature.Metadata == null) continue;

                if (!feature.Metadata.TryGetValue("railway", out var railwayType)) continue;
                bool isMajor = railwayType == "station";
                bool isHalt = railwayType == "halt";
                if (!isMajor && !isHalt) continue;

                feature.Metadata.TryGetValue("name", out var name);
                Vector2 pos = feature.Vertices[0];

                // Dedupe: ten sam POI feature może być w kilku sąsiadujących tile
                string key = $"{name}|{pos.x:F0}|{pos.y:F0}";
                if (!seen.Add(key)) continue;

                int nodeId = pathGraph.FindNearestNode(pos, maxSnapRadiusM);
                if (nodeId < 0) unmatched++;

                var station = new RailwayStation
                {
                    stationId = nextId++,
                    name = name ?? "(bez nazwy)",
                    position = pos,
                    isMajorStation = isMajor,
                    pathNodeId = nodeId,
                    cityName = ExtractCityName(name)
                };

                if (resolver != null)
                {
                    station.voivodeship = resolver.GetVoivodeship(pos);
                    if (station.voivodeship != null) withVoivodeship++;
                }

                result.Add(station);
            }

            Log.Info($"[StationLoader] Loaded {result.Count} stations "
                     + $"({unmatched} without nearby track node, tolerance {maxSnapRadiusM}m"
                     + (resolver != null ? $"; {withVoivodeship} with voivodeship" : "")
                     + ")");
            return result;
        }

        /// <summary>
        /// Heurystyka ekstrakcji nazwy miasta z nazwy stacji.
        /// "Warszawa Centralna" → "Warszawa", "Kraków Główny" → "Kraków", "Olsztyn" → "Olsztyn".
        /// Obsługuje wieloczłonowe nazwy miast — bierze pierwszy token oddzielony spacją.
        /// (Niedoskonałe dla "Biała Podlaska Miasto" itp., ale jako heurystyka wystarczy.)
        /// </summary>
        private static string ExtractCityName(string stationName)
        {
            if (string.IsNullOrEmpty(stationName)) return null;
            int spaceIdx = stationName.IndexOf(' ');
            return spaceIdx < 0 ? stationName : stationName.Substring(0, spaceIdx);
        }
    }
}
