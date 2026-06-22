using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// Hover effect dla <see cref="Image"/>: tint zmienia się między normal a hover przy pointer enter/exit.
    /// Generic helper dla list rows / cards (MainMenu, ModsScreen, MultiplayerScreen, …).
    /// Zastępuje wcześniejsze duplikaty <c>SaveRowHover</c> / <c>ServerRowHover</c>.
    /// </summary>
    public class HoverImageColor : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Image _img;
        private Color _normal;
        private Color _hover;

        public void Init(Image img, Color normal, Color hover)
        {
            _img = img;
            _normal = normal;
            _hover = hover;
        }

        public void OnPointerEnter(PointerEventData _)
        {
            if (_img != null) _img.color = _hover;
        }

        public void OnPointerExit(PointerEventData _)
        {
            if (_img != null) _img.color = _normal;
        }
    }
}
