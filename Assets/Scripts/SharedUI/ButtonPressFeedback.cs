using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// MUI-11/MUI-14: subtelny „press feel" dla przycisków UI — scale-punch na wciśnięcie,
    /// uzupełnia istniejący ColorBlock tint (kolor) o ruch. Zgodny z `visual-style-guide.md`
    /// §4.4: krótki i miękki ruch (~100 ms), BEZ gumowego bounce.
    ///
    /// <para>Skala lokalna 1.0 → <see cref="pressedScale"/> na pointer down, wraca na up/exit.
    /// Exponential smoothing (frame-rate independent), <b>unscaled time</b> (działa w pauzie).
    /// localScale nie wpływa na LayoutGroup, więc bezpieczne wewnątrz vertical/horizontal layoutów.</para>
    ///
    /// <para><b>Auto-attach</b> w centralnych builderach: <see cref="UIBuilders"/> MakeButton/
    /// MakeIconButton, Depot <c>CreateOptionButton</c>/BuildMenuUI, MainMenu CreateButton.
    /// Dla przycisków spoza builderów — dodać ręcznie <c>AddComponent&lt;ButtonPressFeedback&gt;()</c>.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ButtonPressFeedback : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        /// <summary>Skala przy wciśnięciu (1.0 = bez zmiany). 0.94 = subtelne „wgniecenie".</summary>
        public float pressedScale = 0.94f;

        /// <summary>Szybkość dochodzenia do celu (większa = szybsza). 16 ≈ ~100 ms settle.</summary>
        public float speed = 16f;

        private RectTransform _rt;
        private Vector3 _baseScale = Vector3.one;
        private float _target = 1f;

        private void Awake()
        {
            _rt = (RectTransform)transform;
            _baseScale = _rt.localScale;
        }

        private void OnDisable()
        {
            // Reset, żeby przycisk nie został „wciśnięty" po ukryciu/wyłączeniu.
            _target = 1f;
            if (_rt != null)
                _rt.localScale = _baseScale;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsInteractable()) return;
            _target = pressedScale;
        }

        public void OnPointerUp(PointerEventData eventData) => _target = 1f;
        public void OnPointerExit(PointerEventData eventData) => _target = 1f;

        private void Update()
        {
            if (_rt == null) return;

            Vector3 desired = _baseScale * _target;
            if (_rt.localScale == desired) return;

            float t = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
            _rt.localScale = Vector3.Lerp(_rt.localScale, desired, t);

            // Snap gdy bardzo blisko, żeby uniknąć wiecznego dogonienia.
            if ((_rt.localScale - desired).sqrMagnitude < 0.0000004f)
                _rt.localScale = desired;
        }

        private bool IsInteractable()
        {
            var selectable = GetComponent<Selectable>();
            return selectable == null || selectable.IsInteractable();
        }
    }
}
