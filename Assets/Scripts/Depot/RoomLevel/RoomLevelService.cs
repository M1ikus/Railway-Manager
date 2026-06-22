using System;
using System.Collections.Generic;
using UnityEngine;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;
using RailwayManager.Core;

namespace DepotSystem.RoomLevel
{
    /// <summary>
    /// MM-2 — runtime service dla lvlowania pomieszczeń. Singleton MonoBehaviour
    /// w Depot asmdef.
    ///
    /// API:
    /// <list type="bullet">
    /// <item><see cref="GetRoomLevel"/> — odczyt aktualnego lvla pokoju</item>
    /// <item><see cref="CheckEligibility"/> — czy spełnione wymagania awansu (size + furniture)</item>
    /// <item><see cref="TryUpgrade"/> — explicit awans (MM-D1 klik) z walidacją</item>
    /// <item><see cref="GetBestLevelForType"/> — multi-rooms helper (MM-D15 best-lvl wins)</item>
    /// <item><see cref="OnLevelChanged"/> — event dla bonus consumers (Office/Dispatcher/etc.)</item>
    /// </list>
    ///
    /// MM-3 dorzuci UI integration (RoomTypePopupUI extension).
    /// MM-4..7 dorzucą bonus consumers (WorkshopManager Hall lvl, OfficeService cap, etc.).
    ///
    /// Brak downgrade (MM-D2) — once unlocked, lvl zostaje. <see cref="TryUpgrade"/>
    /// monotonicznie zwiększa o +1.
    /// </summary>
    public class RoomLevelService : MonoBehaviour
    {
        public static RoomLevelService Instance { get; private set; }

        /// <summary>Emitowany po pomyślnym awansie pokoju. Args: (roomId, oldLvl, newLvl).</summary>
        public static event Action<int, int, int> OnLevelChanged;

