using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-14 / §3.4 + §3.5: Auto-assign sprzataczy i myjni do pojazdow w depocie.
    ///
    /// Flow (daily tick):
    /// 1. Per pojazd w <see cref="VehicleLocationType.InDepot"/>:
    ///    - Jesli <see cref="FleetVehicleData.cleanlinessPercent"/> &lt; <see cref="CleanThreshold"/>
    ///      i <see cref="AutoCleaningEnabled"/> ON → sprawdz czy sprzatacz dostepny
    ///    - Jesli tak: restore cleanliness do 100, koszt pensji (juz w PayrollService)
    /// 2. Myjnia (WashBay): analogicznie ale wymaga pomieszczenia RoomType.WashBay
    ///    (M8-14 placeholder — w EA tylko logika, full gdy RoomType enum ma WashBay)
    ///
    /// Wplyw cleanliness (M6 Ekonomia hook, w M8-14 placeholder):
    /// - &lt; 50% → comfort -5%, reputation -1/kurs
    /// - 100% → bonus comfort +2, reputation +1/kurs (wymaga myjni)
    ///
    /// Auto-cleaning toggle — gracz moze wylaczyc (osczednosc pensji sprzataczy).
    /// </summary>
    public class CleaningService : MonoBehaviour
    {
        public static CleaningService Instance { get; private set; }

        /// <summary>Feature toggle — default ON (automatyka). Player moze wylaczyc w settings.</summary>
        public static bool AutoCleaningEnabled { get; set; } = true;

        /// <summary>Feature toggle dla myjni. Default ON.</summary>
        public static bool AutoWashingEnabled { get; set; } = true;

        /// <summary>Prog cleanliness ponizej ktorego auto-assign sprzataczy (default 50%).</summary>
        public const float CleanThreshold = 50f;

        /// <summary>Target cleanliness po czyszczeniu (default 100%).</summary>
        public const float CleanTarget = 100f;

        /// <summary>Degradacja cleanliness per km (dodatek do naturalnej degradacji z M7).</summary>
        public const float CleanlinessDegradationPerKm = 0.0003f;

        public static event Action<int> OnVehicleCleaned;
        public static event Action<int> OnVehicleWashed;

        public static CleaningService EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("CleaningService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CleaningService>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // MM-13: install hook dla OutdoorEquipmentJobService.ScheduleWash —
            // wymaga active WashBay worker (OnShift/Available). Personnel referuje Fleet,
            // Fleet nie referuje Personnel → hook-based DI.
            RailwayManager.Fleet.OutdoorEquipmentJobService.WashBayWorkerPresenceHook =
                () => HasActiveWashBayWorker();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                // MM-13: uninstall hook
                RailwayManager.Fleet.OutdoorEquipmentJobService.WashBayWorkerPresenceHook = null;
            }
        }

        /// <summary>MM-13: czy jest co najmniej jeden active WashBay worker.</summary>
        public static bool HasActiveWashBayWorker()
        {
            foreach (var e in PersonnelService.Employees)
            {
                if (e == null) continue;
                if (e.role != EmployeeRole.WashBay) continue;
                if (e.status != EmployeeStatus.OnShift && e.status != EmployeeStatus.Available) continue;
                return true;
            }
            return false;
        }

        // ═══ Daily tick ═══

        /// <summary>
        /// Wywolane z <see cref="PersonnelDailyScheduler"/> (lub per-vehicle event gdy M9c entering depot).
        ///
        /// <para><b>TD-025 change:</b> NIE robi instant clean wszystkich brudnych pojazdow.
        /// Cleaning to teraz realny workflow przez <see cref="Workflows.CleanerWorkflow"/> —
        /// pracownik idzie do pojazdu i 60s pracuje. Daily tick odpowiada tylko za auto-wash
        /// (cleanlinessPercent fix BUG-049) i fallback gdy gracz nie ma sprzataczy.</para>
        /// </summary>
        public static void ApplyDailyTick(string dateIso)
        {
            if (Instance == null) return;
            var locSvc = VehicleLocationService.Instance;
            if (locSvc == null) return;

            int washed = 0, fallbackCleaned = 0;

            bool hasCleaner = HasAvailableCleaner();

            foreach (var rec in locSvc.GetInDepot())
            {
                var v = FleetService.GetOwnedById(rec.vehicleId);
                if (v == null) continue;

                // TD-025: fallback dla graczy bez Cleaner'ow.
                // Brak active cleanera + AutoCleaningEnabled → instant clean tak jak przed TD-025.
                // Z cleanerem: workflow tickuje sam, daily tick nie ingeruje.
                if (AutoCleaningEnabled && v.cleanlinessPercent < CleanThreshold && !hasCleaner)
                {
                    v.cleanlinessPercent = CleanTarget;
                    OnVehicleCleaned?.Invoke(v.id);
                    fallbackCleaned++;
                }

                // BUG-049 fix: wash dotyczy `cleanlinessPercent` (osobny exterior field),
                // NIE `conditionPercent` (agregat 12 komponentów) który jest nadpisywany przez
                // DegradationService.ApplyToVehicle:97 → wash bonus znikał przy następnym tick'u.
                // Trigger: cleanlinessPercent < 80 (auto-wash gdy brudny).
                if (AutoWashingEnabled && v.cleanlinessPercent < 80f && HasAvailableWashWorker())
                {
                    v.cleanlinessPercent = Math.Min(100f, v.cleanlinessPercent + 20f);
                    OnVehicleWashed?.Invoke(v.id);
                    washed++;
                }
            }

            if (washed > 0 || fallbackCleaned > 0)
                Log.Info($"[CleaningService] Tick {dateIso}: fallbackCleaned={fallbackCleaned} (no cleaner), washed={washed}");
        }

        /// <summary>
        /// TD-025: Wywolane przez <see cref="Workflows.CleanerWorkflow"/> po 60s timera
        /// (pracownik dotarl do pojazdu i ukonczyl czyszczenie). Aktualizuje
        /// <c>cleanlinessPercent = 100</c> i emit event.
        /// </summary>
        public static void CompleteCleaning(int vehicleId)
        {
            if (vehicleId < 0) return;
            var v = FleetService.GetOwnedById(vehicleId);
            if (v == null) return;
            v.cleanlinessPercent = CleanTarget;
            OnVehicleCleaned?.Invoke(vehicleId);
        }

        static bool HasAvailableCleaner()
        {
            foreach (var e in PersonnelService.Employees)
            {
                if (e.role != EmployeeRole.Cleaner) continue;
                if (!e.IsActive) continue;
                if (e.status == EmployeeStatus.Sick || e.status == EmployeeStatus.LongSick) continue;
                if (e.status == EmployeeStatus.OnShift || e.status == EmployeeStatus.Available)
                    return true;
            }
            return false;
        }

        static bool HasAvailableWashWorker()
        {
            foreach (var e in PersonnelService.Employees)
            {
                if (e.role != EmployeeRole.WashBay) continue;
                if (!e.IsActive) continue;
                if (e.status == EmployeeStatus.Sick || e.status == EmployeeStatus.LongSick) continue;
                if (e.status == EmployeeStatus.OnShift || e.status == EmployeeStatus.Available)
                    return true;
            }
            return false;
        }

        // ═══ Per-km degradation ═══

        /// <summary>
        /// Wywolac z <see cref="DegradationService"/> hook przy aktualizacji mileage.
        /// Cleanliness spada niezaleznie od conditionPercent (lekki dodatek, 0.03% per km).
        /// </summary>
        public static void ApplyPerKmDegradation(FleetVehicleData v, float deltaKm)
        {
            if (v == null || deltaKm <= 0f) return;
            v.cleanlinessPercent = Mathf.Max(0f, v.cleanlinessPercent - deltaKm * CleanlinessDegradationPerKm * 100f);
        }

        // ═══ Comfort / reputation multipliers (M6 hook) ═══

        /// <summary>
        /// M6 Economy hook: modifier comfort wg cleanliness.
        /// &lt;50%: -5% comfort; &gt;=80%: +2% comfort.
        /// </summary>
        public static float GetComfortMultiplier(FleetVehicleData v)
        {
            if (v == null) return 1f;
            if (v.cleanlinessPercent < 50f) return 0.95f;
            if (v.cleanlinessPercent >= 80f) return 1.02f;
            return 1f;
        }

        /// <summary>
        /// M6 Economy hook: modifier reputation per kurs.
        /// &lt;50% cleanliness: -1 rep; &gt;=90%: +1 rep (myta).
        /// </summary>
        public static int GetReputationDelta(FleetVehicleData v)
        {
            if (v == null) return 0;
            if (v.cleanlinessPercent < 50f) return -1;
            if (v.cleanlinessPercent >= 90f && v.conditionPercent >= 80f) return 1;
            return 0;
        }

        // ═══ Debug ═══

        [ContextMenu("Debug: Toggle auto-cleaning")]
        public void DebugToggleCleaning()
        {
            AutoCleaningEnabled = !AutoCleaningEnabled;
            Log.Info($"[CleaningService] AutoCleaningEnabled = {AutoCleaningEnabled}");
        }

        [ContextMenu("Debug: Toggle auto-washing")]
        public void DebugToggleWashing()
        {
            AutoWashingEnabled = !AutoWashingEnabled;
            Log.Info($"[CleaningService] AutoWashingEnabled = {AutoWashingEnabled}");
        }

        [ContextMenu("Debug: Force daily tick")]
        public void DebugForceTick()
        {
            ApplyDailyTick(GameState.CurrentDateIso);
        }

        [ContextMenu("Debug: Dirty all depot vehicles (test)")]
        public void DebugDirtyAll()
        {
            var locSvc = VehicleLocationService.Instance;
            if (locSvc == null) return;
            int count = 0;
            foreach (var rec in locSvc.GetInDepot())
            {
                var v = FleetService.GetOwnedById(rec.vehicleId);
                if (v != null)
                {
                    v.cleanlinessPercent = 30f;
                    count++;
                }
            }
            Log.Info($"[CleaningService] Dirtied {count} vehicles in depot (cleanliness=30)");
        }

        [ContextMenu("Debug: Report cleanliness")]
        public void DebugReport()
        {
            var locSvc = VehicleLocationService.Instance;
            if (locSvc == null) { Log.Warn("[CleaningService] No VehicleLocationService"); return; }
            int total = 0, dirty = 0, clean = 0;
            foreach (var rec in locSvc.GetInDepot())
            {
                var v = FleetService.GetOwnedById(rec.vehicleId);
                if (v == null) continue;
                total++;
                if (v.cleanlinessPercent < 50) dirty++; else if (v.cleanlinessPercent >= 90) clean++;
            }
            int cleaners = PersonnelService.CountActiveByRole(EmployeeRole.Cleaner);
            int wash = PersonnelService.CountActiveByRole(EmployeeRole.WashBay);
            Log.Info($"[CleaningService] Depot vehicles: {total}, dirty(<50%): {dirty}, clean(>=90%): {clean}");
            Log.Info($"[CleaningService] Personnel: Cleaners={cleaners}, WashBay={wash}, " +
                     $"AutoClean={AutoCleaningEnabled}, AutoWash={AutoWashingEnabled}");
        }
    }
}
