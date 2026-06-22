using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Dane torów stacji z przypisaniem do peronów.
    /// Ładowane z JSON (StreamingAssets/TimetableData/station_tracks.json).
    /// Plik generowany automatycznie z danych OSM, edytowalny ręcznie.
    /// </summary>
    public class StationTrackData
    {
        static readonly string FilePath = Path.Combine(
            AppPaths.TimetableDataDir, "station_tracks.json");

        /// <summary>stationName → trackRef → hasPlatform</summary>
        Dictionary<string, Dictionary<string, bool>> _data;

        public bool IsLoaded => _data != null;

        /// <summary>Ładuje dane z JSON. Jeśli plik nie istnieje, _data = null.</summary>
        public void Load()
        {
            _data = null;
            if (!File.Exists(FilePath))
            {
                Log.Info("[StationTrackData] No file at " + FilePath);
                return;
            }

            try
            {
                string json = File.ReadAllText(FilePath);
                var root = JsonUtility.FromJson<StationTrackRoot>(json);
                if (root?.stations == null) return;

                _data = new Dictionary<string, Dictionary<string, bool>>();
                foreach (var station in root.stations)
                {
                    if (string.IsNullOrEmpty(station.name) || station.tracks == null) continue;
                    var tracks = new Dictionary<string, bool>();
                    foreach (var t in station.tracks)
                        tracks[t.trackRef] = t.hasPlatform;
                    _data[station.name] = tracks;
                }

                Log.Info($"[StationTrackData] Loaded {_data.Count} stations from JSON");
            }
            catch (Exception e)
            {
                Log.Warn($"[StationTrackData] Failed to load: {e.Message}");
            }
        }

        /// <summary>
        /// Zwraca listę torów dla stacji z pliku, lub null jeśli brak danych.
        /// </summary>
        public List<TrackEntry> GetTracks(string stationName)
        {
            if (_data == null) return null;
            if (!_data.TryGetValue(stationName, out var tracks)) return null;

            var result = new List<TrackEntry>();
            foreach (var kv in tracks)
                result.Add(new TrackEntry { trackRef = kv.Key, hasPlatform = kv.Value });
            return result;
        }

        public struct TrackEntry
        {
            public string trackRef;
            public bool hasPlatform;
        }

        /// <summary>
        /// Generuje plik JSON z bieżących danych OSM (perony + edge'e).
        /// Wywołaj raz po załadowaniu mapy.
        /// </summary>
        public static void Generate(TimetableInitializer init)
        {
            if (init?.Graph == null || init.Stations == null)
            {
                Log.Warn("[StationTrackData] Cannot generate — no data");
                return;
            }

            float t0 = Time.realtimeSinceStartup;

            // ── PRE-COMPUTE 1: platformIndex → stationName (najbliższa po sqrMag).
            // Stary kod robił ten lookup PER STACJA → O(S × P × S). Teraz O(P × S) raz,
            // potem O(P) per stacja. Algorytm "najbliższa stacja" identyczny.
            var platformToStation = new string[init.Platforms != null ? init.Platforms.Count : 0];
            if (init.Platforms != null)
            {
                for (int pi = 0; pi < init.Platforms.Count; pi++)
                {
                    var plat = init.Platforms[pi];
                    if (plat.stationNodeId < 0 || plat.stationNodeId >= init.Graph.NodeCount) continue;
                    var platPos = init.Graph.GetNode(plat.stationNodeId).position;

                    float bestDist = float.MaxValue;
                    string bestName = null;
                    foreach (var st in init.Stations)
                    {
                        float d = (st.position - platPos).sqrMagnitude;
                        if (d < bestDist) { bestDist = d; bestName = st.name; }
                    }
                    platformToStation[pi] = bestName;
                }
            }
            float tPreComp = Time.realtimeSinceStartup - t0;

            // ── PRE-COMPUTE 2: stationName → list of platforms (które peron należy do której).
            // Eliminuje O(P) loop wewnątrz loopa po stacjach.
            var stationToPlatforms = new Dictionary<string, List<int>>();
            for (int pi = 0; pi < platformToStation.Length; pi++)
            {
                var stName = platformToStation[pi];
                if (string.IsNullOrEmpty(stName)) continue;
                if (!stationToPlatforms.TryGetValue(stName, out var list))
                {
                    list = new List<int>();
                    stationToPlatforms[stName] = list;
                }
                list.Add(pi);
            }

            var root = new StationTrackRoot { stations = new List<StationEntry>() };
            var nearbyNodes = new List<int>(); // reuse buffer

            foreach (var station in init.Stations)
            {
                if (station.pathNodeId < 0) continue;
                var stationPos = station.position;

                // Zbierz refy z peronów przypisanych do tej stacji (lookup z pre-compute)
                var platformRefs = new HashSet<string>();
                bool hasPlatforms = false;
                if (stationToPlatforms.TryGetValue(station.name, out var platIndices))
                {
                    foreach (int pi in platIndices)
                    {
                        var plat = init.Platforms[pi];
                        hasPlatforms = true;
                        if (!string.IsNullOrEmpty(plat.platformName) && plat.platformName != "?")
                        {
                            foreach (var r in plat.platformName.Split(';'))
                            {
                                var trimmed = r.Trim();
                                if (trimmed.Length > 0) platformRefs.Add(trimmed);
                            }
                        }
                    }
                }
                bool hasRefData = platformRefs.Count > 0;

                // Zbierz tory (edge'e z railway:track_ref) w promieniu 300m — spatial grid lookup
                var graphNodePos = station.pathNodeId >= 0 && station.pathNodeId < init.Graph.NodeCount
                    ? init.Graph.GetNode(station.pathNodeId).position
                    : stationPos;

                const float radiusM = 300f;
                init.Graph.FindNodesInRadius(graphNodePos, radiusM, nearbyNodes);

                var seenRefs = new HashSet<string>();
                var tracks = new List<TrackEntryJson>();

                foreach (int nid in nearbyNodes)
                {
                    var node = init.Graph.GetNode(nid);
                    foreach (var eid in node.edgeIds)
                    {
                        var edge = init.Graph.GetEdge(eid);
                        if (edge.metadata == null) continue;
                        edge.metadata.TryGetValue("railway:track_ref", out var trackRef);
                        if (string.IsNullOrEmpty(trackRef)) continue;
                        if (!seenRefs.Add(trackRef)) continue;

                        // Jeśli mamy ref z peronów — matchuj
                        // Jeśli stacja ma perony ale bez ref — zapisz z hasPlatform=false (do ręcznej edycji)
                        bool hasPlatform = hasRefData && platformRefs.Contains(trackRef);
                        tracks.Add(new TrackEntryJson
                        {
                            trackRef = trackRef,
                            hasPlatform = hasPlatform
                        });
                    }
                }

                // Sortuj po numerze toru
                tracks.Sort((a, b) =>
                {
                    int.TryParse(a.trackRef, out int na);
                    int.TryParse(b.trackRef, out int nb);
                    if (na != 0 && nb != 0) return na.CompareTo(nb);
                    return string.Compare(a.trackRef, b.trackRef, StringComparison.Ordinal);
                });

                // Pomijaj stacje bez torów I bez peronów (dostaną fallback syntetyczny)
                if (tracks.Count == 0 && !hasPlatforms) continue;

                root.stations.Add(new StationEntry
                {
                    name = station.name,
                    tracks = tracks
                });
            }

            // Sortuj stacje alfabetycznie
            root.stations.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            // Zapisz
            string dir = Path.GetDirectoryName(FilePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string json = JsonUtility.ToJson(root, true);
            File.WriteAllText(FilePath, json);
            float tTotal = Time.realtimeSinceStartup - t0;
            Log.Info($"[StationTrackData] Generated {root.stations.Count} stations → {FilePath} "
                     + $"(preCompute={tPreComp:F2}s, total={tTotal:F2}s)");
        }

        // --- JSON serialization classes ---

        [Serializable]
        class StationTrackRoot
        {
            public List<StationEntry> stations;
        }

        [Serializable]
        class StationEntry
        {
            public string name;
            public List<TrackEntryJson> tracks;
        }

        [Serializable]
        class TrackEntryJson
        {
            public string trackRef;
            public bool hasPlatform;
        }
    }
}
