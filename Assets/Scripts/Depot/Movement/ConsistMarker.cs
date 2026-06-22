using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// Component na visual GO consist'u w zajezdni. Nośnik danych — klikalność
    /// obsługuje DepotConsistSelectionHandler przez raycast.
    /// Dodatkowo trzyma referencje do materiału dla highlight'u (select/deselect).
    /// </summary>
    public class ConsistMarker : MonoBehaviour
    {
        public int consistId = -1;
        public List<int> vehicleIds = new List<int>();

        /// <summary>Tor na którym consist aktualnie stoi lub do którego jedzie.</summary>
        public int currentTrackId = -1;

        /// <summary>Cache MeshRenderer do zmiany materiału przy select/deselect.</summary>
        public MeshRenderer meshRenderer;

        /// <summary>Oryginalny materiał (czerwony) — przywracany przy deselect.</summary>
        public Material originalMaterial;
    }
}
