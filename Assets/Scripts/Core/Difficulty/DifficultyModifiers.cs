using Newtonsoft.Json.Linq;

namespace RailwayManager.Core.Difficulty
{
    /// <summary>
    /// M13-13 / D35: POCO z 10 modyfikatorami trudności.
    /// Mnożniki domyślnie 1.0 = neutralne (Normal preset).
    ///
    /// Wartości > 1.0 → "więcej tego" (np. <see cref="StartBudgetMultiplier"/>=2.0 to dwa razy
    /// więcej kasy startowej). Wartości < 1.0 → "mniej tego".
    ///
    /// Sens każdego modifiera określa konkretny system (Economy / Maintenance / Personnel /
    /// Passengers). M13-13 dostarcza tylko POCO + serializację — interpretacja w runtime
    /// (np. "1.2x breakdown chance" w BreakdownService) → integracje per-system w M-Balance.
    /// </summary>
    public class DifficultyModifiers
    {
        /// <summary>Mnożnik kasy startowej (1.0 = bazowe 150k). Wartości typowe: 0.5-3.0.</summary>
        public float StartBudgetMultiplier = 1.0f;

        /// <summary>Mnożnik kosztów operacyjnych (paliwo, energia, opłaty trakcyjne). Niższy = łatwiej.</summary>
        public float OperationalCostMultiplier = 1.0f;

        /// <summary>Mnożnik prawdopodobieństwa awarii taboru (M7 BreakdownService). Niższy = mniej awarii.</summary>
        public float BreakdownChanceMultiplier = 1.0f;

        /// <summary>Mnożnik popytu pasażerskiego (M6 PassengerManager). Wyższy = więcej kasy z biletów.</summary>
        public float PassengerDemandMultiplier = 1.0f;

        /// <summary>Mnożnik pensji pracowników (M8 Personnel). Niższy = łatwiej.</summary>
        public float SalaryMultiplier = 1.0f;

        /// <summary>Mnożnik dotacji wojewódzkich (M6 SubsidyCalculator). Wyższy = łatwiej.</summary>
        public float SubsidyMultiplier = 1.0f;

        /// <summary>Mnożnik propagacji opóźnień (M9 TrainRunSimulator). Niższy = bardziej wybaczające opóźnienia.</summary>
        public float DelayPropagationMultiplier = 1.0f;

        /// <summary>Mnożnik częstotliwości losowych zdarzeń (M12d Random Events). Niższy = mniej eventów.</summary>
        public float EventFrequencyMultiplier = 1.0f;

        /// <summary>Mnożnik kosztów hoteli pracowników (M8 turnusy multi-day). Niższy = łatwiej.</summary>
        public float HotelCostMultiplier = 1.0f;

        /// <summary>Mnożnik tolerancji cenowej pasażerów (M6.5 ticket pricing). Wyższy = wyższe ceny biletów akceptowane.</summary>
        public float TicketPriceToleranceMultiplier = 1.0f;

        /// <summary>Tworzy płytką kopię (do edytora Custom — gracz tweakuje kopię, nie współdzielony obiekt).</summary>
        public DifficultyModifiers Clone()
        {
            return new DifficultyModifiers
            {
                StartBudgetMultiplier            = StartBudgetMultiplier,
                OperationalCostMultiplier        = OperationalCostMultiplier,
                BreakdownChanceMultiplier        = BreakdownChanceMultiplier,
                PassengerDemandMultiplier        = PassengerDemandMultiplier,
                SalaryMultiplier                 = SalaryMultiplier,
                SubsidyMultiplier                = SubsidyMultiplier,
                DelayPropagationMultiplier       = DelayPropagationMultiplier,
                EventFrequencyMultiplier         = EventFrequencyMultiplier,
                HotelCostMultiplier              = HotelCostMultiplier,
                TicketPriceToleranceMultiplier   = TicketPriceToleranceMultiplier
            };
        }

        /// <summary>Serializacja do JSON dla save bundle. Każde pole jako float, fallback do 1.0.</summary>
        public JObject ToJson()
        {
            return new JObject
            {
                ["startBudget"]            = StartBudgetMultiplier,
                ["operationalCost"]        = OperationalCostMultiplier,
                ["breakdownChance"]        = BreakdownChanceMultiplier,
                ["passengerDemand"]        = PassengerDemandMultiplier,
                ["salary"]                 = SalaryMultiplier,
                ["subsidy"]                = SubsidyMultiplier,
                ["delayPropagation"]       = DelayPropagationMultiplier,
                ["eventFrequency"]         = EventFrequencyMultiplier,
                ["hotelCost"]              = HotelCostMultiplier,
                ["ticketPriceTolerance"]   = TicketPriceToleranceMultiplier
            };
        }

        /// <summary>Deserializacja z JSON. Każde brakujące pole → 1.0 (Normal default).</summary>
        public static DifficultyModifiers FromJson(JObject json)
        {
            if (json == null) return new DifficultyModifiers();
            return new DifficultyModifiers
            {
                StartBudgetMultiplier            = json.Value<float?>("startBudget")            ?? 1f,
                OperationalCostMultiplier        = json.Value<float?>("operationalCost")        ?? 1f,
                BreakdownChanceMultiplier        = json.Value<float?>("breakdownChance")        ?? 1f,
                PassengerDemandMultiplier        = json.Value<float?>("passengerDemand")        ?? 1f,
                SalaryMultiplier                 = json.Value<float?>("salary")                 ?? 1f,
                SubsidyMultiplier                = json.Value<float?>("subsidy")                ?? 1f,
                DelayPropagationMultiplier       = json.Value<float?>("delayPropagation")       ?? 1f,
                EventFrequencyMultiplier         = json.Value<float?>("eventFrequency")         ?? 1f,
                HotelCostMultiplier              = json.Value<float?>("hotelCost")              ?? 1f,
                TicketPriceToleranceMultiplier   = json.Value<float?>("ticketPriceTolerance")   ?? 1f
            };
        }
    }
}
