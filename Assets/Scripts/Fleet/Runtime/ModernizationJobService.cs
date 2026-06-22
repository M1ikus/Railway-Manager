using System;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Economy;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// MM-10 / MM-D13/D17 — lifecycle service modernizacji pojazdów (External ZNTK + Internal
    /// własny warsztat lvl5).
    ///
    /// Workflow External (ZNTK):
    /// <list type="number">
    /// <item><see cref="ScheduleExternal"/> — gracz wybiera ZNTK z <see cref="ExternalWorkshop.modernizationAvailable"/>=true.
    /// Vehicle status OutOfService, koszt pobrany. Pomijamy delivery pathfinding (uproszczenie EA;
    /// w polish można reuse <c>WorkshopManager.SendToExternal</c> pattern).</item>
    /// <item>Po <see cref="ModernizationPath.durationDays"/> dni → ApplyEffect:
    /// vehicle.seriesId = path.targetSeriesId, parametry techniczne kopiowane z path overrides.</item>
    /// </list>
    ///
    /// Workflow Internal (Hall lvl5, MM-D13):
    /// <list type="number">
    /// <item><see cref="ScheduleInternal"/> — gracz wybiera ServicePit w Hall lvl ≥
    /// <see cref="ModernizationPath.minHallLevelInternal"/>. Walidacja:
    /// slot.maxVehicleLength ≥ path.minServicePitLength + slot wolny.</item>
    /// <item>Slot zajęty na 60-90 dni — w tym czasie nie obsługuje przeglądów P1-P5
    /// (long-block strategiczna decyzja gracza).</item>
    /// </list>
    ///
    /// Apply effect (oba tryby):
    /// - vehicle.seriesId, series, maxSpeedKmh, powerKw, comfortClass, reliabilityScore
    ///   updated z path overrides
    /// - comfortFeatures merged (path.newComfortFeatures dorzucone do existing)
    /// - history record (typ "Modernizacja [path.displayName]")
    /// - reputation event (VehicleUpgrade) — bonus dla gracza
    ///
    /// Cancel: 50% refund (jak PaintingJobService / OutdoorEquipmentJobService).
    /// </summary>
    public static class ModernizationJobService
    {
        const long DAY_SECONDS = 86400L;

        static readonly List<ModernizationJob> _activeJobs = new();
        static int _nextJobId = 1;

        public static IReadOnlyList<ModernizationJob> ActiveJobs => _activeJobs;

        public static event Action<ModernizationJob> OnJobScheduled;
        public static event Action<ModernizationJob> OnJobCompleted;
        public static event Action<ModernizationJob> OnJobCancelled;

        /// <summary>
        /// MM-10: Hook reputation bonus po completion (analog do <see cref="BreakdownService.SelfRepairBonusHook"/>).
        /// Args: (vehicleId, pathDisplayName). Set przez Timetable.Economy lub inny consumer
        /// (Fleet.asmdef nie referuje Timetable). Null = brak reputation event (silent).
        /// </summary>
        public static Action<int, string> OnModernizationCompletedReputationHook;

        /// <summary>
        /// MM-18: Hook movement scheduling dla Internal mode. Bridge w Timetable
        /// resolves ServicePit access track + EnqueueMove + subscribe callback. Returns
        /// true gdy real movement scheduled; false → fallback do legacy immediate Servicing.
        /// </summary>
        public static Func<ModernizationJob, bool> RequestMovementHook;

        /// <summary>MM-18: Bridge informuje że pojazd dotarł na ServicePit. Recompute completion + start Servicing.</summary>
        public static void MarkMovementCompleted(int jobId, long nowGameTime)
        {
            for (int i = 0; i < _activeJobs.Count; i++)
            {
                var job = _activeJobs[i];
                if (job.jobId != jobId) continue;
                if (job.state != ServiceJobState.EnRoute) return;
                job.state = ServiceJobState.Servicing;
                job.arrivedAtTargetGameTime = nowGameTime;
                job.completionGameTime = nowGameTime + job.durationSec;
                Log.Info($"[Modernization] EnRoute → Servicing #{job.jobId} vehicle#{job.vehicleId} " +
                         $"(complete za {job.durationSec / DAY_SECONDS} dni)");
                FleetService.NotifyOwnedChanged();
                return;
            }
        }

        /// <summary>MM-18: Bridge informuje że movement się wywalił. Refund 100%, reset.</summary>
        public static void MarkMovementFailed(int jobId, long nowGameTime, string reason)
        {
            for (int i = _activeJobs.Count - 1; i >= 0; i--)
            {
                var job = _activeJobs[i];
                if (job.jobId != jobId) continue;
                if (job.state != ServiceJobState.EnRoute) return;

                MoneyLedger.Earn(job.costPlnTotal * 100L, "modernization_refund", $"Modernizacja v#{job.vehicleId} anulowana");
                var v = FleetService.GetOwnedById(job.vehicleId);
                if (v != null)
                {
                    v.status = FleetVehicleStatus.StoppedInDepot;
                    v.currentTask = null;
                    v.estimatedCompletionGameTime = nowGameTime;
                }
                job.state = ServiceJobState.Failed;
                _activeJobs.RemoveAt(i);
                OnJobCancelled?.Invoke(job);
                Log.Warn($"[Modernization] MOVEMENT FAILED #{job.jobId} vehicle#{job.vehicleId} — {reason}, " +
                         $"refund {job.costPlnTotal:N0}zł");
                FleetService.NotifyOwnedChanged();
                return;
            }
        }

        // ════════════════════════════════════════════════════════
        //  Schedule External (ZNTK)
        // ════════════════════════════════════════════════════════

        public static ScheduleResult ScheduleExternal(int vehicleId, string pathId, string workshopId, long nowGameTime)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            if (v == null) { Log.Warn($"[Modernization] vehicle#{vehicleId} not found"); return ScheduleResult.VehicleNotFound; }
            if (GetActiveJobForVehicle(vehicleId) != null)
            {
                Log.Warn($"[Modernization] vehicle#{vehicleId} already has active job"); return ScheduleResult.VehicleHasActiveJob;
            }

            var path = ModernizationPathCatalog.GetByPathId(pathId);
            if (path == null) { Log.Warn($"[Modernization] path '{pathId}' not found"); return ScheduleResult.PathNotApplicable; }
            if (v.seriesId != path.sourceSeriesId)
            {
                Log.Warn($"[Modernization] vehicle#{vehicleId} ({v.seriesId}) nie pasuje do path " +
                         $"sourceSeriesId={path.sourceSeriesId}"); return ScheduleResult.PathNotApplicable;
            }

            var workshop = ExternalWorkshopCatalog.GetById(workshopId);
            if (workshop == null) { Log.Warn($"[Modernization] workshop '{workshopId}' not found"); return ScheduleResult.WorkshopNotFound; }
            if (!workshop.modernizationAvailable)
            {
                Log.Warn($"[Modernization] {workshop.name} nie obsługuje modernizacji"); return ScheduleResult.WorkshopUnavailable;
            }

            long costPln = path.externalCostPln;
            if (GameState.Money < costPln)
            {
                Log.Warn($"[Modernization] Brak gotówki: potrzeba {costPln:N0}zł, masz {GameState.Money:N0}zł");
                return ScheduleResult.InsufficientFunds;
            }

            MoneyLedger.Spend(costPln * 100L, "modernization", "modernizacja pojazdu");

            // BUG-072: durationSec ustawione również dla External (defensive — jeśli kiedyś
            // External dorobi MM-18 movement hook, MarkMovementCompleted nie ustawi
            // completionGameTime + 0 = instant completion).
            long externalDurationSec = path.durationDays * DAY_SECONDS;
            var job = new ModernizationJob
            {
                jobId = _nextJobId++,
                vehicleId = vehicleId,
                pathId = pathId,
                mode = ModernizationMode.External,
                externalWorkshopId = workshopId,
                internalServicePitInstanceId = -1,
                startedGameTime = nowGameTime,
                completionGameTime = nowGameTime + externalDurationSec,
                durationSec = externalDurationSec,
                costPlnTotal = costPln,
            };
            _activeJobs.Add(job);

            v.status = FleetVehicleStatus.OutOfService;
            v.currentTask = $"Modernizacja {path.displayName} ({workshop.name}) → {path.durationDays}d";
            v.estimatedCompletionGameTime = job.completionGameTime;

            Log.Info($"[Modernization] EXTERNAL #{job.jobId} vehicle#{vehicleId} {path.displayName} " +
                     $"@ {workshop.name}, koszt {costPln / 1_000_000f:F1}M zł, " +
                     $"complete za {path.durationDays} dni");
            FleetService.NotifyOwnedChanged();
            OnJobScheduled?.Invoke(job);
            return ScheduleResult.Success;
        }

        // ════════════════════════════════════════════════════════
        //  Schedule Internal (Hall lvl X, MM-D13)
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// MM-D13: modernizacja w własnym warsztacie. Wymagania:
        /// - Hall.level ≥ <see cref="ModernizationPath.minHallLevelInternal"/>
        /// - ServicePit.maxVehicleLength ≥ <see cref="ModernizationPath.minServicePitLength"/>
        /// - Slot wolny (occupyingVehicleId=-1)
        ///
        /// Walidacje sprawdza caller (UI) przed wywołaniem — tu MVP minimum.
        /// </summary>
        public static ScheduleResult ScheduleInternal(int vehicleId, string pathId, int servicePitInstanceId,
                                             float servicePitMaxLength, int hallLvl, long nowGameTime)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            if (v == null) { Log.Warn($"[Modernization] vehicle#{vehicleId} not found"); return ScheduleResult.VehicleNotFound; }
            if (GetActiveJobForVehicle(vehicleId) != null)
            {
                Log.Warn($"[Modernization] vehicle#{vehicleId} already has active job"); return ScheduleResult.VehicleHasActiveJob;
            }

            var path = ModernizationPathCatalog.GetByPathId(pathId);
            if (path == null) { Log.Warn($"[Modernization] path '{pathId}' not found"); return ScheduleResult.PathNotApplicable; }
            if (v.seriesId != path.sourceSeriesId)
            {
                Log.Warn($"[Modernization] vehicle#{vehicleId} ({v.seriesId}) nie pasuje do path " +
                         $"sourceSeriesId={path.sourceSeriesId}"); return ScheduleResult.PathNotApplicable;
            }
            if (hallLvl < path.minHallLevelInternal)
            {
                Log.Warn($"[Modernization] Hall lvl {hallLvl} za niski dla {path.displayName} " +
                         $"(wymaga ≥{path.minHallLevelInternal}). Awansuj Hall lub użyj ZNTK."); return ScheduleResult.HallLevelTooLow;
            }
            if (servicePitMaxLength + 0.01f < path.minServicePitLength)
            {
                Log.Warn($"[Modernization] ServicePit length {servicePitMaxLength}m za krótki " +
                         $"dla {path.displayName} (wymaga ≥{path.minServicePitLength}m)"); return ScheduleResult.ServicePitTooShort;
            }

            long costPln = path.internalCostPln;
            if (GameState.Money < costPln)
            {
                Log.Warn($"[Modernization] Brak gotówki: potrzeba {costPln:N0}zł, masz {GameState.Money:N0}zł");
                return ScheduleResult.InsufficientFunds;
            }

            MoneyLedger.Spend(costPln * 100L, "modernization", "modernizacja pojazdu");

            long durationSec = path.durationDays * DAY_SECONDS;
            var job = new ModernizationJob
            {
                jobId = _nextJobId++,
                vehicleId = vehicleId,
                pathId = pathId,
                mode = ModernizationMode.Internal,
                externalWorkshopId = null,
                internalServicePitInstanceId = servicePitInstanceId,
                startedGameTime = nowGameTime,
                completionGameTime = nowGameTime + durationSec,
                costPlnTotal = costPln,
                durationSec = durationSec,
                state = ServiceJobState.EnRoute, // MM-18: domyślnie EnRoute, hook zaschedulu ruch
            };
            _activeJobs.Add(job);

            // MM-18: hook → Bridge robi real EnqueueMove. Brak hook'a / fallback false → legacy immediate Servicing.
            bool movementScheduled = false;
            if (RequestMovementHook != null)
            {
                try { movementScheduled = RequestMovementHook.Invoke(job); }
                catch (Exception ex) { Log.Warn($"[Modernization] RequestMovementHook threw: {ex.Message}"); }
            }
            if (!movementScheduled)
            {
                job.state = ServiceJobState.Servicing;
                job.arrivedAtTargetGameTime = nowGameTime;
            }

            v.status = movementScheduled ? FleetVehicleStatus.OutOfService : FleetVehicleStatus.InRepair;
            v.currentTask = movementScheduled
                ? $"Modernizacja {path.displayName} — w drodze do slot#{servicePitInstanceId}"
                : $"Modernizacja {path.displayName} (slot ServicePit#{servicePitInstanceId}) → {path.durationDays}d";
            v.estimatedCompletionGameTime = job.completionGameTime;

            Log.Info($"[Modernization] INTERNAL #{job.jobId} vehicle#{vehicleId} {path.displayName} " +
                     $"@ ServicePit#{servicePitInstanceId} (Hall lvl {hallLvl}), " +
                     $"koszt {costPln / 1_000_000f:F1}M zł, phase={job.state}, " +
                     $"complete za {path.durationDays} dni");
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
                // MM-18: skip jobs w fazie EnRoute (DepotMovementSimulator tickuje, callback przejmie)
                if (job.state == ServiceJobState.EnRoute) continue;
                if (job.state == ServiceJobState.Completed || job.state == ServiceJobState.Failed)
                {
                    _activeJobs.RemoveAt(i);
                    continue;
                }
                if (nowGameTime < job.completionGameTime) continue;

                ApplyEffect(job, nowGameTime);
                job.state = ServiceJobState.Completed;
                _activeJobs.RemoveAt(i);
                completed++;
                OnJobCompleted?.Invoke(job);
                Log.Info($"[Modernization] COMPLETED #{job.jobId} vehicle#{job.vehicleId} {job.pathId}");
            }
            if (completed > 0) FleetService.NotifyOwnedChanged();
            return completed;
        }

        static void ApplyEffect(ModernizationJob job, long nowGameTime)
        {
            var v = FleetService.GetOwnedById(job.vehicleId);
            if (v == null) return;

            var path = ModernizationPathCatalog.GetByPathId(job.pathId);
            if (path == null)
            {
                Log.Error($"[Modernization] ApplyEffect: path '{job.pathId}' nieznany — pojazd zostaje as-is");
                v.status = FleetVehicleStatus.StoppedInDepot;
                v.currentTask = null;
                return;
            }

            // Update vehicle parameters z path overrides
            string oldSeriesId = v.seriesId;
            v.seriesId = path.targetSeriesId;
            if (!string.IsNullOrEmpty(path.targetDisplaySeries))
                v.series = path.targetDisplaySeries;
            v.maxSpeedKmh = path.newMaxSpeedKmh;
            v.powerKw = path.newPowerKw;
            v.comfortClass = path.newComfortClass;
            v.reliabilityScore = path.newReliabilityScore;

            // Comfort features merge
            if (path.newComfortFeatures != null && path.newComfortFeatures.Length > 0)
            {
                if (v.comfortFeatures == null) v.comfortFeatures = new System.Collections.Generic.List<string>();
                foreach (var f in path.newComfortFeatures)
                    if (!v.comfortFeatures.Contains(f)) v.comfortFeatures.Add(f);
            }

            // History
            v.history.Add(new MaintenanceRecord
            {
                gameTimeSeconds = nowGameTime,
                recordType = MaintenanceRecordTypes.Modernization(path.displayName),
                description = $"{oldSeriesId} → {path.targetSeriesId} ({(job.mode == ModernizationMode.External ? "ZNTK" : "Własny warsztat")})",
                cost = job.costPlnTotal,
                mileageAtRecord = v.mileageKm,
            });

            v.status = FleetVehicleStatus.StoppedInDepot;
            v.currentTask = null;

            // Reputation bonus przez hook (Fleet.asmdef nie referuje Timetable, hook-based DI).
            // Set'owane przez Timetable.Economy bootstrap. Null = silent.
            OnModernizationCompletedReputationHook?.Invoke(v.id, path.displayName);

            Log.Info($"[Modernization] APPLIED #{job.jobId}: vehicle#{v.id} {oldSeriesId} → {v.seriesId} " +
                     $"(vmax {v.maxSpeedKmh} km/h, power {v.powerKw} kW, comfort {v.comfortClass})");
        }

        // ════════════════════════════════════════════════════════
        //  Cancel + lookup
        // ════════════════════════════════════════════════════════

        public static ModernizationJob GetActiveJobForVehicle(int vehicleId)
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
                j => j.costPlnTotal,
                (j, _, _) => OnJobCancelled?.Invoke(j),
                "Modernization");

        public static void ResetAll()
        {
            _activeJobs.Clear();
            _nextJobId = 1;
        }

        /// <summary>BUG-021: accessor dla save.</summary>
        public static int GetNextJobId() => _nextJobId;

        /// <summary>BUG-021: restore state z save (po ResetAll).</summary>
        public static void RestoreFromSave(IList<ModernizationJob> jobs, int nextJobId)
        {
            _activeJobs.Clear();
            if (jobs != null) _activeJobs.AddRange(jobs);
            _nextJobId = nextJobId > 0 ? nextJobId : 1;
            RecoverInterruptedMovementAfterLoad();
        }

        /// <summary>Recover jobs that lost their runtime-only DepotMoveTask during load.</summary>
        static void RecoverInterruptedMovementAfterLoad()
            => ServiceJobHelpers.MarkPendingMovementRecovery(
                _activeJobs,
                FleetVehicleStatus.OutOfService,
                j =>
                {
                    var path = ModernizationPathCatalog.GetByPathId(j.pathId);
                    return path != null
                        ? $"Modernizacja {path.displayName} — wznawianie po load -> {path.durationDays}d"
                        : "Modernizacja — wznawianie po load";
                },
                "Modernization");

        /// <summary>Czy slot ServicePit jest zajęty przez aktywną modernizację (Internal mode).</summary>
        public static bool IsServicePitOccupied(int servicePitInstanceId)
        {
            foreach (var j in _activeJobs)
                if (j.mode == ModernizationMode.Internal &&
                    j.internalServicePitInstanceId == servicePitInstanceId)
                    return true;
            return false;
        }
    }
}
