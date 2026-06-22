using UnityEngine;
using RailwayManager.Core;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DepotSystem; // PauseMenuUI
using RailwayManager.SharedUI;
using TMPro;

namespace MapSystem
{
    /// <summary>
    /// Singleton UI Manager dla sceny mapy.
    /// Automatycznie tworzy Canvas, EventSystem, TopBar, ZoomSlider, TrainList, PauseMenu.
    /// </summary>
    public class MapUIManager : MonoBehaviour
    {
        public static MapUIManager Instance { get; private set; }

        [HideInInspector] public TopBarUI topBar;
        [HideInInspector] public MapZoomSliderUI zoomSlider;
        [HideInInspector] public MapTrainListUI trainList;
        [HideInInspector] public PauseMenuUI pauseMenu;
        [HideInInspector] public Canvas canvas;

        private EventSystem _eventSystem;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildCanvas();

            // Standalone mode bootstrap: if Depot is not loaded in this session, we're running
            // MapScene by itself (direct play from editor). Force-enable UI and mark Map as
            // the active scene so components stop waiting for SceneController.SwitchToMap that
            // will never come.
            //
            // NOTE: we never toggle Canvas.enabled here — SceneController używa CanvasGroup
            // do ukrywania UI (Canvas.enabled cycle psuje InputField.GenerateCaret).
            var depotScene = SceneManager.GetSceneByName("Depot");
            bool depotLoaded = depotScene.IsValid() && depotScene.isLoaded;
            if (!depotLoaded)
            {
                Log.Info("[MapUIManager] Standalone mode — Depot not loaded, enabling UI immediately");
                if (_eventSystem != null) _eventSystem.enabled = true;
                RailwayManager.Core.SceneController.ForceActiveScene(
                    RailwayManager.Core.SceneController.GameScene.Map);
            }
        }

        public bool IsPointerOverUI()
        {
            // Delegate to EventSystem's own enabled state — SceneController toggles it per scene.
            // Intentionally NOT checking static SceneController.ActiveScene (unreliable in standalone).
            if (_eventSystem != null && !_eventSystem.enabled)
                return true;
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void BuildCanvas()
        {
            // 1. Canvas
            GameObject canvasObj = new GameObject("MapCanvas");
            canvasObj.transform.SetParent(transform, false);

            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            UITheme.ApplyCanvasScaler(scaler);

            canvasObj.AddComponent<GraphicRaycaster>();

            // 2. EventSystem — MapScene MUST have its own (Depot's is disabled when map is active)
            GameObject eventSystemObj = new GameObject("EventSystem_Map");
            eventSystemObj.transform.SetParent(transform, false);
            _eventSystem = eventSystemObj.AddComponent<EventSystem>();
            eventSystemObj.AddComponent<InputSystemUIInputModule>();
            // Start disabled — SceneController enables it when switching to map.
            // Standalone mode bootstrap in Awake() re-enables it when Depot isn't loaded.
            _eventSystem.enabled = false;

            // 3. TopBar
            GameObject topBarObj = new GameObject("TopBar");
            topBarObj.transform.SetParent(canvasObj.transform, false);
            topBar = topBarObj.AddComponent<TopBarUI>();

            // 4. ZoomSlider (prawa strona)
            GameObject zoomObj = new GameObject("ZoomSlider");
            zoomObj.transform.SetParent(canvasObj.transform, false);
            zoomSlider = zoomObj.AddComponent<MapZoomSliderUI>();

            // 5. TrainList (lewa strona)
            GameObject trainListObj = new GameObject("TrainList");
            trainListObj.transform.SetParent(canvasObj.transform, false);
            trainList = trainListObj.AddComponent<MapTrainListUI>();

            // 6. PauseMenu
            GameObject pauseMenuObj = new GameObject("PauseMenu", typeof(RectTransform));
            pauseMenuObj.transform.SetParent(canvasObj.transform, false);
            pauseMenu = pauseMenuObj.AddComponent<PauseMenuUI>();

            // 7. OSM attribution — zawsze widoczny, klikalny kredyt (wymóg ODbL / OSMF
            //    Attribution Guidelines; patrz docs/DATA_LICENSES.md). MapCanvas to
            //    ScreenSpaceOverlay (UI), niezależny od kafli mapy — żaden zoom/pan/LOD go nie ukryje.
            BuildOsmAttribution(canvasObj.transform);

            // Timetable UI (RouteBuildPanel, StateMachine, Initializer) tworzone przez
            // TimetableInitializer.EnsureBootstrapped() — żyje w Timetable asmdef
            // który referuje Map, ale Map NIE referuje Timetable (unikamy cyklu).

            Log.Info("[MapUIManager] Canvas built procedurally");
        }

        // Overlay atrybucji OSM — lewy dolny róg (konwencja map; prawy zajmuje ZoomSlider,
        // góra TopBar). Klikalny link do openstreetmap.org/copyright. "© OpenStreetMap" to
        // nazwa własna (nie tłumaczona). raycastTarget tekstu = false, klik łapie Button na tle.
        private void BuildOsmAttribution(Transform canvasParent)
        {
            var go = new GameObject("OSMAttribution", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(canvasParent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(10f, 10f);
            rt.sizeDelta = new Vector2(200f, 26f);

            // Pólprzezroczyste tlo dla kontrastu/czytelnosci na dowolnym kolorze mapy.
            UITheme.ApplySurface(go.GetComponent<Image>(), UITheme.WithAlpha(UITheme.TopBarInset, 0.85f), UIShapePreset.Inset);

            var tmp = UIPrimitives.MakeTMP("Label", go.transform, 13f, UIThemeTextRole.Primary, TextAlignmentOptions.Center);
            tmp.text = "© OpenStreetMap";
            tmp.raycastTarget = false;
            UIPrimitives.Stretch(tmp.rectTransform);

            go.GetComponent<Button>().onClick.AddListener(
                () => Application.OpenURL("https://www.openstreetmap.org/copyright"));
        }
    }
}
