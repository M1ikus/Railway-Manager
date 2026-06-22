using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    /// <summary>
    /// Fabryka wizualna torów - generuje podkłady, szyny, podsypkę i collidery
    /// wzdłuż dowolnej polyline (proste i krzywe).
    /// Rejestruje tory w TrackGraph z GetOrCreateNode (auto-rozjazdy).
    /// Input handling przeniesiony do TrackBuildStateMachine.
    ///
    /// Klasa rozbita na partial files:
    /// - <c>PrefabTrackBuilder.cs</c>            — pola, lifecycle, materials cache,
    ///                                             ClearAll, EnsureTracksParent,
    ///                                             PlacedTrackSegment data class (ten plik)
    /// - <c>PrefabTrackBuilder.PlaceTrack.cs</c> — public placement API: PlaceTrackVisuals,
    ///                                             PlaceTrackSegment, PlaceTrackWithPolyline
    /// - <c>PrefabTrackBuilder.Generators.cs</c> — visual generators: ballast, sleepers,
    ///                                             rails (LineRenderer/prefabs), colliders
    /// - <c>PrefabTrackBuilder.Turnouts.cs</c>   — turnout registry: RegisterTurnout +
    ///                                             TryGetTurnoutForTrack
    /// - <c>PrefabTrackBuilder.Removal.cs</c>    — RebuildTrackWithPolyline, RemoveTrack(Internal),
    ///                                             RemoveTurnout, RemovePrePostSegments,
    ///                                             RemoveNearestTrack
    /// </summary>
    public partial class PrefabTrackBuilder : MonoBehaviour
    {
        [Header("Track Prefabs")]
        [Tooltip("Prefab szyny 1m (Rail_1m.fbx)")]
        public GameObject railPrefab;

        [Tooltip("Prefab podkładu (Sleeper.fbx)")]
        public GameObject sleeperPrefab;

        [Tooltip("Materiał podsypki/nasypu (Ballast.mat z teksturą tłucznia). Null = runtime placeholder.")]
        public Material ballastMaterialOverride;

        [Header("Track Parameters")]
        [Tooltip("Szerokość toru - rozstaw szyn (m)")]
        public float trackGauge = 1.435f;

        [Tooltip("Odstęp między podkładami (m)")]
        public float sleeperSpacing = 0.6f;

        [Header("Track Collider")]
        [Tooltip("Tag dla obiektów toru")]
        public string trackTag = "Track";

        [Tooltip("Wysokość collidera toru")]
        public float colliderHeight = 0.3f;

        [Tooltip("Szerokość collidera toru")]
        public float colliderWidth = 2.5f;

        [Tooltip("Długość segmentu collidera na łukach (m)")]
        public float colliderSegmentLength = 10f;

        [Header("References")]
        public Transform tracksParent;
        public TrackGraph trackGraph;

        // Materiały (cache)
        private Material sleeperMaterial;
        private Material ballastMaterial;
        private Material railMaterial;

        private List<PlacedTrackSegment> placedTracks = new();

        public IReadOnlyList<PlacedTrackSegment> PlacedTracks => placedTracks;

        // === Rejestr rozjazdów ===
        private Dictionary<int, TurnoutEntity> turnoutEntities = new();
        private Dictionary<int, int> trackIdToTurnoutId = new(); // GraphTrackId → TurnoutId
        private int nextTurnoutId = 0;

        public IReadOnlyDictionary<int, TurnoutEntity> TurnoutEntities => turnoutEntities;

        void Awake()
        {
            CreateMaterials();
        }

        void Start()
        {
            if (tracksParent == null)
            {
                tracksParent = new GameObject("Tracks").transform;
                tracksParent.SetParent(transform);
            }

            if (trackGraph == null)
                trackGraph = DepotServices.Get<TrackGraph>();
        }

        private void CreateMaterials()
        {
            sleeperMaterial = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(sleeperMaterial, new Color(0.45f, 0.3f, 0.15f));
            MaterialFactory.SetMetallicSmoothness(sleeperMaterial, 0f, 0.2f);

            ballastMaterial = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(ballastMaterial, new Color(0.55f, 0.5f, 0.4f));
            MaterialFactory.SetMetallicSmoothness(ballastMaterial, 0f, 0.1f);
            ballastMaterial.SetFloat("_Cull", 0f);   // both sides — nasyp mesh widoczny niezależnie od normali (diag)

            railMaterial = MaterialFactory.CreateLine();
            MaterialFactory.SetBaseColor(railMaterial, new Color(0.5f, 0.5f, 0.55f));
        }

        /// <summary>
        /// Czyści wszystkie tory
        /// </summary>
        public void ClearAll()
        {
            foreach (var track in placedTracks)
            {
                if (track.TrackObject != null)
                    Destroy(track.TrackObject);
            }
            placedTracks.Clear();
            turnoutEntities.Clear();
            trackIdToTurnoutId.Clear();
            nextTurnoutId = 0;
            Log.Info("[PrefabTrackBuilder] Cleared all tracks");
        }

        /// <summary>
        /// Rebuilds procedural track GameObjects from an already restored TrackGraph without
        /// mutating graph topology. Used by DepotSavable after load.
        /// </summary>
        public void RestoreVisualsFromGraph(TrackGraph graph)
        {
            ClearAll();
            if (graph == null) return;

            var tracks = new List<DepotTrackData>(graph.Tracks.Values);
            tracks.Sort((a, b) => a.TrackId.CompareTo(b.TrackId));

            foreach (var track in tracks)
            {
                if (track == null) continue;

                var polyline = graph.GetTrackPolyline(track.TrackId);
                if (polyline == null || polyline.Count < 2)
                    polyline = new List<Vector3> { track.StartPosition, track.EndPosition };

                if (polyline.Count >= 2)
                    PlaceTrackVisuals(polyline, track.TrackId);
            }

            Log.Info($"[PrefabTrackBuilder] Restored {placedTracks.Count} track visual(s) from TrackGraph");
        }

        private void EnsureTracksParent()
        {
            if (tracksParent == null)
            {
                tracksParent = new GameObject("Tracks").transform;
                tracksParent.SetParent(transform);
            }
        }
    }

    [System.Serializable]
    public class PlacedTrackSegment
    {
        public GameObject TrackObject;
        public Vector3 StartPosition;
        public Vector3 EndPosition;
        public float Length;
        public int GraphTrackId = -1;
        public List<Vector3> Polyline;
    }
}
