using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.SaveLoad.Modules
{
    /// <summary>
    /// M13-7: Persystencja ekonomii — EconomyManager (today + history + per-line)
    /// + ReputationManager (global + per-voivodeship + per-station).
    ///
    /// Module ID: "economy". Schema v1.
    ///
    /// Pola w bundle:
    /// - revenueTodayGroszy / costsTodayGroszy / subsidiesTodayGroszy (long) — running today
    /// - lineBalances (JArray LineBalance) — per-circulation breakdown dziś
    /// - history (JArray DailyBalance) — historia zamkniętych dni (max ~365)
    /// - reputationGlobal (int)
    /// - reputationByVoivodeship (JObject string→int)
    /// - reputationByStation (JObject string→int) — int key serialized as string w JSON
    ///
    /// 2026-05-15: refactor z reflection (NonPublic field access na _lineBalances/_history/
    /// _byVoivodeship/_byStation/<RevenueTodayGroszy>k__BackingField) na public
    /// `RestoreFromSave`/`ResetRuntime` API w EconomyManager + ReputationManager. Eliminuje
    /// silent corruption przy rename pola — teraz compile-time check.
    /// </summary>
    public class EconomySavable : ISavable
    {
        public string ModuleId => "economy";
        public int SchemaVersion => 1;

        public JObject Serialize()
        {
            var data = new JObject();

            var econ = EconomyManager.Instance;
            if (econ != null)
            {
                data["revenueTodayGroszy"]   = econ.RevenueTodayGroszy;
                data["costsTodayGroszy"]     = econ.CostsTodayGroszy;
                data["subsidiesTodayGroszy"] = econ.SubsidiesTodayGroszy;

                var lineBalancesArr = new JArray();
                foreach (var kv in econ.LineBalances)
                    lineBalancesArr.Add(JObject.FromObject(kv.Value));
                data["lineBalances"] = lineBalancesArr;

                var historyArr = new JArray();
                foreach (var db in econ.History)
                    historyArr.Add(JObject.FromObject(db));
                data["history"] = historyArr;
            }

            var rep = ReputationManager.Instance;
            if (rep != null)
            {
                data["reputationGlobal"] = rep.Global;

                var voiObj = new JObject();
                foreach (var kv in rep.ByVoivodeship)
                    voiObj[kv.Key] = kv.Value;
                data["reputationByVoivodeship"] = voiObj;

                var stationObj = new JObject();
                foreach (var kv in rep.ByStation)
                    stationObj[kv.Key.ToString()] = kv.Value;
                data["reputationByStation"] = stationObj;
            }

            return data;
        }

        public void Deserialize(JObject data, int sourceVersion)
        {
            // EconomyManager
            var econ = EconomyManager.EnsureExists();

            var lineBalances = new List<LineBalance>();
            if (data["lineBalances"] is JArray lbArr)
            {
                foreach (var item in lbArr)
                {
                    var lb = item.ToObject<LineBalance>();
                    if (lb != null) lineBalances.Add(lb);
                }
            }

            var history = new List<DailyBalance>();
            if (data["history"] is JArray histArr)
            {
                foreach (var item in histArr)
                {
                    var db = item.ToObject<DailyBalance>();
                    if (db != null) history.Add(db);
                }
            }

            econ.RestoreFromSave(
                data.Value<long?>("revenueTodayGroszy") ?? 0L,
                data.Value<long?>("costsTodayGroszy") ?? 0L,
                data.Value<long?>("subsidiesTodayGroszy") ?? 0L,
                lineBalances,
                history);

            // ReputationManager
            var rep = ReputationManager.EnsureExists();
            int globalRep = data.Value<int?>("reputationGlobal") ?? 50;

            var voiPairs = new List<KeyValuePair<string, int>>();
            if (data["reputationByVoivodeship"] is JObject voiJObj)
            {
                foreach (var kv in voiJObj)
                    voiPairs.Add(new KeyValuePair<string, int>(kv.Key, kv.Value.Value<int>()));
            }

            var stationPairs = new List<KeyValuePair<int, int>>();
            if (data["reputationByStation"] is JObject stationJObj)
            {
                foreach (var kv in stationJObj)
                {
                    if (int.TryParse(kv.Key, out int stationId))
                        stationPairs.Add(new KeyValuePair<int, int>(stationId, kv.Value.Value<int>()));
                }
            }

            rep.RestoreFromSave(globalRep, voiPairs, stationPairs);

            Log.Info($"[EconomySavable] Restored: rev/cost/sub today + " +
                     $"{lineBalances.Count} line balances + {history.Count} history days + " +
                     $"reputation {globalRep}/100");
        }

        public void InitializeDefault()
        {
            EconomyManager.EnsureExists().ResetRuntime();
            ReputationManager.EnsureExists().ResetRuntime();
        }
    }

    public static class EconomySavableBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SaveRegistry.Register(new EconomySavable());
        }
    }
}
