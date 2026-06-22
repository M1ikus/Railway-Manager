using UnityEngine;
using UnityEngine.InputSystem;
using DepotSystem;
using RailwayManager.Core;

namespace DepotSystem.Furniture.Placement
{
    /// <summary>
    /// MF-11 — specjalny placer dla obiektów <see cref="SpecialPlacement.WallCell"/> (drzwi).
    ///
    /// Workflow:
    /// 1. <see cref="StartDoorPlacement"/> — wywoływane z <c>FurniturePlacer.StartPlacement</c>
    ///    gdy <c>item.specialPlacement == "WallCell"</c>. Zamiast standardowego flow
    ///    (snap do siatki + cuboid w pokoju), aktywuje door-mode.
    /// 2. Update co frame:
    ///    - Raycast cursor → world point (Y=0 plane)
    ///    - Znajdź najbliższą ścianę w 2m (<see cref="WallBuildingSystem.FindClosestWall"/>)
    ///    - Snap distance to grid + walidacja (<see cref="WallBuildingSystem.IsDoorPlacementValid"/>)
    ///    - Preview cuboid (door rotated to wall orientation)
    ///    - LMB confirm → <see cref="WallBuildingSystem.TryAddDoorOpening"/> + add to FurniturePlacer.PlacedInstances + dorzuca door cells do owning rooms
    ///    - Esc → cancel
    ///
    /// Aspekt PathGraph integration (cells po obu stronach drzwi passable) jest tylko **data prep** —
    /// pracownicy w EA chodzą w straight-line bez sprawdzania ścian (M8-10 MVP). Door cells
    /// służą głównie FurnitureValidator (MF-6) — zapewniają że gracz nie postawi mebli na drzwiach.
    ///
    /// Singleton, lazy-create przez FurniturePlacer.
    /// </summary>
    public class DoorPlacer : MonoBehaviour
    {
        public static DoorPlacer Instance { get; private set; }

        [Header("Wall snap")]
        [Tooltip("Maksymalny dystans (m) od kursora do ściany żeby drzwi się przyczepiły.")]
        public float wallSnapToleranceMeters = 2.0f;

        [Header("State (debug only)")]
        [SerializeField] private bool _isActive;
        [SerializeField] private string _currentItemId;
        [SerializeField] private int _currentDepotId = -1;
        [SerializeField] private bool _hasValidWall;
        [SerializeField] private float _currentDistOnWall;

        private FurnitureItem _currentItem;
        private FurniturePreviewRenderer _previewRenderer;
        private WallSegment _hoveredWall;
        private WallBuildingSystem _wallSystem;
        private RoomDetectionSystem _roomSystem;
        private Camera _mainCamera;
        private ValidationResult _lastValidation = ValidationResult.Ok();

        public bool IsActive => _isActive;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CleanupPreview();
        }

        // ════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════

        public void StartDoorPlacement(FurnitureItem item, int depotId)
        {
            if (item == null) { Log.Error("[DoorPlacer] StartDoorPlacement: item null"); return; }
            if (item.ParseSpecialPlacement() != SpecialPlacement.WallCell)
            {
                Log.Error($"[DoorPlacer] StartDoorPlacement: item '{item.id}' nie jest WallCell");
                return;
            }

            CleanupPreview();
            _currentItem = item;
            _currentItemId = item.id;
            _currentDepotId = depotId;
            _hasValidWall = false;
            _currentDistOnWall = 0f;
            _lastValidation = ValidationResult.Ok();
            CreatePreviewRenderer();
            _previewRenderer.SetItem(item);
            _isActive = true;
            Log.Info($"[DoorPlacer] StartDoorPlacement: '{item.id}' depot={depotId}");
        }

        public void CancelPlacement()
        {
            if (!_isActive) return;
            Log.Info($"[DoorPlacer] CancelPlacement: '{_currentItemId}'");
            CleanupPreview();
            _isActive = false;
            _currentItem = null;
            _currentItemId = null;
            _hoveredWall = null;
        }

