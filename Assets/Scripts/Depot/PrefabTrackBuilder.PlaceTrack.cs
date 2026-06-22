using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    public partial class PrefabTrackBuilder
    {
        // ═══════════════════════════════════════════
        //  PUBLIC PLACEMENT API — visualizacja polyline + register w grafie
        // ═══════════════════════════════════════════

        /// <summary>
        /// Generuje wizualizację toru wzdłuż polyline (podkłady, szyny, podsypkę, collidery).
        /// Obsługuje zarówno proste odcinki jak i łuki.
        /// </summary>
        /// <param name="polyline">Lista punktów trasy (z TrackGeometry)</param>
        /// <param name="graphTrackId">Id toru w TrackGraph (do powiązania)</param>
        /// <returns>PlacedTrackSegment z referencjami</returns>
        public PlacedTrackSegment PlaceTrackVisuals(List<Vector3> polyline, int graphTrackId)
        {
            if (polyline == null || polyline.Count < 2) return null;
            if (sleeperMaterial == null) CreateMaterials();

            EnsureTracksParent();

            float totalLength = TrackGeometry.CalculatePolylineLength(polyline);

            // M-Economy Faza 5: koszt toru wg długości. Bezwarunkowo (blokada CanAfford jest na wejściu
            // gracza — TrackBuildStateMachine); suppress-aware → init/load/removal-rebuild (split/merge) darmowe.
            ConstructionBilling.Charge(ConstructionCosts.TrackGroszy(totalLength), "construction_track", $"tor g{graphTrackId}");

            Vector3 start = polyline[0];
            Vector3 end = polyline[polyline.Count - 1];

            // Obiekt-rodzic dla tego segmentu
            GameObject trackObj = new GameObject($"Track_{placedTracks.Count}");
            trackObj.transform.SetParent(tracksParent);
            trackObj.transform.position = start;
            trackObj.tag = trackTag;

            // 1. Podsypka (ballast) wzdłuż polyline
            GenerateBallast(trackObj.transform, polyline);

            // 2. Podkłady co sleeperSpacing metrów wzdłuż polyline
            GenerateSleepers(trackObj.transform, polyline, totalLength);

            // 3. Szyny (LineRenderer) z offsetem ±trackGauge/2
            float halfGauge = trackGauge / 2f;
            if (railPrefab != null)
            {
                GenerateRailPrefabs(trackObj.transform, polyline, halfGauge, "Left");
                GenerateRailPrefabs(trackObj.transform, polyline, -halfGauge, "Right");
            }
            else
            {
                GenerateRailLineRenderer(trackObj.transform, polyline, halfGauge, "LeftRail");
                GenerateRailLineRenderer(trackObj.transform, polyline, -halfGauge, "RightRail");
            }

            // 4. Collidery wzdłuż polyline
            GenerateColliders(trackObj, polyline, totalLength);

            PlacedTrackSegment segment = new PlacedTrackSegment
            {
                TrackObject = trackObj,
                StartPosition = start,
                EndPosition = end,
                Length = totalLength,
                GraphTrackId = graphTrackId,
                Polyline = new List<Vector3>(polyline)
            };

            placedTracks.Add(segment);
            return segment;
        }

        /// <summary>
        /// Umieszcza prosty segment toru A→B i rejestruje w TrackGraph.
        /// Wrapper na PlaceTrackVisuals z automatyczną geometrią.
        /// </summary>
        public PlacedTrackSegment PlaceTrackSegment(
            Vector3 start, Vector3 end,
            string trackName = null,
            DepotTrackType trackType = DepotTrackType.Parking)
        {
            if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();

            // Oblicz polyline (dla prostego odcinka to po prostu lista punktów co 1m)
            List<Vector3> polyline = TrackGeometry.GenerateStraightLine(start, end);

            // Rejestruj w TrackGraph z GetOrCreateNode
            int graphTrackId = -1;
            if (trackGraph != null)
            {
                int nodeA = trackGraph.GetOrCreateNode(start);
                int nodeB = trackGraph.GetOrCreateNode(end);
                int edgeId = trackGraph.AddEdgeWithPolyline(nodeA, nodeB, polyline);

                string name = trackName ?? $"Tor {placedTracks.Count + 1}";
                graphTrackId = trackGraph.AddTrack(name, trackType, new List<int> { edgeId });
            }

            // Generuj wizualizację
            var segment = PlaceTrackVisuals(polyline, graphTrackId);
            if (segment != null)
            {
                Log.Info($"[PrefabTrackBuilder] Placed track: {segment.Length:F1}m, graphId={graphTrackId}");
            }

            return segment;
        }

        /// <summary>
        /// Umieszcza tor z polyline (krzywy) i rejestruje w TrackGraph.
        /// Używane przez TrackBuildStateMachine i ParallelTrackGenerator.
        /// </summary>
        public PlacedTrackSegment PlaceTrackWithPolyline(
            List<Vector3> polyline,
            string trackName = null,
            DepotTrackType trackType = DepotTrackType.Parking)
        {
            if (polyline == null || polyline.Count < 2) return null;
            if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();

            Vector3 start = polyline[0];
            Vector3 end = polyline[polyline.Count - 1];

            int graphTrackId = -1;
            if (trackGraph != null)
            {
                int nodeA = trackGraph.GetOrCreateNode(start);
                int nodeB = trackGraph.GetOrCreateNode(end);
                int edgeId = trackGraph.AddEdgeWithPolyline(nodeA, nodeB, polyline);

                string name = trackName ?? $"Tor {placedTracks.Count + 1}";
                graphTrackId = trackGraph.AddTrack(name, trackType, new List<int> { edgeId });
            }

            var segment = PlaceTrackVisuals(polyline, graphTrackId);
            if (segment != null)
            {
                Log.Info($"[PrefabTrackBuilder] Placed curved track: {segment.Length:F1}m, graphId={graphTrackId}");

                // Nagraj undo — tylko nieturnout tracks (body/diverging mają własny TurnoutPlacedCommand)
                if (graphTrackId >= 0)
                {
                    DepotSystem.Undo.UndoManager.Record(
                        DepotSystem.Undo.UndoCategory.Tory,
                        new DepotSystem.Undo.TrackPlacedCommand(graphTrackId));
                }
            }

            return segment;
        }
    }
}
