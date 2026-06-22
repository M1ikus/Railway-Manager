using System.Text;
using UnityEngine;
using DepotSystem;
using DepotSystem.RoomLevel;
using RailwayManager.Core;
using RailwayManager.Fleet;

namespace RailwayManager.Maintenance
{
    /// <summary>
    /// MM-4 — diagnostyka integracji WorkshopManager ↔ Hall lvl (z M-Modernization).
    ///
    /// Auto-spawn jak inne smoke testy. ContextMenu metody:
    /// - PrintHallMapping: weryfikuje mapping Hall lvl 0-5 → WorkshopLevel + P-poziomy + slots
    /// - PrintWorkshopSlotsState: aktualne sloty po RescanDepotRooms (slot.level z Hall.level)
    /// - VerifyMaxLengthPerRoom: max ServicePit length per Hall (MM-D16 walidacja)
    /// - ForceRescan: manual rescan trigger
    ///
    /// Lokalizacja: Maintenance asmdef (split 2026-05-15) bo Depot.asmdef NIE referuje
    /// Maintenance/Timetable, ale Maintenance referuje Depot+Timetable. Smoke test używa obu.
    /// </summary>
    public class WorkshopHallLevelSmokeTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Object.FindAnyObjectByType<WorkshopHallLevelSmokeTest>() != null) return;
            var go = new GameObject("WorkshopHallLevelSmokeTest (auto-spawn)");
            go.AddComponent<WorkshopHallLevelSmokeTest>();
        }

        [ContextMenu("MM-4: Print Hall lvl → WorkshopLevel mapping")]
        public void PrintHallMapping()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[WorkshopHallLevelSmokeTest] MM-4 Hall lvl → WorkshopLevel mapping:");
            for (int hallLvl = 0; hallLvl <= 5; hallLvl++)
            {
                var ws = WorkshopLevelExtensions.FromHallLevel(hallLvl);
                int slots = ws.MaxSlots();
                bool p1 = ws.CanPerform(InspectionLevel.P1);
                bool p2 = ws.CanPerform(InspectionLevel.P2);
                bool p3 = ws.CanPerform(InspectionLevel.P3);
                bool p4 = ws.CanPerform(InspectionLevel.P4);
                bool p5 = ws.CanPerform(InspectionLevel.P5);
                bool mod = ws.CanPerformModernization();
                sb.AppendLine($"  Hall lvl {hallLvl} → {ws} (slots={slots}, " +
                              $"P1={p1} P2={p2} P3={p3} P4={p4} P5={p5}, modernization={mod})");
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-4: Print workshop slots state (after rescan)")]
        public void PrintWorkshopSlotsState()
        {
            var wm = WorkshopManager.Instance;
            if (wm == null)
            {
                Log.Warn("[WorkshopHallLevelSmokeTest] WorkshopManager not exists — wejdź w scenę Depot");
                return;
            }
            wm.RescanDepotRooms();  // force fresh scan z nowym Hall.level + ServicePit list
            var sb = new StringBuilder();
            sb.AppendLine($"[WorkshopHallLevelSmokeTest] Workshop slots ({wm.Slots.Count}, MM-8 slot per ServicePit):");
            foreach (var s in wm.Slots)
            {
                string status = s.occupyingVehicleId < 0 ? "WOLNY" : $"vehicle#{s.occupyingVehicleId}";
                sb.AppendLine($"  Slot#{s.slotId} room#{s.roomId} pit#{s.servicePitInstanceId} " +
                              $"{s.level.DisplayName()} (max length {s.maxVehicleLength:F0}m): {status}");
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-4: Verify maxVehicleLength per Hall room (MM-D16)")]
        public void VerifyMaxLengthPerRoom()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[WorkshopHallLevelSmokeTest] MM-D16 max ServicePit length per Hall:");
            var svc = RoomLevelService.EnsureExists();
            int hallCount = 0;
            foreach (var (room, _) in svc.GetAllRoomsWithEligibility())
            {
                if (room.roomType != RoomType.Hall) continue;
                float maxLen = WorkshopManager.GetMaxServicePitLengthInRoom(room.roomId);
                sb.AppendLine($"  Room #{room.roomId} (Hall lvl {room.level}, {room.areaSqM:F0}m²): " +
                              $"max ServicePit length = {(maxLen > 0f ? $"{maxLen:F0}m" : "brak ServicePit")}");
                hallCount++;
            }
            if (hallCount == 0) sb.AppendLine("  (brak Hall room w scenie — postaw najpierw)");
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-4: Force rescan workshop rooms")]
        public void ForceRescan()
        {
            var wm = WorkshopManager.Instance;
            if (wm == null) { Log.Warn("[WorkshopHallLevelSmokeTest] WorkshopManager not exists"); return; }
            wm.RescanDepotRooms();
            Log.Info($"[WorkshopHallLevelSmokeTest] Rescan done: {wm.Slots.Count} slots after refresh");
        }

        [ContextMenu("MM-4: Full summary")]
        public void FullSummary()
        {
            PrintHallMapping();
            PrintWorkshopSlotsState();
            VerifyMaxLengthPerRoom();
        }
    }
}
