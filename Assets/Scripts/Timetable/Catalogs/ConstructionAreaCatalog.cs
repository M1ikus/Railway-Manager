using System.Collections.Generic;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Obszary konstrukcyjne numerów pociągów wg pierwszej cyfry numeru.
    /// Walidacja: pierwsza cyfra wpisanego numeru musi odpowiadać obszarowi, w którym
    /// rozkład ZACZYNA bieg (nie kończy). Mapowanie województwo → obszar konstrukcyjny.
    /// </summary>
    public static class ConstructionAreaCatalog
    {
        public readonly struct Area
        {
            public readonly int firstDigit;           // 1..9
            public readonly string shortName;         // np. "Centralny"
            public readonly string longName;          // np. "Obszar centralny (Warszawa, Mazowsze)"
            public readonly string[] voivodeships;    // nazwy województw w tym obszarze

            public Area(int d, string shortN, string longN, string[] voi)
            {
                firstDigit = d;
                shortName = shortN;
                longName = longN;
                voivodeships = voi;
            }
        }

        // Mapowanie województw → obszar. Nazwy w mianowniku, pisane jak w OSM (lowercase, polskie znaki).
        private static readonly Area[] _areas =
        {
            new(1, "Centralny",    "Obszar 1 — Centralny (Warszawa, Mazowsze, Łódzkie)",
                new[] { "mazowieckie", "łódzkie" }),

            new(2, "Lubelski",     "Obszar 2 — Lubelski",
                new[] { "lubelskie", "podlaskie" }),

            new(3, "Południowy",   "Obszar 3 — Południowy (Kraków, Małopolska, Podkarpacie, Świętokrzyskie)",
                new[] { "małopolskie", "podkarpackie", "świętokrzyskie" }),

            new(4, "Katowicki",    "Obszar 4 — Katowicki (Śląsk, GOP)",
                new[] { "śląskie", "opolskie" }),

            new(5, "Północny",     "Obszar 5 — Północny (Trójmiasto, Pomorze, Kujawy, Warmia-Mazury)",
                new[] { "pomorskie", "kujawsko-pomorskie", "warmińsko-mazurskie" }),

            new(6, "Dolnośląski",  "Obszar 6 — Dolnośląski (Wrocław)",
                new[] { "dolnośląskie" }),

            new(7, "Zachodni",     "Obszar 7 — Zachodni (Poznań, Wielkopolska, Lubuskie)",
                new[] { "wielkopolskie", "lubuskie" }),

            new(8, "Szczeciński",  "Obszar 8 — Szczeciński (Pomorze Zachodnie)",
                new[] { "zachodniopomorskie" }),

            new(9, "Rezerwa",      "Obszar 9 — Rezerwa konstrukcyjna",
                new string[0])
        };

        private static readonly Dictionary<string, int> _voivodeshipToDigit = BuildReverseMap();

        private static Dictionary<string, int> BuildReverseMap()
        {
            var d = new Dictionary<string, int>(32);
            foreach (var a in _areas)
                foreach (var voi in a.voivodeships)
                    d[voi.ToLowerInvariant()] = a.firstDigit;
            return d;
        }

        /// <summary>Zwraca obszar konstrukcyjny dla podanej pierwszej cyfry (1..9). -1 jeśli nieprawidłowa.</summary>
        public static Area? GetByDigit(int digit)
        {
            if (digit < 1 || digit > 9) return null;
            return _areas[digit - 1];
        }

        /// <summary>Mapuje nazwę województwa (z OSM admin_level=4) na obszar konstrukcyjny 1..9.</summary>
        public static int GetDigitForVoivodeship(string voivodeshipName)
        {
            if (string.IsNullOrEmpty(voivodeshipName)) return 9;
            return _voivodeshipToDigit.TryGetValue(voivodeshipName.ToLowerInvariant(), out var d) ? d : 9;
        }

        public static IReadOnlyList<Area> AllAreas => _areas;
    }
}
