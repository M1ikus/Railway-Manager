using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-3 / D7: Platne ogloszenie rekrutacyjne. Po utworzeniu zwieksza weight danej roli
    /// w <see cref="Runtime.PersonnelMarketGenerator.RefreshPool"/> przez 14 dni (D7).
    ///
    /// Koszt: base + per-star multiplier = 3k zl (1★) do ~7k zl (5★ specjalista).
    /// Max 3 aktywne ogloszenia (<see cref="PersonnelBalanceConstants.JobPostingMaxActive"/>).
    /// </summary>
    [Serializable]
    public class JobPosting
    {
        public int jobPostingId;
        public EmployeeRole role;
        /// <summary>Minimalny skill szukany. Wplyw na koszt i weight dystrybucji.</summary>
        public int skillTarget = 1;
        public int costGroszy;
        /// <summary>Ile dni gry zostalo (inkrement -1 per OnDayEnded).</summary>
        public int daysRemaining;
        public string createdDateIso;
    }
}
