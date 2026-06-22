using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;
using MapSystem;
using formap;

namespace RailwayManager.Timetable.UI
{
    /// <summary>
    /// Reuzywalny, kompozytowalny widget mini-mapy OSM: druga kamera ortho renderuje WYŁĄCZNIE
    /// warstwę MapPreview (29) — własne kafle OSM (<see cref="RouteMapPreviewTiles"/>, własny LOD
    /// niezależny od głównej mapy) + własną zawartość (polilinie/markery) — do RenderTexture w RawImage.
    /// Pan/zoom + fit-to-content.
    ///
    /// Reuzywalność: <see cref="SetPolylines"/> (N kolorowych tras — bieżąca / wszystkie rozkłady /
    /// obieg) + <see cref="SetMarkers"/> (pozycje: stacje, pojazdy). Konsumenci (kreator/obiegi/tabor)
    /// dokładają dane, widget jest agnostyczny.
    ///
    /// Wzorzec render-to-texture za <c>DepotMinimapUI</c>; separacja warstw przez
    /// <see cref="SceneController.PreviewCameraCullingMask"/>; własne kafle we własnym LOD przez
    /// <see cref="RouteMapPreviewTiles"/> (zamiast pinowania współdzielonych kafli głównej mapy).
    /// </summary>
    public class RouteMapPreview : MonoBehaviour
    {
        public struct PreviewPolyline
        {
            public IReadOnlyList<Vector2> points; // świat XZ (x=X, y=Z)
            public Color color;
            public float widthM; // <=0 → adaptacyjna do zoomu podglądu
        }

        public struct PreviewMarker
        {
            public Vector2 worldPos;
            public Color color;
            public float sizeM;
        }

        private const int RT_SIZE = 512;
        private const float CAM_Y = 2000f;
        private const float LINE_Y = 20f;    // nad kaflami (Railways 8m) — rysuje się na wierzchu
        private const float MARKER_Y = 22f;
        private const float RENDER_TICK_SEC = 0.1f;   // krok budowy/renderu kafli podglądu
        private const int TILES_PER_TICK = 6;         // ile własnych kafli budować na krok (I/O budżet)

        private Camera _cam;
        private RenderTexture _rt;
        private GameObject _panel;
        private RectTransform _panelRect;
        private RawImage _rawImage;
        private GameObject _contentRoot;

        private Material _lineMat;   // Sprites/Default — LineRenderer per-line startColor
        private Material _markerMat; // Unlit/Color — per-marker MaterialPropertyBlock _Color
        private MaterialPropertyBlock _mpb;
        private static Mesh _quadMesh;

        private readonly List<LineRenderer> _linePool = new();
        private readonly List<MeshRenderer> _markerPool = new();
        private readonly List<PreviewPolyline> _polylines = new();
        private readonly List<PreviewMarker> _markers = new();

        private RouteMapPreviewTiles _tiles; // własne kafle OSM podglądu (LOD niezależny od głównej mapy)
        private Coroutine _settleCo;

        // Stan widoku (świat XZ + ortho half-height)
        private Vector2 _center;
        private float _orthoSize = 5000f;
        private bool _disposed;

        private Vector2 _lastPinCenter = new Vector2(float.MaxValue, float.MaxValue);
        private float _lastPinOrtho = -1f;        // throttle repinu (nie co klatkę przeciągania)

        /// <summary>RawImage pokazujący podgląd (host inputu pan/zoom w etapie E).</summary>
        public RawImage Image => _rawImage;
        /// <summary>Rozmiar RawImage w px (do przeliczeń pan).</summary>
        public Vector2 PanelPixelSize => _panelRect != null ? _panelRect.rect.size : new Vector2(RT_SIZE, RT_SIZE);

        /// <summary>Klik na mini-mapie (świat XZ). Konsument (kreator) rozwiązuje najbliższą stację.</summary>
        public event System.Action<Vector2> OnMapClicked;
        /// <summary>Widok zmienił się znacząco (po throttle) — konsument odświeża np. kropki stacji.</summary>
        public event System.Action OnViewChanged;

