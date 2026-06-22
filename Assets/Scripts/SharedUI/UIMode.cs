namespace RailwayManager.SharedUI
{
    /// <summary>
    /// M-TimetableUX F1.16/F1.17: Progressive disclosure UI mode.
    /// Per-save player preference + unlock conditions w <c>PlayerProgressService</c>.
    /// </summary>
    public enum UIMode
    {
        /// <summary>
        /// Default dla new player. Klik A → klik B → working timetable, conflicts auto-resolved
        /// + informational notifications. Defaults pre-filled but editable. Hint suggestions hidden.
        /// </summary>
        Basic,

        /// <summary>
        /// Opt-in toggle (unlock po N timetables created). Per-stop control + drag waypoints +
        /// conflict details + proactive suggestion prompts. Hint visible.
        /// </summary>
        Advanced,

        /// <summary>
        /// Opt-in dispatcher mode (unlock po N hours play OR tutorial completion).
        /// Multi-objective optimization + manual block reservation override + cascade visualization +
        /// diagnostic details w notifications.
        /// </summary>
        Expert
    }
}
