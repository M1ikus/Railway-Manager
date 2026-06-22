using System.Collections.Generic;
using UnityEngine;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Drop target dla VehicleDraggableTile w VehicleAssignmentModal — reprezentuje
    /// jeden dzień obiegu do którego użytkownik upuszcza pojazdy. Static registry
    /// pozwala VehicleDraggableTile znaleźć właściwy target po pozycji pointera.
    /// </summary>
    public class CirculationDayDropTarget : MonoBehaviour
    {
        public string dateIso; // "YYYY-MM-DD"
        public RectTransform rect;
        public System.Action<string, int> onDropReceived; // (dateIso, vehicleId)

        private static readonly List<CirculationDayDropTarget> _all = new();

        void OnEnable() { if (!_all.Contains(this)) _all.Add(this); }
        void OnDisable() { _all.Remove(this); }

        public static CirculationDayDropTarget FindAtScreenPoint(Vector2 screenPoint, Camera cam)
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
