using System.Collections.Generic;
using System.Linq;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using RailwayManager.SharedUI.Localization;
using TMPro;
using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// Partial FleetPanelUI - wspolne helpery, formatowanie i proste akcje odswiezania.
    /// </summary>
    public partial class FleetPanelUI
    {
        private void UpdateCounter()
        {
            if (_counterLbl == null || _vehicles == null) return;

            int total = _vehicles.Count;
            int locos = _vehicles.Count(v => v.type == FleetVehicleType.ElectricLocomotive || v.type == FleetVehicleType.DieselLocomotive);
            int emus = _vehicles.Count(v => v.type == FleetVehicleType.EMU);
            int dmus = _vehicles.Count(v => v.type == FleetVehicleType.DMU);
            int cars = _vehicles.Count(v => v.type == FleetVehicleType.PassengerCar);

            var parts = new List<string>();
            if (locos > 0) parts.Add(LocalizationService.Get("fleet.counter_parts.loco_format", locos));
            if (emus > 0) parts.Add(LocalizationService.Get("fleet.counter_parts.emu_format", emus));
            if (dmus > 0) parts.Add(LocalizationService.Get("fleet.counter_parts.dmu_format", dmus));
            if (cars > 0) parts.Add(LocalizationService.Get("fleet.counter_parts.car_format", cars));

            _counterLbl.text = LocalizationService.Get("fleet.counter_format", total, string.Join(", ", parts));
        }

        private void OnSearchChanged(string value)
        {
            _searchText = value;
            PopulateContent();
        }

        // TD-032: delegacja do VehicleChipStyle (DRY — współdzielone z ConsistPopupUI picker rozprzęgania)
        private static Color GetThumbnailColor(FleetVehicleType type) => VehicleChipStyle.ColorForType(type);

        private static string GetTypeShortLabel(FleetVehicleType type) => VehicleChipStyle.ShortLabel(type);

        private static Color GetStatusColor(FleetVehicleStatus status) => status switch
        {
            FleetVehicleStatus.MovingOnMap => StatusMovingMap,
            FleetVehicleStatus.StoppedOnMap => StatusStoppedMap,
            FleetVehicleStatus.StoppedInDepot => StatusStoppedDepot,
            FleetVehicleStatus.MovingInDepot => StatusMovingDepot,
            FleetVehicleStatus.InRepair => StatusRepair,
            FleetVehicleStatus.OutOfService => StatusOOS,
            FleetVehicleStatus.InProduction => UITheme.WithAlpha(UITheme.PrimaryAccent, 0.90f),
            FleetVehicleStatus.InTransit => UITheme.WithAlpha(UITheme.Focus, 0.92f),
            FleetVehicleStatus.AwaitingPickup => UITheme.WithAlpha(UITheme.Warning, 0.95f),
            _ => StatusOOS
        };

        private static string GetStatusText(FleetVehicleStatus status) => status switch
        {
            FleetVehicleStatus.MovingOnMap => LocalizationService.Get("fleet.status.moving_on_map"),
            FleetVehicleStatus.StoppedOnMap => LocalizationService.Get("fleet.status.stopped_on_map"),
            FleetVehicleStatus.StoppedInDepot => LocalizationService.Get("fleet.status.stopped_in_depot"),
            FleetVehicleStatus.MovingInDepot => LocalizationService.Get("fleet.status.moving_in_depot"),
            FleetVehicleStatus.InRepair => LocalizationService.Get("fleet.status.in_repair"),
            FleetVehicleStatus.OutOfService => LocalizationService.Get("fleet.status.out_of_service"),
            FleetVehicleStatus.InProduction => LocalizationService.Get("fleet.status.in_production"),
            FleetVehicleStatus.InTransit => LocalizationService.Get("fleet.status.in_transit"),
            FleetVehicleStatus.AwaitingPickup => LocalizationService.Get("fleet.status.awaiting_pickup"),
            _ => LocalizationService.Get("fleet.status.unknown")
        };

        private static Color GetConditionColor(float percent)
        {
            if (percent >= 70f) return CondGood;
            if (percent >= 40f) return CondMed;
            return CondBad;
        }

        private static GameObject NewGO(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static TextMeshProUGUI MakeTMP(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var tmp = go.AddComponent<TextMeshProUGUI>();
            UITheme.ApplyTmpText(tmp, UIThemeTextRole.Primary);
            tmp.fontStyle = FontStyles.Normal;
            return tmp;
        }

        private static void FillRT(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