        public static RoomLevelService EnsureExists()
        {
            if (Instance != null) return Instance;
            var existing = FindAnyObjectByType<RoomLevelService>();
            if (existing != null) { Instance = existing; return Instance; }
            var go = new GameObject("RoomLevelService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<RoomLevelService>();
            Log.Info("[RoomLevelService] Bootstrapped");
            return Instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            EnsureExists();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ════════════════════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════════════════════

        /// <summary>Aktualny lvl pokoju (1-5 dla lvlable, 0 dla nielvlable / nieznanego roomId).</summary>
        public int GetRoomLevel(int roomId)
        {
            var room = FindRoom(roomId);
            if (room == null) return 0;
            if (!RoomLevelCatalog.IsLvlable(room.roomType)) return 0;
            return Mathf.Clamp(room.level, RoomLevelCatalog.DefaultLevel, RoomLevelCatalog.MaxLevel);
        }

        /// <summary>
        /// MM-D15 — best-lvl helper dla bonusów globalnych. Zwraca najwyższy lvl
        /// wśród pomieszczeń danego typu. 0 gdy brak pokoju tego typu.
        /// </summary>
        public int GetBestLevelForType(RoomType type)
        {
            if (!RoomLevelCatalog.IsLvlable(type)) return 0;

            var roomSys = GetRoomDetectionSystem();
            if (roomSys == null) return 0;

            int best = 0;
            foreach (var r in roomSys.Rooms)
            {
                if (r == null || r.roomType != type) continue;
                if (r.level > best) best = r.level;
            }
            return best;
        }

        /// <summary>
        /// Sprawdza czy pokój może być awansowany do następnego lvla (currentLevel + 1).
        /// Kompletna walidacja: size + wszystkie furniture requirements.
        /// </summary>
        public RoomUpgradeEligibility CheckEligibility(int roomId)
        {
            var room = FindRoom(roomId);
            if (room == null) return RoomUpgradeEligibility.NotLvlable(0);
            if (!RoomLevelCatalog.IsLvlable(room.roomType)) return RoomUpgradeEligibility.NotLvlable(room.level);

            int current = Mathf.Clamp(room.level, RoomLevelCatalog.DefaultLevel, RoomLevelCatalog.MaxLevel);
            int target = current + 1;

            if (target > RoomLevelCatalog.MaxLevel)
                return RoomUpgradeEligibility.MaxLevel(current);

            return CheckEligibilityFor(room, target);
        }

        /// <summary>
        /// Sprawdza eligibility dla konkretnego targetLevel (np. preview "co potrzebne do lvl5
        /// z aktualnego lvl1"). Zwraca strukturę z per-requirement check'ami dla UI.
        /// </summary>
        public RoomUpgradeEligibility CheckEligibilityFor(DetectedRoom room, int targetLevel)
        {
            var req = RoomLevelCatalog.GetRequirements(room.roomType, targetLevel);
            if (req == null) return RoomUpgradeEligibility.NotLvlable(room.level);

            var result = new RoomUpgradeEligibility
            {
                currentLevel = room.level,
                targetLevel = targetLevel,
                requiredAreaSqM = req.minAreaSqM,
                currentAreaSqM = room.areaSqM,
                sizeOk = room.areaSqM + 0.01f >= req.minAreaSqM, // mała epsilon na float compare
                furnitureChecks = new List<RequirementCheck>(req.furnitureRequirements.Count),
            };

            // Furniture checks
            var placedInRoom = GetFurnitureInRoom(room);
            bool allFurnitureOk = true;
            foreach (var fr in req.furnitureRequirements)
            {
                int actual = CountByRequirement(fr, placedInRoom);
                bool ok = actual >= fr.count;
                result.furnitureChecks.Add(new RequirementCheck
                {
                    requirement = fr,
                    actualCount = actual,
                    ok = ok,
                });
                if (!ok) allFurnitureOk = false;
            }

            result.canUpgrade = result.sizeOk && allFurnitureOk;
            return result;
        }

        /// <summary>
        /// MM-D1/D10 — explicit awans (klik gracza). Sprawdza eligibility, jeśli OK
        /// inkrementuje lvl i emituje OnLevelChanged.
        ///
        /// Bez downtime (MM-D10), bez kosztu placeholder w MM-2 (gospodarka MM-3 lub
        /// M-Balance — tutaj koncentrujemy się na mechanice). Failure reason zwracany
        /// przez <paramref name="failureReason"/> dla UI feedback.
        /// </summary>
        public bool TryUpgrade(int roomId, out string failureReason)
        {
            failureReason = null;
            var room = FindRoom(roomId);
            if (room == null) { failureReason = $"Pokój #{roomId} nie istnieje"; return false; }

            var eligibility = CheckEligibility(roomId);
            if (eligibility.roomTypeNotLvlable)
            {
                failureReason = $"Typ {room.roomType} nie ma lvlowania";
                return false;
            }
            if (eligibility.isMaxLevel)
            {
                failureReason = $"Pokój już na max lvl ({eligibility.currentLevel})";
                return false;
            }
            if (!eligibility.canUpgrade)
            {
                failureReason = eligibility.Summary;
                return false;
            }

            int oldLvl = room.level;
            int newLvl = eligibility.targetLevel;
            room.level = newLvl;

            Log.Info($"[RoomLevelService] Awans pokoju #{roomId} ({room.roomType}): " +
                     $"lvl {oldLvl} → {newLvl} (area {room.areaSqM:F0}m²)");
            OnLevelChanged?.Invoke(roomId, oldLvl, newLvl);
            return true;
        }

        /// <summary>
        /// Convenience: zwraca listę wszystkich pokoi z aktualnym lvl + eligibility next.
        /// Używane przez smoke test i diagnostykę.
        /// </summary>
        public List<(DetectedRoom room, RoomUpgradeEligibility eligibility)> GetAllRoomsWithEligibility()
        {
            var result = new List<(DetectedRoom, RoomUpgradeEligibility)>();
            var roomSys = GetRoomDetectionSystem();
            if (roomSys == null) return result;

            foreach (var r in roomSys.Rooms)
            {
                if (r == null) continue;
                var elig = CheckEligibility(r.roomId);
                result.Add((r, elig));
            }
            return result;
        }

        // ════════════════════════════════════════════════════════
        //  INTERNAL HELPERS
        // ════════════════════════════════════════════════════════

        /// <summary>Lookup pokoju po roomId. Null gdy RoomDetectionSystem nie istnieje lub roomId nieznany.</summary>
        private DetectedRoom FindRoom(int roomId)
        {
            var roomSys = GetRoomDetectionSystem();
            if (roomSys == null) return null;
            foreach (var r in roomSys.Rooms)
                if (r != null && r.roomId == roomId) return r;
            return null;
        }

        private RoomDetectionSystem _cachedRoomSys;
        private RoomDetectionSystem GetRoomDetectionSystem()
        {
            if (_cachedRoomSys != null) return _cachedRoomSys;
            _cachedRoomSys = DepotServices.Get<RoomDetectionSystem>();
            return _cachedRoomSys;
        }

        /// <summary>
        /// Zwraca listę PlacedFurnitureItem których world position mieści się w bounds pokoju
        /// (mapping cell = floor(position.x), floor(position.z)).
        /// </summary>
        private static List<PlacedFurnitureItem> GetFurnitureInRoom(DetectedRoom room)
        {
            var result = new List<PlacedFurnitureItem>();
            var placer = FurniturePlacer.Instance;
            if (placer == null) return result;

            foreach (var inst in placer.PlacedInstances)
            {
                if (inst == null) continue;
                int cellX = Mathf.FloorToInt(inst.position.x);
                int cellZ = Mathf.FloorToInt(inst.position.z);
                if (room.bounds.Contains(new Vector2Int(cellX, cellZ)))
                    result.Add(inst);
            }
            return result;
        }

        /// <summary>
        /// Liczy ile pasuje meblów dla danego wymagania (3 warianty: ItemId/Function/Compound).
        /// </summary>
        private static int CountByRequirement(FurnitureRequirement req, List<PlacedFurnitureItem> placedInRoom)
        {
            if (req == null || placedInRoom == null || placedInRoom.Count == 0) return 0;

            switch (req.kind)
            {
                case FurnitureReqKind.ItemId:
                    int countById = 0;
                    foreach (var inst in placedInRoom)
                        if (inst.itemId == req.id) countById++;
                    return countById;

                case FurnitureReqKind.Function:
                    if (!Enum.TryParse<ObjectFunction>(req.id, ignoreCase: true, out var fn))
                    {
                        Log.Warn($"[RoomLevelService] Unknown ObjectFunction in requirement: '{req.id}'");
                        return 0;
                    }
                    int countByFn = 0;
                    foreach (var inst in placedInRoom)
                    {
                        var item = FurnitureCatalog.FindById(inst.itemId);
                        if (item != null && item.HasFunction(fn)) countByFn++;
                    }
                    return countByFn;

                case FurnitureReqKind.Compound:
                    return WorkstationDefinitions.CountCompounds(placedInRoom, req.id);

                default:
                    return 0;
            }
        }

        // ════════════════════════════════════════════════════════
        //  DEBUG
        // ════════════════════════════════════════════════════════

        [ContextMenu("Debug: Print all rooms eligibility")]
        public void DebugPrintEligibility()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[RoomLevelService] All rooms eligibility:");
            foreach (var (room, elig) in GetAllRoomsWithEligibility())
            {
                sb.AppendLine($"  Room #{room.roomId} ({room.roomType}, lvl {room.level}, " +
                              $"{room.areaSqM:F0}m²): {elig.Summary}");
            }
            Log.Info(sb.ToString());
        }
    }
}
