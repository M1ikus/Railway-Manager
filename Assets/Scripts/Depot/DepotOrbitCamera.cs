using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;

namespace DepotSystem
{
    public class DepotOrbitCamera : MonoBehaviour
    {
        // CBM-style camera (wariant A z depot-visual-direction.md, 2026-05-17).
        // - Pitch auto-coupled do zoom (im bliżej tym bardziej boczny, im dalej tym top-down).
        // - Yaw (rotacja wokół pionowej osi) kontrolowana przez gracza: MMB drag + Q/E keys.
        //   Decyzja z playtest 2026-05-17 (sesja 5): orbit yaw przywrócony, pitch nadal auto.
        // - _cameraActions.Pitch input binding (T/F keys) zostaje w InputActions.inputactions
        //   ale nieaktywne (pitch jest auto, gracz nie kontroluje).
        [Header("Orbit (tylko yaw, pitch jest auto)")]
        [SerializeField] private float orbitSpeed = 50f;     // było 100 — playtest 2026-05-17 user: za czułe

        [Header("Auto-Pitch coupled to Zoom (CBM-style)")]
        [SerializeField] private float minPitch = 40f;   // przy min zoom (blisko) — mocno boczny widok
        [SerializeField] private float maxPitch = 70f;   // przy max zoom (daleko) — elevated 3/4

        [Header("Zoom (skala 2000x400m depot — gracz widzi fragment, reszta przez minimapę)")]
        [SerializeField] private float minZoom = 5f;
        [SerializeField] private float maxZoom = 100f;
        [SerializeField] private float zoomSpeed = 20f;
        [SerializeField] private float zoomSmoothTime = 0.15f;

        [Header("Pan")]
        [SerializeField] private float panSpeed = 0.25f;     // było 0.5 — playtest 2026-05-17 user: za czułe
        [SerializeField] private float keyPanSpeed = 200f;   // było 400 — playtest 2026-05-17 user: za czułe
        [SerializeField] private float panSmoothTime = 0.1f;

        [Header("Bounds")]
        [SerializeField] private float boundsMargin = 0f;

        // Current state
        private Vector3 pivotPoint;
        private float currentYaw = 180f;
        private float currentPitch = 55f;        // środek auto-pitch range (40-70); faktyczna wartość będzie liczona z zoom
        private float currentDistance = 100f;

        // Target state (for smooth damp)
        private Vector3 targetPivot;
        private float targetYaw;
        private float targetPitch;
        private float targetDistance;

        // Smooth damp velocities
        private Vector3 pivotVelocity;
        private float yawVelocity;
        private float pitchVelocity;
        private float distanceVelocity;

        private Bounds cameraBounds;
        private bool hasBounds;

        // ── Input System ──
        private InputActions _inputActions;
        private InputActions.CameraDepotActions _cameraActions;

        void Awake()
        {
            _inputActions = new InputActions();
            RailwayManager.Core.Settings.RebindingService.ApplyOverridesTo(_inputActions);
            _cameraActions = _inputActions.CameraDepot;
        }

        void OnEnable()
        {
            _cameraActions.Enable();
        }

        void OnDisable()
        {
            _cameraActions.Disable();
        }

        void OnDestroy()
        {
            _inputActions?.Dispose();
        }

        void Start()
        {
            pivotPoint = transform.position + transform.forward * currentDistance;
            pivotPoint.y = 0f;
            targetPivot = pivotPoint;
            targetYaw = currentYaw;
            targetPitch = currentPitch;
            targetDistance = currentDistance;

            UpdateCameraTransform();
        }

