using System.Collections.Generic;
using DepotSystem;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;
using DepotSystem.OutdoorEquipment;
using DepotSystem.RoomLevel;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.SharedUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// MM-16 — UI flow gracza dla M-Modernization akcji "wyślij pojazd".
    ///
    /// Dorzuca sekcję "Akcje" w FleetPanelUI.DetailPopup z 5 grupami przycisków:
    /// <list type="bullet">
    /// <item><b>Mycie</b> (WashZone outdoor + wash_gate indoor) — wymaga active WashBay worker</item>
    /// <item><b>Tankowanie</b> (FuelStation outdoor + fuel_pump indoor) — tylko diesle</item>
    /// <item><b>Modernizacja</b> (External ZNTK + Internal Hall lvl5 z MM-D13) — applicable paths</item>
    /// <item><b>Modyfikacje</b> (External + Internal) — applicable mods filter</item>
    /// <item><b>Self-paint</b> (paint_bay w Hall lvl ≥2) — alternatywa dla ZNTK paint</item>
    /// </list>
    ///
    /// Wzorzec: analog do <c>BuildPaintSection</c> (M-FC-9 ZNTK paint) — najpierw aktywny job
    /// (z Cancel button gdy jest), potem opcje wysłania (Schedule via service API).
    ///
    /// Uwaga: P-przegląd internal NIE w tej sekcji bo już istnieje w `BuildInspectionCollapsible`
    /// (M7 inspection panel z osobnym flow przez WorkshopManager.AssignVehicle).
    /// </summary>
    public partial class FleetPanelUI
    {
        // ════════════════════════════════════════════════════════
        //  ENTRY POINT
        // ════════════════════════════════════════════════════════

        private void BuildSendActionsSection(Transform parent, FleetVehicleData v)
        {
            // M9c-D F3: pojazd w trakcie dostawy (produkcja / oczekuje na odbiór / w transporcie) →
            // tylko sekcja dostawy. Akcje warsztatowe (mycie/tankowanie/modernizacja) wymagają
            // pojazdu fizycznie w zajezdni.
            if (v.status == FleetVehicleStatus.InProduction
                || v.status == FleetVehicleStatus.AwaitingPickup
                || v.status == FleetVehicleStatus.InTransit)
            {
                BuildDeliverySubSection(parent, v);
                return;
            }

            PopupSectionTitle(parent, "Akcje (M-Modernization)");

            BuildWashSubSection(parent, v);
            BuildRefuelSubSection(parent, v);
            BuildWaterServiceSubSection(parent, v);
            BuildModernizationSubSection(parent, v);
            BuildModificationSubSection(parent, v);
            BuildSelfPaintSubSection(parent, v);
        }

        // ════════════════════════════════════════════════════════
        //  M9c-D: Dostawa taboru (produkcja → punkt zakupu → zajezdnia)
        // ════════════════════════════════════════════════════════

        private void BuildDeliverySubSection(Transform parent, FleetVehicleData v)
        {
            PopupSectionTitle(parent, "Dostawa");
            long now = NowGT();

            if (v.status == FleetVehicleStatus.InProduction)
            {
                long remaining = v.estimatedCompletionGameTime - now;
                int days = Mathf.Max(0, Mathf.CeilToInt(remaining / 86400f));
                PopupInfoLine(parent, "W produkcji", days > 0 ? $"gotowy za ~{days} dni" : "wkrótce gotowy", TextMuted);
                if (!string.IsNullOrEmpty(v.position?.externalLocation))
                    PopupInfoLine(parent, "Fabryka", v.position.externalLocation, TextMuted);
                return;
            }

            if (v.status == FleetVehicleStatus.InTransit)
            {
                long remaining = v.estimatedCompletionGameTime - now;
                int hours = Mathf.Max(0, Mathf.CeilToInt(remaining / 3600f));
                PopupInfoLine(parent, "W dostawie", hours > 0 ? $"przyjazd za ~{hours} h" : "prawie na miejscu",
                    UITheme.PrimaryAccent);
                return;
            }

            // AwaitingPickup — pojazd stoi na torze punktu zakupu, gracz wybiera metodę dostawy
            if (!string.IsNullOrEmpty(v.position?.externalLocation))
                PopupInfoLine(parent, "Punkt odbioru", v.position.externalLocation, TextPrimary);

            if (!GameState.IsHomeDepotSet)
            {
                PopupInfoLine(parent, "Dostawa", "Najpierw wybierz lokalizację zajezdni", UITheme.Warning);
                return;
            }

            if (DeliveryActionsHook.RequestExpressDelivery == null)
            {
                PopupInfoLine(parent, "Dostawa", "Usługa dostawy niedostępna", TextMuted);
                return;
            }

            int cost = DeliveryActionsHook.EstimateExpressCostZl?.Invoke(v.id) ?? 0;
            bool selfPropelled = v.supportedTractions != null
                && v.supportedTractions.Exists(t => t != TractionType.None);

            if (selfPropelled)
            {
                // Loko / EZT / SZT: ekspres (płatny, szybki) lub własny rozkład (jedzie sam, za darmo).
                long etaSec = DeliveryActionsHook.EstimateExpressTimeSec?.Invoke(v.id) ?? 0L;
                int etaH = Mathf.Max(1, Mathf.CeilToInt(etaSec / 3600f));
                if (GameState.Money >= cost)
                    BuildActionButton(parent, $"Dostawa ekspresowa ({cost:N0} zł, ~{etaH} h)",
                        () => DeliveryActionsHook.RequestExpressDelivery.Invoke(v.id));
                else
                    PopupInfoLine(parent, "Dostawa ekspresowa", $"Brak gotówki ({cost:N0} zł)", UITheme.Danger);

                if (DeliveryActionsHook.RequestScheduledDelivery != null)
                    BuildActionButton(parent, "Dostawa własnym rozkładem (pojazd jedzie sam)",
                        () => DeliveryActionsHook.RequestScheduledDelivery.Invoke(v.id));
            }
            else
            {
                // F5 wagon pasywny: lokomotywa producenta (płatna, znika po dostawie) lub własne loco (round-trip).
                if (GameState.Money >= cost)
                    BuildActionButton(parent, $"Dostawa z lokomotywą producenta ({cost:N0} zł)",
                        () => DeliveryActionsHook.RequestDealerWagonDelivery?.Invoke(v.id));
                else
                    PopupInfoLine(parent, "Dostawa producenta", $"Brak gotówki ({cost:N0} zł)", UITheme.Danger);

                bool hasLoco = DeliveryActionsHook.HasAvailableLocoForFetch != null
                            && DeliveryActionsHook.HasAvailableLocoForFetch.Invoke();
                if (hasLoco)
                    BuildActionButton(parent, "Wyślij własną lokomotywę po wagon (za darmo)",
                        () => DeliveryActionsHook.RequestOwnLocoWagonDelivery?.Invoke(v.id));
                else
                    PopupInfoLine(parent, "Własna lokomotywa", "Brak wolnej lokomotywy w zajezdni", TextMuted);
            }
        }

        // ════════════════════════════════════════════════════════
        //  Mycie (WashZone outdoor + wash_gate indoor)
        // ════════════════════════════════════════════════════════

        private void BuildWashSubSection(Transform parent, FleetVehicleData v)
        {
            // Aktywny job?
            var activeJob = OutdoorEquipmentJobService.GetActiveJobForVehicle(v.id);
            if (activeJob != null && activeJob.type == OutdoorJobType.Wash)
            {
                BuildJobStatusRow(parent, "W myjni", activeJob.completionGameTime, activeJob.costGroszy / 100,
                    () => OutdoorEquipmentJobService.Cancel(v.id, NowGT()),
                    PhaseHintFor(activeJob.state));
                return;
            }

            int washInstances = CountOutdoorOfType(OutdoorEquipmentType.WashZone)
                              + CountFurnitureWithFunction(ObjectFunction.WashStation);
            if (washInstances == 0)
            {
                PopupInfoLine(parent, "Mycie", "Brak myjni — postaw WashZone lub wash_gate", TextMuted);
                return;
            }

            BuildActionButton(parent, $"Wyślij na mycie (200 zł, 30 min)",
                () => OutdoorEquipmentJobService.ScheduleWash(v.id, -1, NowGT()));
        }

        // ════════════════════════════════════════════════════════
        //  Tankowanie (FuelStation outdoor + fuel_pump indoor)
        // ════════════════════════════════════════════════════════

        private void BuildRefuelSubSection(Transform parent, FleetVehicleData v)
        {
            if (!v.RequiresFuel)
            {
                // Elektryki — pomijamy (brak gameplay'u tankowania)
                return;
            }

            var activeJob = OutdoorEquipmentJobService.GetActiveJobForVehicle(v.id);
            if (activeJob != null && activeJob.type == OutdoorJobType.Refuel)
            {
                BuildJobStatusRow(parent, "Tankowanie", activeJob.completionGameTime, activeJob.costGroszy / 100,
                    () => OutdoorEquipmentJobService.Cancel(v.id, NowGT()),
                    PhaseHintFor(activeJob.state));
                return;
            }

            int fuelInstances = CountOutdoorOfType(OutdoorEquipmentType.FuelStation)
                              + CountFurnitureWithFunction(ObjectFunction.Refueling);
            if (fuelInstances == 0)
            {
                PopupInfoLine(parent, "Tankowanie", "Brak stacji paliw — postaw FuelStation lub fuel_pump", TextMuted);
                return;
            }

            int costZl = FleetFuelMath.RefuelCostGroszy(v) / 100;  // zgodne z kwotą pobieraną przy ScheduleRefuel
            PopupInfoLine(parent, "Poziom paliwa", $"{v.fuelLevelPercent:F0}%",
                v.fuelLevelPercent < 30f ? UITheme.Danger : (v.fuelLevelPercent < 60f ? UITheme.Warning : UITheme.Success));
            BuildActionButton(parent, $"Wyślij na tankowanie ({costZl} zł, 15 min)",
                () => OutdoorEquipmentJobService.ScheduleRefuel(v.id, -1, NowGT()));
        }

        // ════════════════════════════════════════════════════════
        //  Wodowanie (MM-17 — woda + opróżnienie zbiornika fekaliów dla pasażerskich)
        // ════════════════════════════════════════════════════════

        private void BuildWaterServiceSubSection(Transform parent, FleetVehicleData v)
        {
            if (!v.RequiresWaterService)
            {
                // Lokomotywy luzem — bez toalet, sekcja niewidoczna
                return;
            }

            var activeJob = OutdoorEquipmentJobService.GetActiveJobForVehicle(v.id);
            if (activeJob != null && activeJob.type == OutdoorJobType.WaterService)
            {
                BuildJobStatusRow(parent, "Wodowanie", activeJob.completionGameTime,
                    activeJob.costGroszy / 100,
                    () => OutdoorEquipmentJobService.Cancel(v.id, NowGT()),
                    PhaseHintFor(activeJob.state));
                return;
            }

            int waterInstances = CountOutdoorOfType(OutdoorEquipmentType.WaterService)
                              + CountFurnitureWithFunction(ObjectFunction.WaterService);
            if (waterInstances == 0)
            {
                PopupInfoLine(parent, "Wodowanie",
                    "Brak stanowiska — postaw WaterService outdoor (button WOD) lub water_service indoor",
                    TextMuted);
                return;
            }

            // Status zbiorników z kolorem ostrzegawczym przy brakach / przepełnieniach
            Color waterColor = v.waterLevelPercent < 20f ? UITheme.Danger
                              : v.waterLevelPercent < 50f ? UITheme.Warning
                              : UITheme.Success;
            Color wasteColor = v.wasteTankLevelPercent > 80f ? UITheme.Danger
                              : v.wasteTankLevelPercent > 50f ? UITheme.Warning
                              : UITheme.Success;

            PopupInfoLine(parent, "Woda", $"{v.waterLevelPercent:F0}%", waterColor);
            PopupInfoLine(parent, "Fekalia", $"{v.wasteTankLevelPercent:F0}%", wasteColor);

            BuildActionButton(parent, "Wyślij na wodowanie (100 zł, 20 min)",
                () => OutdoorEquipmentJobService.ScheduleWaterService(v.id, -1, NowGT()));
        }

        // ════════════════════════════════════════════════════════
        //  Modernizacja (External ZNTK + Internal Hall lvl5)
        // ════════════════════════════════════════════════════════

        private void BuildModernizationSubSection(Transform parent, FleetVehicleData v)
        {
            var activeJob = ModernizationJobService.GetActiveJobForVehicle(v.id);
            if (activeJob != null)
            {
                var path = ModernizationPathCatalog.GetByPathId(activeJob.pathId);
                string label = path != null ? $"Modernizacja {path.displayName}" : "Modernizacja w toku";
                BuildJobStatusRow(parent, label, activeJob.completionGameTime,
                    (int)(activeJob.costPlnTotal),
                    () => ModernizationJobService.Cancel(v.id, NowGT()),
                    PhaseHintFor(activeJob.state));
                return;
            }

            var paths = ModernizationPathCatalog.GetForSourceSeries(v.seriesId);
            if (paths.Count == 0) return; // brak applicable path = nie pokazujemy sekcji

            PopupSectionTitle(parent, "Modernizacje");
            foreach (var path in paths)
            {
                BuildModernizationRow(parent, v, path);
            }
        }

        private void BuildModernizationRow(Transform parent, FleetVehicleData v, ModernizationPath path)
        {
            var rowGO = NewGO($"ModRow_{path.pathId}", parent);
            var rowImg = rowGO.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.30f), UIShapePreset.Inset);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 60f;
            var rowVL = rowGO.AddComponent<VerticalLayoutGroup>();
            rowVL.padding = UITheme.Padding(UITheme.Spacing.Sm);
            rowVL.spacing = UITheme.Spacing.Xs;
            rowVL.childForceExpandWidth = true; rowVL.childForceExpandHeight = false;
            rowVL.childControlWidth = true; rowVL.childControlHeight = true;

            var titleLbl = MakeTMP("Title", rowGO.transform);
            titleLbl.text = $"<b>{path.displayName}</b> — {path.durationDays}d";
            titleLbl.fontSize = 11; titleLbl.color = TextPrimary;
            titleLbl.richText = true; titleLbl.raycastTarget = false;

            // 2 buttony obok siebie: ZNTK + Własny warsztat
            var btnRowGO = NewGO("ButtonRow", rowGO.transform);
            var btnRowHL = btnRowGO.AddComponent<HorizontalLayoutGroup>();
            btnRowHL.spacing = UITheme.Spacing.Xs;
            btnRowHL.childForceExpandWidth = true; btnRowHL.childForceExpandHeight = true;
            btnRowGO.AddComponent<LayoutElement>().preferredHeight = 28f;

            // ZNTK button — wybiera pierwszy ZNTK obsługujący modernizację (uproszczenie MVP)
            string firstZntk = FindFirstModernizationWorkshop();
            if (!string.IsNullOrEmpty(firstZntk))
            {
                BuildSubButton(btnRowGO.transform,
                    $"ZNTK: {path.externalCostPln / 1_000_000f:F1}M zł",
                    UITheme.PrimaryAccent,
                    () => ModernizationJobService.ScheduleExternal(v.id, path.pathId, firstZntk, NowGT()));
            }

            // Internal button — gdy Hall lvl ≥ minHallLevelInternal + dostępny ServicePit
            int hallLvl = GetBestHallLevel();
            float maxPitLength = GetMaxAvailableServicePitLength();
            bool internalOk = hallLvl >= path.minHallLevelInternal
                           && maxPitLength + 0.01f >= path.minServicePitLength;
            BuildSubButton(btnRowGO.transform,
                internalOk
                    ? $"Własny: {path.internalCostPln / 1_000_000f:F1}M zł"
                    : $"Własny: brak (Hall {hallLvl}/{path.minHallLevelInternal}, pit {maxPitLength:F0}/{path.minServicePitLength:F0}m)",
                internalOk ? UITheme.Success : UITheme.WithAlpha(UITheme.Border, 0.40f),
                internalOk
                    ? () => ModernizationJobService.ScheduleInternal(v.id, path.pathId, -1, maxPitLength, hallLvl, NowGT())
                    : (System.Action)null);
        }

        // ════════════════════════════════════════════════════════
        //  Modyfikacje pojazdu (External + Internal)
        // ════════════════════════════════════════════════════════

        private void BuildModificationSubSection(Transform parent, FleetVehicleData v)
        {
            var activeJob = VehicleModificationJobService.GetActiveJobForVehicle(v.id);
            if (activeJob != null)
            {
                var mod = VehicleModificationCatalog.GetByModId(activeJob.modId);
                string label = mod != null ? $"Modyfikacja {mod.displayName}" : "Modyfikacja w toku";
                BuildJobStatusRow(parent, label, activeJob.completionGameTime,
                    (int)(activeJob.costPlnTotal),
                    () => VehicleModificationJobService.Cancel(v.id, NowGT()),
                    PhaseHintFor(activeJob.state));
                return;
            }

            var mods = VehicleModificationCatalog.GetApplicableFor(v);
            if (mods.Count == 0) return;

            PopupSectionTitle(parent, "Modyfikacje");
            foreach (var mod in mods)
            {
                BuildModificationRow(parent, v, mod);
            }
        }

        private void BuildModificationRow(Transform parent, FleetVehicleData v, VehicleModification mod)
        {
            var rowGO = NewGO($"VehMod_{mod.modId}", parent);
            var rowImg = rowGO.AddComponent<Image>();
            UITheme.ApplySurface(rowImg, UITheme.WithAlpha(UITheme.SecondarySurface, 0.30f), UIShapePreset.Inset);
            rowGO.AddComponent<LayoutElement>().preferredHeight = 56f;
            var rowVL = rowGO.AddComponent<VerticalLayoutGroup>();
            rowVL.padding = UITheme.Padding(UITheme.Spacing.Sm);
            rowVL.spacing = UITheme.Spacing.Xs;
            rowVL.childForceExpandWidth = true; rowVL.childForceExpandHeight = false;
            rowVL.childControlWidth = true; rowVL.childControlHeight = true;

            var titleLbl = MakeTMP("Title", rowGO.transform);
            titleLbl.text = $"<b>{mod.displayName}</b> — {mod.durationDays}d";
            titleLbl.fontSize = 11; titleLbl.color = TextPrimary;
            titleLbl.richText = true; titleLbl.raycastTarget = false;

            var btnRowGO = NewGO("ButtonRow", rowGO.transform);
            var btnRowHL = btnRowGO.AddComponent<HorizontalLayoutGroup>();
            btnRowHL.spacing = UITheme.Spacing.Xs;
            btnRowHL.childForceExpandWidth = true; btnRowHL.childForceExpandHeight = true;
            btnRowGO.AddComponent<LayoutElement>().preferredHeight = 26f;

            string firstZntk = FindFirstModernizationWorkshop();
            if (!string.IsNullOrEmpty(firstZntk))
            {
                BuildSubButton(btnRowGO.transform,
                    $"ZNTK: {mod.externalCostPln / 1_000_000f:F2}M zł",
                    UITheme.PrimaryAccent,
                    () => VehicleModificationJobService.ScheduleExternal(v.id, mod.modId, firstZntk, NowGT()));
            }

            int hallLvl = GetBestHallLevel();
            float maxPitLength = GetMaxAvailableServicePitLength();
            bool internalOk = hallLvl >= mod.minHallLevelInternal && maxPitLength + 0.01f >= v.lengthM;
            BuildSubButton(btnRowGO.transform,
                internalOk
                    ? $"Własny: {mod.internalCostPln / 1_000_000f:F2}M zł"
                    : $"Własny: brak (Hall {hallLvl}/{mod.minHallLevelInternal})",
                internalOk ? UITheme.Success : UITheme.WithAlpha(UITheme.Border, 0.40f),
                internalOk
                    ? () => VehicleModificationJobService.ScheduleInternal(v.id, mod.modId, -1, maxPitLength, hallLvl, NowGT())
                    : (System.Action)null);
        }

        // ════════════════════════════════════════════════════════
        //  Self-paint (paint_bay w Hall lvl ≥ 2)
        // ════════════════════════════════════════════════════════

        private void BuildSelfPaintSubSection(Transform parent, FleetVehicleData v)
        {
            var activeJob = SelfPaintingService.GetActiveJobForVehicle(v.id);
            if (activeJob != null)
            {
                BuildJobStatusRow(parent, "Self-paint", activeJob.completionGameTime,
                    (int)(activeJob.costPln),
                    () => SelfPaintingService.Cancel(v.id, NowGT()),
                    PhaseHintFor(activeJob.state));
                return;
            }

            int paintBayCount = CountFurnitureWithFunction(ObjectFunction.Painting);
            if (paintBayCount == 0) return; // brak paint_bay = nie pokazujemy sekcji

            int hallLvl = GetBestHallLevel();
            int days = SelfPaintingService.GetPaintTimeDays(hallLvl);
            if (days <= 0)
            {
                PopupInfoLine(parent, "Self-paint", $"Wymaga Hall lvl ≥{SelfPaintingService.MinHallLevel} (masz {hallLvl})", TextMuted);
                return;
            }

            PopupSectionTitle(parent, "Self-paint (własny warsztat)");
            BuildActionButton(parent,
                $"Self-paint @ paint_bay (Hall lvl {hallLvl}, {days}d, {SelfPaintingService.BasePaintCostPln / 1000f:F0}k zł)",
                () => SelfPaintingService.Schedule(v.id, -1, null, hallLvl, NowGT()));
        }

        // ════════════════════════════════════════════════════════
        //  Helpers — UI
        // ════════════════════════════════════════════════════════

        private void BuildActionButton(Transform parent, string label, System.Action onClick)
        {
            var btnGO = NewGO("ActionBtn", parent);
            btnGO.AddComponent<LayoutElement>().preferredHeight = 28f;
            var img = btnGO.AddComponent<Image>();
            UITheme.ApplySurface(img, UITheme.WithAlpha(UITheme.PrimaryAccent, 0.40f), UIShapePreset.Button);
            var lbl = MakeTMP("Lbl", btnGO.transform);
            lbl.text = label;
            lbl.fontSize = 11; lbl.color = TextPrimary;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false; FillRT(lbl.gameObject);
            var btn = btnGO.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => {
                onClick?.Invoke();
                OnPopupRefreshNeeded();
            });
        }

        private void BuildSubButton(Transform parent, string label, Color bgColor, System.Action onClick)
        {
            var btnGO = NewGO("SubBtn", parent);
            var img = btnGO.AddComponent<Image>();
            UITheme.ApplySurface(img, UITheme.WithAlpha(bgColor, 0.40f), UIShapePreset.Button);
            var lbl = MakeTMP("Lbl", btnGO.transform);
            lbl.text = label;
            lbl.fontSize = 10; lbl.color = TextPrimary;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false; FillRT(lbl.gameObject);
            if (onClick != null)
            {
                var btn = btnGO.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => {
                    onClick.Invoke();
                    OnPopupRefreshNeeded();
                });
            }
        }

        /// <summary>
        /// MM-18e: phase-aware status row. <paramref name="phaseHint"/> może być:
        /// - null/empty → legacy "pozostało Xmin/Xd"
        /// - "EnRoute" → "▶ Jedzie do stanowiska" + refund 100%
        /// - "Servicing" → "⏳ Serwis — pozostało Xmin/Xd" + refund 50%
        /// </summary>
        private void BuildJobStatusRow(Transform parent, string label, long completionGT, long costZl,
                                        System.Func<bool> onCancel, string phaseHint = null)
        {
            long now = NowGT();
            long remaining = completionGT - now;
            int remainingDays = (int)System.Math.Ceiling(remaining / 86400.0);
            int remainingMin = (int)System.Math.Ceiling(remaining / 60.0);
            string etaStr = remainingDays >= 1 ? $"{remainingDays}d" : $"{remainingMin}min";

            // MM-18e: phase-specific display + refund
            bool isEnRoute = phaseHint == "EnRoute";
            bool isServicing = phaseHint == "Servicing";
            string statusText = isEnRoute
                ? $"<b>{label}</b> <color=#FFC845>▶ Jedzie do stanowiska</color>"
                : isServicing
                    ? $"<b>{label}</b> <color=#5BC8FA>⏳ Serwis</color> — pozostało {etaStr}"
                    : $"<b>{label}</b> — pozostało {etaStr}";

            int refundPercent = isEnRoute ? 100 : 50;
            long refundZl = costZl * refundPercent / 100;

            var statusGO = NewGO($"ActiveJob_{label}", parent);
            var statusImg = statusGO.AddComponent<Image>();
            Color tint = isEnRoute ? UITheme.PrimaryAccent : UITheme.Warning;
            UITheme.ApplySurface(statusImg, UITheme.WithAlpha(tint, 0.20f), UIShapePreset.Inset);
            statusGO.AddComponent<LayoutElement>().preferredHeight = 50f;
            var statusVL = statusGO.AddComponent<VerticalLayoutGroup>();
            statusVL.padding = UITheme.Padding(UITheme.Spacing.Sm);
            statusVL.spacing = UITheme.Spacing.Xxs;
            statusVL.childForceExpandWidth = true; statusVL.childForceExpandHeight = false;
            statusVL.childControlWidth = true; statusVL.childControlHeight = true;

            var statusLbl = MakeTMP("StatusLbl", statusGO.transform);
            statusLbl.text = statusText;
            statusLbl.fontSize = 11; statusLbl.color = TextPrimary;
            statusLbl.richText = true; statusLbl.raycastTarget = false;

            var cancelGO = NewGO("CancelBtn", statusGO.transform);
            cancelGO.AddComponent<LayoutElement>().preferredHeight = 22f;
            var cancelImg = cancelGO.AddComponent<Image>();
            UITheme.ApplySurface(cancelImg, UITheme.WithAlpha(UITheme.Danger, 0.30f), UIShapePreset.Button);
            var cancelLbl = MakeTMP("Lbl", cancelGO.transform);
            cancelLbl.text = $"Anuluj (refund {refundPercent}% = {refundZl:N0} zł)";
            cancelLbl.fontSize = 10; cancelLbl.color = TextPrimary;
            cancelLbl.alignment = TextAlignmentOptions.Center;
            cancelLbl.raycastTarget = false; FillRT(cancelLbl.gameObject);
            var cancelBtn = cancelGO.AddComponent<Button>();
            cancelBtn.targetGraphic = cancelImg;
            cancelBtn.transition = Selectable.Transition.None;
            cancelBtn.onClick.AddListener(() => {
                if (onCancel != null && onCancel.Invoke()) OnPopupRefreshNeeded();
            });
        }

        // MM-18e: helpers do mapowania state → phaseHint string
        private static string PhaseHintFor(OutdoorJobState state) => state switch
        {
            OutdoorJobState.EnRoute => "EnRoute",
            OutdoorJobState.Servicing => "Servicing",
            _ => null,
        };

        private static string PhaseHintFor(ServiceJobState state) => state switch
        {
            ServiceJobState.EnRoute => "EnRoute",
            ServiceJobState.Servicing => "Servicing",
            _ => null,
        };

        // ════════════════════════════════════════════════════════
        //  Helpers — query (state lookups)
        // ════════════════════════════════════════════════════════

        private static long NowGT()
            => RailwayManager.Core.GameState.GameDay * 86400L + (long)RailwayManager.Core.GameState.GameTimeSeconds;

        private static int CountOutdoorOfType(OutdoorEquipmentType type)
        {
            var placer = OutdoorEquipmentPlacer.Instance;
            if (placer == null) return 0;
            int n = 0;
            foreach (var oe in placer.Placed)
                if (oe != null && oe.type == type) n++;
            return n;
        }

        private static int CountFurnitureWithFunction(ObjectFunction fn)
        {
            var placer = FurniturePlacer.Instance;
            if (placer == null) return 0;
            int n = 0;
            foreach (var inst in placer.PlacedInstances)
            {
                if (inst == null) continue;
                var item = FurnitureCatalog.FindById(inst.itemId);
                if (item != null && item.HasFunction(fn)) n++;
            }
            return n;
        }

        private static int GetBestHallLevel()
        {
            var svc = RoomLevelService.Instance;
            return svc == null ? 0 : svc.GetBestLevelForType(RoomType.Hall);
        }

        private static float GetMaxAvailableServicePitLength()
        {
            var placer = FurniturePlacer.Instance;
            if (placer == null) return 0f;
            float maxLen = 0f;
            foreach (var inst in placer.PlacedInstances)
            {
                if (inst == null) continue;
                var item = FurnitureCatalog.FindById(inst.itemId);
                if (item == null) continue;
                if (!item.HasFunction(ObjectFunction.ServicePit)) continue;
                if (item.maxVehicleLength > maxLen) maxLen = item.maxVehicleLength;
            }
            return maxLen;
        }

        private static string FindFirstModernizationWorkshop()
        {
            foreach (var w in ExternalWorkshopCatalog.GetAll())
                if (w.modernizationAvailable) return w.id;
            return null;
        }
    }
}
