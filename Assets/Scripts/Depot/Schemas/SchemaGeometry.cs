using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem.Schemas
{
    /// <summary>
    /// Wynik generatora schematu — pełna geometria głowicy w lokalnych współrzędnych.
    /// Używana przez:
    /// - <c>SchemaPreviewRenderer</c> (MD-3) — renderuje ghost mesh przed kursorem
    /// - <c>SchemaSnapDetector</c> (MD-4) — szuka multi-endpoint snap kandidatów
    /// - <c>TurnoutSchemaPlacer</c> (MD-3..9) — wywołuje <c>TurnoutPlacer.Place...</c> per turnout entry
    ///   + <c>PrefabTrackBuilder.PlaceTrackWithPolyline</c> per track entry, w global coords
    ///
    /// Lokalne współrzędne:
    /// - <c>(0, 0, 0)</c> = anchor schematu (zwykle początek toru wjazdowego głowicy lub centroid)
    /// - +X = wzdłuż toru wjazdowego
    /// - +Z = lateral (np. tory postojowe drabinki idą w +Z gdy mirror=false)
    /// - Y = 0 (zajezdnia jest płaska, brak różnic wysokości w MVP)
    /// </summary>
    public class SchemaGeometry
    {
        /// <summary>Wszystkie odcinki torów (przewodni + postojowe + wstawki + łuki).</summary>
        public List<SchemaTrackEntry> tracks = new List<SchemaTrackEntry>();

        /// <summary>Wszystkie rozjazdy w schemacie.</summary>
        public List<SchemaTurnoutEntry> turnouts = new List<SchemaTurnoutEntry>();

        /// <summary>
        /// Endpointy schematu — punkty, które mogą się snapować do istniejących torów.
        /// Dla Ladder N-track: 1 wjazd (początek toru przewodniego) + 1 koniec toru przewodniego
        /// + N-1 końców torów postojowych = N+1 endpointów.
        /// </summary>
        public List<Vector3> endpoints = new List<Vector3>();

        /// <summary>
        /// Centroid bounding box wszystkich tracks/turnouts (pozycja kursora przy preview).
        /// Wyliczany przez <see cref="ComputeCentroid"/>.
        /// </summary>
        public Vector3 centroid;

        /// <summary>
        /// Bounding box (min/max XZ) wszystkich elementów. Wyliczany przez <see cref="ComputeBounds"/>.
        /// </summary>
        public Bounds bounds;

        /// <summary>
        /// Wylicza centroid (średnia wszystkich punktów polylines + origins rozjazdów).
        /// </summary>
        public void ComputeCentroid()
        {
            if (tracks.Count == 0 && turnouts.Count == 0)
            {
                centroid = Vector3.zero;
                return;
            }

            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var t in tracks)
            {
                if (t.polyline == null) continue;
                foreach (var p in t.polyline)
                {
                    sum += p;
                    count++;
                }
            }
            foreach (var to in turnouts)
            {
                sum += to.origin;
                count++;
            }
            centroid = count > 0 ? sum / count : Vector3.zero;
        }

        /// <summary>
        /// Wylicza bounding box w XZ (Y zawsze 0).
        /// </summary>
        public void ComputeBounds()
        {
            if (tracks.Count == 0 && turnouts.Count == 0)
            {
                bounds = new Bounds(Vector3.zero, Vector3.zero);
                return;
            }

            Vector3 min = new Vector3(float.MaxValue, 0, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, 0, float.MinValue);

            foreach (var t in tracks)
            {
                if (t.polyline == null) continue;
                foreach (var p in t.polyline)
                {
                    min.x = Mathf.Min(min.x, p.x); min.z = Mathf.Min(min.z, p.z);
                    max.x = Mathf.Max(max.x, p.x); max.z = Mathf.Max(max.z, p.z);
                }
            }
            foreach (var to in turnouts)
            {
                min.x = Mathf.Min(min.x, to.origin.x); min.z = Mathf.Min(min.z, to.origin.z);
                max.x = Mathf.Max(max.x, to.origin.x); max.z = Mathf.Max(max.z, to.origin.z);
            }

            Vector3 size = max - min;
            Vector3 center = (min + max) * 0.5f;
            bounds = new Bounds(center, size);
        }
    }
}
