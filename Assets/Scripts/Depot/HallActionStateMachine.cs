using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    /// <summary>
    /// Obsługuje akcje specyficzne dla Hali: tor, elektryfikacja, myjnia, podnośnik.
    /// Musi być dodany do sceny (np. na DepotManager GameObject).
    ///
    /// MM-19 (2026-05-08): pełna implementacja `HandleHallTrack` — kliknij dwa punkty
    /// w hali (lub przy ścianie hali), system stawia prosty tor i auto-tworzy TrackGate
    /// w każdej ścianie którą tor przekracza. To zastępuje stub z M2 i pozwala fizycznie
    /// łączyć tory wewnętrzne hali z torami parkingowymi na zewnątrz (po MM-15 TrackGate).
    /// </summary>
    public class HallActionStateMachine : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (FindAnyObjectByType<HallActionStateMachine>() != null) return;
            var go = new GameObject("HallActionStateMachine (auto-spawn)");
            go.AddComponent<HallActionStateMachine>();
        }

        // ── Input System ──
        private InputActions _inputActions;
        private InputActions.ToolBuildActions _toolBuild;

        // ── Mouse / camera ──
        private Camera _mainCamera;
        private const float GridSize = 1f;
        private const float SampleStepMeters = 0.5f;     // krok sample'owania linii dla walidacji
        private const float WallCrossingTolerance = 0.3f;
        private const float MinTrackLengthMeters = 2f;

        // ── State machine dla HallTrack ──
        private enum HallTrackState { Idle, PreviewingEnd }
        private HallTrackState _hallTrackState = HallTrackState.Idle;
        private Vector3 _pointA;
        private DetectedRoom _hallRoomA;

        // ── Preview visuals ──
        private GameObject _pointAMarker;
        private LineRenderer _previewLine;
        private Material _previewLineMat;

        // ── Cached references ──
        private WallBuildingSystem _wallSystem;
        private RoomDetectionSystem _roomSystem;
        private PrefabTrackBuilder _trackBuilder;

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
            _toolGate = new ToolModeGate(this, m => m == ToolMode.BuildRoom, Cleanup);
            _toolGate.Start();
        }

        void OnDestroy()
        {
            _toolGate?.Stop();
            _inputActions?.Dispose();
            CleanupPreview();
        }

        void Update()
        {
            if (DepotUIManager.Instance.IsPointerOverUI()) return;

            switch (DepotUIManager.Instance.CurrentRoomAction)
            {
                case RoomActionMode.PlaceHallTrack:
                    HandleHallTrack();
                    break;
                case RoomActionMode.ElectrifyHall:
                    HandleElectrifyHall();
                    break;
                default:
                    Cleanup();
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  MM-19 — Tor wewnątrz hali (proste segmenty + auto TrackGate)
        // ═══════════════════════════════════════════════════════════════

        private void HandleHallTrack()
        {
            if (_toolBuild.Cancel.WasPressedThisFrame())
            {
                Cancel();
                return;
            }

            EnsureRefs();
            Vector3 worldPos = GetMouseWorldPosition();
            if (worldPos == Vector3.zero) return;

            Vector3 snapped = SnapToGrid(worldPos);

            switch (_hallTrackState)
            {
                case HallTrackState.Idle:
                    HandleHallTrackIdle(snapped);
                    break;
                case HallTrackState.PreviewingEnd:
                    HandleHallTrackPreviewingEnd(snapped);
                    break;
            }
        }

        private void HandleHallTrackIdle(Vector3 snapped)
        {
            // Walidacja: punkt A musi być w Hall room (nie na wallu)
            DetectedRoom room = FindHallRoomAt(snapped);
            UpdatePointAPreview(snapped, room != null);

            if (_toolBuild.Primary.WasPressedThisFrame())
            {
                if (room == null)
                {
                    Log.Info("[HallTrack] Punkt A musi być wewnątrz hali. Kliknij na podłodze hali.");
                    return;
                }
                _pointA = snapped;
                _hallRoomA = room;
                _hallTrackState = HallTrackState.PreviewingEnd;
            }
        }

        private void HandleHallTrackPreviewingEnd(Vector3 snapped)
        {
            // Walidacja segmentu A→B
            var validation = ValidateHallTrackSegment(_pointA, snapped);
            UpdateLinePreview(_pointA, snapped, validation);

            if (_toolBuild.Primary.WasPressedThisFrame())
            {
                if (!validation.canPlace)
                {
                    Log.Info($"[HallTrack] Nie można postawić: {validation.reason}");
                    return;
                }

                if (Vector3.Distance(_pointA, snapped) < MinTrackLengthMeters)
                {
                    Log.Info($"[HallTrack] Tor za krótki (min {MinTrackLengthMeters}m).");
                    return;
                }

                CommitHallTrack(_pointA, snapped, validation);
                _hallTrackState = HallTrackState.Idle;
                _hallRoomA = null;
                CleanupPreview();
            }
        }

        // ── Walidacja segmentu ─────────────────────────────────────────

        private struct HallTrackValidation
        {
            public bool canPlace;
            public string reason;
            /// <summary>Lista wall crossings (wall + dystans wzdłuż wallu) gdzie trzeba auto-utworzyć TrackGate.</summary>
            public List<(WallSegment wall, float distOnWall)> wallCrossings;
        }

        private HallTrackValidation ValidateHallTrackSegment(Vector3 a, Vector3 b)
        {
            var result = new HallTrackValidation
            {
                wallCrossings = new List<(WallSegment, float)>(),
            };

            if (_roomSystem == null || _wallSystem == null)
            {
                result.reason = "Brak RoomDetectionSystem/WallBuildingSystem";
                return result;
            }

            // Endpoint B: musi być w Hall room (dowolny — niekoniecznie ten sam co A)
            DetectedRoom roomB = FindHallRoomAt(b);
            if (roomB == null)
            {
                result.reason = "Punkt B musi być w hali";
                return result;
            }

            // Sample wzdłuż linii A→B i sprawdź każdy punkt
            float length = Vector3.Distance(a, b);
            int steps = Mathf.Max(2, Mathf.CeilToInt(length / SampleStepMeters));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector3 p = Vector3.Lerp(a, b, t);

                if (FindHallRoomAt(p) != null) continue; // OK — jest w hali

                // Punkt poza halą — czy to crossing wallu Hall room?
                bool nearWall = IsPointNearHallWall(p, out var wall, out float distOnWall);
                if (!nearWall)
                {
                    result.reason = $"Tor wychodzi poza halę (punkt @ ({p.x:F1}, {p.z:F1})). " +
                                    "Zakończ tor przy ścianie żeby utworzyć bramę wjazdową.";
                    return result;
                }

                // Crossing wykryty — dorzuć do listy auto-trackgate (deduplikacja po wallId)
                bool already = false;
                foreach (var (w, _) in result.wallCrossings)
                    if (w.wallId == wall.wallId) { already = true; break; }
                if (!already)
                {
                    // Snap dystansu do siatki + walidacja czy można postawić TrackGate
                    float snappedDist = _wallSystem.SnapTrackGateDistance(wall, distOnWall);
                    if (!_wallSystem.IsTrackGatePlacementValid(wall, snappedDist))
                    {
                        // Sprawdź czy już jest TrackGate na tym wallu (existing opening)
                        if (HasExistingTrackGateAt(wall, distOnWall)) continue;
                        result.reason = $"Brama wjazdowa nie pasuje na wall #{wall.wallId} " +
                                        "(za blisko innego otworu lub za krótki segment).";
                        return result;
                    }
                    result.wallCrossings.Add((wall, snappedDist));
                }
            }

            result.canPlace = true;
            return result;
        }

        private bool HasExistingTrackGateAt(WallSegment wall, float distOnWall)
        {
            if (wall == null || wall.openings == null) return false;
            foreach (var opening in wall.openings)
            {
                if (opening.type != OpeningType.TrackGate) continue;
                float halfWidth = _wallSystem.TrackGateWidth * 0.5f;
                if (Mathf.Abs(opening.distanceOnWall - distOnWall) <= halfWidth)
                    return true;
            }
            return false;
        }

        private bool IsPointNearHallWall(Vector3 worldPos, out WallSegment foundWall, out float distOnWall)
        {
            foundWall = null;
            distOnWall = 0f;
            if (_wallSystem == null) return false;

            float bestDistSq = WallCrossingTolerance * WallCrossingTolerance;

            foreach (var wall in _wallSystem.AllWalls)
            {
                if (wall == null) continue;

                Vector3 a = wall.startPos;
                Vector3 b = wall.endPos;
                Vector3 ab = b - a;
                float lenSq = ab.sqrMagnitude;
                if (lenSq < 0.001f) continue;

                float t = Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / lenSq);
                Vector3 proj = a + ab * t;
                float dSq = (worldPos.x - proj.x) * (worldPos.x - proj.x)
                          + (worldPos.z - proj.z) * (worldPos.z - proj.z);

                if (dSq <= bestDistSq)
                {
                    bestDistSq = dSq;
                    foundWall = wall;
                    distOnWall = t * Mathf.Sqrt(lenSq);
                }
            }

            return foundWall != null;
        }

        // ── Commit (place track + auto-trackgate) ─────────────────────

        private void CommitHallTrack(Vector3 a, Vector3 b, HallTrackValidation validation)
        {
            // 1. Auto-create TrackGate dla każdego wall crossing (przed placement toru
            //    żeby wall mesh już miał lukę gdy track visual się rysuje obok)
            foreach (var (wall, distOnWall) in validation.wallCrossings)
            {
                if (HasExistingTrackGateAt(wall, distOnWall)) continue;
                bool ok = _wallSystem.TryAddTrackGateOpening(wall, distOnWall);
                if (ok)
                    Log.Info($"[HallTrack] Auto-created TrackGate na wall#{wall.wallId} @ {distOnWall:F1}m");
                else
                    Log.Warn($"[HallTrack] Failed to create TrackGate na wall#{wall.wallId} @ {distOnWall:F1}m");
            }

            // 2. Place tor jako prosty segment A→B (rejestrowany w TrackGraph przez PrefabTrackBuilder)
            if (_trackBuilder == null)
            {
                Log.Warn("[HallTrack] PrefabTrackBuilder nie znaleziony — tor nie postawiony");
                return;
            }

            Vector3 startFlat = new Vector3(a.x, 0f, a.z);
            Vector3 endFlat = new Vector3(b.x, 0f, b.z);
            var segment = _trackBuilder.PlaceTrackSegment(startFlat, endFlat,
                trackName: "Tor w hali",
                trackType: DepotTrackType.Parking);

            if (segment != null)
            {
                Log.Info($"[HallTrack] Postawiony tor wewnątrz hali, długość {Vector3.Distance(a, b):F1}m, " +
                         $"crossings={validation.wallCrossings.Count}");
            }
            else
            {
                Log.Warn("[HallTrack] PlaceTrackSegment zwrócił null");
            }
        }

        // ── Helpers: lookup Hall room ─────────────────────────────────

        private DetectedRoom FindHallRoomAt(Vector3 worldPos)
        {
            if (_roomSystem == null) return null;
            int cellX = Mathf.FloorToInt(worldPos.x);
            int cellZ = Mathf.FloorToInt(worldPos.z);
            var cell = new Vector2Int(cellX, cellZ);
            foreach (var r in _roomSystem.Rooms)
            {
                if (r == null || r.roomType != RoomType.Hall) continue;
                if (!r.bounds.Contains(cell)) continue;
                // bounds.Contains nie wystarcza dla L-kształtnych pomieszczeń, ale dla MVP wystarczy
                // (Hall jest zawsze prostokątny). Jeśli L-shape pojawi się — switch na cells.Contains.
                if (r.cells != null && r.cells.Count > 0 && !r.cells.Contains(cell)) continue;
                return r;
            }
            return null;
        }

        // ── Preview visuals ──────────────────────────────────────────

        private void UpdatePointAPreview(Vector3 pos, bool valid)
        {
            if (_pointAMarker == null)
            {
                _pointAMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _pointAMarker.name = "HallTrackPointAPreview";
                _pointAMarker.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
                Object.Destroy(_pointAMarker.GetComponent<Collider>());
            }
            _pointAMarker.transform.position = new Vector3(pos.x, 0.2f, pos.z);
            var rend = _pointAMarker.GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null)
                rend.sharedMaterial.color = valid ? new Color(0.3f, 1f, 0.3f, 1f) : new Color(1f, 0.3f, 0.3f, 1f);
        }

        private void UpdateLinePreview(Vector3 a, Vector3 b, HallTrackValidation v)
        {
            EnsurePreviewLine();
            _previewLine.SetPosition(0, new Vector3(a.x, 0.15f, a.z));
            _previewLine.SetPosition(1, new Vector3(b.x, 0.15f, b.z));

            Color c;
            if (!v.canPlace) c = new Color(1f, 0.3f, 0.3f, 1f);
            else if (v.wallCrossings.Count > 0) c = new Color(1f, 0.85f, 0.3f, 1f);
            else c = new Color(0.3f, 1f, 0.3f, 1f);

            _previewLine.startColor = c;
            _previewLine.endColor = c;
        }

        private void EnsurePreviewLine()
        {
            if (_previewLine != null) return;
            var go = new GameObject("HallTrackPreviewLine");
            _previewLine = go.AddComponent<LineRenderer>();
            _previewLine.positionCount = 2;
            _previewLine.startWidth = 0.3f;
            _previewLine.endWidth = 0.3f;
            _previewLine.useWorldSpace = true;
            if (_previewLineMat == null)
                _previewLineMat = MaterialFactory.CreateLine();
            _previewLine.material = _previewLineMat;
        }

        private void CleanupPreview()
        {
            if (_pointAMarker != null) { Destroy(_pointAMarker); _pointAMarker = null; }
            if (_previewLine != null) { Destroy(_previewLine.gameObject); _previewLine = null; }
        }

        // ── Mouse / snap helpers ─────────────────────────────────────

        private Vector3 GetMouseWorldPosition()
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return Vector3.zero;
            if (Mouse.current == null) return Vector3.zero;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = _mainCamera.ScreenPointToRay(mousePos);
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            return groundPlane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : Vector3.zero;
        }

        private static Vector3 SnapToGrid(Vector3 pos)
            => new Vector3(Mathf.Round(pos.x / GridSize) * GridSize, 0f, Mathf.Round(pos.z / GridSize) * GridSize);

        private void EnsureRefs()
        {
            if (_wallSystem == null) _wallSystem = DepotServices.Get<WallBuildingSystem>();
            if (_roomSystem == null) _roomSystem = DepotServices.Get<RoomDetectionSystem>();
            if (_trackBuilder == null) _trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
        }

        // ═══════════════════════════════════════════════════════════════
        //  ElectrifyHall — TBD (OQ: Sieć trakcyjna w hali). Stub na razie,
        //  decyzja design w M-Balance / M12 polish.
        // ═══════════════════════════════════════════════════════════════

        private void HandleElectrifyHall()
        {
            if (_toolBuild.Cancel.WasPressedThisFrame()) { Cancel(); return; }
            // TODO (OQ): Klik na tor w hali → toggle catenary. Reuse logikę z ElectrificationStateMachine.
            // Decyzja czy w ogóle implementować — patrz docs/OPEN_QUESTIONS.md "Sieć trakcyjna w hali".
        }

        private void Cleanup()
        {
            CleanupPreview();
            _hallTrackState = HallTrackState.Idle;
            _hallRoomA = null;
        }

        private void Cancel()
        {
            Cleanup();
            if (DepotUIManager.Instance != null)
                DepotUIManager.Instance.CurrentRoomAction = RoomActionMode.None;
        }
    }
}
