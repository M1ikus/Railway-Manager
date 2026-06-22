using System;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// BUG-060 v2: morale pracownika rozdzielony na 4 bucket'y per source.
    ///
    /// Każdy bucket ma własny cap (sumują się do max 100):
    /// - <see cref="salaryContrib"/> 0-35 — pensja vs rynek (+ external events: bonus/missed)
    /// - <see cref="fatigueContrib"/> 0-25 — fatigue level (25 gdy świeży, 0 gdy wycieńczony)
    /// - <see cref="overtimeContrib"/> 0-25 — overtime/Night shift (25 gdy normalna zmiana)
    /// - <see cref="roomContrib"/> 0-15 — Supervisor + Social + Bathroom upgrades + external events
    ///
    /// Eliminuje BUG-060: room bonus nigdy nie znika "cicho" przez global clamp 100.
    /// Każdy bucket cap'd osobno, więc inwestycja w lvl 5 Supervisor zawsze ma value
    /// (max +15 osiągalne niezależnie od salary/fatigue stanu).
    ///
    /// External events (bonus, missed payment, hotel reject, fired colleague) modyfikują
    /// odpowiedni bucket przez <see cref="ApplyDeltaToSalary"/> / <see cref="ApplyDeltaToRoom"/> —
    /// trim do cap'a per-bucket eliminuje "ciche znikanie" jak w BUG-060.
    /// </summary>
    [Serializable]
    public class MoraleBreakdown
    {
        public int salaryContrib;
        public int fatigueContrib;
        public int overtimeContrib;
        public int roomContrib;

        /// <summary>Sum 4 bucket'ów — to jest "publiczne" morale 0-100.</summary>
        public int Total => salaryContrib + fatigueContrib + overtimeContrib + roomContrib;

        /// <summary>
        /// Aplikuje delta do salary bucket'u z trim do [0, MoraleSalaryCapMax].
        /// Używane przez external economic events: bonus payment, missed salary, raise/cut.
        /// </summary>
        public void ApplyDeltaToSalary(int delta)
        {
            salaryContrib = Clamp(salaryContrib + delta, 0, PersonnelBalanceConstants.MoraleSalaryCapMax);
        }

        /// <summary>
        /// Aplikuje delta do room bucket'u z trim do [0, MoraleRoomCapMax].
        /// Używane przez external social events: hotel reject, fired colleague.
        /// </summary>
        public void ApplyDeltaToRoom(int delta)
        {
            roomContrib = Clamp(roomContrib + delta, 0, PersonnelBalanceConstants.MoraleRoomCapMax);
        }

        /// <summary>Set salary bucket bezpośrednio z trim. Używane przez daily tick z target value.</summary>
        public void SetSalary(int value) => salaryContrib = Clamp(value, 0, PersonnelBalanceConstants.MoraleSalaryCapMax);
        public void SetFatigue(int value) => fatigueContrib = Clamp(value, 0, PersonnelBalanceConstants.MoraleFatigueCapMax);
        public void SetOvertime(int value) => overtimeContrib = Clamp(value, 0, PersonnelBalanceConstants.MoraleOvertimeCapMax);
        public void SetRoom(int value) => roomContrib = Clamp(value, 0, PersonnelBalanceConstants.MoraleRoomCapMax);

        /// <summary>
        /// Inicjalizacja z legacy `currentMorale` int (range 0-100). Migration v3→v4.
        /// Proportional split — daje 70% to salary (default neutral), reszta proporcjonalnie.
        /// Loose interpretation, ale lepsza niż 0 dla wszystkich buckets.
        /// </summary>
        public static MoraleBreakdown FromLegacyMorale(int legacyMorale)
        {
            int clamped = Clamp(legacyMorale, 0, 100);
            // Default split: salary 35%, fatigue 25%, overtime 25%, room 15% — analogiczny do caps
            float ratio = clamped / 100f;
            return new MoraleBreakdown
            {
                salaryContrib   = Clamp((int)(PersonnelBalanceConstants.MoraleSalaryCapMax * ratio), 0, PersonnelBalanceConstants.MoraleSalaryCapMax),
                fatigueContrib  = Clamp((int)(PersonnelBalanceConstants.MoraleFatigueCapMax * ratio), 0, PersonnelBalanceConstants.MoraleFatigueCapMax),
                overtimeContrib = Clamp((int)(PersonnelBalanceConstants.MoraleOvertimeCapMax * ratio), 0, PersonnelBalanceConstants.MoraleOvertimeCapMax),
                roomContrib     = Clamp((int)(PersonnelBalanceConstants.MoraleRoomCapMax * ratio), 0, PersonnelBalanceConstants.MoraleRoomCapMax)
            };
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
