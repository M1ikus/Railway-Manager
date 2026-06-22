using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.Furniture
{
    /// <summary>
    /// MF-1 smoke test — sprawdza:
    /// - parsowanie wszystkich enum'ów ze string'a (Category / AccessSide / SpecialPlacement)
    /// - serializację/deserializację <see cref="FurnitureItem"/> przez JsonUtility (roundtrip)
    /// - HasFunction / IsCompatibleWith / Parse* helper'y
    ///
    /// Użycie:
    /// 1. Wrzuć ten komponent jako GameObject na scenie Depot (Inspector "Add Component")
    /// 2. Right-click w Inspectorze → wybierz jedną z opcji ContextMenu
    /// 3. Output w Console
    ///
    /// MF-1 nie integruje się z FurnitureCatalog ani placerem — to przyjdzie w MF-2..3.
    /// </summary>
    public class FurnitureSchemaSmokeTest : MonoBehaviour
    {
        [ContextMenu("Print FurnitureItem schema")]
        public void PrintSchema()
        {
            var sample = new FurnitureItem
            {
                id = "desk_office",
                displayName = "Biurko biurowe",
                description = "Standardowe biurko z szufladami",
                category = "Furniture",
                compatibleRoomTypes = new[] { "Office", "Supervisor", "Dispatcher", "TrafficController" },
                footprint = new FurnitureItem.FootprintMeters { length = 1.6f, width = 0.8f },
                footprintCells = new FurnitureItem.FootprintCells { x = 2, y = 1 },
                functions = new[] { "WorkstationOffice" },
                accessSide = "Front",
                defaultRotation = 0,
                specialPlacement = "None",
                assetTag = "office_desk",
                color = "#8B4513",
                priceGroszy = 50000
            };

            string json = sample.ToJson();
            Log.Info($"[FurnitureSchemaSmokeTest] Sample FurnitureItem JSON:\n{json}");

            var roundtrip = FurnitureItem.FromJson(json);
            if (roundtrip == null)
            {
                Log.Error("[FurnitureSchemaSmokeTest] Roundtrip FAIL — FromJson zwrócił null");
                return;
            }

            Log.Info($"[FurnitureSchemaSmokeTest] Roundtrip OK: id={roundtrip.id}, " +
                     $"category={roundtrip.ParseCategory()}, accessSide={roundtrip.ParseAccessSide()}, " +
                     $"specialPlacement={roundtrip.ParseSpecialPlacement()}");

            Log.Info($"[FurnitureSchemaSmokeTest] HasFunction(WorkstationOffice)={roundtrip.HasFunction(ObjectFunction.WorkstationOffice)}, " +
                     $"HasFunction(ServicePit)={roundtrip.HasFunction(ObjectFunction.ServicePit)}");

            Log.Info($"[FurnitureSchemaSmokeTest] IsCompatibleWith(Office)={roundtrip.IsCompatibleWith("Office")}, " +
                     $"IsCompatibleWith(Hall)={roundtrip.IsCompatibleWith("Hall")}, " +
                     $"IsCompatibleWith(Bathroom)={roundtrip.IsCompatibleWith("Bathroom")}");
        }

        [ContextMenu("Print door schema (specialPlacement=WallCell)")]
        public void PrintDoorSchema()
        {
            var door = new FurnitureItem
            {
                id = "door_basic",
                displayName = "Drzwi",
                description = "Drzwi standardowe (placement na wall cell)",
                category = "Industrial",
                compatibleRoomTypes = new[] { "All" },
                footprint = new FurnitureItem.FootprintMeters { length = 1.0f, width = 0.2f },
                footprintCells = new FurnitureItem.FootprintCells { x = 1, y = 1 },
                functions = new[] { "Passage" },
                accessSide = "All",
                defaultRotation = 0,
                specialPlacement = "WallCell",
                assetTag = "door_basic",
                color = "#A0522D"
            };

            string json = door.ToJson();
            Log.Info($"[FurnitureSchemaSmokeTest] door_basic JSON (specialPlacement=WallCell):\n{json}");

            var roundtrip = FurnitureItem.FromJson(json);
            if (roundtrip == null) { Log.Error("[FurnitureSchemaSmokeTest] Door roundtrip FAIL"); return; }

            Log.Info($"[FurnitureSchemaSmokeTest] Door roundtrip OK: " +
                     $"specialPlacement={roundtrip.ParseSpecialPlacement()}, " +
                     $"HasFunction(Passage)={roundtrip.HasFunction(ObjectFunction.Passage)}, " +
                     $"IsCompatibleWith(Bathroom)={roundtrip.IsCompatibleWith("Bathroom")}, " +
                     $"IsCompatibleWith(Corridor)={roundtrip.IsCompatibleWith("Corridor")}");
        }

        [ContextMenu("Print PlacedFurnitureItem instance")]
        public void PrintInstance()
        {
            var instance = new PlacedFurnitureItem
            {
                instanceId = 1,
                itemId = "desk_office",
                depotId = 1,
                position = new Vector3(5f, 0f, 7f),
                rotation = 90,
                assignedEmployeeId = 42
            };
            Log.Info($"[FurnitureSchemaSmokeTest] {instance}");
        }

        [ContextMenu("Test enum parsing — wszystkie warianty")]
        public void TestEnumParsing()
        {
            // Test Category
            var bad = new FurnitureItem { id = "test", category = "BogusCategory" };
            Log.Info($"[FurnitureSchemaSmokeTest] Bad category fallback: {bad.ParseCategory()} (oczekiwane: Furniture)");

            // Test AccessSide
            var bad2 = new FurnitureItem { id = "test", accessSide = "BogusSide" };
            Log.Info($"[FurnitureSchemaSmokeTest] Bad accessSide fallback: {bad2.ParseAccessSide()} (oczekiwane: Front)");

            // Test SpecialPlacement
            var bad3 = new FurnitureItem { id = "test", specialPlacement = "BogusPlacement" };
            Log.Info($"[FurnitureSchemaSmokeTest] Bad specialPlacement fallback: {bad3.ParseSpecialPlacement()} (oczekiwane: None)");

            // Test correct case-insensitive
            var ok = new FurnitureItem { id = "test", category = "industrial", accessSide = "all", specialPlacement = "wallcell" };
            Log.Info($"[FurnitureSchemaSmokeTest] Case-insensitive: " +
                     $"category={ok.ParseCategory()}, accessSide={ok.ParseAccessSide()}, " +
                     $"specialPlacement={ok.ParseSpecialPlacement()}");
        }

        // ════════════════════════════════════════
        //  MF-2 — FurnitureCatalog smoke tests
        // ════════════════════════════════════════

        [ContextMenu("MF-2: Load + list all furniture")]
        public void ListAllFurniture()
        {
            FurnitureCatalog.Reload();  // reload żeby smoke test był deterministyczny

            Log.Info($"[FurnitureSchemaSmokeTest] Built-in path: {FurnitureCatalog.GetBuiltInPath()}");
            Log.Info($"[FurnitureSchemaSmokeTest] User folder: {FurnitureCatalog.GetUserFolderPath()}");
            Log.Info($"[FurnitureSchemaSmokeTest] Loaded: built-in={FurnitureCatalog.BuiltIn.Count}, " +
                     $"user={FurnitureCatalog.UserCustom.Count}, workshop={FurnitureCatalog.Workshop.Count}, " +
                     $"all={FurnitureCatalog.AllItems.Count}");

            int idx = 0;
            foreach (var item in FurnitureCatalog.AllItems)
            {
                Log.Info($"  [{idx++,2}] {item.id,-22} | {item.displayName,-32} | {item.category,-11} | " +
                         $"{item.footprintCells.x}x{item.footprintCells.y} | accessSide={item.accessSide} | " +
                         $"specialPlacement={item.specialPlacement} | rooms=[{string.Join(",", item.compatibleRoomTypes ?? new string[0])}]");
            }
        }

        [ContextMenu("MF-2: Catalog stats per category")]
        public void CatalogStats()
        {
            if (!FurnitureCatalog.IsLoaded) FurnitureCatalog.LoadAll();

            foreach (FurnitureCategory cat in System.Enum.GetValues(typeof(FurnitureCategory)))
            {
                var filtered = FurnitureCatalog.FilterByCategory(cat);
                Log.Info($"[FurnitureSchemaSmokeTest] Category {cat}: {filtered.Count} items " +
                         $"({string.Join(", ", filtered.ConvertAll(i => i.id))})");
            }
        }

        [ContextMenu("MF-2: Filter compatibility — Hall vs Office vs Bathroom")]
        public void FilterByCompatibility()
        {
            if (!FurnitureCatalog.IsLoaded) FurnitureCatalog.LoadAll();

            string[] testRooms = { "Hall", "Office", "Bathroom", "Corridor", "Storage" };
            foreach (var room in testRooms)
            {
                var compatible = FurnitureCatalog.FilterByCompatibility(room);
                Log.Info($"[FurnitureSchemaSmokeTest] {room}: {compatible.Count} compatible " +
                         $"({string.Join(", ", compatible.ConvertAll(i => i.id))})");
            }
        }

        [ContextMenu("MF-2: FindById — desk_office, pit_large, door_basic, bogus")]
        public void FindByIdSamples()
        {
            if (!FurnitureCatalog.IsLoaded) FurnitureCatalog.LoadAll();

            string[] testIds = { "desk_office", "pit_large", "door_basic", "bogus_id_does_not_exist" };
            foreach (var id in testIds)
            {
                var item = FurnitureCatalog.FindById(id);
                if (item != null)
                {
                    Log.Info($"[FurnitureSchemaSmokeTest] FindById('{id}') → {item.displayName} " +
                             $"(category={item.ParseCategory()}, functions=[{string.Join(",", item.functions ?? new string[0])}], " +
                             $"specialPlacement={item.ParseSpecialPlacement()})");
                }
                else
                {
                    Log.Info($"[FurnitureSchemaSmokeTest] FindById('{id}') → null (oczekiwane dla 'bogus_id_does_not_exist')");
                }
            }
        }
    }
}
