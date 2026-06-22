using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    /// <summary>
    /// Tryb elektryfikacji — gracz "maluje" tory myszką aby je zelektryfikować.
    /// Kliknięcie lub przeciągnięcie dodaje wszystkie tory w zasięgu kursora.
    /// Można zaznaczać wiele torów na raz (4, 5, 6 równoległych).
    /// Delete: de-elektryfikuje tor pod kursorem.
    /// </summary>
    public class ElectrificationStateMachine : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CatenaryGenerator catenaryGenerator;
        [SerializeField] private TrackGraph trackGraph;

        [Header("Settings")]
        [Tooltip("Promień pędzla — tory w tej odległości od kursora zostają zaznaczone")]
        public float brushRadius = 15f;

        [Tooltip("Promień do zaznaczania pojedynczego toru (hover/delete)")]
        public float singleTrackRadius = 5f;

        public Color previewColor = new Color(1f, 0.9f, 0.3f, 0.8f);
        public Color deelectrifyColor = new Color(1f, 0.3f, 0.3f, 0.8f);
        public Color electrifiedColor = new Color(0.3f, 1f, 0.5f, 0.6f);
        public float previewWidth = 0.2f;
        public float previewHeight = 6f;

        // Stan
        private ElectrificationState state = ElectrificationState.Idle;

        // Drag — zbiór zaznaczonych torów (akumulowany podczas przeciągania)
        private HashSet<int> selectedTracks = new();
        private int hoveredTrackId = -1;

        // Preview
        private List<GameObject> highlightObjects = new();

        // Cache
        private Camera mainCamera;

        // ── Input System ──
        private InputActions _inputActions;
        private InputActions.ToolBuildActions _toolBuild;

        // ── Tool-mode gate ──
        private ToolModeGate _toolGate;

        void Awake()
        {
            _inputActions = new InputActions();
            RailwayManager.Core.Settings.RebindingService.ApplyOverridesTo(_inputActions);
            _toolBuild = _inputActions.ToolBuild;
        }

        void OnEnable()
        {
            _toolBuild.Enable();
        }

        void OnDisable()
        {
            _toolBuild.Disable();
        }

        void Start()
        {
            mainCamera = Camera.main;
            if (catenaryGenerator == null)
                catenaryGenerator = DepotServices.Get<CatenaryGenerator>();
            if (trackGraph == null)
                trackGraph = DepotServices.Get<TrackGraph>();

            _toolGate = new ToolModeGate(this, m => m == ToolMode.BuildCatenary, OnToolDeactivated);
            _toolGate.Start();
        }

        void OnDestroy()
        {
            _toolGate?.Stop();
            ClearAllHighlights();
            _inputActions?.Dispose();
        }

        private void OnToolDeactivated()
        {
            if (state != ElectrificationState.Idle)
                CancelDrag();
            ClearAllHighlights();
        }

        void Update()
        {
            if (DepotUIManager.Instance.IsPointerOverUI()) return;

            switch (state)
            {
                case ElectrificationState.Idle:
                    HandleIdle();
                    break;
                case ElectrificationState.Dragging:
                    HandleDragging();
                    break;
            }
        }

        // ═══════════════════════════════════════════
        //  STANY
        // ═══════════════════════════════════════════

        private bool IsRemoveMode =>
            DepotUIManager.Instance != null
            && DepotUIManager.Instance.CurrentCatenarySubMode == CatenaryBuildSubMode.RemoveCatenary;

        private void HandleIdle()
        {
            Vector3 mouseWorld = GetMouseWorldPosition();
            if (mouseWorld == Vector3.zero) return;

            // Escape / RMB → powrót do Select (Cancel action = ESC or RMB)
            if (_toolBuild.Cancel.WasPressedThisFrame())
            {
                ClearAllHighlights();
                return;
            }

            // Delete → de-elektryfikuj (niezależnie od sub-mode)
            if (_toolBuild.Delete.WasPressedThisFrame())
            {
                TryDeelectrifyNearest(mouseWorld);
                return;
            }

            bool removing = IsRemoveMode;

            // Podświetl tory w zasięgu pędzla
            var nearbyTracks = FindTracksInRadius(mouseWorld, brushRadius);
            UpdateHoverHighlights(nearbyTracks, removing);

            // LMB → rozpocznij malowanie/zdejmowanie
            if (_toolBuild.Primary.WasPressedThisFrame())
            {
                selectedTracks.Clear();

                foreach (var t in nearbyTracks)
                    selectedTracks.Add(t);

                state = ElectrificationState.Dragging;
                UpdateDragPreview();
            }
        }

        private void HandleDragging()
        {
            Vector3 mouseWorld = GetMouseWorldPosition();
            if (mouseWorld == Vector3.zero) return;

            // Escape / RMB → anuluj (Cancel action)
            if (_toolBuild.Cancel.WasPressedThisFrame())
            {
                CancelDrag();
                return;
            }

            // Podczas przeciągania: dodawaj tory w zasięgu pędzla (akumulacja)
            var nearbyTracks = FindTracksInRadius(mouseWorld, brushRadius);
            foreach (var t in nearbyTracks)
                selectedTracks.Add(t);

            UpdateDragPreview();

            // LMB up → zatwierdź
            if (_toolBuild.Primary.WasReleasedThisFrame())
            {
                if (IsRemoveMode)
                    CommitDeelectrification(selectedTracks.ToList());
                else
                    CommitElectrification(selectedTracks.ToList());
                CancelDrag();
            }
        }

        // ═══════════════════════════════════════════
        //  ELEKTRYFIKACJA
        // ═══════════════════════════════════════════

        private void CommitElectrification(List<int> trackIds)
        {
            if (trackIds == null || trackIds.Count == 0) return;
            if (trackGraph == null || catenaryGenerator == null) return;

            // Zapisz tylko tory które NAPRAWDĘ były niezelektryfikowane (do undo)
            var actuallyChanged = new List<int>();
            foreach (int id in trackIds)
            {
                var t = trackGraph.GetTrack(id);
                if (t != null && !t.HasCatenary)
                    actuallyChanged.Add(id);
            }

            foreach (int id in trackIds)
                trackGraph.SetTrackCatenary(id, true);

            catenaryGenerator.GenerateNetwork();

            Log.Info($"[Electrification] Electrified {trackIds.Count} tracks: " +
                      $"[{string.Join(", ", trackIds)}]");

            if (actuallyChanged.Count > 0)
            {
                DepotSystem.Undo.UndoManager.Record(
                    DepotSystem.Undo.UndoCategory.SiecTrakcyjna,
                    new DepotSystem.Undo.CatenaryToggleCommand(actuallyChanged, false));
            }
        }

        private void CommitDeelectrification(List<int> trackIds)
        {
            if (trackIds == null || trackIds.Count == 0) return;
            if (trackGraph == null || catenaryGenerator == null) return;

            var actuallyChanged = new List<int>();
            foreach (int id in trackIds)
            {
                var track = trackGraph.GetTrack(id);
                if (track != null && track.HasCatenary)
                {
                    trackGraph.SetTrackCatenary(id, false);
                    actuallyChanged.Add(id);
                }
            }

            if (actuallyChanged.Count > 0)
            {
                catenaryGenerator.GenerateNetwork();
                Log.Info($"[Electrification] De-electrified {actuallyChanged.Count} tracks");

                DepotSystem.Undo.UndoManager.Record(
                    DepotSystem.Undo.UndoCategory.SiecTrakcyjna,
                    new DepotSystem.Undo.CatenaryToggleCommand(actuallyChanged, true));
            }
        }

        private void TryDeelectrifyNearest(Vector3 mouseWorld)
        {
            var nearest = FindNearestTrack(mouseWorld, singleTrackRadius);
            if (!nearest.HasValue) return;

            var track = trackGraph.GetTrack(nearest.Value);
            if (track == null || !track.HasCatenary) return;

            trackGraph.SetTrackCatenary(nearest.Value, false);
            catenaryGenerator.GenerateNetwork();

            Log.Info($"[Electrification] De-electrified track {nearest.Value}");

            DepotSystem.Undo.UndoManager.Record(
                DepotSystem.Undo.UndoCategory.SiecTrakcyjna,
                new DepotSystem.Undo.CatenaryToggleCommand(new List<int> { nearest.Value }, true));
        }

        // ═══════════════════════════════════════════
        //  PREVIEW I HIGHLIGHT
        // ═══════════════════════════════════════════

        /// <summary>
        /// Podświetla tory pod kursorem (idle hover).
        /// </summary>
        private void UpdateHoverHighlights(List<int> nearbyTracks, bool removing = false)
        {
            // Sprawdź czy zmienił się zestaw torów
            bool changed = nearbyTracks.Count != highlightObjects.Count;
            if (!changed)
            {
                int firstId = nearbyTracks.Count > 0 ? nearbyTracks[0] : -1;
                changed = firstId != hoveredTrackId;
            }

            if (!changed) return;

            hoveredTrackId = nearbyTracks.Count > 0 ? nearbyTracks[0] : -1;

            ClearAllHighlights();
            Color hoverColor = removing ? deelectrifyColor : previewColor;
            foreach (int trackId in nearbyTracks)
                HighlightTrack(trackId, hoverColor);
        }

        /// <summary>
        /// Aktualizuje podświetlenie podczas przeciągania.
        /// </summary>
        private void UpdateDragPreview()
        {
            ClearAllHighlights();

            if (selectedTracks.Count == 0) return;

            bool removing = IsRemoveMode;

            foreach (int trackId in selectedTracks)
            {
                var track = trackGraph.GetTrack(trackId);
                bool hasCatenary = track != null && track.HasCatenary;

                Color color;
                if (removing)
                    color = hasCatenary ? deelectrifyColor : new Color(0.4f, 0.4f, 0.4f, 0.3f);
                else
                    color = hasCatenary ? electrifiedColor : previewColor;

                HighlightTrack(trackId, color);
            }
        }

        private void HighlightTrack(int trackId, Color color)
        {
            var poly = trackGraph.GetTrackPolyline(trackId);
            if (poly == null || poly.Count < 2) return;

            var hlObj = new GameObject($"TrackHighlight_{trackId}");
            var lr = hlObj.AddComponent<LineRenderer>();
            lr.material = MaterialFactory.CreateLine();
            lr.startColor = color;
            lr.endColor = color;
            lr.startWidth = 1.5f;
            lr.endWidth = 1.5f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var positions = new Vector3[poly.Count];
            for (int i = 0; i < poly.Count; i++)
                positions[i] = new Vector3(poly[i].x, 0.15f, poly[i].z);

            lr.positionCount = positions.Length;
            lr.SetPositions(positions);

            highlightObjects.Add(hlObj);
        }

        private void ClearAllHighlights()
        {
            foreach (var obj in highlightObjects)
            {
                if (obj != null) Destroy(obj);
            }
            highlightObjects.Clear();
        }

        // ═══════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════

        private void CancelDrag()
        {
            state = ElectrificationState.Idle;
            selectedTracks.Clear();
            ClearAllHighlights();
        }

        /// <summary>
        /// Znajduje WSZYSTKIE tory w podanym promieniu od pozycji.
        /// Pędzel — zbiera wiele torów na raz.
        /// </summary>
        private List<int> FindTracksInRadius(Vector3 worldPos, float radius)
        {
            var result = new List<int>();
            if (trackGraph == null) return result;

            foreach (var track in trackGraph.Tracks.Values)
            {
                var poly = trackGraph.GetTrackPolyline(track.TrackId);
                if (poly == null || poly.Count < 2) continue;

                // Szybki test: odległość od środka toru
                Vector3 mid = (track.StartPosition + track.EndPosition) / 2f;
                float roughDist = Vector3.Distance(
                    new Vector3(worldPos.x, 0, worldPos.z),
                    new Vector3(mid.x, 0, mid.z));
                if (roughDist > track.Length / 2f + radius * 2f) continue;

                // Dokładny test: rzutuj na polyline
                float projDist = ProjectOnPolyline(poly, worldPos);
                var (closestPt, _) = TrackGeometry.GetPointAtDistance(poly, projDist);
                float d = Vector3.Distance(
                    new Vector3(worldPos.x, 0, worldPos.z),
                    new Vector3(closestPt.x, 0, closestPt.z));

                if (d < radius)
                    result.Add(track.TrackId);
            }

            return result;
        }

        /// <summary>
        /// Znajduje najbliższy tor (jeden) w podanym promieniu.
        /// </summary>
        private int? FindNearestTrack(Vector3 worldPos, float radius)
        {
            if (trackGraph == null) return null;

            float bestDist = radius;
            int bestTrackId = -1;

            foreach (var track in trackGraph.Tracks.Values)
            {
                var poly = trackGraph.GetTrackPolyline(track.TrackId);
                if (poly == null || poly.Count < 2) continue;

                Vector3 mid = (track.StartPosition + track.EndPosition) / 2f;
                float roughDist = Vector3.Distance(
                    new Vector3(worldPos.x, 0, worldPos.z),
                    new Vector3(mid.x, 0, mid.z));
                if (roughDist > track.Length / 2f + radius * 2f) continue;

                float projDist = ProjectOnPolyline(poly, worldPos);
                var (closestPt, _) = TrackGeometry.GetPointAtDistance(poly, projDist);
                float d = Vector3.Distance(
                    new Vector3(worldPos.x, 0, worldPos.z),
                    new Vector3(closestPt.x, 0, closestPt.z));

                if (d < bestDist)
                {
                    bestDist = d;
                    bestTrackId = track.TrackId;
                }
            }

            return bestTrackId >= 0 ? bestTrackId : null;
        }

        private float ProjectOnPolyline(List<Vector3> polyline, Vector3 point)
        {
            float bestDist = float.MaxValue;
            float bestAlong = 0f;
            float accum = 0f;

            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector3 a = polyline[i];
                Vector3 b = polyline[i + 1];
                float segLen = Vector3.Distance(a, b);
                if (segLen < 0.001f) { accum += segLen; continue; }

                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / (segLen * segLen));
                Vector3 closest = a + ab * t;
                float d = Vector3.Distance(
                    new Vector3(point.x, 0, point.z),
                    new Vector3(closest.x, 0, closest.z));

                if (d < bestDist) { bestDist = d; bestAlong = accum + t * segLen; }
                accum += segLen;
            }

            return bestAlong;
        }

        private Vector3 GetMouseWorldPosition()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return Vector3.zero;
            if (Mouse.current == null) return Vector3.zero;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = mainCamera.ScreenPointToRay(mousePos);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

            if (groundPlane.Raycast(ray, out float distance))
                return ray.GetPoint(distance);

            return Vector3.zero;
        }

    }

    public enum ElectrificationState
    {
        Idle,
        Dragging
    }
}
