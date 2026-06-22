using Newtonsoft.Json.Linq;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Timetable;
// Alias: 'Timetable' jest tez nazwa namespace'u (RailwayManager.Timetable), wiec nieujednoznaczone
// uzycie typu RailwayManager.Timetable.Timetable rzuca CS0118. Alias rozstrzyga ambiguity.
using TimetableObj = RailwayManager.Timetable.Timetable;

namespace RailwayManager.SaveLoad.Modules
{
    /// <summary>
    /// M13-7: Persystencja TimetableService — Routes, Timetables, CommercialCategories,
    /// counter ID'ów. NIE zawiera TrainRuns (runtime state) ani reservations
    /// (rebuilt z load Timetables w M13-8).
    ///
    /// Module ID: "timetable". Schema v1 (pre-EA reset 2026-05-15 — wcześniej v2,
    /// patrz CLAUDE.md "Schema versioning").
    ///
    /// **Schema history (zachowane dla kontekstu):**
    /// - v1: StopType enum {Passenger=0, Technical=1, PassThrough=2}
    /// - v2: StopType enum renamed {Transit=0, PH=1, PT=2, ZD=3} (M-TimetableUX F1.1)
    ///   — int values changed bo enum reorder + new ZD value. Pre-EA: stare save'y
    ///   nieistotne (decyzja 2026-05-08). Po EA: real enum reorder = real migrator.
    ///
    /// Pola w bundle:
    /// - routes (JArray Route) — wszystkie zdefiniowane trasy A→B
    /// - timetables (JArray Timetable) — pełne rozkłady z calendar/freq/stops
    /// - categories (JArray CommercialCategory) — kategorie handlowe gracza
    ///   (custom + default)
    /// - nextRouteId (int), nextTimetableId (int), nextTrainRunId (int) — counter'y
    ///
    /// Decyzje:
    /// - TrainRuns w osobnym module "runtime" (M13-8) — to żywy stan symulacji
    /// - Reservations rebuildowane z timetables w post-load hook (M13-8 też)
    /// - StopMemory (per-route×per-category) zachowujemy implicit przez Routes
    ///   (route.RouteHash używany jako klucz)
    /// </summary>
    public class TimetableSavable : ISavable
    {
        public string ModuleId => "timetable";
        public int SchemaVersion => 1; // pre-EA reset 2026-05-15; bump po EA = real migrator

        public JObject Serialize()
        {
            // M9c-D F7: pomijamy efemeryczne artefakty dostawcze (delivery run/route/timetable).
            // Live delivery run i tak nie jest persystowany (TrainRuns out of scope), a pojazd-w-dostawie
            // ma recovery przez FleetVehicleData.deliveryInProgress → bez zaśmiecania list gracza.
            var routes = new JArray();
            foreach (var r in TimetableService.Routes)
                if (r != null && !r.isDeliveryRoute) routes.Add(JObject.FromObject(r));
            var timetables = new JArray();
            foreach (var tt in TimetableService.Timetables)
                if (tt != null && !tt.isDeliveryTimetable) timetables.Add(JObject.FromObject(tt));

            return new JObject
            {
                ["routes"]           = routes,
                ["timetables"]       = timetables,
                ["categories"]       = JArray.FromObject(TimetableService.CommercialCategories),
                ["nextRouteId"]      = TimetableService.NextRouteId,
                ["nextTimetableId"]  = TimetableService.NextTimetableId,
                ["nextTrainRunId"]   = TimetableService.NextTrainRunId
            };
        }

        public void Deserialize(JObject data, int sourceVersion)
        {
            TimetableService.ResetForSaveLoad();

            // Pre-EA: brak migrator chain (reset SchemaVersion → 1 dnia 2026-05-15).
            // Stare save'y z v1 (Passenger/Technical/PassThrough enum) i v2 (Transit/PH/PT/ZD)
            // mogą się załadować z artefaktami w int values StopType — gracz może po prostu
            // zacząć od nowa. Po EA: real enum reorder = real migrator.

            // Routes
            var routes = data["routes"] as JArray;
            if (routes != null)
            {
                foreach (var item in routes)
                {
                    var r = item.ToObject<Route>();
                    if (r != null) TimetableService.Routes.Add(r);
                }
            }

            // Timetables
            var tts = data["timetables"] as JArray;
            if (tts != null)
            {
                foreach (var item in tts)
                {
                    var tt = item.ToObject<TimetableObj>();
                    if (tt != null) TimetableService.Timetables.Add(tt);
                }
            }

            // Commercial categories — może być pre-loadowane przez Initialize()
            // (jeśli TimetableInitializer wczytał defaults). Czyścimy + restore.
            var cats = data["categories"] as JArray;
            if (cats != null)
            {
                foreach (var item in cats)
                {
                    var c = item.ToObject<CommercialCategory>();
                    if (c != null) TimetableService.CommercialCategories.Add(c);
                }
            }

            // 2026-05-15: public API zamiast reflection na backing fields auto-properties.
            TimetableService.RestoreCountersFromSave(
                data.Value<int?>("nextRouteId") ?? 1,
                data.Value<int?>("nextTimetableId") ?? 1,
                data.Value<int?>("nextTrainRunId") ?? 1);
            TimetableService.MarkInitializedFromSave();

            Log.Info($"[TimetableSavable] Restored: {TimetableService.Routes.Count} routes, " +
                     $"{TimetableService.Timetables.Count} timetables, " +
                     $"{TimetableService.CommercialCategories.Count} categories");
        }

        public void InitializeDefault()
        {
            TimetableService.ResetForNewGame();
        }
    }

    public static class TimetableSavableBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SaveRegistry.Register(new TimetableSavable());
        }
    }
}
