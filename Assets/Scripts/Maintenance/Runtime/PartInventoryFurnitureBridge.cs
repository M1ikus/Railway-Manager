using System.Collections.Generic;
using UnityEngine;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;
using DepotSystem.Furniture.Functional;
using RailwayManager.Core;

namespace RailwayManager.Maintenance.Furniture
{
    /// <summary>
    /// MF-8 — bridge między <see cref="FurniturePlacer"/> a <see cref="PartInventoryService"/>.
    ///
    /// Subscribe na <c>FurniturePlacer.OnPlacementStateChanged</c> (po confirm/move/delete/rotate).
    /// Handler iteruje wszystkie placed instances, group by depotId, sumuje aktywne
    /// (<see cref="FurnitureFunctionalState.IsActive"/>=true) obiekty z funkcją <c>StorageGoods</c>:
    ///
    ///   capacity[depotId] = DefaultDepotCapacity + Σ(footprintCells.x × footprintCells.y × UnitsPerCell)
    ///
    /// Wywołuje <see cref="PartInventoryService.SetCapacity"/> per-depot.
    ///
    /// Architektura: bridge ląduje w Timetable.Maintenance namespace bo Depot.asmdef NIE
    /// referuje Timetable (asymmetria zależności — Timetable używa Depot, nie odwrotnie).
    /// FurniturePlacer exposuje neutralny event, bridge subscribe.
    /// </summary>
    public class PartInventoryFurnitureBridge : MonoBehaviour
    {
        public static PartInventoryFurnitureBridge Instance { get; private set; }

        /// <summary>Capacity units per cell footprint dla obiektu z funkcją StorageGoods.
        /// Placeholder — M-Balance dorzuci real value per-typ regału.</summary>
        public const int UnitsPerCell = 50;

        private bool _eventsSubscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var existing = FindAnyObjectByType<PartInventoryFurnitureBridge>();
            if (existing != null) return;

            var go = new GameObject("PartInventoryFurnitureBridge (auto-spawn)");
            go.AddComponent<PartInventoryFurnitureBridge>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            TrySubscribe();
        }

        void Update()
        {
            // Lazy retry — FurniturePlacer.Instance może być null dopóki gracz nie wszedł w
            // Furniture tool (FurnitureToolBootstrap lazy-create dopiero przy ForceShow).
            if (!_eventsSubscribed) TrySubscribe();
        }

        void OnDestroy()
        {
            UnsubscribeEvents();
            if (Instance == this) Instance = null;
        }

        private void TrySubscribe()
        {
            if (_eventsSubscribed) return;
            var placer = FurniturePlacer.Instance;
            if (placer == null) return;

            placer.OnPlacementStateChanged += OnPlacementStateChanged;
            _eventsSubscribed = true;

            // Initial recompute (gdyby były już jakieś placed instances przed subscribe)
            OnPlacementStateChanged();
        }

        private void UnsubscribeEvents()
        {
            if (!_eventsSubscribed) return;
            var placer = FurniturePlacer.Instance;
            if (placer != null) placer.OnPlacementStateChanged -= OnPlacementStateChanged;
            _eventsSubscribed = false;
        }

        // ════════════════════════════════════════
        //  RECOMPUTE
        // ════════════════════════════════════════

        private void OnPlacementStateChanged()
        {
            var placer = FurniturePlacer.Instance;
            var inv = PartInventoryService.Instance;
            if (placer == null || inv == null) return;

            // Group capacity by depotId
            var perDepotCapacity = new Dictionary<int, int>();

            foreach (var instance in placer.PlacedInstances)
            {
                if (instance == null) continue;

                var item = FurnitureCatalog.FindById(instance.itemId);
                if (item == null) continue;
                if (!item.HasFunction(ObjectFunction.StorageGoods)) continue;

                // Tylko active obiekty (B14: brak dojścia = function blocked, nie liczy się)
                var fn = placer.GetFunctionalState(instance.instanceId);
                if (!fn.IsActive) continue;

                int cells = Mathf.Max(1, item.footprintCells.x) * Mathf.Max(1, item.footprintCells.y);
                int contribution = cells * UnitsPerCell;

                if (perDepotCapacity.TryGetValue(instance.depotId, out int existing))
                    perDepotCapacity[instance.depotId] = existing + contribution;
                else
                    perDepotCapacity[instance.depotId] = contribution;
            }

            // SetCapacity per-depot — base = DefaultDepotCapacity + suma z regałów
            foreach (var kv in perDepotCapacity)
            {
                int total = PartInventoryService.DefaultDepotCapacity + kv.Value;
                inv.SetCapacity(kv.Key, total);
            }

            int totalDepots = perDepotCapacity.Count;
            int totalUnits = 0;
            foreach (var kv in perDepotCapacity) totalUnits += kv.Value;
            Log.Info($"[PartInventoryFurnitureBridge] Recompute: {totalDepots} depot(ów) z aktywnymi regałami, " +
                     $"+{totalUnits} units total (UnitsPerCell={UnitsPerCell})");
        }

        [ContextMenu("Force recompute")]
        public void ForceRecompute() => OnPlacementStateChanged();
    }
}
