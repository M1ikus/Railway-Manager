using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Debug helper dla M-PL-4 manual water polygons: klik w Game/Scene view loguje world coords (X, Y)
    /// na konsolę. User kopiuje do Inspector array `SyntheticWaterRenderer.manualPolygons`.
    ///
    /// Użycie:
    /// 1. Dodaj komponent do MapScene
    /// 2. Play
    /// 3. Mouse LEFT CLICK na mapie w Game view w miejscu gdzie chcesz vertex (np. NW corner Zatoki)
    /// 4. Console: `[WorldCoordPicker] World (X, Y) = (NNNN, MMMM)` — skopiuj do Inspector
    ///
    /// Raycast idzie z kamery przez mouse cursor, trafia w Y=0 plane (nie wymaga collider).
    /// </summary>
    public class WorldCoordPicker : MonoBehaviour
    {
        [Tooltip("Y plane na który raycastujemy. Default 0 (water/ground level).")]
        public float targetY = 0f;

        [Tooltip("Włącz/wyłącz. Po zebraniu coordów możesz wyłączyć bez usuwania komponentu.")]
        public bool enabled_ = true;

        // BUG-034: cache fallback camera żeby nie skanować scene per-click przy braku Camera.main.
        Camera _cachedFallbackCam;

        void Update()
        {
            if (!enabled_) return;

            // New Input System check (projekt używa NIS zgodnie z CLAUDE.md)
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            var cam = Camera.main;
            if (cam == null)
            {
                // BUG-034: cache fallback camera (invalidate gdy zniszczony lub w innej scenie).
                if (_cachedFallbackCam == null || _cachedFallbackCam.gameObject.scene != gameObject.scene)
                {
                    _cachedFallbackCam = null;
                    foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
                    {
                        if (c.gameObject.scene == gameObject.scene) { _cachedFallbackCam = c; break; }
                    }
                }
                cam = _cachedFallbackCam;
            }
            if (cam == null) { Log.Warn("[WorldCoordPicker] No camera"); return; }

            Vector2 mousePos = mouse.position.ReadValue();
            Ray ray = cam.ScreenPointToRay(new Vector3(mousePos.x, mousePos.y, 0));

            // Intersect z Y=targetY plane
            if (Mathf.Abs(ray.direction.y) < 0.001f) { Log.Warn("[WorldCoordPicker] Ray parallel to plane"); return; }
            float t = (targetY - ray.origin.y) / ray.direction.y;
            if (t < 0) { Log.Warn("[WorldCoordPicker] Hit behind camera"); return; }

            Vector3 hit = ray.origin + ray.direction * t;
            Log.Info($"[WorldCoordPicker] World (X, Y) = ({hit.x:F0}, {hit.z:F0})   — copy do Inspector Vector2");
        }
    }
}
