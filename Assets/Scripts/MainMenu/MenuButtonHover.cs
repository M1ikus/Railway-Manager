using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace MainMenu
{
    /// <summary>
    /// Hover effect for main menu buttons — changes text color on pointer enter/exit.
    /// </summary>
    public class MenuButtonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private TextMeshProUGUI label;
        private Selectable selectable;
        private Color normalColor;
        private Color hoverColor;

        public void Init(TextMeshProUGUI label, Color normal, Color hover)
        {
            this.label = label;
            selectable = GetComponent<Selectable>();
            this.normalColor = normal;
            this.hoverColor = hover;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (label != null && (selectable == null || selectable.IsInteractable()))
                label.color = hoverColor;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (label != null)
                label.color = normalColor;
        }
    }
}
