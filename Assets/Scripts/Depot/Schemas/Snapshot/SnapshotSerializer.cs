using System.Collections.Generic;
using UnityEngine;
using DepotSystem.Schemas.Selection;
using RailwayManager.Core;

namespace DepotSystem.Schemas.Snapshot
{
    /// <summary>
    /// MD-8 — serializer dla snapshot mode. Konwertuje <see cref="SnapshotSelectionResult"/>
    /// (wybrane PlacedTrackSegment + TurnoutEntity z PrefabTrackBuilder) do
    /// <see cref="SnapshotGeometry"/> (JSON-serializable, lokalne coords).
    ///
    /// Algorytm:
    /// 1. Anchor = selectionCenter (centroid bbox)
    /// 2. Per tor: subtract anchor z każdego punktu polyline → lokalne coords
    /// 3. Per rozjazd: subtract anchor z origin → lokalne coords. Direction zostaje (znormalizowany)
    /// 4. Map TurnoutEntity.DefinitionName → SchemaTurnoutType (R190/R300/Crossover_R190)
    /// 5. Detect endpoints: punkty z tracks które występują tylko 1 raz (open ends)
    ///
    /// TD-004 (2026-06-15): tory są CLIP'owane do prostokąta selekcji (Wariant A) przez
    /// <see cref="SnapshotClipper.ClipPolylineToRectXZ"/> — tor wystający poza rectangle jest
    /// przycinany na granicy, a punkt cięcia staje się nowym endpointem (DetectEndpoints liczy go
    /// jako open end). Tor w całości wewnątrz pozostaje nietknięty (fast-path w clipperze).
    /// </summary>
    public static class SnapshotSerializer
    {
        /// <summary>Tolerance dla deduplikacji endpointów (m).</summary>
        public const float EndpointDedupTolerance = 0.5f;

        public static SnapshotGeometry Serialize(SnapshotSelectionResult selection)
        {
            if (selection == null || selection.IsEmpty)
            {
                Log.Warn("[SnapshotSerializer] Empty selection — skipping");
                return null;
            }

            var anchor = selection.selectionCenter;

            // Tracks → SnapshotTrackEntry (TD-004: clip do prostokąta selekcji, 1 tor → 0..N fragmentów)
            var tracks = new List<SnapshotTrackEntry>(selection.selectedTracks.Count);
            foreach (var seg in selection.selectedTracks)
            {
                if (seg == null || seg.Polyline == null || seg.Polyline.Count < 2) continue;

                var fragments = SnapshotClipper.ClipPolylineToRectXZ(seg.Polyline, selection.selectionBounds);
                if (fragments.Count == 0) continue;  // tor w całości poza prostokątem (defensywnie)

                string baseName = seg.GraphTrackId >= 0 ? $"Track_{seg.GraphTrackId}" : "Track";
                for (int f = 0; f < fragments.Count; f++)
                {
                    var frag = fragments[f];
                    if (frag.Count < 2) continue;

                    var localPolyline = new Vector3[frag.Count];
                    for (int i = 0; i < frag.Count; i++)
                        localPolyline[i] = frag[i] - anchor;

                    tracks.Add(new SnapshotTrackEntry
                    {
                        polyline = localPolyline,
                        trackTypeName = "Parking",   // MVP — bez lookupu DepotTrackType, default
                        name = fragments.Count > 1 ? $"{baseName}_{f}" : baseName,
                        originalGraphTrackId = seg.GraphTrackId,
                    });
                }
            }

            // Turnouts → SnapshotTurnoutEntry
            var turnouts = new List<SnapshotTurnoutEntry>(selection.selectedTurnouts.Count);
            foreach (var t in selection.selectedTurnouts)
            {
                if (t == null) continue;
                turnouts.Add(new SnapshotTurnoutEntry
                {
                    turnoutTypeName = MapTurnoutTypeName(t.DefinitionName),
                    originLocal = t.Origin - anchor,
                    direction = t.Direction.normalized,
                    divergeLeft = t.DivergeLeft,
                    flipDirection = t.FlipDirection,
                    name = $"Turnout_{t.TurnoutId}",
                });
            }

            // Detect endpoints — punkty z tracks które występują tylko 1 raz
            var endpoints = DetectEndpoints(tracks);

            var snapshot = new SnapshotGeometry
            {
                anchorPoint = anchor,
                tracks = tracks.ToArray(),
                turnouts = turnouts.ToArray(),
                endpoints = endpoints,
            };

            Log.Info($"[SnapshotSerializer] Serialized: {tracks.Count} tracks, {turnouts.Count} turnouts, {endpoints.Length} endpoints (anchor={anchor})");
            return snapshot;
        }

