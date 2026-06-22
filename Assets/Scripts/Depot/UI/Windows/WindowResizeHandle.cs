using UnityEngine;
using UnityEngine.EventSystems;

namespace DepotSystem
{
    /// <summary>
    /// M-Windows P5: uchwyt skalowania okna za krawędź/róg. <c>hx</c>/<c>hy</c> wskazują którą
    /// krawędź ciągnie (+1 prawo/góra, -1 lewo/dół, 0 = oś nieruszana). Grab-tracking od początku
    /// drag (bez dryfu), matematyka w <see cref="WindowLayoutMath.Resize"/> (min/max clamp).
    /// </summary>
    public class WindowResizeHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler
    {
        FloatingWindow _window;
        RectTransform _windowRect;
        RectTransform _parentRect;
        int _hx, _hy;
        Vector2 _startLocal, _startPos, _startSize;

        public void Init(FloatingWindow window, int hx, int hy)
        {
            _window = window;
            _windowRect = window.RootRect;
            _parentRect = window.RootRect.parent as RectTransform;
            _hx = hx; _hy = hy;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_window != null) _window.Focus();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_window != null) _window.Focus();
            if (ToLocal(eventData, out var local))
            {
                _startLocal = local;
                _startPos = _windowRect.anchoredPosition;
                _startSize = _windowRect.rect.size;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_windowRect == null || _parentRect == null) return;
            if (!ToLocal(eventData, out var local)) return;
            var delta = local - _startLocal;
            WindowLayoutMath.Resize(_startPos, _startSize, delta, _hx, _hy,
                FloatingWindow.MinSize, FloatingWindow.MaxSize, out var newPos, out var newSize);
            _windowRect.sizeDelta = newSize;
            _windowRect.anchoredPosition = newPos;
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
