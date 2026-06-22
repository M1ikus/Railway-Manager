using System;
using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// MM-11 / MM-D17 — typ modyfikacji posiadanego pojazdu (lighter niż modernizacja
    /// — nie zmienia seriesId, tylko parametry).
    /// </summary>
    public enum ModificationType
    {
        /// <summary>Wymiana wózków (klockowy → tarczowy → tarczowy-szynowy). +Vmax. Hall lvl 3+.</summary>
        BogieReplacement,
        /// <summary>Dodanie wyposażenia komfortowego (klima/Wi-Fi/gniazdka). Hall lvl 1+.</summary>
        ComfortAddition,
        /// <summary>Zmiana funkcji wagonu (interior mix swap). Hall lvl 3+. M-FC integration polish.</summary>
        BodyFunctionChange,
    }

    /// <summary>
    /// MM-11 — definicja modyfikacji (z <c>vehicle_modifications.json</c>).
    ///
    /// Inaczej niż <see cref="ModernizationPath"/>: nie zmienia seriesId pojazdu,
    /// tylko aktualizuje parametry techniczne / wyposażenie. Aplicable do każdego
    /// pojazdu spełniającego prerequisites (compatible type / current parametry).
    ///
    /// Reuse <see cref="VehicleConfiguration"/> z M-FC do tracking źródłowej konfiguracji
    /// (gdy gracz kupił z konfiguratora, modyfikacja pasuje do bodyTypeId/bogieTypeId).
    /// </summary>
    [Serializable]
    public class VehicleModification
    {
        public string modId;             // np. "bogie_disc_brake"
        public ModificationType type;
        public string displayName;       // "Wymiana na wózki tarczowe"
        public string description;

        public int durationDays;          // 14 dla bogie, 3 dla comfort, 21 dla function change
        public long externalCostPln;
        public long internalCostPln;
        public int minHallLevelInternal = 1;

        // Compatibility filters (które pojazdy mogą skorzystać):
        public string[] applicableVehicleTypes;  // np. ["EMU", "DMU", "PassengerCar"] — null = wszystkie
        public string[] requiresCurrentBogie;    // np. ["klockowy"] — current bogieTypeId musi być w liście
        public int minVehicleLengthM;
        public int maxVehicleLengthM = 999;

        // Effects (apply gdy job complete):
        public int newMaxSpeedKmh;        // 0 = bez zmian
        public string newBogieTypeId;     // np. "tarczowy" — tylko dla BogieReplacement
        public int comfortClassDelta;     // +1/+2 = bonus comfort class
        public string[] addComfortFeatures; // dorzuca do comfortFeatures

        // BodyFunctionChange (stub w MVP):
        public string newDefaultPurpose;  // "regional" / "longDistance" / "agglomeration"
    }

    /// <summary>MM-11: aktywny job modyfikacji. MM-18: state machine dla Internal mode.</summary>
    [Serializable]
    public class VehicleModificationJob : IServiceJobWithMovement
    {
        public int jobId;
        public int vehicleId;

        int IServiceJobWithMovement.JobVehicleId => vehicleId;
        long IServiceJobWithMovement.JobStartedGameTime => startedGameTime;
        long IServiceJobWithMovement.JobCompletionGameTime => completionGameTime;
        long IServiceJobWithMovement.JobArrivedAtTargetGameTime
        {
            get => arrivedAtTargetGameTime;
            set => arrivedAtTargetGameTime = value;
        }
        ServiceJobState IServiceJobWithMovement.JobState { get => state; set => state = value; }
        public string modId;
        public ModernizationMode mode;   // External / Internal (reuse enum z MM-10)

        public string externalWorkshopId;
        public int internalServicePitInstanceId = -1;

        public long startedGameTime;
        public long completionGameTime;
        public long costPlnTotal;

        // ── MM-18: state machine (Internal only) ─────────────────────
        public ServiceJobState state = ServiceJobState.Servicing;
        public int targetTrackId = -1;
        public int originTrackId = -1;
        public int consistId = -1;
        public long durationSec;
        public long arrivedAtTargetGameTime;
    }
}
