using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem
{
    /// <summary>
    /// Generator torów równoległych — proste kopie toru źródłowego z offsetem.
    /// Bez rozjazdów! Rozjazdy powstają automatycznie przy budowie toru A→B
    /// z istniejącego toru (snap do node'a).
    /// </summary>
    public class ParallelTrackGenerator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TrackGraph trackGraph;
        [SerializeField] private PrefabTrackBuilder trackBuilder;
        [SerializeField] private CatenaryGenerator catenaryGenerator;

        [Header("Default Settings")]
        [Tooltip("Domyślny odstęp między torami (m)")]
        public float defaultSpacing = 5f;

        void Start()
        {
            if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();
            if (trackBuilder == null) trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
            if (catenaryGenerator == null) catenaryGenerator = DepotServices.Get<CatenaryGenerator>();
        }

        // ═══════════════════════════════════════════
        //  GŁÓWNA METODA
        // ═══════════════════════════════════════════

        /// <summary>
        /// Generuje tory równoległe — kopie toru źródłowego przesunięte o spacing.
        /// Polyline jest offsetowana prostopadle do tangenty w każdym punkcie.
        /// Nie tworzy rozjazdów.
        /// </summary>
        /// <param name="sourceTrackId">Id toru źródłowego w TrackGraph</param>
        /// <param name="leftCount">Ile torów po lewej stronie</param>
        /// <param name="rightCount">Ile torów po prawej stronie</param>
        /// <param name="spacing">Odstęp między torami (m)</param>
        /// <param name="turnoutType">Nieużywane (zachowane dla kompatybilności API)</param>
        /// <param name="withCatenary">Czy tory mają sieć trakcyjną</param>
        /// <returns>Lista graphTrackId wygenerowanych torów</returns>
        public List<int> GenerateParallelTracks(
            int sourceTrackId,
            int leftCount, int rightCount,
            float spacing = 0,
            TurnoutType turnoutType = TurnoutType.R190,
            bool withCatenary = false)
        {
            if (spacing <= 0) spacing = defaultSpacing;
            if (trackGraph == null || trackBuilder == null) return new List<int>();

            // Znajdź polyline toru źródłowego
            List<Vector3> sourcePolyline = GetTrackPolyline(sourceTrackId);
            if (sourcePolyline == null || sourcePolyline.Count < 2)
            {
                Log.Error($"[ParallelTrackGenerator] Track {sourceTrackId} polyline not found!");
                return new List<int>();
            }

            var sourceTrack = trackGraph.GetTrack(sourceTrackId);
            string baseName = sourceTrack != null ? sourceTrack.Name : $"Tor {sourceTrackId}";

            List<int> generatedTrackIds = new();

            // Generuj tory po lewej stronie (offset ujemny — lewo patrząc od początku polyline)
            for (int i = 1; i <= leftCount; i++)
            {
                float offset = -(i * spacing);
                int trackId = GenerateSingleParallelTrack(
                    sourcePolyline, offset, $"{baseName} L{i}", withCatenary);
                if (trackId >= 0)
                    generatedTrackIds.Add(trackId);
            }

            // Generuj tory po prawej stronie (offset dodatni — prawo patrząc od początku polyline)
            for (int i = 1; i <= rightCount; i++)
            {
                float offset = i * spacing;
                int trackId = GenerateSingleParallelTrack(
                    sourcePolyline, offset, $"{baseName} R{i}", withCatenary);
                if (trackId >= 0)
                    generatedTrackIds.Add(trackId);
            }

            Log.Info($"[ParallelTrackGenerator] Generated {leftCount}L + {rightCount}R parallel tracks " +
                      $"for track {sourceTrackId}, spacing={spacing}m");

            return generatedTrackIds;
        }

        // ═══════════════════════════════════════════
        //  GENEROWANIE POJEDYNCZEGO TORU RÓWNOLEGŁEGO
        // ═══════════════════════════════════════════

        /// <summary>
        /// Generuje jeden tor równoległy = offsetowana kopia polyline źródłowej.
        /// Offset prostopadły do tangenty w każdym punkcie.
        /// Endpoints snap-owane do istniejących węzłów (łączy równoległe odcinki proste z łukami).
        /// </summary>
        private int GenerateSingleParallelTrack(
            List<Vector3> sourcePolyline, float offset,
            string name, bool withCatenary)
        {
            // Użyj OffsetPolyline z TrackGeometry
            List<Vector3> offsetPolyline = TrackGeometry.OffsetPolyline(sourcePolyline, offset);

            if (offsetPolyline == null || offsetPolyline.Count < 2)
            {
                Log.Warn($"[ParallelTrackGenerator] Failed to offset polyline for '{name}'");
                return -1;
            }

            // Snap endpoints do istniejących węzłów (z większą tolerancją niż standardowa 0.5m)
            // Dzięki temu równoległy łuk podłączy się do równoległej prostej
            SnapEndpointsToExistingNodes(offsetPolyline);

            float length = TrackGeometry.CalculatePolylineLength(offsetPolyline);
            if (length < TrackGeometry.MIN_TRACK_LENGTH)
            {
                Log.Warn($"[ParallelTrackGenerator] Parallel track too short: {length:F1}m");
                return -1;
            }

            // Zbuduj tor
            var segment = trackBuilder.PlaceTrackWithPolyline(
                offsetPolyline, name, DepotTrackType.Parking);

            if (segment == null || segment.GraphTrackId < 0)
                return -1;

            return segment.GraphTrackId;
        }

        /// <summary>
        /// Snap-uje endpoints polyline do istniejących węzłów grafu z powiększoną tolerancją.
        /// Przy offsetowaniu łuku i prostej ich punkty styku mogą się różnić —
        /// ta metoda wyrównuje je do wspólnego node'a.
        /// </summary>
        private void SnapEndpointsToExistingNodes(List<Vector3> polyline)
        {
            if (trackGraph == null || polyline == null || polyline.Count < 2) return;

            const float parallelSnapTolerance = 1.5f;

            Vector3 originalStart = polyline[0];
            Vector3 originalEnd = polyline[polyline.Count - 1];
            bool startSnapped = false;
            bool endSnapped = false;

            // Snap start
            int startNode = trackGraph.FindNodeAtPosition(polyline[0], parallelSnapTolerance);
            if (startNode >= 0)
            {
                polyline[0] = trackGraph.Nodes[startNode].Position;
                startSnapped = true;
            }

            // Snap end
            int endNode = trackGraph.FindNodeAtPosition(polyline[polyline.Count - 1], parallelSnapTolerance);
            if (endNode >= 0)
            {
                polyline[polyline.Count - 1] = trackGraph.Nodes[endNode].Position;
                endSnapped = true;
            }

            // Jeśli snap przesunął endpoint, a polyline jest prosta — regeneruj wszystkie punkty
            // żeby uniknąć zagięcia między snapniętym endpointem a niezmienionymi punktami pośrednimi
            if ((startSnapped || endSnapped) && TrackGeometry.IsStraightPolyline(polyline, 0.5f))
            {
                Vector3 newStart = polyline[0];
                Vector3 newEnd = polyline[polyline.Count - 1];
                int count = polyline.Count;
                for (int i = 1; i < count - 1; i++)
                {
                    float t = (float)i / (count - 1);
                    polyline[i] = Vector3.Lerp(newStart, newEnd, t);
                }
            }
        }

        // ═══════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════

        /// <summary>
        /// Pobiera polyline toru z PlacedTracks lub TrackGraph.
        /// </summary>
        private List<Vector3> GetTrackPolyline(int trackId)
        {
            // Najpierw szukaj w PlacedTracks (ma pełną polyline)
            if (trackBuilder != null)
            {
                foreach (var placed in trackBuilder.PlacedTracks)
                {
                    if (placed.GraphTrackId == trackId && placed.Polyline != null && placed.Polyline.Count >= 2)
                        return placed.Polyline;
                }
            }

            // Fallback: TrackGraph
            var polyline = trackGraph.GetTrackPolyline(trackId);
            return polyline;
        }
    }

    public enum TurnoutType
    {
        R190,  // R190 1:9
        R300   // R300 1:9
    }
}
