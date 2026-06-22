namespace RailwayManager.Timetable
{
    /// <summary>Tryb zestawienia pociągu (wpływa na czas zmiany kierunku i literę trakcyjną w numerze).</summary>
    public enum CompositionMode
    {
        /// <summary>Elektryczny lub spalinowy zespół trakcyjny — dwie kabiny, szybka zmiana kierunku.</summary>
        MultipleUnit,
        /// <summary>Lokomotywa + wagony — wymaga objazdu składu przy zmianie kierunku.</summary>
        LocoWithCars
    }

    /// <summary>Sposób przypisania konkretnych pojazdów z floty do rozkładu.</summary>
    public enum CompositionAssignment
    {
        /// <summary>Tylko założona symboliczna kompozycja ("3B+WR+2A"). Fizyczny tabor dopina obieg (M5).</summary>
        Symbolic,
        /// <summary>Konkretne pojazdy z FleetService.OwnedVehicles lockowane dla tego rozkładu.</summary>
        Concrete
    }

    /// <summary>
    /// Typ postoju na stacji wg standardów PKP/Polregio (M-TimetableUX F1.1).
    /// Walidacja per location × hasPlatform: <see cref="StopTypeValidator"/>.
    /// </summary>
    public enum StopType
    {
        /// <summary>Przelot bez zatrzymania (through pass). Wcześniej PassThrough.</summary>
        Transit,
        /// <summary>Postój handlowy — pasażerowie wsiadają/wysiadają. Wymaga peronu (hasPlatform=true).</summary>
        PH,
        /// <summary>Postój techniczny — regulacja ruchu (mijanka, time padding), bez ops pasażerskich. Wymaga isMajorStation (halt nie wspiera regulacji).</summary>
        PT,
        /// <summary>Zmiana drużyny — crew swap event. Wymaga peronu (drużyna wsiada/wysiada).</summary>
        ZD
    }

    /// <summary>Grupa pociągu wg tabeli IRJ PKP (A — pasażerskie, B — towarowe, C — luzem, D — utrzymaniowe).</summary>
    public enum IrjGroup
    {
        // ── A. Pasażerskie ──
        ExpressDomestic,            // EI — Ekspresowy krajowy (4 cyfry)
        ExpressInternational,       // EC — Ekspresowy międzynarodowy (5)
        ExpressInternationalNight,  // EN — Ekspresowy międzynarodowy nocny (5)
        InterregionalFast,          // MP — Międzywojewódzki pospieszny (5)
        InterregionalFastNight,     // MH — Międzywojewódzki pospieszny nocny (5)
        InternationalFast,          // MM — Międzynarodowy pospieszny (5)
        InterregionalLocal,         // MO — Międzywojewódzki osobowy (5)
        RegionalFast,               // RP — Wojewódzki pospieszny (5)
        RegionalAgglomeration,      // RA — Wojewódzki osobowy aglomeracyjny (5)
        RegionalInternational,      // RM — Wojewódzki osobowy międzynarodowy (5)
        RegionalLocal,              // RO — Wojewódzki osobowy (5)
        EmptyPassenger,             // PW — Próżny pasażerski (6)
        EmptyPassengerTest,         // PX — Próżny pasażerski próbny (6)

        // ── B. Towarowe międzynarodowe ──
        FreightIntlIntermodal,      // TC (6)
        FreightIntlMass,            // TG (6)
        FreightIntlNonMass,         // TR (6)

        // ── B. Towarowe krajowe ──
        FreightDomesticIntermodal,  // TD (6)
        FreightDomesticMass,        // TM (6)
        FreightDomesticNonMass,     // TN (6)
        FreightStationService,      // TK (6)
        FreightEmptyTest,           // TS (6)

        // ── C. Pojazdy luzem ──
        LoneLocoPassenger,          // LP (6)
        LoneLocoFreight,            // LT (6)
        LoneLocoShunt,              // LS (6)

        // ── D. Utrzymaniowo-naprawcze ──
        MaintenanceInspection       // ZN (6)
    }

    /// <summary>Trzecia litera kodu IRJ — typ trakcji.</summary>
    public enum TractionLetter
    {
        /// <summary>E — elektryczna lokomotywa.</summary>
        ElectricLoco,
        /// <summary>J — elektryczny zespół trakcyjny (EZT/EMU).</summary>
        ElectricUnit,
        /// <summary>S — spalinowa lokomotywa.</summary>
        DieselLoco,
        /// <summary>M — spalinowy zespół trakcyjny (SZT/DMU).</summary>
        DieselUnit
    }

    /// <summary>Status rozkładu jazdy.</summary>
    public enum TimetableStatus
    {
        Active,     // aktywny — kursuje
        Suspended,  // wstrzymany — tymczasowo nie kursuje
        Archived    // archiwalny — zakończony / historyczny
    }

    /// <summary>Typ częstotliwości kursowania rozkładu.</summary>
    public enum FrequencyType
    {
        /// <summary>Jeden kurs dziennie (lub wg kalendarza).</summary>
        Single,
        /// <summary>Takt — kurs co N minut w zadanym zakresie godzin.</summary>
        Takt
    }
}
