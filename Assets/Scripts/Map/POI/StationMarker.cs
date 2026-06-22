using UnityEngine;
using RailwayManager.Core;

namespace MapSystem
{
    /// <summary>
    /// Component attached to station markers for click handling and data storage.
    /// Stations are clickable POIs that can trigger UI actions.
    /// </summary>
    public class StationMarker : MonoBehaviour
    {
        /// <summary>Globalny event — dowolna stacja na mapie została kliknięta.</summary>
        public static event System.Action<StationMarker> OnAnyStationClicked;

        [Header("Station Data")]
        public string stationName;
        public string stationType; // "station" or "halt"

        [Header("Visual State")]
        public bool isHighlighted = false;
        public bool isSelected = false;

        private Color originalColor;
        private Renderer iconRenderer;

        void Start()
        {
            // Find icon renderer for highlighting
            var icon = transform.Find("Icon");
            if (icon != null)
            {
                iconRenderer = icon.GetComponent<Renderer>();
                if (iconRenderer != null && iconRenderer.material != null)
                {
                    originalColor = iconRenderer.material.color;
                }
            }
        }

        void OnMouseEnter()
        {
            if (!isSelected)
            {
                SetHighlight(true);
            }
        }

        void OnMouseExit()
        {
            if (!isSelected)
            {
                SetHighlight(false);
            }
        }

        void OnMouseDown()
        {
            OnStationClicked();
        }

        /// <summary>
        /// Called when the station is clicked
        /// </summary>
        public void OnStationClicked()
        {
            Log.Info($"[StationMarker] Station clicked: {stationName} ({stationType})");

            isSelected = !isSelected;
            SetHighlight(isSelected);

            OnAnyStationClicked?.Invoke(this);
        }

        /// <summary>
        /// Sets the highlight state of the station marker
        /// </summary>
        public void SetHighlight(bool highlight)
        {
            isHighlighted = highlight;

            if (iconRenderer != null && iconRenderer.material != null)
            {
                if (highlight)
                {
                    iconRenderer.material.color = originalColor * 1.5f;
                }
                else
                {
                    iconRenderer.material.color = originalColor;
                }
            }
        }

        /// <summary>
        /// Programmatically select this station
        /// </summary>
        public void Select()
        {
            isSelected = true;
            SetHighlight(true);
        }

        /// <summary>
        /// Programmatically deselect this station
        /// </summary>
        public void Deselect()
        {
            isSelected = false;
            SetHighlight(false);
        }
    }
}
