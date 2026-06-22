namespace RailwayManager.SaveLoad
{
    /// <summary>
    /// Const'y dla SaveAsync(saveType:) i Manifest.SaveType — zamiast magic strings.
    ///
    /// Manifest.SaveType pozostaje string (JSON wire format, back-compat z save'ami
    /// na dysku zapisanymi przed dodaniem tego helpera). SaveLoadUI lokalizuje przez
    /// "save_load.type_" + saveType — typo w stringu = brakujący loc key, więc
    /// callsite'y MUSZĄ używać tych const'ów zamiast literałów.
    /// </summary>
    public static class SaveTypes
    {
        /// <summary>Manualny save z UI (SaveLoadUI "+ Nowy save").</summary>
        public const string Manual = "manual";

        /// <summary>Auto-save z timera (AutoSaveService.AutoSaveAsync).</summary>
        public const string Auto = "auto";

        /// <summary>Quick-save F5 (AutoSaveService.QuickSaveAsync).</summary>
        public const string Quick = "quick";

        /// <summary>Exit-save przy quit aplikacji (Application.wantsToQuit).</summary>
        public const string Exit = "exit";

        /// <summary>Save & Exit do Main Menu z PauseMenuUI.</summary>
        public const string ManualExitToMenu = "manual-exit-to-menu";

        /// <summary>Save & Quit aplikacji z PauseMenuUI.</summary>
        public const string ManualQuit = "manual-quit";

        /// <summary>Save z SaveLoadDiagnostics (developer test). Nie jest pokazywany w gameplay UI.</summary>
        public const string Diagnostic = "diagnostic";

        /// <summary>Save z SaveLoadSmokeTest (test jednostkowy). Nie jest pokazywany w gameplay UI.</summary>
        public const string Test = "test";
    }
}
