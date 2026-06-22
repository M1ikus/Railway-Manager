using System.Collections.Generic;
using UnityEngine;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Drop target dla CirculationDraggableTile. Każdy wiersz obiegu w CirculationListUI
    /// ma swój instancję tego komponentu z konkretnym circulationId. Statyczny registry
    /// pozwala CirculationDraggableTile.OnEndDrag znaleźć właściwy target po pozycji
    /// pointera bez wiedzy o strukturze UI.
    /// </summary>
    public class CirculationDropTarget : MonoBehaviour
    {
        /// <summary>ID obiegu do którego drop dodaje nowy krok.</summary>
        public int circulationId;

        /// <summary>RectTransform obszaru drop — użyty do RectangleContainsScreenPoint.</summary>
        public RectTransform rect;

        /// <summary>Callback wywoływany gdy tile zostanie dropnięty na ten target.</summary>
        public System.Action<int, int> onDropReceived; // (circulationId, timetableId)

        private static readonly List<CirculationDropTarget> _all = new();

        void OnEnable()
        {
            if (!_all.Contains(this)) _all.Add(this);
        }

        void OnDisable()
        {
            _all.Remove(this);
        }

        /// <summary>
        /// Znajduje pierwszy zarejestrowany drop target nad którym znajduje się screenPoint.
        /// Używane przez CirculationDraggableTile.OnEndDrag.
        /// </summary>
        public static CirculationDropTarget FindAtScreenPoint(Vector2 screenPoint, Camera cam)
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var dt = _all[i];
                if (dt == null || dt.rect == null) continue;
                if (!dt.gameObject.activeInHierarchy) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(dt.rect, screenPoint, cam))
                    return dt;
            }
            return null;
        }
    }
}
