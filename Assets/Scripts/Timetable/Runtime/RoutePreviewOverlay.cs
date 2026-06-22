using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;
using RailwayManager;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Singleton komponent na MapScene rysujący LineRenderer z polyline rozkładu.
    /// Wywoływany z TimetableListUI po kliknięciu [Mapa] przy wierszu rozkładu —
    /// przełącza scenę na MapScene i highlight'uje trasę.
    ///
    /// Auto-tworzy GameObject w razie potrzeby (lazy init). Cleanup po wyjściu z Map.
    /// </summary>
    public class RoutePreviewOverlay : MonoBehaviour
    {
        public static RoutePreviewOverlay Instance { get; private set; }

        private LineRenderer _line;
        private GameObject _lineObj;
        private Camera _mapCamera;

        // Adaptacyjna szerokość: linia ma być cienka przy bliskim zoomie
        // (żeby pokazywała konkretny tor) i grubsza przy oddaleniu (żeby była widoczna).
        // Formula: width_world = clamp(orthoSize * widthScale, minWidth, maxWidth)
        // - przy zoom 50  → 0.5m → clamp do 2.5m (precyzyjna linia po torze)
        // - przy zoom 500 → 5m (parę torów grubości)
        // - przy zoom 5000 → 50m
        // - przy zoom 50000 → 500m → clamp do 250m
        private const float WidthScale = 0.01f;
        private const float MinWidthM = 2.5f;
        private const float MaxWidthM = 250f;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void LateUpdate()
        {
            if (_line == null || _lineObj == null || !_lineObj.activeInHierarchy) return;
            UpdateAdaptiveWidth();
        }

        private void UpdateAdaptiveWidth()
        {
            if (_mapCamera == null || !_mapCamera.isActiveAndEnabled)
                _mapCamera = FindMapCamera();
            if (_mapCamera == null || !_mapCamera.orthographic) return;

            float w = Mathf.Clamp(_mapCamera.orthographicSize * WidthScale, MinWidthM, MaxWidthM);
            _line.startWidth = w;
            _line.endWidth = w;
        }

        /// <summary>
        /// Znajduje aktywną kamerę MapScene (renderującą warstwę MapLayer).
        /// Cache'owana — Update szuka tylko gdy obecny ref jest null/disabled.
        /// </summary>
        private static Camera FindMapCamera()
        {
            // Camera.main może być Depot — szukamy konkretnie kamery na MapLayer
            int mapLayerMask = 1 << SceneController.MapLayer;
            int previewMask = 1 << SceneController.MapPreviewLayer;
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || !cam.orthographic) continue;
                // Pomiń kamerę mini-podglądu (RouteMapPreview) — też renderuje MapLayer,
                // ale szerokość linii dużej mapy ma zależeć od kamery DUŻEJ mapy.
                if ((cam.cullingMask & previewMask) != 0) continue;
                if ((cam.cullingMask & mapLayerMask) != 0)
                    return cam;
            }
            return Camera.main;
        }

        /// <summary>
        /// Pokazuje trasę na mapie. Jeśli MapScene nie aktywna — przełącza.
        /// </summary>
        public void Show(Route route)
        {
            if (route == null || route.nodeIds == null || route.nodeIds.Count < 2)
            {
                Log.Warn("[RoutePreviewOverlay] Brak trasy do podglądu");
                return;
            }

            var init = TimetableInitializer.Instance;
            if (init?.Graph == null)
            {
                Log.Warn("[RoutePreviewOverlay] TimetableInitializer not ready");
                return;
            }

            EnsureLineRenderer();

            var polyline = init.Graph.BuildRoutePolyline(route.nodeIds, out _);
            _line.positionCount = polyline.Count;
            for (int i = 0; i < polyline.Count; i++)
                _line.SetPosition(i, new Vector3(polyline[i].x, 18f, polyline[i].y));

            _lineObj.SetActive(true);
            Log.Info($"[RoutePreviewOverlay] Highlight: {route.name} ({polyline.Count} pts from {route.nodeIds.Count} nodes)");
        }

        /// <summary>Ukrywa overlay (LineRenderer pozostaje w pamięci do reuse).</summary>
        public void Hide()
        {
            if (_lineObj != null) _lineObj.SetActive(false);
        }

        private void EnsureLineRenderer()
        {
            if (_line != null && _lineObj != null) return;

            _lineObj = new GameObject("RoutePreviewOverlay_Line");
            // Warstwa nakładek DUŻEJ mapy (nie kafle 31) — żeby mini-podgląd jej nie pokazywał.
            _lineObj.layer = SceneController.MapOverlayLayer;
            _line = _lineObj.AddComponent<LineRenderer>();
            _line.material = MaterialFactory.CreateLine();
            _line.startColor = new Color(0.2f, 0.9f, 1f, 0.9f); // jasny cyan
            _line.endColor = new Color(0.2f, 0.9f, 1f, 0.9f);
            _line.useWorldSpace = true;
            _line.sortingOrder = 110;
            _line.numCapVertices = 4;     // ładne zaokrąglone końce
            _line.numCornerVertices = 4;  // gładkie zakręty po łukach torów

            // Inicjalna szerokość — od razu, żeby pierwszy frame nie był 90m
            _mapCamera = FindMapCamera();
            UpdateAdaptiveWidth();
            if (_line.startWidth <= 0) { _line.startWidth = 5f; _line.endWidth = 5f; }
        }

        /// <summary>
        /// Helper wywoływany z UI: znajdź instancję (auto-create jeśli brak),
        /// przełącz scenę i pokaż trasę.
        /// </summary>
        public static void ShowRouteOnMap(Route route)
        {
            if (Instance == null)
            {
                var go = new GameObject("RoutePreviewOverlay");
                go.AddComponent<RoutePreviewOverlay>();
                DontDestroyOnLoad(go);
            }

            // Przełącz scenę na MapScene jeśli nie aktywna
            if (SceneController.ActiveScene != SceneController.GameScene.Map)
                SceneController.SwitchToMap();

            Instance.Show(route);
        }
    }
}
