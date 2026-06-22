using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    /// <summary>
    /// Orkiestrator sieci trakcyjnej — 3-etapowy pipeline:
    /// 1. ZoneClassifier: klasyfikacja torów na strefy (rozjazdy, łuki, równoległe, proste)
    /// 2. WirePathGenerator: logiczne linie przewodów z punktami kontrolnymi
    /// 3. SupportOptimizer: minimalne podpory (słupy/bramki) + CatenaryVisualBuilder
    /// </summary>
    public class CatenaryGenerator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TrackGraph trackGraph;
        [SerializeField] private PrefabTrackBuilder trackBuilder;

        [Header("Materials")]
        public Material poleMaterial;
        public Material armMaterial;
        public Material wireMaterial;

        // Wygenerowana sieć
        private CatenaryNetwork network;
        private Transform catenaryParent;

        public CatenaryNetwork Network => network;

        void Start()
        {
            EnsureSceneRefs();
            CreateMaterials();
        }

        /// <summary>Lazy resolve scene refs przez DepotServices cache.</summary>
        private void EnsureSceneRefs()
        {
            if (trackGraph == null) trackGraph = DepotServices.Get<TrackGraph>();
            if (trackBuilder == null) trackBuilder = DepotServices.Get<PrefabTrackBuilder>();
        }

        /// <summary>
        /// Generuje kompletną sieć trakcyjną dla wszystkich zelektryfikowanych torów.
        /// </summary>
        public void GenerateNetwork()
        {
            EnsureSceneRefs();
            if (trackGraph == null) return;

            var electrifiedIds = GetElectrifiedTrackIds();
            GenerateNetwork(electrifiedIds);
        }

        /// <summary>
        /// Generuje sieć trakcyjną dla podanego zestawu torów.
        /// Pipeline: STREFY → PRZEWODY → PODPORY → WIZUALIZACJA
        /// </summary>
        public void GenerateNetwork(IEnumerable<int> electrifiedTrackIds)
        {
            EnsureSceneRefs();

            ClearExistingNetwork();
            CreateFreshParent();
            CreateMaterials();

            var trackIds = electrifiedTrackIds.ToList();
            if (trackIds.Count == 0)
            {
                Log.Info("[CatenaryGenerator] No electrified tracks, network empty");
                return;
            }

            network = new CatenaryNetwork();
            network.RootGameObject = catenaryParent.gameObject;

            Log.Info($"[CatenaryGenerator] Generating for {trackIds.Count} tracks: " +
                      $"[{string.Join(", ", trackIds)}]");

            try
            {
                // ═══════════════════════════════════
                //  ETAP 1: KLASYFIKACJA STREF
                // ═══════════════════════════════════
                var zones = ZoneClassifier.ClassifyZones(trackGraph, trackBuilder, trackIds);
                network.Zones = zones;

                // ═══════════════════════════════════
                //  ETAP 2: LINIE PRZEWODÓW
                // ═══════════════════════════════════
                var wirePaths = WirePathGenerator.GenerateWirePaths(trackGraph, zones, trackIds);
                network.WirePaths = wirePaths;

                // ═══════════════════════════════════
                //  ETAP 3: OPTYMALIZACJA PODPÓR
                // ═══════════════════════════════════
                var (points, supports) = SupportOptimizer.OptimizeSupports(wirePaths, zones, trackGraph);
                network.Supports = supports;

                if (supports.Count < 1)
                {
                    Log.Warn("[CatenaryGenerator] No supports generated");
                    return;
                }

                // ═══════════════════════════════════
                //  ETAP 4: WIZUALIZACJA
                // ═══════════════════════════════════
                var spans = CatenaryVisualBuilder.CreateWireSpans(points);
                network.WireSpans = spans;

                CatenaryVisualBuilder.BuildAllVisuals(
                    network, catenaryParent, poleMaterial, armMaterial, wireMaterial);

                int poleCount = supports.Count(s => s.Type == SupportType.Pole);
                int gantryCount = supports.Count(s => s.Type == SupportType.Gantry);

                Log.Info($"[CatenaryGenerator] DONE: {poleCount} poles, {gantryCount} gantries, " +
                          $"{spans.Count} wire spans, {zones.Count} zones ({trackIds.Count} tracks)");
            }
            catch (Exception ex)
            {
                Log.Error($"[CatenaryGenerator] Fatal: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ═══════════════════════════════════════════
        //  Utility / lifecycle — publiczne API bez zmian
        // ═══════════════════════════════════════════

        [ContextMenu("Clear All Catenary")]
        public void ClearExistingNetwork()
        {
            if (catenaryParent != null)
            {
                if (Application.isPlaying)
                    Destroy(catenaryParent.gameObject);
                else
                    DestroyImmediate(catenaryParent.gameObject);
            }
            catenaryParent = null;
            network = null;
        }

        private void CreateFreshParent()
        {
            var go = new GameObject("CatenaryNetwork");
            go.transform.SetParent(transform);
            catenaryParent = go.transform;
        }

        public void OnTrackRemoved(int graphTrackId)
        {
            if (trackGraph != null)
            {
                var track = trackGraph.GetTrack(graphTrackId);
                if (track != null && track.HasCatenary)
                {
                    trackGraph.SetTrackCatenary(graphTrackId, false);
                    // M-Economy Faza 5: tor z siecią usunięty → zwrot kosztu sieci (oprócz refundu samego toru).
                    ConstructionBilling.Refund(ConstructionCosts.CatenaryGroszy(TrackLengthM(graphTrackId)), "construction_catenary_refund", "tor usunięty");
                    GenerateNetwork();
                }
            }
        }

        public void ElectrifyTrack(int trackId)
        {
            if (trackGraph == null) return;
            // M-Economy Faza 5: koszt sieci trakcyjnej wg długości toru. Blokada „nie stać → nie buduj"
            // (pomijana przy load — suppress). Charge po oznaczeniu.
            long cost = ConstructionCosts.CatenaryGroszy(TrackLengthM(trackId));
            if (!ConstructionBilling.SuppressCharging && !ConstructionBilling.CanAfford(cost))
            {
                Log.Warn($"[CatenaryGenerator] Brak srodkow na sieć toru {trackId} ({cost / 100} zl) → blocked");
                return;
            }
            trackGraph.SetTrackCatenary(trackId, true);
            ConstructionBilling.Charge(cost, "construction_catenary", $"tor {trackId}");
            GenerateNetwork();
        }

        public void DeelectrifyTrack(int trackId)
        {
            if (trackGraph == null) return;
            trackGraph.SetTrackCatenary(trackId, false);
            // M-Economy Faza 5: zwrot kosztu sieci (de-elektryfikacja).
            ConstructionBilling.Refund(ConstructionCosts.CatenaryGroszy(TrackLengthM(trackId)), "construction_catenary_refund", $"tor {trackId}");
            GenerateNetwork();
        }

        /// <summary>M-Economy Faza 5: długość toru [m] dla kosztu sieci (recompute = refund == charge).</summary>
        private float TrackLengthM(int trackId)
        {
            var poly = trackGraph?.GetTrackPolyline(trackId);
            return poly != null ? TrackGeometry.CalculatePolylineLength(poly) : 0f;
        }

        public List<int> GetElectrifiedTrackIds()
        {
            if (trackGraph == null) return new List<int>();
            return trackGraph.Tracks.Values
                .Where(t => t.HasCatenary)
                .Select(t => t.TrackId)
                .ToList();
        }

        private void CreateMaterials()
        {
            if (poleMaterial == null)
            {
                poleMaterial = MaterialFactory.CreateLit();
                MaterialFactory.SetBaseColor(poleMaterial, new Color(0.45f, 0.45f, 0.5f));
                MaterialFactory.SetMetallicSmoothness(poleMaterial, 0.7f, 0.3f);
            }

            if (armMaterial == null)
            {
                armMaterial = MaterialFactory.CreateLit();
                MaterialFactory.SetBaseColor(armMaterial, new Color(0.35f, 0.35f, 0.4f));
                MaterialFactory.SetMetallicSmoothness(armMaterial, 0.5f, 0.2f);
            }

            if (wireMaterial == null)
            {
                wireMaterial = MaterialFactory.CreateLit();
                MaterialFactory.SetBaseColor(wireMaterial, new Color(0.7f, 0.45f, 0.15f));
                MaterialFactory.SetMetallicSmoothness(wireMaterial, 0.9f, 0.6f);
            }
        }
    }
}
