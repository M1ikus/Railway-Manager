namespace RailwayManager.Fleet
{
    /// <summary>
    /// M7-4 / MM-4: Poziom warsztatu w depocie — determinuje jakie przeglądy może wykonać.
    ///
    /// **MM-4 (2026-05-05) refactor:** stare 4-poziomowe (Basic/Medium/Major/MainOverhaul
    /// liczone z areaSqM) zastąpione 5-poziomowym systemem (Lvl1-Lvl5) wpiętym w
    /// M-Modernization room lvlowanie. Source of truth = <see cref="DepotSystem.DetectedRoom.level"/>,
    /// nie areaSqM. Mapowanie 1:1 — Hall lvl X → WorkshopLevel.LvlX.
    ///
    /// Hall lvl 5 (MM-D13) dodatkowo wykonuje modernizacje wewnętrzne (EN57→Ryba etc.) —
    /// to integracja w MM-10 (ModernizationJobService), tutaj tylko mapping P-poziomów:
    /// <list type="bullet">
    /// <item>Lvl1 → P1 only</item>
    /// <item>Lvl2 → P1-P2</item>
    /// <item>Lvl3 → P1-P3</item>
    /// <item>Lvl4 → P1-P4</item>
    /// <item>Lvl5 → P1-P5 + modernizacje</item>
    /// </list>
    /// </summary>
    public enum WorkshopLevel
    {
        None = 0,
        Lvl1 = 1,
        Lvl2 = 2,
        Lvl3 = 3,
        Lvl4 = 4,
        Lvl5 = 5,
    }

    public static class WorkshopLevelExtensions
    {
        /// <summary>
        /// MM-4: Mapping Hall lvl (z M-Modernization, 1-5) → WorkshopLevel enum.
        /// Source of truth: <see cref="DepotSystem.DetectedRoom.level"/>.
        /// hallLvl=0 (lub poza zakresem) → None.
        /// </summary>
        public static WorkshopLevel FromHallLevel(int hallLvl)
        {
            return hallLvl switch
            {
                1 => WorkshopLevel.Lvl1,
                2 => WorkshopLevel.Lvl2,
                3 => WorkshopLevel.Lvl3,
                4 => WorkshopLevel.Lvl4,
                5 => WorkshopLevel.Lvl5,
                _ => WorkshopLevel.None,
            };
        }

        /// <summary>Liczba slotów warsztatowych per hala na danym poziomie.</summary>
        public static int MaxSlots(this WorkshopLevel level) => level switch
        {
            WorkshopLevel.Lvl1 => 1,
            WorkshopLevel.Lvl2 => 2,
            WorkshopLevel.Lvl3 => 3,
            WorkshopLevel.Lvl4 => 4,
            WorkshopLevel.Lvl5 => 5,
            _ => 0,
        };

        /// <summary>
        /// Czy dany warsztat może wykonać dany poziom przeglądu.
        /// MM-4: spec 2.2 mapping (Hall lvl 1 = P1 only, Hall lvl 5 = P1-P5).
        /// </summary>
        public static bool CanPerform(this WorkshopLevel level, InspectionLevel insp) => level switch
        {
            WorkshopLevel.Lvl1 => insp == InspectionLevel.P1,
            WorkshopLevel.Lvl2 => insp <= InspectionLevel.P2,
            WorkshopLevel.Lvl3 => insp <= InspectionLevel.P3,
            WorkshopLevel.Lvl4 => insp <= InspectionLevel.P4,
            WorkshopLevel.Lvl5 => true, // P1-P5
            _ => false,
        };

        /// <summary>MM-D13: czy poziom obsługuje modernizacje wewnętrzne (Hall lvl5).</summary>
        public static bool CanPerformModernization(this WorkshopLevel level)
            => level == WorkshopLevel.Lvl5;

        public static string DisplayName(this WorkshopLevel level) => level switch
        {
            WorkshopLevel.Lvl1 => "Warsztat poziom 1 (P1)",
            WorkshopLevel.Lvl2 => "Warsztat poziom 2 (P1-P2)",
            WorkshopLevel.Lvl3 => "Warsztat poziom 3 (P1-P3)",
            WorkshopLevel.Lvl4 => "Warsztat poziom 4 (P1-P4)",
            WorkshopLevel.Lvl5 => "Warsztat poziom 5 (P1-P5 + modernizacje)",
            _ => "(brak warsztatu)",
        };

        /// <summary>
        /// Numeryczna wartość poziomu (1-5) lub 0 dla None. Wygodne do display
        /// "Warsztat lvl X" w UI.
        /// </summary>
        public static int ToInt(this WorkshopLevel level) => (int)level;
    }
}
