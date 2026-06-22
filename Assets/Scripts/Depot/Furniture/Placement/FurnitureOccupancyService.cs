using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.Furniture.Placement
{
    /// <summary>
    /// TD-034 B: transient rejestr rezerwacji WSPÓŁDZIELONYCH mebli (krzesła/sofy/WC/szafki) pod
    /// czynności pracowników (przerwa, łazienka, przebranie, idle-siad). Desk-i 1:1 idą osobno przez
    /// <c>PlacedFurnitureItem.assignedEmployeeId</c> (FurnitureAssignmentService) — tu rezerwujemy
    /// chwilowo, na czas czynności, żeby dwóch pracowników nie wpadło na to samo krzesło.
    ///
    /// <para>Operuje wyłącznie na typach Depot + <c>int ownerId</c> (= employeeId) → mieszka w asmdef
    /// Depot (jak <c>DepotNavService</c>); Personnel czyta przez asmdef. Bootstrap w DepotManager.</para>
    ///
    /// <para>NIE zapisywane — rejestr odtwarza się runtime (pracownicy re-acquire po load; release przy
    /// despawn/OnFire/EndOfShift). MonoBehaviour singleton z OnDestroy clear (test-isolation friendly).</para>
    ///
    /// Determinizm: <see cref="FindNearestFreeByFunction"/> deleguje do <see cref="FurnitureOccupancyMath"/>
    /// (sort dystans + tie-break id) → niezależny od kolejności <c>PlacedInstances</c>.
    /// </summary>
    public class FurnitureOccupancyService : MonoBehaviour
    {
        public static FurnitureOccupancyService Instance { get; private set; }

        // instanceId → ownerId (employeeId). Brak klucza = wolny.
        readonly Dictionary<int, int> _reservations = new();
        // Reużywalny bufor kandydatów (find-free rzadkie, ale zero-alloc kosztuje grosze).
        readonly List<(int, Vector2, bool)> _candBuffer = new();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _reservations.Clear();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Rezerwacje ────────────────────────────────────────────────

        /// <summary>Czy mebel jest wolny (nie zarezerwowany przez nikogo).</summary>
        public bool IsFree(int instanceId) => !_reservations.ContainsKey(instanceId);

        /// <summary>Owner rezerwujący mebel, lub -1 gdy wolny.</summary>
        public int GetOwner(int instanceId) => _reservations.TryGetValue(instanceId, out int o) ? o : -1;

        /// <summary>Rezerwuje mebel dla ownera. True gdy sukces (lub już jego). False gdy zajęty przez innego.</summary>
        public bool TryReserve(int instanceId, int ownerId)
        {
            if (instanceId < 0) return false;
            if (_reservations.TryGetValue(instanceId, out int cur))
                return cur == ownerId; // idempotentne dla tego samego ownera
            _reservations[instanceId] = ownerId;
            return true;
        }

        /// <summary>Zwalnia wszystkie meble ownera (przy zakończeniu czynności / despawn / fire / end-of-shift).</summary>
        public void Release(int ownerId)
        {
            if (_reservations.Count == 0) return;
            List<int> toRemove = null;
            foreach (var kv in _reservations)
                if (kv.Value == ownerId) (toRemove ??= new List<int>()).Add(kv.Key);
            if (toRemove != null)
                for (int i = 0; i < toRemove.Count; i++) _reservations.Remove(toRemove[i]);
        }

        /// <summary>Zwalnia konkretny mebel (niezależnie od ownera) — np. gdy mebel usunięty.</summary>
        public void ReleaseInstance(int instanceId) => _reservations.Remove(instanceId);

        // ── Wyszukiwanie wolnego mebla ────────────────────────────────

        /// <summary>
        /// Najbliższy WOLNY mebel z daną funkcją (np. SeatingRest, Sanitary, StoragePersonal) w lokalnym
        /// depot, opcjonalnie ograniczony do typu pokoju. Zwraca instanceId lub -1.
        /// Wolny = nie zarezerwowany w occupancy AND nie permanentny desk (assignedEmployeeId &lt; 0)
        /// AND functional state active (dojście wolne).
        /// </summary>
        public int FindNearestFreeByFunction(ObjectFunction fn, Vector3 from, RoomType? roomFilter = null)
        {
            var placer = FurniturePlacer.Instance;
            if (placer == null) return -1;
            int depotId = OwnershipService.LocalDepotId;

            RoomDetectionSystem roomSys = roomFilter.HasValue
                ? Object.FindAnyObjectByType<RoomDetectionSystem>() : null;

            _candBuffer.Clear();
            foreach (var inst in placer.PlacedInstances)
            {
                if (inst == null) continue;
                if (inst.depotId != depotId) continue;
                if (inst.assignedEmployeeId >= 0) continue; // permanentny desk — nie pod czynności
                var item = FurnitureCatalog.FindById(inst.itemId);
                if (item == null || !item.HasFunction(fn)) continue;
                if (!placer.GetFunctionalState(inst.instanceId).IsActive) continue;
                if (roomFilter.HasValue)
                {
                    var room = FindOwningRoom(roomSys, inst.position);
                    if (room == null || room.roomType != roomFilter.Value) continue;
                }
                bool occupied = _reservations.ContainsKey(inst.instanceId);
                _candBuffer.Add((inst.instanceId, new Vector2(inst.position.x, inst.position.z), occupied));
            }

            return FurnitureOccupancyMath.PickNearestFree(_candBuffer, new Vector2(from.x, from.z));
        }

        /// <summary>World-point gdzie pracownik staje przy meblu (access cell, jak biurka). Null gdy brak instancji.</summary>
        public Vector3? GetSeatPoint(int instanceId)
        {
            var placer = FurniturePlacer.Instance;
            if (placer == null) return null;
            var inst = placer.GetInstance(instanceId);
            if (inst == null) return null;
            var item = FurnitureCatalog.FindById(inst.itemId);
            if (item == null) return inst.position;

            var side = item.ParseAccessSide();
            if (side == AccessSide.All)
                return inst.position + Vector3.forward * 0.5f;

            var cells = FurnitureSnapDetector.GetAccessSideCells(
                inst.position, item.footprintCells.x, item.footprintCells.y, inst.rotation, side);
            if (cells.Count == 0) return inst.position;
            var center = cells[cells.Count / 2];
            return new Vector3(center.x + 0.5f, 0f, center.y + 0.5f);
        }

        static DetectedRoom FindOwningRoom(RoomDetectionSystem roomSys, Vector3 worldPos)
        {
            if (roomSys == null) return null;
            var cell = new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.z));
            foreach (var room in roomSys.Rooms)
            {
                if (room == null) continue;
                if (room.bounds.Contains(cell)) return room;
            }
            return null;
        }

        // ── Diagnostyka ───────────────────────────────────────────────

        [ContextMenu("TD-034: Dump occupancy")]
        void DumpOccupancy()
        {
            Log.Info($"[FurnitureOccupancyService] Rezerwacje: {_reservations.Count}");
            foreach (var kv in _reservations)
                Log.Info($"  mebel #{kv.Key} → pracownik #{kv.Value}");
        }
    }
}
