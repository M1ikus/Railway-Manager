using UnityEngine;
using TMPro;

namespace DepotSystem.Furniture.Functional
{
    /// <summary>
    /// MF-8 — billboard overlay (TextMeshPro 3D) wyświetlający status funkcjonalny instancji.
    ///
    /// Symbol:
    /// - Zielony "✓" gdy <see cref="FurnitureFunctionalState.IsActive"/> == true
    /// - Czerwony "✗" gdy IsActive == false (dojście zablokowane lub inny Warning)
    ///
    /// Pozycja: 1.5m nad pivotem instance, billboard tylko Y-axis (tekst zawsze upright,
    /// rotuje się po Y żeby patrzeć na kamerę).
    ///
    /// Dorzucany jako child stamped visualization GameObject przez FurniturePlacer.
    /// Update'owany przez <see cref="SetState"/> po RecomputeAllFunctionalStates.
    /// </summary>
    public class FurnitureWarningOverlay : MonoBehaviour
    {
        public const float HoverHeight = 1.5f;     // metres above pivot
        public const float TextSize = 1.2f;        // world-space text size
        private static readonly Color ActiveColor = new Color(0.2f, 0.9f, 0.3f, 1f);
        private static readonly Color BlockedColor = new Color(1f, 0.25f, 0.25f, 1f);

        private TextMeshPro _tmp;
        private Camera _mainCamera;
        private FurnitureFunctionalState _currentState = FurnitureFunctionalState.Active();

        void Awake()
        {
            EnsureText();
            ApplyState();
        }

        void LateUpdate()
        {
            // Y-only billboard: text rotuje się po Y żeby patrzeć na kamerę, ale nie pochyla się
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return;

            Vector3 dir = transform.position - _mainCamera.transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(dir);
            }
        }

        public void SetState(FurnitureFunctionalState state)
        {
            _currentState = state;
            ApplyState();
        }

        private void EnsureText()
        {
            if (_tmp != null) return;

            _tmp = gameObject.GetComponent<TextMeshPro>();
            if (_tmp == null) _tmp = gameObject.AddComponent<TextMeshPro>();

            _tmp.fontSize = TextSize;
            _tmp.alignment = TextAlignmentOptions.Center;
            _tmp.fontStyle = FontStyles.Bold;
            _tmp.text = "✓";
            _tmp.color = ActiveColor;

            // Wymiary RectTransform (TMP 3D używa RectTransform jako MeshRenderer'a)
            var rt = _tmp.rectTransform;
            rt.sizeDelta = new Vector2(2f, 1f);
            rt.localPosition = new Vector3(0f, HoverHeight, 0f);
        }

        private void ApplyState()
        {
            if (_tmp == null) EnsureText();
            if (_tmp == null) return;

            if (_currentState.IsActive)
            {
                _tmp.text = "✓";
                _tmp.color = ActiveColor;
            }
            else
            {
                _tmp.text = "✗";
                _tmp.color = BlockedColor;
            }
        }
    }
}