        /// <summary>Tworzy widget: nowy GameObject + komponent, inicjalizacja na hoście.</summary>
        public static RouteMapPreview Create(RectTransform host, string pinId)
        {
            var go = new GameObject("RouteMapPreview");
            DontDestroyOnLoad(go); // parytet cyklu życia z kreatorem (też DontDestroyOnLoad)
            var w = go.AddComponent<RouteMapPreview>();
            w.Initialize(host);
            return w;
        }

        private void Initialize(RectTransform host)
        {
            BuildMaterials();
            BuildCamera();
            BuildPanel(host);
            _contentRoot = new GameObject("RouteMapPreviewContent");
            _contentRoot.transform.SetParent(transform, false);
            _tiles = new RouteMapPreviewTiles(_contentRoot.transform, SceneController.MapPreviewLayer);
            if (!_tiles.Available)
                Log.Warn("[RouteMapPreview] Brak MapLoader/MapRenderer — podgląd pokaże tylko trasę bez kafli OSM (MapScene niezaładowana?).");

            // Render ciemnego tła od razu — żeby RawImage nie pokazywał niezainicjowanego RT
            // (czarny/garbage) zanim powstanie pierwsza trasa.
            if (_cam != null)
            {
                _cam.transform.position = new Vector3(_center.x, CAM_Y, _center.y);
                _cam.orthographicSize = _orthoSize;
                CameraRenderUtil.Render(_cam);
            }
        }

        private void BuildMaterials()
        {
            _lineMat = MaterialFactory.CreateLine();
            _markerMat = MaterialFactory.CreateUnlit();
            _mpb = new MaterialPropertyBlock();
        }

        private void BuildCamera()
        {
            var camGo = new GameObject("RouteMapPreviewCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.06f, 0.09f, 0.12f, 1f); // ciemne tło (brak kafli)
            _cam.cullingMask = SceneController.PreviewCameraCullingMask; // kafle (31) + zawartość podglądu (29)
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 4000f;
            _cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // patrzy w dół
            _cam.aspect = 1f;
            _cam.enabled = false; // manual render (GPU savings)

            _rt = new RenderTexture(RT_SIZE, RT_SIZE, 16, RenderTextureFormat.ARGB32) { name = "RouteMapPreviewRT" };
            _cam.targetTexture = _rt;
        }

        private void BuildPanel(RectTransform host)
        {
            _panel = new GameObject("RouteMapPreviewPanel");
            _panel.transform.SetParent(host, false);
            _panelRect = _panel.AddComponent<RectTransform>();
            _panelRect.anchorMin = Vector2.zero;
            _panelRect.anchorMax = Vector2.one;
            _panelRect.offsetMin = Vector2.zero;
            _panelRect.offsetMax = Vector2.zero;
            _rawImage = _panel.AddComponent<RawImage>();
            _rawImage.texture = _rt;
            _rawImage.raycastTarget = true; // pan/zoom (etap E)

            var input = _panel.AddComponent<RouteMapPreviewInput>();
            input.Target = this;
        }

        // ── Zawartość ──────────────────────────────────────────────

        public void SetPolylines(IReadOnlyList<PreviewPolyline> polylines)
        {
            _polylines.Clear();
            if (polylines != null) _polylines.AddRange(polylines);
            RebuildLines();
        }

        public void SetMarkers(IReadOnlyList<PreviewMarker> markers)
        {
            _markers.Clear();
            if (markers != null) _markers.AddRange(markers);
            RebuildMarkers();
        }

        public void ClearOverlays()
        {
            _polylines.Clear();
            _markers.Clear();
            RebuildLines();
            RebuildMarkers();
            if (_cam != null) CameraRenderUtil.Render(_cam); // odśwież RT (sama mapa, bez nakładek trasy)
        }

        /// <summary>
        /// Ustaw widok podglądu na bieżący widok DUŻEJ mapy (te kafle są już załadowane → brak lagu).
        /// Używane gdy brak trasy: pusty podgląd pokazuje to, na co patrzy gracz — i jest gotowy do
        /// wybierania stacji klikiem.
        /// </summary>
        public void SyncToMainMapView()
        {
            var mapCam = FindMainMapCamera();
            if (mapCam == null) return;
            var p = mapCam.transform.position;
            _center = new Vector2(p.x, p.z);
            _orthoSize = Mathf.Clamp(mapCam.orthographicSize, RouteMapPreviewMath.MinOrthoSizeM, RouteMapPreviewMath.MaxOrthoSizeM);
            ApplyView();
        }

        private static Camera FindMainMapCamera()
        {
            int mapMask = 1 << SceneController.MapLayer;
            int prevMask = 1 << SceneController.MapPreviewLayer;
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || !cam.orthographic) continue;
                if ((cam.cullingMask & prevMask) != 0) continue; // pomiń kamerę podglądu
                if ((cam.cullingMask & mapMask) != 0) return cam;
            }
            return null;
        }

