using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Kafelek pojazdu w VehicleAssignmentModal — drag&drop z prawej puli na wiersz dnia
    /// po lewej. Pattern taki sam jak CirculationDraggableTile, ale niesie vehicleId
    /// i szuka CirculationDayDropTarget zamiast CirculationDropTarget.
    /// </summary>
    public class VehicleDraggableTile : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public int vehicleId;
        public Canvas canvas;

        // M-Windows P4: klik (bez draga) → okno detalu pojazdu. UGUI nie woła OnPointerClick gdy był drag.
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.dragging) return;
            DepotSystem.FleetPanelUI.OpenVehicleWindow(vehicleId);
        }

        private GameObject _ghost;
        private RectTransform _ghostRt;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
                if (canvas == null) return;
            }
            _ghost = Instantiate(gameObject, canvas.transform);
            _ghost.name = "VehDragGhost_" + vehicleId;
            var dragScript = _ghost.GetComponent<VehicleDraggableTile>();
            if (dragScript != null) Destroy(dragScript);
            var btn = _ghost.GetComponent<Button>();
            if (btn != null) Destroy(btn);

            var cg = _ghost.GetComponent<CanvasGroup>();
            if (cg == null) cg = _ghost.AddComponent<CanvasGroup>();
            cg.alpha = 0.8f;
            cg.blocksRaycasts = false;
            cg.interactable = false;

            _ghostRt = _ghost.GetComponent<RectTransform>();
            _ghostRt.anchorMin = new Vector2(0, 0);
            _ghostRt.anchorMax = new Vector2(0, 0);
            _ghostRt.pivot = new Vector2(0.5f, 0.5f);
            UpdateGhostPosition(eventData);

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

            var target = CirculationDayDropTarget.FindAtScreenPoint(eventData.position, cam);
            if (target != null && target.onDropReceived != null)
                target.onDropReceived.Invoke(target.dateIso, vehicleId);
        }

        private void UpdateGhostPosition(PointerEventData eventData)
        {
            if (_ghostRt == null || canvas == null) return;
            Camera cam = null;
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = canvas.worldCamera;
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
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
            var srcCg = GetComponent<CanvasGroup>();
            if (srcCg != null) srcCg.alpha = 1f;
        }
    }
}
