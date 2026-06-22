using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RailwayManager.Core
{
    /// <summary>
    /// Partial dla <see cref="SceneController"/> — lighting save/restore + retry hack.
    ///
    /// Unity stosuje lighting w scenie asynchronicznie po `LoadSceneAsync` — gdy MapScene
    /// (active = Depot, additively załadowany Map) wciska swoje RenderSettings przez
    /// kilka klatek po load, musimy je nadpisywać. Stąd 60-frame retry loop.
    /// </summary>
    public static partial class SceneController
    {
        // Saved Depot lighting state
        private static Material savedSkybox;
        private static Color savedAmbientLight;
        private static UnityEngine.Rendering.AmbientMode savedAmbientMode;
        private static float savedAmbientIntensity;
        private static bool savedFog;
        private static bool depotLightingSaved = false;

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Whenever any scene loads, if we're on Depot — force Depot lighting
            if (ActiveScene == GameScene.Depot && depotLightingSaved)
            {
                RestoreDepotLighting();
            }
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>
        /// Aggressively restore Depot lighting for 60 frames after MapScene load.
        /// Unity applies scene lighting data asynchronously — we must keep overriding.
        /// </summary>
        private static IEnumerator KeepRestoringLighting()
        {
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                if (ActiveScene == GameScene.Depot && depotLightingSaved)
                    RestoreDepotLighting();
            }
        }

        private static void SaveDepotLighting()
        {
            savedSkybox = RenderSettings.skybox;
            savedAmbientLight = RenderSettings.ambientLight;
            savedAmbientMode = RenderSettings.ambientMode;
            savedAmbientIntensity = RenderSettings.ambientIntensity;
            savedFog = RenderSettings.fog;
            depotLightingSaved = true;
        }

        private static void RestoreDepotLighting()
        {
            if (!depotLightingSaved) return;
            RenderSettings.skybox = savedSkybox;
            RenderSettings.ambientLight = savedAmbientLight;
            RenderSettings.ambientMode = savedAmbientMode;
            RenderSettings.ambientIntensity = savedAmbientIntensity;
            RenderSettings.fog = savedFog;
        }

        /// <summary>
        /// Unity applies scene lighting with a delay after additive load.
        /// Keep restoring Depot lighting for several frames to override it.
        /// </summary>
        private static IEnumerator RestoreLightingDelayed()
        {
            for (int i = 0; i < 10; i++)
            {
                yield return null;
                if (ActiveScene == GameScene.Depot)
                    RestoreDepotLighting();
            }
        }

        private static void ApplyMapLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.fog = false;
        }
    }
}
