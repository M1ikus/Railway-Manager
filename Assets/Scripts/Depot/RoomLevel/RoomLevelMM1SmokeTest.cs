using System.Text;
using UnityEngine;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;
using RailwayManager.Core;

namespace DepotSystem.RoomLevel
{
    /// <summary>
    /// MM-1 — diagnostyka data layer M-Modernization. Sprawdza ładowanie katalogu
    /// wymagań, compound definitions, oraz dostępność maxVehicleLength field.
    ///
    /// Auto-spawn jak <see cref="DepotSystem.Furniture.FurnitureSchemaSmokeTest"/> —
    /// dorzuca komponent na pierwszy GameObject w scenie po starcie. ContextMenu
    /// 4 metody dla manual validation w Unity Editor.
    ///
    /// MM-2 dorzuci pełny smoke test z runtime computation (RoomLevelService).
    /// Tutaj tylko data layer + structural integrity.
    /// </summary>
    public class RoomLevelMM1SmokeTest : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Object.FindAnyObjectByType<RoomLevelMM1SmokeTest>() != null) return;
            var go = new GameObject("RoomLevelMM1SmokeTest (auto-spawn)");
            go.AddComponent<RoomLevelMM1SmokeTest>();
        }

        [ContextMenu("MM-1: Print room level catalog")]
        public void PrintCatalog()
        {
            RoomLevelCatalog.EnsureInitialized();
            var sb = new StringBuilder();
            sb.AppendLine("[RoomLevelMM1SmokeTest] Room level catalog:");

            int total = 0;
            foreach (var roomType in RoomLevelCatalog.LvlableRoomTypes)
            {
                int max = RoomLevelCatalog.GetMaxLevel(roomType);
                sb.AppendLine($"  {roomType} (max lvl {max}):");
                for (int lvl = 1; lvl <= max; lvl++)
                {
                    var req = RoomLevelCatalog.GetRequirements(roomType, lvl);
                    if (req == null) continue;
                    total++;
                    sb.AppendLine($"    lvl{lvl}: ≥{req.minAreaSqM:F0}m², {req.furnitureRequirements.Count} req(s)");
                    foreach (var fr in req.furnitureRequirements)
                        sb.AppendLine($"      - {fr}");
                }
            }
            sb.AppendLine($"  TOTAL: {total} entries.");
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-1: Print compound definitions")]
        public void PrintCompounds()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[RoomLevelMM1SmokeTest] Compound definitions (MM-D9):");
            foreach (var kv in WorkstationDefinitions.Compounds)
            {
                sb.AppendLine($"  {kv.Key}:");
                foreach (var componentItemId in kv.Value)
                    sb.AppendLine($"    - {componentItemId}");
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-1: Verify maxVehicleLength on furniture catalog")]
        public void VerifyMaxVehicleLength()
        {
            if (!FurnitureCatalog.IsLoaded) FurnitureCatalog.LoadAll();
            var sb = new StringBuilder();
            sb.AppendLine("[RoomLevelMM1SmokeTest] maxVehicleLength values (MM-D16):");

            string[] expectedItems = { "pit_small", "pit_medium", "pit_large", "lift_aux", "wash_gate" };
            float[] expectedLengths = { 18f, 25f, 35f, 15f, 64f };

            int passed = 0, failed = 0;
            for (int i = 0; i < expectedItems.Length; i++)
            {
                var item = FurnitureCatalog.FindById(expectedItems[i]);
                if (item == null)
                {
                    sb.AppendLine($"  {expectedItems[i]}: NOT FOUND in catalog ✗");
                    failed++;
                    continue;
                }
                bool ok = Mathf.Approximately(item.maxVehicleLength, expectedLengths[i]);
                sb.AppendLine($"  {expectedItems[i]}: {item.maxVehicleLength:F0}m (expected {expectedLengths[i]:F0}m) " +
                              (ok ? "✓" : "✗"));
                if (ok) passed++; else failed++;
            }

            sb.AppendLine($"  Result: {passed}/{expectedItems.Length} passed.");
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-1: Test compound counting on placed instances")]
        public void TestCompoundCounting()
        {
            var placer = FurniturePlacer.Instance;
            if (placer == null)
            {
                Log.Warn("[RoomLevelMM1SmokeTest] FurniturePlacer not available — wstaw kilka mebli najpierw");
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine($"[RoomLevelMM1SmokeTest] Compound counting on {placer.PlacedInstances.Count} placed instances:");

            foreach (var compoundName in WorkstationDefinitions.Compounds.Keys)
            {
                int count = WorkstationDefinitions.CountCompounds(placer.PlacedInstances, compoundName);
                sb.AppendLine($"  {compoundName}: {count} kompletów");
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-1: Full data layer summary")]
        public void FullSummary()
        {
            PrintCatalog();
            PrintCompounds();
            VerifyMaxVehicleLength();
            TestCompoundCounting();
        }
    }
}
