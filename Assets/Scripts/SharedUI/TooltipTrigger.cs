using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// MUI-3 (M-UIPolish 2026-05-06): MonoBehaviour attach do dowolnego UI elementu (Image/Button/Panel),
    /// żeby pokazać tooltip na hover. Wywołuje <see cref="TooltipManager"/>.
    ///
    /// <para><b>Use case:</b></para>
    /// <code>
    /// var trigger = button.gameObject.AddComponent&lt;TooltipTrigger&gt;();
    /// trigger.text = "Buduj torowisko";
    /// trigger.hoverDelay = 0.5f; // opcjonalne
    /// </code>
    ///
    /// Auto-attached przez <see cref="UIBuilders.MakeIconButton"/> gdy <c>tooltipText != null</c>.
    ///
    /// <para><b>Wymagania:</b></para>
    /// - GameObject musi mieć Graphic component (Image/Text/etc.) z <c>raycastTarget = true</c>
    ///   żeby OnPointerEnter/Exit zadziałało.
    /// - W scenie musi być EventSystem (zazwyczaj jest auto z UI canvas).
    /// </summary>
    public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Tooltip("Tekst tooltip'a do pokazania na hover. Pusty = brak tooltip.")]
        public string text;

        [Tooltip("Hover delay przed pokazaniem tooltip [sekundy realtime]. Default 0.5s.")]
        public float hoverDelay = 0.5f;

        Coroutine _showRoutine;
        Vector2 _enterPosition;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(text)) return;

            _enterPosition = eventData.position;
            if (_showRoutine != null) StopCoroutine(_showRoutine);
            _showRoutine = StartCoroutine(ShowAfterDelay());
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_showRoutine != null)
            {
                StopCoroutine(_showRoutine);
                _showRoutine = null;
            }
            var mgr = TooltipManager.Instance;
            if (mgr != null) mgr.Hide();
        }

        void OnDisable()
        {
            // Defensive: jeśli component disabled w trakcie hover, hide tooltip
            if (_showRoutine != null)
            {
                StopCoroutine(_showRoutine);
                _showRoutine = null;
            }
            var mgr = TooltipManager.Instance;
            if (mgr != null) mgr.Hide();
        }

        IEnumerator ShowAfterDelay()
        {
            yield return new WaitForSecondsRealtime(hoverDelay);
            var mgr = TooltipManager.EnsureExists();
            mgr.Show(text, _enterPosition);
            _showRoutine = null;
        }
    }
}
