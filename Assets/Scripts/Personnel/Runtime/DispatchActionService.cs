using System;
using System.Collections.Generic;
using DepotSystem;
using DepotSystem.RoomLevel;
using RailwayManager;
using RailwayManager.Core;

namespace RailwayManager.Personnel
{
    /// <summary>
    /// MM-6b / MM-D12 — typ akcji dyspozytorskiej kontrolowanej przez TrafficController.
    /// </summary>
    public enum DispatchActionType
    {
        /// <summary>Wysłanie pojazdu z zajezdni na rozkład (mapa świata, kurs TrainRun).</summary>
        Out,
        /// <summary>Pojazd jedzie do FuelStation/fuel_pump na tankowanie (MM-D14, MM-9).</summary>
        Refuel,
        /// <summary>Pojazd jedzie do ServicePit na przegląd P1-P5 (M7).</summary>
        Workshop,
        /// <summary>Pojazd jedzie do ServicePit large w Hall lvl5 na modernizację (MM-D13).</summary>
        Modernization,
        /// <summary>MM-18g: pojazd jedzie do WashZone/wash_gate na mycie.</summary>
        Wash,
        /// <summary>MM-18g: pojazd jedzie do WaterService/water_service na wodowanie (MM-17).</summary>
        WaterService,
        /// <summary>MM-18g: pojazd jedzie do Turntable na obrót.</summary>
        Rotate,
        /// <summary>MM-18g: pojazd jedzie do PitLift na quick maintenance.</summary>
        PitLift,
        /// <summary>MM-18g: pojazd jedzie do paint_bay na self-paint (MM-12).</summary>
        SelfPaint,
        /// <summary>MM-18g: pojazd jedzie do ServicePit na modyfikację (MM-11 Internal).</summary>
        Modification,
    }

    /// <summary>
    /// MM-6b — pojedynczy wpis kolejki/aktywnych dispatch'ów. Lekka POCO, brak runtime
    /// state pojazdu (vehicleId jako klucz, lookup w FleetService.GetOwnedById).
    /// </summary>
    [Serializable]
    public class DispatchActionEntry
    {
        public int dispatchId;
        public int vehicleId;
        public DispatchActionType type;
        public long requestedGameTime;
        public long dispatchedGameTime;  // 0 gdy pending

        public bool IsPending => dispatchedGameTime <= 0;
        public bool IsActive => dispatchedGameTime > 0;
    }

    /// <summary>
    /// MM-6b / MM-D12 — limit akcji równoczesnych zarządzany przez TrafficController.
    ///
    /// Formula:
    /// <list type="bullet">
    /// <item>Akcje per pojedynczy dyżurny = <c>1 + skill</c> (1★=2, 5★=6)</item>
    /// <item>Total = sum dla wszystkich active TC (OnShift/Available, nie Sick/Resting)</item>
    /// <item>Room lvl TrafficController = TYLKO cap headcount (RoleCaps z MM-5),
    ///       skill = akcje per dyżurny (decyzja MM-D12)</item>
    /// </list>
    ///
    /// 4 typy akcji blokujące slot dopóki pojazd nie dotrze na cel:
    /// <see cref="DispatchActionType.Out"/> / <see cref="DispatchActionType.Refuel"/> /
    /// <see cref="DispatchActionType.Workshop"/> / <see cref="DispatchActionType.Modernization"/>.
    ///
    /// Workflow:
    /// <list type="number">
    /// <item><see cref="TryDispatch"/> — wywołane gdy gracz/AI chce wysłać pojazd. Jeśli wolne
    ///       miejsce → status Active, akcja się "blokuje". Jeśli brak → Pending kolejka.</item>
    /// <item><see cref="ReleaseAction"/> — gdy pojazd dotarł na cel (hook z TrainRunSimulator
    ///       OnVehicleSpawned, DepotMovementSimulator OnVehicleArrivedAt — integracja MM-9 dla
    ///       Refuel, dalsze etapy dla pozostałych).</item>
    /// <item><see cref="StartNextPending"/> — auto-promote pierwszy z _pending do _active gdy
    ///       wolne miejsce. Wywoływane przy ReleaseAction + OnEmployeesChanged.</item>
    /// </list>
    ///
    /// MM-D21 manual dispatch fallback: gdy GetTotalActionsCapacity() = 0 (brak nastawni
    /// lub dyżurnych), TryDispatch zwraca <c>RequiresManual</c> — gracz musi sam kliknąć
    /// "Wyślij teraz" w UI (MM-9 lub późniejszy etap dorzuca UI).
    /// </summary>
    public static class DispatchActionService
    {
        public enum DispatchResult
        {
            /// <summary>Akcja przeszła do active (slot wolny, pojazd ruszył).</summary>
            Dispatched,
            /// <summary>Akcja w kolejce pending (cap reached, czeka na wolne miejsce).</summary>
            Queued,
            /// <summary>Brak nastawni / dyżurnych — gracz musi manualnie potwierdzić (MM-D21).</summary>
            RequiresManual,
            /// <summary>Pojazd już ma aktywną akcję tego typu (duplicate).</summary>
            AlreadyDispatched,
        }

