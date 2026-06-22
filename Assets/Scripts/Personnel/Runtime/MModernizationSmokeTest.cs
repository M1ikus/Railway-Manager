using System.Text;
using UnityEngine;
using DepotSystem;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;
using DepotSystem.OutdoorEquipment;
using DepotSystem.RoomLevel;
using RailwayManager.Core;
using RailwayManager.Fleet;
using RailwayManager.Maintenance;
using RailwayManager.Personnel.Furniture;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// MM-14 — combined end-to-end smoke test dla całego M-Modernization.
    ///
    /// Dump'uje stan wszystkich obszarów MM-1..MM-13 w jednym ContextMenu, plus
    /// audit hooków cross-asmdef i catalog availability. Lokalizacja:
    /// <c>RailwayManager.Personnel</c> namespace bo Personnel asmdef widzi
    /// Depot+Fleet+Timetable+Maintenance (najszerszy zasięg po Personnel reference list).
    ///
    /// Auto-spawn jak inne smoke testy. ContextMenu metody:
    /// <list type="bullet">
    /// <item>Full M-Modernization summary — wszystkie obszary jednym log'iem</item>
    /// <item>Hook audit — sprawdza czy cross-asmdef hooki installed</item>
    /// <item>Catalog availability — RoomLevel + Modernization + VehicleModification</item>
    /// <item>Active jobs counts — Outdoor + Modernization + VehicleMod + SelfPaint + Dispatch</item>
    /// </list>
    /// </summary>
    public class MModernizationSmokeTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Object.FindAnyObjectByType<MModernizationSmokeTest>() != null) return;
            var go = new GameObject("MModernizationSmokeTest (auto-spawn)");
            go.AddComponent<MModernizationSmokeTest>();
        }

        [ContextMenu("MM-14: Full M-Modernization summary")]
        public void FullSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("══════════════════════════════════════════════");
            sb.AppendLine("  M-MODERNIZATION FULL SUMMARY (MM-1..MM-13)");
            sb.AppendLine("══════════════════════════════════════════════");

            DumpRoomLevels(sb);
            DumpWorkshopSlots(sb);
            DumpOfficeBonus(sb);
            DumpDispatcherBonus(sb);
            DumpTrafficControllerBonus(sb);
            DumpMoraleBonus(sb);
            DumpActiveJobs(sb);
            DumpDispatchActions(sb);
            DumpOutdoorEquipment(sb);
            DumpFurnitureCounts(sb);

            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-14: Hook integration audit")]
        public void HookAudit()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[MM-14] Hook integration audit (cross-asmdef DI):");

            // M8 → M7 hooki (już od dawna)
            sb.AppendLine($"  WorkshopManager.SlotSpeedMultiplierHook: " +
                          $"{(WorkshopManager.SlotSpeedMultiplierHook != null ? "✓ installed" : "✗ MISSING")} (M8-12)");
            sb.AppendLine($"  BreakdownService.SelfRepairBonusHook: " +
                          $"{(BreakdownService.SelfRepairBonusHook != null ? "✓ installed" : "✗ MISSING")} (M8-12)");

            // MM-10: Reputation hook dla modernizacji
            sb.AppendLine($"  ModernizationJobService.OnModernizationCompletedReputationHook: " +
                          $"{(ModernizationJobService.OnModernizationCompletedReputationHook != null ? "✓ installed" : "✗ MISSING")} (MM-10)");

            // MM-13: Wash bay worker presence
            sb.AppendLine($"  OutdoorEquipmentJobService.WashBayWorkerPresenceHook: " +
                          $"{(OutdoorEquipmentJobService.WashBayWorkerPresenceHook != null ? "✓ installed" : "✗ MISSING")} (MM-13)");

            // MM-18: Movement hooks (Service → Bridge w Timetable)
            sb.AppendLine($"  OutdoorEquipmentJobService.RequestMovementHook: " +
                          $"{(OutdoorEquipmentJobService.RequestMovementHook != null ? "✓ installed" : "✗ MISSING")} (MM-18b)");
            sb.AppendLine($"  ModernizationJobService.RequestMovementHook: " +
                          $"{(ModernizationJobService.RequestMovementHook != null ? "✓ installed" : "✗ MISSING")} (MM-18d)");
            sb.AppendLine($"  VehicleModificationJobService.RequestMovementHook: " +
                          $"{(VehicleModificationJobService.RequestMovementHook != null ? "✓ installed" : "✗ MISSING")} (MM-18d)");
            sb.AppendLine($"  SelfPaintingService.RequestMovementHook: " +
                          $"{(SelfPaintingService.RequestMovementHook != null ? "✓ installed" : "✗ MISSING")} (MM-18d)");

            // MM-18f/g: Validation hooks (Personnel → Bridge)
            sb.AppendLine($"  Bridge.DriverAvailableHook: " +
                          $"{(RailwayManager.Maintenance.Movement.OutdoorEquipmentMovementBridge.DriverAvailableHook != null ? "✓ installed" : "✗ MISSING")} (MM-18f)");
            sb.AppendLine($"  Bridge.TrafficControllerAcceptHook: " +
                          $"{(RailwayManager.Maintenance.Movement.OutdoorEquipmentMovementBridge.TrafficControllerAcceptHook != null ? "✓ installed" : "✗ MISSING")} (MM-18g)");

            // MM-18b: bridge instance
            sb.AppendLine($"  OutdoorEquipmentMovementBridge.Instance: " +
                          $"{(RailwayManager.Maintenance.Movement.OutdoorEquipmentMovementBridge.Instance != null ? "✓ exists" : "✗ MISSING")} (MM-18b)");
            sb.AppendLine($"  DepotMovementSimulator.Instance: " +
                          $"{(DepotSystem.DepotMovementSimulator.Instance != null ? "✓ exists" : "✗ MISSING")} (M9b)");

            // Service singletons
            sb.AppendLine($"  RoomLevelService.Instance: " +
                          $"{(RoomLevelService.Instance != null ? "✓ exists" : "✗ MISSING")} (MM-2)");
            sb.AppendLine($"  WorkshopManager.Instance: " +
                          $"{(WorkshopManager.Instance != null ? "✓ exists" : "✗ MISSING")} (M7)");
            sb.AppendLine($"  PersonnelService initialized: " +
                          $"{(PersonnelService.IsInitialized ? "✓" : "✗")} (M8)");
            sb.AppendLine($"  CleaningService.Instance: " +
                          $"{(CleaningService.Instance != null ? "✓ exists" : "✗ MISSING")} (M8/MM-13)");
            sb.AppendLine($"  ReputationManager.Instance: " +
                          $"{(RailwayManager.Timetable.Economy.ReputationManager.Instance != null ? "✓ exists" : "✗ MISSING")} (M6/MM-10)");

            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-14: Catalog availability check")]
        public void CatalogAudit()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[MM-14] Catalog availability check:");

            // RoomLevel catalog (MM-1)
            RoomLevelCatalog.EnsureInitialized();
            int roomLevelEntries = 0;
            foreach (var rt in RoomLevelCatalog.LvlableRoomTypes)
                roomLevelEntries += RoomLevelCatalog.GetMaxLevel(rt);
            sb.AppendLine($"  RoomLevelCatalog: {roomLevelEntries} entries " +
                          $"({RoomLevelCatalog.LvlableRoomTypes.Length} room types × max 5 lvl, MM-1) " +
                          (roomLevelEntries >= 35 ? "✓" : "✗"));

            // Modernization paths (MM-10)
            ModernizationPathCatalog.LoadAll();
            int modPaths = ModernizationPathCatalog.GetAll().Count;
            sb.AppendLine($"  ModernizationPathCatalog: {modPaths} paths " +
                          (modPaths >= 3 ? "✓" : "✗") + " (MM-10, EN57/EU07/SM42)");

            // Vehicle modifications (MM-11)
            VehicleModificationCatalog.LoadAll();
            int vehModCount = VehicleModificationCatalog.GetAll().Count;
            sb.AppendLine($"  VehicleModificationCatalog: {vehModCount} modifications " +
                          (vehModCount >= 5 ? "✓" : "✗") + " (MM-11, bogie/comfort/function)");

            // Furniture catalog (M-Furniture + MM-9 fuel_pump + MM-12 paint_bay)
            if (!FurnitureCatalog.IsLoaded) FurnitureCatalog.LoadAll();
            var fuelPump = FurnitureCatalog.FindById("fuel_pump");
            var paintBay = FurnitureCatalog.FindById("paint_bay");
            sb.AppendLine($"  Furniture: fuel_pump (MM-9) " + (fuelPump != null ? "✓" : "✗") +
                          $", paint_bay (MM-12) " + (paintBay != null ? "✓" : "✗"));

            // ServicePit maxVehicleLength (MM-D16)
            var pitSmall = FurnitureCatalog.FindById("pit_small");
            var pitLarge = FurnitureCatalog.FindById("pit_large");
            sb.AppendLine($"  ServicePit length (MM-D16): " +
                          $"pit_small={pitSmall?.maxVehicleLength ?? 0:F0}m " +
                          $"(expect 18) {(pitSmall?.maxVehicleLength == 18f ? "✓" : "✗")}, " +
                          $"pit_large={pitLarge?.maxVehicleLength ?? 0:F0}m " +
                          $"(expect 35) {(pitLarge?.maxVehicleLength == 35f ? "✓" : "✗")}");

            // Enum values (MM-9 Refueling, MM-12 Painting, MM-D11 Onboarding, MM-17 WaterService)
            sb.AppendLine($"  ObjectFunction.Refueling (MM-D14): " +
                          $"{(System.Enum.IsDefined(typeof(ObjectFunction), "Refueling") ? "✓" : "✗")}");
            sb.AppendLine($"  ObjectFunction.Painting (MM-12): " +
                          $"{(System.Enum.IsDefined(typeof(ObjectFunction), "Painting") ? "✓" : "✗")}");
            sb.AppendLine($"  ObjectFunction.WaterService (MM-17): " +
                          $"{(System.Enum.IsDefined(typeof(ObjectFunction), "WaterService") ? "✓" : "✗")}");
            sb.AppendLine($"  EmployeeStatus.Onboarding (MM-D11): " +
                          $"{(System.Enum.IsDefined(typeof(EmployeeStatus), "Onboarding") ? "✓" : "✗")}");
            sb.AppendLine($"  OutdoorEquipmentType.FuelStation (MM-D14): " +
                          $"{(System.Enum.IsDefined(typeof(OutdoorEquipmentType), "FuelStation") ? "✓" : "✗")}");
            sb.AppendLine($"  OutdoorEquipmentType.WaterService (MM-17): " +
                          $"{(System.Enum.IsDefined(typeof(OutdoorEquipmentType), "WaterService") ? "✓" : "✗")}");
            sb.AppendLine($"  OutdoorJobType.WaterService (MM-17): " +
                          $"{(System.Enum.IsDefined(typeof(OutdoorJobType), "WaterService") ? "✓" : "✗")}");

            // MM-17: water_service mebel
            var waterServiceItem = FurnitureCatalog.FindById("water_service");
            sb.AppendLine($"  Furniture: water_service (MM-17) " + (waterServiceItem != null ? "✓" : "✗"));

            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-14: Print active jobs across all services")]
        public void PrintActiveJobsAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[MM-14] Active jobs across M-Modernization services:");
            sb.AppendLine($"  OutdoorEquipmentJobService: {OutdoorEquipmentJobService.ActiveJobs.Count} (MM-9)");
            sb.AppendLine($"  ModernizationJobService: {ModernizationJobService.ActiveJobs.Count} (MM-10)");
            sb.AppendLine($"  VehicleModificationJobService: {VehicleModificationJobService.ActiveJobs.Count} (MM-11)");
            sb.AppendLine($"  SelfPaintingService: {SelfPaintingService.ActiveJobs.Count} (MM-12)");
            sb.AppendLine($"  DispatchActionService active: {DispatchActionService.GetUsedActions()}/" +
                          $"{DispatchActionService.GetTotalActionsCapacity()} (MM-6b), " +
                          $"pending: {DispatchActionService.GetPendingCount()}, " +
                          $"manual mode: {(DispatchActionService.IsManualMode() ? "TAK" : "nie")}");
            Log.Info(sb.ToString());
        }

        // ════════════════════════════════════════════════════════
        //  Helpers (dump sections)
        // ════════════════════════════════════════════════════════

        static void DumpRoomLevels(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("── ROOM LEVELS (MM-1/MM-2) ──");
            var svc = RoomLevelService.Instance;
            if (svc == null) { sb.AppendLine("  RoomLevelService.Instance == null (Depot scene niezaładowane?)"); return; }
            foreach (var rt in RoomLevelCatalog.LvlableRoomTypes)
            {
                int best = svc.GetBestLevelForType(rt);
                sb.AppendLine($"  {rt}: best lvl {best}/5");
            }
        }

        static void DumpWorkshopSlots(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("── WORKSHOP SLOTS (MM-4/MM-8) ──");
            var wm = WorkshopManager.Instance;
            if (wm == null) { sb.AppendLine("  WorkshopManager.Instance == null"); return; }
            sb.AppendLine($"  Total slots: {wm.Slots.Count} (slot per ServicePit instance, MM-8)");
            foreach (var s in wm.Slots)
                sb.AppendLine($"    Slot#{s.slotId} room#{s.roomId} pit#{s.servicePitInstanceId} " +
                              $"{s.level} (max {s.maxVehicleLength:F0}m)");
        }

        static void DumpOfficeBonus(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("── OFFICE (MM-5) ──");
            int lvl = OfficeService.GetOfficeLvl();
            int cap = OfficeService.GetMaxOfficeHeadcount();
            int current = RoleCaps.GetCurrentHeadcountForRole(EmployeeRole.Office);
            float speed = OfficeService.GetResearchSpeedMultiplier();
            sb.AppendLine($"  Lvl: {lvl}, Headcount: {current}/{cap}, R&D speed: {speed:F1}×");
        }

        static void DumpDispatcherBonus(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("── DISPATCHER (MM-6) ──");
            int lvl = DispatcherService.GetDispatcherLvl();
            int cap = DispatcherService.GetMaxDispatcherHeadcount();
            int current = RoleCaps.GetCurrentHeadcountForRole(EmployeeRole.Dispatcher);
            float onboardingMin = DispatcherService.GetOnboardingMinutes();
            sb.AppendLine($"  Lvl: {lvl}, Headcount: {current}/{cap}, Onboarding: {onboardingMin:F1} min");
        }

        static void DumpTrafficControllerBonus(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("── TRAFFIC CONTROLLER (MM-6b) ──");
            int lvl = DispatchActionService.GetTrafficControllerRoomLvl();
            int cap = RoleCaps.GetMaxForRole(EmployeeRole.TrafficController);
            int current = RoleCaps.GetCurrentHeadcountForRole(EmployeeRole.TrafficController);
            int actions = DispatchActionService.GetTotalActionsCapacity();
            sb.AppendLine($"  Lvl: {lvl}, Headcount: {current}/{(cap == int.MaxValue ? "∞" : cap.ToString())}, " +
                          $"Actions cap: {actions}");
        }

        static void DumpMoraleBonus(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("── MORALE (MM-7) ──");
            int sup = MoraleBonusService.GetSupervisorLvl();
            int soc = MoraleBonusService.GetSocialLvl();
            int bath = MoraleBonusService.GetBathroomLvl();
            int total = MoraleBonusService.SupervisorBonusForLvl(sup);
            if (MoraleBonusService.AnyRoomReachable(RoomType.Social))
                total += MoraleBonusService.SocialBonusForLvl(soc);
            if (MoraleBonusService.AnyRoomReachable(RoomType.Bathroom))
                total += Mathf.RoundToInt(MoraleBonusService.BathroomBonusForLvl(bath));
            sb.AppendLine($"  Supervisor lvl {sup}, Social lvl {soc}, Bathroom lvl {bath}");
            sb.AppendLine($"  Total daily morale bonus: +{total}/dzień per active employee");
        }

        static void DumpActiveJobs(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("── ACTIVE JOBS ──");
            sb.AppendLine($"  OutdoorEquipment (MM-9): {OutdoorEquipmentJobService.ActiveJobs.Count}");
            sb.AppendLine($"  Modernization (MM-10): {ModernizationJobService.ActiveJobs.Count}");
            sb.AppendLine($"  VehicleModification (MM-11): {VehicleModificationJobService.ActiveJobs.Count}");
            sb.AppendLine($"  SelfPainting (MM-12): {SelfPaintingService.ActiveJobs.Count}");
        }

        static void DumpDispatchActions(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("── DISPATCH ACTIONS (MM-6b) ──");
            sb.AppendLine($"  Capacity: {DispatchActionService.GetTotalActionsCapacity()} " +
                          $"(used {DispatchActionService.GetUsedActions()}, free {DispatchActionService.GetFreeSlots()})");
            sb.AppendLine($"  Pending: {DispatchActionService.GetPendingCount()}");
            sb.AppendLine($"  Manual mode (MM-D21): {(DispatchActionService.IsManualMode() ? "TAK" : "nie")}");
        }

        static void DumpOutdoorEquipment(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("── OUTDOOR EQUIPMENT (MM-9) ──");
            var placer = OutdoorEquipmentPlacer.Instance;
            if (placer == null) { sb.AppendLine("  OutdoorEquipmentPlacer.Instance == null"); return; }
            sb.AppendLine($"  Placed: {placer.Placed.Count}");
            int washCount = 0, turntableCount = 0, pitliftCount = 0, fuelCount = 0, waterCount = 0;
            foreach (var oe in placer.Placed)
            {
                switch (oe.type)
                {
                    case OutdoorEquipmentType.WashZone: washCount++; break;
                    case OutdoorEquipmentType.Turntable: turntableCount++; break;
                    case OutdoorEquipmentType.PitLift: pitliftCount++; break;
                    case OutdoorEquipmentType.FuelStation: fuelCount++; break;
                    case OutdoorEquipmentType.WaterService: waterCount++; break;
                }
            }
            sb.AppendLine($"    WashZone: {washCount}, Turntable: {turntableCount}, PitLift: {pitliftCount}, " +
                          $"FuelStation: {fuelCount}, WaterService: {waterCount}");
        }

        static void DumpFurnitureCounts(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("── FURNITURE (M-Furniture + MM-9 + MM-12) ──");
            var placer = FurniturePlacer.Instance;
            if (placer == null) { sb.AppendLine("  FurniturePlacer.Instance == null"); return; }
            sb.AppendLine($"  Total placed instances: {placer.PlacedInstances.Count}");
            int servicePit = 0, washStation = 0, refueling = 0, painting = 0, waterService = 0;
            foreach (var inst in placer.PlacedInstances)
            {
                var item = FurnitureCatalog.FindById(inst.itemId);
                if (item == null) continue;
                if (item.HasFunction(ObjectFunction.ServicePit)) servicePit++;
                if (item.HasFunction(ObjectFunction.WashStation)) washStation++;
                if (item.HasFunction(ObjectFunction.Refueling)) refueling++;
                if (item.HasFunction(ObjectFunction.Painting)) painting++;
                if (item.HasFunction(ObjectFunction.WaterService)) waterService++;
            }
            sb.AppendLine($"    ServicePit: {servicePit}, WashStation: {washStation}, " +
                          $"Refueling (fuel_pump): {refueling}, Painting (paint_bay): {painting}, " +
                          $"WaterService (water_service): {waterService}");
        }
    }
}
