using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem.Schemas.Selection
{
    /// <summary>
    /// Wynik selekcji rectangle drag — zaznaczone obiekty + bounds.
    ///
    /// Używane przez:
    /// - MD-7 SnapshotSelectionTool: tworzony przy każdym update drag'u + finalne ConfirmSelection
    /// - MD-8 SnapshotSerializer: konwersja do lokalnych coords + serializacja JSON
    /// </summary>
    public class SnapshotSelectionResult
    {
        /// <summary>Zaznaczone tory z <c>PrefabTrackBuilder.PlacedTracks</c>.</summary>
        public List<PlacedTrackSegment> selectedTracks = new List<PlacedTrackSegment>();

        /// <summary>Zaznaczone rozjazdy z <c>PrefabTrackBuilder.TurnoutEntities</c>.</summary>
        public List<TurnoutEntity> selectedTurnouts = new List<TurnoutEntity>();

        /// <summary>Bounding box selekcji w world coords (XZ rectangle, Y zawsze 0 dla zajezdni).</summary>
        public Bounds selectionBounds;

        /// <summary>Środek selekcji = bounds.center. Anchor dla snapshot lokalnych coords (MD-8).</summary>
        public Vector3 selectionCenter;

        /// <summary>Brak zaznaczonych obiektów = pusty wynik.</summary>
        public bool IsEmpty => selectedTracks.Count == 0 && selectedTurnouts.Count == 0;

        /// <summary>Łączna liczba elementów (do log/UI).</summary>
        public int TotalCount => selectedTracks.Count + selectedTurnouts.Count;

        public override string ToString()
        {
            return $"SnapshotSelectionResult({selectedTracks.Count} tracks, {selectedTurnouts.Count} turnouts, bounds={selectionBounds.size})";
        }
    }
}
