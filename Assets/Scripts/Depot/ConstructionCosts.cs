using DepotSystem.OutdoorEquipment;

namespace DepotSystem
{
    /// <summary>
    /// M-Economy Faza 5: kalkulatory kosztów budowy [gr] — single source łączący geometrię
    /// (długość / powierzchnia / typ) z cennikiem (<see cref="ConstructionConstants"/>).
    ///
    /// Buildery wołają to dwa razy: przy stawianiu (charge przez <see cref="ConstructionBilling"/>)
    /// i przy usuwaniu/undo (refund — recompute tym samym wzorem → ta sama kwota, bez przechowywania
    /// kosztu na encji). Deterministyczne. Liczone w double dla precyzji przy dużych kwotach.
    /// </summary>
    public static class ConstructionCosts
    {
        /// <summary>Tor zajezdniowy [gr] wg długości.</summary>
        public static long TrackGroszy(float lengthM)
            => (long)(ConstructionConstants.TrackZajezdniaPerKmGroszy * System.Math.Max(0.0, lengthM) / 1000.0);

        /// <summary>Sieć trakcyjna [gr] wg długości.</summary>
        public static long CatenaryGroszy(float lengthM)
            => (long)(ConstructionConstants.CatenaryPerKmGroszy * System.Math.Max(0.0, lengthM) / 1000.0);

        /// <summary>Rozjazd [gr] wg typu (R190/R300/R500/R760/krzyżownica — string z enuma, defName
        /// lub SchemaTurnoutType). Domyślnie R190 (najtańszy/najczęstszy).</summary>
        public static long TurnoutGroszy(string turnoutTypeName)
        {
            string t = turnoutTypeName ?? "";
            // TD-035: krzyżownica PRZED checkami liczbowymi — "Krzyżowy R190" łapie "190".
            // Łapie defName ("Krzyżowy R190") i schema-string ("Crossover_R190").
            if (t.Contains("Krzyż") || t.Contains("Crossover")) return ConstructionConstants.KrzyzownicaPodwojnaGroszy;
            if (t.Contains("760")) return ConstructionConstants.RozjazdR760Groszy;
            if (t.Contains("500")) return ConstructionConstants.RozjazdR500Groszy;
            if (t.Contains("300")) return ConstructionConstants.RozjazdR300Groszy;
            return ConstructionConstants.RozjazdR190Groszy;
        }

        /// <summary>Pomieszczenie [gr] wg typu (stawka per m²) × powierzchnia.</summary>
        public static long RoomGroszy(RoomType type, float areaM2)
            => (long)(RoomPerSqMGroszy(type) * System.Math.Max(0.0, areaM2));

        static long RoomPerSqMGroszy(RoomType type) => type switch
        {
            RoomType.Hall              => ConstructionConstants.HalaP1P2PerSqMGroszy,
            RoomType.Office            => ConstructionConstants.BiuroPerSqMGroszy,
            RoomType.Dispatcher        => ConstructionConstants.BiuroPerSqMGroszy,
            RoomType.Supervisor        => ConstructionConstants.BiuroPerSqMGroszy,
            RoomType.TrafficController  => ConstructionConstants.BiuroPerSqMGroszy,
            RoomType.Social            => ConstructionConstants.PomieszczenieSocjalnePerSqMGroszy,
            RoomType.Bathroom          => ConstructionConstants.PomieszczenieSocjalnePerSqMGroszy,
            RoomType.Locker            => ConstructionConstants.PomieszczenieSocjalnePerSqMGroszy,
            RoomType.Storage           => ConstructionConstants.MagazynCzesciPerSqMGroszy,
            RoomType.Corridor          => ConstructionConstants.MagazynCzesciPerSqMGroszy,
            _                          => 0L, // None
        };

        /// <summary>Mebel [gr] — flat placeholder (M-Balance).</summary>
        public static long FurnitureGroszy() => ConstructionConstants.FurnitureItemPlaceholderGroszy;

        /// <summary>Infrastruktura outdoor [gr] wg typu.</summary>
        public static long OutdoorGroszy(OutdoorEquipmentType type) => type switch
        {
            OutdoorEquipmentType.WashZone     => ConstructionConstants.WashZoneOutdoorGroszy,
            OutdoorEquipmentType.Turntable    => ConstructionConstants.TurntableGroszy,
            OutdoorEquipmentType.PitLift      => ConstructionConstants.PitLiftGroszy,
            OutdoorEquipmentType.FuelStation  => ConstructionConstants.StacjaPaliwGroszy,
            OutdoorEquipmentType.WaterService => ConstructionConstants.WaterServiceGroszy,
            _                                 => 0L,
        };
    }
}
