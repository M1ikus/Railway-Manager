using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// Dane o łańcuchu połączonych prostych odcinków traktowanych jako jedność.
    /// </summary>
    public class StraightChain
    {
        public List<PlacedTrackSegment> Segments = new();
        public List<Vector3> MergedPolyline = new();
        public float TotalLength;
        public Vector3 StartPos;
        public Vector3 EndPos;
        public Vector3 Direction;
    }

    /// <summary>
    /// Logika dzielenia istniejącego toru prostego i wstawiania rozjazdu.
    /// Obsługuje: R190 1:9, R300 1:9, krzyżowy podwójny R190.
    /// Łańcuchy połączonych odcinków prostych traktowane jako jedność.
    ///
    /// Klasa rozbita na partial files:
    /// - <c>TurnoutPlacer.cs</c>           — pola, lifecycle, StraightChain detection (ten plik)
    /// - <c>TurnoutPlacer.Place.cs</c>     — single turnout (R190/R300): CanPlace, PlaceTurnoutOnChain
    ///                                       (+ Internal), PlaceTurnout, ConvertDistToChain
    /// - <c>TurnoutPlacer.Crossover.cs</c> — rozjazd krzyżowy R190 (X-pattern)
    /// - <c>TurnoutPlacer.Compound.cs</c>  — pair (parallel) + branch with return (arc/turnout)
    /// - <c>TurnoutPlacer.Helpers.cs</c>   — FindParallelTrack, FindTrackNearPoint, IsTurnoutMember
    /// </summary>
    public partial class TurnoutPlacer : MonoBehaviour
    {
        private TrackGraph trackGraph;
        private PrefabTrackBuilder trackBuilder;
        private SnapPointSystem snapSystem;

        void Start()
        {
            trackGraph = DepotServices.Get<TrackGraph>();
            trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            snapSystem = DepotServices.Get<SnapPointSystem>();
        }

        // ═══════════════════════════════════════════
        //  STRAIGHT CHAIN DETECTION
        // ═══════════════════════════════════════════

        /// <summary>
        /// Znajduje łańcuch połączonych odcinków prostych zawierający dany segment.
        /// Idzie w obu kierunkach przez Throughput nodes (2 krawędzie) dopóki tory są proste.
        /// </summary>
        public StraightChain FindStraightChain(PlacedTrackSegment startSegment)
        {
            if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();
            if (trackBuilder == null) trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            if (trackGraph == null || trackBuilder == null) return null;
            if (startSegment == null || startSegment.Polyline == null) return null;
            if (!TrackGeometry.IsStraightPolyline(startSegment.Polyline)) return null;

            // Diverging leg (krzywy) jest już odfiltrowany przez IsStraightPolyline.
            // Body (prosta noga) rozjazdu MOŻE być częścią chain — pozwala stawiać
            // kolejne rozjazdy na prostej nodze istniejącego.

            // Zbierz segmenty w kolejności: od początku łańcucha do końca
            List<PlacedTrackSegment> chain = new() { startSegment };

            // Idź w kierunku "start" (FromNode pierwszego edge)
            ExpandChain(startSegment, chain, forward: false);
            // Idź w kierunku "end" (ToNode ostatniego edge)
            ExpandChain(startSegment, chain, forward: true);

            // Sortuj segmenty wzdłuż kierunku toru
            if (chain.Count > 1)
                SortChainByDirection(chain);

            // Buduj merged polyline
            StraightChain result = new();
            result.Segments = chain;
            result.MergedPolyline = MergePolylines(chain);
            result.TotalLength = TrackGeometry.CalculatePolylineLength(result.MergedPolyline);
            result.StartPos = result.MergedPolyline[0];
            result.EndPos = result.MergedPolyline[result.MergedPolyline.Count - 1];
            result.Direction = (result.EndPos - result.StartPos).normalized;

            return result;
        }

        private void ExpandChain(PlacedTrackSegment from, List<PlacedTrackSegment> chain, bool forward)
        {
            var current = from;

            while (true)
            {
                var trackData = trackGraph.GetTrack(current.GraphTrackId);
                if (trackData == null || trackData.EdgeIds.Count == 0) break;

                // Znajdź node na końcu w danym kierunku
                int edgeId = forward
                    ? trackData.EdgeIds[trackData.EdgeIds.Count - 1]
                    : trackData.EdgeIds[0];

                if (!trackGraph.Edges.ContainsKey(edgeId)) break;
                var edge = trackGraph.Edges[edgeId];

                int boundaryNodeId = forward ? edge.ToNodeId : edge.FromNodeId;
                if (!trackGraph.Nodes.ContainsKey(boundaryNodeId)) break;

                var boundaryNode = trackGraph.Nodes[boundaryNodeId];

                // Throughput = dokładnie 2 krawędzie (przechodzi na wprost)
                if (boundaryNode.EdgeIds.Count != 2) break;

                // Znajdź drugą krawędź (nie tę, z której przyszliśmy)
                int otherEdgeId = -1;
                foreach (int eid in boundaryNode.EdgeIds)
                {
                    if (eid != edgeId) { otherEdgeId = eid; break; }
                }
                if (otherEdgeId < 0) break;

                // Znajdź segment z tą krawędzią
                PlacedTrackSegment neighbor = FindSegmentByEdgeId(otherEdgeId);
                if (neighbor == null) break;
                if (chain.Contains(neighbor)) break;

                // Sąsiad musi być prosty (odfiltruje diverging legs = krzywe)
                if (!TrackGeometry.IsStraightPolyline(neighbor.Polyline)) break;

                // Kierunki muszą być współliniowe (±2°)
                Vector3 curDir = TrackGeometry.GetStartTangent(current.Polyline).normalized;
                Vector3 nDir = TrackGeometry.GetStartTangent(neighbor.Polyline).normalized;
                if (Mathf.Abs(Vector3.Dot(curDir, nDir)) < 0.9994f) break;

                chain.Add(neighbor);
                current = neighbor;
            }
        }

        private PlacedTrackSegment FindSegmentByEdgeId(int edgeId)
        {
            // Szukaj tracka, który zawiera tę krawędź
            foreach (var track in trackBuilder.PlacedTracks)
            {
                if (track.GraphTrackId < 0) continue;
                var trackData = trackGraph.GetTrack(track.GraphTrackId);
                if (trackData == null) continue;
                if (trackData.EdgeIds.Contains(edgeId)) return track;
            }
            return null;
        }

        /// <summary>
        /// Sprawdza czy punkt końcowy może być przedłużony w kierunku chain.
        /// Zwraca true jeśli brak prostych, współliniowych sąsiadów spoza chain
        /// (np. jest tylko łuk odgałęziający — wtedy koniec jest wolny do przedłużenia).
        /// </summary>
        public bool IsEndpointExtensible(Vector3 position, Vector3 collinearDirection, StraightChain chain)
        {
            if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();
            if (trackBuilder == null) trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            int nodeId = trackGraph?.FindNodeAtPosition(position) ?? -1;
            if (nodeId < 0) return false;
            var node = trackGraph.Nodes[nodeId];
            if (node.EdgeIds.Count == 1) return true;

            Vector3 dir = collinearDirection.normalized;
            foreach (int edgeId in node.EdgeIds)
            {
                var seg = FindSegmentByEdgeId(edgeId);
                if (seg == null) continue;
                // Pomijaj segmenty należące do bieżącego chain
                if (chain != null && chain.Segments.Contains(seg)) continue;
                // Pomijaj krzywe (łuki odgałęziające)
                if (!TrackGeometry.IsStraightPolyline(seg.Polyline)) continue;
                // Jeśli prosta i współliniowa → blokuje przedłużenie
                Vector3 segDir = TrackGeometry.GetStartTangent(seg.Polyline).normalized;
                if (Mathf.Abs(Vector3.Dot(segDir, dir)) > 0.9994f)
                    return false;
            }
            return true;
        }

        private void SortChainByDirection(List<PlacedTrackSegment> chain)
        {
            // Posortuj segmenty wzdłuż prostej: rzut StartPosition na kierunek
            if (chain.Count < 2) return;

            Vector3 refDir = TrackGeometry.GetStartTangent(chain[0].Polyline).normalized;
            Vector3 refOrigin = chain[0].StartPosition;

            chain.Sort((a, b) =>
            {
                float projA = Vector3.Dot(a.StartPosition - refOrigin, refDir);
                float projB = Vector3.Dot(b.StartPosition - refOrigin, refDir);
                return projA.CompareTo(projB);
            });
        }

        private List<Vector3> MergePolylines(List<PlacedTrackSegment> chain)
        {
            if (chain.Count == 0) return new List<Vector3>();
            if (chain.Count == 1) return new List<Vector3>(chain[0].Polyline);

            // Sprawdź czy segmenty muszą być odwrócone żeby się zgadzały end→start
            Vector3 chainDir = (chain[chain.Count - 1].EndPosition - chain[0].StartPosition).normalized;

            List<Vector3> merged = new();
            for (int i = 0; i < chain.Count; i++)
            {
                var poly = chain[i].Polyline;

                // Czy polyline idzie w kierunku łańcucha?
                Vector3 segDir = (poly[poly.Count - 1] - poly[0]).normalized;
                bool reversed = Vector3.Dot(segDir, chainDir) < 0;

                int startIdx = reversed ? poly.Count - 1 : 0;
                int endIdx = reversed ? -1 : poly.Count;
                int step = reversed ? -1 : 1;

                for (int j = startIdx; j != endIdx; j += step)
                {
                    // Pomijaj duplikat na złączeniu (pierwszy punkt kolejnego = ostatni punkt poprzedniego)
                    if (merged.Count > 0 && Vector3.Distance(merged[merged.Count - 1], poly[j]) < 0.5f)
                        continue;
                    merged.Add(poly[j]);
                }
            }

            return merged;
        }
    }
}
