using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RailwayManager.SharedUI.Assistant
{
    /// <summary>
    /// M11 AS-3: pulsująca poświata guidance (warstwa [2] UX) — nakładka na wskazany
    /// RectTransform, znika sama po czasie. Attachowana jako child celu (podąża za layoutem),
    /// raycast OFF (nie blokuje kliknięcia w podświetlany element — gracz klika PRZEZ highlight).
    /// </summary>
    public class AssistantHighlight : MonoBehaviour
    {
        public const float DefaultDurationSec = 8f;
        const float PulseSpeed = 2.2f;
        const float AlphaMin = 0.18f;
        const float AlphaMax = 0.55f;
        const float FramePadding = 5f;

        private static readonly List<AssistantHighlight> _active = new List<AssistantHighlight>();

        private Image _glow;
        private float _remainingRealSec;

        /// <summary>Pokazuje poświatę na celu. Wcześniejsze highlighty NIE są czyszczone — caller decyduje (ClearAll).</summary>
        public static void Show(RectTransform target, float durationSec = DefaultDurationSec)
        {
            if (target == null) return;

            var go = new GameObject("AssistantHighlight", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(target, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-FramePadding, -FramePadding);
            rt.offsetMax = new Vector2(FramePadding, FramePadding);
            // Layout-proof: gdy parent ma LayoutGroup, ignoruj nakładkę w layoucie.
            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;

            var img = go.GetComponent<Image>();
            UITheme.ApplySurface(img, UITheme.Warning, UIShapePreset.Button);
            img.raycastTarget = false;

            var highlight = go.AddComponent<AssistantHighlight>();
            highlight._glow = img;
            highlight._remainingRealSec = durationSec;
            _active.Add(highlight);
        }

        /// <summary>Zdejmuje wszystkie aktywne poświaty (np. nowy krok guidance / zmiana sceny).</summary>
        public static void ClearAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                if (_active[i] != null) Destroy(_active[i].gameObject);
            }
            _active.Clear();
        }

        void Update()
        {
            _remainingRealSec -= Time.unscaledDeltaTime;
            if (_remainingRealSec <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            if (_glow != null)
            {
                float t = Mathf.PingPong(Time.unscaledTime * PulseSpeed, 1f);
                var c = _glow.color;
                c.a = Mathf.Lerp(AlphaMin, AlphaMax, t);
                _glow.color = c;
            }
        }

        void OnDestroy() => _active.Remove(this);
    }
}
