using UnityEngine;
using UnityEngine.EventSystems;

namespace DepotSystem
{
    /// <summary>
    /// M-FC-7: Event handler dla RawImage z 3D paint preview. Drag → obrót kamery,
    /// scroll → zoom. <c>eventData.Use()</c> zapobiega forwardowaniu scroll'a do
    /// rodzica ScrollRect'a (paint preview area to "blocker" dla scroll panel'u).
    /// </summary>
    public class PaintPreviewOrbitHandler : MonoBehaviour, IDragHandler, IScrollHandler
    {
        public PaintPreview3D preview;
        public float orbitSensitivityYaw = 0.5f;
        public float orbitSensitivityPitch = 0.3f;

        public void OnDrag(PointerEventData eventData)
        {
            if (preview == null) return;
            preview.OrbitDelta(eventData.delta.x * orbitSensitivityYaw,
                               -eventData.delta.y * orbitSensitivityPitch);
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (preview == null) return;
            preview.ZoomDelta(eventData.scrollDelta.y);
            eventData.Use(); // blokuje scroll w rodzicu (panel ScrollRect)
        }
    }
}
