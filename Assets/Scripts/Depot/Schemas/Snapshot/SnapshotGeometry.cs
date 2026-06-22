using System;
using UnityEngine;

namespace DepotSystem.Schemas.Snapshot
{
    /// <summary>
    /// Geometria literalna snapshot schematu — JSON-serializable, embedded w
    /// <c>TurnoutSchemaDefinition.snapshotGeometry</c> gdy type="snapshot".
    ///
    /// Wszystko w lokalnych współrzędnych względem <see cref="anchorPoint"/> (centroid bbox selekcji).
    /// Anchor sam jest world coords w momencie zapisu — informacyjne, nie używane przy placement.
    ///
    /// Po deserialize w MD-9: lokalne coords + cursor world position + rotation = global preview.
    /// </summary>
    [Serializable]
    public class SnapshotGeometry
    {
        /// <summary>Centroid bounding box selekcji w world coords (informacyjne).</summary>
        public Vector3 anchorPoint;

        /// <summary>Wszystkie odcinki torów w lokalnych coords.</summary>
        public SnapshotTrackEntry[] tracks;

        /// <summary>Wszystkie rozjazdy w lokalnych coords.</summary>
        public SnapshotTurnoutEntry[] turnouts;

        /// <summary>
        /// Endpointy schematu w lokalnych coords — punkty, które mogą się snapować
        /// do istniejących torów przy późniejszym placement (MD-9).
        ///
        /// Detekcja: punkty z <see cref="tracks"/> które występują tylko 1 raz (= otwarte końce,
        /// nie połączone z innym torem w schemacie). Junction'y wewnątrz schematu (= punkt
        /// występuje 2+ razy) NIE są endpointami.
        /// </summary>
        public Vector3[] endpoints;
    }
}