        /// <summary>
        /// Mapuje <see cref="TurnoutEntity.DefinitionName"/> ("R190 1:9", "R300 1:9", "Krzyżowy R190")
        /// na <c>SchemaTurnoutType</c> ("R190" / "R300" / "Crossover_R190").
        ///
        /// Fallback "R190" gdy nazwa nieznana.
        /// </summary>
        public static string MapTurnoutTypeName(string definitionName)
        {
            if (string.IsNullOrEmpty(definitionName)) return SchemaTurnoutType.R190;

            // Match dokładnie "R190 1:9" / "R300 1:9" / "Krzyżowy R190"
            if (definitionName == "R190 1:9") return SchemaTurnoutType.R190;
            if (definitionName == "R300 1:9") return SchemaTurnoutType.R300;
            if (definitionName == "Krzyżowy R190") return SchemaTurnoutType.Crossover_R190;

            // Fallback po prefix'ie (gdy dorzucimy nowe typy w przyszłości)
            if (definitionName.StartsWith("R190")) return SchemaTurnoutType.R190;
            if (definitionName.StartsWith("R300")) return SchemaTurnoutType.R300;
            if (definitionName.Contains("Krzyż")) return SchemaTurnoutType.Crossover_R190;

            Log.Warn($"[SnapshotSerializer] Unknown turnout definition '{definitionName}', fallback to R190");
            return SchemaTurnoutType.R190;
        }

        /// <summary>
        /// Detekcja endpointów — punkty z polyline'ów które występują tylko 1 raz w skali całego
        /// schematu. Junction'y wewnątrz schematu (punkt = end jednego toru = start drugiego)
        /// występują 2+ razy → NIE są endpointami.
        ///
        /// Tolerance 0.5m (EndpointDedupTolerance) — punkty bliskie sobie (np. snap pointy w grafie)
        /// liczone jako ten sam punkt.
        /// </summary>
        private static Vector3[] DetectEndpoints(List<SnapshotTrackEntry> tracks)
        {
            // Zbierz wszystkie endpoint candidates (start + end każdego toru)
            var candidates = new List<Vector3>();
            foreach (var track in tracks)
            {
                if (track.polyline == null || track.polyline.Length < 2) continue;
                candidates.Add(track.polyline[0]);
                candidates.Add(track.polyline[track.polyline.Length - 1]);
            }

            // Liczenie wystąpień (z tolerance dedup)
            var counts = new Dictionary<int, (Vector3 pos, int count)>();
            foreach (var c in candidates)
            {
                bool merged = false;
                foreach (var key in new List<int>(counts.Keys))
                {
                    if (Vector3.Distance(counts[key].pos, c) <= EndpointDedupTolerance)
                    {
                        counts[key] = (counts[key].pos, counts[key].count + 1);
                        merged = true;
                        break;
                    }
                }
                if (!merged)
                {
                    counts[counts.Count] = (c, 1);
                }
            }

            // Endpoints = te z count == 1
            var endpoints = new List<Vector3>();
            foreach (var kvp in counts)
            {
                if (kvp.Value.count == 1)
                    endpoints.Add(kvp.Value.pos);
            }

            return endpoints.ToArray();
        }
    }
}
