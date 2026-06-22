using Newtonsoft.Json.Linq;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Timetable;

namespace RailwayManager.SaveLoad.Modules
{
    /// <summary>
    /// M13-7: Persystencja CirculationService — obiegi taboru z per-day vehicle
    /// assignments + status + names.
    ///
    /// Module ID: "circulations". Schema v1 (pre-EA reset 2026-05-15 — wcześniej v2,
    /// patrz CLAUDE.md "Schema versioning").
    ///
    /// Pola w bundle:
    /// - circulations (JArray Circulation) — pełna lista obiegów ze stepami,
    ///   per-day assignments, status, name, calendar
    ///
    /// Decyzje:
    /// - NIE serializujemy nextCirculationId — Circulation type generuje ID
    ///   w AddCirculation (sprawdzimy max+1 po deserializacji)
    /// - Vehicles assignments (FleetVehicleData.assignedCirculationId) pochodzą
    ///   z FleetSavable — load order matters (Fleet przed Circulations? lub
    ///   Circulations po Fleet — kolejność z SaveRegistry.All odzwierciedla
    ///   kolejność rejestracji w bootstrapach)
    /// - Osobny moduł od TimetableSavable bo CirculationService to osobny serwis
    /// </summary>
    public class CirculationsSavable : ISavable
    {
        public string ModuleId => "circulations";
        public int SchemaVersion => 1; // pre-EA reset 2026-05-15; bump po EA = real migrator

        public JObject Serialize()
        {
            return new JObject
            {
                ["circulations"] = JArray.FromObject(CirculationService.Circulations),
                ["nextId"]       = CirculationService.GetNextId()  // BUG-078
            };
        }

        public void Deserialize(JObject data, int sourceVersion)
        {
            // Pre-EA: brak migrator chain (reset SchemaVersion → 1 dnia 2026-05-15).
            // Circulation referuje timetableId — jeśli stary save miał enum reorder w
            // TimetableSavable, references mogą być stale; gracz może zacząć od nowa.

            // BUG-078: restore _nextId via RestoreFromSave (poprzednio _nextId zostawał 1
            // i nowo dodany obieg dostawał id=1 — kolizja z istniejącym).
            var circList = new System.Collections.Generic.List<Circulation>();
            var circs = data["circulations"] as JArray;
            if (circs != null)
            {
                foreach (var item in circs)
                {
                    var c = item.ToObject<Circulation>();
                    if (c != null) circList.Add(c);
                }
            }

            // Backward compat: stare savy bez "nextId" → fallback na max+1 z deserialized list.
            int nextId = data.Value<int?>("nextId") ?? ComputeMaxIdPlusOne(circList);
            CirculationService.RestoreFromSave(circList, nextId);

            Log.Info($"[CirculationsSavable] Restored: {CirculationService.Circulations.Count} circulations, nextId={nextId}");
        }

        /// <summary>BUG-078: fallback dla starych saveów bez "nextId" — szukamy max(id)+1.</summary>
        private static int ComputeMaxIdPlusOne(System.Collections.Generic.IList<Circulation> circs)
        {
            int maxId = 0;
            if (circs != null)
            {
                foreach (var c in circs)
                    if (c != null && c.id > maxId) maxId = c.id;
            }
            return maxId + 1;
        }

        public void InitializeDefault()
        {
            CirculationService.Reset();
        }
    }

    public static class CirculationsSavableBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SaveRegistry.Register(new CirculationsSavable());
        }
    }
}
