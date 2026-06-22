using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using RailwayManager.Core;

namespace RailwayManager.Timetable.Simulation
{
    /// <summary>
    /// Manualny handler klików na mapie 2D. New Input System Only nie wspiera
    /// OnMouseDown, więc robimy raczej raycast z pozycji myszy w Update.
    ///
    /// Priorytet detekcji:
    /// 1. Pociąg (TrainMarker) — klik bezpośrednio na prostokąt pociągu
    /// 2. Stacja (MapSystem.StationMarker) — klik na marker stacji
    /// 3. Tor/linia — klik w pustą część mapy → snap do najbliższego edge w PathfindingGraph
    /// </summary>
    public class MapClickHandler : MonoBehaviour
    {
        /// <summary>Global event: kliknięto stację.</summary>
        public static event System.Action<MapSystem.StationMarker> OnStationClicked;

        /// <summary>Global event: kliknięto tor. (segmentId z grafu OSM, worldPos kliku)</summary>
        public static event System.Action<int, Vector2> OnTrackClicked;

        /// <summary>Max odległość (world units) od klika do najbliższego toru żeby uznać za klik.</summary>
        const float TrackSnapMaxDistM = 500f;

        Camera _mapCamera;

        void Update()
        {
            // Skip jeśli nie jesteśmy w scenie mapy (depot active = click w Depot, nie dla nas)
            if (RailwayManager.Core.SceneController.ActiveScene != RailwayManager.Core.SceneController.GameScene.Map)
                return;

            var mouse = Mouse.current;
            if (mouse == null) return;
            if (!mouse.leftButton.wasPressedThisFrame) return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            if (_mapCamera == null || !_mapCamera.isActiveAndEnabled)
                _mapCamera = FindMapCamera();
            if (_mapCamera == null) return;

            var mousePos = mouse.position.ReadValue();
            var ray = _mapCamera.ScreenPointToRay(mousePos);

            // 1. Raycast z kolizjami (trafia pociąg / stacja)
            int mapLayerMask = 1 << 31;
            if (Physics.Raycast(ray, out var hit, 10000f, mapLayerMask, QueryTriggerInteraction.Collide))
            {
                // Pociąg?
                var trainMarker = hit.collider.GetComponent<TrainMarker>();
                if (trainMarker != null)
                {
                    TrainMarker.InvokeClick(trainMarker);
                    return;
                }

                // Stacja?
                var stationMarker = hit.collider.GetComponentInParent<MapSystem.StationMarker>();
                if (stationMarker != null)
                {
                    OnStationClicked?.Invoke(stationMarker);
                    return;
                }
            }

            // 2. Fallback — klik w ziemię, spróbuj znaleźć najbliższy tor
            var worldPos = GetWorldPosFromRay(ray);
            if (worldPos.HasValue)
            {
                TryClickNearestTrack(worldPos.Value);
            }
        }

        /// <summary>Rzut ray na płaszczyznę Y=railwayHeight (~8m) żeby dostać 2D pozycję na mapie.</summary>
        static Vector2? GetWorldPosFromRay(Ray ray)
        {
            // Płaszczyzna mapy na Y=8 (railways)
            var plane = new Plane(Vector3.up, new Vector3(0, 8f, 0));
            if (plane.Raycast(ray, out float enter))
            {
                var p = ray.GetPoint(enter);
                return new Vector2(p.x, p.z);
            }
            return null;
        }

        void TryClickNearestTrack(Vector2 worldPos)
        {
            var init = TimetableInitializer.Instance;
            if (init == null || init.Graph == null) return;

            var graph = init.Graph;
            float bestDistSq = TrackSnapMaxDistM * TrackSnapMaxDistM;
            int bestEdgeId = -1;

            // Iteruj po wszystkich krawędziach, znajdź najbliższą do worldPos.
            // Przy setkach tysięcy krawędzi to O(N) per klik — niekrytyczne (1 klik/s).
            int edgeCount = graph.EdgeCount;
            for (int e = 0; e < edgeCount; e++)
            {
                var edge = graph.GetEdge(e);
                var a = graph.GetNode(edge.fromNodeId).position;
                var b = graph.GetNode(edge.toNodeId).position;

                float dSq = DistancePointToSegmentSq(worldPos, a, b);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestEdgeId = e;
                }
            }

            if (bestEdgeId >= 0)
            {
                OnTrackClicked?.Invoke(bestEdgeId, worldPos);
                Log.Info($"[MapClickHandler] Track clicked: edge#{bestEdgeId}, " +
                         $"dist={Mathf.Sqrt(bestDistSq):F0}m");
            }
        }

        static float DistancePointToSegmentSq(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return (p - a).sqrMagnitude;

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            var proj = a + ab * t;
            return (p - proj).sqrMagnitude;
        }

        static Camera FindMapCamera()
        {
            int mapLayerMask = 1 << 31;
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || !cam.orthographic) continue;
                if ((cam.cullingMask & mapLayerMask) != 0)
                    return cam;
            }
            return Camera.main;
        }
    }
}
