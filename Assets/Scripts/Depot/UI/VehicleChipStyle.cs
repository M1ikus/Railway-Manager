using UnityEngine;
using RailwayManager.Fleet;
using RailwayManager.SharedUI.Localization;

namespace DepotSystem
{
    /// <summary>
    /// TD-032: wspólne style chipów pojazdu — kolor wg typu + krótki label. DRY: wcześniej był
    /// duplikat private static w FleetPanelUI.Helpers; teraz współdzielone z ConsistPopupUI
    /// (picker rozprzęgania). FleetPanelUI.{GetThumbnailColor,GetTypeShortLabel} delegują tutaj.
    /// </summary>
    public static class VehicleChipStyle
    {
        // Type thumbnail colors (placeholder — przeniesione 1:1 z FleetPanelUI).
        static readonly Color ElLoco = new(0.20f, 0.50f, 0.85f, 1f);
        static readonly Color DiLoco = new(0.70f, 0.45f, 0.15f, 1f);
        static readonly Color Emu    = new(0.25f, 0.70f, 0.50f, 1f);
        static readonly Color Dmu    = new(0.60f, 0.55f, 0.20f, 1f);
        static readonly Color Car    = new(0.50f, 0.50f, 0.55f, 1f);

        public static Color ColorForType(FleetVehicleType type) => type switch
        {
            FleetVehicleType.ElectricLocomotive => ElLoco,
            FleetVehicleType.DieselLocomotive => DiLoco,
            FleetVehicleType.EMU => Emu,
            FleetVehicleType.DMU => Dmu,
            FleetVehicleType.PassengerCar => Car,
            _ => Car
        };

        public static string ShortLabel(FleetVehicleType type) => type switch
        {
            FleetVehicleType.ElectricLocomotive => LocalizationService.Get("fleet.vehicle_type.short.electric_loco"),
            FleetVehicleType.DieselLocomotive => LocalizationService.Get("fleet.vehicle_type.short.diesel_loco"),
            FleetVehicleType.EMU => LocalizationService.Get("fleet.vehicle_type.short.emu"),
            FleetVehicleType.DMU => LocalizationService.Get("fleet.vehicle_type.short.dmu"),
            FleetVehicleType.PassengerCar => LocalizationService.Get("fleet.vehicle_type.short.passenger_car"),
            _ => LocalizationService.Get("fleet.vehicle_type.short.unknown")
        };

        /// <summary>
        /// TD-032: chip dla pojazdu po id — etykieta (series gdy znane, inaczej krótki typ) + kolor typu.
        /// Fallback „#id" + szary gdy pojazd nie w OwnedVehicles (np. depot-only/test).
        /// </summary>
        public static (string label, Color color) ChipForVehicle(int vehicleId)
        {
            var owned = FleetService.OwnedVehicles;
            if (owned != null)
            {
                for (int i = 0; i < owned.Count; i++)
                {
                    var v = owned[i];
                    if (v != null && v.id == vehicleId)
                    {
                        string lbl = !string.IsNullOrWhiteSpace(v.series) ? v.series : ShortLabel(v.type);
                        return (lbl, ColorForType(v.type));
                    }
                }
            }
            return ($"#{vehicleId}", Car);
        }
    }
}
