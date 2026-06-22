using System.Collections.Generic;
using UnityEngine;
using DepotSystem;

namespace DepotSystem.Furniture.Placement
{
    /// <summary>Poziom wyniku walidacji placement.</summary>
    public enum ValidationLevel
    {
        Ok,        // wszystko gra — placement dozwolony, funkcje obiektu aktywne
        Warning,   // soft issue (np. brak dojścia accessSide) — placement dozwolony, funkcja zablokowana
        Error      // hard issue (footprint poza pokojem, kolizja, niekompatybilny pokój) — placement zablokowany
    }

    /// <summary>Wynik walidacji placement.</summary>
    public struct ValidationResult
    {
        public ValidationLevel Level;
        public string Reason;

        public static ValidationResult Ok() => new ValidationResult { Level = ValidationLevel.Ok, Reason = "" };
        public static ValidationResult Warning(string reason) => new ValidationResult { Level = ValidationLevel.Warning, Reason = reason };
        public static ValidationResult Error(string reason) => new ValidationResult { Level = ValidationLevel.Error, Reason = reason };
    }

    /// <summary>
    /// MF-6 — walidacja placement furniture obiektu w danej pozycji + rotacji.
    ///
    /// Walidacje (kolejność = priorytet, pierwszy fail kończy):
    /// 1. <b>Special placement (drzwi)</b>: w MF-6 zwraca Warning "MF-11" (DoorPlacer w MF-11)
    /// 2. <b>Brak pokojów</b>: jeśli zajezdnia nie ma żadnych <see cref="DetectedRoom"/> → Error
    /// 3. <b>Footprint w pokoju</b>: wszystkie cells obiektu muszą być w bounds JEDNEGO pokoju → Error jeśli nie
    /// 4. <b>CompatibleRoomTypes</b>: pokój musi być w <see cref="FurnitureItem.compatibleRoomTypes"/> → Error jeśli nie
    /// 5. <b>Door cells</b>: footprint nie może blokować door cells (drzwi, MF-11) → Error
    /// 6. <b>Kolizje z innym furniture</b>: footprint cells nie mogą overlap'ować z istniejącymi instancjami w tym depocie → Error
    /// 7. <b>AccessSide free</b>: cells po stronie accessSide muszą być w pokoju + nie zajęte → Warning jeśli nie
    ///
    /// Decyzja B14 (2026-04-30): brak dojścia accessSide = Warning, nie Error. Gracz może
    /// świadomie postawić szafę plecami w róg (funkcja zablokowana, ale obiekt istnieje).
    /// </summary>
    public static class FurnitureValidator
    {
        /// <summary>
        /// Waliduje czy obiekt może być postawiony w danej pozycji + rotacji w danym depocie.
        /// </summary>
        public static ValidationResult Validate(
            FurnitureItem item,
            int depotId,
            Vector3 pivot,
            int rotationDeg,
            IReadOnlyList<DetectedRoom> rooms,
            IReadOnlyList<PlacedFurnitureItem> placedInstances)
        {
            if (item == null) return ValidationResult.Error("item == null");

            // 1. Drzwi (specialPlacement = WallCell) — DoorPlacer w MF-11
            if (item.ParseSpecialPlacement() == SpecialPlacement.WallCell)
            {
                return ValidationResult.Warning("Drzwi: pełna walidacja w MF-11 (DoorPlacer)");
            }

            // 2. Brak pokojów
            if (rooms == null || rooms.Count == 0)
            {
                return ValidationResult.Error("Brak pomieszczeń w zajezdni");
            }

            // Compute footprint cells
            var cells = FurnitureSnapDetector.GetFootprintCells(pivot, item.footprintCells.x, item.footprintCells.y, rotationDeg);
            if (cells.Count == 0) return ValidationResult.Error("Pusty footprint");

            // 3. Footprint w pokoju (wszystkie cells w bounds JEDNEGO room)
            DetectedRoom owningRoom = FindOwningRoom(cells, rooms);
            if (owningRoom == null)
            {
                return ValidationResult.Error("Footprint poza pokojem (lub w 2 pokojach na raz)");
            }

            // 4. CompatibleRoomTypes
            string roomTypeStr = owningRoom.roomType.ToString();
            if (owningRoom.roomType == RoomType.None)
            {
                return ValidationResult.Error("Pokój nie ma przypisanego typu (kliknij pokój → wybierz typ)");
            }
            if (!item.IsCompatibleWith(roomTypeStr))
            {
                return ValidationResult.Error($"Niekompatybilny pokój ({roomTypeStr})");
            }

            // 5. Door cells collision (MF-11 placeholder)
            if (owningRoom.doorCells != null && owningRoom.doorCells.Count > 0)
            {
                foreach (var cell in cells)
                {
                    if (owningRoom.doorCells.Contains(cell))
                    {
                        return ValidationResult.Error("Blokuje drzwi");
                    }
                }
            }

            // 6. Kolizje z innym furniture w tym samym depocie
            if (placedInstances != null && placedInstances.Count > 0)
            {
                var collisionResult = CheckCollisions(cells, depotId, placedInstances);
                if (collisionResult.Level == ValidationLevel.Error) return collisionResult;
            }

            // 7. AccessSide free (Warning, nie Error)
            AccessSide side = item.ParseAccessSide();
            if (side != AccessSide.All)
            {
                var accessResult = CheckAccessSide(item, pivot, rotationDeg, side, owningRoom, depotId, placedInstances);
                if (accessResult.Level == ValidationLevel.Warning) return accessResult;
            }

            return ValidationResult.Ok();
        }

