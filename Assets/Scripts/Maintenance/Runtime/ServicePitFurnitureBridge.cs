using UnityEngine;
using DepotSystem.Furniture.Placement;
using RailwayManager.Core;

namespace RailwayManager.Maintenance.Furniture
{
    /// <summary>
    /// MM-8 — bridge między <see cref="FurniturePlacer"/> a <see cref="WorkshopManager"/>.
    ///
    /// Subscribe na <c>FurniturePlacer.OnPlacementStateChanged</c> (po confirm/move/delete/rotate
    /// mebla, w szczególności gdy gracz dorzuci/usunie ServicePit). Handler wywołuje
    /// <see cref="WorkshopManager.RescanDepotRooms"/> dla instant rescan slotów.
    ///
    /// Architektura: bridge ląduje w <c>RailwayManager.Maintenance.Furniture</c> namespace
    /// bo <c>Depot.asmdef</c> NIE referuje Timetable (asymmetria zależności — Timetable używa
    /// Depot, nie odwrotnie). Wzorzec analogiczny do <c>PartInventoryFurnitureBridge</c> (MF-8).
    ///
    /// Bez tego bridge'a, slot per ServicePit aktualizowałby się tylko co 5s tick — gracz
    /// musiałby czekać na refresh po postawieniu kanału. Bridge daje natychmiastowe
    /// odświeżenie.
    /// </summary>
    public class ServicePitFurnitureBridge : MonoBehaviour
    {
        public static ServicePitFurnitureBridge Instance { get; private set; }

        private bool _eventsSubscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var existing = FindAnyObjectByType<ServicePitFurnitureBridge>();
            if (existing != null) return;

            var go = new GameObject("ServicePitFurnitureBridge (auto-spawn)");
            go.AddComponent<ServicePitFurnitureBridge>();
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
            // Lazy retry — FurniturePlacer.Instance może być null dopóki gracz nie wszedł
            // w Furniture tool (lazy-create przy ForceShow). Analogicznie do
            // PartInventoryFurnitureBridge.
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

            // Initial rescan (gdyby już były postawione ServicePit przed subscribe)
            OnPlacementStateChanged();
        }

        private void UnsubscribeEvents()
        {
            if (!_eventsSubscribed) return;
            var placer = FurniturePlacer.Instance;
            if (placer != null) placer.OnPlacementStateChanged -= OnPlacementStateChanged;
            _eventsSubscribed = false;
        }

        private void OnPlacementStateChanged()
        {
            var wm = WorkshopManager.Instance;
            if (wm == null) wm = WorkshopManager.EnsureExists();
            wm.RescanDepotRooms();
        }

        [ContextMenu("Force rescan workshop")]
        public void ForceRescan() => OnPlacementStateChanged();
    }
}