        private void RebuildLines()
        {
            for (int i = 0; i < _polylines.Count; i++)
            {
                var lr = GetOrCreateLine(i);
                var p = _polylines[i];
                int n = p.points?.Count ?? 0;
                lr.positionCount = n;
                for (int k = 0; k < n; k++)
                    lr.SetPosition(k, new Vector3(p.points[k].x, LINE_Y, p.points[k].y));
                float w = p.widthM > 0f ? p.widthM : RouteMapPreviewMath.AdaptiveWidth(_orthoSize);
                lr.startWidth = lr.endWidth = w;
                lr.startColor = lr.endColor = p.color;
                lr.gameObject.SetActive(n >= 2);
            }
            for (int i = _polylines.Count; i < _linePool.Count; i++)
                _linePool[i].gameObject.SetActive(false);
        }

        private LineRenderer GetOrCreateLine(int idx)
        {
            while (_linePool.Count <= idx)
            {
                var go = new GameObject($"PreviewLine_{_linePool.Count}");
                go.layer = SceneController.MapPreviewLayer;
                go.transform.SetParent(_contentRoot.transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.material = _lineMat;
                lr.useWorldSpace = true;
                lr.numCapVertices = 4;
                lr.numCornerVertices = 4;
                lr.alignment = LineAlignment.View;
                _linePool.Add(lr);
            }
            return _linePool[idx];
        }

        private void RebuildMarkers()
        {
            for (int i = 0; i < _markers.Count; i++)
            {
                var mr = GetOrCreateMarker(i);
                var m = _markers[i];
                var t = mr.transform;
                t.position = new Vector3(m.worldPos.x, MARKER_Y, m.worldPos.y);
                float s = m.sizeM > 0f ? m.sizeM : AdaptiveMarkerSize();
                t.localScale = new Vector3(s, 1f, s);
                mr.GetPropertyBlock(_mpb);
                _mpb.SetColor("_Color", m.color);
                mr.SetPropertyBlock(_mpb);
                mr.gameObject.SetActive(true);
            }
            for (int i = _markers.Count; i < _markerPool.Count; i++)
                _markerPool[i].gameObject.SetActive(false);
        }

        private MeshRenderer GetOrCreateMarker(int idx)
        {
            while (_markerPool.Count <= idx)
            {
                var go = new GameObject($"PreviewMarker_{_markerPool.Count}");
                go.layer = SceneController.MapPreviewLayer;
                go.transform.SetParent(_contentRoot.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = GetQuadMesh();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _markerMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _markerPool.Add(mr);
            }
            return _markerPool[idx];
        }

        private static Mesh GetQuadMesh()
        {
            if (_quadMesh != null) return _quadMesh;
            _quadMesh = new Mesh { name = "RouteMapPreviewQuad" };
            _quadMesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),   new Vector3(-0.5f, 0f, 0.5f)
            };
            _quadMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            _quadMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            _quadMesh.RecalculateBounds();
            return _quadMesh;
        }

