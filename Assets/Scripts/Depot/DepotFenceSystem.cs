using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem
{
    /// <summary>
    /// Proceduralny system ogrodzenia wokół obszaru budowlanego zajezdni.
    /// Generuje słupki, siatkę, bramę górną (od drogi) i bramę lewą (od torów).
    /// </summary>
    public class DepotFenceSystem : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Referencja do GroundGenerator dla bounds")]
        public GroundGenerator groundGenerator;

        [Header("Fence Settings")]
        [Tooltip("Odległość między słupkami (metry)")]
        public float postSpacing = 3.5f;

        [Tooltip("Wysokość słupka (metry)")]
        public float postHeight = 1.5f;

        [Tooltip("Promień słupka (metry)")]
        public float postRadius = 0.05f;

        [Tooltip("Kolor słupków")]
        public Color postColor = new Color(0.5f, 0.5f, 0.5f);

        [Tooltip("Kolor siatki ogrodzenia")]
        public Color meshColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);

        [Header("Top Gate (employee entrance from road, left side)")]
        [Tooltip("Szerokość bramy górnej (wejście dla pracowników, metry)")]
        public float topGateWidth = 5f;

        [Tooltip("Offset bramy górnej od lewej krawędzi (metry, 0 = przy lewym rogu)")]
        public float topGateOffsetFromLeft = 20f;

        [Header("Left Gate (from tracks)")]
        [Tooltip("Szerokość bramy lewej (od torów, metry)")]
        public float leftGateWidth = 10f;

        [Header("Gate Visuals")]
        [Tooltip("Wysokość belki bramowej")]
        public float gateBeamHeight = 1.5f;

        [Tooltip("Kolor bramy")]
        public Color gateColor = new Color(0.3f, 0.3f, 0.6f);

        [Header("3D Models (przyszłość)")]
        [Tooltip("Prefab sekcji ogrodzenia (jeśli null → generowane proceduralnie)")]
        public GameObject fenceSectionPrefab;

        [Tooltip("Prefab bramy kolejowej (lewa)")]
        public GameObject railwayGatePrefab;

        [Tooltip("Prefab bramy pracowniczej (górna)")]
        public GameObject employeeGatePrefab;

        [Header("Adjustment")]
        [Tooltip("Dodatkowa rotacja dla prefabów (jeśli stoją bokiem)")]
        public float meshRotationOffset = 0f;
        
        [Tooltip("Dodatkowa rotacja dla bram")]
        public float gateRotationOffset = 0f;

        [Header("Visibility")]
        public bool showRailwayGate = true;
        public bool showEmployeeGate = true;

        private GameObject fenceParent;
        private Material postMaterial;
        private Material meshMaterial;
        private Material gateMaterial;

        void Start()
        {
            if (groundGenerator == null)
                groundGenerator = DepotServices.Get<GroundGenerator>();

            if (groundGenerator != null)
                RegenerateFence();
        }

        /// <summary>
        /// Regeneruje całe ogrodzenie na podstawie aktualnych bounds
        /// </summary>
        [ContextMenu("Regenerate Fence")]
        public void RegenerateFence()
        {
            ClearFence();

            if (groundGenerator == null)
            {
                groundGenerator = DepotServices.Get<GroundGenerator>();
                if (groundGenerator == null)
                {
                    Log.Error("[DepotFenceSystem] GroundGenerator not found!");
                    return;
                }
            }

            fenceParent = new GameObject("Fence");
            fenceParent.transform.SetParent(transform);
            fenceParent.transform.localPosition = Vector3.zero;

            CreateMaterials();

            Bounds ba = groundGenerator.BuildableArea;
            float minX = ba.min.x;
            float maxX = ba.max.x;
            float minZ = ba.min.z;
            float maxZ = ba.max.z;

            float topGateCenterX = minX + topGateOffsetFromLeft + topGateWidth / 2f;
            float leftGateCenterZ = (minZ + maxZ) / 2f;

            GenerateFenceSegment(minX, minZ, maxX, minZ, "Fence_Bottom");
            GenerateFenceSegment(maxX, minZ, maxX, maxZ, "Fence_Right");

            float topGateHalf = topGateWidth / 2f;
            GenerateFenceSegment(minX, maxZ, topGateCenterX - topGateHalf, maxZ, "Fence_Top_Left");
            GenerateFenceSegment(topGateCenterX + topGateHalf, maxZ, maxX, maxZ, "Fence_Top_Right");
            
            if (showEmployeeGate)
            {
                GenerateGate(
                    new Vector3(topGateCenterX - topGateHalf, 0, maxZ),
                    new Vector3(topGateCenterX + topGateHalf, 0, maxZ),
                    "Gate_Top",
                    employeeGatePrefab
                );
            }

            float leftGateHalf = leftGateWidth / 2f;
            GenerateFenceSegment(minX, minZ, minX, leftGateCenterZ - leftGateHalf, "Fence_Left_Bottom");
            GenerateFenceSegment(minX, leftGateCenterZ + leftGateHalf, minX, maxZ, "Fence_Left_Top");
            
            if (showRailwayGate)
            {
                GenerateGate(
                    new Vector3(minX, 0, leftGateCenterZ - leftGateHalf),
                    new Vector3(minX, 0, leftGateCenterZ + leftGateHalf),
                    "Gate_Left",
                    railwayGatePrefab
                );
            }

            Log.Info($"[DepotFenceSystem] Fence generated. Top gate at X={topGateCenterX:F0}, Left gate at Z={leftGateCenterZ:F0}");
        }

        private void CreateMaterials()
        {
            postMaterial = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(postMaterial, postColor);
            MaterialFactory.SetMetallicSmoothness(postMaterial, 0.5f, 0.3f);

            meshMaterial = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(meshMaterial, meshColor);
            MaterialFactory.SetMetallicSmoothness(meshMaterial, 0.3f, 0.1f);
            MaterialFactory.SetTransparent(meshMaterial);

            gateMaterial = MaterialFactory.CreateLit();
            MaterialFactory.SetBaseColor(gateMaterial, gateColor);
            MaterialFactory.SetMetallicSmoothness(gateMaterial, 0.6f, 0.4f);
        }

        private void GenerateFenceSegment(float startX, float startZ, float endX, float endZ, string name)
        {
            Vector3 start = new Vector3(startX, 0, startZ);
            Vector3 end = new Vector3(endX, 0, endZ);
            float totalLength = Vector3.Distance(start, end);
            if (totalLength < 0.1f) return;

            if (fenceSectionPrefab != null)
            {
                Vector3 dir = (end - start).normalized;
                int sectionCount = Mathf.Max(1, Mathf.RoundToInt(totalLength / postSpacing));
                float sectionLen = totalLength / sectionCount;
                
                for (int i = 0; i < sectionCount; i++)
                {
                    Vector3 pos = start + dir * (i * sectionLen + sectionLen * 0.5f);
                    var section = Instantiate(fenceSectionPrefab, pos, Quaternion.identity, fenceParent.transform);
                    section.name = $"{name}_Section_{i}";
                    
                    var renderer = section.GetComponentInChildren<Renderer>();
                    if (renderer != null)
                    {
                        Bounds b = renderer.localBounds;
                        bool isLongInX = b.size.x > b.size.z;
                        float meshWidth = isLongInX ? b.size.x : b.size.z;
                        
                        section.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                        section.transform.Rotate(0, 90f + meshRotationOffset, 0);

                        float scaleWidth = sectionLen / meshWidth;
                        float scaleHeight = postHeight / b.size.y;
                        section.transform.localScale = new Vector3(scaleWidth, scaleHeight, scaleWidth);
                        section.transform.position = new Vector3(pos.x, -b.min.y * section.transform.localScale.y, pos.z);
                    }
                }
                return;
            }

            GameObject segment = new GameObject(name);
            segment.transform.SetParent(fenceParent.transform);
            Vector3 direction = (end - start).normalized;
            int postCount = Mathf.Max(2, Mathf.FloorToInt(totalLength / postSpacing) + 1);
            float actualSpacing = totalLength / (postCount - 1);
            for (int i = 0; i < postCount; i++)
            {
                Vector3 pos = start + direction * (i * actualSpacing);
                CreatePost(pos, segment.transform, $"Post_{i}");
            }
            float meshHeight = postHeight * 0.8f;
            float meshBottomY = postHeight * 0.1f;
            GameObject meshQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            meshQuad.name = "FenceMesh";
            meshQuad.transform.SetParent(segment.transform);
            Vector3 center = (start + end) / 2f;
            center.y = meshBottomY + meshHeight / 2f;
            meshQuad.transform.position = center;
            float angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            meshQuad.transform.rotation = Quaternion.Euler(0, angle + 90f, 0);
            meshQuad.transform.localScale = new Vector3(totalLength, meshHeight, 1f);
            meshQuad.GetComponent<MeshRenderer>().material = meshMaterial;
            DestroyImmediate(meshQuad.GetComponent<MeshCollider>());
        }

        private void CreatePost(Vector3 position, Transform parent, string name)
        {
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = name;
            post.transform.SetParent(parent);
            post.transform.position = new Vector3(position.x, postHeight / 2f, position.z);
            post.transform.localScale = new Vector3(postRadius * 2f, postHeight / 2f, postRadius * 2f);
            post.GetComponent<MeshRenderer>().material = postMaterial;
            DestroyImmediate(post.GetComponent<CapsuleCollider>());
        }

        private void GenerateGate(Vector3 leftPost, Vector3 rightPost, string name, GameObject specificPrefab)
        {
            if (specificPrefab != null)
            {
                Vector3 center = (leftPost + rightPost) / 2f;
                Vector3 dir = (rightPost - leftPost).normalized;
                var gate = Instantiate(specificPrefab, center, Quaternion.identity, fenceParent.transform);
                gate.name = name;
                float actualGateWidth = Vector3.Distance(leftPost, rightPost);
                var renderer = gate.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    Bounds b = renderer.localBounds;
                    bool isLongInX = b.size.x > b.size.z;
                    float meshWidth = isLongInX ? b.size.x : b.size.z;
                    gate.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                    gate.transform.Rotate(0, 90f + gateRotationOffset, 0);
                    float scaleWidth = actualGateWidth / meshWidth;
                    float scaleHeight = gateBeamHeight / b.size.y;
                    gate.transform.localScale = new Vector3(scaleWidth, scaleHeight, scaleWidth);
                    gate.transform.position = new Vector3(center.x, -b.min.y * gate.transform.localScale.y, center.z);
                }
                return;
            }
            GameObject gateObj = new GameObject(name);
            gateObj.transform.SetParent(fenceParent.transform);
            GameObject leftPostObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            leftPostObj.name = "GatePost_Left";
            leftPostObj.transform.SetParent(gateObj.transform);
            leftPostObj.transform.position = new Vector3(leftPost.x, gateBeamHeight / 2f, leftPost.z);
            leftPostObj.transform.localScale = new Vector3(0.2f, gateBeamHeight / 2f, 0.2f);
            leftPostObj.GetComponent<MeshRenderer>().material = gateMaterial;
            DestroyImmediate(leftPostObj.GetComponent<CapsuleCollider>());
            GameObject rightPostObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rightPostObj.name = "GatePost_Right";
            rightPostObj.transform.SetParent(gateObj.transform);
            rightPostObj.transform.position = new Vector3(rightPost.x, gateBeamHeight / 2f, rightPost.z);
            rightPostObj.transform.localScale = new Vector3(0.2f, gateBeamHeight / 2f, 0.2f);
            rightPostObj.GetComponent<MeshRenderer>().material = gateMaterial;
            DestroyImmediate(rightPostObj.GetComponent<CapsuleCollider>());
            Vector3 beamCenter = (leftPost + rightPost) / 2f;
            beamCenter.y = gateBeamHeight;
            float gateWidthValue = Vector3.Distance(leftPost, rightPost);
            GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beam.name = "GateBeam";
            beam.transform.SetParent(gateObj.transform);
            beam.transform.position = beamCenter;
            Vector3 dir2 = (rightPost - leftPost).normalized;
            float angle = Mathf.Atan2(dir2.x, dir2.z) * Mathf.Rad2Deg;
            beam.transform.rotation = Quaternion.Euler(0, angle, 0);
            beam.transform.localScale = new Vector3(0.15f, 0.15f, gateWidthValue);
            beam.GetComponent<MeshRenderer>().material = gateMaterial;
            DestroyImmediate(beam.GetComponent<BoxCollider>());
        }

        public Vector3 GetTopGatePosition()
        {
            if (groundGenerator == null) return Vector3.zero;
            Bounds ba = groundGenerator.BuildableArea;
            float gateCenterX = ba.min.x + topGateOffsetFromLeft + topGateWidth / 2f;
            return new Vector3(gateCenterX, 0, ba.max.z);
        }

        public Vector3 GetLeftGatePosition()
        {
            if (groundGenerator == null) return Vector3.zero;
            Bounds ba = groundGenerator.BuildableArea;
            return new Vector3(ba.min.x, 0, ba.center.z);
        }

        [ContextMenu("Clear Fence")]
        public void ClearFence()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name == "Fence" || child.name.StartsWith("Fence_") || child.name.Contains("Gate"))
                {
                    DestroyImmediate(child.gameObject);
                }
            }
            fenceParent = null;
        }
    }
}