        // ════════════════════════════════════════
        //  PRIVATE — walidacje skladowe
        // ════════════════════════════════════════

        private static DetectedRoom FindOwningRoom(List<Vector2Int> cells, IReadOnlyList<DetectedRoom> rooms)
        {
            // Wszystkie cells muszą być w bounds tego samego pokoju
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                if (room == null) continue;
                bool allInRoom = true;
                for (int c = 0; c < cells.Count; c++)
                {
                    if (!room.bounds.Contains(cells[c]))
                    {
                        allInRoom = false;
                        break;
                    }
                }
                if (allInRoom) return room;
            }
            return null;
        }

        private static ValidationResult CheckCollisions(
            List<Vector2Int> cells,
            int depotId,
            IReadOnlyList<PlacedFurnitureItem> placedInstances)
        {
            for (int i = 0; i < placedInstances.Count; i++)
            {
                var instance = placedInstances[i];
                if (instance == null) continue;
                if (instance.depotId != depotId) continue;

                var existingItem = FurnitureCatalog.FindById(instance.itemId);
                if (existingItem == null) continue;

                var existingCells = FurnitureSnapDetector.GetFootprintCells(
                    instance.position,
                    existingItem.footprintCells.x,
                    existingItem.footprintCells.y,
                    instance.rotation);

                for (int c = 0; c < cells.Count; c++)
                {
                    for (int e = 0; e < existingCells.Count; e++)
                    {
                        if (cells[c] == existingCells[e])
                        {
                            return ValidationResult.Error(
                                $"Kolizja z {existingItem.displayName} (#{instance.instanceId})");
                        }
                    }
                }
            }
            return ValidationResult.Ok();
        }

        private static ValidationResult CheckAccessSide(
            FurnitureItem item,
            Vector3 pivot,
            int rotationDeg,
            AccessSide side,
            DetectedRoom owningRoom,
            int depotId,
            IReadOnlyList<PlacedFurnitureItem> placedInstances)
        {
            var accessCells = FurnitureSnapDetector.GetAccessSideCells(
                pivot, item.footprintCells.x, item.footprintCells.y, rotationDeg, side);

            if (accessCells.Count == 0) return ValidationResult.Ok();  // AccessSide.All lub edge case

            // Każda access cell musi być w pokoju (tym samym, w którym jest obiekt)
            for (int i = 0; i < accessCells.Count; i++)
            {
                if (!owningRoom.bounds.Contains(accessCells[i]))
                {
                    return ValidationResult.Warning(
                        $"Brak dojścia ({side}) — cell poza pokojem, funkcja zablokowana");
                }
            }

            // Każda access cell musi być wolna (nie zajęta innym furniture w tym depocie)
            if (placedInstances != null)
            {
                for (int i = 0; i < placedInstances.Count; i++)
                {
                    var instance = placedInstances[i];
                    if (instance == null) continue;
                    if (instance.depotId != depotId) continue;

                    var existingItem = FurnitureCatalog.FindById(instance.itemId);
                    if (existingItem == null) continue;

                    var existingCells = FurnitureSnapDetector.GetFootprintCells(
                        instance.position,
                        existingItem.footprintCells.x,
                        existingItem.footprintCells.y,
                        instance.rotation);

                    for (int a = 0; a < accessCells.Count; a++)
                    {
                        for (int e = 0; e < existingCells.Count; e++)
                        {
                            if (accessCells[a] == existingCells[e])
                            {
                                return ValidationResult.Warning(
                                    $"Brak dojścia ({side}) — cell zajęta przez {existingItem.displayName}, funkcja zablokowana");
                            }
                        }
                    }
                }
            }

            return ValidationResult.Ok();
        }
    }
}
