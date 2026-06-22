using System.Collections.Generic;
using MapSystem;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Wylicza Vmax dla RailwaySegment z OSM metadata (maxspeed) lub fallback z LineUsageSpeedCatalog.
    /// Stateless. Do użytku przy pathfindingu i kalkulacji czasów jazdy.
    /// </summary>
    public static class SegmentSpeedResolver
    {
        /// <summary>
        /// Zwraca Vmax dla segmentu w km/h. Kolejność: maxspeed z OSM → fallback z usage/service → Unknown (80).
        /// </summary>
        public static int GetMaxSpeedKmh(RailwaySegment segment)
        {
            if (segment?.Metadata == null) return LineUsageSpeedCatalog.Unknown;
            return GetMaxSpeedKmh(segment.Metadata);
        }

        /// <summary>
        /// Wariant przyjmujący metadata bezpośrednio — przydatne przy batch processingu.
        /// </summary>
        public static int GetMaxSpeedKmh(Dictionary<string, string> metadata)
        {
            if (metadata == null) return LineUsageSpeedCatalog.Unknown;

            // 1. Tag maxspeed — najbardziej precyzyjne źródło
            if (metadata.TryGetValue("maxspeed", out var rawMaxSpeed))
            {
                int parsed = LineUsageSpeedCatalog.ParseMaxSpeed(rawMaxSpeed);
                if (parsed > 0) return parsed;
            }

            // 2. Fallback z usage/service
            metadata.TryGetValue("usage", out var usage);
            metadata.TryGetValue("service", out var service);
            return LineUsageSpeedCatalog.GetFallbackSpeed(usage, service);
        }

        /// <summary>
        /// Czy segment jest zelektryfikowany (tag electrified = contact_line / yes / rail).
        /// </summary>
        public static bool IsElectrified(RailwaySegment segment)
        {
            if (segment?.Metadata == null) return false;
            if (!segment.Metadata.TryGetValue("electrified", out var value)) return false;
            if (string.IsNullOrEmpty(value)) return false;

            var v = value.ToLowerInvariant();
            return v == "yes" || v == "contact_line" || v == "rail" || v == "3rd_rail";
        }

        /// <summary>
        /// Napięcie sieci trakcyjnej w woltach (z tagu voltage). 0 jeśli brak lub nie-elektryczna.
        /// </summary>
        public static int GetVoltage(RailwaySegment segment)
        {
            if (segment?.Metadata == null) return 0;
            if (!segment.Metadata.TryGetValue("voltage", out var value)) return 0;
            if (int.TryParse(value, out var v)) return v;
            return 0;
        }
    }
}
