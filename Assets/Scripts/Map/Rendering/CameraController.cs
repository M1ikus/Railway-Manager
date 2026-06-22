using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;

namespace RailwayManager
{
    /// <summary>
    /// Camera controller for 2D orthographic map view.
    /// Uses new Input System (RailwayManager.Core.InputActions, map Camera.Map).
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class CameraController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [Tooltip("Camera movement speed (units per second)")]
        public float panSpeed = 500f;

        [Tooltip("Speed multiplier when holding Shift")]
        public float fastSpeedMultiplier = 2f;

        [Tooltip("Speed multiplier when holding Ctrl")]
        public float slowSpeedMultiplier = 0.25f;

        [Header("Zoom Settings")]
        [Tooltip("Zoom speed (scroll sensitivity)")]
        public float zoomSpeed = 100f;

        [Tooltip("Minimum zoom level (smaller = more zoomed in)")]
        public float minZoom = 50f;

        [Tooltip("Maximum zoom level (larger = more zoomed out)")]
        public float maxZoom = 50000f;

        [Tooltip("Smooth zoom interpolation speed")]
        [Range(1f, 20f)]
        public float zoomSmoothness = 10f;

        [Header("Mouse Pan Settings")]
        [Tooltip("Enable panning with middle mouse button")]
        public bool enableMiddleMousePan = true;

        [Tooltip("Mouse pan sensitivity")]
        public float mousePanSensitivity = 1f;

        [Header("Rotation Settings")]
        [Tooltip("Enable camera rotation with Q/E keys")]
        public bool enableRotation = true;

        [Tooltip("Rotation speed (degrees per second)")]
        public float rotationSpeed = 45f;

        [Header("Bounds (Optional)")]
        [Tooltip("Limit camera movement to map bounds")]
        public bool useBounds = false;

        public float minX = -10000f;
        public float maxX = 10000f;
        public float minZ = -10000f;
        public float maxZ = 10000f;

        [Header("Debug")]
        public bool showDebugInfo = false;
        public bool showMovementVectors = false;

        // Private variables
        private UnityEngine.Camera cam;
        private float targetZoom;
        private bool isDragging = false;

        // ── Input System ──
        private InputActions _inputActions;
        private InputActions.CameraMapActions _mapCamera;

        void Awake()
        {
            cam = GetComponent<UnityEngine.Camera>();

            if (!cam.orthographic)
            {
                Log.Warn("[CameraController] Camera is not orthographic! Converting to orthographic mode.");
                cam.orthographic = true;
            }

            targetZoom = cam.orthographicSize;

            _inputActions = new InputActions();
            RailwayManager.Core.Settings.RebindingService.ApplyOverridesTo(_inputActions);
            _mapCamera = _inputActions.CameraMap;
        }

        void OnEnable()
        {
            _mapCamera.Enable();
        }

        void OnDisable()
        {
            _mapCamera.Disable();
        }

        void OnDestroy()
        {
            _inputActions?.Dispose();
        }

        void Update()
        {
            // Don't process input when camera is disabled (Depot is active or scene not ready).
            // Intentionally NOT checking SceneController.ActiveScene — standalone MapScene load
            // (when Depot isn't in the session) still needs input to work. The physical cam.enabled
            // state is driven by SceneController.SwitchToMap/SwitchToDepot and is authoritative.
            if (!cam.enabled)
                return;

            // Block input when fullscreen overlay is open (DepotLocationPicker etc.)
            if (RailwayManager.Core.SceneController.FullscreenOverlayOpen)
                return;

            HandleKeyboardMovement();
            HandleMousePan();
            HandleZoom();

            if (enableRotation)
                HandleRotation();

            if (showDebugInfo && Time.frameCount % 60 == 0)
                DisplayDebugInfo();
        }

