using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.SaveLoad.Modules
{
    /// <summary>
    /// M13-7: Persystencja FleetService — posiadane pojazdy, składy, koszyk, rynek wtórny.
    ///
    /// Module ID: "fleet". Schema v3.
    ///
    /// Pola w bundle:
    /// - ownedVehicles (JArray) — pełna lista FleetVehicleData (z [Serializable] →
    ///   JsonConvert)
    /// - consists (JArray) — FleetConsistData lista
    /// - cart (JArray) — CartItem lista
    /// - marketVehicles (JArray) — aktualny rynek wtórny (gracz może wrócić do tego samego stanu)
    /// - nextCartId (int) — counter dla nowych CartItem'ów
    /// - marketRefreshDaysSinceLast (int) - countdown rynku wtórnego
    /// - vehicleLocations (JArray) — snapshot VehicleLocationService (M9c authority)
    ///
    /// Decyzja: NIE serializujemy katalogów (FleetCatalog/InspectionCatalog) — to są
    /// statyczne pliki JSON ładowane z Resources, niezależne od save'a.
    ///
    /// JsonConvert używa default settings — POCO z [Serializable] field-based.
    /// Edge case: Lists wewnątrz POCO (seatBreakdown, history, supportedTractions) też
    /// serializują się rekurencyjnie.
    /// </summary>
    public class FleetSavable : ISavable
    {
        public string ModuleId => "fleet";
        public int SchemaVersion => 1; // pre-EA reset 2026-05-15; bump po EA = real migrator

        public JObject Serialize()
        {
            var locSvc = VehicleLocationService.Instance ?? VehicleLocationService.EnsureExists();
            return new JObject
            {
                ["ownedVehicles"]  = JArray.FromObject(FleetService.OwnedVehicles),
                ["consists"]       = JArray.FromObject(FleetService.Consists),
                ["cart"]           = JArray.FromObject(FleetService.Cart),
                ["marketVehicles"] = JArray.FromObject(FleetService.MarketVehicles),
                ["nextCartId"]     = FleetService.NextCartId,
                ["vehicleLocations"] = JArray.FromObject(locSvc.GetSnapshot()),
                ["marketRefreshDaysSinceLast"] = FleetMarketRefreshService.GetDaysSinceLastRefresh()
            };
        }

        public void Deserialize(JObject data, int sourceVersion)
        {
            // BUG-035: bulk replace przez LoadXxxSnapshot API zamiast bezpośredniej mutacji.
            // Helper emituje notify automatycznie — usunięte explicit Notify*Changed na końcu.
            // C4: flaga blokuje konkurencyjne Initialize() (np. otwarcie FleetPanelUI mid-load).
            FleetService.BeginRestoreFromSave();

            var ownedList = new System.Collections.Generic.List<FleetVehicleData>();
            if (data["ownedVehicles"] is JArray owned)
            {
                foreach (var item in owned)
                {
                    var v = item.ToObject<FleetVehicleData>();
                    if (v != null) ownedList.Add(v);
                }
            }
            FleetService.LoadOwnedSnapshot(ownedList);

            var consistsList = new System.Collections.Generic.List<FleetConsistData>();
            if (data["consists"] is JArray consists)
            {
                foreach (var item in consists)
                {
                    var c = item.ToObject<FleetConsistData>();
                    if (c != null) consistsList.Add(c);
                }
            }
            FleetService.LoadConsistsSnapshot(consistsList);

            var cartList = new System.Collections.Generic.List<CartItem>();
            if (data["cart"] is JArray cart)
            {
                foreach (var item in cart)
                {
                    var ci = item.ToObject<CartItem>();
                    if (ci != null) cartList.Add(ci);
                }
            }
            FleetService.LoadCartSnapshot(cartList);

            var marketList = new System.Collections.Generic.List<FleetMarketVehicle>();
            if (data["marketVehicles"] is JArray market)
            {
                foreach (var item in market)
                {
                    var mv = item.ToObject<FleetMarketVehicle>();
                    if (mv != null) marketList.Add(mv);
                }
            }
            FleetService.LoadMarketSnapshot(marketList);

            FleetService.NextCartId = data.Value<int?>("nextCartId") ?? 1;

            var locSvc = VehicleLocationService.Instance ?? VehicleLocationService.EnsureExists();
            var locations = new List<VehicleLocationRecord>();
            if (data["vehicleLocations"] is JArray locArray)
            {
                foreach (var item in locArray)
                {
                    var loc = item.ToObject<VehicleLocationRecord>();
                    if (loc != null) locations.Add(loc);
                }
            }
            else
            {
                // Legacy v1 saves had no VehicleLocationService snapshot. Best fallback:
                // owned vehicles are known to exist, but exact in-world state is lost.
                foreach (var v in FleetService.OwnedVehicles)
                {
                    if (v == null || v.id <= 0) continue;
                    locations.Add(new VehicleLocationRecord
                    {
                        vehicleId = v.id,
                        type = VehicleLocationType.InDepot,
                        stationId = GameState.HomeDepotStationId,
                        depotTrackId = -1,
                        currentTrainRunId = -1,
                        currentConsistId = -1,
                        worldMapPosition = Vector2.zero
                    });
                }
            }
            locSvc.RestoreSnapshot(locations);
            FleetMarketRefreshService.RestoreDaysSinceLastRefresh(
                data.Value<int?>("marketRefreshDaysSinceLast") ?? 0);
            FleetMarketRefreshService.EnsureExists();
            FleetService.MarkInitializedFromSave();

            Log.Info($"[FleetSavable] Restored: {FleetService.OwnedVehicles.Count} vehicles, " +
                     $"{FleetService.Consists.Count} consists, {FleetService.Cart.Count} cart, " +
                     $"{FleetService.MarketVehicles.Count} market");
        }

        public void InitializeDefault()
        {
            // #1B: trzy NIEZALEŻNE resety singletonów — każdy izolowany, bo failure
            // jednego nie może pominąć pozostałych. Bez izolacji: gdy
            // FleetService.ResetForNewGame() rzuci (np. subskrybent OnOwnedChanged),
            // VehicleLocationService.ResetAll() i FleetMarketRefreshService.ResetAll()
            // nigdy się nie wykonają -> stare cross-session records przeciekają do
            // nowej gry ("pojazd-zombie", regresja BUG-039/BUG-046). InitializeDefault
            // to ścieżka graceful degradation — partial-reset jest gorszy niż izolowane
            // wykonanie wszystkich (taki sam kontrakt jak per-module isolation w
            // SaveOrchestrator/SaveLoadServiceBootstrap, tylko o poziom niżej).
            SafeReset("FleetService.ResetForNewGame", FleetService.ResetForNewGame);

            // BUG-039: clear VehicleLocationService cross-session state (singleton
            // DontDestroyOnLoad — bez tego stare records pokazują się w nowej grze).
            SafeReset("VehicleLocationService.ResetAll", VehicleLocationService.ResetAll);

            // BUG-046: reset market refresh countdown (singleton DontDestroyOnLoad)
            SafeReset("FleetMarketRefreshService.ResetAll", FleetMarketRefreshService.ResetAll);
        }

        /// <summary>
        /// #1B: wykonuje pojedynczy reset singletona, łapie i loguje wyjątek żeby
        /// nie przerwać resetu pozostałych singletonów w <see cref="InitializeDefault"/>.
        /// </summary>
        private static void SafeReset(string what, System.Action reset)
        {
            try { reset(); }
            catch (System.Exception e)
            {
                Log.Error($"[FleetSavable] Reset '{what}' threw: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    public static class FleetSavableBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SaveRegistry.Register(new FleetSavable());
        }
    }

    // Migrator'y v1→v2/v2→v3 usunięte 2026-05-15 (identity, bez wartości — Deserialize
    // używa fallbacków). Zob. CLAUDE.md "Schema versioning".
}
