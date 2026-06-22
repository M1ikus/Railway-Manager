using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// MUI-11: zunifikowane stany przycisku-kafla toolbara w stylu City Bus Manager.
    /// Cztery stany: podstawowy / hover (powiekszenie) / wcisniety (punch) / wybrany.
    ///
    /// <para>Ten komponent odpowiada za RUCH (skala): hover → <see cref="hoverScale"/>
    /// (powiekszenie kafla, jak CBM), press → <see cref="pressedScale"/> (subtelny punch).
    /// Pierwszenstwo: press &gt; hover. Exponential smoothing, <b>unscaled time</b> (dziala w pauzie).
    /// <c>localScale</c> NIE wplywa na LayoutGroup, wiec kafel powieksza sie „nad" sasiadami bez reflow.</para>
    ///
    /// <para>Pozostale stany robi otoczenie (rozdzial odpowiedzialnosci): <b>nazwa na hover</b> =
    /// tooltip (<see cref="UIBuilders.AttachTooltip"/>), <b>stan wybrany</b> = logika panelu
    /// (np. BuildMenuUI.UpdateVisuals tintuje tlo). Dzieki temu komponent nie walczy o kolor tla.</para>
    ///
    /// <para>Reuzywalny — zastepuje <see cref="ButtonPressFeedback"/> na kaflach toolbara; docelowo
    /// na wszystkich paskach (budowanie, glowny pasek menu, pod-toolbary). Zwykle przyciski (menu,
    /// formularze) zostaja na <see cref="ButtonPressFeedback"/> — tam powiekszanie na hover jest niepozadane.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ToolbarButtonStates : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        /// <summary>Skala na hover (&gt;1 = powiekszenie, styl CBM). 1.0 = bez zmiany.</summary>
        public float hoverScale = 1.10f;

        /// <summary>Skala na wcisniecie (subtelne „wgniecenie").</summary>
        public float pressedScale = 0.94f;

        /// <summary>Szybkosc dochodzenia do celu (wieksza = szybsza). 16 ≈ ~100 ms settle.</summary>
        public float speed = 16f;

        private RectTransform _rt;
        private Selectable _selectable;
        private Vector3 _baseScale = Vector3.one;
        private bool _hover, _pressed;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            _baseScale = _rt.localScale;
            _selectable = GetComponent<Selectable>();
        }

        private void OnDisable()
        {
            // Reset, zeby kafel nie zostal powiekszony/wcisniety po ukryciu lub zmianie toola.
            _hover = false;
            _pressed = false;
            if (_rt != null)
                _rt.localScale = _baseScale;
        }

        public void OnPointerEnter(PointerEventData e) { if (IsInteractable()) _hover = true; }
        public void OnPointerExit(PointerEventData e)  { _hover = false; _pressed = false; }
        public void OnPointerDown(PointerEventData e)  { if (IsInteractable()) _pressed = true; }
        public void OnPointerUp(PointerEventData e)    => _pressed = false;

        private void Update()
        {
            if (_rt == null) return;

            float target = 1f;
            if (IsInteractable())
                target = _pressed ? pressedScale : (_hover ? hoverScale : 1f);

            Vector3 desired = _baseScale * target;
            if (_rt.localScale == desired) return;

            float t = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
            _rt.localScale = Vector3.Lerp(_rt.localScale, desired, t);

            // Snap gdy bardzo blisko, zeby uniknac wiecznego dogonienia.
            if ((_rt.localScale - desired).sqrMagnitude < 0.0000004f)
                _rt.localScale = desired;
        }

        private bool IsInteractable() => _selectable == null || _selectable.IsInteractable();
    }
}
