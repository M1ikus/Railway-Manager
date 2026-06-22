using System.Collections.Generic;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// M-TimetableUX F1.1: walidacja StopType per location × hasPlatform.
    ///
    /// Reguły z `memory/timetable_ux_milestone_design.md` (Defaults Table):
    /// <list type="bullet">
    /// <item>Major + ≥1 hasPlatform=true → PH/PT/ZD/Transit (wszystkie)</item>
    /// <item>Major + brak peronu → PT/ZD/Transit (no PH — pasażerowie nie wsiądą)</item>
    /// <item>Halt + ≥1 hasPlatform=true → PH/ZD/Transit (no PT — halt brak rozjazdów)</item>
    /// <item>Halt + brak peronu → Transit only (lewotorowe ops na szlaku OK)</item>
    /// </list>
    ///
    /// Decomposition do orthogonalnych reguł:
    /// <list type="bullet">
    /// <item><see cref="StopType.Transit"/> zawsze allowed (przelot bez stopu).</item>
    /// <item><see cref="StopType.PT"/> wymaga isMajorStation (halt brak rozjazdów do regulacji ruchu).</item>
    /// <item><see cref="StopType.PH"/> wymaga hasPlatform (pasażerowie wsiadają/wysiadają).</item>
    /// <item><see cref="StopType.ZD"/> wymaga hasPlatform (drużyna wsiada/wysiada).</item>
    /// </list>
    ///
    /// Synthetic platform fallback wywalony w F1.0.5 — hasPlatform pochodzi z OSM
    /// `railway=platform` + JSON override `Assets/StreamingAssets/TimetableData/station_tracks.json`.
    /// </summary>
    public static class StopTypeValidator
    {
        /// <summary>
        /// Zwraca listę dopuszczalnych typów dla danej lokalizacji × dostępności peronu.
        /// Używaj w UI dropdown filtrach (F1.4) i save validation.
        /// </summary>
        public static IReadOnlyList<StopType> GetAllowedTypes(bool isMajorStation, bool hasPlatform)
        {
            // Major + peron: wszystkie 4
            if (isMajorStation && hasPlatform)
                return new[] { StopType.PH, StopType.PT, StopType.ZD, StopType.Transit };

            // Major bez peronu: brak PH (pasażerowie nie wsiądą), brak ZD (drużyna wymaga peronu)
            if (isMajorStation)
                return new[] { StopType.PT, StopType.Transit };

            // Halt + peron: PH/ZD/Transit (no PT — halt brak rozjazdów)
            if (hasPlatform)
                return new[] { StopType.PH, StopType.ZD, StopType.Transit };

            // Halt bez peronu: tylko Transit (track w grafie wystarcza dla lewotorowych ops)
            return new[] { StopType.Transit };
        }

        /// <summary>
        /// Czy dany typ jest dozwolony na konkretnej lokalizacji.
        /// Używaj w setter validation, save validation, runtime guards.
        /// </summary>
        public static bool IsAllowed(StopType type, bool isMajorStation, bool hasPlatform)
        {
            return type switch
            {
                StopType.Transit => true,
                StopType.PT => isMajorStation,        // halt nie wspiera PT
                StopType.PH => hasPlatform,            // PH wymaga peronu
                StopType.ZD => hasPlatform,            // ZD wymaga peronu
                _ => false
            };
        }

        /// <summary>
        /// Polski human-readable opis StopType — dla UI dropdown labels + tooltip'ów.
        /// </summary>
        public static string DisplayName(StopType type) => type switch
        {
            StopType.PH => "Postój handlowy (PH)",
            StopType.PT => "Postój techniczny (PT)",
            StopType.ZD => "Zmiana drużyny (ZD)",
            StopType.Transit => "Przelot",
            _ => type.ToString()
        };

        /// <summary>
        /// Krótki kod (2 znaki) — dla compact UI badges.
        /// </summary>
        public static string ShortCode(StopType type) => type switch
        {
            StopType.PH => "PH",
            StopType.PT => "PT",
            StopType.ZD => "ZD",
            StopType.Transit => "→",
            _ => "?"
        };
    }
}
