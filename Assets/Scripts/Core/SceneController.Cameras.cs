using UnityEngine;
using UnityEngine.SceneManagement;

namespace RailwayManager.Core
{
    /// <summary>
    /// Partial dla <see cref="SceneController"/> — operacje na kamerach scen Depot/Map.
    ///
    /// Camera active vs culling mask: dwa niezależne mechanizmy.
    /// - active (cam.enabled) decyduje czy kamera renderuje
    /// - cullingMask decyduje co kamera widzi (per-layer bitmask)
    ///
    /// Depot camera renderuje wszystko poza MapLayer, Map camera renderuje TYLKO MapLayer
    /// + UI layer. Plus per-scene cam.enabled toggle przy switchu.
    /// </summary>
    public static partial class SceneController
    {
        /// <summary>Builtin Unity UI layer.</summary>
        public const int UiLayer = 5;

        // --- Czyste helpery masek (testowalne w EditMode, bez sceny/kamer) ---

        /// <summary>
        /// Maska kamery DUŻEJ mapy: kafle (MapLayer) + UI + nakładki dużej mapy (MapOverlayLayer).
        /// NIE zawiera MapPreviewLayer (zawartość mini-podglądu zostaje poza dużą mapą).
        /// </summary>
        public static int MapCameraCullingMask =>
            (1 << MapLayer) | (1 << UiLayer) | (1 << MapOverlayLayer);

        /// <summary>
        /// Maska kamery MINI-PODGLĄDU: WYŁĄCZNIE MapPreviewLayer (własne kafle OSM podglądu we
        /// własnym LOD + linia/markery). NIE renderuje MapLayer (głównych kafli) — podgląd ma
        /// własne kafle, więc unikamy współdzielenia globalnego LOD i podwójnej geometrii.
        /// Bez UI (rekurencja RawImage) i bez MapOverlayLayer (linia dużej mapy nie przecieka).
        /// </summary>
        public static int PreviewCameraCullingMask =>
            (1 << MapPreviewLayer);

        /// <summary>
        /// Usuwa z maski wszystkie warstwy „mapowe" (kafle + nakładki + podgląd) — dla kamery Depot,
        /// żeby świat 2D ani podgląd nie renderowały się w widoku zajezdni 3D.
        /// </summary>
        public static int ApplyDepotCullingExclusions(int mask) =>
            mask & ~((1 << MapLayer) | (1 << MapOverlayLayer) | (1 << MapPreviewLayer));

        /// <summary>
        /// Kamera Depot wyklucza warstwy mapowe (kafle + nakładki dużej mapy + zawartość podglądu).
        /// </summary>
        private static void SetDepotCameraCulling()
        {
            var depotScene = SceneManager.GetSceneByName("Depot");
            if (!depotScene.IsValid()) return;

            foreach (var root in depotScene.GetRootGameObjects())
            {
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                {
                    cam.cullingMask = ApplyDepotCullingExclusions(cam.cullingMask);
                }
            }
        }

        /// <summary>
        /// Kamera MapScene renderuje kafle (MapLayer) + UI + nakładki dużej mapy (MapOverlayLayer).
        /// Nie widzi Depot (default layer) ani zawartości mini-podglądu (MapPreviewLayer).
        /// </summary>
        private static void SetMapCameraCulling()
        {
            var mapScene = SceneManager.GetSceneByName("MapScene");
            if (!mapScene.IsValid()) return;

            foreach (var root in mapScene.GetRootGameObjects())
            {
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                {
                    cam.cullingMask = MapCameraCullingMask;
                }
            }
        }

        private static void SetDepotCameraActive(bool enabled)
        {
            var scene = SceneManager.GetSceneByName("Depot");
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                    cam.enabled = enabled;
            }
        }

        private static void SetMapCameraActive(bool enabled)
        {
            var scene = SceneManager.GetSceneByName("MapScene");
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                    cam.enabled = enabled;
            }
        }
    }
}
