using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace RailwayManager.Core
{
    /// <summary>
    /// Partial dla <see cref="SceneController"/> — layer recursion + UI/EventSystem toggling.
    ///
    /// Layer separation: MapScene obiekty → layer 31, Depot → default. Pozwala kamerom
    /// niezależnie cullować swoje sceny.
    ///
    /// UI toggling: CanvasGroup (alpha/interactable/blocksRaycasts) zamiast Canvas.enabled —
    /// dla kompatybilności z UGUI InputField rebuild cycle (Canvas.enabled toggling psuje
    /// state caretu).
    /// </summary>
    public static partial class SceneController
    {
        /// <summary>
        /// Ustawia WSZYSTKIE obiekty w scenie (i ich dzieci) na dany layer.
        /// Nowe obiekty tworzone jako dzieci też dziedziczą layer.
        /// </summary>
        private static void SetSceneLayer(string sceneName, int layer)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded) return;

            foreach (var root in scene.GetRootGameObjects())
            {
                SetLayerRecursive(root, layer);
            }
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            // Don't change layer on UI elements — Canvas/EventSystem must stay on default layer
            // UI layer (5) and objects with Canvas component should keep their layer
            if (go.GetComponent<Canvas>() != null || go.GetComponent<EventSystem>() != null)
                return;

            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        /// <summary>
        /// Włącza/wyłącza UI sceny (widoczność + interaktywność).
        ///
        /// Używa CanvasGroup (alpha/interactable/blocksRaycasts) zamiast Canvas.enabled,
        /// bo toggling Canvas.enabled psuje Unity UGUI InputField — GenerateCaret rzuca
        /// NullReferenceException przy wyjściu z cyklu disabled→enabled (Unity trzyma
        /// stale state w m_CachedInputRenderer gdy rebuildy są pauzowane). CanvasGroup
        /// utrzymuje Canvas cały czas enabled, więc UGUI rebuild cykl nigdy się nie
        /// zatrzymuje, a InputField pozostaje poprawnie zainicjalizowany.
        ///
        /// EventSystem.enabled nadal jest przełączany (to bezpieczne i potrzebne dla
        /// cooldown ping-pong logic).
        /// </summary>
        private static void SetSceneUIEnabled(string sceneName, bool enabled)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            if (!scene.IsValid() || !scene.isLoaded) return;

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                {
                    var group = canvas.GetComponent<CanvasGroup>();
                    if (group == null)
                        group = canvas.gameObject.AddComponent<CanvasGroup>();
                    group.alpha = enabled ? 1f : 0f;
                    group.interactable = enabled;
                    group.blocksRaycasts = enabled;
                }
                foreach (var es in root.GetComponentsInChildren<EventSystem>(true))
                    es.enabled = enabled;
            }
        }

        /// <summary>
        /// Enables target scene's UI after one frame, so that the click event from the
        /// originating scene finishes processing before the destination scene's EventSystem
        /// starts handling input. Prevents "immediate reverse switch" on click-through.
        /// </summary>
        private static IEnumerator SetSceneUIDelayed(string sceneName, bool enabled)
        {
            yield return null; // let the click that triggered us finish in Depot EventSystem
            SetSceneUIEnabled(sceneName, enabled);
        }
    }
}
