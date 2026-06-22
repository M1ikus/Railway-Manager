using UnityEngine;
using DepotSystem;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;
using RailwayManager.Core;

namespace RailwayManager.Personnel.Furniture
{
    /// <summary>
    /// MF-10 — service przypisywania pracowników do instancji furniture (1:1 mapping
    /// + FIFO reassign przy OnFire/OnRetired).
    ///
    /// Mapping rola → wymagana <see cref="ObjectFunction"/>:
    /// - Office, Research → WorkstationOffice (desk_office w Office room)
    /// - Dispatcher → WorkstationOffice (desk_office w Dispatcher room — różnicuje pokój, nie funkcja)
    /// - TrafficController → WorkstationTraffic (traffic_console w TrafficController room)
    /// - Driver, Conductor, Mechanic, Cleaner, WashBay, TicketClerk → null (brak biurka)
    ///
    /// Decyzja 2026-05-03: brak manual override w EA. FIFO auto-assign przy hire,
    /// release przy fire. Manual override = post-EA QoL.
    ///
    /// Wywoływane z PersonnelDispatcher3D.HandleOnShift (assign jeśli brak biurka)
    /// i OnEmployeeLost (release + FIFO reassign).
    /// </summary>
    public static class FurnitureAssignmentService
    {
        /// <summary>
        /// Mapping rola → wymagana funkcja furniture. Null = rola nie wymaga biurka
        /// (np. Driver używa pojazdu, Mechanic używa ServicePit gdy M-Modernization wejdzie).
        /// </summary>
        public static ObjectFunction? GetRequiredFunction(EmployeeRole role) => role switch
        {
            EmployeeRole.Office           => ObjectFunction.WorkstationOffice,
            EmployeeRole.Research         => ObjectFunction.WorkstationOffice,
            EmployeeRole.Dispatcher       => ObjectFunction.WorkstationOffice,
            EmployeeRole.TrafficController => ObjectFunction.WorkstationTraffic,
            // Driver, Conductor, Mechanic, Cleaner, WashBay, TicketClerk → no furniture
            _ => null
        };

        /// <summary>
        /// Preferowany typ pokoju dla roli — filter przy AssignBestFurniture.
        /// Dispatcher musi siedzieć w Dispatcher room (nie Office), TrafficController w TrafficController itd.
        /// Null = brak preferencji (każdy compatibleRoomTypes OK).
        /// </summary>
        public static RoomType? GetPreferredRoomType(EmployeeRole role) => role switch
        {
            EmployeeRole.Office           => RoomType.Office,
            EmployeeRole.Research         => RoomType.Office,
            EmployeeRole.Dispatcher       => RoomType.Dispatcher,
            EmployeeRole.TrafficController => RoomType.TrafficController,
            _ => null
        };

        /// <summary>
        /// Auto-assign przy OnHire — znajdź pierwsze wolne furniture matching role
        /// (HasFunction + IsActive + preferred room type) w lokalnym depot.
        /// Zwraca true gdy assigned (e.assignedFurnitureId zaktualizowane).
        /// </summary>
        public static bool AssignBestFurniture(Employee e)
        {
            if (e == null || e.assignedFurnitureId >= 0) return false;
            var fn = GetRequiredFunction(e.role);
            if (!fn.HasValue) return false;  // role bez biurka — no-op

            var placer = FurniturePlacer.Instance;
            if (placer == null) return false;

            int depotId = OwnershipService.LocalDepotId;
            var preferredRoom = GetPreferredRoomType(e.role);

            foreach (var instance in placer.PlacedInstances)
            {
                if (instance == null) continue;
                if (instance.depotId != depotId) continue;
                if (instance.assignedEmployeeId >= 0) continue;  // already assigned

                var item = FurnitureCatalog.FindById(instance.itemId);
                if (item == null) continue;
                if (!item.HasFunction(fn.Value)) continue;

                // Functional state must be active (dojście wolne)
                var state = placer.GetFunctionalState(instance.instanceId);
                if (!state.IsActive) continue;

                // Preferred room filter (jeśli dotyczy)
                if (preferredRoom.HasValue)
                {
                    var room = FindOwningRoom(instance.position);
                    if (room == null || room.roomType != preferredRoom.Value) continue;
                }

                // Match!
                instance.assignedEmployeeId = e.employeeId;
                e.assignedFurnitureId = instance.instanceId;
                Log.Info($"[FurnitureAssignmentService] Assigned employee #{e.employeeId} ({e.role}) → " +
                         $"furniture #{instance.instanceId} ({item.id}) in depot {depotId}");
                return true;
            }

            Log.Info($"[FurnitureAssignmentService] No free furniture for employee #{e.employeeId} ({e.role}) — idle (alert MF-12)");
            return false;
        }

