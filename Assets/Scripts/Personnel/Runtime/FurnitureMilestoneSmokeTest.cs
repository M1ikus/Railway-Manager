using UnityEngine;
using DepotSystem;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;
using RailwayManager.Core;
using RailwayManager.Maintenance;

namespace RailwayManager.Personnel.Furniture
{
    /// <summary>
    /// MF-13 — diagnostic component pokrywający scenariusze smoke test'u milestone'u M-Furniture.
    ///
    /// Komponent ląduje w Personnel asmdef bo wymaga dostępu do trzech namespace'ów:
    /// - <c>DepotSystem.Furniture.*</c> (FurniturePlacer, FurnitureCatalog) — Personnel referuje Depot
    /// - <c>RailwayManager.Maintenance.PartInventoryService</c> — Personnel referuje Timetable
    /// - <c>RailwayManager.Personnel.Furniture.FurnitureAssignmentService</c> — własny asmdef
    /// Asymetria asmdef:
    /// - Depot.asmdef referuje tylko Core/Fleet/SharedUI/MainMenu
    /// - Timetable.asmdef referuje Depot ale NIE Personnel
    /// - Personnel.asmdef referuje Depot + Timetable + Fleet (= najszersze pre-SaveLoad)
    ///
    /// Wymaga manual setup w Unity (postawienie pokojów, mebli, zatrudnienie pracowników).
    /// Po setupie ContextMenu metody raportują stan systemu — gracz waliduje że wszystko działa.
    ///
    /// Scenariusze (z spec'a MF-13):
    /// 1. Office 5×5 + 3 biurka + 5 krzeseł + 3 dyspozytorów → każdy ma biurko
    /// 2. Brak dojścia — postaw biurko plecami w róg → warning ikon ✗
    /// 3. Funkcji per-depot — regały w Hall+Storage → PartInventoryService.GetCapacity
    /// 4. Multi-depot — 2 depot'y z różnym furniture, scope per depot
    /// 5. Save/load — scenariusz #1 + save F5 + load F9 → wszystko na miejscu
    /// 6. Drzwi — 2 pokoje + drzwi → pracownik przechodzi, animacja
    /// </summary>
    public class FurnitureMilestoneSmokeTest : MonoBehaviour
    {
        [ContextMenu("MF-13 #1: Report all placed instances + functional state")]
        public void Report_PlacedAndFunctional()
        {
            var placer = FurniturePlacer.Instance;
            if (placer == null) { Log.Warn("[MF-13] FurniturePlacer.Instance == null"); return; }

            int total = placer.PlacedInstances.Count;
            int active = 0, blocked = 0;
            foreach (var inst in placer.PlacedInstances)
            {
                var fn = placer.GetFunctionalState(inst.instanceId);
                if (fn.IsActive) active++; else blocked++;
            }
            Log.Info($"[MF-13] Total placed: {total} (active: {active}, blocked: {blocked})");

            foreach (var inst in placer.PlacedInstances)
            {
                var fn = placer.GetFunctionalState(inst.instanceId);
                var item = FurnitureCatalog.FindById(inst.itemId);
                string reason = fn.IsActive ? "" : $" [{fn.BlockedReason}]";
                Log.Info($"  #{inst.instanceId,3} {inst.itemId,-22} depot={inst.depotId} pos={inst.position} rot={inst.rotation,3}° " +
                         $"{(fn.IsActive ? "✓" : "✗")} employee={inst.assignedEmployeeId}{reason}");
            }
        }

        [ContextMenu("MF-13 #2: Per-depot capacity (PartInventoryService)")]
        public void Report_PerDepotCapacity()
        {
            var inv = PartInventoryService.Instance;
            if (inv == null) { Log.Warn("[MF-13] PartInventoryService.Instance == null"); return; }
            inv.DebugDump();
        }

