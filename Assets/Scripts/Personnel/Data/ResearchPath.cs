using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-15 / §3.7: Sciezka R&amp;D — jeden badany "upgrade" z wymaganiami i efektem.
    /// Katalog w <see cref="ResearchPathCatalog"/>.
    ///
    /// Flow:
    /// - Draft → gracz wybiera, start
    /// - InProgress → daily tick dec daysRemaining (jesli requirements met: &gt;=N badaczy o &gt;=skillMin)
    /// - Completed → apply globalny <see cref="ResearchUnlocks"/> bonus
    /// - Interrupted → jesli requirements nie spelnione, progress stoi (daysRemaining nie dec)
    /// </summary>
    [Serializable]
    public class ResearchPath
    {
        public string pathId;
        public string displayName;
        public string description;

        public int durationDays;
        public int daysRemaining;

        /// <summary>
        /// MM-5: akumulator postępu w 1/10 dnia, używany przez R&D speed multiplier
        /// z Office lvl (<see cref="OfficeService.GetResearchSpeedMultiplier"/>). Daily tick
        /// dorzuca <c>round(speedMult × 10)</c> tenths; gdy accum ≥ 10, dec daysRemaining
        /// o <c>accum/10</c> i resetuje do <c>accum % 10</c>. Pozwala na fractional speed
        /// (1.5×) przy zachowaniu int daysRemaining (UI display nie zmienia się).
        /// Default 0 = brak progresu jeszcze.
        /// </summary>
        public int progressTenthsAccumulated;

        public int requiredResearchers;
        public int minSkill;

        public ResearchPathStatus status;

        /// <summary>Efekt odblokowany po completed (np. "MaintenanceTimeReduction", "TractionEnergyReduction").</summary>
        public string effectKey;
        /// <summary>Wartosc efektu (np. 0.10 = -10% czasu napraw).</summary>
        public float effectValue;

        public ResearchPath Clone() => new()
        {
            pathId = pathId,
            displayName = displayName,
            description = description,
            durationDays = durationDays,
            daysRemaining = durationDays,
            requiredResearchers = requiredResearchers,
            minSkill = minSkill,
            status = ResearchPathStatus.Available,
            effectKey = effectKey,
            effectValue = effectValue
        };
    }

    public enum ResearchPathStatus
    {
        Available,
        InProgress,
        Interrupted,
        Completed
    }
}
