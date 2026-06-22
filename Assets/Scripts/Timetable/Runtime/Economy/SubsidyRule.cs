using System;
using System.Collections.Generic;

namespace RailwayManager.Timetable.Economy
{
    /// <summary>
    /// M6.5-6: DTO dla reguly dotacji per województwo.
    /// Loadowane z <c>Assets/StreamingAssets/Economy/subsidy_rules.json</c>.
    ///
    /// Stawki real-world calibrated do PSC contracts (Polregio Łódzkie reference 20.92 zł/pociągokm).
    /// Tier:
    /// - centralne (Mazowieckie): najniższe (800 zł/run)
    /// - metropolitalne (Małopolskie/Wielkopolskie/Pomorskie/Śląskie/Dolnośląskie): średnie (1200 zł/run)
    /// - srednie (Łódzkie/KP/ZP/Opolskie): wyższe (1800 zł/run)
    /// - peryferyjne (Podkarpackie/Świętokrzyskie/Lubelskie/Podlaskie/W-M/Lubuskie): najwyższe (2500 zł/run)
    /// </summary>
    [Serializable]
    public class SubsidyRule
    {
        public string code;          // "MZ", "MA", ...
        public string displayName;
        public string tier;          // centralne / metropolitalne / srednie / peryferyjne
        public int subsidyPerRunGroszy;
    }

    [Serializable]
    public class SubsidyGlobalDefaults
    {
        public int minRunsPerDayForFullSubsidy = 4;
        public int maxAvgTicketPriceGroszy = 4000;
        public float punctualityKpiThreshold = 0.80f;
    }

    [Serializable]
    public class SubsidyRulesData
    {
        public SubsidyGlobalDefaults globalDefaults;
        public List<SubsidyRule> voivodeships;
    }
}