        // ── Widok: fit / pan / zoom ────────────────────────────────

        /// <summary>Dopasuj widok do całej bieżącej zawartości (polilinie + markery), zapinuj kafle, renderuj.</summary>
        public void FitToContent()
        {
            if (!TryGetContentBounds(out var b)) return;
            float pad = Mathf.Max(500f, (b.MaxX - b.MinX + b.MaxY - b.MinY) * 0.05f);
            b.MinX -= pad; b.MinY -= pad; b.MaxX += pad; b.MaxY += pad;
            RouteMapPreviewMath.FitOrtho(b, rtAspect: 1f, out _center, out _orthoSize);
            ApplyView();
        }

        private bool TryGetContentBounds(out BBox bounds)
        {
            var polys = new List<IReadOnlyList<Vector2>>(_polylines.Count + 1);
            foreach (var p in _polylines) if (p.points != null) polys.Add(p.points);
            if (_markers.Count > 0)
            {
                var pts = new List<Vector2>(_markers.Count);
                foreach (var m in _markers) pts.Add(m.worldPos);
                polys.Add(pts);
            }
            return RouteMapPreviewMath.TryGetBounds(polys, out bounds);
        }

        /// <summary>Przesunięcie widoku (E: drag). screenDelta w px RawImage.</summary>
        public void Pan(Vector2 screenDelta)
        {
            _center += RouteMapPreviewMath.PanScreenDeltaToWorld(screenDelta, _orthoSize, PanelPixelSize);
            ApplyView();
        }

        /// <summary>Zoom (E: scroll). Dodatni scroll = zoom in.</summary>
        public void Zoom(float scrollDelta)
        {
            _orthoSize = RouteMapPreviewMath.ZoomStep(_orthoSize, scrollDelta);
            ApplyView();
        }

        /// <summary>Wymuś re-render RT (po zmianie zawartości bez zmiany widoku — np. kropki stacji).</summary>
        public void Redraw()
        {
            if (_cam != null) CameraRenderUtil.Render(_cam);
        }

        /// <summary>UV (0..1 w RawImage) → pozycja świata XZ (aspect RT = 1).</summary>
        public Vector2 ViewportToWorld(Vector2 uv01)
        {
            float wx = _center.x + (uv01.x - 0.5f) * 2f * _orthoSize;
            float wz = _center.y + (uv01.y - 0.5f) * 2f * _orthoSize;
            return new Vector2(wx, wz);
        }

        /// <summary>Bieżący widoczny prostokąt świata (aspect=1 → half = orthoSize).</summary>
        public bool TryGetViewBounds(out BBox b)
        {
            b = new BBox
            {
                MinX = _center.x - _orthoSize, MinY = _center.y - _orthoSize,
                MaxX = _center.x + _orthoSize, MaxY = _center.y + _orthoSize
            };
            return true;
        }

        /// <summary>Wywoływane przez RouteMapPreviewInput na klik (nie drag) — emituje OnMapClicked.</summary>
        public void HandleClickAtUv(Vector2 uv01)
        {
            OnMapClicked?.Invoke(ViewportToWorld(uv01));
        }

        private void ApplyView()
        {
            if (_cam == null) return;
            _cam.transform.position = new Vector3(_center.x, CAM_Y, _center.y);
            _cam.orthographicSize = _orthoSize;
            RefreshLineWidths();
            RefreshMarkerSizes();

            // Throttle ciężkich operacji (pin kafli + event dla kropek stacji) do znaczących
            // zmian widoku — bez tego leciałyby co klatkę przeciągania (lag + spam ostrzeżeń).
            float moved = (_center - _lastPinCenter).magnitude;
            float orthoRatio = _lastPinOrtho > 0f ? Mathf.Abs(_orthoSize - _lastPinOrtho) / _lastPinOrtho : 1f;
            if (moved >= TileGrid.TILE_SIZE * 0.5f || orthoRatio >= 0.05f)
            {
                _lastPinCenter = _center;
                _lastPinOrtho = _orthoSize;
                RequestViewportTiles();
                OnViewChanged?.Invoke();
            }
            RenderSoon();
        }

