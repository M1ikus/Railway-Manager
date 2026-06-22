using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Economy;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// MM-18: 2-fazowy flow service jobs.
    ///
    /// <list type="bullet">
    /// <item><b>EnRoute</b> — pojazd jedzie po torach z origin → target stanowiska
    /// (DepotMovementSimulator.EnqueueMove). Timer NIE liczy.</item>
    /// <item><b>Servicing</b> — pojazd dotarł, na stanowisku, count-down
    /// <c>completionGameTime</c> w toku. Po skończeniu ApplyEffect.</item>
    /// <item><b>Completed</b> — effect applied, pojazd zostaje na stanowisku
    /// (decyzja MM-18 #1: nie wraca automatycznie).</item>
    /// <item><b>Failed</b> — pathfinding fail / cancel mid-route / brak access
    /// track. Job removed z _activeJobs, refund 100% jeśli nie zaczął ApplyEffect.</item>
    /// </list>
    /// </summary>
    public enum OutdoorJobState
    {
        /// <summary>Pojazd jedzie do stanowiska (DepotMovementSimulator aktywny).</summary>
        EnRoute,
        /// <summary>Pojazd na stanowisku, count-down do completion.</summary>
        Servicing,
        /// <summary>Effect applied, pojazd na stanowisku stoi.</summary>
        Completed,
        /// <summary>Job się wywalił — refund + status reset.</summary>
        Failed,
    }

    /// <summary>
    /// MM-9 / MM-D14 — typ operacji outdoor equipment (4 lifecycle).
    /// </summary>
    public enum OutdoorJobType
    {
        /// <summary>Mycie pojazdu w WashZone outdoor lub wash_gate w Hall (cleanliness restore).</summary>
        Wash,
        /// <summary>Obrót pojazdu na Turntable (orientation change, stub w EA).</summary>
        Rotate,
        /// <summary>Quick maintenance na PitLift outdoor (P1 only, tańszy alternatywny do Hall).</summary>
        PitLiftMaint,
        /// <summary>Tankowanie diesli na FuelStation outdoor lub fuel_pump w Hall (fuelLevel restore).</summary>
        Refuel,
        /// <summary>MM-17: wodowanie pasażerskich (EMU/DMU/PassengerCar) — woda do toalet
        /// uzupełniona, zbiornik fekaliów opróżniony. WaterService outdoor lub water_service indoor.</summary>
        WaterService,
    }

    /// <summary>MM-9: aktywny job outdoor equipment. MM-18: 2-fazowy state machine.</summary>
    [Serializable]
    public class OutdoorJob : IServiceJobWithMovement
    {
        public int jobId;
        public int vehicleId;

        // A1 partial: explicit interface mapping (OutdoorJob używa OutdoorJobState
        // zamiast ServiceJobState, więc translujemy między enumami).
        int IServiceJobWithMovement.JobVehicleId => vehicleId;
        long IServiceJobWithMovement.JobStartedGameTime => startedGameTime;
        long IServiceJobWithMovement.JobCompletionGameTime => completionGameTime;
        long IServiceJobWithMovement.JobArrivedAtTargetGameTime
        {
            get => arrivedAtTargetGameTime;
            set => arrivedAtTargetGameTime = value;
        }
        ServiceJobState IServiceJobWithMovement.JobState
        {
            get => state switch
            {
                OutdoorJobState.EnRoute   => ServiceJobState.EnRoute,
                OutdoorJobState.Servicing => ServiceJobState.Servicing,
                OutdoorJobState.Completed => ServiceJobState.Completed,
                _                          => ServiceJobState.Failed,
            };
            set => state = value switch
            {
                ServiceJobState.EnRoute   => OutdoorJobState.EnRoute,
                ServiceJobState.Servicing => OutdoorJobState.Servicing,
                ServiceJobState.Completed => OutdoorJobState.Completed,
                _                          => OutdoorJobState.Failed,
            };
        }
        public OutdoorJobType type;
        /// <summary>PlacedOutdoorEquipment.instanceId LUB PlacedFurnitureItem.instanceId
        /// (indoor fuel_pump/wash_gate w Hall). -1 = unknown/legacy.</summary>
        public int equipmentInstanceId;
        public long startedGameTime;
        public long completionGameTime;
        public int costGroszy;

        // ── MM-18: state machine + movement integration ────────────

        /// <summary>MM-18: aktualna faza (EnRoute/Servicing/Completed/Failed).
        /// Default <see cref="OutdoorJobState.EnRoute"/> — Schedule od razu wywołuje
        /// EnqueueMove. Dla legacy/teleport mode (testy, brak grafu torów) caller może
        /// pominąć movement i ustawić Servicing bezpośrednio.</summary>
        public OutdoorJobState state = OutdoorJobState.EnRoute;

        /// <summary>MM-18: tor docelowy resolved przez <see cref="DepotSystem.AccessTrackResolver"/>
        /// (najbliższy do centroidu equipment). -1 gdy resolve nie powiódł się
        /// (Schedule rzuci Warn i zwróci false zanim job dorzuci do _activeJobs).</summary>
        public int targetTrackId = -1;

        /// <summary>MM-18: tor pochodzenia (audit + ewentualne return). -1 = unknown
        /// (np. spawnowany pojazd z showroomu).</summary>
        public int originTrackId = -1;

        /// <summary>MM-18: consistId DepotMoveTask'a w fazie EnRoute. Default vehicleId
        /// dla single-vehicle move; dla multi-vehicle service z jednego skladu trzeba
        /// generated unique przez <c>DepotMovementSimulator.GenerateConsistId()</c>.</summary>
        public int consistId = -1;

        /// <summary>MM-18: kiedy faza Servicing wystartowała (po dotarciu pojazdu).
        /// 0 = jeszcze EnRoute. Wykorzystywane do recompute <see cref="completionGameTime"/>:
        /// gdy EnqueueMove zajmie X sekund, completion = arrivedAtTarget + duration.</summary>
        public long arrivedAtTargetGameTime;

        /// <summary>MM-18: oryginalny duration (w sekundach gry) zachowany dla recompute
        /// completionGameTime po przejściu EnRoute → Servicing.</summary>
        public long durationSec;
    }

    /// <summary>
    /// MM-9 — lifecycle service dla outdoor equipment operations (analogicznie
    /// <see cref="PaintingJobService"/> dla ZNTK paint).
    ///
    /// Workflow:
    /// <list type="number">
    /// <item>Caller (UI / dispatcher) wywołuje <c>ScheduleX</c> z vehicleId + equipmentId</item>
    /// <item>Job dorzucony do <see cref="ActiveJobs"/>, vehicle status = OutOfService</item>
    /// <item><see cref="CheckCompletions"/> wywoływane per-tick (z WorkshopManager.Update)
    /// przez game time progression. Gdy <c>nowGameTime ≥ completionGameTime</c>:
    /// apply effect per type, vehicle status = StoppedInDepot.</item>
    /// <item><see cref="Cancel"/> — anuluje job, 50% refund (jak PaintingJobService).</item>
    /// </list>
    ///
    /// Effect per type:
    /// <list type="bullet">
    /// <item><b>Wash</b>: <c>cleanlinessPercent = 100</c></item>
    /// <item><b>Rotate</b>: stub (orientation tracking dorzuca M-Models / post-EA;
    /// w EA tylko log + cost charge)</item>
    /// <item><b>PitLiftMaint</b>: P1 inspection inline (RestoreComponentsForLevel(P1)
    /// wymagałoby ref do WorkshopManager — w MM-9 placeholder log, faktyczna
    /// integracja w polish gdy WorkshopManager expose'ować helper)</item>
    /// <item><b>Refuel</b>: <c>fuelLevelPercent = 100</c></item>
    /// </list>
    ///
    /// Czas + koszt placeholder do M-Balance:
    /// <list type="bullet">
    /// <item>Wash: 30 min gry, 200 zł</item>
    /// <item>Rotate: 5 min gry, 50 zł (stub)</item>
    /// <item>PitLiftMaint: 2h gry, 2000 zł (~jak P1 inspection)</item>
    /// <item>Refuel: 15 min gry, koszt = lengthM × 100 zł (długi pojazd = więcej paliwa)</item>
    /// </list>
    /// </summary>
    public static class OutdoorEquipmentJobService
    {
        const long DAY_SECONDS = 86400L;

        // Czasy + koszty — patrz FleetBalanceConstants.Outdoor*.

        static readonly List<OutdoorJob> _activeJobs = new();
        static int _nextJobId = 1;

        public static IReadOnlyList<OutdoorJob> ActiveJobs => _activeJobs;

        public static event Action<OutdoorJob> OnJobScheduled;
        public static event Action<OutdoorJob> OnJobCompleted;
        public static event Action<OutdoorJob> OnJobCancelled;

        /// <summary>
        /// MM-13: Hook na presence active WashBay worker. Set'owane przez Personnel.
        /// Null = brak walidacji (Wash dispatch zawsze succeed). Func zwraca true gdy
        /// jest co najmniej jeden active WashBay worker (OnShift/Available).
        /// </summary>
        public static Func<bool> WashBayWorkerPresenceHook;

        /// <summary>
        /// MM-18: Hook do wywołania movement (DepotMovementSimulator.EnqueueMove).
        /// Set'owany przez OutdoorEquipmentMovementBridge w Timetable namespace
        /// (asymetria asmdef: Fleet NIE widzi Depot). Bridge:
        /// <list type="number">
        /// <item>Resolve target track via AccessTrackResolver z equipment instance</item>
        /// <item>Resolve origin track via VehicleLocationService.depotTrackId</item>
        /// <item>Wywołać DepotMovementSimulator.EnqueueMove(consistId, [vehicleId], origin, target, accessPos)</item>
        /// <item>Subskrybować task.onCompleted → callback do
        /// <see cref="MarkMovementCompleted"/> / <see cref="MarkMovementFailed"/></item>
        /// </list>
        /// Returns true gdy ruch zaschedulowany (state pozostaje EnRoute), false gdy
        /// fallback (np. brak grafu torów, target nieosiągalny) — wtedy
        /// <see cref="ScheduleInternal"/> ustawia <see cref="OutdoorJobState.Servicing"/>
        /// immediate (legacy mode).
        /// </summary>
        public static Func<OutdoorJob, bool> RequestMovementHook;

        /// <summary>
        /// MM-18: API dla Bridge'u — informuje że pojazd dotarł na stanowisko (movement
        /// task completed). Przejście EnRoute → Servicing + recompute completionGameTime.
        /// </summary>
        public static void MarkMovementCompleted(int jobId, long nowGameTime)
        {
            for (int i = 0; i < _activeJobs.Count; i++)
            {
                var job = _activeJobs[i];
                if (job.jobId != jobId) continue;
                if (job.state != OutdoorJobState.EnRoute) return; // już moved (defensive)

                job.state = OutdoorJobState.Servicing;
                job.arrivedAtTargetGameTime = nowGameTime;
                job.completionGameTime = nowGameTime + job.durationSec;

                Log.Info($"[OutdoorEquipmentJobService] EnRoute → Servicing #{job.jobId} " +
                         $"vehicle#{job.vehicleId} {job.type} (complete za {job.durationSec / 60} min gry)");
                FleetService.NotifyOwnedChanged();
                return;
            }
        }

        /// <summary>
        /// MM-18: API dla Bridge'u — informuje że movement się wywalił (np. brak ścieżki).
        /// Job removed + 100% refund (effect nie był applied).
        /// </summary>
        public static void MarkMovementFailed(int jobId, long nowGameTime, string reason)
        {
            for (int i = _activeJobs.Count - 1; i >= 0; i--)
            {
                var job = _activeJobs[i];
                if (job.jobId != jobId) continue;
                if (job.state != OutdoorJobState.EnRoute) return; // już moved (defensive)

                // 100% refund (movement się wywalił przed Servicing)
                long refund = job.costGroszy / 100;
                MoneyLedger.Earn(job.costGroszy, "outdoor_refund", $"Usługa outdoor v#{job.vehicleId} anulowana (100%)");

                var v = FleetService.GetOwnedById(job.vehicleId);
                if (v != null)
                {
                    v.status = FleetVehicleStatus.StoppedInDepot;
                    v.currentTask = null;
                    v.estimatedCompletionGameTime = nowGameTime;
                }

                job.state = OutdoorJobState.Failed;
                _activeJobs.RemoveAt(i);
                OnJobCancelled?.Invoke(job);
                Log.Warn($"[OutdoorEquipmentJobService] MOVEMENT FAILED #{job.jobId} " +
                         $"vehicle#{job.vehicleId} {job.type} — {reason}, refund {refund}zł");
                FleetService.NotifyOwnedChanged();
                return;
            }
        }

        // ════════════════════════════════════════════════════════
        //  Schedule API
        // ════════════════════════════════════════════════════════

        public static ScheduleResult ScheduleWash(int vehicleId, int equipmentInstanceId, long nowGameTime)
        {
            // MM-13: walidacja presence WashBay worker przez hook (set'owany przez Personnel)
            if (WashBayWorkerPresenceHook != null && !WashBayWorkerPresenceHook())
            {
                Log.Warn("[OutdoorEquipmentJobService] ScheduleWash: brak active WashBay worker. " +
                         "Zatrudnij pracownika myjni lub wykonaj manualnie.");
                return ScheduleResult.WorkshopUnavailable;
            }
            return ScheduleInternal(vehicleId, equipmentInstanceId, OutdoorJobType.Wash,
                                    FleetBalanceConstants.OutdoorWashDurationSec,
                                    FleetBalanceConstants.OutdoorWashCostGr, nowGameTime);
        }

        /// <summary>
        /// MM-9 stub: orientation tracking (VehiclePosition.headingDegrees) dorzucony
        /// w M-Models / post-EA. Do tego czasu Schedule zwraca <see cref="ScheduleResult.StubNotImplemented"/>
        /// bez pobierania kosztu — gwarantuje brak money-leak'u jeśli UI omyłkowo zaoferuje akcję.
        /// </summary>
        public static ScheduleResult ScheduleRotate(int vehicleId, int equipmentInstanceId, long nowGameTime)
        {
            Log.Warn("[OutdoorEquipmentJobService] ScheduleRotate: orientation tracking " +
                     "niedostępne w EA (planowane M-Models). Akcja zablokowana, koszt nie pobrany.");
            return ScheduleResult.StubNotImplemented;
        }

        /// <summary>
        /// MM-9 stub: P1 inspection effect (RestoreComponents + InspectionSchedule.Perform)
        /// wymaga ref do WorkshopManager.RestoreP1ForVehicle (post-EA polish).
        /// </summary>
        public static ScheduleResult SchedulePitLiftMaint(int vehicleId, int equipmentInstanceId, long nowGameTime)
        {
            Log.Warn("[OutdoorEquipmentJobService] SchedulePitLiftMaint: quick P1 niedostępny " +
                     "w EA (planowany post-EA polish). Użyj Hall P1 ServicePit. Koszt nie pobrany.");
            return ScheduleResult.StubNotImplemented;
        }

        public static ScheduleResult ScheduleRefuel(int vehicleId, int equipmentInstanceId, long nowGameTime)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            if (v != null && !v.RequiresFuel)
            {
                Log.Warn($"[OutdoorEquipmentJobService] ScheduleRefuel: vehicle#{vehicleId} ({v.series}) " +
                         "nie wymaga paliwa (elektryczny — energia z catenary). Job pominięty.");
                return ScheduleResult.IncompatibleVehicle;
            }

            // AS-P1: koszt realny = brakujące litry × cena paliwa (zastąpiło proxy 1 zł × lengthM).
            int costGr = v != null ? FleetFuelMath.RefuelCostGroszy(v) : 0;
            return ScheduleInternal(vehicleId, equipmentInstanceId, OutdoorJobType.Refuel,
                                    FleetBalanceConstants.OutdoorRefuelDurationSec, costGr, nowGameTime);
        }

        /// <summary>
        /// MM-17: wodowanie pasażerskich — woda + opróżnienie zbiornika fekaliów.
        /// Walidacja: pojazd musi mieć <see cref="FleetVehicleData.RequiresWaterService"/>=true
        /// (EMU/DMU/PassengerCar). Lokomotywy luzem (Electric/Diesel) odrzucone.
        /// </summary>
        public static ScheduleResult ScheduleWaterService(int vehicleId, int equipmentInstanceId, long nowGameTime)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            if (v != null && !v.RequiresWaterService)
            {
                Log.Warn($"[OutdoorEquipmentJobService] ScheduleWaterService: vehicle#{vehicleId} ({v.series}) " +
                         $"nie wymaga wodowania (typ {v.type} — lokomotywa luzem bez toalet). Job pominięty.");
                return ScheduleResult.IncompatibleVehicle;
            }
            return ScheduleInternal(vehicleId, equipmentInstanceId, OutdoorJobType.WaterService,
                                    FleetBalanceConstants.OutdoorWaterServiceDurationSec,
                                    FleetBalanceConstants.OutdoorWaterServiceCostGr, nowGameTime);
        }

        static ScheduleResult ScheduleInternal(int vehicleId, int equipmentInstanceId, OutdoorJobType type,
                                     long durationSec, int costGr, long nowGameTime)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            if (v == null)
            {
                Log.Warn($"[OutdoorEquipmentJobService] Schedule{type}: vehicle#{vehicleId} not found");
                return ScheduleResult.VehicleNotFound;
            }
            if (GetActiveJobForVehicle(vehicleId) != null)
            {
                Log.Warn($"[OutdoorEquipmentJobService] Schedule{type}: vehicle#{vehicleId} already has active job");
                return ScheduleResult.VehicleHasActiveJob;
            }

            // Cash check
            int cashGr = (int)(GameState.Money * 100L);
            if (cashGr < costGr)
            {
                Log.Warn($"[OutdoorEquipmentJobService] Brak gotówki na {type}: " +
                         $"potrzeba {costGr / 100f:F0}zł, masz {GameState.Money}zł");
                return ScheduleResult.InsufficientFunds;
            }

            var job = new OutdoorJob
            {
                jobId = _nextJobId++,
                vehicleId = vehicleId,
                type = type,
                equipmentInstanceId = equipmentInstanceId,
                startedGameTime = nowGameTime,
                completionGameTime = nowGameTime + durationSec, // tymczasowe; recompute po dotarciu w MarkMovementCompleted
                costGroszy = costGr,
                durationSec = durationSec,
                // MM-18: początkowo EnRoute. Bridge wywoła hook → real movement
                // → callback MarkMovementCompleted przejdzie na Servicing + recompute completion.
                state = OutdoorJobState.EnRoute,
                consistId = -1,        // Bridge ustawi consistId wygenerowany przez DepotMovementSimulator
                targetTrackId = -1,    // Bridge resolves
                originTrackId = -1,    // Bridge resolves
                arrivedAtTargetGameTime = 0,
            };
            _activeJobs.Add(job);

            // Pobierz koszt (już teraz, refund per faza w Cancel/MarkMovementFailed)
            MoneyLedger.Spend(costGr, "outdoor_service", "usługa outdoor equipment");

            // MM-18: hook → Bridge robi real EnqueueMove. Brak hook'a (legacy / test mode) =
            // fallback do immediate Servicing (zachowanie z MM-9..17).
            bool movementScheduled = false;
            if (RequestMovementHook != null)
            {
                try
                {
                    movementScheduled = RequestMovementHook.Invoke(job);
                }
                catch (Exception ex)
                {
                    Log.Warn($"[OutdoorEquipmentJobService] RequestMovementHook threw: {ex.Message}");
                    movementScheduled = false;
                }
            }

            if (!movementScheduled)
            {
                // Legacy fallback: bypass movement, prosto do Servicing.
                // Stosowane gdy: (a) brak Bridge'u (np. testy bez Depot scene),
                // (b) Bridge zwrócił false (np. równoważne stanowisko ale brak grafu torów).
                job.state = OutdoorJobState.Servicing;
                job.arrivedAtTargetGameTime = nowGameTime;
                Log.Info($"[OutdoorEquipmentJobService] LEGACY mode (no movement) #{job.jobId} " +
                         $"vehicle#{vehicleId} {type} → bezpośrednio Servicing");
            }

            v.status = FleetVehicleStatus.OutOfService;
            v.currentTask = movementScheduled
                ? $"{TypeLabel(type)} — w drodze (potem {durationSec / 60} min gry)"
                : $"{TypeLabel(type)} ({durationSec / 60} min gry)";
            v.estimatedCompletionGameTime = job.completionGameTime;

            Log.Info($"[OutdoorEquipmentJobService] SCHEDULED #{job.jobId} vehicle#{vehicleId} {type} " +
                     $"@ equipment#{equipmentInstanceId}, cost {costGr / 100f:F0}zł, " +
                     $"phase={job.state}, complete za {durationSec / 60} min");
            FleetService.NotifyOwnedChanged();
            OnJobScheduled?.Invoke(job);
            return ScheduleResult.Success;
        }

        // ════════════════════════════════════════════════════════
        //  Completion check (wywoływane z WorkshopManager.Update)
        // ════════════════════════════════════════════════════════

        public static int CheckCompletions(long nowGameTime)
        {
            int completed = 0;
            for (int i = _activeJobs.Count - 1; i >= 0; i--)
            {
                var job = _activeJobs[i];

                // MM-18 state machine:
                // - EnRoute → DepotMovementSimulator tickuje samodzielnie, callback przejmie
                //   stan na Servicing (MM-18b). W MM-18a tego ticka jeszcze nie ma, więc
                //   EnRoute joby zostają w kolejce indefinite — testy E2E w MM-18b dorzucą
                //   movement integration.
                // - Servicing → check timer
                // - Completed/Failed → już usunięte (defensive cleanup)
                if (job.state == OutdoorJobState.Completed || job.state == OutdoorJobState.Failed)
                {
                    _activeJobs.RemoveAt(i);
                    continue;
                }
                if (job.state != OutdoorJobState.Servicing) continue;
                if (nowGameTime < job.completionGameTime) continue;

                ApplyEffect(job);
                job.state = OutdoorJobState.Completed;
                _activeJobs.RemoveAt(i);
                completed++;
                OnJobCompleted?.Invoke(job);
                Log.Info($"[OutdoorEquipmentJobService] COMPLETED #{job.jobId} " +
                         $"vehicle#{job.vehicleId} {job.type}");
            }
            if (completed > 0) FleetService.NotifyOwnedChanged();
            return completed;
        }

        static void ApplyEffect(OutdoorJob job)
        {
            var v = FleetService.GetOwnedById(job.vehicleId);
            if (v == null) return;

            switch (job.type)
            {
                case OutdoorJobType.Wash:
                    v.cleanlinessPercent = 100f;
                    break;
                case OutdoorJobType.Rotate:
                    // Stub w MM-9: orientation tracking w VehiclePosition.headingDegrees → M-Models / post-EA
                    break;
                case OutdoorJobType.PitLiftMaint:
                    // Stub w MM-9: P1 inspection effect (RestoreComponents + InspectionSchedule.Perform)
                    // wymaga ref do WorkshopManager helper. Polish: wywołać WorkshopManager.RestoreP1ForVehicle(v)
                    // po refactor'ze (w MM-9 tylko log).
                    Log.Info($"[OutdoorEquipmentJobService] PitLift P1 maint applied to vehicle#{v.id} (stub w EA)");
                    break;
                case OutdoorJobType.Refuel:
                    v.fuelLevelPercent = 100f;
                    break;
                case OutdoorJobType.WaterService:
                    // MM-17: uzupełnij wodę + opróżnij zbiornik fekaliów
                    v.waterLevelPercent = 100f;
                    v.wasteTankLevelPercent = 0f;
                    break;
            }

            v.status = FleetVehicleStatus.StoppedInDepot;
            v.currentTask = null;
            v.estimatedCompletionGameTime = job.completionGameTime;

            v.history.Add(new MaintenanceRecord
            {
                gameTimeSeconds = job.completionGameTime,
                recordType = TypeLabel(job.type),
                description = $"Outdoor equipment job @ instance#{job.equipmentInstanceId}",
                cost = job.costGroszy / 100,
                mileageAtRecord = v.mileageKm,
            });
        }

        // ════════════════════════════════════════════════════════
        //  Cancel + lookup
        // ════════════════════════════════════════════════════════

        public static OutdoorJob GetActiveJobForVehicle(int vehicleId)
        {
            foreach (var j in _activeJobs)
                if (j.vehicleId == vehicleId) return j;
            return null;
        }

        public static bool Cancel(int vehicleId, long nowGameTime)
            => ServiceJobHelpers.CancelWithRefundPolicy(
                _activeJobs,
                vehicleId,
                nowGameTime,
                j => j.costGroszy / 100L,  // groszy → PLN
                (j, _, _) =>
                {
                    j.state = OutdoorJobState.Failed;
                    OnJobCancelled?.Invoke(j);
                },
                "OutdoorEquipmentJobService");

        public static void ResetAll()
        {
            _activeJobs.Clear();
            _nextJobId = 1;
        }

        /// <summary>BUG-021: accessor dla save.</summary>
        public static int GetNextJobId() => _nextJobId;

        /// <summary>BUG-021: restore state z save (po ResetAll).</summary>
        public static void RestoreFromSave(IList<OutdoorJob> jobs, int nextJobId)
        {
            _activeJobs.Clear();
            if (jobs != null) _activeJobs.AddRange(jobs);
            _nextJobId = nextJobId > 0 ? nextJobId : 1;
            RecoverInterruptedMovementAfterLoad();
        }

        static void RecoverInterruptedMovementAfterLoad()
            => ServiceJobHelpers.MarkPendingMovementRecovery(
                _activeJobs,
                FleetVehicleStatus.OutOfService,
                j => $"{TypeLabel(j.type)} — wznawianie po load",
                "OutdoorEquipmentJobService");

        static string TypeLabel(OutdoorJobType type) => type switch
        {
            OutdoorJobType.Wash         => MaintenanceRecordTypes.Wash,
            OutdoorJobType.Rotate       => MaintenanceRecordTypes.Rotate,
            OutdoorJobType.PitLiftMaint => MaintenanceRecordTypes.PitLiftMaint,
            OutdoorJobType.Refuel       => MaintenanceRecordTypes.Refuel,
            OutdoorJobType.WaterService => MaintenanceRecordTypes.WaterService,
            _ => type.ToString(),
        };
    }
}
