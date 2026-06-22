using UnityEngine;
using DepotSystem;
using DepotSystem.Furniture.Placement;
using RailwayManager.Core;

namespace DepotSystem.Furniture.UI
{
    /// <summary>
    /// MF-3 + reorg 2026-05-03 — bootstrap component dla M-Furniture komponentów.
    ///
    /// Po reorganizacji UX: meble są dostępne przez RoomBuildPanelUI w trybie
    /// <see cref="ToolMode.BuildRoom"/> (zamiast osobnego ToolMode.Furniture). Bootstrap
    /// spawnuje Placer/Selector/ContextMenu gdy gracz wchodzi w BuildRoom — wszystkie
    /// komponenty współdzielą cykl życia z BuildRoom tool.
    ///
    /// Subscribuje się na <c>DepotUIManager.OnToolChanged</c>:
    /// - Mode = BuildRoom → spawn Placer + Selector + ContextMenu
    /// - Inny ToolMode → Deselect (FurnitureSelector dezaktywuje się sam)
    /// </summary>
    public class FurnitureToolBootstrap : MonoBehaviour
    {
        public static FurnitureToolBootstrap Instance { get; private set; }

        private bool _eventsSubscribed;

        /// <summary>
        /// Auto-spawn po załadowaniu sceny — gracz nie musi ręcznie wrzucać GameObject'u.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null) return;
            var existing = FindAnyObjectByType<FurnitureToolBootstrap>();
            if (existing != null) return;

            var go = new GameObject("FurnitureToolBootstrap (auto-spawn)");
            go.AddComponent<FurnitureToolBootstrap>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            // Event-driven init: jeśli DepotUIManager już istnieje → subscribe immediate;
            // w przeciwnym razie czekamy na OnReady event z jego Awake.
            DepotUIManager.OnReady += TrySubscribe;
            TrySubscribe();
        }

        private void TrySubscribe()
        {
            if (_eventsSubscribed) return;
            var uiManager = DepotUIManager.Instance;
            if (uiManager == null) return;

            uiManager.OnToolChanged += OnToolChanged;
            // Fix 2026-05-03: cancel aktywnego furniture placement gdy gracz zmienia
            // room sub-mode lub klika inną akcję (BuildRoom/PlaceHallTrack/etc.).
            // Bez tego cuboid krzesła nadal podążał za myszką po kliku "Buduj hale".
            uiManager.OnRoomSubModeChanged += OnRoomSubModeChanged;
            uiManager.OnRoomActionChanged += OnRoomActionChanged;
            // Fix 2026-05-03 #2: również Track/Catenary/Path sub-mode changes
            // (np. user wybral mebel w POM, potem przelaczyl na TOR -> WashZone).
            uiManager.OnTrackSubModeChanged += OnTrackSubModeChanged;
            uiManager.OnCatenarySubModeChanged += OnCatenarySubModeChanged;
            uiManager.OnPathSubModeChanged += OnPathSubModeChanged;
            _eventsSubscribed = true;
        }

        void OnDestroy()
        {
            DepotUIManager.OnReady -= TrySubscribe;
            UnsubscribeEvents();
            if (Instance == this) Instance = null;
        }

        private void UnsubscribeEvents()
        {
            if (!_eventsSubscribed) return;
            var uiManager = DepotUIManager.Instance;
            if (uiManager == null) return;

            uiManager.OnToolChanged -= OnToolChanged;
            uiManager.OnRoomSubModeChanged -= OnRoomSubModeChanged;
            uiManager.OnRoomActionChanged -= OnRoomActionChanged;
            uiManager.OnTrackSubModeChanged -= OnTrackSubModeChanged;
            uiManager.OnCatenarySubModeChanged -= OnCatenarySubModeChanged;
            uiManager.OnPathSubModeChanged -= OnPathSubModeChanged;
            _eventsSubscribed = false;
        }

        private void OnTrackSubModeChanged(TrackBuildSubMode mode)
        {
            CancelActiveFurniturePlacement($"OnTrackSubModeChanged({mode})");
        }

        private void OnCatenarySubModeChanged(CatenaryBuildSubMode mode)
        {
            CancelActiveFurniturePlacement($"OnCatenarySubModeChanged({mode})");
        }

        private void OnPathSubModeChanged(PathBuildSubMode mode)
        {
            CancelActiveFurniturePlacement($"OnPathSubModeChanged({mode})");
        }

        private void OnRoomSubModeChanged(RoomBuildSubMode mode)
        {
            CancelActiveFurniturePlacement("OnRoomSubModeChanged");
        }

        private void OnRoomActionChanged(RoomActionMode action)
        {
            // Klik na non-None room action (BuildRoom/PlaceHallTrack/etc.) anuluje furniture
            if (action != RoomActionMode.None)
            {
                CancelActiveFurniturePlacement($"OnRoomActionChanged({action})");
            }
        }

        private void CancelActiveFurniturePlacement(string reason)
        {
            var placer = FurniturePlacer.Instance;
            if (placer != null && placer.IsActive)
            {
                placer.CancelPlacement();
                Log.Info($"[FurnitureToolBootstrap] Cancelled furniture placement: {reason}");
            }
            var doorPlacer = DoorPlacer.Instance;
            if (doorPlacer != null && doorPlacer.IsActive)
            {
                doorPlacer.CancelPlacement();
                Log.Info($"[FurnitureToolBootstrap] Cancelled door placement: {reason}");
            }
        }

        private void OnToolChanged(ToolMode mode)
        {
            if (mode == ToolMode.BuildRoom)
            {
                // Reorg 2026-05-03: meble są częścią BuildRoom flow (RoomBuildPanelUI).
                // Spawn komponenty Furniture żeby były gotowe gdy gracz kliknie button mebla.
                EnsurePlacer();
                EnsureSelector();
                EnsureContextMenu();
            }
            else
            {
                // MF-7: deselect przy wyjściu z toola (selector reaguje na ToolMode w Update,
                // ale dla pewności wymuszamy też tutaj)
                if (FurnitureSelector.Instance != null)
                {
                    FurnitureSelector.Instance.Deselect();
                }
            }
        }

        /// <summary>Public API — zachowane dla kompatybilności (legacy callers). No-op po reorg.</summary>
        public void ForceShow()
        {
            try
            {
                EnsurePlacer();
                EnsureSelector();
                EnsureContextMenu();
            }
            catch (System.Exception e)
            {
                Log.Error($"[FurnitureToolBootstrap] ForceShow exception: {e.Message}\n{e.StackTrace}");
            }
        }

        private void EnsurePlacer()
        {
            if (FurniturePlacer.Instance != null) return;
            var existing = FindAnyObjectByType<FurniturePlacer>();
            if (existing != null) return;
            var go = new GameObject("FurniturePlacer (auto-created by FurnitureToolBootstrap)");
            go.AddComponent<FurniturePlacer>();
        }

        private void EnsureSelector()
        {
            if (FurnitureSelector.Instance != null) return;
            var existing = FindAnyObjectByType<FurnitureSelector>();
            if (existing != null) return;
            var go = new GameObject("FurnitureSelector (auto-created by FurnitureToolBootstrap)");
            go.AddComponent<FurnitureSelector>();
        }

        private void EnsureContextMenu()
        {
            if (FurnitureContextMenuUI.Instance != null) return;
            var existing = FindAnyObjectByType<FurnitureContextMenuUI>();
            if (existing != null) return;
            var go = new GameObject("FurnitureContextMenuUI (auto-created by FurnitureToolBootstrap)");
            go.AddComponent<FurnitureContextMenuUI>();
        }
    }
}
