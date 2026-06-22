using Newtonsoft.Json.Linq;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Timetable;
using RailwayManager.Timetable.Simulation;

namespace RailwayManager.SaveLoad.Modules
{
    /// <summary>
    /// TD-037: Persystencja runtime świata kursów — pełna lista TrainRuns (statyczne + runtime pola)
    /// + snapshoty AKTYWNYCH pociągów (SimulatedTrain stan nie-derived).
    ///
    /// Module ID: "trainruns". Schema v1.
    ///
    /// Po co osobny moduł (nie TimetableSavable): runy to runtime świata, granularny fallback
    /// (fail tego modułu nie psuje planów rozkładów), a stare save'y bez sekcji → InitializeDefault
    /// = dotychczasowe zachowanie (świat odżywa przez rolling top-up okna, etap F).
    ///
    /// KLUCZOWE: runy są serializowane Z ICH ID, nie regenerowane po load —
    /// `CrewDuty.referencedTrainRunId` (PersonnelSavable) linkuje załogi po id runa;
    /// regeneracja (`AllocateTrainRunId`) dałaby nowe id i zerwała obsady (CrewCheckHook fail).
    ///
    /// Snapshoty aktywnych NIE są wznawiane tutaj — graf mapy buduje się async po load.
    /// Trafiają do <see cref="TrainRunSimulator.SetPendingRestore"/>, konsumpcja w FixedUpdate
    /// pod gate'em gotowości grafu (TD-037 etap B; wzorzec TD-031 RestoreActiveMove).
    /// </summary>
    public class TrainRunsSavable : ISavable
    {
        public string ModuleId => "trainruns";
        public int SchemaVersion => 1;

        public JObject Serialize()
        {
            var sim = TrainRunSimulator.Instance;
            var snapshots = sim != null
                ? sim.BuildActiveSnapshots()
                : new System.Collections.Generic.List<ActiveRunSnapshot>();

            return new JObject
            {
                ["trainRuns"] = JArray.FromObject(TimetableService.TrainRuns),
                ["activeSnapshots"] = JArray.FromObject(snapshots),
            };
        }

        public void Deserialize(JObject data, int sourceVersion)
        {
            var runs = new System.Collections.Generic.List<TrainRun>();
            if (data["trainRuns"] is JArray runsArr)
            {
                foreach (var item in runsArr)
                {
                    var r = item.ToObject<TrainRun>();
                    if (r != null) runs.Add(r);
                }
            }
            TimetableService.RestoreTrainRunsFromSave(runs);

            var snapshots = new System.Collections.Generic.List<ActiveRunSnapshot>();
            if (data["activeSnapshots"] is JArray snapArr)
            {
                foreach (var item in snapArr)
                {
                    var s = item.ToObject<ActiveRunSnapshot>();
                    if (s != null) snapshots.Add(s);
                }
            }
            TrainRunSimulator.SetPendingRestore(snapshots);

            Log.Info($"[TrainRunsSavable] Restored: {runs.Count} TrainRuns, {snapshots.Count} active snapshot(s) pending");
        }

        public void InitializeDefault()
        {
            // Stary save bez sekcji / failed load: brak runów (jak dotąd) — rolling top-up
            // okna (TD-037 F) dogeneruje od dziś dla Active obiegów. Pending wyczyszczony.
            TimetableService.RestoreTrainRunsFromSave(null);
            TrainRunSimulator.SetPendingRestore(null);
        }
    }

    public static class TrainRunsSavableBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SaveRegistry.Register(new TrainRunsSavable());
        }
    }
}
