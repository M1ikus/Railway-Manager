using System.Collections.Generic;
using UnityEngine;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-5: Wylicza liczbę miejsc i klasę komfortu z miksu stref siedzeń wzdłuż pudła.
    /// Heurystyka: każda <see cref="SeatZoneType"/> ma swoją gęstość miejsc (m² per pasażer)
    /// i punkty komfortu — końcowe wartości średnie ważone długością strefy.
    /// </summary>
    public static class InteriorMixCalculator
    {
        // Gęstość miejsc per typ strefy (m² powierzchni podłogi per pasażer).
        // Wyższe = mniej miejsc per metr (lepszy komfort lub special-purpose).
        // 0 = brak miejsc (bagażowy, rowerowy partly, gastronomiczny — opcjonalnie odrębne miejsca).
        private static readonly Dictionary<SeatZoneType, float> M2PerSeat = new()
        {
            { SeatZoneType.SecondClassOpen,         3.3f }, // 2x2 + corridor
            { SeatZoneType.SecondClassCompartment,  2.8f }, // 6-os przedział, gęściej
            { SeatZoneType.FirstClassOpen,          5.0f }, // 2x1 + szerszy corridor
            { SeatZoneType.FirstClassCompartment,   4.5f }, // 4-os przedział, więcej miejsca
            { SeatZoneType.Sleeping,                6.0f }, // łóżka 6-os
            { SeatZoneType.Reclining,               4.0f }, // fotele rozkładane
            { SeatZoneType.Family,                  3.5f },
            { SeatZoneType.WheelchairAccessible,    8.0f }, // duża przestrzeń + rampa
            { SeatZoneType.ManagerCompartment,      6.0f }, // 4-os luksusowy
            { SeatZoneType.Bicycle,                 0f },   // 0 miejsc siedzących (stojaki)
            { SeatZoneType.SmallCatering,           8.0f }, // bar z paroma stołkami
            { SeatZoneType.LargeCatering,           4.0f }  // restauracyjny (stoliki + krzesła)
        };

        // Punkty komfortu per typ strefy (1-5). Średnia ważona długością → comfort class.
        private static readonly Dictionary<SeatZoneType, float> ComfortPoints = new()
        {
            { SeatZoneType.SecondClassOpen,         2.5f },
            { SeatZoneType.SecondClassCompartment,  3.0f },
            { SeatZoneType.FirstClassOpen,          4.0f },
            { SeatZoneType.FirstClassCompartment,   4.5f },
            { SeatZoneType.Sleeping,                5.0f },
            { SeatZoneType.Reclining,               4.0f },
            { SeatZoneType.Family,                  3.5f },
            { SeatZoneType.WheelchairAccessible,    3.0f },
            { SeatZoneType.ManagerCompartment,      5.0f },
            { SeatZoneType.Bicycle,                 1.5f },
            { SeatZoneType.SmallCatering,           3.5f },
            { SeatZoneType.LargeCatering,           4.5f }
        };

        // Bonusy komfortu per feature wyposażenia (cumulative).
        private static readonly Dictionary<string, float> ComfortBonus = new()
        {
            { "Klimatyzacja", 0.3f },
            { "Wi-Fi",        0.2f },
            { "Gniazdka 230V", 0.2f },
            { "USB",          0.1f },
            { "Toalety",      0.1f },
            { "Info pasażerskie", 0.1f },
            { "Monitoring CCTV", 0.05f },
            { "Bar/restauracja", 0.3f },
            { "Strefa ciszy", 0.2f },
            { "Strefa dziecka", 0.15f },
            { "Przewijak", 0.05f }
        };

        /// <summary>
        /// Wylicza całkowitą liczbę miejsc dla wagonu/członu o długości <paramref name="lengthM"/>
        /// z miksu stref. Zakłada szerokość 2.85m (UIC standard) — powierzchnia = length × 2.85.
        /// </summary>
        public static int CalculateSeats(List<SeatZoneSlot> mix, float lengthM)
        {
            if (mix == null || mix.Count == 0 || lengthM <= 0) return 0;

            const float WAGON_WIDTH_M = 2.85f;
            int total = 0;
            foreach (var slot in mix)
            {
                float zoneLengthM = lengthM * (slot.endPercent - slot.startPercent) / 100f;
                if (zoneLengthM <= 0) continue;
                float zoneAreaM2 = zoneLengthM * WAGON_WIDTH_M;
                if (M2PerSeat.TryGetValue(slot.type, out var m2) && m2 > 0)
                    total += Mathf.RoundToInt(zoneAreaM2 / m2);
            }
            return total;
        }

        /// <summary>
        /// Wylicza klasę komfortu (1-5) jako średnią ważoną długością z miksu stref + bonus z features.
        /// </summary>
        public static int CalculateComfortClass(List<SeatZoneSlot> mix, List<string> comfortFeatures)
        {
            if (mix == null || mix.Count == 0) return 3; // default

            float weightedSum = 0f;
            float totalLength = 0f;
            foreach (var slot in mix)
            {
                float len = slot.endPercent - slot.startPercent;
                if (len <= 0) continue;
                float pts = ComfortPoints.TryGetValue(slot.type, out var p) ? p : 2.5f;
                weightedSum += pts * len;
                totalLength += len;
            }

            float avgPts = totalLength > 0 ? weightedSum / totalLength : 2.5f;

            if (comfortFeatures != null)
            {
                foreach (var f in comfortFeatures)
                    if (ComfortBonus.TryGetValue(f, out var bonus)) avgPts += bonus;
            }

            return Mathf.Clamp(Mathf.RoundToInt(avgPts), 1, 5);
        }

        /// <summary>Walidacja miksu: suma % == 100 (z tolerancją 0.01), kolejność rosnąca, brak overlap'ów.</summary>
        public static bool IsValid(List<SeatZoneSlot> mix, out string error)
        {
            error = "";
            if (mix == null || mix.Count == 0) { error = "Pusty miks"; return false; }

            float lastEnd = 0f;
            for (int i = 0; i < mix.Count; i++)
            {
                var s = mix[i];
                if (s.startPercent < lastEnd - 0.01f)
                {
                    error = $"Strefa {i + 1}: nakłada się z poprzednią";
                    return false;
                }
                if (s.endPercent <= s.startPercent)
                {
                    error = $"Strefa {i + 1}: end ({s.endPercent}%) <= start ({s.startPercent}%)";
                    return false;
                }
                lastEnd = s.endPercent;
            }

            if (Mathf.Abs(lastEnd - 100f) > 0.01f)
            {
                error = $"Suma stref: {lastEnd:0.0}% (powinno 100%)";
                return false;
            }

            return true;
        }

        /// <summary>Recompute startPercent + endPercent z lengthPercent listy. Zachowuje kolejność.</summary>
        public static void Normalize(List<SeatZoneSlot> mix)
        {
            if (mix == null || mix.Count == 0) return;
            float cursor = 0f;
            foreach (var s in mix)
            {
                float len = s.endPercent - s.startPercent;
                if (len < 0.1f) len = 0.1f;
                s.startPercent = cursor;
                s.endPercent = cursor + len;
                cursor = s.endPercent;
            }
        }
    }
}
