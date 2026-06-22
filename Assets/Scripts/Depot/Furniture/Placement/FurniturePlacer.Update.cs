using DepotSystem;
using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;

namespace DepotSystem.Furniture.Placement
{
    /// <summary>
    /// Partial: Update tick — cursor raycast + snap (grid + auto-snap-to-wall) +
    /// walidacja + preview transform + keyboard/mouse handlers.
    /// </summary>
    public partial class FurniturePlacer
    {
        void Update()
        {
            if (!_isActive) return;

            UpdateCursorWorldPosition();
            UpdateSnap();
            UpdatePreview();
            HandleKeyboardInput();
            HandleConfirmCancel();
        }

        private void UpdateCursorWorldPosition()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null || Mouse.current == null) return;

            Vector2 mouseScreen = Mouse.current.position.ReadValue();
            Ray ray = _mainCamera.ScreenPointToRay(mouseScreen);

            // Raycast na płaszczyznę Y=0 (zajezdnia jest płaska)
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float dist))
            {
                _cursorWorldPos = ray.GetPoint(dist);
            }
        }

        private void UpdateSnap()
        {
            if (_currentItem == null) return;

            // MF-6: parity-aware snap (footprint cells symmetric wokół pivota)
            _snappedWorldPos = FurnitureSnapDetector.SnapToGridForFootprint(
                _cursorWorldPos,
                _currentItem.footprintCells.x,
                _currentItem.footprintCells.y,
                _currentRotationDeg);

            // MF-5: auto-snap-to-wall (jeśli gracz nie wymusił manual override przez R)
            if (!_userManualRotation)
            {
                if (_wallSystem == null) _wallSystem = DepotServices.Get<WallBuildingSystem>();
                if (_wallSystem != null)
                {
                    var snap = FurnitureSnapDetector.AutoSnapToWall(_snappedWorldPos, _currentItem, _wallSystem);
                    _wallSnapActive = snap.hasSnap;
                    _wallSnapDistance = snap.hasSnap ? snap.distanceToWall : 0f;
                    if (snap.hasSnap && snap.suggestedRotationDeg != _currentRotationDeg)
                    {
                        _currentRotationDeg = snap.suggestedRotationDeg;
                        // Re-snap z nową rotacją (parity może się zmienić dla 90/270 vs 0/180)
                        _snappedWorldPos = FurnitureSnapDetector.SnapToGridForFootprint(
                            _cursorWorldPos,
                            _currentItem.footprintCells.x,
                            _currentItem.footprintCells.y,
                            _currentRotationDeg);
                    }
                }
                else
                {
                    _wallSnapActive = false;
                    _wallSnapDistance = 0f;
                }
            }
            else
            {
                _wallSnapActive = false;
                _wallSnapDistance = 0f;
            }

            // MF-6: walidacja placement
            UpdateValidation();
        }

        private void UpdateValidation()
        {
            if (_currentItem == null) return;

            if (_roomSystem == null) _roomSystem = DepotServices.Get<RoomDetectionSystem>();
            var rooms = _roomSystem != null ? _roomSystem.Rooms : null;

            _lastValidation = FurnitureValidator.Validate(
                _currentItem,
                _currentDepotId,
                _snappedWorldPos,
                _currentRotationDeg,
                rooms,
                _placedInstances);

            _validationLevel = _lastValidation.Level.ToString();
            _validationReason = _lastValidation.Reason ?? "";

            if (_previewRenderer != null)
            {
                _previewRenderer.SetValidationLevel(_lastValidation.Level);
            }
        }

        private void UpdatePreview()
        {
            if (_previewRenderer == null) return;
            _previewRenderer.SetPosition(_snappedWorldPos);
            _previewRenderer.SetRotation(_currentRotationDeg);
        }

        private void HandleKeyboardInput()
        {
            if (Keyboard.current == null) return;

            // R = rotate 90° (cykluje 0/90/180/270) + cancel auto-snap-to-wall (manual override)
            if (Keyboard.current.rKey.wasPressedThisFrame)
            {
                _currentRotationDeg = (_currentRotationDeg + 90) % 360;
                _userManualRotation = true;  // MF-5: po R gracz steruje rotacją ręcznie do końca placement
                _wallSnapActive = false;
                _wallSnapDistance = 0f;
                Log.Info($"[FurniturePlacer] Rotation: {_currentRotationDeg}° (manual override — auto-snap-to-wall wyłączony do końca placement)");
            }
        }

        private void HandleConfirmCancel()
        {
            // Esc = anuluj
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelPlacement();
                return;
            }

            // LMB = confirm — ALE tylko gdy kursor NIE jest nad UI (panel po lewej, toolbar etc.)
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (DepotUIManager.Instance != null && DepotUIManager.Instance.IsPointerOverUI())
                {
                    return;  // klik na UI — nie placement
                }
                ConfirmPlacement();
            }
        }
    }
}
