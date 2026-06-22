namespace RailwayManager.Core.Difficulty
{
    /// <summary>
    /// M13-13 / D35: Katalog 4 hardcoded presetów (+ Custom).
    /// Wartości placeholder — finalny tuning w M-Balance po cross-system playtestach.
    ///
    /// Użycie:
    /// <code>
    /// var modifiers = DifficultyPresetCatalog.Get(DifficultyPreset.Hard);
    /// // gracz może tweakować Custom przez Clone() i zmianę pól
    /// </code>
    /// </summary>
    public static class DifficultyPresetCatalog
    {
        /// <summary>Wraca <see cref="DifficultyModifiers"/> dla podanego presetu.
        /// Custom wraca neutralne (1.0) — gracz dostosuje sliderami w UI.</summary>
        public static DifficultyModifiers Get(DifficultyPreset preset)
        {
            return preset switch
            {
                DifficultyPreset.Easy => new DifficultyModifiers
                {
                    StartBudgetMultiplier            = 2.0f,    // 2x kasy startowej
                    OperationalCostMultiplier        = 0.7f,    // -30% kosztów operacyjnych
                    BreakdownChanceMultiplier        = 0.5f,    // o połowę mniej awarii
                    PassengerDemandMultiplier        = 1.3f,    // 30% więcej pasażerów
                    SalaryMultiplier                 = 0.8f,    // -20% pensje
                    SubsidyMultiplier                = 1.5f,    // 50% więcej dotacji
                    DelayPropagationMultiplier       = 0.5f,    // opóźnienia mniej propagują
                    EventFrequencyMultiplier         = 0.5f,    // mniej eventów
                    HotelCostMultiplier              = 0.7f,    // tańsze hotele
                    TicketPriceToleranceMultiplier   = 1.2f     // pasażerowie tolerują wyższe ceny
                },

                DifficultyPreset.Normal => new DifficultyModifiers(), // wszystko 1.0

                DifficultyPreset.Hard => new DifficultyModifiers
                {
                    StartBudgetMultiplier            = 0.7f,    // -30% kasy startowej
                    OperationalCostMultiplier        = 1.3f,    // +30% kosztów
                    BreakdownChanceMultiplier        = 1.5f,    // 50% więcej awarii
                    PassengerDemandMultiplier        = 0.8f,    // -20% pasażerów
                    SalaryMultiplier                 = 1.2f,    // +20% pensje
                    SubsidyMultiplier                = 0.7f,    // -30% dotacji
                    DelayPropagationMultiplier       = 1.5f,    // opóźnienia kaskadują mocniej
                    EventFrequencyMultiplier         = 1.5f,    // więcej eventów
                    HotelCostMultiplier              = 1.3f,    // droższe hotele
                    TicketPriceToleranceMultiplier   = 0.8f     // pasażerowie wrażliwi na ceny
                },

                DifficultyPreset.Realistic => new DifficultyModifiers
                {
                    StartBudgetMultiplier            = 0.5f,    // mocno ograniczona kasa startowa
                    OperationalCostMultiplier        = 1.5f,    // realistyczne ceny paliwa/energii
                    BreakdownChanceMultiplier        = 2.0f,    // taborowi prawie 30+ lat awariuje często
                    PassengerDemandMultiplier        = 0.7f,    // pasażerowie wybredni
                    SalaryMultiplier                 = 1.5f,    // realne pensje brutto
                    SubsidyMultiplier                = 0.5f,    // dotacje wymagają walki o przetargi
                    DelayPropagationMultiplier       = 2.0f,    // realna kaskada opóźnień
                    EventFrequencyMultiplier         = 2.0f,    // strajki, awarie sieci, pogoda
                    HotelCostMultiplier              = 1.5f,    // droższe noclegi
                    TicketPriceToleranceMultiplier   = 0.7f     // pasażerowie odejdą jak za drogo
                },

                _ => new DifficultyModifiers() // Custom + nieznane → neutralne, gracz tweakuje
            };
        }

        /// <summary>Lokalizowana nazwa presetu (i18n key). UI tłumaczy przez LocalizationService.Get.</summary>
        public static string GetLocalizationKey(DifficultyPreset preset)
        {
            return preset switch
            {
                DifficultyPreset.Easy      => "difficulty.preset.easy",
                DifficultyPreset.Normal    => "difficulty.preset.normal",
                DifficultyPreset.Hard      => "difficulty.preset.hard",
                DifficultyPreset.Realistic => "difficulty.preset.realistic",
                DifficultyPreset.Custom    => "difficulty.preset.custom",
                _                          => "difficulty.preset.normal"
            };
        }
    }
}
