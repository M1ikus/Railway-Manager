using UnityEngine;
using UnityEngine.EventSystems;

namespace RailwayManager.Timetable.UI
{
    /// <summary>
    /// Input pan/zoom dla <see cref="RouteMapPreview"/> — komponent na RawImage podglądu.
    /// Drag = pan (przesunięcie widoku), scroll = zoom. Izolowany do RawImage (UI EventSystem),
    /// więc NIE koliduje z globalnym CameraController świata. Cała algebra w widgecie/RouteMapPreviewMath.
    /// </summary>
    public class RouteMapPreviewInput : MonoBehaviour, IDragHandler, IScrollHandler, IPointerClickHandler
    {
        public RouteMapPreview Target;

        public void OnDrag(PointerEventData eventData)
        {
            if (Target != null) Target.Pan(eventData.delta);
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (Target == null) return;
            Target.Zoom(eventData.scrollDelta.y);
            eventData.Use(); // nie propaguj scrolla do ewentualnego rodzica ScrollRect
        }

        // IPointerClickHandler NIE odpala się po przeciągnięciu (Unity rozróżnia klik vs drag),
        // więc pan nie wywoła wyboru stacji.
        public void OnPointerClick(PointerEventData eventData)
        {
            if (Target == null) return;
            var rt = transform as RectTransform;
            if (rt == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rt, eventData.position, eventData.pressEventCamera, out var local))
                return;
            var r = rt.rect;
            float u = Mathf.Clamp01((local.x - r.xMin) / r.width);
            float v = Mathf.Clamp01((local.y - r.yMin) / r.height);
            Target.HandleClickAtUv(new Vector2(u, v));
        }
    }
}
