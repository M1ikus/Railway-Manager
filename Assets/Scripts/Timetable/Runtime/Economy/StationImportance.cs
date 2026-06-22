using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M6-1: Oblicza factor "ważności" stacji na podstawie OSM metadata.
    /// Używane przez <see cref="OriginDestinationMatrix"/> jako wejście do gravity
    /// modelu popytu.
    ///
    /// Kalkulacja jednorazowa przy starcie gry (po załadowaniu RailwayStation +
    /// CityPlace + StationPlatform + PathfindingGraph). Wyniki cached w słowniku.
    ///
    /// Typowe wartości:
    /// - Warszawa Centralna: ~8-10
    /// - Olsztyn Główny: ~4-6
    /// - Halt przy mieście: ~1.5
    /// - Halt w szczerym polu: ~1.0
    /// </summary>
    public static class StationImportance
    {
        // Wagi (do kalibracji w M6.5 Rebalance)
        const float BaseValue = 1.0f;
        const float MajorStationBonus = 2.0f;
        const float PlatformWeight = 0.3f;
        const float JunctionBonus = 1.0f;              // ≥3 linie wychodzące
        const float CityPopulationLogWeight = 0.5f;
        const float NamedStationBonus = 2.0f;          // "Główna"/"Centralna"/"Główny"

        const float CitySearchRadiusM = 3000f;         // szukaj najbliższego miasta w 3km
        const int JunctionMinEdges = 3;

        /// <summary>
        /// Oblicza factor dla pojedynczej stacji.
        /// </summary>
        public static float Calculate(
            RailwayStation station,
            IReadOnlyList<CityPlace> places,
            IReadOnlyList<StationPlatform> platforms,
            PathfindingGraph graph)
        {
            float value = BaseValue;

            if (station.isMajorStation)
                value += MajorStationBonus;

            // Platformy
            int platformCount = CountPlatformsForStation(station, platforms);
            value += platformCount * PlatformWeight;

            // Węzeł kolejowy — liczba wychodzących krawędzi z nodu stacji
            if (station.pathNodeId >= 0 && graph != null
                && station.pathNodeId < graph.Nodes.Count)
            {
                var node = graph.GetNode(station.pathNodeId);
                if (node.edgeIds != null && node.edgeIds.Count >= JunctionMinEdges)
                    value += JunctionBonus;
            }

            // Populacja najbliższego miasta
            var nearestCity = FindNearestCity(station.position, places, CitySearchRadiusM);
            if (nearestCity != null && nearestCity.population > 0)
            {
                // log10(pop) — 10000 mieszk = +0.5 × 4 = 2, 1mln = +0.5 × 6 = 3
                value += Mathf.Log10(Mathf.Max(nearestCity.population, 1)) * CityPopulationLogWeight;
            }

            // Bonus za nazwę (stacje węzłowe Polski)
            if (HasMajorNameKeyword(station.name))
                value += NamedStationBonus;

            return value;
        }

        /// <summary>
        /// Oblicza factory dla wszystkich stacji naraz — zwraca słownik stationId → importance.
        /// </summary>
        public static Dictionary<int, float> CalculateAll(
            IReadOnlyList<RailwayStation> stations,
            IReadOnlyList<CityPlace> places,
            IReadOnlyList<StationPlatform> platforms,
            PathfindingGraph graph)
        {
            var result = new Dictionary<int, float>();
            if (stations == null) return result;

            foreach (var s in stations)
                result[s.stationId] = Calculate(s, places, platforms, graph);

            return result;
        }

        // ── Helpers ──────────────────────────────────────────────────

        static int CountPlatformsForStation(RailwayStation s, IReadOnlyList<StationPlatform> platforms)
        {
            if (platforms == null || s.pathNodeId < 0) return 0;
            int count = 0;
            foreach (var p in platforms)
                if (p.stationNodeId == s.pathNodeId) count++;
            return count;
        }

        static CityPlace FindNearestCity(Vector2 pos, IReadOnlyList<CityPlace> places, float maxDistM)
        {
            if (places == null) return null;
            CityPlace best = null;
            float bestDist = maxDistM;
            foreach (var p in places)
            {
                if (p.population <= 0) continue; // ignoruj miejsca bez populacji w OSM
                float d = Vector2.Distance(p.position, pos);
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        static readonly string[] MajorKeywords = { "Główna", "Centralna", "Główny", "Centralny" };

        static bool HasMajorNameKeyword(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var kw in MajorKeywords)
                if (name.Contains(kw)) return true;
            return false;
        }
    }
}
