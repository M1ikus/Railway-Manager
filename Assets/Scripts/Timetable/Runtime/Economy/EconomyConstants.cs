namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M6.5-4: Centralne stałe ekonomiczne — koszty operacyjne, opłaty PLK,
    /// energia, paliwo, ubezpieczenia, podatki. Real-world Polska 2024-2025.
    ///
    /// Source: <c>docs/design/m6-5-economy-research.md</c> sekcja 4 (Utrzymanie operacyjne).
    /// Wszystkie kwoty w groszach (1 zł = 100 gr) — konwencja projektu.
    ///
    /// 🌍 ARCHITEKTURA PER-KRAJ (DLC ready, post-EA):
    ///   - Stale tu = PL (taryfy PLK / PGE EK / Orlen / URE / UTK 2024-25).
    ///   - W EA wszystkie pociągi/pojazdy uznawane za PL (depot PL).
    ///   - W DLC (DE/CZ/SK): per-kraj stale (np. <c>TuiDeStandardGroszy</c>) +
    ///     resolver wg kraju depotu/trasy. Implementacja post-EA.
    ///
    /// Caveat: niektóre wartości są placeholder/uśrednione — szczegółowe rozdzielenie
    /// (np. TUI per klasa linii zamiast średniej) odłożone do <b>M-Balance</b>.
    /// </summary>
    public static class EconomyConstants
    {
        // ═══════════════════════════════════════════════════════════════════
        //  TUI (Track Use Indemnity) — opłaty PKP PLK za dostęp do infrastruktury
        // ═══════════════════════════════════════════════════════════════════
        // Stawki obowiązują od 2022 r., niezmienione w rozkładzie 2024/2025
        // (decyzja UTK XII 2023). Macierz kategoria linii (1-7) × przedział masy.
        //
        // W EA upraszczamy do 5 klas (Premium/I/II/III/Lokalna) + 3 typów
        // (pasażer/towar/manewry). M-Balance: rozdzielenie per real macierz PLK.

        /// <summary>Średnia stawka PLK pasażerski 2024-25: 6.31 zł/pociągokm.</summary>
        public const int TuiPasazerskiSredniaGroszy = 631;

        /// <summary>Średnia stawka PLK towarowy 2024-25: 12.77 zł/pociągokm.</summary>
        public const int TuiTowarowySredniaGroszy = 1277;

        /// <summary>Średnia opłata manewrowa 2024-25: 3.66 zł/pociągokm (trakcja własna).</summary>
        public const int TuiManewryGroszy = 366;

        // Per-klasa-linii (placeholder dla M-Balance refactor)
        public const int TuiPasazerskiMagistralaGroszy   = 1000; // 10.00 zł/pkm — najwyższa kat.
        public const int TuiPasazerskiPierwszorzednaGroszy =  800; //  8.00 zł/pkm
        public const int TuiPasazerskiDrugorzednaGroszy  =  600; //  6.00 zł/pkm — średnia
        public const int TuiPasazerskiMiejscowaGroszy    =  400; //  4.00 zł/pkm
        public const int TuiPasazerskiLokalnaGroszy      =  300; //  3.00 zł/pkm — najniższa

        public const int TuiTowarowyMagistralaGroszy     = 2000; // 20.00 zł/pkm
        public const int TuiTowarowyDrugorzednaGroszy    = 1200; // 12.00 zł/pkm
        public const int TuiTowarowyLokalnaGroszy        =  600; //  6.00 zł/pkm

        // ═══════════════════════════════════════════════════════════════════
        //  Energia trakcyjna (PGE Energetyka Kolejowa, taryfa C1x)
        // ═══════════════════════════════════════════════════════════════════
        // Realna cena brutto 2024-25: 0.90-1.30 zł/kWh (cena energii + dystrybucja
        // + opłata mocowa + akcyza). Środek widełek: 1.10 zł/kWh.

        /// <summary>Cena energii trakcyjnej brutto: 1.10 zł/kWh (110 gr).</summary>
        public const int EnergyTrakcyjnaPlnPerKwhGroszy = 110;

        /// <summary>Zużycie EZT lekki (EN57, EN71) z rekuperacją: 5 kWh/pockm. Bez rekuperacji + ogrzewanie zima ×2.</summary>
        public const int EnergyConsumptionEztLekkiKwhPerKm = 5;

        /// <summary>Zużycie EZT średni (Impuls, FLIRT): 7 kWh/pockm.</summary>
        public const int EnergyConsumptionEztSredniKwhPerKm = 7;

        /// <summary>Zużycie EZT długi (FLIRT_ED160 8-czł.): 10 kWh/pockm.</summary>
        public const int EnergyConsumptionEztDlugiKwhPerKm = 10;

        /// <summary>Zużycie lokomotywa elektryczna + 6-7 wagonów (EU07+TLK): 15 kWh/pockm.</summary>
        public const int EnergyConsumptionLokoElektrycznaKwhPerKm = 15;

        /// <summary>Zużycie ekspres 160 km/h (Vectron+IC, FLIRT_ED160 high-speed): 18 kWh/pockm.</summary>
        public const int EnergyConsumptionEkspresKwhPerKm = 18;

        // ═══════════════════════════════════════════════════════════════════
        //  Paliwo (diesel hurt Orlen B2B, taryfa 2025)
        // ═══════════════════════════════════════════════════════════════════
        // Hurt 5500-6700 zł/m³ netto (kwiecień 2025). Karty paliwowe + przetargi
        // przewoźników kolejowych: 5.50-6.00 zł/L netto.

        /// <summary>Cena diesla netto hurt dla floty kolejowej: 5.75 zł/L (575 gr).</summary>
        public const int DieselPriceGroszyPerLiter = 575;

        /// <summary>Zużycie SM42 manewrowe (lekki ruch, średni cykl): 2.0 L/km.</summary>
        public const float FuelConsumptionSm42LitersPerKm = 2.0f;

        /// <summary>Zużycie SU45/SU46 (liniowa spalinowa): 2.5 L/km.</summary>
        public const float FuelConsumptionSu45LitersPerKm = 2.5f;

        /// <summary>Zużycie SU42 (lekka liniowa spalinowa): 2.0 L/km.</summary>
        public const float FuelConsumptionSu42LitersPerKm = 2.0f;

        /// <summary>Zużycie 754 Brejlovec (CZ, 1460 kW): 2.8 L/km.</summary>
        public const float FuelConsumption754LitersPerKm = 2.8f;

        /// <summary>Zużycie SA134/SA136 Bydgostia (silniki klasyczne): 1.5 L/km.</summary>
        public const float FuelConsumptionSaBydgostiaLitersPerKm = 1.5f;

        /// <summary>Zużycie SA137/SA138 nowsze Pesa (efektywniejsze MTU PowerPack): 1.3 L/km.</summary>
        public const float FuelConsumptionSaNowyLitersPerKm = 1.3f;

        // ═══════════════════════════════════════════════════════════════════
        //  Postoje na stacjach (Cennik OIU PKP PLK 2024)
        // ═══════════════════════════════════════════════════════════════════
        // Bardzo zróżnicowane wg kategorii peronu i wielkości stacji.
        // Mała stacja: 0.68 zł/postój. Duży węzeł (Łódź Fabryczna): 138.72 zł/postój.

        /// <summary>Postój komercyjny stacja Premium (Warszawa Centralna, Kraków Główny): 130 zł.</summary>
        public const int PlatformFeePremiumGroszy = 13000;

        /// <summary>Postój komercyjny stacja kategoria I (duże miasto wojewódzkie): 50 zł.</summary>
        public const int PlatformFeeKategoriaIGroszy = 5000;

        /// <summary>Postój komercyjny stacja kategoria II (średnie miasto): 20 zł.</summary>
        public const int PlatformFeeKategoriaIIGroszy = 2000;

        /// <summary>Postój komercyjny stacja kategoria III (mała stacja): 5 zł.</summary>
        public const int PlatformFeeKategoriaIIIGroszy = 500;

        /// <summary>Postój komercyjny halt / mijanka: 1 zł.</summary>
        public const int PlatformFeeHaltGroszy = 100;

        // TD-036a: progi StationImportance → kategoria opłaty postojowej. Mapowanie wg wzoru
        // StationImportance.Calculate (base 1 + major 2 + perony 0.3/szt + węzeł 1 + 0.5×log10(pop)
        // + nazwa „Główna/Centralna" 2): polny halt ~1.3, halt przy miasteczku ~3.3, mała stacja ~5.7,
        // wojewódzka ~10, W-wa Centralna ~11. Kalibracja progów → M-Balance.
        /// <summary>Importance ≥ próg → stawka Premium (130 zł).</summary>
        public const float PlatformFeeImportancePremium = 10.5f;
        /// <summary>Importance ≥ próg → kategoria I (50 zł).</summary>
        public const float PlatformFeeImportanceKategoriaI = 8f;
        /// <summary>Importance ≥ próg → kategoria II (20 zł).</summary>
        public const float PlatformFeeImportanceKategoriaII = 5f;
        /// <summary>Importance ≥ próg → kategoria III (5 zł); poniżej = halt (1 zł).</summary>
        public const float PlatformFeeImportanceKategoriaIII = 2.5f;

        /// <summary>Postój techniczny / overnight: ~3 zł/godz × długość pociągu (m). Manual calc per pojazd.</summary>
        public const int PostojTechnicznyZlPerHourPerMeterGroszy = 300;

        // ═══════════════════════════════════════════════════════════════════
        //  Woda (mycie pojazdów)
        // ═══════════════════════════════════════════════════════════════════
        // Taryfa wody przemysłowej + ścieków średnia PL 2024: ~12-14 zł/m³.

        /// <summary>Cena wody przemysłowej + ścieki: 13 zł/m³ (1300 gr).</summary>
        public const int WaterIndustrialGroszyPerM3 = 1300;

        /// <summary>Zużycie wody na cykl mycia EZT 200m bez recyrkulacji: 12 m³.</summary>
        public const int WaterPerEztWashCycleM3 = 12;

        /// <summary>Zużycie wody na cykl mycia EZT 200m z recyrkulacją (nowoczesna myjnia): 3 m³.</summary>
        public const int WaterPerEztWashCycleRecyrkulacjaM3 = 3;

        /// <summary>Zużycie wody mycie lokomotywa solo: 2 m³.</summary>
        public const int WaterPerLocoWashCycleM3 = 2;

        // ═══════════════════════════════════════════════════════════════════
        //  Ubezpieczenia
        // ═══════════════════════════════════════════════════════════════════
        // OC przewoźnika kolejowego — minimalna suma gwarancyjna 2026 (UTK):
        // mainstream przewoźnik = 2.5 mln EUR (~10.5 mln zł).
        // Składka realna: 80-300k zł/rok bazowa (zależy od historii szkodowej).
        // Casco taboru: 0.15-0.40% wartości pojazdu/rok.

        /// <summary>Składka roczna OC przewoźnika kolejowego: 150k zł (środek widełek 80-300k).</summary>
        public const int OcCarrierAnnualPremiumGroszy = 15_000_000;

        /// <summary>Casco roczna stawka jako % wartości pojazdu (0.25%).</summary>
        public const float CascoVehicleAnnualRatePercent = 0.0025f;

        // ═══════════════════════════════════════════════════════════════════
        //  GSM-R (komunikacja kolejowa)
        // ═══════════════════════════════════════════════════════════════════
        // Czesne PLK + utrzymanie + amortyzacja terminalu = ~10k zł/pojazd/rok.
        // Konkretne ceny abonamentu niejawne (PLK rozlicza się indywidualnie).

        /// <summary>GSM-R all-in (abonament + utrzymanie + amortyzacja terminalu): 10k zł/pojazd/rok.</summary>
        public const int GsmRAllInPerVehicleAnnualGroszy = 1_000_000;

        // ═══════════════════════════════════════════════════════════════════
        //  Licencje / certyfikacja UTK
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Świadectwo bezpieczeństwa przewoźnika (nowe wydanie): 7000 zł.</summary>
        public const int UtkSafetyCertificateNewGroszy = 700_000;

        /// <summary>Przedłużenie świadectwa bezpieczeństwa: 1500 zł (cyklicznie co kilka lat).</summary>
        public const int UtkSafetyCertificateRenewalGroszy = 150_000;

        /// <summary>Licencja maszynisty (one-time): 100 zł.</summary>
        public const int UtkDriverLicenseGroszy = 10_000;

        /// <summary>Egzamin państwowy na licencję maszynisty: 150 zł.</summary>
        public const int UtkDriverExamGroszy = 15_000;

        /// <summary>Egzamin świadectwa maszynisty per kategoria/seria pojazdu: 600 zł.</summary>
        public const int UtkDriverCategoryExamGroszy = 60_000;

        /// <summary>Średnie roczne wydatki UTK dla średniego przewoźnika (3-5 typów pojazdów, 50-100 maszynistów): 100k zł.</summary>
        public const int UtkAnnualCostMediumCarrierGroszy = 10_000_000;

        // ═══════════════════════════════════════════════════════════════════
        //  Podatek od nieruchomości
        // ═══════════════════════════════════════════════════════════════════
        // Stawki maksymalne 2025 (Min. Finansów). Gminy mogą obniżać 70-95%.

        /// <summary>Podatek od nieruchomości: budynki działalności gospodarczej (hala, biuro, magazyn): 34 zł/m²/rok.</summary>
        public const int PropertyTaxBuildingPerSqMAnnualGroszy = 3400;

        /// <summary>Podatek od nieruchomości: grunty pod działalność: 1.38 zł/m²/rok.</summary>
        public const int PropertyTaxLandPerSqMAnnualGroszy = 138;

        // ═══════════════════════════════════════════════════════════════════
        //  ISO audity (opcjonalne, branżowy standard)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>Roczne utrzymanie 3 ISO (9001+14001+45001) dla średniego przewoźnika: 100k zł.</summary>
        public const int IsoAuditsAnnualMediumCarrierGroszy = 10_000_000;
    }
}
