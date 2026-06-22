using System;
using UnityEngine;

namespace DepotSystem.Schemas.Snapshot
{
    /// <summary>
    /// Serializowalna POCO — pojedynczy odcinek toru w snapshot schemacie.
    /// Polyline w lokalnych współrzędnych (względem snapshot anchorPoint = centroid selekcji).
    /// </summary>
    [Serializable]
    public class SnapshotTrackEntry
    {
        /// <summary>Polyline w lokalnych współrzędnych (subtract anchor).</summary>
        public Vector3[] polyline;

        /// <summary>Typ toru (Parking / Maneuver / Entry / Exit / Washing / Workshop).</summary>
        public string trackTypeName = "Parking";

        /// <summary>Nazwa toru (z oryginalnego PlacedTrackSegment / DepotTrackData).</summary>
        public string name = "";

        /// <summary>
        /// Oryginalny GraphTrackId — informacyjne (dla debug). Przy placement
        /// (MD-9) nieużywane — tor jest tworzony od nowa.
        /// </summary>
        public int originalGraphTrackId = -1;
    }
}
