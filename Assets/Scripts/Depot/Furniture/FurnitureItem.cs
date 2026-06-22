using System;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.Furniture
{
    /// <summary>
    /// Definicja typu obiektu furniture (deserializowalna z JSON).
    ///
    /// Plik źródłowy: <c>Resources/Furniture/builtin_catalog.json</c> (built-in)
    /// lub <c>%AppData%/RailwayManager/CustomFurniture/*.json</c> (user mods, post-EA)
    /// lub Steam Workshop folder (M14).
    ///
    /// Multi-source loader: <c>FurnitureCatalog.LoadAll()</c> (MF-2).
    /// Instance state per depot: <see cref="PlacedFurnitureItem"/>.
    /// </summary>
    [Serializable]
    public class FurnitureItem
    {
        // ── Identyfikacja ─────────────────────────────────
        public string id = "";
        public string displayName = "";
        public string description = "";

        // ── Klasyfikacja ──────────────────────────────────
        public string category = "Furniture";       // mapuje na FurnitureCategory enum
        public string[] compatibleRoomTypes;        // mapuje na RoomType enum (RoomDetectionSystem)

        // ── Geometria ─────────────────────────────────────
        public FootprintMeters footprint = new FootprintMeters { length = 1f, width = 1f };
        public FootprintCells footprintCells = new FootprintCells { x = 1, y = 1 };

        // ── Funkcje + accessSide ──────────────────────────
        public string[] functions;                  // mapuje na ObjectFunction enum (może być wiele)
        public string accessSide = "Front";         // mapuje na AccessSide enum
        public int defaultRotation = 0;             // 0/90/180/270

        // ── Specjalne reguły placement ────────────────────
        public string specialPlacement = "None";    // mapuje na SpecialPlacement enum

        // ── Asset + visual ────────────────────────────────
        public string assetTag = "";                // klucz dla M-Models swap
        public string iconResource = "";            // ścieżka do ikony (post-EA, w EA proceduralna z color)
        public string color = "#808080";            // hex, placeholder background do ikony i preview cuboid

        // ── Ekonomia ──────────────────────────────────────
        public int priceGroszy = 0;                 // cena zakupu w groszach (0 = darmowe placeholder w EA)

        // ── M-Modernization MM-D16 ─────────────────────────
        /// <summary>
        /// MM-D16 — maksymalna długość pojazdu który slot/equipment przyjmie [m].
        /// 0 = brak constraints (domyślne dla mebli niefizycznie blokujących pojazdy
        /// — biurka, krzesła, sofy, kuchnie, drzwi, etc.).
        ///
        /// Wartości dla obiektów akceptujących pojazdy:
        /// - pit_small = 18m, pit_medium = 25m, pit_large = 35m
        /// - lift_aux = 15m (mniejszy podnośnik pomocniczy)
        /// - wash_gate = 64m (przejazd całego składu)
        /// - fuel_pump = 50m (długie pojazdy też tankuje, MM-D14)
        ///
        /// Walidacja: <c>WorkshopManager.AssignVehicle</c> i <c>DispatchActionService</c>
        /// odrzucają assignment gdy <c>vehicle.lengthM &gt; maxVehicleLength</c>.
        /// </summary>
        public float maxVehicleLength = 0f;

        /// <summary>Parsuje category string na enum. Fallback Furniture jeśli nieznana.</summary>
        public FurnitureCategory ParseCategory()
        {
            if (Enum.TryParse<FurnitureCategory>(category, ignoreCase: true, out var result))
                return result;
            Log.Warn($"[FurnitureItem] Unknown category '{category}' for id='{id}', fallback to Furniture");
            return FurnitureCategory.Furniture;
        }

        /// <summary>Parsuje accessSide string na enum. Fallback Front jeśli nieznana.</summary>
        public AccessSide ParseAccessSide()
        {
            if (Enum.TryParse<AccessSide>(accessSide, ignoreCase: true, out var result))
                return result;
            Log.Warn($"[FurnitureItem] Unknown accessSide '{accessSide}' for id='{id}', fallback to Front");
            return AccessSide.Front;
        }

        /// <summary>Parsuje specialPlacement string na enum. Fallback None.</summary>
        public SpecialPlacement ParseSpecialPlacement()
        {
            if (Enum.TryParse<SpecialPlacement>(specialPlacement, ignoreCase: true, out var result))
                return result;
            return SpecialPlacement.None;
        }

        /// <summary>Czy obiekt ma daną funkcję (po porównaniu functions string array z enum name).</summary>
        public bool HasFunction(ObjectFunction fn)
        {
            if (functions == null) return false;
            string target = fn.ToString();
            for (int i = 0; i < functions.Length; i++)
            {
                if (string.Equals(functions[i], target, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Czy obiekt jest kompatybilny z danym typem pomieszczenia.
        /// "All" w compatibleRoomTypes oznacza wszystkie typy poza Corridor (decyzja 2026-05-03).
        /// </summary>
        public bool IsCompatibleWith(string roomType)
        {
            if (compatibleRoomTypes == null) return false;
            for (int i = 0; i < compatibleRoomTypes.Length; i++)
            {
                if (string.Equals(compatibleRoomTypes[i], "All", StringComparison.OrdinalIgnoreCase))
                {
                    return !string.Equals(roomType, "Corridor", StringComparison.OrdinalIgnoreCase);
                }
                if (string.Equals(compatibleRoomTypes[i], roomType, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Deserializuje z JSON string'a. Używa Unity JsonUtility
        /// (spójność z TurnoutSchemaDefinition i FleetCatalog).
        /// </summary>
        public static FurnitureItem FromJson(string json)
        {
            try
            {
                var item = JsonUtility.FromJson<FurnitureItem>(json);
                if (item == null)
                {
                    Log.Error("[FurnitureItem] FromJson returned null");
                    return null;
                }
                return item;
            }
            catch (Exception e)
            {
                Log.Error($"[FurnitureItem] FromJson failed: {e.Message}");
                return null;
            }
        }

        /// <summary>Serializuje do JSON string'a (pretty print).</summary>
        public string ToJson() => JsonUtility.ToJson(this, prettyPrint: true);

        // ── Helper structs ─────────────────────────────────

        [Serializable]
        public struct FootprintMeters
        {
            public float length;   // wymiar wzdłuż X (po rotacji 0°)
            public float width;    // wymiar wzdłuż Z (po rotacji 0°)
        }

        [Serializable]
        public struct FootprintCells
        {
            public int x;
            public int y;
        }
    }
}
