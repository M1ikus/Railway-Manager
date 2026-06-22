using Newtonsoft.Json.Linq;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Timetable.Economy;

namespace RailwayManager.SaveLoad.Modules
{
    /// <summary>
    /// TD-037: Persystencja CAŁEJ puli pasażerów-agentów (kolumnowy <see cref="PassengerPoolSnapshot"/> —
    /// do 50k agentów; format kolumnowy = kompaktowy JSON + szybki parse; bundle i tak gzip).
    ///
    /// Module ID: "passengers". Schema v1.
    ///
    /// Restore deferred: payload → <see cref="PassengerManager.SetPendingRestore"/>, konsumpcja
    /// w FixedUpdate managera gdy OD matrix gotowa ORAZ symulator wznowił kursy (kolejność:
    /// runy przed pasażerami — inaczej guard ubiłby agentów OnTrain). Stare save'y bez sekcji →
    /// InitializeDefault = pusta pula (spawn z demand modelu jak dotąd).
    /// </summary>
    public class PassengersSavable : ISavable
    {
        public string ModuleId => "passengers";
        public int SchemaVersion => 1;

        public JObject Serialize()
        {
            var pm = PassengerManager.Instance;
            var snapshot = pm != null ? pm.BuildSaveSnapshot() : new PassengerPoolSnapshot();
            return new JObject
            {
                ["pool"] = JObject.FromObject(snapshot),
            };
        }

        public void Deserialize(JObject data, int sourceVersion)
        {
            PassengerPoolSnapshot snapshot = null;
            if (data["pool"] is JObject poolObj)
                snapshot = poolObj.ToObject<PassengerPoolSnapshot>();

            PassengerManager.SetPendingRestore(snapshot);
            Log.Info($"[PassengersSavable] Restored: {snapshot?.Count ?? 0} agentów pending " +
                     $"(nextAgentId={snapshot?.nextAgentId ?? 1})");
        }

        public void InitializeDefault()
        {
            PassengerManager.SetPendingRestore(null);
        }
    }

    public static class PassengersSavableBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SaveRegistry.Register(new PassengersSavable());
        }
    }
}
