using System.Collections.Generic;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Wykrywa aglomeracje miejskie heurystyką: miasto jest "aglomeracyjne" jeśli ma
    /// co najmniej N stacji kolejowych, których nazwa zawiera nazwę miasta.
    ///
    /// Przykład: "Warszawa Centralna", "Warszawa Wschodnia", "Warszawa Zachodnia" →
    /// 3 stacje zawierają "Warszawa" → Warszawa jest aglomeracją.
    ///
    /// Próg z TimetableTuningConstants.AgglomerationMinStationCount (domyślnie 2).
    /// Wynik to HashSet<string> nazw miast aglomeracyjnych — używane przez CategoryClassifier
    /// do wyboru kategorii RA (Regionalny Aglomeracyjny).
    /// </summary>
    public static class AgglomerationDetector
    {
        /// <summary>
        /// Buduje set aglomeracyjnych miast na podstawie listy stacji i listy miast (Places).
        /// Sprawdza tylko cities i towns (villages pomijane — zwykle mają 1 małą stację).
        /// </summary>
        public static HashSet<string> DetectAgglomerations(
            List<RailwayStation> stations,
            List<CityPlace> places,
            int minStationCount = -1)
        {
            if (minStationCount < 0)
                minStationCount = TimetableTuningConstants.AgglomerationMinStationCount;

            var result = new HashSet<string>();
            if (stations == null || places == null) return result;

            foreach (var place in places)
            {
                if (!place.IsMajor) continue;  // tylko city/town
                if (string.IsNullOrEmpty(place.name)) continue;

                int count = CountStationsContainingName(stations, place.name);
                if (count >= minStationCount)
                    result.Add(place.name);
            }

            Log.Info($"[AgglomerationDetector] Detected {result.Count} agglomerations "
                     + $"(threshold: {minStationCount} stations)");
            return result;
        }

        /// <summary>
        /// Zlicza stacje których nazwa zawiera nazwę miasta (case-insensitive).
        /// Używa Contains, nie StartsWith — stacja "Warszawa Centralna" i "Warszawa Wsch." obie pasują.
        /// </summary>
        private static int CountStationsContainingName(List<RailwayStation> stations, string cityName)
        {
            string lowered = cityName.ToLowerInvariant();
            int count = 0;
            foreach (var s in stations)
            {
                if (string.IsNullOrEmpty(s.name)) continue;
                if (s.name.ToLowerInvariant().Contains(lowered))
                    count++;
            }
            return count;
        }
    }
}