        /// <summary>
        /// Handles WASD / Arrow keys camera movement (keyboard only, ignores gamepad sticks).
        /// </summary>
        private void HandleKeyboardMovement()
        {
            // Composite Vector2 binding: WSAD + Arrows -> Pan
            Vector2 pan = _mapCamera.Pan.ReadValue<Vector2>();
            float horizontal = pan.x;
            float vertical = pan.y;

            if (Mathf.Abs(horizontal) < 0.01f && Mathf.Abs(vertical) < 0.01f)
                return;

            // Speed modifier: Shift = fast, Ctrl = slow
            float speedMultiplier = 1f;
            if (_mapCamera.SpeedModifier.IsPressed())
                speedMultiplier = fastSpeedMultiplier;
            else if (_mapCamera.SlowModifier.IsPressed())
                speedMultiplier = slowSpeedMultiplier;

            // Speed scales with zoom level
            float zoomScaledSpeed = panSpeed * (cam.orthographicSize / 1000f);
            float actualSpeed = zoomScaledSpeed * speedMultiplier * Time.deltaTime;

            // Calculate movement vectors in world space
            Vector3 camForward = transform.forward;
            Vector3 camRight = transform.right;

            // Project onto XZ plane
            Vector3 forward = new Vector3(camForward.x, 0f, camForward.z);
            Vector3 right = new Vector3(camRight.x, 0f, camRight.z);

            // Fallback if camera looks straight down
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
                right = Vector3.right;
            }
            else
            {
                forward.Normalize();
                right.Normalize();
            }

            // Debug visualization
            if (showMovementVectors)
            {
                Debug.DrawRay(transform.position, forward * 1000f, Color.blue, 0.1f);
                Debug.DrawRay(transform.position, right * 1000f, Color.red, 0.1f);
                Log.Info($"[CameraController] Input: H={horizontal:F2}, V={vertical:F2}");
            }

            // Calculate final movement
            Vector3 movement = (right * horizontal + forward * vertical) * actualSpeed;

            // Apply movement
            Vector3 newPosition = transform.position + movement;

            // Apply bounds if enabled
            if (useBounds)
            {
                newPosition.x = Mathf.Clamp(newPosition.x, minX, maxX);
                newPosition.z = Mathf.Clamp(newPosition.z, minZ, maxZ);
            }

