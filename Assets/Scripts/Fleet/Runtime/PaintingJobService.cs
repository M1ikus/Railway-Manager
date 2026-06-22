using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Economy;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-9: Service do schedulingu i wykonywania malowania pojazdów w ZNTK.
    /// Lifecycle:
    /// 1. <see cref="Schedule"/> — gracz wysyła pojazd, płaci, status = OutOfService.
    /// 2. <see cref="CheckCompletions"/> — periodically (przy refresh panelu, market refresh)
    ///    sprawdza czy nowGameTime &gt; completionGameTime. Jeśli tak: apply newPaint do
    ///    FleetVehicleData.paintDefinition, status = StoppedInDepot, cleanlinessPercent=100.
    /// </summary>
    public static class PaintingJobService
    {
        private static readonly List<PaintingJob> _activeJobs = new();
        public static IReadOnlyList<PaintingJob> ActiveJobs => _activeJobs;

        /// <summary>Czas trwania dnia gry w sekundach (zsynchronizowane z innymi M7 services).</summary>
        private const long DAY_SECONDS = 86400;

        /// <summary>BUG-021: reset state (nowa gra / load).</summary>
        public static void ResetAll()
        {
            _activeJobs.Clear();
        }

        /// <summary>BUG-021: restore z save. Wywoływane przez MaintenanceJobsSavable.</summary>
        public static void RestoreFromSave(IList<PaintingJob> jobs)
        {
            _activeJobs.Clear();
            if (jobs != null) _activeJobs.AddRange(jobs);
        }

        /// <summary>
        /// Zlecenie malowania pojazdu w ZNTK. Pobiera koszt z konta gracza, zmienia status pojazdu.
        /// C1: zwraca <see cref="ScheduleResult"/> z typowanym powodem (UI może pokazać user message).
        /// </summary>
        public static ScheduleResult Schedule(int vehicleId, string workshopId, PaintDefinition newPaint, long nowGameTime)
        {
            var v = FleetService.GetOwnedById(vehicleId);
            if (v == null) { Log.Warn($"[PaintingJobService] Schedule: vehicle id={vehicleId} not found"); return ScheduleResult.VehicleNotFound; }

            var workshop = ExternalWorkshopCatalog.GetById(workshopId);
            if (workshop == null) { Log.Warn($"[PaintingJobService] Schedule: workshop {workshopId} not found"); return ScheduleResult.WorkshopNotFound; }

            if (workshop.paintCostPln <= 0)
            {
                Log.Warn($"[PaintingJobService] {workshop.name} nie oferuje usługi malowania (cost=0)");
                return ScheduleResult.WorkshopUnavailable;
            }

            // Cena
            if (GameState.Money < workshop.paintCostPln)
            {
                Log.Warn($"[PaintingJobService] Brak gotówki: {workshop.paintCostPln:N0} zł (masz {GameState.Money:N0})");
                return ScheduleResult.InsufficientFunds;
            }

            // Sprawdź czy pojazd już w paint job
            if (GetActiveJobForVehicle(vehicleId) != null)
            {
                Log.Warn($"[PaintingJobService] Vehicle id={vehicleId} już ma aktywny paint job");
                return ScheduleResult.VehicleHasActiveJob;
            }

            // Płać
            MoneyLedger.Spend(workshop.paintCostPln * 100L, "painting", "malowanie ZNTK");

            // Zaplanuj
            var job = new PaintingJob
            {
                vehicleId = vehicleId,
                workshopId = workshopId,
                scheduledGameTime = nowGameTime,
                completionGameTime = nowGameTime + workshop.paintTimeDays * DAY_SECONDS,
                costPln = workshop.paintCostPln,
                newPaint = newPaint ?? v.paintDefinition // jeśli null, użyj aktualnego (refresh)
            };
            _activeJobs.Add(job);

            // Vehicle status: OutOfService
            v.status = FleetVehicleStatus.OutOfService;
            v.currentTask = $"Malowanie w {workshop.name} → {workshop.paintTimeDays} dni";
            v.estimatedCompletionGameTime = job.completionGameTime;

            Log.Info($"[PaintingJobService] Scheduled paint job: vehicle={v.series}-{v.number} → {workshop.name}, " +
                     $"cost={workshop.paintCostPln:N0} zł, {workshop.paintTimeDays} dni");
            FleetService.NotifyOwnedChanged();
            return ScheduleResult.Success;
        }

        /// <summary>
        /// Sprawdza wszystkie aktywne jobs — kompletne (nowGameTime &gt;= completionGameTime)
        /// applikuje newPaint i przywraca pojazd do StoppedInDepot. Zwraca liczbę ukończonych.
        /// </summary>
        public static int CheckCompletions(long nowGameTime)
        {
            int completed = 0;
            for (int i = _activeJobs.Count - 1; i >= 0; i--)
            {
                var job = _activeJobs[i];
                if (nowGameTime < job.completionGameTime) continue;

                var v = FleetService.GetOwnedById(job.vehicleId);
                if (v == null)
                {
                    // Vehicle zniknął (np. usunięty) — usuwamy job też
                    _activeJobs.RemoveAt(i);
                    continue;
                }

                // Apply paint + status
                v.paintDefinition = job.newPaint;
                v.cleanlinessPercent = 100f;
                v.status = FleetVehicleStatus.StoppedInDepot;
                v.currentTask = null;

                v.history.Add(new MaintenanceRecord
                {
                    gameTimeSeconds = nowGameTime,
                    recordType = MaintenanceRecordTypes.PaintZntk,
                    description = $"Malowanie ukończone w {ExternalWorkshopCatalog.GetById(job.workshopId)?.name ?? job.workshopId}",
                    cost = job.costPln,
                    mileageAtRecord = v.mileageKm
                });

                _activeJobs.RemoveAt(i);
                completed++;
                Log.Info($"[PaintingJobService] Completed paint job for vehicle id={job.vehicleId}");
            }
            if (completed > 0) FleetService.NotifyOwnedChanged();
            return completed;
        }

        /// <summary>Zwraca aktywny job dla pojazdu lub null.</summary>
        public static PaintingJob GetActiveJobForVehicle(int vehicleId)
        {
            foreach (var j in _activeJobs)
                if (j.vehicleId == vehicleId) return j;
            return null;
        }

        /// <summary>Anuluje paint job (zwraca refund 50% gracza). Zwraca true gdy udało się.</summary>
        public static bool Cancel(int vehicleId, long nowGameTime)
        {
            var job = GetActiveJobForVehicle(vehicleId);
            if (job == null) return false;

            // 50% refund
            long refund = job.costPln / 2;
            MoneyLedger.Earn(refund * 100L, "painting_refund", "malowanie anulowane (50%)");

            var v = FleetService.GetOwnedById(vehicleId);
            if (v != null)
            {
                v.status = FleetVehicleStatus.StoppedInDepot;
                v.currentTask = null;
                v.estimatedCompletionGameTime = nowGameTime;
            }

            _activeJobs.Remove(job);
            Log.Info($"[PaintingJobService] Cancelled paint job for vehicle id={vehicleId}, refund {refund:N0} zł");
            FleetService.NotifyOwnedChanged();
            return true;
        }
    }
}
