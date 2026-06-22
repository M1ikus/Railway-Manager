using System;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Economy;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// MM-11 / MM-D17 — lifecycle modyfikacji posiadanych pojazdów (External + Internal
    /// symetrycznie do <see cref="ModernizationJobService"/>, ale lighter — nie zmienia
    /// seriesId, tylko parametry).
    ///
    /// Workflow analog do MM-10:
    /// <list type="number">
    /// <item>UI w FleetPanelUI.DetailPopup (MM-13 polish) wyświetla applicable modifications
    /// per pojazd (z <see cref="VehicleModificationCatalog.GetApplicableFor"/>)</item>
    /// <item>Gracz wybiera External (ZNTK z modernizationAvailable=true) lub Internal
    /// (Hall lvl ≥ minHallLevelInternal)</item>
    /// <item><see cref="Schedule"/> → vehicle status OutOfService/InRepair, koszt pobrany</item>
    /// <item><see cref="CheckCompletions"/> wpięte w WorkshopManager.Update — po
    /// durationDays apply effect</item>
    /// <item>ApplyEffect: update parametrów per ModificationType (BogieReplacement,
    /// ComfortAddition, BodyFunctionChange)</item>
    /// </list>
    ///
    /// Cancel: 50% refund (analogicznie do innych services).
    /// </summary>
    public static class VehicleModificationJobService
    {
        const long DAY_SECONDS = 86400L;

        static readonly List<VehicleModificationJob> _activeJobs = new();
        static int _nextJobId = 1;

        public static IReadOnlyList<VehicleModificationJob> ActiveJobs => _activeJobs;

        public static event Action<VehicleModificationJob> OnJobScheduled;
        public static event Action<VehicleModificationJob> OnJobCompleted;
        public static event Action<VehicleModificationJob> OnJobCancelled;

        /// <summary>
        /// MM-18: Hook movement scheduling dla Internal mode. Bridge w Timetable resolves
        /// ServicePit access track + EnqueueMove. Returns true gdy real movement scheduled.
        /// </summary>
        public static Func<VehicleModificationJob, bool> RequestMovementHook;

        /// <summary>MM-18: Bridge informuje że pojazd dotarł na ServicePit.</summary>
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
                Log.Info($"[VehicleModification] EnRoute → Servicing #{job.jobId} vehicle#{job.vehicleId}");
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
                MoneyLedger.Earn(job.costPlnTotal * 100L, "modification_refund", $"Modyfikacja v#{job.vehicleId} anulowana");
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
                Log.Warn($"[VehicleModification] MOVEMENT FAILED #{job.jobId} vehicle#{job.vehicleId} — {reason}");
                FleetService.NotifyOwnedChanged();
                return;
            }
        }

        // ════════════════════════════════════════════════════════
        //  Schedule (External + Internal w jednej metodzie z parametrem mode)
        // ════════════════════════════════════════════════════════

        public static ScheduleResult ScheduleExternal(int vehicleId, string modId, string workshopId, long nowGameTime)
        {
            var workshop = ExternalWorkshopCatalog.GetById(workshopId);
            if (workshop == null) { Log.Warn($"[VehicleModification] workshop '{workshopId}' not found"); return ScheduleResult.WorkshopNotFound; }
            if (!workshop.modernizationAvailable)
            {
                Log.Warn($"[VehicleModification] {workshop.name} nie obsługuje modyfikacji " +
                         "(modernizationAvailable=false)"); return ScheduleResult.WorkshopUnavailable;
            }
            return ScheduleInternalImpl(vehicleId, modId, ModernizationMode.External,
                                         workshopId, -1, 0f, 0, nowGameTime);
        }

        public static ScheduleResult ScheduleInternal(int vehicleId, string modId, int servicePitInstanceId,
                                             float servicePitMaxLength, int hallLvl, long nowGameTime)
        {
            return ScheduleInternalImpl(vehicleId, modId, ModernizationMode.Internal,
                                         null, servicePitInstanceId, servicePitMaxLength, hallLvl, nowGameTime);
        }

        static ScheduleResult ScheduleInternalImpl(int vehicleId, string modId, ModernizationMode mode,
                                          string workshopId, int servicePitInstanceId,
                                          float servicePitMaxLength, int hallLvl, long nowGameTime)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            if (v == null) { Log.Warn($"[VehicleModification] vehicle#{vehicleId} not found"); return ScheduleResult.VehicleNotFound; }
            if (GetActiveJobForVehicle(vehicleId) != null)
            {
                Log.Warn($"[VehicleModification] vehicle#{vehicleId} already has active job"); return ScheduleResult.VehicleHasActiveJob;
            }

            var mod = VehicleModificationCatalog.GetByModId(modId);
            if (mod == null) { Log.Warn($"[VehicleModification] modId '{modId}' not found"); return ScheduleResult.ModNotApplicable; }
            if (!VehicleModificationCatalog.IsApplicable(mod, v))
            {
                Log.Warn($"[VehicleModification] {mod.displayName} nie applicable dla vehicle#{vehicleId} " +
                         $"({v.series}, type={v.type})"); return ScheduleResult.ModNotApplicable;
            }

            long costPln;
            if (mode == ModernizationMode.External)
            {
                costPln = mod.externalCostPln;
            }
            else
            {
                if (hallLvl < mod.minHallLevelInternal)
                {
                    Log.Warn($"[VehicleModification] Hall lvl {hallLvl} za niski dla {mod.displayName} " +
                             $"(wymaga ≥{mod.minHallLevelInternal})"); return ScheduleResult.HallLevelTooLow;
                }
                if (servicePitMaxLength + 0.01f < v.lengthM)
                {
                    Log.Warn($"[VehicleModification] ServicePit length {servicePitMaxLength}m za krótki " +
                             $"dla pojazdu {v.lengthM}m"); return ScheduleResult.ServicePitTooShort;
                }
                costPln = mod.internalCostPln;
            }

            if (GameState.Money < costPln)
            {
                Log.Warn($"[VehicleModification] Brak gotówki: potrzeba {costPln:N0}zł, masz {GameState.Money:N0}zł");
                return ScheduleResult.InsufficientFunds;
            }

            MoneyLedger.Spend(costPln * 100L, "modification", "modyfikacja pojazdu");

            long durationSec = mod.durationDays * DAY_SECONDS;
            var job = new VehicleModificationJob
            {
                jobId = _nextJobId++,
                vehicleId = vehicleId,
                modId = modId,
                mode = mode,
                externalWorkshopId = workshopId,
                internalServicePitInstanceId = servicePitInstanceId,
                startedGameTime = nowGameTime,
                completionGameTime = nowGameTime + durationSec,
                costPlnTotal = costPln,
                durationSec = durationSec,
                // MM-18: External = abstract (Servicing immediate), Internal = EnRoute
                state = (mode == ModernizationMode.Internal) ? ServiceJobState.EnRoute : ServiceJobState.Servicing,
            };
            _activeJobs.Add(job);

            // MM-18: Internal — try schedule movement
            bool movementScheduled = false;
            if (mode == ModernizationMode.Internal && RequestMovementHook != null)
            {
                try { movementScheduled = RequestMovementHook.Invoke(job); }
                catch (Exception ex) { Log.Warn($"[VehicleModification] RequestMovementHook threw: {ex.Message}"); }
            }
            if (mode == ModernizationMode.Internal && !movementScheduled)
            {
                job.state = ServiceJobState.Servicing;
                job.arrivedAtTargetGameTime = nowGameTime;
            }

            v.status = (mode == ModernizationMode.External)
                ? FleetVehicleStatus.OutOfService
                : (movementScheduled ? FleetVehicleStatus.OutOfService : FleetVehicleStatus.InRepair);
            v.currentTask = movementScheduled
                ? $"{mod.displayName} — w drodze do slot#{servicePitInstanceId}"
                : $"{mod.displayName} ({(mode == ModernizationMode.External ? "ZNTK" : "Własny warsztat")}) → {mod.durationDays}d";
            v.estimatedCompletionGameTime = job.completionGameTime;

            Log.Info($"[VehicleModification] {mode.ToString().ToUpper()} #{job.jobId} vehicle#{vehicleId} " +
                     $"{mod.displayName}, koszt {costPln / 1_000_000f:F2}M zł, phase={job.state}, {mod.durationDays}d");
            FleetService.NotifyOwnedChanged();
            OnJobScheduled?.Invoke(job);
            return ScheduleResult.Success;
        }

        // ════════════════════════════════════════════════════════
        //  Completion check (z WorkshopManager.Update)
        // ════════════════════════════════════════════════════════

        public static int CheckCompletions(long nowGameTime)
        {
            int completed = 0;
            for (int i = _activeJobs.Count - 1; i >= 0; i--)
            {
                var job = _activeJobs[i];
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
                Log.Info($"[VehicleModification] COMPLETED #{job.jobId} vehicle#{job.vehicleId} {job.modId}");
            }
            if (completed > 0) FleetService.NotifyOwnedChanged();
            return completed;
        }

        static void ApplyEffect(VehicleModificationJob job, long nowGameTime)
        {
            var v = FleetService.GetOwnedById(job.vehicleId);
            if (v == null) return;

            var mod = VehicleModificationCatalog.GetByModId(job.modId);
            if (mod == null)
            {
                Log.Error($"[VehicleModification] ApplyEffect: modId '{job.modId}' nieznany");
                v.status = FleetVehicleStatus.StoppedInDepot;
                v.currentTask = null;
                return;
            }

            // Apply effect per type
            switch (mod.type)
            {
                case ModificationType.BogieReplacement:
                    if (!string.IsNullOrEmpty(mod.newBogieTypeId))
                    {
                        if (v.sourceConfiguration == null) v.sourceConfiguration = new VehicleConfiguration();
                        v.sourceConfiguration.bogieTypeId = mod.newBogieTypeId;
                        // Vmax bonus per bogie type (placeholder, M-Balance dopracuje):
                        // klockowy → tarczowy: +20 km/h, tarczowy → tarczowy-szynowy: +30 km/h
                        int vmaxBonus = mod.newBogieTypeId switch
                        {
                            "tarczowy" => 20,
                            "tarczowy-szynowy" => 30,
                            _ => 0,
                        };
                        v.maxSpeedKmh += vmaxBonus;
                        Log.Info($"[VehicleModification] BogieReplacement: vehicle#{v.id} → {mod.newBogieTypeId}, +{vmaxBonus} km/h vmax (now {v.maxSpeedKmh})");
                    }
                    break;

                case ModificationType.ComfortAddition:
                    v.comfortClass += mod.comfortClassDelta;
                    if (v.comfortClass > 5) v.comfortClass = 5;
                    if (mod.addComfortFeatures != null && mod.addComfortFeatures.Length > 0)
                    {
                        if (v.comfortFeatures == null) v.comfortFeatures = new List<string>();
                        foreach (var f in mod.addComfortFeatures)
                            if (!v.comfortFeatures.Contains(f)) v.comfortFeatures.Add(f);
                    }
                    Log.Info($"[VehicleModification] ComfortAddition: vehicle#{v.id} comfort {v.comfortClass}, " +
                             $"features: {string.Join(",", v.comfortFeatures ?? new List<string>())}");
                    break;

                case ModificationType.BodyFunctionChange:
                    // MVP stub: tylko comfort + purpose change. Pełen interiorMix swap w polish
                    // (wymaga FleetPanelUI.Configurator integration z M-FC).
                    if (!string.IsNullOrEmpty(mod.newDefaultPurpose))
                    {
                        if (v.suggestedCategoryGroups == null) v.suggestedCategoryGroups = new List<string>();
                        v.suggestedCategoryGroups.Clear();
                        v.suggestedCategoryGroups.Add(mod.newDefaultPurpose);
                    }
                    if (mod.comfortClassDelta != 0)
                    {
                        v.comfortClass += mod.comfortClassDelta;
                        if (v.comfortClass > 5) v.comfortClass = 5;
                    }
                    Log.Info($"[VehicleModification] BodyFunctionChange: vehicle#{v.id} → purpose " +
                             $"{mod.newDefaultPurpose} (interiorMix swap stub, polish w MM-13)");
                    break;
            }

            // Vmax override (gdy mod.newMaxSpeedKmh > 0)
            if (mod.newMaxSpeedKmh > 0) v.maxSpeedKmh = mod.newMaxSpeedKmh;

            // History
            v.history.Add(new MaintenanceRecord
            {
                gameTimeSeconds = nowGameTime,
                recordType = MaintenanceRecordTypes.Modification(mod.displayName),
                description = $"{mod.type} ({(job.mode == ModernizationMode.External ? "ZNTK" : "Własny warsztat")})",
                cost = job.costPlnTotal,
                mileageAtRecord = v.mileageKm,
            });

            v.status = FleetVehicleStatus.StoppedInDepot;
            v.currentTask = null;
        }

        // ════════════════════════════════════════════════════════
        //  Cancel + lookup
        // ════════════════════════════════════════════════════════

        public static VehicleModificationJob GetActiveJobForVehicle(int vehicleId)
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
                "VehicleModification");

        public static void ResetAll()
        {
            _activeJobs.Clear();
            _nextJobId = 1;
        }

        /// <summary>BUG-021: accessor dla save.</summary>
        public static int GetNextJobId() => _nextJobId;

        /// <summary>BUG-021: restore state z save (po ResetAll).</summary>
        public static void RestoreFromSave(IList<VehicleModificationJob> jobs, int nextJobId)
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
                j =>
                {
                    var mod = VehicleModificationCatalog.GetByModId(j.modId);
                    return mod != null
                        ? $"{mod.displayName} — wznawianie po load -> {mod.durationDays}d"
                        : "Modyfikacja pojazdu — wznawianie po load";
                },
                "VehicleModification");

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
