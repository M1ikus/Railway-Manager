using UnityEngine;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Marker dodawany przez <see cref="OutsidePolandPOIHider"/> do POI GameObject'ów
    /// po przetworzeniu (hide / style application). Zapobiega ponownemu stylizowaniu
    /// tego samego POI co frame (per-frame Update scan).
    ///
    /// Marker NIE jest persistent — przy LOD reload nowy GO jest tworzony bez markera,
    /// więc zostanie ponownie scanowany i stylizowany.
    /// </summary>
    public class OutsidePolandStyledMarker : MonoBehaviour
    {
        // Empty — to tylko tag/marker. GetComponent<>() check decyduje czy POI już processed.
    }
}