            transform.position = newPosition;
        }

        // Scroll input scale — tuned empirically żeby pasować do DepotOrbitCamera (jeden tick scroll
        // wheel daje takie samo wrażenie zoom w 3D depocie i 2D mapie).
        private const float ScrollSensitivity = 0.1f;

        // Minimalny scroll delta poniżej którego nie aktualizujemy zoom (dead-zone na trackpad noise).
        private const float ScrollDeadZone = 0.01f;

        // Proportional zoom — jeden scroll tick = ~15% zmiany orthographic size.
        // Wartość 1.5f żeby przy ScrollSensitivity=0.1 i delta=1 multiplier wyszedł 1 - 0.15 = 0.85.
        private const float ZoomTickFactor = 1.5f;

        /// <summary>
        /// Handles mouse scroll wheel zooming.
        /// </summary>
        private void HandleZoom()
        {
            Vector2 scrollVec = _mapCamera.Zoom.ReadValue<Vector2>();
            float scrollDelta = scrollVec.y * ScrollSensitivity;

            if (Mathf.Abs(scrollDelta) > ScrollDeadZone)
            {
                float zoomMultiplier = 1f - scrollDelta * ZoomTickFactor;
                targetZoom *= zoomMultiplier;
                targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);
            }

            // Smooth zoom interpolation
            cam.orthographicSize = Mathf.Lerp(
                cam.orthographicSize,
                targetZoom,
                Time.deltaTime * zoomSmoothness
            );
        }

        /// <summary>
        /// Handles middle mouse button panning.
        /// </summary>
        private void HandleMousePan()
        {
            if (!enableMiddleMousePan)
                return;

            // Start dragging
            if (_mapCamera.PanHeld.WasPressedThisFrame())
            {
                isDragging = true;
            }

            // Stop dragging
            if (_mapCamera.PanHeld.WasReleasedThisFrame())
            {
                isDragging = false;
            }

            // Drag
            if (isDragging)
            {
                Vector2 delta = _mapCamera.MouseDelta.ReadValue<Vector2>();
                if (delta.sqrMagnitude < 0.0001f) return;

                float worldDeltaX = -delta.x * mousePanSensitivity * cam.orthographicSize * cam.aspect / Screen.height;
                float worldDeltaZ = -delta.y * mousePanSensitivity * cam.orthographicSize / Screen.height;

                Vector3 camForward = transform.forward;
                Vector3 camRight = transform.right;

                Vector3 forward = new Vector3(camForward.x, 0f, camForward.z);
                Vector3 right = new Vector3(camRight.x, 0f, camRight.z);

                if (forward.sqrMagnitude < 0.001f)
                {
                    forward = Vector3.forward;
                    right = Vector3.right;
                }
                else
                {
                    forward.Normalize();
                    right.Normalize();
                }

                Vector3 movement = right * worldDeltaX + forward * worldDeltaZ;
                Vector3 newPosition = transform.position + movement;

                if (useBounds)
                {
                    newPosition.x = Mathf.Clamp(newPosition.x, minX, maxX);
                    newPosition.z = Mathf.Clamp(newPosition.z, minZ, maxZ);
                }

                transform.position = newPosition;
            }
        }

        /// <summary>
        /// Handles Q/E rotation around Y axis.
        /// Q = positive (counter-clockwise), E = negative (clockwise).
        /// </summary>
        private void HandleRotation()
        {
            // Rotate action: Q = +1, E = -1 (1DAxis composite)
            float rotInput = _mapCamera.Rotate.ReadValue<float>();

            if (Mathf.Abs(rotInput) > 0.01f)
            {
                float rotation = rotInput * rotationSpeed * Time.deltaTime;
                transform.Rotate(Vector3.up, rotation, Space.World);
            }
        }

        /// <summary>
        /// Sets camera bounds based on map bounds.
        /// </summary>
        public void SetBounds(float minX, float maxX, float minZ, float maxZ)
        {
            this.minX = minX;
            this.maxX = maxX;
            this.minZ = minZ;
            this.maxZ = maxZ;
            this.useBounds = true;

            if (showDebugInfo)
                Log.Info($"[CameraController] Bounds set: X[{minX:F0}, {maxX:F0}] Z[{minZ:F0}, {maxZ:F0}]");
        }

        /// <summary>
        /// Focuses camera on a specific world position.
        /// </summary>
        public void FocusOnPosition(Vector3 worldPosition, float zoomLevel = -1f)
        {
            Vector3 newPos = transform.position;
            newPos.x = worldPosition.x;
            newPos.z = worldPosition.z;
            transform.position = newPos;

            if (zoomLevel > 0)
            {
                targetZoom = zoomLevel;
                cam.orthographicSize = zoomLevel;
            }

            if (showDebugInfo)
                Log.Info($"[CameraController] Focused on position: {worldPosition}");
        }

        /// <summary>
        /// Displays debug information.
        /// </summary>
        private void DisplayDebugInfo()
        {
            // Dead code usuniety przy migracji Input System:
            //   Input.GetAxis("Horizontal") / ("Vertical") byly duplikatem keyboard input
            //   z HandleKeyboardMovement. Komentarz w kodzie mowil "drifuja przez gamepad"
            //   ale mimo to byly wywolywane. Teraz czysto.

            Log.Info($"[Camera] Pos: {transform.position}, Zoom: {cam.orthographicSize:F0}");
        }

        /// <summary>
        /// Public method to set zoom level.
        /// </summary>
        public void SetZoom(float zoom)
        {
            targetZoom = Mathf.Clamp(zoom, minZoom, maxZoom);
        }

        /// <summary>
        /// Public method to get current zoom level.
        /// </summary>
        public float GetZoom()
        {
            return cam.orthographicSize;
        }

        /// <summary>
        /// Resets camera to initial position and zoom.
        /// </summary>
        [ContextMenu("Reset Camera")]
        public void ResetCamera()
        {
            var mapSetup = GetComponent<MapSetup>();
            if (mapSetup != null)
            {
                mapSetup.ReconfigureCamera();
            }
            else
            {
                Log.Warn("[CameraController] No MapSetup component found to reset camera");
            }
        }
    }
}
