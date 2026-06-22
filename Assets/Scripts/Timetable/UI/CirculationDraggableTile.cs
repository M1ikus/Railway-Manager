using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Komponent przeciągalnego kafelka rozkładu w CirculationCreatorUI. Gdy user
    /// zaczyna drag, tworzymy półprzezroczysty ghost visual który podąża za pointerem.
    /// Na OnEndDrag sprawdzamy czy pointer jest nad sequence drop target i jeśli tak,
    /// wywołujemy callback z timetableId — caller decyduje co zrobić (walidacja, modale).
    ///
    /// Implementuje IBeginDragHandler, IDragHandler, IEndDragHandler — wymagają
    /// że GameObject ma Raycast Target (jakiś Graphic, np. Image — masz to z tile bg).
    /// </summary>
    public class CirculationDraggableTile : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>ID rozkładu który ten kafelek reprezentuje.</summary>
        public int timetableId;

        /// <summary>Canvas root — używany do umieszczenia ghost visual.</summary>
        public Canvas canvas;

        /// <summary>
        /// (DEPRECATED — legacy dla Etap 6 creator) Drop zone single.
        /// Nowy flow: używaj CirculationDropTarget registry.
        /// </summary>
        public RectTransform dropZone;

        /// <summary>(legacy) Callback dla single-drop-zone flow.</summary>
        public System.Action<int> onDropped;

        private GameObject _ghost;
        private RectTransform _ghostRt;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
                if (canvas == null) return;
            }

            // Klonuj tile jako ghost, wrzuć do canvas top-level
            _ghost = Instantiate(gameObject, canvas.transform);
            _ghost.name = "DragGhost_" + timetableId;

            // Ghost nie powinien być draggable ani klikalny
            var dragScript = _ghost.GetComponent<CirculationDraggableTile>();
            if (dragScript != null) Destroy(dragScript);
            var btn = _ghost.GetComponent<Button>();
            if (btn != null) Destroy(btn);

            // Raycast-blocking off żeby pointer "przechodził przez ghost" do drop zone
            var cg = _ghost.GetComponent<CanvasGroup>();
            if (cg == null) cg = _ghost.AddComponent<CanvasGroup>();
            cg.alpha = 0.75f;
            cg.blocksRaycasts = false;
            cg.interactable = false;

            _ghostRt = _ghost.GetComponent<RectTransform>();

            // Ghost root — wyciągnij z layout grupy żeby mógł swobodnie się przesuwać
            // (Instantiate zachowuje parent hierarchy; my ustawiamy parent = canvas.transform
            // ale RectTransform może mieć dziwne anchors)
            _ghostRt.anchorMin = new Vector2(0, 0);
            _ghostRt.anchorMax = new Vector2(0, 0);
            _ghostRt.pivot = new Vector2(0.5f, 0.5f);

            // Ustaw pozycję ghost na punkcie pointer'a
            UpdateGhostPosition(eventData);

            // Oryginał: lekko przygaszony podczas drag (indicator że coś się dzieje)
            var srcCg = GetComponent<CanvasGroup>();
            if (srcCg == null) srcCg = gameObject.AddComponent<CanvasGroup>();
            srcCg.alpha = 0.4f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_ghost == null) return;
            UpdateGhostPosition(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            // Przywróć alpha oryginalnego kafelka
            var srcCg = GetComponent<CanvasGroup>();
            if (srcCg != null) srcCg.alpha = 1f;

            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera;

            if (_ghost != null)
            {
                Destroy(_ghost);
                _ghost = null;
                _ghostRt = null;
            }

            // Nowy flow: sprawdź registry CirculationDropTarget'ów
            var target = CirculationDropTarget.FindAtScreenPoint(eventData.position, cam);
            if (target != null && target.onDropReceived != null)
            {
                target.onDropReceived.Invoke(target.circulationId, timetableId);
                return;
            }

            // Legacy flow (Etap 6 CirculationCreatorUI — deprecated)
            if (dropZone != null && RectTransformUtility.RectangleContainsScreenPoint(dropZone, eventData.position, cam))
            {
                onDropped?.Invoke(timetableId);
            }
        }

        private void UpdateGhostPosition(PointerEventData eventData)
        {
            if (_ghostRt == null || canvas == null) return;

            Camera cam = null;
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera;

            // Konwersja screen point → local point w canvas
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.transform as RectTransform,
                eventData.position,
                cam,
                out Vector2 local))
            {
                _ghostRt.anchoredPosition = local;
            }
        }

        void OnDisable()
        {
            // Cleanup jeśli drag został przerwany (np. panel zniknął)
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
            var srcCg = GetComponent<CanvasGroup>();
            if (srcCg != null) srcCg.alpha = 1f;
        }
    }
}
