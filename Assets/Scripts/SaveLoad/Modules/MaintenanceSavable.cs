using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Maintenance;
using RailwayManager.Fleet; // ComponentType, PartCatalog

namespace RailwayManager.SaveLoad.Modules
{
    /// <summary>
    /// M13-7: Persystencja Maintenance — parts inventory + workshop slots +
    /// external ZNTK jobs.
    ///
    /// Module ID: "maintenance". Schema v1.
    ///
    /// Pola w bundle:
    /// - partsStock (JObject string→int) — Dictionary<ComponentType, int> ze stockiem
    /// - partsPending (JArray PendingPartOrder) — zamówienia w drodze
    /// - partsHistory (JArray PartPurchaseRecord) — historia zakupów (max 30)
    /// - workshopSlots (JArray WorkshopSlot) — sloty z occupancy
    /// - workshopExternalJobs (JArray OngoingExternalJob) — aktywne zadania ZNTK
    /// - workshopNextSlotId (int) - counter slotow warsztatowych
    ///
    /// 2026-05-15: refactor z reflection (NonPublic field access na _stock/_pending/
    /// _history/_slots/_externalJobs) na public `RestoreFromSave`/`ResetRuntime` API w
    /// PartInventoryService + WorkshopManager. Eliminuje silent corruption przy rename
    /// pola. WorkshopManager.RestoreFromSave istniał już wcześniej (BUG-021/BUG-088).
    ///
    /// Decyzje:
    /// - Komponenty per-pojazd (FleetVehicleData.components) już persystowane przez
    ///   FleetSavable (są pole pojazdu) — nie duplikujemy
    /// - DegradationService runtime (per-km tracking) → Phase 2 (M13-8)
    /// - BreakdownService active breakdowns → Phase 2 (M13-8)
    /// - WorkshopAssignmentService (mechanic→slot) → tu też (statyczne assignments per-slot)
    /// - PartInventoryService Stock<ComponentType, int> serialized as string-keyed
    ///   JObject (enum→string konwersja)
    /// </summary>
    public class MaintenanceSavable : ISavable
    {
        public string ModuleId => "maintenance";
        public int SchemaVersion => 1; // pre-EA reset 2026-05-15; bump po EA = real migrator

        public JObject Serialize()
        {
            var data = new JObject();

            var inv = PartInventoryService.Instance;
            if (inv != null)
            {
                var stockJObj = new JObject();
                foreach (var kv in inv.StockSnapshot)
                    stockJObj[kv.Key.ToString()] = kv.Value;
                data["partsStock"] = stockJObj;

                data["partsPending"] = JArray.FromObject(inv.PendingOrders);
                data["partsHistory"] = JArray.FromObject(inv.History);
            }

            var wm = WorkshopManager.Instance;
            if (wm != null)
            {
                data["workshopSlots"] = JArray.FromObject(wm.Slots);
                data["workshopExternalJobs"] = JArray.FromObject(wm.ExternalJobs);
                data["workshopNextSlotId"] = wm.GetNextSlotId();
            }

            // TD-037: misje rescue w toku (czysto timerowe — fazy + absolutne game-time finish'e)
            var rescue = RescueService.Instance;
            if (rescue != null)
                data["rescueOngoing"] = JArray.FromObject(rescue.BuildSnapshot());

            return data;
        }

        public void Deserialize(JObject data, int sourceVersion)
        {
            // PartInventoryService
            var inv = PartInventoryService.EnsureExists();

            var stock = new Dictionary<ComponentType, int>();
            if (data["partsStock"] is JObject stockJObj)
            {
                foreach (var kv in stockJObj)
                {
                    if (System.Enum.TryParse<ComponentType>(kv.Key, out var ct))
                        stock[ct] = kv.Value.Value<int>();
                }
            }

            var pending = new List<PendingPartOrder>();
            if (data["partsPending"] is JArray pendArr)
            {
                foreach (var item in pendArr)
                {
                    var po = item.ToObject<PendingPartOrder>();
                    if (po != null) pending.Add(po);
                }
            }

            var history = new List<PartPurchaseRecord>();
            if (data["partsHistory"] is JArray histArr)
            {
                foreach (var item in histArr)
                {
                    var rec = item.ToObject<PartPurchaseRecord>();
                    if (rec != null) history.Add(rec);
                }
            }

            inv.RestoreFromSave(stock, pending, history);

            // WorkshopManager — slots + jobs
            var wm = WorkshopManager.EnsureExists();
            if (wm == null)
            {
                Log.Warn("[MaintenanceSavable] WorkshopManager.Instance null — slots/jobs not restored. " +
                         "WorkshopManager bootstrapuje sie po WorkshopRoomDetection — load order issue.");
            }
            else
            {
                var slots = new List<WorkshopSlot>();
                if (data["workshopSlots"] is JArray slotsArr)
                {
                    foreach (var item in slotsArr)
                    {
                        var slot = item.ToObject<WorkshopSlot>();
                        if (slot != null) slots.Add(slot);
                    }
                }

                var jobs = new List<OngoingExternalJob>();
                if (data["workshopExternalJobs"] is JArray jobsArr)
                {
                    foreach (var item in jobsArr)
                    {
                        var j = item.ToObject<OngoingExternalJob>();
                        if (j != null) jobs.Add(j);
                    }
                }

                wm.RestoreFromSave(slots, jobs, data.Value<int?>("workshopNextSlotId") ?? 0);
            }

            // TD-037: misje rescue w toku — fallback `brak pola` → brak misji (stare save'y).
            // Pending static — RescueService wstaje po OnSimulatorBootstrapped, konsumpcja w jego Update.
            var rescues = new List<OngoingRescue>();
            if (data["rescueOngoing"] is JArray rescueArr)
            {
                foreach (var item in rescueArr)
                {
                    var r = item.ToObject<OngoingRescue>();
                    if (r.brokenTrainRunId > 0 || r.rescueLocoId > 0) rescues.Add(r);
                }
            }
            RescueService.SetPendingRestore(rescues);

            Log.Info($"[MaintenanceSavable] Restored: parts stock + pending + history + workshop slots/jobs"
                     + (rescues.Count > 0 ? $" + {rescues.Count} rescue ongoing" : ""));
        }

        public void InitializeDefault()
        {
            PartInventoryService.EnsureExists().ResetRuntime();
            WorkshopManager.Instance?.ResetRuntimeState();
            RescueService.SetPendingRestore(null); // TD-037
        }
    }

    public static class MaintenanceSavableBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SaveRegistry.Register(new MaintenanceSavable());
        }
    }

    // Migrator v1→v2 usunięty 2026-05-15 (identity, bez wartości — Deserialize
    // używa fallbacków). Zob. CLAUDE.md "Schema versioning".
}
