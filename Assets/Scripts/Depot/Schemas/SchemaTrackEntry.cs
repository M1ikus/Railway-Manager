using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem.Schemas
{
    /// <summary>
    /// Runtime POCO — pojedynczy odcinek toru wygenerowany przez schema generator.
    /// W lokalnych współrzędnych schematu (anchor = (0,0,0), tor wjazdowy w +X kierunku).
    ///
    /// Po placement'cie konwertowany na <c>PlacedTrackSegment</c> przez <c>PrefabTrackBuilder</c>
    /// (transformacja lokalne → global po snap'ie + rotacji w MD-3+).
    /// </summary>
    public class SchemaTrackEntry
    {
        /// <summary>Polyline w lokalnych współrzędnych (X = wzdłuż toru wjazdowego, Y zawsze 0, Z = lateral).</summary>
        public List<Vector3> polyline;

        /// <summary>
        /// Typ toru (mapuje na <see cref="DepotTrackType"/>):
        /// "Parking" / "Entry" / "Exit" / "Washing" / "Workshop" / "Maneuver".
        /// Generators dają zazwyczaj "Parking" dla torów przewodnich i postojowych +
        /// "Maneuver" dla wstawek prostych i łuków łączących między rozjazdami.
        /// </summary>
        public string trackTypeName = "Parking";

        /// <summary>Nazwa toru (np. "Tor przewodni", "Tor 2", "Łuk powrotny").</summary>
        public string name = "";

        /// <summary>
        /// Jeśli true (default), tor jest stawiany przez PrefabTrackBuilder.PlaceTrackWithPolyline
        /// w PHASE 1 placement. Jeśli false, tor pomijany — odpowiednie geometrie tworzone przez
        /// TurnoutPlacer.PlaceTurnoutOnChain w PHASE 2 (np. łuk start = R(i) odgałęzienie,
        /// łuk powrotny = R(i+1) odgałęzienie). Zapobiega duplikatom torów na sobie.
        ///
        /// Preview (SchemaPreviewRenderer) ignoruje tę flagę — wszystkie elementy widoczne.
        /// </summary>
        public bool placeAsTrack = true;

        /// <summary>
        /// Konwertuje string na enum <see cref="DepotTrackType"/>.
        /// </summary>
        public DepotTrackType ParseTrackType()
        {
            if (System.Enum.TryParse<DepotTrackType>(trackTypeName, ignoreCase: true, out var result))
                return result;
            return DepotTrackType.Parking;
        }
    }
}
