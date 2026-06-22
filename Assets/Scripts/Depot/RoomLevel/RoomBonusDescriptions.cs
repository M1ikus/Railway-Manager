namespace DepotSystem.RoomLevel
{
    /// <summary>
    /// MM-3 — human-readable opisy bonusów per (RoomType, lvl) dla popup'u.
    ///
    /// Bonusy są zdefiniowane gameplay'owo w spec'u sekcja 2.2 — tu są tylko
    /// stringi do UI display. Faktyczne wpięcie hooków konsumentów (OfficeService cap,
    /// WorkshopManager P-poziom, itp.) → MM-4..7.
    ///
    /// Koszt awansu (MM-D10: bez downtime, mały koszt) — placeholder kwoty,
    /// do dopracowania w M-Balance. EA: pokazujemy "Koszt: X zł" ale TryUpgrade
    /// nie pobiera kasy w MM-3 (integracja EconomyManager w MM-Balance).
    /// </summary>
    public static class RoomBonusDescriptions
    {
        /// <summary>Czytelny opis aktualnego bonusu pokoju o danym lvlu (1-5).</summary>
        public static string GetCurrentBonus(RoomType roomType, int lvl)
        {
            if (lvl < 1 || lvl > RoomLevelCatalog.MaxLevel) return "—";
            return roomType switch
            {
                RoomType.Hall => lvl switch
                {
                    1 => "P1 inspection",
                    2 => "P1-P2 inspection",
                    3 => "P1-P3 inspection",
                    4 => "P1-P4 inspection",
                    5 => "P1-P5 inspection + modernizacje wewnętrzne (EN57→Ryba, EU07→160, SM42→6Dg)",
                    _ => "—"
                },
                RoomType.Office => lvl switch
                {
                    1 => "Max 2 biurowych, R&D speed ×1.0",
                    2 => "Max 4 biurowych, R&D speed ×1.2",
                    3 => "Max 6 biurowych, R&D speed ×1.5",
                    4 => "Max 8 biurowych, R&D speed ×1.8",
                    5 => "Max 12 biurowych, R&D speed ×2.2",
                    _ => "—"
                },
                RoomType.Dispatcher => lvl switch
                {
                    1 => "Max 1 dyspozytor, odprawa zmiany 2.0× czas",
                    2 => "Max 2 dyspozytorów, odprawa 1.5× czas",
                    3 => "Max 3 dyspozytorów, odprawa 1.0× (baseline)",
                    4 => "Max 4 dyspozytorów, odprawa 0.7×",
                    5 => "Max 5 dyspozytorów, odprawa 0.5×",
                    _ => "—"
                },
                RoomType.TrafficController => lvl switch
                {
                    1 => "Max 1 dyżurny (akcje per dyżurny: 1+skill)",
                    2 => "Max 2 dyżurnych (akcje sumują się)",
                    3 => "Max 3 dyżurnych",
                    4 => "Max 4 dyżurnych",
                    5 => "Max 5 dyżurnych",
                    _ => "—"
                },
                RoomType.Supervisor => lvl switch
                {
                    1 => "Globalny morale +1 dla wszystkich w zajezdni",
                    2 => "Globalny morale +2",
                    3 => "Globalny morale +3",
                    4 => "Globalny morale +4",
                    5 => "Globalny morale +5",
                    _ => "—"
                },
                RoomType.Social => lvl switch
                {
                    1 => "Morale +1 dla pracowników z dojściem",
                    2 => "Morale +2 dla pracowników z dojściem",
                    3 => "Morale +3 dla pracowników z dojściem",
                    4 => "Morale +4 dla pracowników z dojściem",
                    5 => "Morale +5 dla pracowników z dojściem",
                    _ => "—"
                },
                RoomType.Bathroom => lvl switch
                {
                    1 => "Morale +0.5 dla pracowników z dojściem",
                    2 => "Morale +1 dla pracowników z dojściem",
                    3 => "Morale +1.5 dla pracowników z dojściem",
                    4 => "Morale +2 dla pracowników z dojściem",
                    5 => "Morale +2.5 dla pracowników z dojściem",
                    _ => "—"
                },
                _ => "—"
            };
        }

        /// <summary>
        /// Koszt awansu z lvl (currentLevel) do (currentLevel+1) w groszach.
        /// MM-D10 placeholder: mały koszt rosnący wykładniczo.
        /// </summary>
        public static long GetUpgradeCostGroszy(int currentLevel)
        {
            return currentLevel switch
            {
                1 => 500_000L,         //   5 000 zł (lvl1 → lvl2)
                2 => 2_000_000L,       //  20 000 zł (lvl2 → lvl3)
                3 => 10_000_000L,      // 100 000 zł (lvl3 → lvl4)
                4 => 50_000_000L,      // 500 000 zł (lvl4 → lvl5)
                _ => 0L,
            };
        }

        /// <summary>Display string kosztu awansu, np. "5 000 zł".</summary>
        public static string GetUpgradeCostLabel(int currentLevel)
        {
            long gr = GetUpgradeCostGroszy(currentLevel);
            if (gr <= 0) return "—";
            long zl = gr / 100L;
            return $"{zl:N0} zł";
        }

        /// <summary>
        /// Czytelna nazwa typu pokoju (po polsku) z RoomRequirements.MinSize.
        /// Fallback do enum.ToString() jeśli typ nieznany.
        /// </summary>
        public static string GetRoomTypeDisplayName(RoomType roomType)
        {
            if (RoomRequirements.MinSize.TryGetValue(roomType, out var entry))
                return entry.label;
            return roomType.ToString();
        }
    }
}
