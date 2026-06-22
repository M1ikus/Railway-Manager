namespace RailwayManager.Timetable
{
    /// <summary>
    /// Wszystkie wartości kalibracyjne kreatora rozkładów w jednym miejscu.
    /// Zmiana wartości tutaj wpływa na całą logikę klasyfikacji, wyliczania czasów, walidacji.
    /// Nie wrzucać magic numbers do algorytmów — zawsze odczytywać z tej klasy.
    /// </summary>
    public static class TimetableTuningConstants
    {
        // ─────────────────────────────────────────────────────────
        //  Klasyfikacja kategorii rozkładowej z % postojów
        // ─────────────────────────────────────────────────────────

        /// <summary>Powyżej tego % postojów (od wszystkich stacji na trasie) — kategoria "osobowy" (Ro/Mo).</summary>
        public const float StopRatioThresholdLocal = 0.80f;

        /// <summary>Powyżej tego % postojów — "pospieszny" (Rp/Mp). Poniżej obu — "ekspres" (EI).</summary>
        public const float StopRatioThresholdFast = 0.40f;

        /// <summary>Minimalna Vmax rozkładu żeby zakwalifikować jako ekspresowy (EI).</summary>
        public const int ExpressMinSpeedKmh = 160;

        // ─────────────────────────────────────────────────────────
        //  Kategoria nocna
        // ─────────────────────────────────────────────────────────

        /// <summary>Godzina początku okna "nocnego" — start z pierwszej stacji w tym oknie = kategoria nocna.</summary>
        public const int NightStartHour = 22;

        /// <summary>Godzina końca okna "nocnego" (godzina nie wchodzi już do okna — do 03:59 włącznie).</summary>
        public const int NightEndHour = 4;

        // ─────────────────────────────────────────────────────────
        //  Czasy zmiany kierunku (reverse)
        // ─────────────────────────────────────────────────────────

        /// <summary>Minimalny czas postoju dla zespołu trakcyjnego przy zmianie kierunku (sekundy).</summary>
        public const int ReverseStopSecondsMultipleUnit = 120;   // 2 min

        /// <summary>Minimalny czas postoju dla lok+wagonów przy zmianie kierunku (sekundy).</summary>
        public const int ReverseStopSecondsLocoWithCars = 600;   // 10 min

        // ─────────────────────────────────────────────────────────
        //  Uproszczona fizyka ruchu
        // ─────────────────────────────────────────────────────────

        /// <summary>Domyślne przyspieszenie rozruchu [m/s²] — używane gdy nic lepszego nie wiadomo.</summary>
        public const float DefaultAccelerationMs2 = 0.8f;

        /// <summary>Domyślne opóźnienie hamowania [m/s²] dla BrakeRegime.R (pospieszny).</summary>
        public const float DefaultDecelerationMs2 = 1.0f;

        /// <summary>Mnożnik przyspieszenia dla zespołów trakcyjnych (EMU/DMU) względem lok+wagon.</summary>
        public const float MultipleUnitAccelBonus = 1.25f;

        /// <summary>Mnożnik opóźnienia dla nastawy P (osobowy) względem R.</summary>
        public const float BrakeRegimeP_DecelMultiplier = 0.75f;

        /// <summary>Mnożnik opóźnienia dla nastawy G (towarowy) — bardzo łagodne hamowanie.</summary>
        public const float BrakeRegimeG_DecelMultiplier = 0.5f;

        /// <summary>Bonus opóźnienia dla R+Mg (hamulec szynowy magnetyczny).</summary>
        public const float BrakeRegimeRMg_DecelMultiplier = 1.3f;

        /// <summary>Bonus opóźnienia dla R+E (hamulec elektrodynamiczny).</summary>
        public const float BrakeRegimeRE_DecelMultiplier = 1.15f;

        // ─────────────────────────────────────────────────────────
        //  Walidacja / kolizje
        // ─────────────────────────────────────────────────────────

        /// <summary>Ile sekund w obie strony szukać wolnego slotu gdy kolizja rezerwacji (domyślnie ±30 min).</summary>
        public const int ReservationSlotSearchRangeSec = 30 * 60;

        /// <summary>Domyślny minimalny czas postoju w sekundach gdy kategoria handlowa go nie określa.</summary>
        public const int DefaultMinStopSeconds = 30;

        /// <summary>Maksymalny dopuszczalny czas postoju (żeby nie pozwolić na absurdalne wartości).</summary>
        public const int MaxStopSeconds = 3600; // 1h

        // ─────────────────────────────────────────────────────────
        //  Predykcyjny dispatcher mapy OSM (M-Dispatch, Faza 1+)
        // ─────────────────────────────────────────────────────────

        /// <summary>Jak daleko w przyszłość (sekundy symulacji) liczyć prognozę zajętości
        /// bloków per pociąg. Dłuższy horyzont = więcej przewidywania, ale droższy forecast.</summary>
        public const float DispatchForecastHorizonSec = 30f * 60f;

        /// <summary>Dolny clamp prędkości użytej w prognozie przejazdu przez blok [m/s].
        /// Chroni przed dzieleniem przez ~0 gdy Vmax segmentu jest zerowe/uszkodzone.</summary>
        public const float DispatchMinForecastSpeedMps = 2f;

        /// <summary>Twardy limit predykcyjnego trzymania pociągu przed odjazdem [s], by przepuścić
        /// wyżej-ważonego rywala. Powyżej tego progu dispatcher woli wypuścić (anti-starvation).
        /// To tylko SUFIT bezpieczeństwa — realny tuning robi funkcja kosztu (waga×czas).
        /// 30 min, bo na części szlaków osobowy jedzie ~2× dłużej niż pośpieszny i opłacalne
        /// przepuszczenie może wymagać dłuższego postoju niż 5 min.</summary>
        public const float DispatchMaxExtraHoldSec = 30f * 60f;

        /// <summary>Faza 4c: ile jednostek wagi dispatchera = 1 poziom priorytetu IRJ. Skaluje
        /// surowy priorytet (1-10), by obłożenie pasażerami mogło dodać porównywalny wkład.</summary>
        public const int DispatchPriorityScale = 100;

        /// <summary>Faza 4c: maksymalny wkład obłożenia do wagi (clamp liczby pasażerów na pokładzie).
        /// 300 ≈ 3 poziomy IRJ — zapchany osobowy może przeważyć pusty pośpieszny. Tuning M-Balance.</summary>
        public const int DispatchMaxLoadWeight = 300;

        /// <summary>Faza 4b: mnożnik kosztu wypuszczenia w polityce „Punktualność" (>1 = trzymaj chętniej,
        /// by chronić punktualność wyżej-ważonych). 1.5 = release wygląda o 50% drożej. Tuning M-Balance.</summary>
        public const float DispatchPunctualityHoldBias = 1.5f;

        // ─────────────────────────────────────────────────────────
        //  Pasażerowie — cel podróży (M-PaxV2 Faza B)
        // ─────────────────────────────────────────────────────────

        /// <summary>Wagi celów [Commute, Business, Leisure, Tourism] w godzinach szczytu (pn-pt 6-9/13-16).
        /// Kolejność = TripPurpose enum. Szczyt = dominują dojeżdżający do pracy.</summary>
        public static readonly float[] PurposeWeightsRush    = { 0.70f, 0.15f, 0.10f, 0.05f };
        /// <summary>Wagi celów poza szczytem (dzień roboczy) — dominują biznes/wypoczynek.</summary>
        public static readonly float[] PurposeWeightsOffpeak = { 0.20f, 0.35f, 0.30f, 0.15f };
        /// <summary>Wagi celów w weekend — dominują wypoczynek/turystyka.</summary>
        public static readonly float[] PurposeWeightsWeekend = { 0.10f, 0.10f, 0.45f, 0.35f };

        /// <summary>Udział pasażerów wybierających 1. klasę wg celu (reszta → 2. klasa). Commute zawsze 2.kl.</summary>
        public const float PurposeFirstClassShareBusiness = 0.70f;
        public const float PurposeFirstClassShareLeisure  = 0.10f;
        public const float PurposeFirstClassShareTourism  = 0.15f;

        /// <summary>Bazowa skłonność do zapłaty [gr] wg celu (× jitter). Zastępuje losowy portfel.</summary>
        public const int PurposeWillingnessCommute  = 5000;   // 50 zł — regularny, cenowo wrażliwy
        public const int PurposeWillingnessBusiness = 16000;  // 160 zł — płaci za czas/komfort
        public const int PurposeWillingnessLeisure  = 8000;   // 80 zł
        public const int PurposeWillingnessTourism  = 11000;  // 110 zł

        /// <summary>Rozrzut skłonności do zapłaty (±frakcja) — jitter wokół bazy per pasażer.</summary>
        public const float PurposeWillingnessJitter = 0.25f;

        // ─────────────────────────────────────────────────────────
        //  Aglomeracja
        // ─────────────────────────────────────────────────────────

        /// <summary>Minimalna liczba przystanków o nazwie zawierającej nazwę miasta,
        /// żeby uznać to miasto za aglomeracyjne.</summary>
        public const int AgglomerationMinStationCount = 2;

        // ─────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────

        /// <summary>Czy podana godzina (0..23) mieści się w oknie nocnym [NightStartHour, NightEndHour).</summary>
        public static bool IsNightHour(int hour)
        {
            if (hour < 0 || hour > 23) return false;
            return hour >= NightStartHour || hour < NightEndHour;
        }

        /// <summary>Zwraca minimalny czas postoju reverse dla danego trybu składu.</summary>
        public static int GetReverseStopSec(CompositionMode mode)
            => mode == CompositionMode.MultipleUnit
                ? ReverseStopSecondsMultipleUnit
                : ReverseStopSecondsLocoWithCars;
    }
}
