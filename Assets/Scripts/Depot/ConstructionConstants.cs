namespace DepotSystem
{
    /// <summary>
    /// M6.5-5: Centralne stałe kosztów budowy infrastruktury — tory, sieć trakcyjna,
    /// rozjazdy, hale, ziemia, nawierzchnie. Real-world Polska 2024-2025.
    ///
    /// Source: <c>docs/design/m6-5-economy-research.md</c> sekcja 3 (Koszty budowy).
    /// Wszystkie kwoty w groszach (1 zł = 100 gr) — konwencja projektu.
    ///
    /// 🌍 ARCHITEKTURA PER-KRAJ (DLC ready, post-EA):
    ///   - Stale tu = PL (kontrakty PKP PLK, Sekocenbud, ceny działek 2024-25).
    ///   - W EA wszystkie depoty są PL.
    ///   - W DLC (DE/CZ/SK): per-kraj stale (DE drożej ~+30%, CZ taniej ~-20%) +
    ///     resolver wg kraju depotu. Implementacja post-EA.
    ///
    /// Inflacja sektorowa 2020-2024: +50-100%. Skaluj historyczne kontrakty ×1.5-2.
    ///
    /// Caveat: niektóre wartości są środkiem szerokich widełek (modernizacja vs nowa
    /// linia, peryferia vs aglomeracja). Per-konkretny scenariusz kontekst może
    /// wymagać dodatkowego mnożnika (M-Balance refactor).
    /// </summary>
    public static class ConstructionConstants
    {
        // ═══════════════════════════════════════════════════════════════════
        //  Tory (per km)
        // ═══════════════════════════════════════════════════════════════════
        // Średnia branżowa modernizacja PLK 2023-24: 15-25 mln/km
        // (plan 180 mld zł / 7000 km do 2030 = ~25 mln/km średnio).
        // Sam tor szlak (wymiana, bez SRK/peronów): 5-10 mln/km.

        /// <summary>Tor szlakowy nowy (sam tor, bez sieci/SRK): 8 mln zł/km.</summary>
        public const long TrackNewSzlakPerKmGroszy = 800_000_000L;

        /// <summary>Tor szlakowy modernizacja (sam tor): 7 mln zł/km.</summary>
        public const long TrackModernizacjaSzlakPerKmGroszy = 700_000_000L;

        /// <summary>Tor stacyjny nowy (krótki, ze rozjazdami pakietowo): 12 mln zł/km.</summary>
        public const long TrackNewStacyjnyPerKmGroszy = 1_200_000_000L;

        /// <summary>Modernizacja kompleksowa (tor + SRK + sieć + perony, kontrakt PLK śr.): 25 mln zł/km.</summary>
        public const long TrackModernizacjaKompleksowaPerKmGroszy = 2_500_000_000L;

        /// <summary>Nowa linia w czystym terenie (tor + sieć + SRK, bez obiektów): 40 mln zł/km.</summary>
        public const long TrackNewKompleksowaPerKmGroszy = 4_000_000_000L;

        /// <summary>Tor zajezdniowy (krótki, nieelektryfikowany, parking pojazdów): 5 mln zł/km.</summary>
        public const long TrackZajezdniaPerKmGroszy = 500_000_000L;

        // ═══════════════════════════════════════════════════════════════════
        //  Sieć trakcyjna 3kV DC
        // ═══════════════════════════════════════════════════════════════════
        // Sama sieć (słupy + przewody jezdne + powrót): 0.86-1.5 mln zł/km.
        // Elektryfikacja kompleksowa (sieć + drobne prace): 1-2 mln zł/km.
        // Podstacja trakcyjna: 22-70 mln zł/szt (modernizacja vs nowa).

        /// <summary>Sama sieć trakcyjna 3kV DC (słupy + przewody): 1 mln zł/km.</summary>
        public const long CatenaryPerKmGroszy = 100_000_000L;

        /// <summary>Elektryfikacja kompleksowa (sieć + drobne prace, gdy tor już istnieje): 1.5 mln zł/km.</summary>
        public const long CatenaryElectryfikacjaKompleksowaPerKmGroszy = 150_000_000L;

        /// <summary>Podstacja trakcyjna nowa (110/3kV DC + budynki + drogi): 70 mln zł/szt.</summary>
        public const long PodstacjaTrakcyjnaNowaGroszy = 7_000_000_000L;

        /// <summary>Podstacja trakcyjna modernizacja istniejącej: 22 mln zł/szt (program MUZa II 2024-29).</summary>
        public const long PodstacjaTrakcyjnaModernizacjaGroszy = 2_200_000_000L;

        /// <summary>Typowy zasięg podstacji 3kV DC: 15-20 km dla linii dwutorowej.</summary>
        public const int PodstacjaTrakcyjnaZasiegKm = 18;

        // ═══════════════════════════════════════════════════════════════════
        //  Rozjazdy (per szt., z montażem)
        // ═══════════════════════════════════════════════════════════════════
        // Sam materiał (rynek wtórny S60 R190): 12 600 zł.
        // Z montażem PLK pakiety: 3-4 mln zł (×100 ceny używanego!).
        // Montaż >> materiał — rozjazd to głównie robocizna.

        /// <summary>Rozjazd zwykły R190 1:9 (40 km/h na bok) z montażem: 3.5 mln zł/szt.</summary>
        public const long RozjazdR190Groszy = 350_000_000L;

        /// <summary>Rozjazd R300 1:12 z montażem: 600 tys zł/szt.</summary>
        public const long RozjazdR300Groszy = 60_000_000L;

        /// <summary>Rozjazd R500 1:14 z montażem: 800 tys zł/szt.</summary>
        public const long RozjazdR500Groszy = 80_000_000L;

        /// <summary>Rozjazd R760 1:18 KDP (do 200 km/h): 2 mln zł/szt.</summary>
        public const long RozjazdR760Groszy = 200_000_000L;

        /// <summary>Skrzyżowanie pojedyncze: 500 tys zł/szt.</summary>
        public const long SkrzyzowaniePojedynczeGroszy = 50_000_000L;

        /// <summary>Krzyżownica podwójna (scissors crossover): ~2× cena 2 rozjazdów R190 + bonus za kompleks. ~9 mln zł/szt.</summary>
        public const long KrzyzownicaPodwojnaGroszy = 900_000_000L;

        // ═══════════════════════════════════════════════════════════════════
        //  Budynki w zajezdni (per m²)
        // ═══════════════════════════════════════════════════════════════════
        // P1/P2 = lekka hala z kanałami rewizyjnymi (3-5 tys zł/m²).
        // P3 = + suwnice, dźwigi, tokarka podtorowa (5-8 tys zł/m²).
        // P5 = + suwnice 2× 50t, podnośniki kolumnowe, pełen warsztat (8-15 tys zł/m²).
        // Lokomotywownia W-wa Grochów (P3+myjnia+diagnostyka, ~25k m²): 179 mln zł.

        /// <summary>Hala P1/P2 (przeglądy bieżące, codzienne): 4 tys zł/m².</summary>
        public const long HalaP1P2PerSqMGroszy = 400_000L;

        /// <summary>Hala P3 (średnia naprawa, suwnice + dźwigi): 6.5 tys zł/m².</summary>
        public const long HalaP3PerSqMGroszy = 650_000L;

        /// <summary>Hala P4 (rozszerzona naprawa): 9 tys zł/m².</summary>
        public const long HalaP4PerSqMGroszy = 900_000L;

        /// <summary>Hala P5 (główna naprawa, suwnice 50t, podnośniki, pełen warsztat): 11 tys zł/m².</summary>
        public const long HalaP5PerSqMGroszy = 1_100_000L;

        /// <summary>Hala myjni automatycznej EZT (linia mycia z portalami samojezdnymi + zamknięty obieg wody): 30 mln zł/szt (benchmark PKP IC Wrocław 33M).</summary>
        public const long HalaMyjniaGroszy = 3_000_000_000L;

        /// <summary>Biuro administracyjne klasa B: 5 tys zł/m².</summary>
        public const long BiuroPerSqMGroszy = 500_000L;

        /// <summary>Pomieszczenie socjalne (szatnie/jadalnia/prysznice): 4 tys zł/m².</summary>
        public const long PomieszczenieSocjalnePerSqMGroszy = 400_000L;

        /// <summary>Magazyn części (hala stalowa standard): 2.5 tys zł/m².</summary>
        public const long MagazynCzesciPerSqMGroszy = 250_000L;

        /// <summary>Stacja paliw / punkt tankowania spalinówek (zbiornik 50-100 m³ + dystrybutory + przeciwpożarowe): 7 mln zł/szt.</summary>
        public const long StacjaPaliwGroszy = 700_000_000L;

        /// <summary>Hala stalowa magazynowa (bez instalacji ppoż): 3.2 tys zł/m².</summary>
        public const long HalaStalowaMagazynowaPerSqMGroszy = 320_000L;

        // ═══════════════════════════════════════════════════════════════════
        //  Powierzchnie utwardzone
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Asfalt + podbudowa kompleks (korytowanie + stabilizacja + kruszywo + asfalt): 250 zł/m².</summary>
        public const long AsfaltKompleksPerSqMGroszy = 25_000L;

        /// <summary>Plac manewrowy ciężki (TIR/lokomotywy): 400 zł/m².</summary>
        public const long PlacManewrowyCiezkiPerSqMGroszy = 40_000L;

        /// <summary>Droga wewnętrzna asfaltowa 6m szer (1m liniowy): 2.2 tys zł/m.</summary>
        public const long DrogaWewnetrznaPerMGroszy = 220_000L;

        /// <summary>Parking samochodowy (stanowisko + nawierzchnia + oznakowanie + kanalizacja): 5 tys zł/szt.</summary>
        public const long ParkingStanowiskoGroszy = 500_000L;

        /// <summary>Płot przemysłowy panelowy 2m (siatka + słupki + cokół): 300 zł/m.</summary>
        public const long PlotPrzemyslowyPerMGroszy = 30_000L;

        /// <summary>Płot kolejowy ciężki, antywandal (wyższy + antywspinaczowy): 600 zł/m.</summary>
        public const long PlotKolejowyCiezkiPerMGroszy = 60_000L;

        // ═══════════════════════════════════════════════════════════════════
        //  Ziemia (działki przemysłowe)
        // ═══════════════════════════════════════════════════════════════════
        // Działki z bocznicą kolejową = rynek niszowy. Często grunty poprzemysłowe
        // (cukrownie, huty, papiernie) w peryferiach za 50-200 zł/m².
        // W centrach aglomeracji praktycznie niedostępne dla nowej zajezdni.

        /// <summary>Działka peryferyjna (małe miasto, peryferia średniego): 100 zł/m².</summary>
        public const long ZiemiaPeryferiaPerSqMGroszy = 10_000L;

        /// <summary>Działka średnie miasto (peryferia dużego, np. Płock/Radom): 150 zł/m².</summary>
        public const long ZiemiaSrednieMiastoPerSqMGroszy = 15_000L;

        /// <summary>Działka aglomeracja II strefa (np. Wrocław/Kraków/Poznań peryferia): 300 zł/m².</summary>
        public const long ZiemiaAglomeracjaIIPerSqMGroszy = 30_000L;

        /// <summary>Działka aglomeracja I strefa (Warszawa/Kraków bliżej centrum): 500 zł/m².</summary>
        public const long ZiemiaAglomeracjaIPerSqMGroszy = 50_000L;

        /// <summary>Działka z istniejącą bocznicą peryferia (grunty poprzemysłowe, premium za bocznicę): 100 zł/m².</summary>
        public const long ZiemiaZBocznicaPeryferiaPerSqMGroszy = 10_000L;

        /// <summary>Działka z istniejącą bocznicą aglomeracja: 250 zł/m².</summary>
        public const long ZiemiaZBocznicaAglomeracjaPerSqMGroszy = 25_000L;

        // ═══════════════════════════════════════════════════════════════════
        //  Benchmarks (referencyjne kontrakty real-world)
        // ═══════════════════════════════════════════════════════════════════
        // Dla sanity check przy implementacji M-Balance / kalibracji in-game.

        /// <summary>Lokomotywownia W-wa Grochów (P3 + myjnia + budynek socjalny + diagnostyka, ~25k m²): 179 mln zł (kontrakt PKP IC 2024-27).</summary>
        public const long BenchmarkLokomotywowniaGrochowGroszy = 17_900_000_000L;

        /// <summary>Myjnia EZT PKP IC Wrocław (200m, portale samojezdne, zamknięty obieg wody): 33 mln zł (2023).</summary>
        public const long BenchmarkMyjniaPkpIcWroclawGroszy = 3_300_000_000L;

        /// <summary>Zajezdnia tramwajowa Gdańsk (30 tramwajów + stacja techniczna + myjnia + paliwa): 321 mln zł (kontrakt 2024-30).</summary>
        public const long BenchmarkZajezdniaGdanskGroszy = 32_100_000_000L;

        /// <summary>Plan PKP PLK 2030: 180 mld zł / 7000 km torów = ~25.7 mln zł/km średnio modernizacji kompleksowej.</summary>
        public const long BenchmarkPlkPlan2030PerKmGroszy = 2_570_000_000L;

        // ═══════════════════════════════════════════════════════════════════
        //  PLACEHOLDER (M-Economy Faza 5) — WYMYŚLONE, do kalibracji w M-Balance
        // ═══════════════════════════════════════════════════════════════════
        // User 2026-06-07: „wymyśl kwoty, nie jest ważne na ten moment". Brak realnego
        // źródła (w przeciwieństwie do reszty pliku) — tymczasowe, tune przy M-Balance.

        /// <summary>Mebel — flat per sztuka (placeholder): 8 tys zł.</summary>
        public const long FurnitureItemPlaceholderGroszy = 800_000L;

        /// <summary>Obrotnica outdoor (placeholder): 8 mln zł.</summary>
        public const long TurntableGroszy = 800_000_000L;

        /// <summary>Kanał/podnośnik outdoor (placeholder): 1.5 mln zł.</summary>
        public const long PitLiftGroszy = 150_000_000L;

        /// <summary>Myjnia outdoor — płyta z bramami (placeholder, mniejsza niż hala myjni 30 mln): 5 mln zł.</summary>
        public const long WashZoneOutdoorGroszy = 500_000_000L;

        /// <summary>Punkt wodowania outdoor — woda + zb. fekaliów (placeholder): 2 mln zł.</summary>
        public const long WaterServiceGroszy = 200_000_000L;
    }
}
