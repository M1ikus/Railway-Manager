using System;
using System.Collections.Generic;
using RailwayManager.Fleet;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Założone parametry taboru dla rozkładu. Zawsze wypełnione (w trybie Symbolic i Concrete).
    /// W trybie Concrete dodatkowo zawiera listę ID konkretnych pojazdów z FleetService.OwnedVehicles.
    /// </summary>
    [Serializable]
    public class PlannedComposition
    {
        public CompositionAssignment assignment = CompositionAssignment.Symbolic;
        public CompositionMode mode = CompositionMode.MultipleUnit;

        /// <summary>Symboliczny zapis — np. "3B+WR+2A" (3 wagony 2kl + restauracyjny + 2 wagony 1kl).</summary>
        public string symbolicNotation;

        // ── Parametry liczbowe (zawsze wypełnione) ──
        public float totalLengthM;
        public float emptyMassTons;
        public float loadedMassTons;
        public float brakingMassTons;
        public int maxSpeedKmh;
        public BrakeRegime brakeRegime = BrakeRegime.R;

        /// <summary>Procent hamowania wpisany ręcznie przez gracza (bez wpływu na wyliczenia na M4).</summary>
        public float brakingPercent;

        // ── Tryb Concrete ──
        /// <summary>ID pojazdów z FleetService.OwnedVehicles lockowanych dla tego rozkładu (tylko Concrete).</summary>
        public List<int> assignedVehicleIds = new();
    }
}
