using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// M-TimetableUX F1.6 polish: visual ghost markers dla off-path stations near route.
    /// Singleton MonoBehaviour rysujący semi-transparent kolizjowe ringi per station — gracz
    /// widzi że stacja jest blisko trasy ale topologically off-path (suggestion: dodaj jako
    /// explicit waypoint przez F1.3 multi-stage).
    ///
    /// Click handler dla markers (add-as-waypoint flow) deferred post-EA — wymaga collider +
    /// raycast handler integration z RouteBuildStateMachine.
    /// </summary>
    public class GhostStationMarkersOverlay : MonoBehaviour
    {
        public static GhostStationMarkersOverlay Instance { get; private set; }

        private const int CircleSegments = 24;
        private const float CircleRadiusM = 200f;
        private const float LineWidthM = 25f;
        private static readonly Color MarkerColor = new Color(1f, 0.85f, 0.2f, 0.45f); // semi-transparent yellow

        /// <summary>
        /// M-TimetableUX F1.6 click polish: emitowany gdy gracz klika ghost marker.
        /// Subscribuje TimetableCreatorUI (lub orchestrator) → append station jako waypoint.
        /// </summary>
        public static event System.Action<RailwayStation> OnMarkerClicked;

        private readonly List<GameObject> _markers = new();
        private Material _sharedMaterial;

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

        /// <summary>
        /// Pokaż markery dla podanych stacji. Clear() existing markers + rebuild.
        /// </summary>
        public void ShowMarkers(IList<RailwayStation> offPathStations)
        {
            Clear();
            if (offPathStations == null || offPathStations.Count == 0) return;

            if (_sharedMaterial == null)
            {
                _sharedMaterial = MaterialFactory.CreateLine();
            }

            foreach (var st in offPathStations)
            {
                var go = new GameObject($"GhostMarker_{st.name}");
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(st.position.x, 0.5f, st.position.y);

                // F1.6 click polish: SphereCollider dla raycast click detection
                var collider = go.AddComponent<SphereCollider>();
                collider.radius = CircleRadiusM;
                collider.isTrigger = true;

                // Capture station per marker dla click callback
                var clickHandler = go.AddComponent<GhostMarkerClickHandler>();
                clickHandler.station = st;

                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = CircleSegments + 1;
                for (int i = 0; i <= CircleSegments; i++)
                {
                    float angle = (i / (float)CircleSegments) * Mathf.PI * 2f;
                    float x = st.position.x + Mathf.Cos(angle) * CircleRadiusM;
                    float z = st.position.y + Mathf.Sin(angle) * CircleRadiusM;
                    lr.SetPosition(i, new Vector3(x, 0.5f, z));
                }
                lr.startWidth = lr.endWidth = LineWidthM;
                lr.startColor = lr.endColor = MarkerColor;
                lr.useWorldSpace = true;
                if (_sharedMaterial != null) lr.material = _sharedMaterial;
                lr.numCornerVertices = 2;
                lr.numCapVertices = 0;

                _markers.Add(go);
            }

            Log.Info($"[F1.6 polish] GhostStationMarkersOverlay: showing {_markers.Count} markers");
        }

        /// <summary>Wyczyść markery (np. po Cancel kreator lub nowa route).</summary>
        public void Clear()
        {
            foreach (var m in _markers)
                if (m != null) Destroy(m);
            _markers.Clear();
        }

        /// <summary>
        /// Lazy auto-init: jeśli brak Instance, tworzy GameObject. Wywoływać przed ShowMarkers
        /// gdy nie wiemy czy singleton istnieje.
        /// </summary>
        public static GhostStationMarkersOverlay EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("GhostStationMarkersOverlay");
            DontDestroyOnLoad(go);
            return go.AddComponent<GhostStationMarkersOverlay>();
        }

        void Update()
        {
            // F1.6 click detection — gdy marker active i mouse left click
            if (_markers.Count == 0) return;
            if (UnityEngine.InputSystem.Mouse.current == null) return;
            if (!UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame) return;

            // Block jeśli kursor nad UI
            var mapUI = MapSystem.MapUIManager.Instance;
            if (mapUI != null && mapUI.IsPointerOverUI()) return;

            // Find map camera
            var camCtrl = FindAnyObjectByType<RailwayManager.CameraController>();
            if (camCtrl == null) return;
            var cam = camCtrl.GetComponent<Camera>();
            if (cam == null || !cam.enabled) return;

            UnityEngine.Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            UnityEngine.Ray ray = cam.ScreenPointToRay(mousePos);

            // Raycast against ghost marker colliders only (TriggerInteraction=Collide żeby trigger'y się łapały)
            if (UnityEngine.Physics.Raycast(ray, out UnityEngine.RaycastHit hit, 500000f, ~0, UnityEngine.QueryTriggerInteraction.Collide))
            {
                var clickHandler = hit.collider.GetComponent<GhostMarkerClickHandler>();
                if (clickHandler != null && clickHandler.station != null)
                {
                    Log.Info($"[F1.6 click] Ghost marker clicked: {clickHandler.station.name}");
                    OnMarkerClicked?.Invoke(clickHandler.station);
                }
            }
        }
    }

    /// <summary>
    /// Marker click identifier — przypisuje station do collider GameObject.
    /// </summary>
    public class GhostMarkerClickHandler : MonoBehaviour
    {
        public RailwayStation station;
    }
}
