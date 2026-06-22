using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// Oblicza wynik komfortu pasażerskiego (0-100) na podstawie listy udogodnień.
    /// Wagi:
    /// - Klimatyzacja: 40 pkt (najważniejsze)
    /// - System informacji pasażerskiej: 30 pkt
    /// - Gniazdka 230V: 20 pkt
    /// - Wi-Fi: 10 pkt
    /// Suma maksymalna: 100 pkt.
    /// </summary>
    public static class ComfortCalculator
    {
        public const string AirConditioning = "Klimatyzacja";
        public const string PassengerInfo   = "System informacji pasa\u017cerskiej";
        public const string PowerSockets    = "Gniazdka 230V";
        public const string WiFi            = "Wi-Fi";

        public static int CalculateScore(List<string> features)
        {
            if (features == null || features.Count == 0) return 0;

            int score = 0;
            foreach (var f in features)
            {
                score += f switch
                {
                    AirConditioning => 40,
                    PassengerInfo   => 30,
                    PowerSockets    => 20,
                    WiFi            => 10,
                    _               => 0
                };
            }
            return score > 100 ? 100 : score;
        }

        /// <summary>Tekstowa ocena na podstawie scoru (0-100).</summary>
        public static string ScoreLabel(int score)
        {
            if (score >= 90) return "Luksusowy";
            if (score >= 70) return "Wysoki";
            if (score >= 50) return "\u015aredni";
            if (score >= 30) return "Podstawowy";
            if (score > 0)   return "Niski";
            return "Brak udogodnie\u0144";
        }
    }
}
