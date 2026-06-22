using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1 / D6: Pojedynczy override w indywidualnym harmonogramie.
    /// Zakres dat [dateIsoStart, dateIsoEnd] inclusive. Jednodniowy = start==end.
    ///
    /// <see cref="ScheduleOverrideType.ShiftSwap"/> wymaga ustawionego <see cref="replacementShift"/>
    /// + <see cref="hasReplacementShift"/> = true (enum non-nullable w Unity Serialization).
    ///
    /// Dla <see cref="ScheduleOverrideType.Training"/> pole <see cref="notes"/> zawiera trainingId (stub post-EA).
    /// </summary>
    [Serializable]
    public class ScheduleOverride
    {
        public string dateIsoStart;
        public string dateIsoEnd;
        public ScheduleOverrideType type;

        /// <summary>Dla ShiftSwap — nowa zmiana w tym okresie. Ignore gdy <see cref="hasReplacementShift"/>=false.</summary>
        public ShiftType replacementShift;
        public bool hasReplacementShift;

        /// <summary>Wolna notatka / dodatkowe info (np. trainingId, cel urlopu).</summary>
        public string notes;
    }
}
