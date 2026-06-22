using System;
using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// MM-10 / MM-D13/D17 — tryb wykonania modernizacji.
    /// </summary>
    public enum ModernizationMode
    {
        /// <summary>ZNTK (zewnętrzny zakład z <see cref="ExternalWorkshop.modernizationAvailable"/>=true).</summary>
        External,
        /// <summary>Własny warsztat — Hall lvl ≥ <see cref="ModernizationPath.minHallLevelInternal"/>
        /// (default 5 dla EN57/EU07, 3 dla SM42). MM-D13.</summary>
        Internal,
    }

    /// <summary>
    /// MM-10 — definicja ścieżki modernizacji (z <c>modernization_paths.json</c>).
    ///
    /// Workflow gameplay'owy:
    /// <list type="number">
    /// <item>Gracz kupuje wrak (np. EN57-1120 stara za 0.4M zł)</item>
    /// <item>Wybiera tryb modernizacji: External (ZNTK) lub Internal (własny warsztat lvl5)</item>
    /// <item>Schedule via <see cref="ModernizationJobService"/> → vehicle status OutOfService,
    /// slot zajęty (Internal) na <see cref="durationDays"/></item>
    /// <item>Po zakończeniu: vehicle.seriesId zmienia się na <see cref="targetSeriesId"/>,
    /// parametry techniczne updated (kopiowane z catalog target series LUB z inline overrides
    /// w tej ścieżce), history record, reputation bonus</item>
    /// </list>
    ///
    /// Real-world reference w spec'u (sekcja 3.4):
    /// <list type="bullet">
    /// <item>EN57 → Ryba: Pesa MM 6.75M / Newag 8.07M / FPS Cegielski 8.5M (~7M placeholder)</item>
    /// <item>EU07/EP07 → 160 km/h: PKP IC 2024 9.96M (~10M placeholder)</item>
    /// <item>SM42 → 6Dg/6Di: Newag 2014 77M / 20 szt = 3.85M placeholder</item>
    /// </list>
    /// </summary>
    [Serializable]
    public class ModernizationPath
    {
        public string pathId;            // np. "EN57_to_Ryba"
        public string sourceSeriesId;    // np. "EN57"
        public string targetSeriesId;    // np. "EN57_Ryba"
        public string displayName;       // "EN57 → Ryba (PESA modernizacja)"
        public string description;

        public int durationDays;          // 60 dla EN57→Ryba
        public long externalCostPln;      // ZNTK pełen koszt (7M)
        public long internalCostPln;      // Własny warsztat (oszczędność, 6M)

        /// <summary>Min Hall lvl wymagany dla Internal mode (5 dla EN57/EU07, 3 dla SM42).</summary>
        public int minHallLevelInternal = 5;

        /// <summary>
        /// Min ServicePit max length w Hall (Internal mode). EN57=65m → wymaga pit_large
        /// (35m) + jakiś łącznik (uproszczenie EA: walidujemy że min jeden pit_large w pokoju).
        /// </summary>
        public float minServicePitLength;

        // ── Apply effect: parametry pojazdu po modernizacji (inline overrides) ──
        // Alternatywa: lookup z FleetCatalog target series. W MVP używamy inline żeby
        // nie wymuszać nowych entries w katalogu modeli.
        public string targetDisplaySeries;     // np. "EN57AKM" lub "EN57 Ryba"
        public int newMaxSpeedKmh;
        public int newPowerKw;
        public int newComfortClass = 3;
        public int newReliabilityScore = 85;
        public string[] newComfortFeatures;    // klima, Wi-Fi etc.
    }

    /// <summary>MM-10: aktywny job modernizacji. MM-18: state machine dla Internal mode.</summary>
    [Serializable]
    public class ModernizationJob : IServiceJobWithMovement
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
        public string pathId;
        public ModernizationMode mode;

        /// <summary>External: ExternalWorkshop.id. Internal: -1 (slot zajęty per ServicePit).</summary>
        public string externalWorkshopId;
        /// <summary>Internal: WorkshopSlot.servicePitInstanceId. External: -1.</summary>
        public int internalServicePitInstanceId = -1;

        public long startedGameTime;
        public long completionGameTime;
        public long costPlnTotal;

        // ── MM-18: state machine (relevant only for Internal mode) ────────

        /// <summary>MM-18: faza Internal jobu (Outbound/Servicing/Completed/Failed).
        /// External: zawsze Servicing (delivery to ZNTK = abstrakt, brak depot movement).</summary>
        public ServiceJobState state = ServiceJobState.Servicing;

        /// <summary>MM-18 Internal: tor docelowy (resolved przez AccessTrackResolver dla ServicePit).</summary>
        public int targetTrackId = -1;
        /// <summary>MM-18 Internal: tor pochodzenia.</summary>
        public int originTrackId = -1;
        /// <summary>MM-18 Internal: consistId DepotMoveTask'a w fazie EnRoute.</summary>
        public int consistId = -1;
        /// <summary>MM-18 Internal: pełny duration (sec game time, recompute po dotarciu).</summary>
        public long durationSec;
        /// <summary>MM-18 Internal: kiedy faza Servicing wystartowała.</summary>
        public long arrivedAtTargetGameTime;
    }

    /// <summary>
    /// MM-18: generic state machine dla wszystkich service jobs (Modernization/VehicleModification/
    /// SelfPaint). Analog <see cref="OutdoorJobState"/>.
    /// </summary>
    public enum ServiceJobState
    {
        /// <summary>Pojazd jedzie do stanowiska.</summary>
        EnRoute,
        /// <summary>Pojazd na stanowisku, count-down do completion.</summary>
        Servicing,
        /// <summary>Effect applied, pojazd zostaje na stanowisku.</summary>
        Completed,
        /// <summary>Job się wywalił — refund + reset.</summary>
        Failed,
    }

    /// <summary>
    /// A1 partial: wspólny kontrakt service jobów z movement state machine.
    /// Pozwala helperom (np. <c>ServiceJobHelpers.MarkPendingMovementRecovery</c>)
    /// operować generycznie. PaintingJob (ZNTK external) NIE implementuje —
    /// nie ma fazy EnRoute w grze.
    /// </summary>
    public interface IServiceJobWithMovement
    {
        int JobVehicleId { get; }
        long JobStartedGameTime { get; }
        long JobCompletionGameTime { get; }
        long JobArrivedAtTargetGameTime { get; set; }
        ServiceJobState JobState { get; set; }
    }
}
