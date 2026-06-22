using System.Text;
using UnityEngine;
using DepotSystem;
using DepotSystem.RoomLevel;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// MM-5 — diagnostyka integracji Office lvl ↔ OfficeService cap + ResearchService speed.
    ///
    /// Auto-spawn jak inne smoke testy. ContextMenu metody:
    /// - PrintOfficeStatus: aktualny lvl + cap + headcount + speed multiplier
    /// - PrintRoleCaps: per rola (Office/Dispatcher/TrafficController) + current/max
    /// - SimulateResearchTick: dry-run jak szybko aktywny research postępowałby
    /// - FullSummary
    /// </summary>
    public class OfficeMM5SmokeTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Object.FindAnyObjectByType<OfficeMM5SmokeTest>() != null) return;
            var go = new GameObject("OfficeMM5SmokeTest (auto-spawn)");
            go.AddComponent<OfficeMM5SmokeTest>();
        }

        [ContextMenu("MM-5: Print Office status (lvl + cap + speed multiplier)")]
        public void PrintOfficeStatus()
        {
            int lvl = OfficeService.GetOfficeLvl();
            int cap = OfficeService.GetMaxOfficeHeadcount();
            int current = RoleCaps.GetCurrentHeadcountForRole(EmployeeRole.Office);
            float speed = OfficeService.GetResearchSpeedMultiplier();
            float costRed = OfficeService.GetFixedCostReduction();

            Log.Info($"[OfficeMM5SmokeTest] Office status:\n" +
                     $"  Lvl: {lvl}\n" +
                     $"  Headcount: {current}/{cap} (Office+Research łącznie)\n" +
                     $"  R&D speed multiplier: {speed:F1}× (lvl 0=0×, lvl 5=2.2×)\n" +
                     $"  Fixed cost reduction: {costRed * 100f:F1}% (M8 OfficeService)");
        }

        [ContextMenu("MM-5: Print all role caps (Office/Dispatcher/TrafficController)")]
        public void PrintRoleCaps()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[OfficeMM5SmokeTest] Role caps z lvla pomieszczeń:");
            foreach (var role in new[] {
                EmployeeRole.Office, EmployeeRole.Research,
                EmployeeRole.Dispatcher, EmployeeRole.TrafficController,
                EmployeeRole.Driver, EmployeeRole.Mechanic })
            {
                int cap = RoleCaps.GetMaxForRole(role);
                int current = RoleCaps.GetCurrentHeadcountForRole(role);
                bool atCap = RoleCaps.IsAtCap(role);
                string capStr = cap == int.MaxValue ? "∞" : cap.ToString();
                sb.AppendLine($"  {role}: {current}/{capStr}{(atCap ? " [CAP]" : "")}");
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-5: Simulate research tick (current speed)")]
        public void SimulateResearchTick()
        {
            var active = ResearchService.Active;
            if (active == null)
            {
                Log.Info("[OfficeMM5SmokeTest] No active research — uruchom research path najpierw " +
                         "(ContextMenu Debug: Start 'Lepsze przeglady' albo 'Optymalizacja trakcji')");
                return;
            }

            float speed = OfficeService.GetResearchSpeedMultiplier();
            int tenths = Mathf.RoundToInt(speed * 10f);
            int daysAtBaseline = active.daysRemaining;
            int daysAtCurrent = Mathf.CeilToInt(daysAtBaseline * 10f / Mathf.Max(1, tenths));

            Log.Info($"[OfficeMM5SmokeTest] Active research: {active.displayName}\n" +
                     $"  Days remaining (baseline 1×): {daysAtBaseline}\n" +
                     $"  Current speed: {speed:F1}× ({tenths} tenths/tick)\n" +
                     $"  Estimated days @ current speed: {daysAtCurrent}\n" +
                     $"  Progress accumulator: {active.progressTenthsAccumulated}/10 tenths");
        }

        [ContextMenu("MM-5: Full summary")]
        public void FullSummary()
        {
            PrintOfficeStatus();
            PrintRoleCaps();
        }

        // ════════════════════════════════════════════════════════
        //  MM-6 — Dispatcher onboarding integration
        // ════════════════════════════════════════════════════════

        [ContextMenu("MM-6: Print Dispatcher status (lvl + cap + onboarding minutes)")]
        public void PrintDispatcherStatus()
        {
            int lvl = DispatcherService.GetDispatcherLvl();
            int cap = DispatcherService.GetMaxDispatcherHeadcount();
            int current = RoleCaps.GetCurrentHeadcountForRole(EmployeeRole.Dispatcher);
            float onboardingMin = DispatcherService.GetOnboardingMinutes();
            int globalCapacity = DispatcherService.GetTotalCapacity();

            Log.Info($"[OfficeMM5SmokeTest] Dispatcher status:\n" +
                     $"  Lvl: {lvl}\n" +
                     $"  Headcount: {current}/{cap}\n" +
                     $"  Onboarding minutes: {onboardingMin:F1} min " +
                     $"(0=brak biura, lvl1=30, lvl3=15 baseline, lvl5=7.5)\n" +
                     $"  Global dispatch capacity (M8 sum 50+5×skill): {globalCapacity}");
        }

        [ContextMenu("MM-6: Print employees in Onboarding state")]
        public void PrintOnboardingEmployees()
        {
            long now = (long)RailwayManager.Core.GameState.GameTimeSeconds + RailwayManager.Core.GameState.GameDay * 86400L;
            var sb = new StringBuilder();
            sb.AppendLine("[OfficeMM5SmokeTest] Employees in Onboarding state:");
            int count = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (e.status != EmployeeStatus.Onboarding) continue;
                long remainingSec = e.onboardingFinishGameTime - now;
                float remainingMin = Mathf.Max(0f, remainingSec / 60f);
                sb.AppendLine($"  #{e.employeeId} {e.DisplayFullName} ({e.role}): " +
                              $"remaining {remainingMin:F1} min (finish at {e.onboardingFinishGameTime}s game time)");
                count++;
            }
            if (count == 0) sb.AppendLine("  (brak pracowników w Onboarding)");
            else sb.AppendLine($"  Total: {count}");
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-6: Force trigger ApplyDailyTick (test onboarding routing)")]
        public void ForceDailyTick()
        {
            string today = RailwayManager.Core.GameState.CurrentDateIso;
            ShiftManager.ApplyDailyTick(today);
            Log.Info($"[OfficeMM5SmokeTest] Forced ShiftManager.ApplyDailyTick({today})");
            PrintOnboardingEmployees();
        }

        // ════════════════════════════════════════════════════════
        //  MM-6b — TrafficController DispatchActionService
        // ════════════════════════════════════════════════════════

        [ContextMenu("MM-6b: Print DispatchActionService status")]
        public void PrintDispatchActionStatus()
        {
            int roomLvl = DispatchActionService.GetTrafficControllerRoomLvl();
            int capacity = DispatchActionService.GetTotalActionsCapacity();
            int used = DispatchActionService.GetUsedActions();
            int free = DispatchActionService.GetFreeSlots();
            int pending = DispatchActionService.GetPendingCount();
            bool manual = DispatchActionService.IsManualMode();

            var sb = new StringBuilder();
            sb.AppendLine($"[OfficeMM5SmokeTest] DispatchActionService status:\n" +
                          $"  TrafficController room lvl: {roomLvl}\n" +
                          $"  Headcount: {RoleCaps.FormatHeadcount(EmployeeRole.TrafficController)}\n" +
                          $"  Total actions capacity: {capacity} (sum 1+skill per active dyżurny)\n" +
                          $"  Used: {used}, Free: {free}, Pending: {pending}\n" +
                          $"  Manual mode (MM-D21): {(manual ? "TAK (brak nastawni/dyżurnych)" : "nie")}");

            if (DispatchActionService.Active.Count > 0)
            {
                sb.AppendLine("  Active:");
                foreach (var a in DispatchActionService.Active)
                    sb.AppendLine($"    #{a.dispatchId} vehicle#{a.vehicleId} {a.type} (dispatched {a.dispatchedGameTime}s)");
            }
            if (DispatchActionService.Pending.Count > 0)
            {
                sb.AppendLine("  Pending:");
                foreach (var p in DispatchActionService.Pending)
                    sb.AppendLine($"    #{p.dispatchId} vehicle#{p.vehicleId} {p.type} (requested {p.requestedGameTime}s)");
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-6b: Test TryDispatch (vehicle#1 OUT)")]
        public void TestTryDispatchOut()
        {
            var result = DispatchActionService.TryDispatch(1, DispatchActionType.Out);
            Log.Info($"[OfficeMM5SmokeTest] TryDispatch(vehicle#1, OUT) = {result}");
            PrintDispatchActionStatus();
        }

        [ContextMenu("MM-6b: Test ReleaseAction (vehicle#1 OUT)")]
        public void TestReleaseAction()
        {
            bool ok = DispatchActionService.ReleaseAction(1, DispatchActionType.Out);
            Log.Info($"[OfficeMM5SmokeTest] ReleaseAction(vehicle#1, OUT) = {ok}");
            PrintDispatchActionStatus();
        }

        [ContextMenu("MM-6b: Reset DispatchActionService")]
        public void ResetDispatch()
        {
            DispatchActionService.ResetAll();
            Log.Info("[OfficeMM5SmokeTest] DispatchActionService reset (active + pending cleared)");
        }

        [ContextMenu("MM-6b: Stress test (5 dispatches)")]
        public void StressTestDispatches()
        {
            for (int i = 1; i <= 5; i++)
            {
                var result = DispatchActionService.TryDispatch(i, DispatchActionType.Out);
                Log.Info($"  vehicle#{i} OUT = {result}");
            }
            PrintDispatchActionStatus();
        }

        // ════════════════════════════════════════════════════════
        //  MM-7 — Supervisor/Social/Bathroom morale
        // ════════════════════════════════════════════════════════

        [ContextMenu("MM-7: Print morale bonus breakdown")]
        public void PrintMoraleBreakdown()
        {
            int supLvl = MoraleBonusService.GetSupervisorLvl();
            int socLvl = MoraleBonusService.GetSocialLvl();
            int bathLvl = MoraleBonusService.GetBathroomLvl();

            int supBonus = MoraleBonusService.SupervisorBonusForLvl(supLvl);
            int socBonus = MoraleBonusService.SocialBonusForLvl(socLvl);
            float bathBonus = MoraleBonusService.BathroomBonusForLvl(bathLvl);

            bool socialReachable = MoraleBonusService.AnyRoomReachable(RoomType.Social);
            bool bathroomReachable = MoraleBonusService.AnyRoomReachable(RoomType.Bathroom);

            int effectiveSocial = socialReachable ? socBonus : 0;
            int effectiveBathroom = bathroomReachable ? Mathf.RoundToInt(bathBonus) : 0;
            int total = supBonus + effectiveSocial + effectiveBathroom;

            Log.Info($"[OfficeMM5SmokeTest] MM-7 morale bonus breakdown:\n" +
                     $"  Supervisor lvl {supLvl} → +{supBonus} morale/dzień (globalny)\n" +
                     $"  Social lvl {socLvl} → +{socBonus} morale/dzień (effective: " +
                     $"{(socialReachable ? "+" + effectiveSocial : "0 — brak dojścia/drzwi")})\n" +
                     $"  Bathroom lvl {bathLvl} → +{bathBonus:F1} morale/dzień (effective: " +
                     $"{(bathroomReachable ? "+" + effectiveBathroom : "0 — brak dojścia/drzwi")})\n" +
                     $"  TOTAL per active pracownik: +{total}/dzień");
        }

        [ContextMenu("MM-7: Force daily morale tick")]
        public void ForceMoraleDailyTick()
        {
            string today = RailwayManager.Core.GameState.CurrentDateIso;
            FatigueMoraleTickService.ApplyDailyTick(today);
            Log.Info($"[OfficeMM5SmokeTest] Forced FatigueMoraleTickService.ApplyDailyTick({today})");
        }

        [ContextMenu("MM-7: Print employee morale + breakdown for first 3")]
        public void PrintEmployeeMoraleBreakdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[OfficeMM5SmokeTest] MM-7 employee morale (first 3):");
            int n = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (!e.IsActive) continue;
                if (n >= 3) break;
                var bd = MoraleBonusService.GetBreakdown(e);
                sb.AppendLine($"  #{e.employeeId} {e.DisplayFullName} ({e.role}, morale {e.currentMorale})\n" +
                              $"    Bonus: Supervisor +{bd.supervisor}, Social +{bd.social}, " +
                              $"Bathroom +{bd.bathroom} → total +{bd.total}");
                n++;
            }
            if (n == 0) sb.AppendLine("  (brak active pracowników)");
            Log.Info(sb.ToString());
        }

        // ════════════════════════════════════════════════════════
        //  MM-9 — Outdoor equipment jobs
        // ════════════════════════════════════════════════════════

        [ContextMenu("MM-9: Print active outdoor jobs")]
        public void PrintOutdoorJobs()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[OfficeMM5SmokeTest] MM-9 active outdoor jobs ({RailwayManager.Fleet.OutdoorEquipmentJobService.ActiveJobs.Count}):");
            long now = (long)RailwayManager.Core.GameState.GameTimeSeconds + RailwayManager.Core.GameState.GameDay * 86400L;
            foreach (var j in RailwayManager.Fleet.OutdoorEquipmentJobService.ActiveJobs)
            {
                long remainingSec = j.completionGameTime - now;
                float remainingMin = Mathf.Max(0f, remainingSec / 60f);
                sb.AppendLine($"  #{j.jobId} vehicle#{j.vehicleId} {j.type} @ equipment#{j.equipmentInstanceId}, " +
                              $"remaining {remainingMin:F1} min, cost {j.costGroszy / 100f:F0}zł");
            }
            if (RailwayManager.Fleet.OutdoorEquipmentJobService.ActiveJobs.Count == 0)
                sb.AppendLine("  (brak aktywnych jobs)");
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-9: Test ScheduleWash for vehicle#3 (EN57)")]
        public void TestScheduleWash()
        {
            long now = (long)RailwayManager.Core.GameState.GameTimeSeconds + RailwayManager.Core.GameState.GameDay * 86400L;
            var result = RailwayManager.Fleet.OutdoorEquipmentJobService.ScheduleWash(3, -1, now);
            Log.Info($"[OfficeMM5SmokeTest] ScheduleWash(vehicle#3) = {result}");
            PrintOutdoorJobs();
        }

        [ContextMenu("MM-9: Test ScheduleRefuel for vehicle#9 (SA134 diesel)")]
        public void TestScheduleRefuel()
        {
            long now = (long)RailwayManager.Core.GameState.GameTimeSeconds + RailwayManager.Core.GameState.GameDay * 86400L;
            var result = RailwayManager.Fleet.OutdoorEquipmentJobService.ScheduleRefuel(9, -1, now);
            Log.Info($"[OfficeMM5SmokeTest] ScheduleRefuel(vehicle#9 SA134) = {result}");
            PrintOutdoorJobs();
        }

        [ContextMenu("MM-9: Test ScheduleRefuel for vehicle#1 (EU07 electric — should fail)")]
        public void TestScheduleRefuelElectric()
        {
            long now = (long)RailwayManager.Core.GameState.GameTimeSeconds + RailwayManager.Core.GameState.GameDay * 86400L;
            var result = RailwayManager.Fleet.OutdoorEquipmentJobService.ScheduleRefuel(1, -1, now);
            Log.Info($"[OfficeMM5SmokeTest] ScheduleRefuel(vehicle#1 EU07 electric) = {result} " +
                     "(should fail — RequiresFuel=false → IncompatibleVehicle)");
        }

        // ════════════════════════════════════════════════════════
        //  MM-10 — Modernizacje pojazdów
        // ════════════════════════════════════════════════════════

        [ContextMenu("MM-10: Print modernization paths catalog")]
        public void PrintModernizationCatalog()
        {
            RailwayManager.Fleet.ModernizationPathCatalog.LoadAll();
            var sb = new StringBuilder();
            sb.AppendLine($"[OfficeMM5SmokeTest] Modernization paths " +
                          $"({RailwayManager.Fleet.ModernizationPathCatalog.GetAll().Count}):");
            foreach (var p in RailwayManager.Fleet.ModernizationPathCatalog.GetAll())
            {
                sb.AppendLine($"  {p.pathId}: {p.displayName}");
                sb.AppendLine($"    {p.sourceSeriesId} → {p.targetSeriesId}, {p.durationDays}d");
                sb.AppendLine($"    External {p.externalCostPln / 1_000_000f:F1}M zł / Internal {p.internalCostPln / 1_000_000f:F1}M zł");
                sb.AppendLine($"    Hall lvl ≥{p.minHallLevelInternal}, ServicePit ≥{p.minServicePitLength}m");
                sb.AppendLine($"    Target: vmax {p.newMaxSpeedKmh} km/h, power {p.newPowerKw} kW, comfort {p.newComfortClass}");
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-10: Print active modernization jobs")]
        public void PrintModernizationJobs()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[OfficeMM5SmokeTest] Active modernization jobs " +
                          $"({RailwayManager.Fleet.ModernizationJobService.ActiveJobs.Count}):");
            long now = (long)RailwayManager.Core.GameState.GameTimeSeconds + RailwayManager.Core.GameState.GameDay * 86400L;
            foreach (var j in RailwayManager.Fleet.ModernizationJobService.ActiveJobs)
            {
                long remainingSec = j.completionGameTime - now;
                float remainingDays = Mathf.Max(0f, remainingSec / 86400f);
                sb.AppendLine($"  #{j.jobId} vehicle#{j.vehicleId} {j.pathId} ({j.mode}), " +
                              $"remaining {remainingDays:F1}d, cost {j.costPlnTotal / 1_000_000f:F1}M zł");
            }
            if (RailwayManager.Fleet.ModernizationJobService.ActiveJobs.Count == 0)
                sb.AppendLine("  (brak aktywnych jobs)");
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-10: Test ScheduleExternal EN57→Ryba (vehicle#3 ZNTK_NEWAG)")]
        public void TestScheduleExternalEN57()
        {
            long now = (long)RailwayManager.Core.GameState.GameTimeSeconds + RailwayManager.Core.GameState.GameDay * 86400L;
            var result = RailwayManager.Fleet.ModernizationJobService.ScheduleExternal(
                3, "EN57_to_Ryba", "ZNTK_NEWAG", now);
            Log.Info($"[OfficeMM5SmokeTest] ScheduleExternal EN57→Ryba (vehicle#3) = {result}");
            PrintModernizationJobs();
        }

        [ContextMenu("MM-10: Test ScheduleInternal EN57→Ryba (vehicle#3, fake Hall lvl5 + 65m pit)")]
        public void TestScheduleInternalEN57()
        {
            long now = (long)RailwayManager.Core.GameState.GameTimeSeconds + RailwayManager.Core.GameState.GameDay * 86400L;
            // Test: forced parameters (servicePitInstanceId=99, length=65, hallLvl=5)
            var result = RailwayManager.Fleet.ModernizationJobService.ScheduleInternal(
                3, "EN57_to_Ryba", servicePitInstanceId: 99, servicePitMaxLength: 65f, hallLvl: 5, now);
            Log.Info($"[OfficeMM5SmokeTest] ScheduleInternal EN57→Ryba (vehicle#3, faked params) = {result}");
            PrintModernizationJobs();
        }

        // ════════════════════════════════════════════════════════
        //  MM-11 — Modyfikacje pojazdów
        // ════════════════════════════════════════════════════════

        [ContextMenu("MM-11: Print modifications catalog")]
        public void PrintModificationsCatalog()
        {
            RailwayManager.Fleet.VehicleModificationCatalog.LoadAll();
            var sb = new StringBuilder();
            sb.AppendLine($"[OfficeMM5SmokeTest] Vehicle modifications " +
                          $"({RailwayManager.Fleet.VehicleModificationCatalog.GetAll().Count}):");
            foreach (var m in RailwayManager.Fleet.VehicleModificationCatalog.GetAll())
            {
                sb.AppendLine($"  {m.modId} ({m.type}): {m.displayName}");
                sb.AppendLine($"    {m.durationDays}d, External {m.externalCostPln / 1_000_000f:F2}M / Internal {m.internalCostPln / 1_000_000f:F2}M zł");
                sb.AppendLine($"    Hall lvl ≥{m.minHallLevelInternal}");
                if (m.applicableVehicleTypes != null && m.applicableVehicleTypes.Length > 0)
                    sb.AppendLine($"    Types: {string.Join(",", m.applicableVehicleTypes)}");
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-11: Print applicable mods for vehicle#5 (111A wagon)")]
        public void PrintApplicableForWagon()
        {
            var v = RailwayManager.Fleet.FleetService.GetOwnedById(5);
            if (v == null) { Log.Warn("[OfficeMM5SmokeTest] vehicle#5 not found"); return; }
            var applicable = RailwayManager.Fleet.VehicleModificationCatalog.GetApplicableFor(v);
            var sb = new StringBuilder();
            sb.AppendLine($"[OfficeMM5SmokeTest] Applicable modifications for vehicle#5 ({v.series}, type={v.type}):");
            foreach (var m in applicable)
                sb.AppendLine($"  - {m.modId}: {m.displayName}");
            if (applicable.Count == 0) sb.AppendLine("  (brak applicable — sprawdź type / current bogie)");
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-11: Print active modification jobs")]
        public void PrintModificationJobs()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[OfficeMM5SmokeTest] Active modification jobs " +
                          $"({RailwayManager.Fleet.VehicleModificationJobService.ActiveJobs.Count}):");
            long now = (long)RailwayManager.Core.GameState.GameTimeSeconds + RailwayManager.Core.GameState.GameDay * 86400L;
            foreach (var j in RailwayManager.Fleet.VehicleModificationJobService.ActiveJobs)
            {
                long remainingSec = j.completionGameTime - now;
                float remainingDays = Mathf.Max(0f, remainingSec / 86400f);
                sb.AppendLine($"  #{j.jobId} vehicle#{j.vehicleId} {j.modId} ({j.mode}), " +
                              $"remaining {remainingDays:F1}d, cost {j.costPlnTotal / 1_000_000f:F2}M zł");
            }
            if (RailwayManager.Fleet.VehicleModificationJobService.ActiveJobs.Count == 0)
                sb.AppendLine("  (brak aktywnych jobs)");
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-11: Test ScheduleInternal AC for vehicle#3 (EN57)")]
        public void TestScheduleACInternal()
        {
            long now = (long)RailwayManager.Core.GameState.GameTimeSeconds + RailwayManager.Core.GameState.GameDay * 86400L;
            var result = RailwayManager.Fleet.VehicleModificationJobService.ScheduleInternal(
                3, "comfort_air_conditioning", servicePitInstanceId: 99,
                servicePitMaxLength: 65f, hallLvl: 1, now);
            Log.Info($"[OfficeMM5SmokeTest] ScheduleInternal AC (vehicle#3) = {result}");
            PrintModificationJobs();
        }

        // ════════════════════════════════════════════════════════
        //  MM-12 — PaintBay self-paint
        // ════════════════════════════════════════════════════════

        [ContextMenu("MM-12: Print self-paint times per Hall lvl")]
        public void PrintSelfPaintTimes()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[OfficeMM5SmokeTest] MM-12 self-paint times (Hall lvl → days):");
            for (int lvl = 1; lvl <= 5; lvl++)
            {
                int days = RailwayManager.Fleet.SelfPaintingService.GetPaintTimeDays(lvl);
                sb.AppendLine($"  Hall lvl {lvl}: {(days > 0 ? days + "d" : "niedostępne (wymaga ≥2)")}");
            }
            sb.AppendLine($"  Cost (placeholder): {RailwayManager.Fleet.SelfPaintingService.BasePaintCostPln / 1000f:F0}k zł");
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-12: Print active self-paint jobs")]
        public void PrintSelfPaintJobs()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[OfficeMM5SmokeTest] Active self-paint jobs " +
                          $"({RailwayManager.Fleet.SelfPaintingService.ActiveJobs.Count}):");
            long now = (long)RailwayManager.Core.GameState.GameTimeSeconds + RailwayManager.Core.GameState.GameDay * 86400L;
            foreach (var j in RailwayManager.Fleet.SelfPaintingService.ActiveJobs)
            {
                long remainingSec = j.completionGameTime - now;
                float remainingDays = Mathf.Max(0f, remainingSec / 86400f);
                sb.AppendLine($"  #{j.jobId} vehicle#{j.vehicleId} @ paint_bay#{j.paintBayInstanceId}, " +
                              $"remaining {remainingDays:F1}d, cost {j.costPln / 1000f:F0}k zł");
            }
            if (RailwayManager.Fleet.SelfPaintingService.ActiveJobs.Count == 0)
                sb.AppendLine("  (brak aktywnych jobs)");
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-12: Test Schedule self-paint vehicle#3 (Hall lvl 3)")]
        public void TestScheduleSelfPaint()
        {
            long now = (long)RailwayManager.Core.GameState.GameTimeSeconds + RailwayManager.Core.GameState.GameDay * 86400L;
            var result = RailwayManager.Fleet.SelfPaintingService.Schedule(
                vehicleId: 3,
                paintBayInstanceId: 99,
                newPaint: null,  // null = refresh aktualnego
                hallLvl: 3,
                now);
            Log.Info($"[OfficeMM5SmokeTest] Schedule self-paint vehicle#3 (Hall lvl 3) = {result}");
            PrintSelfPaintJobs();
        }

        // ════════════════════════════════════════════════════════
        //  MM-13 — Manual dispatch + researcher at desk + wash worker presence
        // ════════════════════════════════════════════════════════

        [ContextMenu("MM-13: Test ManualDispatch vehicle#1 OUT (bypass capacity)")]
        public void TestManualDispatch()
        {
            bool ok = DispatchActionService.ManualDispatch(1, DispatchActionType.Out);
            Log.Info($"[OfficeMM5SmokeTest] ManualDispatch(vehicle#1, OUT) = {ok}");
            PrintDispatchActionStatus();
        }

        [ContextMenu("MM-13: Print researcher at desks vs total qualified")]
        public void PrintResearcherAtDesksDiagnostic()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[OfficeMM5SmokeTest] MM-13 researcher at desks diagnostic:");
            for (int minSkill = 1; minSkill <= 5; minSkill++)
            {
                int total = 0, atDesks = 0;
                foreach (var e in PersonnelService.Employees)
                {
                    if (e.role != EmployeeRole.Research) continue;
                    if (!e.IsActive) continue;
                    if (e.status != EmployeeStatus.OnShift && e.status != EmployeeStatus.Available) continue;
                    if (e.skill < minSkill) continue;
                    total++;
                    if (e.assignedFurnitureId >= 0) atDesks++;
                }
                if (total == 0) continue;
                sb.AppendLine($"  Skill ≥{minSkill}★: {atDesks}/{total} researcherów przy biurku");
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-13: Test wash worker presence check")]
        public void TestWashWorkerPresence()
        {
            bool hasWorker = CleaningService.HasActiveWashBayWorker();
            Log.Info($"[OfficeMM5SmokeTest] HasActiveWashBayWorker = {hasWorker} " +
                     $"(jeśli brak — ScheduleWash w OutdoorEquipmentJobService się odrzuci)");
        }
    }
}
