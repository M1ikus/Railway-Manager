using System.Collections.Generic;
using UnityEngine;
using MapSystem;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Ogranicza ruch kamery do FAKTYCZNEGO KONTURU Polski (country polygon outline),
    /// nie tylko axis-aligned bbox. Per frame (LateUpdate) sprawdza czy kamera jest w PL polygon
    /// i jeśli nie — klampuje do najbliższego punktu konturu.
    ///
    /// Country polygon = admin_level=2 'Polska' z OSM (~9000 vertices). Klamp używa closest-point-on-edge
    /// test dla każdej krawędzi outline (O(N) per frame — dla 9k vertices ~5ms na frame, OK).
    /// </summary>
    public class MapCameraBoundsLimiter : MonoBehaviour
    {
        [Header("References")]
        public Camera mapCamera;

        [Header("Margin")]
        [Tooltip("Margin (m) — kamera może wyjść poza kontur o N metrów. Default 0 = dokładnie po granicy.")]
        public float marginM = 0f;

        [Header("Debug")]
        [Tooltip("Log co N frame'ów gdy kamera jest klampowana.")]
        public int debugLogEveryNFrames = 0;

        private List<Vector2> _polandOutline; // cached z PL country polygon
        private int _clampLogCounter;

        void Start()
        {
            // Autowire camera
            if (mapCamera == null)
            {
                var scene = gameObject.scene;
                if (scene.IsValid())
                {
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        mapCamera = root.GetComponentInChildren<Camera>(true);
                        if (mapCamera != null) break;
                    }
                }
            }
            if (mapCamera == null)
            {
                Log.Warn("[MapCameraBoundsLimiter] Camera not found — limiter disabled.");
                return;
            }

            CountryOverlayService.OnInitialized += BuildOutline;
            if (CountryOverlayService.IsInitialized) BuildOutline();
        }

        void OnDestroy()
        {
            CountryOverlayService.OnInitialized -= BuildOutline;
        }

        private void BuildOutline()
        {
            AdminRegion poland = null;
            foreach (var r in CountryOverlayService.GetPolandRegions())
            {
                if (r.adminLevel == 2) { poland = r; break; }
            }
            if (poland == null)
            {
                Log.Warn("[MapCameraBoundsLimiter] No PL country polygon — limiter disabled.");
                return;
            }

            _polandOutline = ExtractPolygonOutline(poland.vertices, poland.indices);
            if (_polandOutline == null || _polandOutline.Count < 3)
            {
                Log.Warn($"[MapCameraBoundsLimiter] Failed to extract PL outline.");
                _polandOutline = null;
                return;
            }
            Log.Info($"[MapCameraBoundsLimiter] PL outline ready ({_polandOutline.Count} verts). " +
                     $"Camera będzie klampowana do konturu + {marginM:F0}m margin.");
        }

        void LateUpdate()
        {
            if (_polandOutline == null || mapCamera == null) return;

            var pos = mapCamera.transform.position;
            var xz = new Vector2(pos.x, pos.z);

            // Point-in-polygon test via CountryOverlayService (uses triangulation, O(triangles)).
            bool inside = CountryOverlayService.IsInsidePoland(xz);
            if (inside) return; // OK — kamera w PL

            // Outside PL — find closest point on outline + clamp camera tam
            var clamped = ClosestPointOnPolygonOutline(xz, _polandOutline);

            // Push outward by margin (toward inside direction — simple: just stay on boundary for now,
            // margin effect is via point-in-polygon test with expanded polygon — MVP: no margin applied,
            // user prosił margin=0).
            mapCamera.transform.position = new Vector3(clamped.x, pos.y, clamped.y);

            if (debugLogEveryNFrames > 0)
            {
                _clampLogCounter++;
                if (_clampLogCounter % debugLogEveryNFrames == 0)
                    Log.Info($"[MapCameraBoundsLimiter] Clamped camera from ({xz.x:F0},{xz.y:F0}) to ({clamped.x:F0},{clamped.y:F0})");
            }
        }

        /// <summary>
        /// Find closest point on any edge of closed polygon outline.
        /// O(N) — iterate po krawędziach, closest-point-on-segment per edge, take min.
        /// </summary>
        private static Vector2 ClosestPointOnPolygonOutline(Vector2 p, List<Vector2> outline)
        {
            Vector2 best = outline[0];
            float bestSqDist = (p - best).sqrMagnitude;
            int n = outline.Count;
            for (int i = 0; i < n; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % n];
                var c = ClosestPointOnSegment(p, a, b);
                float d = (p - c).sqrMagnitude;
                if (d < bestSqDist) { bestSqDist = d; best = c; }
            }
            return best;
        }

        private static Vector2 ClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return a;
            float t = Vector2.Dot(p - a, ab) / lenSq;
            t = Mathf.Clamp01(t);
            return a + ab * t;
        }

        // === Boundary edge extraction (kopia z CountryOutsideMeshRenderer) ===

        private static List<Vector2> ExtractPolygonOutline(List<Vector2> vertices, List<int> indices)
        {
            var edgeCount = new Dictionary<long, int>();
            var edgeOrient = new Dictionary<long, (int from, int to)>();
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                AddEdge(edgeCount, edgeOrient, a, b);
                AddEdge(edgeCount, edgeOrient, b, c);
                AddEdge(edgeCount, edgeOrient, c, a);
            }
            var boundaryEdges = new Dictionary<int, int>();
            foreach (var kv in edgeCount)
                if (kv.Value == 1) { var (from, to) = edgeOrient[kv.Key]; boundaryEdges[from] = to; }
            if (boundaryEdges.Count < 3) return null;

            var outline = new List<Vector2>();
            var visited = new HashSet<int>();
            int startVertex = -1;
            foreach (var kv in boundaryEdges) { startVertex = kv.Key; break; }
            int current = startVertex;
            while (current >= 0 && !visited.Contains(current))
            {
                visited.Add(current);
                outline.Add(vertices[current]);
                if (!boundaryEdges.TryGetValue(current, out int next)) break;
                current = next;
                if (current == startVertex) break;
            }
            return outline.Count >= 3 ? outline : null;
        }

        private static void AddEdge(Dictionary<long, int> count, Dictionary<long, (int, int)> orient, int from, int to)
        {
            int lo = System.Math.Min(from, to), hi = System.Math.Max(from, to);
            long key = ((long)lo << 32) | (uint)hi;
            if (count.TryGetValue(key, out int c)) count[key] = c + 1;
            else { count[key] = 1; orient[key] = (from, to); }
        }
    }
}