        void LateUpdate()
        {
            // Don't process input when camera is disabled (other scene is active)
            var cam = GetComponent<Camera>();
            if (cam != null && !cam.enabled) return;

            // Block all camera input when fullscreen overlay is open (Fleet panel, Timetable creator,
            // DepotLocationPicker etc — czyli cokolwiek co blokuje gameplay)
            bool fullscreenOverlay = (DepotUIManager.Instance != null
                && DepotUIManager.Instance.fleetPanel != null
                && DepotUIManager.Instance.fleetPanel.IsVisible)
                || RailwayManager.Core.SceneController.TimetablePopupOpen
                || RailwayManager.Core.SceneController.FullscreenOverlayOpen;
            if (fullscreenOverlay) return;

            bool overUI = DepotUIManager.Instance != null && DepotUIManager.Instance.IsPointerOverUI();

            if (!overUI)
            {
                HandleOrbit();              // MMB drag: yaw only
                HandlePan();
                HandleZoom();
            }

            HandleKeyboardPan();
            HandleKeyboardOrbit();          // Q/E keys: yaw only
            UpdateAutoPitch();              // CBM-style: pitch derived from zoom
            ApplySmoothDamp();
            ClampPivotToBounds();
            UpdateCameraTransform();
            ClampCameraPosition();
        }

        private void HandleOrbit()
        {
            // Orbit: MMB (middleButton) drag — TYLKO yaw, pitch jest auto z zoom.
            if (!_cameraActions.OrbitHeld.IsPressed()) return;

            Vector2 delta = _cameraActions.MouseDelta.ReadValue<Vector2>();
            if (Mathf.Abs(delta.x) < 0.0001f) return;

            targetYaw += delta.x * orbitSpeed * 0.01f;
            // pitch.y component ignorowane — pitch jest auto-coupled do zoom (UpdateAutoPitch).
        }

        private void HandlePan()
        {
            // Pan: RMB drag
            if (!_cameraActions.PanHeld.IsPressed()) return;

            Vector2 delta = _cameraActions.MouseDelta.ReadValue<Vector2>();
            if (delta.sqrMagnitude < 0.0001f) return;

            Vector3 right = transform.right;
            Vector3 forward = Vector3.Cross(right, Vector3.up).normalized;

            float speedFactor = currentDistance * panSpeed * Time.deltaTime;
            targetPivot -= right * delta.x * speedFactor;
            targetPivot -= forward * delta.y * speedFactor;
            targetPivot.y = 0f;
        }

        private void HandleKeyboardPan()
        {
            // WSAD composite -> Vector2
            Vector2 pan = _cameraActions.Pan.ReadValue<Vector2>();
            if (pan.sqrMagnitude < 0.0001f) return;

            Vector3 right = transform.right;
            Vector3 forward = Vector3.Cross(right, Vector3.up).normalized;

            // Pan scaling by zoom (CBM-style): przy max zoom (400m) pan szybki,
            // przy min zoom (15m) pan wolny dla precyzji. Zakres ~0.15× do 4× bazowej prędkości.
            float zoomFactor = Mathf.Clamp(currentDistance / 100f, 0.15f, 4f);
            float speed = keyPanSpeed * zoomFactor * Time.deltaTime;
            targetPivot += (right * pan.x + forward * pan.y) * speed;
            targetPivot.y = 0f;
        }

        private void HandleKeyboardOrbit()
        {
            // Q/E -> Yaw axis (Pitch axis T/F ignorowane — pitch jest auto).
            float yawInput = _cameraActions.Yaw.ReadValue<float>();
            if (Mathf.Abs(yawInput) < 0.01f) return;

            float speed = orbitSpeed * Time.deltaTime;
            targetYaw += yawInput * speed;
        }

        private void HandleZoom()
        {
            // Zoom: scroll wheel. Block when Ctrl held (Ctrl+Scroll reserved for turnout rotation).
            if (Keyboard.current != null && Keyboard.current.ctrlKey.isPressed) return;

            // Scroll delta is Vector2 (y = vertical scroll).
            // Input System returns raw wheel delta - scale to match old Input.GetAxis feel.
            // Tuned empirically: 0.1 feels close to old behavior on typical mice.
            Vector2 scrollDelta = _cameraActions.Zoom.ReadValue<Vector2>();
            float scroll = scrollDelta.y * 0.1f;

            if (Mathf.Abs(scroll) < 0.001f) return;

            targetDistance -= scroll * zoomSpeed * currentDistance * 0.1f;
            targetDistance = Mathf.Clamp(targetDistance, minZoom, maxZoom);
        }

