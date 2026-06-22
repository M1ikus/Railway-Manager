using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// M-TimetableUX F1.3 polish (visual waypoint markers): blue rings na mapie per
    /// explicit waypoint w kreator. Click w marker → remove waypoint flow.
    ///
    /// Drag-to-move (per F1.3 spec) deferred post-EA — wymaga IDragHandler + InputSystem
    /// continuous polling + snap-to-nearest-station integracja z RouteBuildStateMachine.
    /// Pre-EA: click-to-remove jako visual interaction; ↑/↓ reorder w panelu (commit 93ba863)
    /// + append z mapy (commit 57c3abc) cover other manipulation use cases.
    /// </summary>
    public class WaypointMarkersOverlay : MonoBehaviour
    {
        public static WaypointMarkersOverlay Instance { get; private set; }

        private const int CircleSegments = 24;
        private const float CircleRadiusM = 250f; // slightly larger niż ghost markers żeby się distinguish
        private const float LineWidthM = 30f;
        private static readonly Color MarkerColor = new Color(0.25f, 0.5f, 0.95f, 0.55f); // semi-transparent blue (różne od ghost yellow)

        /// <summary>F1.3 click polish: emitowany gdy gracz klika waypoint marker. Subscriber UI usuwa waypoint z listy.</summary>
        public static event System.Action<int> OnMarkerClicked; // int = waypoint index

        /// <summary>F1.3 drag polish: emitowany gdy gracz draguje waypoint marker do nowej stacji (snap-to-nearest). Subscriber UI replaces _waypoints[idx] = newStation.</summary>
        public static event System.Action<int, RailwayStation> OnMarkerDragged; // (idx, snappedStation)

        private readonly List<GameObject> _markers = new();
        private Material _sharedMaterial;

        // Drag state tracking (F1.3 drag polish)
        private int _dragMarkerIdx = -1;
        private Vector2 _dragStartScreenPos;
        private const float DragThresholdPx = 8f; // screen distance > threshold → drag mode (else click)
        private const float DragSnapRadiusM = 5000f; // snap waypoint do nearest station w 5km
        private bool _isDragging = false;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            Clear();
            if (Instance == this) Instance = null;
        }

        /// <summary>Pokaż markery dla listy waypointów (index = waypoint position w sekwencji).</summary>
        public void ShowWaypoints(IList<RailwayStation> waypoints)
        {
            Clear();
            if (waypoints == null || waypoints.Count == 0) return;

            if (_sharedMaterial == null)
            {
                _sharedMaterial = MaterialFactory.CreateLine();
            }

            for (int i = 0; i < waypoints.Count; i++)
            {
                var wp = waypoints[i];
                if (wp == null) continue;

                var go = new GameObject($"WaypointMarker_{i}_{wp.name}");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(wp.position.x, 0.6f, wp.position.y);

                var collider = go.AddComponent<SphereCollider>();
                collider.radius = CircleRadiusM;
                collider.isTrigger = true;

                var clickHandler = go.AddComponent<WaypointMarkerClickHandler>();
                clickHandler.waypointIndex = i;

                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = CircleSegments + 1;
                for (int s = 0; s <= CircleSegments; s++)
                {
                    float angle = (s / (float)CircleSegments) * Mathf.PI * 2f;
                    float x = wp.position.x + Mathf.Cos(angle) * CircleRadiusM;
                    float z = wp.position.y + Mathf.Sin(angle) * CircleRadiusM;
                    lr.SetPosition(s, new Vector3(x, 0.6f, z));
                }
                lr.startWidth = lr.endWidth = LineWidthM;
                lr.startColor = lr.endColor = MarkerColor;
                lr.useWorldSpace = true;
                if (_sharedMaterial != null) lr.material = _sharedMaterial;
                lr.numCornerVertices = 2;

                _markers.Add(go);
            }

            Log.Info($"[F1.3 markers] WaypointMarkersOverlay: showing {_markers.Count} markers");
        }

        public void Clear()
        {
            foreach (var m in _markers)
                if (m != null) Destroy(m);
            _markers.Clear();
        }

        public static WaypointMarkersOverlay EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("WaypointMarkersOverlay");
            DontDestroyOnLoad(go);
            return go.AddComponent<WaypointMarkersOverlay>();
        }

        void Update()
        {
            if (_markers.Count == 0) return;
            if (UnityEngine.InputSystem.Mouse.current == null) return;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            var camCtrl = FindAnyObjectByType<RailwayManager.CameraController>();
            if (camCtrl == null) return;
            var cam = camCtrl.GetComponent<Camera>();
            if (cam == null || !cam.enabled) return;

            Vector2 mousePos = mouse.position.ReadValue();

            // F1.3 drag polish: detect drag flow
            // 1. LMB down on marker → record drag candidate
            if (mouse.leftButton.wasPressedThisFrame)
            {
                var mapUI = MapSystem.MapUIManager.Instance;
                if (mapUI != null && mapUI.IsPointerOverUI()) return;

                Ray ray = cam.ScreenPointToRay(mousePos);
                if (Physics.Raycast(ray, out RaycastHit hit, 500000f, ~0, QueryTriggerInteraction.Collide))
                {
                    var clickHandler = hit.collider.GetComponent<WaypointMarkerClickHandler>();
                    if (clickHandler != null && clickHandler.waypointIndex >= 0)
                    {
                        _dragMarkerIdx = clickHandler.waypointIndex;
                        _dragStartScreenPos = mousePos;
                        _isDragging = false; // determine after threshold
                    }
                }
            }

            // 2. LMB held → check drag threshold
            if (_dragMarkerIdx >= 0 && mouse.leftButton.isPressed)
            {
                float screenDist = Vector2.Distance(mousePos, _dragStartScreenPos);
                if (!_isDragging && screenDist > DragThresholdPx)
                {
                    _isDragging = true;
                    Log.Info($"[F1.3 drag] Drag started dla marker idx={_dragMarkerIdx}");
                }

                if (_isDragging)
                {
                    // Update marker visual position w world space (smooth drag preview)
                    if (_dragMarkerIdx < _markers.Count && _markers[_dragMarkerIdx] != null)
                    {
                        Vector3 worldPos = ScreenToGroundPlane(cam, mousePos);
                        _markers[_dragMarkerIdx].transform.position = new Vector3(worldPos.x, 0.6f, worldPos.z);
                        // Update LineRenderer center (rebuild circle wokół new position)
                        var lr = _markers[_dragMarkerIdx].GetComponent<LineRenderer>();
                        if (lr != null)
                        {
                            for (int s = 0; s <= CircleSegments; s++)
                            {
                                float angle = (s / (float)CircleSegments) * Mathf.PI * 2f;
                                float x = worldPos.x + Mathf.Cos(angle) * CircleRadiusM;
                                float z = worldPos.z + Mathf.Sin(angle) * CircleRadiusM;
                                lr.SetPosition(s, new Vector3(x, 0.6f, z));
                            }
                        }
                    }
                }
            }

            // 3. LMB released → finalize drag (snap to nearest station) lub treat as click
            if (mouse.leftButton.wasReleasedThisFrame && _dragMarkerIdx >= 0)
            {
                if (_isDragging)
                {
                    // Drag complete — find nearest station do snap
                    Vector3 worldPos = ScreenToGroundPlane(cam, mousePos);
                    Vector2 mapPos = new Vector2(worldPos.x, worldPos.z);
                    var snapped = FindNearestStation(mapPos, DragSnapRadiusM);

                    if (snapped != null)
                    {
                        Log.Info($"[F1.3 drag] Drag complete: marker idx={_dragMarkerIdx} → snap '{snapped.name}'");
                        OnMarkerDragged?.Invoke(_dragMarkerIdx, snapped);
                    }
                    else
                    {
                        Log.Info($"[F1.3 drag] Drag cancelled: no station w radius {DragSnapRadiusM}m");
                        // Revert visual position (caller refresh restores)
                        ShowWaypointsFromTimetableCreator();
                    }
                }
                else
                {
                    // Click (no drag) → existing remove flow
                    Log.Info($"[F1.3 click] Waypoint marker clicked: index={_dragMarkerIdx}");
                    OnMarkerClicked?.Invoke(_dragMarkerIdx);
                }
                _dragMarkerIdx = -1;
                _isDragging = false;
            }
        }

        /// <summary>Helper: screen point → ground plane (y=0) world position.</summary>
        private static Vector3 ScreenToGroundPlane(Camera cam, Vector2 screenPos)
        {
            Ray ray = cam.ScreenPointToRay(screenPos);
            var groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (groundPlane.Raycast(ray, out float enter))
                return ray.GetPoint(enter);
            return Vector3.zero;
        }

        /// <summary>Helper: find nearest station w map position w radius (meters).</summary>
        private static RailwayStation FindNearestStation(Vector2 mapPos, float radiusM)
        {
            var init = TimetableInitializer.Instance;
            if (init?.Stations == null) return null;

            float bestDistSq = radiusM * radiusM;
            RailwayStation best = null;
            foreach (var st in init.Stations)
            {
                if (st.pathNodeId < 0) continue;
                float distSq = (st.position - mapPos).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = st;
                }
            }
            return best;
        }

        /// <summary>Helper: revert visual gdy drag cancelled — re-render z existing _waypoints.</summary>
        private void ShowWaypointsFromTimetableCreator()
        {
            // Trigger refresh — TimetableCreatorUI will call ShowWaypoints na Refresh()
            // Simpler: hide markers (creator BuildRoute regeneruje na next compute)
            Clear();
        }
    }

    /// <summary>Marker click identifier z waypoint index dla F1.3 click-to-remove flow.</summary>
    public class WaypointMarkerClickHandler : MonoBehaviour
    {
        public int waypointIndex = -1;
    }
}
