using System;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Economy;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// MM-12: aktywny self-paint job (analog <see cref="PaintingJob"/> dla ZNTK,
    /// ale z paintBayInstanceId zamiast workshopId).
    /// </summary>
    [Serializable]
    public class SelfPaintingJob : IServiceJobWithMovement
    {
        public int jobId;
        public int vehicleId;

        int IServiceJobWithMovement.JobVehicleId => vehicleId;
        long IServiceJobWithMovement.JobStartedGameTime => scheduledGameTime;
        long IServiceJobWithMovement.JobCompletionGameTime => completionGameTime;
        long IServiceJobWithMovement.JobArrivedAtTargetGameTime
        {
            get => arrivedAtTargetGameTime;
            set => arrivedAtTargetGameTime = value;
        }
        ServiceJobState IServiceJobWithMovement.JobState { get => state; set => state = value; }
        public int paintBayInstanceId;    // PlacedFurnitureItem.instanceId (paint_bay)
        public long scheduledGameTime;
        public long completionGameTime;
        public long costPln;
        public PaintDefinition newPaint;

        // ── MM-18: state machine ─────────────────────────────────────
        public ServiceJobState state = ServiceJobState.Servicing;
        public int targetTrackId = -1;
        public int originTrackId = -1;
        public int consistId = -1;
        public long durationSec;
        public long arrivedAtTargetGameTime;
    }

    /// <summary>
    /// MM-12 / MM-D17 — service dla self-paint w własnym warsztacie (paint_bay mebel).
    /// Reuse ~90% wzorca z <see cref="PaintingJobService"/> (M-FC-9 ZNTK paint), różnice:
    ///
    /// <list type="bullet">
    /// <item><b>Cost</b>: tylko farba (~30-40% kosztu ZNTK), placeholder 50k zł</item>
    /// <item><b>Time</b>: zależny od Hall lvl (lvl2=7d baseline, lvl5=2d szybciej)</item>
    /// <item><b>Wymagania</b>: paint_bay mebel postawiony w Hall lvl ≥ 2 (decyzja MM-12)</item>
    /// <item><b>Brak delivery</b>: pojazd już w zajezdni, instant start (vs ZNTK 2× delivery)</item>
    /// </list>
    ///
    /// UI w paint editor (MM-13 polish): toggle "ZNTK / Własny warsztat" gdy gracz ma
    /// paint_bay postawiony.
    /// </summary>
    public static class SelfPaintingService
    {
        const long DAY_SECONDS = 86400L;

        // Min Hall lvl + base cost + time-per-lvl table — patrz FleetBalanceConstants.SelfPainting*.
        // Re-exposed dla istniejących callerów (UI/save):
        public const int MinHallLevel = FleetBalanceConstants.SelfPaintingMinHallLevel;
        public const long BasePaintCostPln = FleetBalanceConstants.SelfPaintingBaseCostPln;

        static readonly List<SelfPaintingJob> _activeJobs = new();
        static int _nextJobId = 1;

        public static IReadOnlyList<SelfPaintingJob> ActiveJobs => _activeJobs;

        public static event Action<SelfPaintingJob> OnJobScheduled;
        public static event Action<SelfPaintingJob> OnJobCompleted;
        public static event Action<SelfPaintingJob> OnJobCancelled;

        /// <summary>MM-18: Hook movement scheduling. Bridge w Timetable resolves paint_bay access track.</summary>
        public static Func<SelfPaintingJob, bool> RequestMovementHook;

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
                Log.Info($"[SelfPainting] EnRoute → Servicing #{job.jobId} vehicle#{job.vehicleId}");
                FleetService.NotifyOwnedChanged();
                return;
            }
        }

        public static void MarkMovementFailed(int jobId, long nowGameTime, string reason)
        {
            for (int i = _activeJobs.Count - 1; i >= 0; i--)
            {
                var job = _activeJobs[i];
                if (job.jobId != jobId) continue;
                if (job.state != ServiceJobState.EnRoute) return;
                MoneyLedger.Earn(job.costPln * 100L, "self_painting_refund", $"Malowanie własne v#{job.vehicleId} anulowane");
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
                Log.Warn($"[SelfPainting] MOVEMENT FAILED #{job.jobId} vehicle#{job.vehicleId} — {reason}");
                FleetService.NotifyOwnedChanged();
                return;
            }
        }

        // ════════════════════════════════════════════════════════
        //  Time / Cost calculations (Hall lvl based)
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// MM-12: czas malowania w dniach w zależności od Hall lvl.
        /// Lvl 2 baseline (7d), lvl 5 szybciej (2d). Lvl &lt; 2 → 0 (job nieavailable).
        /// Tabela w <see cref="FleetBalanceConstants.GetSelfPaintingDaysForHallLvl"/>.
        /// </summary>
        public static int GetPaintTimeDays(int hallLvl)
            => FleetBalanceConstants.GetSelfPaintingDaysForHallLvl(hallLvl);

        // ════════════════════════════════════════════════════════
        //  Schedule
        // ════════════════════════════════════════════════════════

        public static ScheduleResult Schedule(int vehicleId, int paintBayInstanceId, PaintDefinition newPaint,
                                     int hallLvl, long nowGameTime)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            if (v == null) { Log.Warn($"[SelfPainting] vehicle#{vehicleId} not found"); return ScheduleResult.VehicleNotFound; }
            if (GetActiveJobForVehicle(vehicleId) != null)
            {
                Log.Warn($"[SelfPainting] vehicle#{vehicleId} already has active job"); return ScheduleResult.VehicleHasActiveJob;
            }

            if (hallLvl < MinHallLevel)
            {
                Log.Warn($"[SelfPainting] Hall lvl {hallLvl} za niski (wymaga ≥{MinHallLevel}). " +
                         "Awansuj Hall lub użyj ZNTK paint job"); return ScheduleResult.HallLevelTooLow;
            }

            int days = GetPaintTimeDays(hallLvl);
            if (days <= 0)
            {
                Log.Warn($"[SelfPainting] Hall lvl {hallLvl} nie obsługuje self-paint"); return ScheduleResult.HallLevelTooLow;
            }

            if (GameState.Money < BasePaintCostPln)
            {
                Log.Warn($"[SelfPainting] Brak gotówki: potrzeba {BasePaintCostPln:N0}zł, masz {GameState.Money:N0}zł");
                return ScheduleResult.InsufficientFunds;
            }

            MoneyLedger.Spend(BasePaintCostPln * 100L, "self_painting", "malowanie własne (PaintBay)");

            long durationSec = days * DAY_SECONDS;
            var job = new SelfPaintingJob
            {
                jobId = _nextJobId++,
                vehicleId = vehicleId,
                paintBayInstanceId = paintBayInstanceId,
                scheduledGameTime = nowGameTime,
                completionGameTime = nowGameTime + durationSec,
                costPln = BasePaintCostPln,
                newPaint = newPaint ?? v.paintDefinition,
                durationSec = durationSec,
                state = ServiceJobState.EnRoute, // MM-18
            };
            _activeJobs.Add(job);

            // MM-18: hook → Bridge robi real EnqueueMove
            bool movementScheduled = false;
            if (RequestMovementHook != null)
            {
                try { movementScheduled = RequestMovementHook.Invoke(job); }
                catch (Exception ex) { Log.Warn($"[SelfPainting] RequestMovementHook threw: {ex.Message}"); }
            }
            if (!movementScheduled)
            {
                job.state = ServiceJobState.Servicing;
                job.arrivedAtTargetGameTime = nowGameTime;
            }

            v.status = movementScheduled ? FleetVehicleStatus.OutOfService : FleetVehicleStatus.InRepair;
            v.currentTask = movementScheduled
                ? $"Malowanie self-paint — w drodze do paint_bay#{paintBayInstanceId}"
                : $"Malowanie self-paint (Hall lvl {hallLvl}) → {days} dni";
            v.estimatedCompletionGameTime = job.completionGameTime;

            Log.Info($"[SelfPainting] SCHEDULED #{job.jobId} vehicle#{vehicleId} self-paint @ paint_bay#{paintBayInstanceId}, " +
                     $"Hall lvl {hallLvl}, phase={job.state}, {days}d, koszt {BasePaintCostPln / 1000f:F0}k zł");
            FleetService.NotifyOwnedChanged();
            OnJobScheduled?.Invoke(job);
            return ScheduleResult.Success;
        }

        // ════════════════════════════════════════════════════════
        //  CheckCompletions (z WorkshopManager.Update)
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
                Log.Info($"[SelfPainting] COMPLETED #{job.jobId} vehicle#{job.vehicleId}");
            }
            if (completed > 0) FleetService.NotifyOwnedChanged();
            return completed;
        }

        static void ApplyEffect(SelfPaintingJob job, long nowGameTime)
        {
            var v = FleetService.GetOwnedById(job.vehicleId);
            if (v == null) return;

            v.paintDefinition = job.newPaint;
            v.cleanlinessPercent = 100f;
            v.status = FleetVehicleStatus.StoppedInDepot;
            v.currentTask = null;

            v.history.Add(new MaintenanceRecord
            {
                gameTimeSeconds = nowGameTime,
                recordType = MaintenanceRecordTypes.PaintSelf,
                description = $"Self-paint @ paint_bay#{job.paintBayInstanceId}",
                cost = job.costPln,
                mileageAtRecord = v.mileageKm,
            });
        }

        // ════════════════════════════════════════════════════════
        //  Cancel + lookup
        // ════════════════════════════════════════════════════════

        public static SelfPaintingJob GetActiveJobForVehicle(int vehicleId)
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
                j => j.costPln,
                (j, _, _) => OnJobCancelled?.Invoke(j),
                "SelfPainting");

        public static void ResetAll()
        {
            _activeJobs.Clear();
            _nextJobId = 1;
        }

        /// <summary>BUG-021: accessor dla save.</summary>
        public static int GetNextJobId() => _nextJobId;

        /// <summary>BUG-021: restore state z save (po ResetAll).</summary>
        public static void RestoreFromSave(IList<SelfPaintingJob> jobs, int nextJobId)
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
                _ => "Malowanie self-paint — wznawianie po load",
                "SelfPainting");
    }
}
