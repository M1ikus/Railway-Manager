namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1: Wszystkie balans-stale dla systemu Personnel.
    ///
    /// Wartosci wytyczne — finalna kalibracja w <b>M6.5 Rebalance</b> (post-M13 Save/Load).
    /// Uklad referencji decyzji (D*) — patrz <c>docs/design/m8-personnel.md</c>.
    ///
    /// Kwoty w groszach (1 zl = 100 groszy) — zgodnie z konwencja projektu (M6 EconomyManager).
    /// </summary>
    public static class PersonnelBalanceConstants
    {
        // ═══ Salaries (base per role, miesieczne BRUTTO PRACOWNIKA w groszach) ═══
        // Wzor: salary = base × (0.7 + 0.15 × skill) — 1★=0.85x, 5★=1.45x
        //
        // Wartosci PL 2024-2025 z real-world research (docs/design/m6-5-economy-research.md sekcja 2):
        // base × 1.15 (skill 3) ≈ srednia rynkowa.
        //
        // ⚠️ Floor: minimalna krajowa 2025 = 4666 zl brutto. Cleaner/WashBay/TicketClerk
        //   bumpniete do base 5500 zeby 1★ × 0.85 = 4675 ≈ minimalka.
        //
        // 🌍 ARCHITEKTURA PER-KRAJ (DLC ready, post-EA):
        //   - Te stale to PL salaries.
        //   - W EA wszyscy pracownicy maja Employee.countryCode = "PL".
        //   - W DLC (DE, CZ): osobne stale per kraj (np. BaseSalaryDriver_DE) +
        //     per-kraj resolver. RoleDefinitions.GetBaseSalaryGroszy(role, countryCode).
        //   - Lokalizacja depotu (post-EA): Depot ma countryCode, pracownik dziedziczy
        //     z depotu w ktorym jest zatrudniony. Niemiecki depot → niemieckie pensje.
        //   - ZUS pracodawcy multiplier per-kraj: PayrollConstants.GetEmployerCostMultiplier(countryCode)

        public const int BaseSalaryDriver            = 680_000; // 6800 zl (real PKP IC+Polregio sr. 7800 zl / 1.15) — was 4500, +51%
        public const int BaseSalaryConductor         = 480_000; // 4800 zl (real PKP IC+Polregio sr. 5500) — was 3200, +50%
        public const int BaseSalaryMechanic          = 610_000; // 6100 zl (real P1-P5 sr. 7000) — was 3800, +60%
        public const int BaseSalaryCleaner           = 550_000; // 5500 zl (real outsourced 5000, ale minimalka floor) — was 2400, +129%
        public const int BaseSalaryWashBay           = 550_000; // 5500 zl (real 5200, minimalka floor) — was 2200, +150%
        public const int BaseSalaryOffice            = 590_000; // 5900 zl (real sekretarka/specjalista sr. 6800) — was 3000, +97%
        public const int BaseSalaryResearch          = 1_170_000; // 11700 zl (real R&D inzynier 5+ lat sr. 13500) — was 5000, +134%
        public const int BaseSalaryTicket            = 550_000; // 5500 zl (real kasjer 5200, minimalka floor) — was 2600, +112%
        public const int BaseSalaryDispatcher        = 680_000; // 6800 zl (real dyspozytor przewoznika sr. 7800) — was 3500, +94% (D27)
        public const int BaseSalaryTrafficController = 650_000; // 6500 zl (real PLK dyzurny ruchu sr. 7500) — was 3200, +103% (D28)

        public const float SkillSalaryMultBase = 0.7f;
        public const float SkillSalaryMultPerStar = 0.15f;

        // ═══ Fatigue rates (per godzina na zmianie) ═══

        public const float FatigueRateDriverPerHour            = 1.2f;
        public const float FatigueRateConductorPerHour         = 0.9f;
        public const float FatigueRateMechanicPerHour          = 0.8f;
        public const float FatigueRateCleanerPerHour           = 1.0f;
        public const float FatigueRateWashPerHour              = 0.8f;
        public const float FatigueRateOfficePerHour            = 0.5f;
        public const float FatigueRateResearchPerHour          = 0.4f;
        public const float FatigueRateTicketPerHour            = 0.8f;
        public const float FatigueRateDispatcherPerHour        = 0.6f;
        public const float FatigueRateTrafficControllerPerHour = 0.7f;

        public const float FatigueRegenDayOff = 80f;       // pelen reset po dniu wolnym
        public const float FatigueRegenShiftEnd = 20f;     // nocleg w domu po zmianie

        // ═══ Morale ═══
        // BUG-060 v2: morale rozdzielone na 4 bucket'y per source (sumują się do max 100).
        // Każdy bucket ma własny cap — bonus z roomu nigdy nie marnuje się "ciemno"
        // przez global clamp 100. Inwestycja w lvl 5 Supervisor zawsze ma value
        // (max +15 osiągalne niezależnie od salary/fatigue/overtime stanu).
        // External events (bonus, missed, hotel reject, fired colleague) → modyfikują
        // odpowiedni bucket z trim do cap'a.

        /// <summary>BUG-060 v2: cap dla salary contribution bucket'u. Pensja porównana z rynkiem.</summary>
        public const int MoraleSalaryCapMax = 35;
        /// <summary>BUG-060 v2: cap dla fatigue contribution bucket'u. Pracownik nie zmęczony.</summary>
        public const int MoraleFatigueCapMax = 25;
        /// <summary>BUG-060 v2: cap dla overtime contribution bucket'u. Pracownik nie pracuje overtime/Night przy fatigue.</summary>
        public const int MoraleOvertimeCapMax = 25;
        /// <summary>BUG-060 v2: cap dla room contribution bucket'u. Supervisor + Social + Bathroom upgrades.</summary>
        public const int MoraleRoomCapMax = 15;

        public const int MoraleStartNewHire = 70;
        public const int MoraleDailySalaryAbove = 1;       // +1/day gdy pensja &gt;10% powyzej rynku
        public const int MoraleDailySalaryBelow = -2;
        public const int MoraleMissedPayment = -20;
        public const int MoraleOvertimePenalty = -1;
        public const int MoraleFatigueOverPenalty = -3;
        public const int MoraleTrainingSuccess = 15;       // POST-EA
        public const int MoraleBonusPerThousand = 1;       // +1 morale per 1000 zl premii
        public const int MoraleBonusMaxPerEvent = 20;
        public const int MoraleColleagueFired = -2;        // 3 dni
        public const int MoraleStrikeSuccess = 25;
        public const int MoraleStrikeFailure = -15;

        public const int MoraleThresholdStrikeWarning = 30;
        public const int MoraleThresholdStrikeGeneral = 20;
        public const int MoraleThresholdRoleStrike = 25;
        public const int StrikeWarningDaysRequired = 5;
        public const int StrikeGeneralDaysRequired = 7;
        public const int StrikeWarningHours = 24;
        public const int StrikeGeneralHours = 72;
        public const int StrikeNegotiationWindowHours = 48;

        // Morale buckets behavior
        public const int MoraleBucketEnthusiasticMin = 80;  // +5% wydajnosc, -50% error
        public const int MoraleBucketNormalMin = 60;
        public const int MoraleBucketDissatisfiedMin = 40;  // -5% wydajnosc
        public const int MoraleBucketAtRiskMin = 20;        // 1%/dzien wypowiedzenie
        // ponizej 20 = Critical, 3%/dzien

        // ═══ Shifts (sekundy doby, D4) ═══

        public const int ShiftMorningStartSec   = 6 * 3600;  // 21_600 = 06:00
        public const int ShiftMorningEndSec     = 14 * 3600;
        public const int ShiftAfternoonStartSec = 14 * 3600;
        public const int ShiftAfternoonEndSec   = 22 * 3600;
        public const int ShiftNightStartSec     = 22 * 3600; // rollover doby
        public const int ShiftNightEndSec       = 6 * 3600;  // nastepnego dnia
        public const int ShiftDurationSec       = 8 * 3600;

        // ═══ Market kandydatow (D7) ═══

        public const int CandidateMarketRefreshDays = 7;
        public const int CandidateMarketRefreshAddCount = 4;
        public const int CandidateMarketMaxSize = 30;
        public const int CandidateValidityDays = 7;

        // Dystrybucja skill (D7): 1★=35%, 2★=30%, 3★=20%, 4★=12%, 5★=3%
        public const float CandidateSkillDist1Star = 0.35f;
        public const float CandidateSkillDist2Star = 0.30f;
        public const float CandidateSkillDist3Star = 0.20f;
        public const float CandidateSkillDist4Star = 0.12f;
        public const float CandidateSkillDist5Star = 0.03f;

        public const float CandidateSalaryVariance = 0.10f;  // ±10% od base × skill mult
        public const float CandidateHireBonusChance = 0.05f; // 5% kandydatow ma
        public const float CandidateHireBonusMinPct = 0.05f; // 5% rocznej pensji
        public const float CandidateHireBonusMaxPct = 0.25f; // 25% rocznej pensji

        // ═══ Job Posting (D7) ═══

        public const int JobPostingDurationDays = 14;
        public const int JobPostingBaseCost = 300_000;    // 3000 zl
        public const int JobPostingSpecialistMultPerStar = 100_000;  // +1000 zl per target star
        public const int JobPostingMaxActive = 3;

        // ═══ Training — POST-EA (M8.5, D11), stub values ═══
        // public const int TrainingExternalDaysBase = 14;
        // public const int TrainingExternalCost1to2 = 800_000;
        // ... (implementacja w M8.5)

        // ═══ Dispatcher (D27) ═══

        public const int DispatcherBaseCapacity = 50;      // 1-star capacity
        public const int DispatcherCapacityPerStar = 5;    // +5 per star above 1
        public const float DispatcherOverCapacityDelayHoursMin = 2f;
        public const float DispatcherOverCapacityDelayHoursMax = 6f;
        public const float DispatcherCriticalOverThreshold = 1.5f;
        public const float DispatcherMissedActionChance = 0.2f;
        public const int DispatcherAutoTimeoutHours = 1;   // L4 replacement timeout

        // ═══ Traffic Controller (D28) ═══

        public const int TrafficControllerBaseCapacity = 10;
        public const int TrafficControllerCapacityPerStar = 5;
        public const float TrafficControllerCriticalOverThreshold = 1.5f;
        public const float TrafficControllerOverCapacityDelayMult = 0.5f;  // +50% time
        public const float TrafficControllerCriticalMissChance = 0.25f;

        public const float DepotMovementNoControllerEfficiency = 0.7f;
        public const float DepotMovementRandomDelayVariance = 0.2f;        // ±20% bez dyzurnego

        // Priorytety domyslne (D34, sliders w UI)
        public const int TrafficPriorityWorkshopOverdue    = 100;
        public const int TrafficPriorityScheduledDeparture = 80;
        public const int TrafficPriorityWashBayPlanned     = 60;
        public const int TrafficPriorityParkingReshuffle   = 40;

        // ═══ 24/7 coverage requirement (D35) ═══

        public const int DispatcherMin24_7Count = 3;       // 3 zmiany × 1 osoba
        public const int TrafficControllerMin24_7Count = 3;

        // ═══ Dispatcher auto-assign weights (D30) ═══

        public const float DispatcherWeightProximity  = 0.4f;
        public const float DispatcherWeightSkillMatch = 0.3f;
        public const float DispatcherWeightRestedness = 0.3f;
        // Skill 1★ ignoruje restedness (tylko proximity); 5★ uwzglednia wszystko

        /// <summary>
        /// Maksymalna odległość (PathGraph units, ~metry) gdzie proximity zaczyna spadać do 0.
        /// Mapa Polski ~600km Warszawa-Hel; 400000m=400km daje sensowny gradient
        /// (kandydat w tym samym województwie ~50-100km = ~0.75-0.88 proximity,
        /// kandydat z drugiego końca Polski ~500km = 0 proximity).
        /// </summary>
        public const float DispatcherMaxProximityDistance = 400000f;

        /// <summary>
        /// MM-D11/D20: bazowy czas onboarding'u dyspozytora dla Dispatcher room lvl 3 (baseline 1.0×).
        /// Lvl 1 = 2.0× = 30 min, lvl 5 = 0.5× = 7.5 min. Placeholder kalibracja, dopracowanie M-Balance.
        /// </summary>
        public const float DispatcherBaseOnboardingMinutes = 15f;

        /// <summary>
        /// BUG-068 rotation fairness: max liczba zliczanych ostatnich dyspozycji per pracownik.
        /// Powyżej zerujemy penalty (cap'd score reduction).
        /// </summary>
        public const int DispatcherRecentPenaltyMax = 5;

        /// <summary>
        /// BUG-068 rotation fairness: kara do score'a per ostatnia dyspozycja (max
        /// <see cref="DispatcherRecentPenaltyMax"/> zliczanych = max -0.5 score reduction).
        /// </summary>
        public const float DispatcherRecentPenaltyPerCount = 0.1f;

        /// <summary>
        /// Waga morale w PickReplacement score (D30 D-Bug fix dodatkowy do 3-wagowej formuły).
        /// Preferuje kandydatów z wyższym morale (less risk of leaving).
        /// </summary>
        public const float DispatcherMoraleScoreWeight = 0.05f;

        // ═══ Retirement + Sick (D13) ═══

        public const int RetirementAgeMin = 60;
        public const int RetirementAgeMax = 65;
        public const int RetirementAgeForce = 70;
        public const float RetirementChance60to64PerMonth = 0.05f;
        public const float RetirementChance65plusPerDay = 0.20f;
        public const int RetirementNoticeDays = 30;
        public const int RetirementSeveranceMonths = 3;

        public const float SickLeaveChancePerDay = 0.005f;
        public const float SickLeaveFatigueModifier = 0.02f;  // ×(fatigue &gt; 50)
        public const int SickLeaveMinDays = 1;
        public const int SickLeaveMaxDays = 3;

        // ═══ Severance (zwolnienie) — mnoznik pensji miesiecznej ═══

        public const int SeveranceUnder1Month = 0;
        public const int Severance1To6Months = 1;
        public const int Severance6To24Months = 2;
        public const int SeveranceOver2Years = 3;

        // ═══ Vacation (urlopy, D13) ═══

        public const int VacationDaysPerYear = 26;
        public const int VacationRolloverMaxDays = 10;

        // ═══ Crew Circulation (D5, D20) ═══

        public const int CrewMaxWorkHoursPerDay = 12;      // limit 12h/dobe
        public const int CrewMinBreakAfterHours = 4;       // po 4h ciaglej pracy min przerwa
        public const int CrewMinBreakMinutes = 30;
        public const int CrewHandoverMinMinutes = 5;
        public const int CrewMaxMultiDayDays = 3;          // EA limit (D20)

        // ═══ Hotels (multi-day turnusy, D20, D29) ═══

        public const int HotelCostBasicPerNight    = 8_000;   // 80 zl
        public const int HotelCostStandardPerNight = 15_000;  // 150 zl
        public const int HotelCostPremiumPerNight  = 25_000;  // 250 zl

        public const int HotelMoraleBasicPenalty   = -1;
        public const int HotelMoraleStandardBonus  = 0;       // neutralne
        public const int HotelMoralePremiumBonus   = 2;

        public const float HotelBasicFatigueRegenMult    = 0.8f;
        public const float HotelStandardFatigueRegenMult = 1.0f;
        public const float HotelPremiumFatigueRegenMult  = 1.2f;

        public const float HotelBasicErrorChanceMult    = 1.1f;
        public const float HotelStandardErrorChanceMult = 1.0f;
        public const float HotelPremiumErrorChanceMult  = 0.9f;

        public const int HotelPrivateFallbackMult = 2;        // 2× cost Standard
        public const int HotelPrivateFallbackMoralePenalty = -3;

        // ═══ Cleaning (Sprzatacz, D2) ═══

        public const float CleaningInteriorRestorePerMinute = 20f;    // % / minuta
        public const float CleaningDegradationPerKm = 0.0003f;
        public const int CleaningInteriorStart = 100;                 // 0-100 scale

        // ═══ Office clerks ═══

        public const float OfficeFixedCostReductionPerClerkPerStar = 0.01f;  // 1% per clerk per star
        public const float OfficeFixedCostReductionMax = 0.30f;              // cap 30%

        // ═══ Ticket clerks (D19) ═══

        public const float TicketClerkRevenueBonus = 0.08f;  // +8% z tej stacji

        // ═══ Conductor requirement (D16 + D31: wagon/EMU/DMU osobno) ═══

        public const int ConductorRequiredFromWagonCount = 3;  // &gt;3 wagonow pasazerskich
        public const int ConductorRequiredFromEmuCount = 1;    // &gt;1 EMU
        public const int ConductorRequiredFromDmuCount = 1;    // &gt;1 DMU
        public const float FareEvasionWithoutConductor = 0.15f; // +15% gapowiczow

        // ═══ Walk simulator 3D (D26) ═══

        public const float WalkSpeedNormalMps = 1.4f;
        public const float WalkSpeedHurryMps = 2.5f;
        public const float WalkSimUpdateRateIdleHz = 1f;
        public const float WalkSimUpdateRateActiveHz = 10f;
        public const int WalkSimMaxActiveCapsules = 50;   // object pooling cap dla widocznych capsules

        // ═══ TD-025: Workflow loop 3D ═══

        /// <summary>Ile minut przed startem <see cref="CrewDuty"/> Driver/Conductor zaczyna
        /// <see cref="EmployeeWorkflowState.ComingToDepot"/>. Future: zależny od morale
        /// (gracz z wyższym morale przychodzi wcześniej). EA: stała 30 min.</summary>
        public const int CrewReportLeadMinutes = 30;

        /// <summary>Częstotliwość tick'u state machine <c>PersonnelDispatcher3D</c>.
        /// 1 Hz = re-evaluation per pracownik raz na sekundę gry. Walk visual tickuje
        /// w FixedUpdate (50 Hz) niezależnie.</summary>
        public const float WorkflowTickIntervalSec = 1f;

        /// <summary>Czas trwania meldunku u dyspozytora (<see cref="EmployeeWorkflowState.ReportingToDispatcher"/>).
        /// 8 sekund — wystarczy żeby gracz zauważył animację, nie nudząc.</summary>
        public const float MeldunekDurationSec = 8f;

        /// <summary>Sprzątanie jednego pojazdu (Cleaner WorkingMobile timer). Po tym czasie
        /// <c>cleanlinessPercent = 100</c>. EA stała, post-EA może zależeć od skill.</summary>
        public const float CleaningSecondsPerVehicle = 60f;

        // ── TD-034: czynności osobiste (czasy + efekt placeholder, balans → M-Balance) ──

        /// <summary>TD-034: czas przebrania przy szafce (ChangingClothes, LockerIn/Out). Placeholder.</summary>
        public const float LockerChangeDurationSec = 45f;

        /// <summary>TD-034: czas wizyty w łazience (UsingBathroom). Placeholder.</summary>
        public const float BathroomDurationSec = 60f;

        /// <summary>TD-034: czas przerwy/posiłku (OnBreak). Placeholder.</summary>
        public const float BreakDurationSec = 300f;

        /// <summary>TD-034: ulga zmęczenia po przerwie (pkt fatigue 0-100). Lekki addytywny efekt
        /// (decyzja #1). Bathroom/Locker bez efektu stat (diegetyczne). Placeholder → M-Balance.</summary>
        public const int BreakFatigueReliefPts = 3;

        /// <summary>Tolerancja "dotarł" — gdy capsule jest bliżej niż X metrów od waypoint'a
        /// finalnego, walk task zakończony i wywoływany onArrive.</summary>
        public const float EmployeeArriveThresholdM = 0.5f;

        /// <summary>Cap visual rendering: pracownicy dalej niż X metrów od Camera.main mają
        /// disabled main mesh (tylko label). Już używane przez <see cref="EmployeeVisual"/>.</summary>
        public const float WalkLodLabelOnlyDistanceM = 100f;

        // Kolory placeholder capsules (hex encoded jako uint RRGGBB, dla RoleDefinitions)
        // Uzywane w EmployeeVisual (M8-10)
        public const uint ColorDriverRgb         = 0x1E3A8A; // granatowy
        public const uint ColorConductorRgb      = 0x15803D; // zielony
        public const uint ColorMechanicRgb       = 0xEA580C; // pomaranczowy
        public const uint ColorCleanerRgb        = 0x6B7280; // szary
        public const uint ColorWashBayRgb        = 0x38BDF8; // jasnoniebieski
        public const uint ColorOfficeRgb         = 0xF3F4F6; // bialy
        public const uint ColorResearchRgb       = 0x7C3AED; // fioletowy
        public const uint ColorTicketRgb         = 0xFACC15; // zolty (unused in 3D, popup-only)
        public const uint ColorDispatcherRgb     = 0x14B8A6; // turkusowy
        public const uint ColorTrafficCtrlRgb    = 0xDC2626; // czerwony (high visibility)

        // ═══ Remote Work penalty (brak odpowiedniego pomieszczenia) ═══

        public const float RemoteWorkPerformanceMult = 0.9f; // -10% wydajnosc
        public const int RemoteWorkMoralePenalty = -5;       // per dzien
        public const float RemoteWorkDispatcherCapacityMult = 0.9f;  // -10%
        public const float RemoteWorkTrafficControllerCapacityMult = 0.85f; // -15%
    }
}
