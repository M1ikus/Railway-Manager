using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Maintenance.Movement;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// MM-18f / MM-18g — installer hook'ów dla movement walidacji w bridge'u
    /// (<see cref="OutdoorEquipmentMovementBridge"/>).
    ///
    /// <list type="bullet">
    /// <item><b>DriverAvailableHook</b> (MM-18f): walidacja maszynisty. MVP: wystarczy
    /// jakiś active driver w firmie (luźno, bez per-vehicle assignment). Pełne
    /// per-vehicle crew assignment z grafików = post-EA.</item>
    /// <item><b>TrafficControllerAcceptHook</b> (MM-18g): TC capacity check + workload
    /// accounting via <see cref="DispatchActionService.TryDispatch"/>. Brak TC →
    /// fallback do legacy (manual dispatch).</item>
    /// </list>
    ///
    /// Asmdef: Personnel widzi Timetable (Bridge tam siedzi). Hooks set'owane w
    /// AfterSceneLoad + przed pierwszym wywołaniem Schedule (race-safe bo Schedule
    /// jest wywoływane z UI po user input, więc po Awake+Start).
    /// </summary>
    public static class MM18MovementHooksInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            OutdoorEquipmentMovementBridge.DriverAvailableHook = HasAvailableDriver;
            OutdoorEquipmentMovementBridge.TrafficControllerAcceptHook = TryAcceptDispatchAction;
            Log.Info("[MM18MovementHooksInstaller] Hooks installed (DriverAvailable + TC Accept)");
        }

        // ── Driver check ─────────────────────────────────────────────

        /// <summary>
        /// MM-18f MVP: wystarczy 1+ active driver w firmie. Per-vehicle crew assignment
        /// z grafików (decyzja user'a "powiązanie z grafikami pracowniczymi") =
        /// post-EA polish — wymaga rozszerzenia <c>CrewAssignmentService</c> żeby
        /// ad-hoc service moves też mogły wziąć driver'a z puli.
        /// </summary>
        static bool HasAvailableDriver(int vehicleId)
        {
            int active = PersonnelService.CountActiveByRole(EmployeeRole.Driver);
            return active > 0;
        }

        // ── TC dispatch accept ──────────────────────────────────────

        /// <summary>
        /// MM-18g: map action type name → DispatchActionType + TryDispatch. Returns true
        /// gdy TC zaakceptował (Dispatched/Queued — slot zarezerwowany), false gdy
        /// RequiresManual/AlreadyDispatched (gracz musi sam wysłać lub duplikat).
        ///
        /// Bridge fallback'uje do legacy gdy false — pojazd przechodzi w Servicing
        /// immediate bez konsumpcji TC slota (jako compromise w MVP — manual dispatch
        /// nie konsumuje TC slota, tak jak postanowione w MM-D21).
        /// </summary>
        static bool TryAcceptDispatchAction(int vehicleId, string actionTypeName)
        {
            if (!TryParseActionType(actionTypeName, out var type)) return true; // unknown → don't block

            var result = DispatchActionService.TryDispatch(vehicleId, type);
            return result == DispatchActionService.DispatchResult.Dispatched
                || result == DispatchActionService.DispatchResult.Queued;
        }

        static bool TryParseActionType(string name, out DispatchActionType type)
        {
            switch (name)
            {
                case "Wash": type = DispatchActionType.Wash; return true;
                case "WaterService": type = DispatchActionType.WaterService; return true;
                case "Refuel": type = DispatchActionType.Refuel; return true;
                case "Rotate": type = DispatchActionType.Rotate; return true;
                case "PitLiftMaint":
                case "PitLift": type = DispatchActionType.PitLift; return true;
                case "Modernization": type = DispatchActionType.Modernization; return true;
                case "VehicleModification":
                case "Modification": type = DispatchActionType.Modification; return true;
                case "SelfPaint": type = DispatchActionType.SelfPaint; return true;
                case "Workshop": type = DispatchActionType.Workshop; return true;
                case "Out": type = DispatchActionType.Out; return true;
                default:
                    type = DispatchActionType.Out;
                    return false;
            }
        }
    }
}
