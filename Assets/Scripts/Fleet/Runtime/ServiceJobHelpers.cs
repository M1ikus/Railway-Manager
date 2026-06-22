using System;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Economy;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// A1 partial: wspólne helpery dla job services. Aktualnie obsługuje recovery
    /// EnRoute → Servicing po save load (4 service'y miały tę samą funkcję z drobnymi
    /// różnicami statusu pojazdu/task label).
    ///
    /// Docelowo (pełny A1) zostanie rozbudowany o unified Cancel z refundem per faza,
    /// MarkMovementCompleted/Failed, plus ServiceJobBase abstract pattern.
    /// </summary>
    public static class ServiceJobHelpers
    {
        /// <summary>
        /// TD-031: po load save jobs w fazie EnRoute tracą runtime <c>DepotMoveTask</c>
        /// (delegat <c>onCompleted</c> nieserializowalny). Pozycja składu jest jednak zachowana
        /// (occupancy grafu + visual odtworzone przez DepotSavable), więc zamiast teleportować
        /// pojazd na stanowisko (stary <c>RecoverEnRouteAsServicing</c> — desync visual↔logika),
        /// ZOSTAWIAMY job w fazie EnRoute i oznaczamy pojazd jako „wznawianie po load".
        ///
        /// Faktyczne wznowienie ruchu robi watchdog w Maintenance (OutdoorEquipmentMovementBridge
        /// / WorkshopManager) — wykrywa job EnRoute bez aktywnego <c>DepotMoveTask</c> i re-issue
        /// manewr (analogicznie do tego jak <c>DeliveryService</c> domyka osierocone dostawy).
        /// Robimy to przez watchdog a nie tutaj, bo <c>RestoreFromSave</c> wykonuje się PRZED
        /// odtworzeniem grafu torów (ModuleOrder: maintenance/maintenanceJobs przed depot_3d,
        /// load z MainMenu deferuje depot_3d aż scena Depot się załaduje).
        ///
        /// Ustawia status pojazdu (<paramref name="pendingVehicleStatus"/>) + currentTask
        /// (<paramref name="taskLabel"/>) + estimatedCompletionGameTime (przybliżenie do UI;
        /// recompute po realnym dotarciu w <c>MarkMovementCompleted</c>). NIE zmienia
        /// <see cref="IServiceJobWithMovement.JobState"/> — zostaje EnRoute (marker orphana).
        /// </summary>
        public static int MarkPendingMovementRecovery<TJob>(
            IList<TJob> jobs,
            FleetVehicleStatus pendingVehicleStatus,
            System.Func<TJob, string> taskLabel,
            string logTag)
            where TJob : class, IServiceJobWithMovement
        {
            if (jobs == null || jobs.Count == 0) return 0;

            int pending = 0;
            foreach (var job in jobs)
            {
                if (job == null || job.JobState != ServiceJobState.EnRoute) continue;

                var v = FleetService.GetOwnedById(job.JobVehicleId);
                if (v != null)
                {
                    v.status = pendingVehicleStatus;
                    v.currentTask = taskLabel != null ? taskLabel(job) : null;
                    v.estimatedCompletionGameTime = job.JobCompletionGameTime;
                }

                pending++;
            }

            if (pending > 0)
            {
                Log.Info($"[{logTag}] {pending} EnRoute job(s) oczekuje na wznowienie ruchu po load " +
                         "(watchdog re-issue DepotMoveTask gdy scena zajezdni gotowa)");
                FleetService.NotifyOwnedChanged();
            }

            return pending;
        }

        /// <summary>
        /// A1 cz.2: unified cancel z polityką refundu per faza (spójnie z C2):
        /// 100% w EnRoute (effect nie zaczęty), 50% w Servicing (część materiałów zużyta),
        /// 0% w Completed/Failed (defensive — nie powinno tu trafić).
        ///
        /// <paramref name="getCostPln"/> ekstraktor kosztu pojedynczego job'a w PLN
        /// (caller konwertuje z groszy/innych jednostek jeśli trzeba).
        ///
        /// Returns true gdy job znaleziony i usunięty; false gdy brak.
        /// Wywołuje <paramref name="onCancelled"/> z (job, refundPln, phaseAtCancel)
        /// dla per-service custom event/log.
        /// </summary>
        public static bool CancelWithRefundPolicy<TJob>(
            IList<TJob> jobs,
            int vehicleId,
            long nowGameTime,
            Func<TJob, long> getCostPln,
            Action<TJob, long, ServiceJobState> onCancelled,
            string logTag)
            where TJob : class, IServiceJobWithMovement
        {
            if (jobs == null) return false;

            TJob found = null;
            foreach (var j in jobs)
            {
                if (j != null && j.JobVehicleId == vehicleId) { found = j; break; }
            }
            if (found == null) return false;

            long costPln = getCostPln != null ? getCostPln(found) : 0L;
            long refundPln = found.JobState switch
            {
                ServiceJobState.EnRoute   => costPln,
                ServiceJobState.Servicing => costPln / 2,
                _                          => 0L,
            };
            MoneyLedger.Earn(refundPln * 100L, "service_refund", "anulowanie usługi (refund)");

            var v = FleetService.GetOwnedById(found.JobVehicleId);
            if (v != null)
            {
                v.status = FleetVehicleStatus.StoppedInDepot;
                v.currentTask = null;
                v.estimatedCompletionGameTime = nowGameTime;
            }

            var phaseAtCancel = found.JobState;
            jobs.Remove(found);
            onCancelled?.Invoke(found, refundPln, phaseAtCancel);

            Log.Info($"[{logTag}] CANCELLED vehicle#{vehicleId}, refund {refundPln:N0}zł (faza={phaseAtCancel})");
            FleetService.NotifyOwnedChanged();
            return true;
        }
    }
}
