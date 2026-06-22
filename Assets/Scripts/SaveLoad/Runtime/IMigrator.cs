using Newtonsoft.Json.Linq;

namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-12: Migrator dla pojedynczego skoku wersji modułu.
    ///
    /// Każdy migrator obsługuje DOKŁADNIE JEDEN skok (np. v1→v2, v2→v3).
    /// Chain wielu skoków robi <see cref="MigrationRunner"/> przez iterację
    /// kolejnych migratorów z chain'a per modułu.
    ///
    /// Implementacja: dodaj klasę <c>FleetMigrator_v1_v2 : IMigrator</c> z
    /// ModuleId="fleet", SourceVersion=1, TargetVersion=2 i logiką w Migrate.
    /// MigrationRunner auto-discoveruje przez reflection (Type.GetTypes Assembly).
    ///
    /// Idempotent: Migrate musi być deterministyczne (ten sam input → ten sam output).
    /// </summary>
    public interface IMigrator
    {
        /// <summary>ID modułu (musi pasować do ISavable.ModuleId).</summary>
        string ModuleId { get; }

        /// <summary>Z wersji X migracja zaczyna.</summary>
        int SourceVersion { get; }

        /// <summary>Do wersji Y migracja kończy. Zwykle Source+1, ale można wymusić skok więcej.</summary>
        int TargetVersion { get; }

        /// <summary>Wykonuje migrację. Modyfikuje JObject in-place lub wraca nową instancję
        /// (zwracana wartość jest używana). Może rzucić wyjątek jeśli dane są niezgodne
        /// z założeniami SourceVersion (np. brak wymaganego pola) — wtedy MigrationRunner
        /// rzuci dalej i SaveOrchestrator wywoła InitializeDefault dla modułu.</summary>
        JObject Migrate(JObject input);
    }
}