        private void RefreshLineWidths()
        {
            for (int i = 0; i < _polylines.Count && i < _linePool.Count; i++)
            {
                if (_polylines[i].widthM > 0f) continue; // stała szerokość — nie ruszaj
                float w = RouteMapPreviewMath.AdaptiveWidth(_orthoSize);
                _linePool[i].startWidth = _linePool[i].endWidth = w;
            }
        }

        /// <summary>Rozmiar markera (m) skalowany zoomem — żeby był widoczny i przy długiej trasie.</summary>
        private float AdaptiveMarkerSize() => Mathf.Clamp(_orthoSize * 0.03f, 120f, 5000f);

        private void RefreshMarkerSizes()
        {
            for (int i = 0; i < _markers.Count && i < _markerPool.Count; i++)
            {
                if (_markers[i].sizeM > 0f) continue; // stały rozmiar — nie ruszaj
                float s = AdaptiveMarkerSize();
                _markerPool[i].transform.localScale = new Vector3(s, 1f, s);
            }
        }

        private void RequestViewportTiles()
        {
            if (_tiles == null) return;
            // Throttle jest w ApplyView. Widoczny prostokąt świata (aspect=1 → half=orthoSize), z marginesem.
            float half = _orthoSize * 1.1f;
            var vp = new BBox
            {
                MinX = _center.x - half, MinY = _center.y - half,
                MaxX = _center.x + half, MaxY = _center.y + half
            };
            // LOD z WŁASNEGO zoomu podglądu — grube kafle przy oddaleniu (tanie), detal przy zbliżeniu.
            int lod = MapLod.LodForOrtho(_orthoSize);
            _tiles.RequestViewport(RouteMapPreviewMath.TilesCovering(vp), lod);
        }

        // ── Render (manual + okno na async kafle) ──────────────────

        private void RenderSoon()
        {
            if (_cam != null) CameraRenderUtil.Render(_cam);
            if (!isActiveAndEnabled) return;
            if (_settleCo != null) StopCoroutine(_settleCo);
            _settleCo = StartCoroutine(BuildAndRenderLoop());
        }

        // Buduje własne kafle podglądu po kilka na krok (I/O budżet) + re-renderuje aż kolejka pusta.
        private IEnumerator BuildAndRenderLoop()
        {
            var wait = new WaitForSeconds(RENDER_TICK_SEC);
            if (_cam != null) CameraRenderUtil.Render(_cam);
            int guard = 0;
            while (_tiles != null && _tiles.PendingCount > 0 && guard < 2000)
            {
                guard++;
                _tiles.BuildStep(TILES_PER_TICK);
                if (_cam != null) CameraRenderUtil.Render(_cam);
                yield return wait;
            }
            if (_cam != null) CameraRenderUtil.Render(_cam);
            _settleCo = null;
        }

        // ── Lifecycle ──────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_settleCo != null) { StopCoroutine(_settleCo); _settleCo = null; }
            _tiles?.Dispose();
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
            if (_lineMat != null) Destroy(_lineMat);
            if (_markerMat != null) Destroy(_markerMat);
            if (_panel != null) Destroy(_panel);
            if (gameObject != null) Destroy(gameObject);
        }

        void OnDestroy()
        {
            // Guard na wypadek zniszczenia bez Dispose (np. zamknięcie sceny)
            if (!_disposed)
            {
                _tiles?.Dispose();
                if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
                if (_lineMat != null) Destroy(_lineMat);
                if (_markerMat != null) Destroy(_markerMat);
            }
        }
    }
}
