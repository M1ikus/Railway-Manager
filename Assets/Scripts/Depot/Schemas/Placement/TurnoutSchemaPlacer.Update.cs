using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;

namespace DepotSystem.Schemas.Placement
{
    /// <summary>
    /// Partial: Update tick (cursor raycast + keyboard input + dispatch do snap/preview/confirm).
    /// </summary>
    public partial class TurnoutSchemaPlacer
    {
        // ════════════════════════════════════════
        //  UPDATE — input + raycast + snap + preview transform
        // ════════════════════════════════════════

        void Update()
        {
            if (!_isActive) return;
            if (_currentGeometry == null) return;

            // 1. Cursor → world position (płaszczyzna Y=0)
            UpdateCursorWorldPosition();

            // 2. Klawiszologia
            HandleKeyboardInput();

            // 3. Snap detection (multi-endpoint MD-4) — chyba że Shift hold
            bool snapDisabled = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            if (!snapDisabled)
            {
                UpdateSnapV2();
            }
            else
            {
                ResetSnapState();
            }

            // 4. Apply auto-rotation (raz po snap aktywuje, jeśli enableAutoRotation)
            if (enableAutoRotation && _hasSnap && !_autoRotationApplied
                && _lastSnapResult != null && _lastSnapResult.proposedRotationDeg.HasValue)
            {
                ApplyAutoRotation(_lastSnapResult.proposedRotationDeg.Value);
            }

            // 5. Apply transform do preview
            UpdatePreviewTransform();

            // 6. Update snap visualization w preview renderer
            UpdatePreviewSnapVisualization();

            // 7. Confirm / Cancel
            HandleConfirmCancel();
        }

        private void ResetSnapState()
        {
            _hasSnap = false;
            _snappedEndpointCount = 0;
            _snapTranslation = Vector3.zero;
            _autoRotationApplied = false;
            _hasAdaptiveProposal = false;
            _lastSnapResult = null;
        }

        private void ApplyAutoRotation(float newRotationDeg)
        {
            float old = _currentRotationDeg;
            _currentRotationDeg = newRotationDeg;
            _autoRotationApplied = true;
            Log.Info($"[TurnoutSchemaPlacer] Auto-rotation applied: {old:F1}° → {newRotationDeg:F1}° (snap collinearity)");
        }

        private void UpdateCursorWorldPosition()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null || Mouse.current == null) return;

            Vector2 mouseScreen = Mouse.current.position.ReadValue();
            Ray ray = _mainCamera.ScreenPointToRay(mouseScreen);

            // Raycast na płaszczyznę Y=0 (zajezdnia jest płaska w MVP)
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float dist))
            {
                _cursorWorldPos = ray.GetPoint(dist);
            }
        }

        private void HandleKeyboardInput()
        {
            if (Keyboard.current == null) return;

            // R = mirror flip (tylko generative, snapshot pomija)
            if (Keyboard.current.rKey.wasPressedThisFrame && _currentSchema != null && _currentSchema.IsGenerative)
            {
                _currentMirror = !_currentMirror;
                if (_currentSchema.parameters != null)
                    _currentSchema.parameters.mirror = _currentMirror;
                RegenerateGeometry();
                Log.Info($"[TurnoutSchemaPlacer] Mirror toggled: {_currentMirror}");
            }

            // Ctrl+Scroll = rotacja 5° step (koordynacja z Camera.Depot.Zoom przez Ctrl gate)
            if (Mouse.current != null && Keyboard.current.ctrlKey.isPressed)
            {
                Vector2 scroll = Mouse.current.scroll.ReadValue();
                if (Mathf.Abs(scroll.y) > 0.01f)
                {
                    float deltaRot = (scroll.y > 0 ? rotationStepDeg : -rotationStepDeg);
                    _currentRotationDeg = Mathf.Repeat(_currentRotationDeg + deltaRot, 360f);
                }
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

            // LMB = confirm — ALE tylko gdy kursor NIE jest nad UI (panel parametrów, save dialog, itp.)
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
