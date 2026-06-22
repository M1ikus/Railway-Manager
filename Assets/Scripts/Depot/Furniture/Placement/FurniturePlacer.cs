using System;
using System.Collections.Generic;
using UnityEngine;
using DepotSystem;
using DepotSystem.Furniture.Functional;

namespace DepotSystem.Furniture.Placement
{
    /// <summary>
    /// MF-4 — placement UX MVP dla furniture obiektów.
    ///
    /// Plik partial — root trzyma stan + lifecycle. Logika podzielona:
    /// <list type="bullet">
    ///   <item><c>FurniturePlacer.Lifecycle.cs</c> — public StartPlacement / CancelPlacement / ConfirmPlacement</item>
    ///   <item><c>FurniturePlacer.Instances.cs</c> — placed list management (Restore/AddDoor/Clear/Delete/Rotate/Move + RecomputeAllFunctionalStates)</item>
    ///   <item><c>FurniturePlacer.Update.cs</c> — Update tick (cursor raycast, snap, validation, preview, keyboard input)</item>
    ///   <item><c>FurniturePlacer.Visual.cs</c> — preview renderer lifecycle + stamped cuboid spawn (z warning overlay + DoorAnimator)</item>
    /// </list>
    ///
    /// Workflow:
    /// 1. <c>StartPlacement</c> — wywołane z <c>RoomBuildPanelUI</c> po kliku mebla,
    ///    sprawdza ownership, tworzy preview cuboid (FurniturePreviewRenderer)
    /// 2. <c>Update</c> — co frame: raycast cursor → world, snap do siatki 1×1m,
    ///    auto-snap-to-wall (MF-5), walidacja (MF-6), R rotate 90°, LMB confirm, Esc cancel
    /// 3. <c>ConfirmPlacement</c> — dorzuca PlacedFurnitureItem do internal list,
    ///    spawnuje stamped cuboid (MF-7 selectable) + FurnitureWarningOverlay (MF-8)
    ///    + DoorAnimator (MF-11 dla drzwi). Tool zostaje aktywny — można stawiać kolejne.
    ///
    /// Klawiszologia:
    /// - <b>R</b> — rotate 90° (cykluje 0/90/180/270), cancel auto-snap-to-wall
    /// - <b>LMB</b> — confirm placement (jeśli nie nad UI)
    /// - <b>Esc</b> — anuluj placement (z move rollback gdy w trybie Move)
    /// </summary>
    public partial class FurniturePlacer : MonoBehaviour
    {
        public static FurniturePlacer Instance { get; private set; }

        [Header("State (debug only — set by Update)")]
        [SerializeField] private bool _isActive;
        [SerializeField] private string _currentItemId;
        [SerializeField] private int _currentRotationDeg;
        [SerializeField] private int _currentDepotId = -1;
        [SerializeField] private int _placedInstancesCount;

        // ── Runtime ──
        private FurnitureItem _currentItem;
        private FurniturePreviewRenderer _previewRenderer;
        private Vector3 _cursorWorldPos;
        private Vector3 _snappedWorldPos;
        private Camera _mainCamera;
        private WallBuildingSystem _wallSystem;
        private RoomDetectionSystem _roomSystem;

        // ── MF-5 auto-snap-to-wall state ──
        [Header("MF-5 auto-snap state (debug only)")]
        [SerializeField] private bool _wallSnapActive;
        [SerializeField] private float _wallSnapDistance;
        [SerializeField] private bool _userManualRotation;  // R cancel auto-rotate dla aktualnego placement

        // ── MF-6 validation state ──
        [Header("MF-6 validation state (debug only)")]
        [SerializeField] private string _validationLevel = "Ok";
        [SerializeField] private string _validationReason = "";
        private ValidationResult _lastValidation = ValidationResult.Ok();

        // ── Placed instances (in-memory dla MF-4, MF-9 persisted przez DepotSavable) ──
        private readonly List<PlacedFurnitureItem> _placedInstances = new();
        private int _nextInstanceId = 1;

        // ── Spawned visualizations (cuboidy stamped, niezależne od preview) ──
        // MF-7: Dictionary dla O(1) lookup po instanceId (FurnitureSelector + actions).
        private readonly Dictionary<int, GameObject> _placedVisuals = new();

        // ── MF-8: functional state per instance + warning overlay refs ──
        private readonly Dictionary<int, FurnitureFunctionalState> _functionalStates = new();
        private readonly Dictionary<int, FurnitureWarningOverlay> _warningOverlays = new();

        // ── MF-7 Move: snapshot do rollback przy Esc ──
        /// <summary>2026-05-08 MoveInstance: snapshot oryginalnej instancji do rollback przy Esc.</summary>
        private PlacedFurnitureItem _moveSnapshot;
        /// <summary>2026-05-08 MoveInstance: flag żeby ConfirmPlacement wiedział czy przenieść assignedEmployeeId.</summary>
        private bool _isMoveOperation;

        /// <summary>MF-8: emitowany po każdej zmianie placedInstances/functional state
        /// (confirm/move/delete/rotate). Konsumenci: PartInventoryFurnitureBridge w Timetable.</summary>
        public event Action OnPlacementStateChanged;

        public IReadOnlyDictionary<int, FurnitureFunctionalState> FunctionalStates => _functionalStates;

        public bool IsActive => _isActive;
        public FurnitureItem CurrentItem => _currentItem;
        public IReadOnlyList<PlacedFurnitureItem> PlacedInstances => _placedInstances;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CleanupPreview();
        }
    }
}
