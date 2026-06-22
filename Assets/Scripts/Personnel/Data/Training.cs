using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-1 / D11: POST-EA (M8.5) — placeholder data model dla szkolen.
    ///
    /// W EA pracownicy zatrudniani sa z rynku z gotowym skillem — brak runtime awansu.
    /// Pole <c>skillXp</c> na <see cref="Employee"/> i <c>isOnTraining</c> zostaja jako
    /// zero-cost stuby zeby nie blokowac save/load migration gdy M8.5 wejdzie.
    ///
    /// Gdy M8.5 aktywne:
    /// - External: czas 14-30 dni, 100% success
    /// - Internal: czas 20-45 dni, 80% success (wymaga mentora +2 skill)
    /// - Koszt 8k-60k zl w zaleznosci od target skill
    /// </summary>
    [Serializable]
    public class Training
    {
        public int trainingId;
        public int employeeId;
        public TrainingType type;
        public string startDateIso;
        public string endDateIso;
        public int targetSkillLevel;
        public int costGroszy;
        /// <summary>External = 1.0, Internal = 0.8 (D11).</summary>
        public float successChance;
    }
}