        [ContextMenu("MF-13 #3: Personnel furniture assignment report")]
        public void Report_PersonnelAssignment()
        {
            int total = 0, withFurniture = 0, idle = 0, noFurnitureRequired = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (e == null || !e.IsActive) continue;
                total++;
                var fn = FurnitureAssignmentService.GetRequiredFunction(e.role);
                if (!fn.HasValue) { noFurnitureRequired++; continue; }
                if (e.assignedFurnitureId >= 0) withFurniture++;
                else idle++;
            }
            Log.Info($"[MF-13] Personnel: {total} active, {withFurniture} z biurkiem, {idle} idle (alert), " +
                     $"{noFurnitureRequired} role bez biurka (Driver/Conductor/Mechanic/Cleaner/WashBay)");

            foreach (var e in PersonnelService.Employees)
            {
                if (e == null || !e.IsActive) continue;
                var fn = FurnitureAssignmentService.GetRequiredFunction(e.role);
                string furnitureInfo;
                if (!fn.HasValue) furnitureInfo = "(role bez biurka)";
                else if (e.assignedFurnitureId >= 0) furnitureInfo = $"furniture #{e.assignedFurnitureId}";
                else furnitureInfo = "⚠ IDLE (brak biurka)";

                Log.Info($"  #{e.employeeId,3} {e.DisplayShortName,-25} {e.role,-20} {furnitureInfo}");
            }
        }

        [ContextMenu("MF-13 #4: Door cells per room")]
        public void Report_DoorCellsPerRoom()
        {
            var roomSys = FindAnyObjectByType<RoomDetectionSystem>();
            if (roomSys == null) { Log.Warn("[MF-13] RoomDetectionSystem nie istnieje"); return; }
            int totalDoors = 0;
            foreach (var room in roomSys.Rooms)
            {
                if (room == null) continue;
                int n = room.doorCells?.Count ?? 0;
                totalDoors += n;
                if (n > 0)
                {
                    var cells = string.Join(",", room.doorCells.ConvertAll(c => $"({c.x},{c.y})"));
                    Log.Info($"[MF-13] Room #{room.roomId} ({room.roomType}): {n} door cells [{cells}]");
                }
                else
                {
                    Log.Info($"[MF-13] Room #{room.roomId} ({room.roomType}): brak drzwi");
                }
            }
            Log.Info($"[MF-13] Total door cells across {roomSys.Rooms.Count} rooms: {totalDoors}");
        }

        [ContextMenu("MF-13 #5: Force recompute everything")]
        public void Force_RecomputeAll()
        {
            var placer = FurniturePlacer.Instance;
            if (placer == null) { Log.Warn("[MF-13] FurniturePlacer.Instance == null"); return; }
            placer.RecomputeAllFunctionalStates();
            Log.Info("[MF-13] RecomputeAllFunctionalStates wywołane (functional states + bridge → PartInventory)");
        }

        [ContextMenu("MF-13 #6: Force assign all idle employees")]
        public void Force_AssignAllIdle()
        {
            int assigned = 0;
            foreach (var e in PersonnelService.Employees)
            {
                if (e == null || !e.IsActive) continue;
                if (e.assignedFurnitureId >= 0) continue;
                if (FurnitureAssignmentService.AssignBestFurniture(e))
                    assigned++;
            }
            Log.Info($"[MF-13] Force-assigned {assigned} idle employees do wolnych biurek");
        }

        [ContextMenu("MF-13 #7: Full milestone summary")]
        public void Report_MilestoneSummary()
        {
            Log.Info("════════════ MF-13 — M-Furniture Milestone Summary ════════════");
            Report_PlacedAndFunctional();
            Log.Info("─────────────────────────────────────────────────────────────");
            Report_PerDepotCapacity();
            Log.Info("─────────────────────────────────────────────────────────────");
            Report_PersonnelAssignment();
            Log.Info("─────────────────────────────────────────────────────────────");
            Report_DoorCellsPerRoom();
            Log.Info("════════════ MF-13 summary done ════════════");
        }
    }
}
