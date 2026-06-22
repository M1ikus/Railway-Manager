using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem.Schemas.Snapshot
{
    /// <summary>
    /// Konwerter <see cref="SnapshotGeometry"/> (JSON-serializable POCO) → <see cref="SchemaGeometry"/>
    /// (runtime POCO używany przez SchemaPreviewRenderer + SchemaThumbnailGenerator + SchemaSnapDetector).
    ///
    /// Strukturalnie identyczne (tracks/turnouts/endpoints), ale różne typy entries:
    /// - SnapshotTrackEntry → SchemaTrackEntry (Vector3[] polyline → List<Vector3>)
    /// - SnapshotTurnoutEntry → SchemaTurnoutEntry (originLocal → origin, direction zostaje)
    /// </summary>
    public static class SnapshotToSchemaGeometryConverter
    {
        public static SchemaGeometry Convert(SnapshotGeometry snapshot)
        {
            if (snapshot == null) return new SchemaGeometry();

            var geom = new SchemaGeometry();

            // Tracks
            if (snapshot.tracks != null)
            {
                foreach (var src in snapshot.tracks)
                {
                    if (src == null || src.polyline == null || src.polyline.Length < 2) continue;
                    geom.tracks.Add(new SchemaTrackEntry
                    {
                        polyline = new List<Vector3>(src.polyline),
                        trackTypeName = src.trackTypeName,
                        name = src.name,
                    });
                }
            }

            // Turnouts
            if (snapshot.turnouts != null)
            {
                foreach (var src in snapshot.turnouts)
                {
                    if (src == null) continue;
                    geom.turnouts.Add(new SchemaTurnoutEntry
                    {
                        turnoutTypeName = src.turnoutTypeName,
                        origin = src.originLocal,
                        direction = src.direction,
                        divergeLeft = src.divergeLeft,
                        flipDirection = src.flipDirection,
                        name = src.name,
                    });
                }
            }

            // Endpoints
            if (snapshot.endpoints != null)
            {
                geom.endpoints.AddRange(snapshot.endpoints);
            }

            geom.ComputeCentroid();
            geom.ComputeBounds();

            return geom;
        }
    }
}
