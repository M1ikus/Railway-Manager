using System.Text;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.RoomLevel
{
    /// <summary>
    /// MM-2 — diagnostyka runtime computation. Sprawdza RoomLevelService
    /// (eligibility, upgrade, best-lvl-for-type, event subscribe).
    ///
    /// Auto-spawn jak <see cref="RoomLevelMM1SmokeTest"/>.
    /// 5 ContextMenu metod dla manual validation w Unity Editor:
    /// - Print all rooms with eligibility
    /// - Force upgrade first eligible room
    /// - Print best lvl per type (MM-D15)
    /// - Subscribe OnLevelChanged + log
    /// - Full MM-2 summary
    /// </summary>
    public class RoomLevelMM2SmokeTest : MonoBehaviour
    {
        private bool _eventSubscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Object.FindAnyObjectByType<RoomLevelMM2SmokeTest>() != null) return;
            var go = new GameObject("RoomLevelMM2SmokeTest (auto-spawn)");
            go.AddComponent<RoomLevelMM2SmokeTest>();
        }

        void OnEnable()
        {
            if (_eventSubscribed) return;
            RoomLevelService.OnLevelChanged += OnLevelChangedHandler;
            _eventSubscribed = true;
        }

        void OnDisable()
        {
            if (!_eventSubscribed) return;
            RoomLevelService.OnLevelChanged -= OnLevelChangedHandler;
            _eventSubscribed = false;
        }

        private void OnLevelChangedHandler(int roomId, int oldLvl, int newLvl)
        {
            Log.Info($"[RoomLevelMM2SmokeTest] OnLevelChanged event: room #{roomId} {oldLvl} → {newLvl}");
        }

        [ContextMenu("MM-2: Print all rooms with eligibility")]
        public void PrintAllEligibility()
        {
            var svc = RoomLevelService.EnsureExists();
            var sb = new StringBuilder();
            sb.AppendLine("[RoomLevelMM2SmokeTest] All rooms with eligibility check:");

            var rooms = svc.GetAllRoomsWithEligibility();
            if (rooms.Count == 0)
            {
                sb.AppendLine("  (brak pokoi — zbuduj kilka w scenie Depot)");
                Log.Info(sb.ToString());
                return;
            }

            foreach (var (room, elig) in rooms)
            {
                sb.AppendLine($"  Room #{room.roomId} ({room.roomType}, lvl {room.level}, {room.areaSqM:F0}m²):");
                if (elig.roomTypeNotLvlable)
                {
                    sb.AppendLine($"    Typ bez lvlowania");
                    continue;
                }
                if (elig.isMaxLevel)
                {
                    sb.AppendLine($"    Max lvl {elig.currentLevel} osiągnięty");
                    continue;
                }

                sb.AppendLine($"    Awans do lvl {elig.targetLevel}: {(elig.canUpgrade ? "✓ READY" : "✗ blocked")}");
                sb.AppendLine($"    Area: {elig.currentAreaSqM:F0}/{elig.requiredAreaSqM:F0}m² {(elig.sizeOk ? "✓" : "✗")}");
                if (elig.furnitureChecks != null)
                {
                    foreach (var c in elig.furnitureChecks)
                    {
                        sb.AppendLine($"    {(c.ok ? "✓" : "✗")} {c.requirement.kind}:{c.requirement.id} " +
                                      $"{c.actualCount}/{c.requirement.count}");
                    }
                }
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-2: Force upgrade first eligible room")]
        public void ForceUpgradeFirstEligible()
        {
            var svc = RoomLevelService.EnsureExists();
            foreach (var (room, elig) in svc.GetAllRoomsWithEligibility())
            {
                if (!elig.canUpgrade) continue;
                bool ok = svc.TryUpgrade(room.roomId, out var failureReason);
                Log.Info($"[RoomLevelMM2SmokeTest] TryUpgrade room #{room.roomId} ({room.roomType}): " +
                         (ok ? $"SUCCESS lvl {elig.currentLevel} → {elig.targetLevel}"
                             : $"FAILED ({failureReason})"));
                return;
            }
            Log.Info("[RoomLevelMM2SmokeTest] Brak pokoju gotowego do awansu (ForceUpgradeFirstEligible no-op)");
        }

        [ContextMenu("MM-2: Print best level per RoomType (MM-D15)")]
        public void PrintBestLevelPerType()
        {
            var svc = RoomLevelService.EnsureExists();
            var sb = new StringBuilder();
            sb.AppendLine("[RoomLevelMM2SmokeTest] Best level per RoomType (MM-D15 multi-rooms):");
            foreach (var rt in RoomLevelCatalog.LvlableRoomTypes)
            {
                int best = svc.GetBestLevelForType(rt);
                sb.AppendLine($"  {rt}: lvl {best}{(best == 0 ? " (brak pokoju tego typu)" : "")}");
            }
            Log.Info(sb.ToString());
        }

        [ContextMenu("MM-2: Force downgrade test room (debug only)")]
        public void DebugDowngradeFirstRoom()
        {
            // MM-D2: brak downgrade w gameplay'u, ale ten ContextMenu jest do testów awansu
            // (ustawia random pokój na lvl 1 żeby gracz mógł testować awans od początku).
            var svc = RoomLevelService.EnsureExists();
            foreach (var (room, _) in svc.GetAllRoomsWithEligibility())
            {
                if (room.level <= 1) continue;
                Log.Info($"[RoomLevelMM2SmokeTest] DEBUG: reset room #{room.roomId} ({room.roomType}) " +
                         $"lvl {room.level} → 1");
                room.level = 1;
                return;
            }
            Log.Info("[RoomLevelMM2SmokeTest] Brak pokoju z lvl > 1 do resetu");
        }

        [ContextMenu("MM-2: Full summary")]
        public void FullSummary()
        {
            PrintAllEligibility();
            PrintBestLevelPerType();
        }

        // ════════════════════════════════════════════════════════
        //  MM-3 — UI integration tests
        // ════════════════════════════════════════════════════════

        [ContextMenu("MM-3: Open level popup for first lvlable room")]
        public void OpenPopupForFirstLvlableRoom()
        {
            var ui = DepotUIManager.Instance;
            if (ui == null || ui.roomLevelPopup == null)
            {
                Log.Warn("[RoomLevelMM2SmokeTest] DepotUIManager / roomLevelPopup nie istnieje (jesteś w Depot scene?)");
                return;
            }

            var svc = RoomLevelService.EnsureExists();
            foreach (var (room, _) in svc.GetAllRoomsWithEligibility())
            {
                if (!RoomLevelCatalog.IsLvlable(room.roomType)) continue;
                Log.Info($"[RoomLevelMM2SmokeTest] Open RoomLevelPopupUI dla room #{room.roomId} ({room.roomType})");
                ui.roomLevelPopup.ShowFor(room.roomId);
                return;
            }
            Log.Info("[RoomLevelMM2SmokeTest] Brak pokoju lvlable w scenie — postaw najpierw Hall/Office/etc.");
        }

        [ContextMenu("MM-3: Close level popup")]
        public void ClosePopup()
        {
            var ui = DepotUIManager.Instance;
            if (ui != null && ui.roomLevelPopup != null) ui.roomLevelPopup.Close();
        }
    }
}
