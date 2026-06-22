using System.Collections;
using System.Collections.Generic;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;
using RailwayManager.SharedUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DepotSystem
{
    /// <summary>
    /// Real-time minimapa zajezdni (Q2 depot-visual-direction.md 2026-05-17).
    /// Architektura: ortho camera child + RenderTexture + UI RawImage.
    /// Pozycja: prawy górny róg, 400px szer, height adaptywny do current bounds aspect.
    ///
    /// Sub-steps implementacji (✅ DONE):
    ///  ✅ 1. Bare-bones: ortho cam + RT + RawImage statyczny
    ///  ✅ 2. Adaptive aspect: OnBoundsChanged → fit ortho + resize UI height
    ///  ✅ 3. Frustum ramka — biała ramka wskazująca camera view
    ///  ✅ 4. Markery pociągów (żółte) — pull z ConsistMarker co 10Hz
    ///  ✅ 5. Markery pracowników (niebieskie) — pull z EmployeeVisual co 10Hz
    ///  ✅ 6. Click handler — UV → world → FocusOn(instant)
    ///  ✅ 7. Reactive position — przesuwa się przy kolizji z UI (BuildRoom mode)
    /// </summary>
    public class DepotMinimapUI : MonoBehaviour, IPointerClickHandler
    {
        // ── Konfiguracja ────────────────────────────────────────────
        private const int RT_WIDTH = 512;
        private const int RT_HEIGHT = 128;       // bazowy, gracz nie widzi
        private const float UI_WIDTH = 400f;
        private const float UI_PADDING = 20f;            // offset poziomy od prawej krawędzi
        private const float UI_TOP_OFFSET = 90f;          // offset Y od top — pod MaintenanceAlertsUI strip (Y=-44, height 36 → kończy się Y=-80, +10 gap)
        private const float RENDER_REFRESH_INTERVAL = 1.0f;  // 1Hz background render
        private const int MARKER_UPDATE_FRAMES = 5;          // ~10Hz przy 50Hz physics
        private const float ROOM_BUILD_PANEL_OFFSET = 280f;  // przesuń w lewo o 280px gdy BuildRoom active

        private static DepotMinimapUI _instance;
        public static DepotMinimapUI Instance => _instance;

        private Camera _orthoCamera;
        private RenderTexture _renderTexture;
        private GameObject _panel;
        private RectTransform _panelRect;
        private RectTransform _mapAreaRect;       // inner RawImage area (gdzie UV coords mapują)
        private RawImage _rawImage;
        private Coroutine _renderCoroutine;

        // Frustum ramka (Step 3) — 4 lines: top/right/bottom/left
        private RectTransform[] _frustumLines = new RectTransform[4];

        // Markery (Step 4+5) — pool
        private readonly List<RectTransform> _trainMarkers = new();
        private readonly List<RectTransform> _employeeMarkers = new();
        private GameObject _markersContainer;
        private int _markerTickCounter;

        private bool _boundsEventSubscribed;
        private bool _toolEventSubscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != "Depot") return;
            if (FindAnyObjectByType<DepotMinimapUI>() != null) return;

            var go = new GameObject("DepotMinimapUI (auto-spawn)");
            go.AddComponent<DepotMinimapUI>();
        }

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            BuildOrthoCamera();
            BuildUI();
        }

        void Start()
        {
            var gg = DepotServices.Get<GroundGenerator>();
            if (gg != null)
            {
                gg.OnBoundsChanged += OnBoundsChanged;
                _boundsEventSubscribed = true;
            }

            // Step 7: subscribe na DepotUIManager events dla reactive position
            TrySubscribeUIEvents();
            DepotUIManager.OnReady += TrySubscribeUIEvents;

            FitToCurrentBounds();
            _renderCoroutine = StartCoroutine(BackgroundRenderLoop());
        }

        void LateUpdate()
        {
            // Ustąp pełnoekranowym popupom (kreator rozkładów / pickery) — minimapa nie ma prawa
            // wisieć nad nimi w prawym-górnym rogu.
            bool popupOpen = SceneController.TimetablePopupOpen || SceneController.FullscreenOverlayOpen;
            if (_panel != null && _panel.activeSelf == popupOpen)
                _panel.SetActive(!popupOpen);
            if (popupOpen) return;

            // Step 3: Frustum ramka — update co frame (cheap, 4 RectTransforms)
            UpdateFrustumOverlay();

            // Step 4+5: Markery pociągów + pracowników — co 10Hz
            _markerTickCounter++;
            if (_markerTickCounter >= MARKER_UPDATE_FRAMES)
            {
                _markerTickCounter = 0;
                UpdateMarkers();
            }
        }

        void OnDestroy()
        {
            if (_boundsEventSubscribed)
            {
                var gg = DepotServices.Get<GroundGenerator>();
                if (gg != null) gg.OnBoundsChanged -= OnBoundsChanged;
                _boundsEventSubscribed = false;
            }
            DepotUIManager.OnReady -= TrySubscribeUIEvents;
            UnsubscribeUIEvents();
            if (_renderCoroutine != null) StopCoroutine(_renderCoroutine);
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }
            if (_instance == this) _instance = null;
        }

        // ── Step 7: Reactive position (UI collision avoidance) ────────

        private void TrySubscribeUIEvents()
        {
            if (_toolEventSubscribed) return;
            var ui = DepotUIManager.Instance;
            if (ui == null) return;

            ui.OnToolChanged += OnToolChanged;
            _toolEventSubscribed = true;
            // Initial sync — może już jest w BuildRoom mode
            OnToolChanged(ui.CurrentTool);
        }

        private void UnsubscribeUIEvents()
        {
            if (!_toolEventSubscribed) return;
            var ui = DepotUIManager.Instance;
            if (ui != null) ui.OnToolChanged -= OnToolChanged;
            _toolEventSubscribed = false;
        }

        private void OnToolChanged(ToolMode mode)
        {
            if (_panelRect == null) return;
            // RoomBuildPanelUI pojawia się gdy gracz wybrał ToolMode.BuildRoom (panel po prawej stronie)
            // → przesuń minimapę o ROOM_BUILD_PANEL_OFFSET w lewo żeby nie kolidowały.
            bool collides = (mode == ToolMode.BuildRoom);
            float xOffset = collides ? -(UI_PADDING + ROOM_BUILD_PANEL_OFFSET) : -UI_PADDING;
            _panelRect.anchoredPosition = new Vector2(xOffset, -UI_TOP_OFFSET);
        }

        private void BuildOrthoCamera()
        {
            var camGo = new GameObject("MinimapCamera");
            camGo.transform.SetParent(transform, false);
            _orthoCamera = camGo.AddComponent<Camera>();
            _orthoCamera.orthographic = true;
            _orthoCamera.clearFlags = CameraClearFlags.SolidColor;
            _orthoCamera.backgroundColor = new Color(0.2f, 0.25f, 0.2f, 1f);  // ciemna trawa fallback
            _orthoCamera.cullingMask = ~(1 << 5);  // wszystko OPRÓCZ layer UI (5)
            _orthoCamera.nearClipPlane = 0.1f;
            _orthoCamera.farClipPlane = 500f;
            _orthoCamera.enabled = false;       // manual render only (GPU savings)

            _renderTexture = new RenderTexture(RT_WIDTH, RT_HEIGHT, 16, RenderTextureFormat.ARGB32);
            _renderTexture.name = "DepotMinimapRT";
            _orthoCamera.targetTexture = _renderTexture;
        }

        private void BuildUI()
        {
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                Log.Warn("[DepotMinimapUI] No Canvas in scene, minimap not created");
                return;
            }

            _panel = new GameObject("DepotMinimapPanel");
            _panel.transform.SetParent(canvas.transform, false);
            _panelRect = _panel.AddComponent<RectTransform>();
            // Anchor top-right
            _panelRect.anchorMin = new Vector2(1f, 1f);
            _panelRect.anchorMax = new Vector2(1f, 1f);
            _panelRect.pivot = new Vector2(1f, 1f);
            _panelRect.anchoredPosition = new Vector2(-UI_PADDING, -UI_TOP_OFFSET);
            _panelRect.sizeDelta = new Vector2(UI_WIDTH, 80f);  // default Tier 4 aspect 5:1

            // Background (border) — dark frame
            var bg = _panel.AddComponent<Image>();
            bg.color = UITheme.OverlayPanelStrong;

            // RawImage z RenderTexture (inset 2px dla border efektu)
            var imgGo = new GameObject("MinimapImage");
            imgGo.transform.SetParent(_panel.transform, false);
            _mapAreaRect = imgGo.AddComponent<RectTransform>();
            _mapAreaRect.anchorMin = Vector2.zero;
            _mapAreaRect.anchorMax = Vector2.one;
            _mapAreaRect.offsetMin = new Vector2(2f, 2f);
            _mapAreaRect.offsetMax = new Vector2(-2f, -2f);
            _rawImage = imgGo.AddComponent<RawImage>();
            _rawImage.texture = _renderTexture;
            _rawImage.raycastTarget = true;  // click handler

            // Step 3: Frustum ramka — 4 białe linie (top/right/bottom/left)
            BuildFrustumOverlay();

            // Step 4+5: Markery container (sprites pool)
            _markersContainer = new GameObject("MarkersContainer");
            _markersContainer.transform.SetParent(imgGo.transform, false);
            var markersRt = _markersContainer.AddComponent<RectTransform>();
            markersRt.anchorMin = Vector2.zero;
            markersRt.anchorMax = Vector2.one;
            markersRt.offsetMin = Vector2.zero;
            markersRt.offsetMax = Vector2.zero;
            // markers nie blokują click — gracz może kliknąć "przez" marker
            var markersCanvasGroup = _markersContainer.AddComponent<CanvasGroup>();
            markersCanvasGroup.blocksRaycasts = false;
            markersCanvasGroup.interactable = false;
        }

        private void BuildFrustumOverlay()
        {
            if (_mapAreaRect == null) return;
            var frustumRoot = new GameObject("FrustumOverlay");
            frustumRoot.transform.SetParent(_mapAreaRect, false);
            var rootRt = frustumRoot.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;
            var rootGroup = frustumRoot.AddComponent<CanvasGroup>();
            rootGroup.blocksRaycasts = false;
            rootGroup.interactable = false;

            // 4 lines: top/right/bottom/left jako UI Image
            for (int i = 0; i < 4; i++)
            {
                var lineGo = new GameObject($"FrustumLine{i}");
                lineGo.transform.SetParent(frustumRoot.transform, false);
                var lineRt = lineGo.AddComponent<RectTransform>();
                lineRt.anchorMin = new Vector2(0.5f, 0.5f);
                lineRt.anchorMax = new Vector2(0.5f, 0.5f);
                lineRt.pivot = new Vector2(0f, 0.5f);  // pivot na lewym końcu → easy length/rotation
                var img = lineGo.AddComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0.7f);
                img.raycastTarget = false;
                _frustumLines[i] = lineRt;
            }
        }

        // ── Step 3: Frustum overlay update ────────────────────────

        private void UpdateFrustumOverlay()
        {
            if (_frustumLines == null || _frustumLines[0] == null) return;
            if (_mapAreaRect == null) return;

            var depotCam = DepotServices.Get<DepotOrbitCamera>();
            if (depotCam == null) return;
            var cam = depotCam.GetComponent<Camera>();
            if (cam == null) { HideFrustum(); return; }

            // Project 4 viewport corners na world Y=0 plane → minimap UV → UI local position
            Vector2[] uiCorners = new Vector2[4];
            bool allValid = true;
            for (int i = 0; i < 4; i++)
            {
                float u = (i == 1 || i == 2) ? 1f : 0f;
                float v = (i == 2 || i == 3) ? 1f : 0f;
                Ray ray = cam.ViewportPointToRay(new Vector3(u, v, 0));
                if (Mathf.Abs(ray.direction.y) < 0.001f) { allValid = false; break; }
                float t = -ray.origin.y / ray.direction.y;
                if (t < 0) { allValid = false; break; }
                Vector3 worldPt = ray.origin + ray.direction * t;
                uiCorners[i] = WorldToMinimapLocalPos(worldPt);
            }

            if (!allValid) { HideFrustum(); return; }

            // 4 lines: 0→1 (top), 1→2 (right), 2→3 (bottom), 3→0 (left)
            DrawLine(_frustumLines[0], uiCorners[0], uiCorners[1]);
            DrawLine(_frustumLines[1], uiCorners[1], uiCorners[2]);
            DrawLine(_frustumLines[2], uiCorners[2], uiCorners[3]);
            DrawLine(_frustumLines[3], uiCorners[3], uiCorners[0]);
        }

        private void HideFrustum()
        {
            for (int i = 0; i < 4; i++)
                if (_frustumLines[i] != null) _frustumLines[i].sizeDelta = Vector2.zero;
        }

        private void DrawLine(RectTransform line, Vector2 start, Vector2 end)
        {
            Vector2 delta = end - start;
            float length = delta.magnitude;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            line.anchoredPosition = start;
            line.sizeDelta = new Vector2(length, 1.5f);
            line.localRotation = Quaternion.Euler(0, 0, angle);
        }

        // ── Step 4+5: Markery pociągów i pracowników ──────────────

        private void UpdateMarkers()
        {
            UpdateTrainMarkers();
            UpdateEmployeeMarkers();
        }

        private void UpdateTrainMarkers()
        {
            var consists = FindObjectsByType<ConsistMarker>(FindObjectsInactive.Exclude);
            EnsureMarkerPoolSize(_trainMarkers, consists.Length, new Color(1f, 0.85f, 0.2f), "TrainMarker");
            for (int i = 0; i < _trainMarkers.Count; i++)
            {
                if (i < consists.Length)
                {
                    Vector2 pos = WorldToMinimapLocalPos(consists[i].transform.position);
                    _trainMarkers[i].anchoredPosition = pos;
                    _trainMarkers[i].gameObject.SetActive(true);
                }
                else
                {
                    _trainMarkers[i].gameObject.SetActive(false);
                }
            }
        }

        private void UpdateEmployeeMarkers()
        {
            // Cross-asmdef bridge: Core registry zawiera pozycje pracowników
            // (Depot asmdef nie referuje Personnel, więc nie ma direct dostępu do EmployeeVisual).
            // Personnel.EmployeeWalkSimulator.SpawnEmployee → MinimapAgentRegistry.Register.
            int count = MinimapAgentRegistry.GetCount(MinimapAgentRegistry.AgentType.Employee);
            EnsureMarkerPoolSize(_employeeMarkers, count, new Color(0.3f, 0.6f, 1f), "EmployeeMarker");

            int idx = 0;
            foreach (var pos in MinimapAgentRegistry.GetPositions(MinimapAgentRegistry.AgentType.Employee))
            {
                if (idx >= _employeeMarkers.Count) break;
                Vector2 uiPos = WorldToMinimapLocalPos(pos);
                _employeeMarkers[idx].anchoredPosition = uiPos;
                _employeeMarkers[idx].gameObject.SetActive(true);
                idx++;
            }
            for (int i = idx; i < _employeeMarkers.Count; i++)
                _employeeMarkers[i].gameObject.SetActive(false);
        }

        private void EnsureMarkerPoolSize(List<RectTransform> pool, int needed, Color color, string namePrefix)
        {
            if (_markersContainer == null) return;
            while (pool.Count < needed)
            {
                var go = new GameObject($"{namePrefix}_{pool.Count}");
                go.transform.SetParent(_markersContainer.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(6f, 6f);
                var img = go.AddComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
                pool.Add(rt);
            }
        }

        // ── Step 6: Click handler ─────────────────────────────────

        public void OnPointerClick(PointerEventData eventData)
        {
            if (_mapAreaRect == null) return;
            // Convert screen point → local point w mapAreaRect
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _mapAreaRect, eventData.position, eventData.pressEventCamera, out var localPoint))
                return;

            // localPoint w pivot middle (anchor 0-1, pivot 0.5) — przesuwamy na (0,0)-(width,height)
            Rect r = _mapAreaRect.rect;
            float u = (localPoint.x - r.xMin) / r.width;
            float v = (localPoint.y - r.yMin) / r.height;
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            // UV → world position (camera ortho projection)
            var gg = DepotServices.Get<GroundGenerator>();
            if (gg == null) return;
            var bounds = gg.BuildableArea;
            // Account for ortho aspect mismatch: RT pokazuje fragment world, mamy ortho size
            // ale UI mapuje 0-1 na cały mapAreaRect. Założenie: minimap UI ma proporcje
            // równoważne current bounds (FitToCurrentBounds zapewnia to). → linear map.
            float worldX = Mathf.Lerp(bounds.min.x, bounds.max.x, u);
            float worldZ = Mathf.Lerp(bounds.min.z, bounds.max.z, v);
            Vector3 worldPos = new Vector3(worldX, 0f, worldZ);

            var depotCam = DepotServices.Get<DepotOrbitCamera>();
            if (depotCam != null)
            {
                depotCam.FocusOn(worldPos, instant: true);
                Log.Info($"[DepotMinimapUI] Click → camera jump to ({worldX:F0}, {worldZ:F0})");
            }
        }

        // ── Helpers ───────────────────────────────────────────────

        /// <summary>
        /// Mapuje world position (X,Z) na local anchored position w mapAreaRect.
        /// Wynikowa pozycja jest w lokalnym układzie z anchor (0.5, 0.5) i pivot (0.5, 0.5),
        /// czyli (0,0) = środek minimapy, (-width/2, -height/2) = lewy-dolny.
        /// </summary>
        private Vector2 WorldToMinimapLocalPos(Vector3 worldPos)
        {
            var gg = DepotServices.Get<GroundGenerator>();
            if (gg == null || _mapAreaRect == null) return Vector2.zero;
            var bounds = gg.BuildableArea;

            // U,V (0-1) w bounds
            float u = Mathf.InverseLerp(bounds.min.x, bounds.max.x, worldPos.x);
            float v = Mathf.InverseLerp(bounds.min.z, bounds.max.z, worldPos.z);

            // Local position w mapAreaRect — anchor pivot middle
            Rect r = _mapAreaRect.rect;
            float localX = Mathf.Lerp(r.xMin, r.xMax, u);
            float localY = Mathf.Lerp(r.yMin, r.yMax, v);
            return new Vector2(localX, localY);
        }

        // ── Adaptive aspect (Step 2) ──────────────────────────────

        private void OnBoundsChanged()
        {
            FitToCurrentBounds();
            // Force re-render after expand (świat zmienił się dramatically)
            RenderOrthoNow();
        }

        /// <summary>
        /// Dopasuj ortho cam + UI height do current bounds.
        /// - Ortho camera position = center of buildable area, patrzy w dół
        /// - Ortho size = max(lengthX, widthZ) / 2 (fit całość)
        /// - UI height = UI_WIDTH * (widthZ / lengthX) — aspect ratio current bounds
        /// </summary>
        private void FitToCurrentBounds()
        {
            var gg = DepotServices.Get<GroundGenerator>();
            if (gg == null || _orthoCamera == null || _panelRect == null) return;

            var bounds = gg.BuildableArea;
            float lengthX = gg.CurrentLengthX;
            float widthZ = gg.CurrentWidthZ;

            // Ortho cam — patrzy z góry w dół na center bounds
            _orthoCamera.transform.position = new Vector3(bounds.center.x, 100f, bounds.center.z);
            _orthoCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            // Ortho size — half-height widoku. Chcemy fit length na width RT_WIDTH (512px),
            // a width na RT_HEIGHT. Ale RT jest 512×128 (4:1), a current bounds aspect może
            // być różne (2.67:1 do 5:1). Lepiej: ortho size fit dłuższego wymiaru, RT cropuje
            // strony lub gracz nie widzi czarnych krawędzi bo UI rescale.
            //
            // Strategia: ortho cam orthographicSize = widthZ / 2 (fit width Z na RT height).
            // length X może być szerszy niż RT pokazuje → ale RT cropuje (1:1 ortho per pixel).
            // Lepiej: ortho size = max(lengthX, widthZ * (RT_WIDTH/RT_HEIGHT)) / 2 — fit całość.
            float rtAspect = (float)RT_WIDTH / RT_HEIGHT;        // 4:1
            float worldAspect = lengthX / widthZ;                 // 2.67:1 do 5:1
            float orthoSize;
            if (worldAspect > rtAspect)
            {
                // World szerszy niż RT — fit po szerokości X, height nadmiarowy
                orthoSize = lengthX / (2f * rtAspect);
            }
            else
            {
                // World węższy niż RT — fit po szerokości Z
                orthoSize = widthZ / 2f;
            }
            _orthoCamera.orthographicSize = orthoSize;
            _orthoCamera.aspect = rtAspect;

            // UI height — proportional do world aspect (więc minimapa wygląda tak jak world)
            float uiHeight = UI_WIDTH * (widthZ / lengthX);
            _panelRect.sizeDelta = new Vector2(UI_WIDTH, uiHeight);

            Log.Info($"[DepotMinimapUI] Fit bounds: {lengthX:F0}×{widthZ:F0}m, " +
                     $"ortho size={orthoSize:F1}, UI {UI_WIDTH:F0}×{uiHeight:F0}px");
        }

        // ── Background render loop (świat statyczny, refresh co 1s) ──

        private IEnumerator BackgroundRenderLoop()
        {
            var wait = new WaitForSeconds(RENDER_REFRESH_INTERVAL);
            // Pierwszy render natychmiast (gracz zobaczy minimapę odbiorą)
            yield return null;
            RenderOrthoNow();
            while (true)
            {
                yield return wait;
                RenderOrthoNow();
            }
        }

        private void RenderOrthoNow()
        {
            if (_orthoCamera != null) CameraRenderUtil.Render(_orthoCamera);
        }

        [ContextMenu("Force Re-render")]
        private void ForceRerender() => RenderOrthoNow();
    }
}
