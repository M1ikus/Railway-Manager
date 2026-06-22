using UnityEngine;
using System;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Peron stacyjny — jednostka rezerwacji przy postoju rozkładu.
    /// Docelowo ładowany z warstwy Platforms w .bin (railway=platform z OSM).
    /// </summary>
    [Serializable]
    public class StationPlatform
    {
        public int platformId;

        /// <summary>ID węzła stacji do której peron należy (matching odległościowy).</summary>
        public int stationNodeId;

        /// <summary>Centroid peronu (geographic position w grafie). Wypełniany w PlatformLoader/
        /// PlatformStreamProcessor. Używany m.in. w propagacji peron→tor track_ref żeby precyzyjnie
        /// znaleźć edge fizycznie przy peronie (vs `_nodes[stationNodeId].position` które jest
        /// pozycją node grafu = centroid CAŁEJ stacji, identyczny dla wszystkich peronów halt'u).</summary>
        public Vector2 position;

        /// <summary>Nazwa peronu — np. "1", "2a", "3".</summary>
        public string platformName;

        /// <summary>Numer toru przy peronie (z tagu OSM railway:track_ref jeśli dostępny).</summary>
        public string trackRef;

        /// <summary>Długość peronu w metrach — do walidacji czy skład się zmieści.</summary>
        public float lengthM;
    }
}