        static readonly List<DispatchActionEntry> _active = new();
        static readonly List<DispatchActionEntry> _pending = new();
        static int _nextDispatchId = 1;
        static bool _eventsHooked;

        public static event Action OnDispatchChanged;

        public static IReadOnlyList<DispatchActionEntry> Active => _active;
        public static IReadOnlyList<DispatchActionEntry> Pending => _pending;

        // ════════════════════════════════════════════════════════
        //  Capacity
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// Total akcji równoczesnych = suma <c>(1 + skill)</c> dla każdego active TrafficController
        /// (OnShift lub Available; Sick/Resting/Onboarding nie liczą się jako active).
        /// </summary>
        public static int GetTotalActionsCapacity()
        {
            int total = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (e == null) continue;
                if (e.role != EmployeeRole.TrafficController) continue;
                if (e.status != EmployeeStatus.OnShift && e.status != EmployeeStatus.Available) continue;
                total += 1 + e.skill;
            }
            return total;
        }

        public static int GetUsedActions() => _active.Count;
        public static int GetPendingCount() => _pending.Count;
        public static int GetFreeSlots() => Math.Max(0, GetTotalActionsCapacity() - GetUsedActions());

        /// <summary>Czy gra jest w manual dispatch mode (MM-D21 — brak automatyki).</summary>
        public static bool IsManualMode() => GetTotalActionsCapacity() == 0;

        // ════════════════════════════════════════════════════════
        //  TryDispatch / Release / Pending promotion
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// Próbuje wysłać pojazd. Zwraca <see cref="DispatchResult"/>:
        /// - <c>Dispatched</c> — akcja w _active, pojazd "ruszył" (caller integruje
        ///   z TrainRunSimulator/DepotMovementSimulator)
        /// - <c>Queued</c> — _pending, czeka na wolne miejsce
        /// - <c>RequiresManual</c> — manual dispatch mode (cap=0)
        /// - <c>AlreadyDispatched</c> — pojazd ma już aktywną akcję tego typu
        /// </summary>
        public static DispatchResult TryDispatch(int vehicleId, DispatchActionType type)
        {
            EnsureEventsHooked();

            // Duplicate check (active + pending)
            foreach (var a in _active)
                if (a.vehicleId == vehicleId && a.type == type) return DispatchResult.AlreadyDispatched;
            foreach (var p in _pending)
                if (p.vehicleId == vehicleId && p.type == type) return DispatchResult.AlreadyDispatched;

            long now = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;

            if (IsManualMode())
            {
                // MM-D21: brak nastawni — wymagana manual interaction
                Log.Info($"[DispatchActionService] Manual dispatch required for vehicle#{vehicleId} {type} " +
                         "(brak nastawni / dyżurnych — zbuduj TrafficController room lub poczekaj na zmianę)");
                return DispatchResult.RequiresManual;
            }

            var entry = new DispatchActionEntry
            {
                dispatchId = _nextDispatchId++,
                vehicleId = vehicleId,
                type = type,
                requestedGameTime = now,
            };

            if (GetFreeSlots() > 0)
            {
                entry.dispatchedGameTime = now;
                _active.Add(entry);
                Log.Info($"[DispatchActionService] DISPATCHED #{entry.dispatchId} vehicle#{vehicleId} {type} " +
                         $"(active {_active.Count}/{GetTotalActionsCapacity()})");
                OnDispatchChanged?.Invoke();
                return DispatchResult.Dispatched;
            }

            _pending.Add(entry);
            Log.Info($"[DispatchActionService] QUEUED #{entry.dispatchId} vehicle#{vehicleId} {type} " +
                     $"(active {_active.Count}/{GetTotalActionsCapacity()}, pending {_pending.Count})");
            OnDispatchChanged?.Invoke();
            return DispatchResult.Queued;
        }

