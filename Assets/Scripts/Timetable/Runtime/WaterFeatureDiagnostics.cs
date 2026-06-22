using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using MapSystem;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Debug helper: loguje top N largest water meshes w bin (sortowane po area).
    /// Pomaga zweryfikować czy duże akweny (Zatoka Gdańska 360km², Zalew Wiślany 330km²) są w bin.
    /// </summary>
    public class WaterFeatureDiagnostics : MonoBehaviour
    {
        [Tooltip("Log top N largest water meshes.")]
        public int topN = 20;

        [Tooltip("Skanuj raz po N sekundach (tile loading delay).")]
        public float scanDelaySec = 10f;

        [Header("Stackup scan — co nakłada się nad Zatoką Gdańską")]
        [Tooltip("Sample point (world coords) do scan stackup. Default (40000, 275000) = środek Zalewu Wiślanego. " +
                 "Inne przydatne: (10000, 285000) = Zatoka Gdańska center, (-24000, 285000) = Zatoka zachodnia.")]
        public Vector2 stackupSamplePoint = new Vector2(40000, 275000);

        [Tooltip("Include też nasz synthetic water + ground (pod SyntheticWaterRenderer/VoivodeshipGroundRenderer GO).")]
        public bool scanAllScene = true;

        private bool didScan = false;
        private float startTime;

        void Start() { startTime = Time.unscaledTime; }

        void Update()
        {
            if (didScan) return;
            if (Time.unscaledTime - startTime < scanDelaySec) return;
            didScan = true;
            ScanWaterFeatures();
        }

        private void ScanWaterFeatures()
        {
            var mapRenderer = FindAnyObjectByType<MapRenderer>();
            if (mapRenderer == null) { Log.Warn("[WaterDiagnostics] No MapRenderer"); return; }

            var allMeshes = new List<(string path, Vector3 center, Vector3 size, float areaKm2, int verts)>();

            int totalTiles = 0;
            foreach (Transform tile in mapRenderer.transform)
            {
                if (tile == null || !tile.name.StartsWith("Tile_")) continue;
                totalTiles++;

                var layerWater = tile.Find("Layer_Water");
                if (layerWater == null) continue;

                foreach (Transform child in layerWater)
                {
                    if (child == null) continue;
                    var mf = child.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;

                    var b = mf.sharedMesh.bounds;
                    float areaKm2 = (b.size.x / 1000f) * (b.size.z / 1000f);
                    int triCount = mf.sharedMesh.triangles != null ? mf.sharedMesh.triangles.Length / 3 : 0;
                    var mr = child.GetComponent<MeshRenderer>();
                    bool layerActive = layerWater.gameObject.activeSelf;
                    bool meshActive = child.gameObject.activeSelf;
                    bool rendererEnabled = mr != null && mr.enabled;
                    string status = $"v={mf.sharedMesh.vertexCount},t={triCount},layerAct={layerActive},meshAct={meshActive},rendEna={rendererEnabled}";
                    allMeshes.Add(($"{tile.name}/{child.name} [{status}]", b.center, b.size, areaKm2, mf.sharedMesh.vertexCount));
                }
            }

            allMeshes.Sort((a, b) => b.areaKm2.CompareTo(a.areaKm2)); // desc by area

            // SINGLE big log — łatwiej skopiować z Console (Unity nie collapse'uje 1 wiadomości)
            var sb = new System.Text.StringBuilder(2048);
            sb.AppendLine($"[WaterDiagnostics] SUMMARY: {totalTiles} tiles, {allMeshes.Count} water meshes. TOP {topN} by area:");
            int n = Mathf.Min(topN, allMeshes.Count);
            for (int i = 0; i < n; i++)
            {
                var (path, center, size, area, verts) = allMeshes[i];
                sb.AppendLine($"  #{i + 1,2} {path}");
                sb.AppendLine($"      center=({center.x:F0},{center.z:F0}), size=({size.x:F0}x{size.z:F0}), area={area:F1}km²");
            }
            sb.AppendLine("Reference: Zatoka Gdańska ~360km², Zalew Wiślany ~330km², Bałtyk (jeśli OSM) >1000km².");
            sb.AppendLine("Jeśli TOP #1 <200km² → duże akweny NIE w bin (formap odrzuca multipolygon).");

            // === STACKUP SCAN: co jest w punkcie Zatoki Gdańskiej ===
            sb.AppendLine();
            sb.AppendLine($"=== STACKUP @ ({stackupSamplePoint.x:F0}, {stackupSamplePoint.y:F0}) ===");
            var renderers = scanAllScene
                ? Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include)
                : mapRenderer.transform.GetComponentsInChildren<MeshRenderer>(true);

            var stackup = new List<(string path, float y, float areaKm2, bool active, string layerName)>();
            foreach (var mr2 in renderers)
            {
                if (mr2 == null) continue;
                var mf2 = mr2.GetComponent<MeshFilter>();
                if (mf2 == null || mf2.sharedMesh == null) continue;
                var b2 = mf2.sharedMesh.bounds;
                // Apply GO transform to bounds
                var worldCenter = mr2.transform.TransformPoint(b2.center);
                var worldSize = Vector3.Scale(b2.size, mr2.transform.lossyScale);
                float wMinX = worldCenter.x - worldSize.x * 0.5f;
                float wMaxX = worldCenter.x + worldSize.x * 0.5f;
                float wMinZ = worldCenter.z - worldSize.z * 0.5f;
                float wMaxZ = worldCenter.z + worldSize.z * 0.5f;
                // Test czy stackupSamplePoint mieści się w bbox
                if (stackupSamplePoint.x < wMinX || stackupSamplePoint.x > wMaxX) continue;
                if (stackupSamplePoint.y < wMinZ || stackupSamplePoint.y > wMaxZ) continue;
                // Znaleziono! GO na tym punkcie.
                string path = mr2.gameObject.name;
                Transform p = mr2.transform.parent;
                while (p != null) { path = p.name + "/" + path; p = p.parent; }
                float area = (worldSize.x / 1000f) * (worldSize.z / 1000f);
                stackup.Add((path, worldCenter.y, area, mr2.gameObject.activeInHierarchy && mr2.enabled,
                    mr2.sharedMaterial != null ? mr2.sharedMaterial.name : "(no material)"));
            }
            stackup.Sort((a, b) => b.y.CompareTo(a.y)); // desc by Y (top first)
            sb.AppendLine($"Found {stackup.Count} mesh'y pokrywających ten punkt (sort by Y desc = top first):");
            foreach (var (path, y, area, active, layerName) in stackup)
            {
                sb.AppendLine($"  Y={y,7:F1} active={active,-5} area={area,7:F1}km² mat='{layerName}' path={path}");
            }

            Log.Info(sb.ToString());
        }

        [ContextMenu("DEBUG: Rescan now")]
        public void DebugRescan() { didScan = false; startTime = Time.unscaledTime - scanDelaySec - 1; }
    }
}