        /// <summary>
        /// Release przy OnFire/OnRetired + FIFO reassign do pierwszego pracownika bez biurka
        /// matching role. Spec MF-10 — czyste 1:1 z FIFO.
        /// </summary>
        public static void ReleaseAndReassignFifo(Employee e)
        {
            if (e == null || e.assignedFurnitureId < 0) return;

            int releasedFurnitureId = e.assignedFurnitureId;
            var placer = FurniturePlacer.Instance;
            if (placer == null)
            {
                e.assignedFurnitureId = -1;
                return;
            }

            var instance = placer.GetInstance(releasedFurnitureId);
            if (instance != null) instance.assignedEmployeeId = -1;
            e.assignedFurnitureId = -1;
            Log.Info($"[FurnitureAssignmentService] Released furniture #{releasedFurnitureId} from employee #{e.employeeId}");

            if (instance == null) return;  // furniture deleted — nothing to reassign

            var item = FurnitureCatalog.FindById(instance.itemId);
            if (item == null) return;

            // FIFO: znajdź pierwszego active employee bez biurka, którego role pasuje do funkcji item
            foreach (var candidate in PersonnelService.Employees)
            {
                if (candidate == null || !candidate.IsActive) continue;
                if (candidate.assignedFurnitureId >= 0) continue;
                var fn = GetRequiredFunction(candidate.role);
                if (!fn.HasValue) continue;
                if (!item.HasFunction(fn.Value)) continue;

                var preferredRoom = GetPreferredRoomType(candidate.role);
                if (preferredRoom.HasValue)
                {
                    var room = FindOwningRoom(instance.position);
                    if (room == null || room.roomType != preferredRoom.Value) continue;
                }

                // Reassign
                instance.assignedEmployeeId = candidate.employeeId;
                candidate.assignedFurnitureId = instance.instanceId;
                Log.Info($"[FurnitureAssignmentService] FIFO reassign: furniture #{instance.instanceId} " +
                         $"→ employee #{candidate.employeeId} ({candidate.role})");
                return;
            }

            Log.Info($"[FurnitureAssignmentService] FIFO reassign: brak kandydata dla furniture #{releasedFurnitureId}");
        }

        /// <summary>
        /// MF-12: czy pracownik jest "idle without furniture" — wymaga biurka ale go nie ma.
        /// True gdy:
        /// - <c>assignedFurnitureId &lt; 0</c> (brak przypisanego)
        /// - rola wymaga furniture (<see cref="GetRequiredFunction"/> != null)
        ///
        /// Używane przez PersonnelMainTabUI dla alert ikony oraz przez
        /// PersonnelDispatcher3D.ResolveWorkDestination dla idle pathfinding fallback.
        /// </summary>
        public static bool IsIdleWithoutFurniture(Employee e)
        {
            if (e == null) return false;
            if (e.assignedFurnitureId >= 0) return false;
            return GetRequiredFunction(e.role).HasValue;
        }

        /// <summary>
        /// MF-12: zwraca random world position w pokoju preferred type dla idle pracownika.
        /// Używane jako fallback w PersonnelDispatcher3D gdy brak biurka. Pracownik chodzi
        /// "bez celu" — random cell w bounds pokoju, deterministic seed z employeeId żeby
        /// nie przeskakiwał chaotycznie co frame.
        ///
        /// Zwraca null gdy brak preferred room type lub pokój nie istnieje w zajezdni.
        /// </summary>
        public static Vector3? GetIdleRoamPosition(Employee e)
        {
            if (e == null) return null;
            var preferred = GetPreferredRoomType(e.role);
            if (!preferred.HasValue) return null;

            var roomSys = Object.FindAnyObjectByType<RoomDetectionSystem>();
            if (roomSys == null) return null;

            // Znajdź pierwszy pokój preferred type
            DetectedRoom room = null;
            foreach (var r in roomSys.Rooms)
            {
                if (r != null && r.roomType == preferred.Value) { room = r; break; }
            }
            if (room == null) return null;

            // Random cell w bounds, deterministic seed = employeeId (stabilne między framami)
            int seed = e.employeeId;
            int xRange = Mathf.Max(1, room.bounds.width);
            int zRange = Mathf.Max(1, room.bounds.height);
            int dx = (seed * 31) % xRange;
            int dz = (seed * 47) % zRange;
            float wx = room.bounds.x + dx + 0.5f;
            float wz = room.bounds.y + dz + 0.5f;
            return new Vector3(wx, 0f, wz);
        }

        /// <summary>
        /// Helper: zwraca world position do której pracownik podchodzi (cell po stronie
        /// accessSide instance'a, wcentralizowana). Null gdy instance nie istnieje.
        /// </summary>
        public static Vector3? GetAccessSideWorldPosition(int instanceId)
        {
            var placer = FurniturePlacer.Instance;
            if (placer == null) return null;

            var instance = placer.GetInstance(instanceId);
            if (instance == null) return null;

            var item = FurnitureCatalog.FindById(instance.itemId);
            if (item == null) return null;

            var side = item.ParseAccessSide();
            if (side == AccessSide.All)
            {
                // Brak preferowanej strony — pracownik podchodzi z któregokolwiek wolnego cell.
                // MVP: użyj pivota +0.5m forward (cokolwiek byle blisko).
                return instance.position + Vector3.forward * 0.5f;
            }

            var accessCells = FurnitureSnapDetector.GetAccessSideCells(
                instance.position, item.footprintCells.x, item.footprintCells.y, instance.rotation, side);
            if (accessCells.Count == 0) return instance.position;

            // Środek listy access cells (najczęściej jedna cell, czasem N=footprint length)
            var center = accessCells[accessCells.Count / 2];
            return new Vector3(center.x + 0.5f, 0f, center.y + 0.5f);
        }

        /// <summary>
        /// Helper: pokój zawierający dany world point (XZ plane). Null gdy poza wszystkimi pokojami.
        /// </summary>
        private static DetectedRoom FindOwningRoom(Vector3 worldPos)
        {
            var roomSys = Object.FindAnyObjectByType<RoomDetectionSystem>();
            if (roomSys == null) return null;

            int cellX = Mathf.FloorToInt(worldPos.x);
            int cellZ = Mathf.FloorToInt(worldPos.z);
            var cell = new Vector2Int(cellX, cellZ);

            foreach (var room in roomSys.Rooms)
            {
                if (room == null) continue;
                if (room.bounds.Contains(cell)) return room;
            }
            return null;
        }
    }
}
