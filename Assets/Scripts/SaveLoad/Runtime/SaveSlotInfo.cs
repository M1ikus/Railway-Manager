namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// M13-6: DTO dla SaveLoadUI — meta-info o slot'cie bez load'owania całego bundle'a.
    ///
    /// LocalDiskStorage.ListAsync() wraca listę tych — wystarczy wczytać manifest
    /// (pierwszy file w gzip) zamiast całego bundle'a (~MB). Lista 100 save'ów ~100KB.
    /// </summary>
    public class SaveSlotInfo
    {
        /// <summary>Identyfikator slot'a — fragment nazwy pliku bez `.rmsave` extension.
        /// Np. "save_001", "autosave_003", "quicksave".</summary>
        public string SlotId { get; set; } = "";

        /// <summary>Pełna ścieżka do pliku na dysku (LocalDiskStorage). Null dla SteamCloud.</summary>
        public string FilePath { get; set; }

        /// <summary>Nazwa wyświetlana (z manifestu).</summary>
        public string SlotName { get; set; } = "";

        /// <summary>Typ save'a — patrz <see cref="SaveTypes"/> dla const'ów.</summary>
        public string SaveType { get; set; } = SaveTypes.Manual;

        /// <summary>Wersja gry która zapisała.</summary>
        public string GameVersion { get; set; } = "";

        /// <summary>Aktualna data w grze.</summary>
        public string GameTimeIso { get; set; } = "";

        /// <summary>Timestamp UTC zapisu — do sortowania.</summary>
        public string SavedAt { get; set; } = "";

        /// <summary>Łączny playtime w sekundach.</summary>
        public double Playtime { get; set; }

        /// <summary>Rozmiar pliku w bajtach (wyświetlane w SaveLoadUI).</summary>
        public long FileSizeBytes { get; set; }
    }
}
