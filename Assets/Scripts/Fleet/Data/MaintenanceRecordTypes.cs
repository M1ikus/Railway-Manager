namespace RailwayManager.Fleet
{
    /// <summary>
    /// D2: stałe + helpery dla <see cref="MaintenanceRecord.recordType"/>.
    ///
    /// Wcześniej recordType był wpisywany jako string literal w 11 miejscach
    /// (FleetService/CartProcessor/PaintingJobService/SelfPaintingService/
    /// ModernizationJobService/VehicleModificationJobService/WorkshopManager +
    /// 5 typów z OutdoorEquipmentJobService.TypeLabel). Literówki przy ewentualnym
    /// filtrowaniu w UI wpadały cicho — teraz central source of truth.
    ///
    /// Dynamic kombinacje (Przegląd P{level} (ZNTK/własny warsztat),
    /// Modernizacja {displayName}, Modyfikacja {displayName}) jako helper methods.
    /// </summary>
    public static class MaintenanceRecordTypes
    {
        // ── Zakup ─────────────────────────────────────
        public const string Purchase                = "Zakup";
        public const string PurchaseNew             = "Zakup (nowy)";
        public const string PurchaseUsed            = "Zakup (uzywany)";
        public const string PurchaseNewConfigurable = "Zakup (nowy konfigurowalny)";

        // ── Malowanie ─────────────────────────────────
        public const string PaintZntk = "Malowanie (ZNTK)";
        public const string PaintSelf = "Malowanie (własny warsztat)";

        // ── Outdoor equipment (MM-9/MM-17) ────────────
        public const string Wash         = "Mycie pojazdu";
        public const string Refuel       = "Tankowanie";
        public const string WaterService = "Wodowanie";
        public const string Rotate       = "Obrót na obrotnicy";
        public const string PitLiftMaint = "Quick maint (PitLift)";

        // ── Dynamic combos ────────────────────────────
        public static string Inspection(InspectionLevel level, bool external)
            => $"Przegląd {level} ({(external ? "ZNTK" : "własny warsztat")})";

        public static string Modernization(string displayName)
            => $"Modernizacja {displayName}";

        public static string Modification(string displayName)
            => $"Modyfikacja {displayName}";
    }
}
