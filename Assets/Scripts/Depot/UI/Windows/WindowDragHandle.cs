using UnityEngine;
using UnityEngine.EventSystems;

namespace DepotSystem
{
    /// <summary>
    /// M-Windows P1: uchwyt przeciągania pływającego okna za pasek tytułu. Zachowuje offset chwytu
    /// (grab-offset) — okno nie „skacze" pod kursor. Konwersja ekran→local przez
    /// <see cref="RectTransformUtility.ScreenPointToLocalPointInRectangle"/> (wzór
    /// <c>VehicleDraggableTile</c>), pozycja ograniczana przez <see cref="WindowLayoutMath"/>.
    /// </summary>
    public class WindowDragHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
    {
        const float KeepOnScreenMargin = 56f;

        FloatingWindow _window;
        RectTransform _windowRect;
        RectTransform _parentRect;
        Vector2 _grabOffset;

        public void Init(FloatingWindow window)
        {
            _window = window;
            _windowRect = window.RootRect;
            _parentRect = window.RootRect.parent as RectTransform;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_window != null) _window.Focus();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_window != null) _window.Focus();
            if (ToLocal(eventData, out var local))
                _grabOffset = _windowRect.anchoredPosition - local;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_windowRect == null || _parentRect == null) return;
            if (!ToLocal(eventData, out var local)) return;
            var desired = local + _grabOffset;
            _windowRect.anchoredPosition = WindowLayoutMath.ClampTitleBarOnScreen(
                desired, _windowRect.rect.size, _parentRect.rect.size, KeepOnScreenMargin);
        }

        bool ToLocal(PointerEventData eventData, out Vector2 local)
        {
            local = Vector2.zero;
            if (_parentRect == null) return false;
            Camera cam = null;
            var canvas = _windowRect != null ? _windowRect.GetComponentInParent<Canvas>() : null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentRect, eventData.position, cam, out local);
        }
    }
}