        /// <summary>MM-15: czy aktualnie placement'owany item to track_gate (brama wjazdowa).
        /// Używane do route'owania logiki snap/walidacji/Add między door vs trackGate w WallBuildingSystem.</summary>
        private bool IsCurrentItemTrackGate()
            => _currentItem != null && _currentItem.id == "track_gate";

        // ════════════════════════════════════════
        //  UPDATE LOOP
        // ════════════════════════════════════════

        void Update()
        {
            if (!_isActive) return;
            if (_wallSystem == null) _wallSystem = DepotServices.Get<WallBuildingSystem>();
            if (_roomSystem == null) _roomSystem = DepotServices.Get<RoomDetectionSystem>();

            UpdateCursorAndWall();
            HandleConfirmCancel();
        }

        private void UpdateCursorAndWall()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null || Mouse.current == null) return;

            Vector2 mouseScreen = Mouse.current.position.ReadValue();
            Ray ray = _mainCamera.ScreenPointToRay(mouseScreen);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float dist)) { _hasValidWall = false; return; }
            Vector3 cursorWorld = ray.GetPoint(dist);

            if (_wallSystem == null) { _hasValidWall = false; return; }

            // Znajdź najbliższą ścianę
            _hoveredWall = _wallSystem.FindClosestWall(cursorWorld, wallSnapToleranceMeters);
            if (_hoveredWall == null)
            {
                _hasValidWall = false;
                _lastValidation = ValidationResult.Error("Brak ściany w pobliżu");
                if (_previewRenderer != null) _previewRenderer.SetValidationLevel(ValidationLevel.Error);
                return;
            }

            // Compute snap distance + validate (MM-15: TrackGate vs Door różny snap/walidacja)
            float rawDist = _wallSystem.ComputeDistanceOnWall(_hoveredWall, cursorWorld);
            bool isTrackGate = IsCurrentItemTrackGate();
            _currentDistOnWall = isTrackGate
                ? _wallSystem.SnapTrackGateDistance(_hoveredWall, rawDist)
                : _wallSystem.SnapDoorDistance(_hoveredWall, rawDist);
            bool valid = isTrackGate
                ? _wallSystem.IsTrackGatePlacementValid(_hoveredWall, _currentDistOnWall)
                : _wallSystem.IsDoorPlacementValid(_hoveredWall, _currentDistOnWall);

            _hasValidWall = valid;
            _lastValidation = valid
                ? ValidationResult.Ok()
                : ValidationResult.Error(isTrackGate
                    ? "Brama wjazdowa nie mieści się (potrzebna ściana ≥4m, brak kolizji z innymi otworami)"
                    : "Drzwi nie mieszczą się (kolizja z innym otworem lub poza krawędzią ściany)");

            // Position + rotation preview cuboid
            if (_previewRenderer != null)
            {
                Vector3 doorWorld = _hoveredWall.startPos + _hoveredWall.Direction * _currentDistOnWall;
                doorWorld.y = 0f;
                _previewRenderer.SetPosition(doorWorld);

                float angle = Mathf.Atan2(_hoveredWall.Direction.x, _hoveredWall.Direction.z) * Mathf.Rad2Deg;
                int rotInt = ((Mathf.RoundToInt(angle / 90f) * 90) % 360 + 360) % 360;
                _previewRenderer.SetRotation(rotInt);
                _previewRenderer.SetValidationLevel(valid ? ValidationLevel.Ok : ValidationLevel.Error);
            }
        }

        private void HandleConfirmCancel()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelPlacement();
                return;
            }

            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
            if (DepotUIManager.Instance != null && DepotUIManager.Instance.IsPointerOverUI()) return;
            if (!_hasValidWall) return;

            ConfirmPlacement();
        }

        // ════════════════════════════════════════
        //  CONFIRM
        // ════════════════════════════════════════

        private void ConfirmPlacement()
        {
            if (_hoveredWall == null || _wallSystem == null) return;
            if (!OwnershipService.IsOwnedByLocalPlayer(_currentDepotId))
            {
                Log.Warn($"[DoorPlacer] ConfirmPlacement: depot {_currentDepotId} nie należy do lokalnego gracza");
                return;
            }

            // 1. Cut hole in wall mesh — MM-15: TrackGate vs Door różny opening type
            bool isTrackGate = IsCurrentItemTrackGate();
            bool added = isTrackGate
                ? _wallSystem.TryAddTrackGateOpening(_hoveredWall, _currentDistOnWall)
                : _wallSystem.TryAddDoorOpening(_hoveredWall, _currentDistOnWall);
            if (!added)
            {
                Log.Warn($"[DoorPlacer] ConfirmPlacement: " +
                         $"TryAdd{(isTrackGate ? "TrackGate" : "Door")}Opening failed (walidacja zmieniła się?)");
                return;
            }

            // 2. Compute door world position + rotation
            Vector3 doorWorldPos = _hoveredWall.startPos + _hoveredWall.Direction * _currentDistOnWall;
            doorWorldPos.y = 0f;
            float angle = Mathf.Atan2(_hoveredWall.Direction.x, _hoveredWall.Direction.z) * Mathf.Rad2Deg;
            int rotInt = ((Mathf.RoundToInt(angle / 90f) * 90) % 360 + 360) % 360;

            // 3. Add to FurniturePlacer.PlacedInstances (przez public method dorzuconą poniżej)
            var placer = FurniturePlacer.Instance;
            if (placer == null)
            {
                Log.Error("[DoorPlacer] ConfirmPlacement: FurniturePlacer.Instance == null");
                return;
            }
            int instanceId = placer.AddDoorInstance(_currentItem, _currentDepotId, doorWorldPos, rotInt);

            // 4. Add door cells do owning rooms (po obu stronach ściany)
            AddDoorCellsToRooms(doorWorldPos, _hoveredWall);

            Log.Info($"[DoorPlacer] CONFIRM: door instance #{instanceId} '{_currentItem.id}' " +
                     $"at {doorWorldPos} rot={rotInt}° wall={_hoveredWall.wallId} dist={_currentDistOnWall:F2}m");

            // Tool zostaje aktywny — gracz może postawić kolejne drzwi (Esc cancel)
        }

        private void AddDoorCellsToRooms(Vector3 doorWorldPos, WallSegment wall)
        {
            if (_roomSystem == null) return;

            // Door cell on grid (door world pos ~ wall edge cell)
            Vector2Int doorCell = new Vector2Int(
                Mathf.FloorToInt(doorWorldPos.x),
                Mathf.FloorToInt(doorWorldPos.z));

            // Cells po obu stronach ściany — wall normal jest perpendicular do Direction
            Vector3 wallNormal = Vector3.Cross(wall.Direction, Vector3.up).normalized;
            Vector3 sideA = doorWorldPos + wallNormal * 0.6f;
            Vector3 sideB = doorWorldPos - wallNormal * 0.6f;

            Vector2Int cellA = new Vector2Int(Mathf.FloorToInt(sideA.x), Mathf.FloorToInt(sideA.z));
            Vector2Int cellB = new Vector2Int(Mathf.FloorToInt(sideB.x), Mathf.FloorToInt(sideB.z));

            foreach (var room in _roomSystem.Rooms)
            {
                if (room == null) continue;
                if (room.bounds.Contains(cellA) && !room.doorCells.Contains(cellA)) room.doorCells.Add(cellA);
                if (room.bounds.Contains(cellB) && !room.doorCells.Contains(cellB)) room.doorCells.Add(cellB);
                // Opcjonalnie też doorCell sam (centralny — głównie info diagnostic)
                if (room.bounds.Contains(doorCell) && !room.doorCells.Contains(doorCell)) room.doorCells.Add(doorCell);
            }
        }

        // ════════════════════════════════════════
        //  PRIVATE — preview lifecycle
        // ════════════════════════════════════════

        private void CreatePreviewRenderer()
        {
            var go = new GameObject("DoorPlacerPreview");
            go.transform.SetParent(transform, worldPositionStays: false);
            _previewRenderer = go.AddComponent<FurniturePreviewRenderer>();
        }

        private void CleanupPreview()
        {
            if (_previewRenderer != null)
            {
                _previewRenderer.Clear();
                Destroy(_previewRenderer.gameObject);
                _previewRenderer = null;
            }
        }
    }
}
