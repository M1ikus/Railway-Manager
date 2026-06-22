using Newtonsoft.Json.Linq;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-6: Kontrakt dla modułu uczestniczącego w save/load.
    ///
    /// Każdy moduł (Fleet/Timetable/Personnel/...) implementuje to + rejestruje się
    /// w <see cref="SaveRegistry"/> w bootstrapie. <see cref="SaveOrchestrator"/>
    /// iteruje po wszystkich zarejestrowanych i serializuje per moduł osobno.
    ///
    /// JObject jako wymiana — pozwala migrator chain operować na surowym JSON
    /// (dodać/usunąć/przemianować pole) zanim trafi do POCO deserialize'a.
    ///
    /// Per-module schemaVersion + module migrator chain — patrz
    /// <see cref="MigrationRunner"/> (M13-12).
    /// </summary>
    public interface ISavable
    {
        /// <summary>Stabilny string identyfikator modułu (np. "fleet", "timetable").
        /// MUSI być unikalny w całej aplikacji. Używany jako klucz w bundle + manifest.</summary>
        string ModuleId { get; }

        /// <summary>Aktualna schema version dla tego modułu. Bumpowane na breaking change.
        /// Save z niższą wersją przejdzie przez migrator chain przed Deserialize.</summary>
        int SchemaVersion { get; }

        /// <summary>Zbiera state modułu jako JObject. Wywoływane przez SaveOrchestrator
        /// dla każdego SaveAsync.</summary>
        JObject Serialize();

        /// <summary>Restore'uje state modułu z JObject. Wywoływane przez SaveOrchestrator
        /// po VerifyHmac + Migrate. <paramref name="sourceVersion"/> jest aktualną
        /// schemaVersion (po migracji = SchemaVersion).
        /// Throw exception jeśli data invalid → orchestrator wywoła
        /// <see cref="InitializeDefault"/> (graceful degradation).</summary>
        void Deserialize(JObject data, int sourceVersion);

        /// <summary>Wywoływane gdy save dla tego modułu jest uszkodzony / brakujący.
        /// Powinno zresetować moduł do empty state (jak nowa gra).
        /// Reszta save'a kontynuuje load (per-module isolation).</summary>
        void InitializeDefault();
    }
}
