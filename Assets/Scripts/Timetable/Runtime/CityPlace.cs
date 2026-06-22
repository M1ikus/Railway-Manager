using UnityEngine;

namespace RailwayManager.Timetable
{
    /// <summary>Typ miejscowości z tagu place=* w OSM.</summary>
    public enum PlaceType
    {
        City,       // place=city  — duże miasta (>100k mieszkańców typowo)
        Town,       // place=town  — średnie miasta
        Village     // place=village — wsie
    }

    /// <summary>
    /// Miasto/miejscowość/wieś z OSM — warstwa Places w .bin.
    /// Używane przez AgglomerationDetector i do lookup "w jakim mieście jest stacja X".
    /// </summary>
    public class CityPlace
    {
        public string name;
        public Vector2 position;
        public PlaceType type;
        public int population;      // 0 jeśli brak tagu population

        public string voivodeship;  // wypełniany przez VoivodeshipResolver po załadowaniu

        /// <summary>
        /// M-DLC foundation 2026-05-04: ISO 3166-1 alpha-2 kod kraju ("PL", "DE", "CZ"...).
        /// Wypełniany w PlaceStreamProcessor.Finalize przez VoivodeshipResolver.GetCountryCode.
        /// Patrz: docs/design/dlc-multi-country.md
        /// </summary>
        public string countryCode;

        /// <summary>Jest-ali miasto "dużym" — city lub town.</summary>
        public bool IsMajor => type == PlaceType.City || type == PlaceType.Town;
    }
}
