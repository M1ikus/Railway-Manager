using System;
using UnityEngine;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// M8-5: Master daily tick orchestrator dla Personnel. Subskrybuje
    /// <see cref="GameState.OnDayEnded"/> i wywoluje 3 serwisy w ustalonej kolejnosci:
    ///
    /// 1. <see cref="ShiftManager.ApplyDailyTick"/> — ustaw status/shift per pracownik wg schedule
    /// 2. <see cref="FatigueMoraleTickService.ApplyDailyTick"/> — fatigue+morale delta (uzywa statusu z #1)
    /// 3. <see cref="PayrollService.ApplyDailyTick"/> — monthly payroll (tylko day=1)
    ///
    /// Bootstrap: <see cref="EnsureExists"/> z <see cref="PersonnelServiceBootstrap"/>.
    ///
    /// Deterministyczna kolejnosc wykonywania gwarantowana tylko poprzez ten scheduler —
    /// osobne subskrypcje do OnDayEnded mialyby undefined order.
    /// </summary>
    public class PersonnelDailyScheduler : MonoBehaviour
    {
        public static PersonnelDailyScheduler Instance { get; private set; }

        public static PersonnelDailyScheduler EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PersonnelDailyScheduler");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<PersonnelDailyScheduler>();
            return Instance;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Log.Info("[PersonnelDailyScheduler] Bootstrapped");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable() { GameState.OnDayEnded += OnDayEnded; }
        void OnDisable() { GameState.OnDayEnded -= OnDayEnded; }

        void OnDayEnded(string dateIsoJustEnded)
        {
            // Uwaga: OnDayEnded przekazuje date dnia ktory wlasnie sie skonczyl.
            // GameState.CurrentDateIso juz wskazuje na NOWY dzien (GameDay post-increment).
            string newDateIso = GameState.CurrentDateIso;

            // 1. Fatigue + morale delta za dzien ktory wlasnie minal (status pracownika = status wczoraj)
            FatigueMoraleTickService.ApplyDailyTick(dateIsoJustEnded);

            // 2. M8-6: Retirement check (age update + 30-day notice + actual retirement)
            RetirementService.ApplyDailyTick(newDateIso);

            // 3. M8-6: Sick leave roll (probability + recovery detection)
            SickLeaveService.ApplyDailyTick(newDateIso);

            // 4. Oblicz nowy status per pracownik na dzisiaj (nowy dzien, po retirement/sick changes)
            ShiftManager.ApplyDailyTick(newDateIso);

            // 4b. Rebuild OnShift cache po mass status update (tańsze niż 200 eventów per dzień).
            // Konieczne przed ResolveAllForDay/FixedUpdate, które iterują PersonnelService.OnShiftAgents.
            PersonnelService.RebuildOnShiftCache();

            // 5. M8-7: Dispatcher tick — process Delayed pending actions
            DispatcherService.ApplyDailyTick(newDateIso);

            // 6. M8-10: Dispatcher3D — spawn/despawn visuals wg nowego statusu
            PersonnelDispatcher3D.Instance?.ResolveAllForDay();

            // 7. M8-14: Cleaning + washing — auto-assign sprzataczy/myjni dla pojazdow w depocie
            CleaningService.ApplyDailyTick(newDateIso);

            // 8. M8-15: Research — daily progress (if active + requirements met)
            ResearchService.ApplyDailyTick(newDateIso);

            // 9. Payroll — pierwszy dzien miesiaca (sprawdz newDateIso bo teraz jestesmy w nowym dniu)
            PayrollService.ApplyDailyTick(newDateIso);
        }

        // ═══ Debug ContextMenu ═══

        [ContextMenu("Debug: Force daily tick now")]
        public void DebugForceTick()
        {
            OnDayEnded(GameState.CurrentDateIso);
        }

        [ContextMenu("Debug: Force monthly payroll now")]
        public void DebugForcePayroll()
        {
            try
            {
                var date = IsoTime.ParseDate(GameState.CurrentDateIso);
                PayrollService.PayAll(date);
            }
            catch (Exception ex)
            {
                Log.Error($"[PersonnelDailyScheduler] DebugForcePayroll failed: {ex.Message}");
            }
        }

        [ContextMenu("Debug: Simulate 7 days (accelerated)")]
        public void DebugSimulate7Days()
        {
            try
            {
                for (int i = 0; i < 7; i++)
                {
                    var today = IsoTime.ParseDate(GameState.CurrentDateIso);
                    GameState.GameDay++;
                    var newDate = IsoTime.ParseDate(GameState.CurrentDateIso);
                    OnDayEnded(today.ToString("yyyy-MM-dd"));
                    Log.Info($"[PersonnelDailyScheduler] Simulated day {today:yyyy-MM-dd} → {newDate:yyyy-MM-dd}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[PersonnelDailyScheduler] DebugSimulate7Days failed: {ex.Message}");
            }
        }

        [ContextMenu("Debug: Report payroll summary")]
        public void DebugPayrollSummary()
        {
            int total = PayrollService.EstimateMonthlyTotalGroszy();
            Log.Info($"[PayrollService] Estimated monthly total: {total / 100} zł");

            var byRole = PayrollService.EstimateMonthlyByRoleGroszy();
            foreach (var kv in byRole)
            {
                if (kv.Value == 0) continue;
                Log.Info($"  {RoleDefinitions.GetDisplayNamePl(kv.Key),-20}: {kv.Value / 100,8} zł");
            }
        }
    }
}
