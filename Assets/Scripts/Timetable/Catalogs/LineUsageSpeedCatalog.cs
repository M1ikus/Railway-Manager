namespace RailwayManager.Timetable
{
    /// <summary>
    /// Fallback Vmax dla linii kolejowej gdy tag maxspeed jest nieobecny lub nieprawidłowy.
    /// Decyzja na podstawie OSM tagów usage/service (ekstraktowane przez formap do Metadata).
    /// Wartości można kalibrować, są świadomie konserwatywne dla braku danych.
    /// </summary>
    public static class LineUsageSpeedCatalog
    {
        // ── Stałe wartości domyślne (km/h) ──
        public const int Main       = 140;  // usage=main — magistralne (E20, E30, E65)
        public const int Branch     = 120;  // usage=branch — pierwszorzędne
        public const int Secondary  = 100;  // fallback dla main lines bez usage
        public const int Industrial = 60;   // usage=industrial, tourism, military
        public const int Siding     = 40;   // service=siding — boczne
        public const int Yard       = 30;   // service=yard — stacyjne manewrowe
        public const int Spur       = 40;   // service=spur — odgałęzienia
        public const int Crossover  = 40;   // service=crossover — rozjazdy łącznikowe
        public const int Unknown    = 80;   // ostateczny fallback gdy nic nie pasuje

        /// <summary>
        /// Zwraca Vmax na podstawie tagów OSM z MeshGeometry.Metadata warstwy Railways.
        /// Oba tagi mogą być null (nie występować).
        /// </summary>
        public static int GetFallbackSpeed(string usageTag, string serviceTag)
        {
            // service ma pierwszeństwo nad usage — "service=siding" oznacza tor boczny nawet
            // na linii oznaczonej jako main
            if (!string.IsNullOrEmpty(serviceTag))
            {
                switch (serviceTag.ToLowerInvariant())
                {
                    case "siding":    return Siding;
                    case "yard":      return Yard;
                    case "spur":      return Spur;
                    case "crossover": return Crossover;
                }
            }

            if (!string.IsNullOrEmpty(usageTag))
            {
                switch (usageTag.ToLowerInvariant())
                {
                    case "main":        return Main;
                    case "branch":      return Branch;
                    case "industrial":
                    case "tourism":
                    case "military":    return Industrial;
                }
            }

            return Unknown;
        }

        /// <summary>
        /// Parsuje wartość tagu maxspeed z OSM. Obsługuje różne formaty:
        /// "120", "120 km/h", "120;130", "none", "PL:rural", itp.
        /// Zwraca 0 gdy nie udało się sparsować — wtedy wywołujący powinien użyć fallbacka.
        /// </summary>
        public static int ParseMaxSpeed(string rawMaxSpeed)
        {
            if (string.IsNullOrWhiteSpace(rawMaxSpeed)) return 0;

            var s = rawMaxSpeed.Trim().ToLowerInvariant();

            // Specjalne wartości
            if (s == "none" || s == "signals" || s == "variable") return 0;

            // Średnik = wiele prędkości (np. kierunki) — bierzemy pierwszą
            int semi = s.IndexOf(';');
            if (semi > 0) s = s.Substring(0, semi);

            // Usuń "km/h" i spacje
            s = s.Replace("km/h", "").Replace("kmh", "").Trim();

            // Obsługa "PL:rural", "PL:urban" itp. — zostawiamy 0 (fallback z usage)
            if (s.Contains(":")) return 0;

            if (int.TryParse(s, out var value) && value > 0 && value <= 350)
                return value;

            return 0;
        }
    }
}
