using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RailwayManager.Core
{
    /// <summary>
    /// Zarządza przełączaniem między scenami Depot i MapScene.
    /// Obie sceny żyją jednocześnie (additive loading), logika działa zawsze.
    ///
    /// Separacja wizualna:
    /// - 3D obiekty: layer-based culling (MapScene na layerze 31, Depot na default)
    /// - UI: Canvas enable/disable (właściwie CanvasGroup, patrz <see cref="SetSceneUIEnabled"/>)
    /// - Input: EventSystem enable/disable
    /// - Rendering: Camera enable/disable
    ///
    /// 2026-05-13: split na 4 partials żeby okiełznać god-class (469 linii / 7 odpowiedzialności):
    /// - <see cref="SceneController"/> (ten plik) — main flow: Initialize/Switch/Reset/cooldown
    /// - `SceneController.Lighting.cs` — save/restore RenderSettings + 60-frame retry
    /// - `SceneController.Cameras.cs` — camera active + culling mask
    /// - `SceneController.Layers.cs` — layer recursion + UI/EventSystem toggling
    /// </summary>
    public static partial class SceneController
    {
        public enum GameScene { Depot, Map }

        public static GameScene ActiveScene { get; private set; } = GameScene.Depot;
        public static bool MapSceneLoaded { get; private set; } = false;

        /// <summary>
        /// True gdy otwarty jest fullscreen overlay blokujący interakcję z grą
        /// (np. DepotLocationPicker). Kamery Depot/Map używają tego żeby przerwać
        /// handling input'u na czas overlay'a.
        /// </summary>
        public static bool FullscreenOverlayOpen = false;

        private static bool isLoading = false;
        private static MonoBehaviour runner;

        /// <summary>
        /// Layer dla obiektów MapScene (kafle OSM + symulacja). Kamera Depot wyklucza ten layer.
        /// Layer 31 jest zwykle nieużywany w Unity.
        /// </summary>
        public const int MapLayer = 31;

        /// <summary>
        /// Layer nakładek DUŻEJ mapy (RoutePreviewOverlay i pokrewne) — oddzielony od kafli (31),
        /// żeby kamera mini-podglądu (RouteMapPreview) renderująca kafle NIE pokazywała tych
        /// nakładek. Renderowany przez kamerę MapScene, wykluczony z Depot i z podglądu.
        /// </summary>
        public const int MapOverlayLayer = 30;

        /// <summary>
        /// Layer zawartości TYLKO mini-podglądu (kompozytowalne polilinie/markery RouteMapPreview).
        /// Renderowany WYŁĄCZNIE przez kamerę podglądu — duża mapa i Depot go wykluczają, więc
        /// kolory podglądu nie zaśmiecają głównego widoku.
        /// </summary>
        public const int MapPreviewLayer = 29;

        public static void Initialize(MonoBehaviour coroutineRunner)
        {
            if (MapSceneLoaded || isLoading) return;
            isLoading = true;

            // VehicleLocationService + GameClock są bootstrap'owane przez
            // <see cref="Bootstrap.LateInit"/> w AfterSceneLoad — przed jakimkolwiek
            // Start() MonoBehaviour'a. Tutaj defensive guard EnsureExists w razie
            // niespodziewanego order (np. test scenes bypassujące Bootstrap).
            VehicleLocationService.EnsureExists();
            GameClock.EnsureExists();

            // Save Depot lighting BEFORE MapScene loads and overwrites it
            SaveDepotLighting();

            runner = coroutineRunner;
            coroutineRunner.StartCoroutine(LoadMapSceneAsync());
        }

        private static IEnumerator LoadMapSceneAsync()
        {
            Log.Info("[SceneController] Begin LoadMapSceneAsync");

            // Subscribe to scene loaded event to catch lighting changes immediately
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Guard against the case where MapScene is already loaded (e.g. user had it open
            // as edit-mode scene and hit Play on Depot — Unity carries it over as additive).
            var existing = SceneManager.GetSceneByName("MapScene");
            if (existing.IsValid() && existing.isLoaded)
            {
                Log.Info("[SceneController] MapScene already loaded — skipping LoadSceneAsync");
            }
            else
            {
                var asyncOp = SceneManager.LoadSceneAsync("MapScene", LoadSceneMode.Additive);
                asyncOp.allowSceneActivation = true;

                while (!asyncOp.isDone)
                    yield return null;
            }

            // Poczekaj klatkę żeby Start() się wykonał
            yield return null;

            Log.Info("[SceneController] MapScene load coroutine reached end — marking loaded");
            MapSceneLoaded = true;
            isLoading = false;

            // Ustaw wszystkie obiekty MapScene na MapLayer
            SetSceneLayer("MapScene", MapLayer);

            // Ukryj MapScene UI (Canvas, EventSystem)
            SetSceneUIEnabled("MapScene", false);

            // Wyłącz kamerę MapScene — logika mapy (symulacja pociągów, TileManager) ma działać
            // w tle zawsze, ale sama kamera nie renderuje i nie czyta inputu dopóki nie klikniesz
            // "Mapa 2D". CameraController.Update sprawdza cam.enabled i gate'uje input.
            SetMapCameraActive(false);

            // Set Depot as active scene (its lighting settings take priority)
            var depotScene = SceneManager.GetSceneByName("Depot");
            if (depotScene.IsValid())
                SceneManager.SetActiveScene(depotScene);

            // Kamera Depot nie renderuje MapLayer
            SetDepotCameraCulling();

            // Map camera renderuje TYLKO MapLayer
            SetMapCameraCulling();

            // Force restore lighting
            RestoreDepotLighting();

            // Keep restoring for several frames
            runner.StartCoroutine(KeepRestoringLighting());

            Log.Info("[SceneController] MapScene loaded additively (hidden, logic running)");
        }

        // Cooldown blokuje ping-pong: gdy klik na 'Mapa 2D' dociera do obu EventSystemów
        // (Depot i nowo-włączony Map) w zbliżonej klatce, drugie wywołanie Switch* jest
        // odrzucone. 0.3s to znacznie więcej niż długość pojedynczego clicka, ale na tyle
        // mało, żeby świadomy ponowny klik gracza nie był ignorowany.
        private static float lastSwitchRealtime = -999f;
        private const float switchCooldownSec = 0.3f;

        private static bool IsSwitchOnCooldown()
        {
            return Time.realtimeSinceStartup - lastSwitchRealtime < switchCooldownSec;
        }

        public static void SwitchToMap()
        {
            if (ActiveScene == GameScene.Map) return;

            if (IsSwitchOnCooldown())
            {
                Log.Info("[SceneController] SwitchToMap ignored — on cooldown (ping-pong prevention)");
                return;
            }

            // Dynamic check: the authoritative source of truth is whether MapScene is actually
            // loaded in SceneManager right now, not our stale MapSceneLoaded flag (which could
            // be out of date if the async load coroutine was interrupted).
            var mapScene = SceneManager.GetSceneByName("MapScene");
            if (!mapScene.IsValid() || !mapScene.isLoaded)
            {
                Log.Warn("[SceneController] SwitchToMap called but MapScene not in SceneManager");
                return;
            }
            MapSceneLoaded = true;
            lastSwitchRealtime = Time.realtimeSinceStartup;

            // Save Depot lighting before MapScene overwrites it
            SaveDepotLighting();

            // CRITICAL: disable source scene UI IMMEDIATELY, enable target scene UI on NEXT FRAME.
            // Without the 1-frame delay, the still-pending pointer state from the click that
            // triggered this switch gets processed by the newly-enabled EventSystem and "clicks"
            // whatever button happens to be under the cursor in the target scene — typically
            // the Map/Depot nav button which is at the same screen position, so the switch
            // immediately reverses itself. See SetSceneUIDelayed for details.
            SetSceneUIEnabled("Depot", false);

            SetMapCameraActive(true);
            SetDepotCameraActive(false);

            ApplyMapLighting();

            ActiveScene = GameScene.Map;
            Log.Info("[SceneController] Switched to MapScene");

            if (runner != null)
                runner.StartCoroutine(SetSceneUIDelayed("MapScene", true));
            else
                SetSceneUIEnabled("MapScene", true);
        }

        /// <summary>
        /// Forces ActiveScene value without performing any scene transition.
        /// Used by MapScene standalone bootstrap when Depot is not in the session — makes
        /// components that check ActiveScene (legacy) behave as if SwitchToMap happened.
        /// </summary>
        public static void ForceActiveScene(GameScene scene)
        {
            ActiveScene = scene;
            if (scene == GameScene.Map)
                MapSceneLoaded = true;
        }

        public static void SwitchToDepot()
        {
            if (ActiveScene == GameScene.Depot) return;

            if (IsSwitchOnCooldown())
            {
                Log.Info("[SceneController] SwitchToDepot ignored — on cooldown (ping-pong prevention)");
                return;
            }

            lastSwitchRealtime = Time.realtimeSinceStartup;

            SetSceneUIEnabled("MapScene", false);

            SetDepotCameraActive(true);
            SetMapCameraActive(false);

            RestoreDepotLighting();

            ActiveScene = GameScene.Depot;
            Log.Info("[SceneController] Switched to Depot");

            if (runner != null)
                runner.StartCoroutine(SetSceneUIDelayed("Depot", true));
            else
                SetSceneUIEnabled("Depot", true);
        }

        /// <summary>
        /// Flaga: popup kreatora rozkładów jest otwarty — blokuje input kamery depot.
        ///
        /// Stateful (read-many, write-many) — nie one-shot intent. Wielu writerów
        /// (TimetableCreatorUI/CategoryEditorUI/CirculationListUI/TimetableListUI Show/Hide),
        /// czytany przez DepotOrbitCamera dla input gating. Zostaje jako flag (kontrast do
        /// 5 Pending* flag wymienionych na <see cref="UIIntents"/> 2026-05-10).
        /// </summary>
        public static bool TimetablePopupOpen { get; set; }

        /// <summary>
        /// Frame w którym ESC został skonsumowany przez UI popup. PauseMenuUI sprawdza
        /// to w LateUpdate i nie otwiera menu jeśli ESC już został użyty w tej klatce.
        /// Shared między Depot i Timetable (oba asmdef mają dostęp do Core).
        /// </summary>
        public static int LastEscConsumedFrame = -1;

        public static void Reset()
        {
            // BUG-027: zatrzymaj uciekające coroutines (LoadMapSceneAsync, KeepRestoringLighting)
            // żeby nie manipulowały RenderSettings po reset (np. wyjście do MainMenu).
            if (runner != null)
            {
                runner.StopAllCoroutines();
            }
            // Plus odhokuj OnSceneLoaded (może zostać po przerwanym LoadMapSceneAsync).
            SceneManager.sceneLoaded -= OnSceneLoaded;

            MapSceneLoaded = false;
            isLoading = false;
            ActiveScene = GameScene.Depot;
            FullscreenOverlayOpen = false;
            TimetablePopupOpen = false;
            LastEscConsumedFrame = -1;
            lastSwitchRealtime = -999f;
            depotLightingSaved = false;
            runner = null;
            PauseStack.Clear();  // wyczyść stuck popup owners przy wyjściu do MainMenu
        }
    }
}
