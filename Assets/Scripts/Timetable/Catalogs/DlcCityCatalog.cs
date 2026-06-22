namespace RailwayManager.Timetable
{
    /// <summary>
    /// Hand-picked lista miast zagranicznych planowanych jako DLC (M-PL-5 DLC locks).
    /// Każde miasto ma aliasy OSM (lokalne + łacinka) — ważne dla miast w cyrylicy
    /// gdzie default OSM "name" tag jest w lokalnym alfabecie (Mińsk = "Мінск").
    ///
    /// Dodawanie nowych miast: dopisz w <see cref="Entries"/> z aliasami OSM.
    /// Regionów dodawaj TYLKO gdy planujemy DLC dla tego kraju.
    /// </summary>
    public static class DlcCityCatalog
    {
        public enum Region
        {
            Unknown = 0,   // fallback — miasto nie-w-katalogu
            Germany,
            Czechia,
            Slovakia,
            Lithuania,
            Belarus,
            Ukraine,
            Russia         // enklawa kaliningradzka
        }

        public struct Entry
        {
            public string DisplayName;  // polski display name ("Drezno")
            public string[] OsmNames;   // aliasy z OSM ("Dresden" + ewentualnie wariant)
            public Region Region;
        }

        public static readonly Entry[] Entries = new[]
        {
            // Niemcy (zachodnia granica)
            Make(Region.Germany,  "Berlin",              "Berlin"),
            Make(Region.Germany,  "Drezno",              "Dresden"),
            Make(Region.Germany,  "Frankfurt n. Odrą",   "Frankfurt (Oder)", "Frankfurt an der Oder"),
            Make(Region.Germany,  "Görlitz",             "Görlitz", "Gorlitz"),

            // Czechy (południowo-zachodnia granica)
            Make(Region.Czechia,  "Praga",               "Praha", "Prague"),
            Make(Region.Czechia,  "Ostrawa",             "Ostrava"),
            Make(Region.Czechia,  "Hradec Králové",      "Hradec Králové", "Hradec Kralove"),
            Make(Region.Czechia,  "Brno",                "Brno"),

            // Słowacja (południowa granica)
            Make(Region.Slovakia, "Bratysława",          "Bratislava"),
            Make(Region.Slovakia, "Żylina",              "Žilina", "Zilina"),
            Make(Region.Slovakia, "Koszyce",             "Košice", "Kosice"),

            // Litwa (północno-wschodnia granica)
            Make(Region.Lithuania, "Kowno",              "Kaunas"),
            Make(Region.Lithuania, "Wilno",              "Vilnius"),

            // Białoruś (wschodnia granica — OSM nazwy w cyrylicy)
            Make(Region.Belarus,  "Mińsk",               "Minsk", "Мінск", "Минск"),
            Make(Region.Belarus,  "Grodno",              "Hrodna", "Гродна", "Гродно", "Grodno"),
            Make(Region.Belarus,  "Brześć",              "Brest", "Брэст", "Брест"),

            // Ukraina (południowo-wschodnia granica)
            Make(Region.Ukraine,  "Lwów",                "Lviv", "Львів", "Львов"),
            Make(Region.Ukraine,  "Łuck",                "Lutsk", "Луцьк", "Луцк"),

            // Obwód kaliningradzki (enklawa)
            Make(Region.Russia,   "Kaliningrad",         "Kaliningrad", "Калининград"),
        };

        private static Entry Make(Region region, string displayName, params string[] osmNames)
            => new Entry { Region = region, DisplayName = displayName, OsmNames = osmNames };

        /// <summary>Match po OSM name tag. Case-sensitive (OSM names są konsystentne).</summary>
        public static bool TryFindByOsmName(string osmName, out Entry entry)
        {
            if (string.IsNullOrEmpty(osmName)) { entry = default; return false; }
            foreach (var e in Entries)
                foreach (var n in e.OsmNames)
                    if (n == osmName) { entry = e; return true; }
            entry = default;
            return false;
        }

        /// <summary>Polski display name regionu DLC dla tooltipów / UI.</summary>
        public static string GetRegionDisplayName(Region r) => r switch
        {
            Region.Germany   => "Niemcy",
            Region.Czechia   => "Czechy",
            Region.Slovakia  => "Słowacja",
            Region.Lithuania => "Litwa",
            Region.Belarus   => "Białoruś",
            Region.Ukraine   => "Ukraina",
            Region.Russia    => "Obwód kaliningradzki",
            _                => "Obce państwo"
        };
    }
}
