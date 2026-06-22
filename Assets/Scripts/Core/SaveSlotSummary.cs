namespace RailwayManager.Core
{
    /// <summary>
    /// 2026-05-10 — DTO dla hook boundary między SaveLoad asmdef a niższymi warstwami
    /// (MainMenu/GameCreator), żeby uniknąć cyclic dependency.
    ///
    /// Parallel do <c>RailwayManager.SaveLoad.SaveSlotInfo</c> (full DTO w SaveLoad asmdef).
    /// Mapping w <c>AutoSaveService.InstallSaveActionsHooks</c>.
    ///
    /// Layering: SaveLoad to najwyższa warstwa, NIE może być referowana przez MainMenu
    /// (Depot → MainMenu existing, więc MainMenu → SaveLoad → Depot → MainMenu = cycle).
    /// Hook pattern przez statyczne delegaty w Core eliminuje cycle.
    /// </summary>
    public class SaveSlotSummary
    {
        public string SlotId { get; set; } = "";
        public string SlotName { get; set; } = "";
        public string SaveType { get; set; } = "manual";  // "manual" | "auto" | "quick" | "exit"
        public string GameVersion { get; set; } = "";
        public string GameTimeIso { get; set; } = "";
        public string SavedAt { get; set; } = "";   // UTC ISO
        public double Playtime { get; set; }        // seconds
        public long FileSizeBytes { get; set; }
    }

    /// <summary>
    /// 2026-05-10 — enum dla hook boundary, parallel do <c>RailwayManager.SaveLoad.LoadStatus</c>.
    /// </summary>
    public enum LoadOutcome
    {
        Success,
        PartialLoad,    // niektóre moduły failed, reszta OK
        NotFound,       // slot nie istnieje
        ModifiedSave,   // HMAC mismatch
        NewerVersion,   // save z nowszej wersji
        Failed          // hard failure
    }
}