        /// <summary>
        /// CBM-style auto-pitch: pitch jest derived from current zoom distance.
        /// - Przy min zoom (5m, blisko) → minPitch (40°) — mocno boczny widok, widać ściany.
        /// - Przy max zoom (100m, daleko) → maxPitch (70°) — elevated 3/4, widać dachy.
        /// Zakres 30° daje wyraźną różnicę w playtest.
        /// Lerp jest na targetDistance (nie currentDistance), żeby pitch wyprzedzał smooth damp zoomu
        /// i kamera nie czuła się "opóźniona" przy zmianie zoom.
        /// </summary>
        private void UpdateAutoPitch()
        {
            float zoomT = Mathf.InverseLerp(minZoom, maxZoom, targetDistance);
            targetPitch = Mathf.Lerp(minPitch, maxPitch, zoomT);
        }

        private void ApplySmoothDamp()
        {
            currentYaw = Mathf.SmoothDamp(currentYaw, targetYaw, ref yawVelocity, panSmoothTime);
            currentPitch = Mathf.SmoothDamp(currentPitch, targetPitch, ref pitchVelocity, panSmoothTime);
            currentDistance = Mathf.SmoothDamp(currentDistance, targetDistance, ref distanceVelocity, zoomSmoothTime);
            pivotPoint = Vector3.SmoothDamp(pivotPoint, targetPivot, ref pivotVelocity, panSmoothTime);
        }

        private void ClampPivotToBounds()
        {
            if (!hasBounds) return;

            float margin = boundsMargin;
            Vector3 min = cameraBounds.min - new Vector3(margin, 0, margin);
            Vector3 max = cameraBounds.max + new Vector3(margin, 0, margin);

            pivotPoint.x = Mathf.Clamp(pivotPoint.x, min.x, max.x);
            pivotPoint.z = Mathf.Clamp(pivotPoint.z, min.z, max.z);

            targetPivot.x = Mathf.Clamp(targetPivot.x, min.x, max.x);
            targetPivot.z = Mathf.Clamp(targetPivot.z, min.z, max.z);
        }

        private void UpdateCameraTransform()
        {
            float pitchRad = currentPitch * Mathf.Deg2Rad;
            float yawRad = currentYaw * Mathf.Deg2Rad;

            Vector3 offset = new Vector3(
                Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
                Mathf.Sin(pitchRad),
                Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)
            ) * currentDistance;

            transform.position = pivotPoint + offset;
            transform.LookAt(pivotPoint);
        }

        /// <summary>
        /// Po UpdateCameraTransform — jeśli kamera (nie pivot) wyleciała poza bounds,
        /// przesuń pivot tak żeby kamera wróciła.
        /// </summary>
        private void ClampCameraPosition()
        {
            if (!hasBounds) return;

            Vector3 min = cameraBounds.min;
            Vector3 max = cameraBounds.max;

            Vector3 camPos = transform.position;
            Vector3 clamped = camPos;
            clamped.x = Mathf.Clamp(clamped.x, min.x, max.x);
            clamped.z = Mathf.Clamp(clamped.z, min.z, max.z);

            Vector3 delta = clamped - camPos;
            if (delta.sqrMagnitude > 0.01f)
            {
                pivotPoint += delta;
                targetPivot += delta;
                transform.position = clamped;
                transform.LookAt(pivotPoint);
            }
        }

        public void UpdateBounds(Bounds newBounds)
        {
            cameraBounds = newBounds;
            hasBounds = true;
            // Natychmiast clampuj pivot do nowych bounds
            ClampPivotToBounds();
            Log.Info($"[DepotOrbitCamera] Bounds updated: min={newBounds.min}, max={newBounds.max}, margin={boundsMargin}");
        }

        public void FocusOn(Vector3 worldPosition, bool instant = false)
        {
            targetPivot = new Vector3(worldPosition.x, 0f, worldPosition.z);
            if (instant)
            {
                pivotPoint = targetPivot;
                UpdateCameraTransform();
            }
        }

        public void FocusOnObject(GameObject target, float distance = -1f)
        {
            if (target == null) return;
            FocusOn(target.transform.position);
            if (distance > 0f)
                targetDistance = Mathf.Clamp(distance, minZoom, maxZoom);
        }
    }
}