        /// <summary>
        /// Zwalnia akcję dla danego pojazdu (callable z TrainRunSimulator.OnVehicleSpawned,
        /// DepotMovementSimulator.OnVehicleArrivedAt). Auto-promote pierwszego pending'a do active.
        /// Zwraca true gdy znaleziono i zwolniono akcję.
        /// </summary>
        public static bool ReleaseAction(int vehicleId, DispatchActionType type)
        {
            EnsureEventsHooked();

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i].vehicleId == vehicleId && _active[i].type == type)
                {
                    int dispatchId = _active[i].dispatchId;
                    _active.RemoveAt(i);
                    Log.Info($"[DispatchActionService] RELEASED #{dispatchId} vehicle#{vehicleId} {type} " +
                             $"(active {_active.Count}/{GetTotalActionsCapacity()})");
                    StartNextPending();
                    OnDispatchChanged?.Invoke();
                    return true;
                }
            }

            // Może akcja była w pending (np. cancel'owana zanim ruszyła)
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].vehicleId == vehicleId && _pending[i].type == type)
                {
                    _pending.RemoveAt(i);
                    OnDispatchChanged?.Invoke();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Promote pierwszego pending'a do active gdy są wolne miejsca. Wywoływane
        /// przy ReleaseAction + OnEmployeesChanged (np. dyżurny wraca z Resting → wzrost capacity).
        /// </summary>
        public static void StartNextPending()
        {
            int promoted = 0;
            long now = (long)GameState.GameTimeSeconds + GameState.GameDay * 86400L;

            while (_pending.Count > 0 && GetFreeSlots() > 0)
            {
                var entry = _pending[0];
                _pending.RemoveAt(0);
                entry.dispatchedGameTime = now;
                _active.Add(entry);
                promoted++;
                Log.Info($"[DispatchActionService] AUTO-PROMOTED #{entry.dispatchId} " +
                         $"vehicle#{entry.vehicleId} {entry.type} from pending → active");
            }

            if (promoted > 0)
                OnDispatchChanged?.Invoke();
        }

        public static void ResetAll()
        {
            _active.Clear();
            _pending.Clear();
            _nextDispatchId = 1;
            // BUG-025: unhook event przy reset (bez tego handler od starej sesji aktywny w nowej).
            if (_eventsHooked)
            {
                PersonnelService.OnEmployeesChanged -= HandleEmployeesChanged;
                _eventsHooked = false;
            }
            OnDispatchChanged?.Invoke();
        }

        // ════════════════════════════════════════════════════════
        //  BUG-022: Save/Load support
        // ════════════════════════════════════════════════════════

        /// <summary>Snapshot active entries (dla save). Read-only — modyfikacja przez TryDispatch/ReleaseAction.</summary>
        public static IReadOnlyList<DispatchActionEntry> GetActiveSnapshot() => _active;

        /// <summary>Snapshot pending entries (dla save).</summary>
        public static IReadOnlyList<DispatchActionEntry> GetPendingSnapshot() => _pending;

        /// <summary>Aktualny licznik dispatchId (dla save — żeby nie kolizjować ID po load).</summary>
        public static int GetNextDispatchId() => _nextDispatchId;

        /// <summary>
        /// BUG-022: restore state z save. Wywoływane przez PersonnelSavable.Deserialize.
        /// Wcześniej wywołać <see cref="ResetAll"/> żeby wyczyścić poprzedni state.
        /// </summary>
        public static void RestoreFromSave(IList<DispatchActionEntry> active,
                                           IList<DispatchActionEntry> pending,
                                           int nextDispatchId)
        {
            _active.Clear();
            _pending.Clear();
            if (active != null) _active.AddRange(active);
            if (pending != null) _pending.AddRange(pending);
            _nextDispatchId = nextDispatchId > 0 ? nextDispatchId : 1;
            EnsureEventsHooked();
            OnDispatchChanged?.Invoke();
        }

        // ════════════════════════════════════════════════════════
        //  MM-13 / MM-D21 — Manual dispatch fallback
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// MM-D21: manualne wysłanie pojazdu z bypass'em cap dyspozytorów. Wywoływane
        /// gdy <see cref="IsManualMode"/>=true (brak nastawni / dyżurnych) i gracz
        /// klika "Wyślij teraz" w UI. Zawsze tworzy active dispatch (nie pending).
        ///
        /// Zwraca <c>true</c> gdy udało się dispatch'ować, <c>false</c> tylko przy
        /// <see cref="DispatchResult.AlreadyDispatched"/> (duplicate).
        ///
        /// Caller (UI / smoke test) jest odpowiedzialny za:
        /// - Walidację że gracz świadomie kliknął manual
        /// - Pokazanie warning'u że jest w manual mode
        /// - Tracking że to manual (np. log entry, achievement counter)
        /// </summary>
        public static bool ManualDispatch(int vehicleId, DispatchActionType type)
        {
            EnsureEventsHooked();

            // Duplicate check (active + pending)
            foreach (var a in _active)
                if (a.vehicleId == vehicleId && a.type == type) return false;
            foreach (var p in _pending)
                if (p.vehicleId == vehicleId && p.type == type) return false;

            long now = (long)RailwayManager.Core.GameState.GameTimeSeconds + RailwayManager.Core.GameState.GameDay * 86400L;

            var entry = new DispatchActionEntry
            {
                dispatchId = _nextDispatchId++,
                vehicleId = vehicleId,
                type = type,
                requestedGameTime = now,
                dispatchedGameTime = now,  // instant active (manual = bypass capacity)
            };
            _active.Add(entry);

            string mode = IsManualMode() ? "MANUAL FORCED (no automation)" : "MANUAL OVERRIDE (capacity bypass)";
            Log.Info($"[DispatchActionService] {mode} #{entry.dispatchId} vehicle#{vehicleId} {type}");
            OnDispatchChanged?.Invoke();
            return true;
        }

        // ════════════════════════════════════════════════════════
        //  Hook init (lazy — przy pierwszym TryDispatch/ReleaseAction)
        // ════════════════════════════════════════════════════════

        static void EnsureEventsHooked()
        {
            if (_eventsHooked) return;
            PersonnelService.OnEmployeesChanged += HandleEmployeesChanged;
            _eventsHooked = true;
        }

        static void HandleEmployeesChanged()
        {
            // Capacity może się zmienić (hire/fire dyżurnego, transition Sick→Available).
            // Auto-promote pending jeśli właśnie zwiększyła się capacity.
            StartNextPending();
        }

        // ════════════════════════════════════════════════════════
        //  Diagnostics
        // ════════════════════════════════════════════════════════

        /// <summary>Lvl pokoju TrafficController (MM-D15 best-lvl-wins).</summary>
        public static int GetTrafficControllerRoomLvl()
        {
            var svc = RoomLevelService.Instance;
            return svc == null ? 0 : svc.GetBestLevelForType(RoomType.TrafficController);
        }
    }
}
