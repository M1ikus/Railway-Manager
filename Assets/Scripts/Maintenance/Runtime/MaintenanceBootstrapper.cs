using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.Maintenance
{
    /// <summary>
    /// M-Maintenance asmdef split (2026-05-15): bootstrap singletonów Maintenance po pełnym
    /// Awake <see cref="TrainRunSimulator"/>. Wcześniej te <c>EnsureExists()</c> wywołania
    /// siedziały bezpośrednio w <see cref="TrainRunSimulator.Awake"/> — wymagało to żeby
    /// Timetable asmdef referował Maintenance, co tworzyło cykl (Maintenance referuje Timetable
    /// dla <c>TrainRun</c>/<c>SimulatedTrain</c>/<c>TrainRunSimulator</c> hook events).
    ///
    /// Wzorzec inwersji:
    /// <list type="bullet">
    /// <item><see cref="TrainRunSimulator"/> definiuje <see cref="TrainRunSimulator.OnSimulatorBootstrapped"/>
    /// static event emit'owany na końcu Awake.</item>
    /// <item>Maintenance asmdef subskrybuje przez <c>[RuntimeInitializeOnLoadMethod]</c>
    /// (early, przed BeforeSceneLoad) — bootstrap fires zaraz po Awake Simulatora.</item>
    /// </list>
    ///
    /// Idempotent — wszystkie <c>EnsureExists</c> mają guard <c>if (Instance != null) return</c>.
    /// </summary>
    public static class MaintenanceBootstrapper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterEarlyHook()
        {
            TrainRunSimulator.OnSimulatorBootstrapped += BootstrapMaintenanceServices;

            // Defensive: gdy domain reload nastąpił PO Awake simulatora (rzadkie w edytorze,
            // ale możliwe przy hot reload skryptów). EnsureExists jest idempotent.
            if (TrainRunSimulator.Instance != null)
                BootstrapMaintenanceServices();
        }

        private static void BootstrapMaintenanceServices()
        {
            // M7-3b: RescueDispatchUI — pokazuje się gdy pociąg w AwaitingRescue
            RescueDispatchUI.EnsureExists();

            // M7-3c: RescueService — pathfinding + ETA-based rescue execution
            RescueService.EnsureExists();

            // M7-4: WorkshopManager — wykonywanie przeglądów w halach depot
            WorkshopManager.EnsureExists();

            // M7-4: WorkshopsPanelUI — panel "Warsztaty" (UIIntent.OpenWorkshopsPanel)
            WorkshopsPanelUI.EnsureExists();

            // M7-6: PartInventoryService — magazyn części (zamówienia, dostawa OnDayEnded)
            PartInventoryService.EnsureExists();

            // M7-6: PartsPanelUI — panel "Magazyn" (UIIntent.OpenPartsPanel)
            PartsPanelUI.EnsureExists();

            // M7-7: MaintenanceAlertsUI — floating badges (overdue / awarie / brak części)
            MaintenanceAlertsUI.EnsureExists();

            Log.Info("[MaintenanceBootstrapper] 7 services + UI bootstrapped");
        }
    }
}
