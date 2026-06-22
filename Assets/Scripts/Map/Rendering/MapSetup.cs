using UnityEngine;
using MapSystem;
using RailwayManager.Core;

namespace RailwayManager
{
    /// <summary>
    /// MapSetup - Camera auto-positioning and lighting configuration
    /// NO CHANGES NEEDED: Does not interact with geometry data
    /// </summary>
    [RequireComponent(typeof(MapRenderer))]
    public class MapSetup : MonoBehaviour
    {
        [Header("Lighting Fix")]
        [Tooltip("Use flat ambient lighting (no shadows)")]
        public bool useUniformLighting = true;
        
        [Tooltip("Ambient light color")]
        public Color ambientColor = Color.white;
        
        [Header("Camera Settings")]
        [Tooltip("Initial zoom level (0.5 = see full map, 1.0 = zoomed in)")]
        [Range(0.1f, 2.0f)]
        public float initialZoom = 0.6f;
        
        [Tooltip("Camera view angle (90 = top-down, 45 = isometric)")]
        [Range(30f, 90f)]
        public float cameraAngle = 60f;
        
        [Tooltip("Camera rotation around Y axis")]
        [Range(-180f, 180f)]
        public float cameraRotation = 0f;
        
        [Header("Debug")]
        public bool showDebugInfo = false;
        
        private UnityEngine.Camera mainCamera;
        private MapRenderer mapRenderer;
        // BUG-043: cache MapLoader (3-4× FindAnyObjectByType wcześniej — full scene scan).
        private MapLoader _cachedMapLoader;

        private MapLoader GetMapLoader()
        {
            if (_cachedMapLoader == null)
                _cachedMapLoader = FindAnyObjectByType<MapLoader>();
            return _cachedMapLoader;
        }
        
        void Awake()
        {
            mapRenderer = GetComponent<MapRenderer>();

            // Find camera ONLY in same scene — NEVER use Camera.main (that's Depot camera)
            var scene = gameObject.scene;
            if (scene.IsValid())
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    mainCamera = root.GetComponentInChildren<UnityEngine.Camera>(true);
                    if (mainCamera != null) break;
                }
            }

            SetupLighting();
        }

        /// <summary>
        /// Fixes lighting to be uniform across the entire map
        /// Also fixes camera skybox to prevent gradient background
        /// CRITICAL: This runs in Awake() - BEFORE map loads
        /// </summary>
        private void SetupLighting()
        {
            // Configure ONLY the map camera (not Camera.main which may be Depot camera)
            var mapCamera = GetComponentInParent<Camera>();
            if (mapCamera == null)
            {
                // Find camera in same scene
                var scene = gameObject.scene;
                if (scene.IsValid())
                {
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        mapCamera = root.GetComponentInChildren<Camera>(true);
                        if (mapCamera != null) break;
                    }
                }
            }

            if (mapCamera != null)
            {
                mapCamera.clearFlags = CameraClearFlags.SolidColor;
                mapCamera.backgroundColor = Color.black;
            }

            // Disable all lights in MapScene — map uses Unlit materials,
            // and directional lights are GLOBAL (would affect Depot scene too)
            var scene2 = gameObject.scene;
            if (scene2.IsValid())
            {
                foreach (var root in scene2.GetRootGameObjects())
                {
                    foreach (var light in root.GetComponentsInChildren<Light>(true))
                    {
                        light.enabled = false;
                    }
                }
            }

            // NOTE: RenderSettings are GLOBAL — don't change them here.
            // SceneController.ApplyMapLighting() handles this when switching to map.

            if (showDebugInfo)
                Log.Info("[MapSetup] Map camera and lights configured (no global RenderSettings changed)");
        }
        
        /// <summary>
        /// Positions and configures camera for optimal map viewing.
        /// Wywoływane manualnie z ContextMenu "Manually Setup Camera" — nie ma auto-trigger
        /// przy starcie sceny. CameraController jest single owner kamery w typowym flow.
        /// </summary>
        public void SetupCamera(formap.BBox bounds)
        {
            if (mainCamera == null)
                return;
            
            Log.Info("[MapSetup] Configuring camera...");
            
            // Calculate map center
            float centerX = (bounds.MinX + bounds.MaxX) / 2f;
            float centerZ = (bounds.MinY + bounds.MaxY) / 2f;
            
            // Calculate map size
            float sizeX = bounds.MaxX - bounds.MinX;
            float sizeZ = bounds.MaxY - bounds.MinY;
            float maxSize = Mathf.Max(sizeX, sizeZ);
            
            // Switch to orthographic mode
            mainCamera.orthographic = true;
            
            // Calculate orthographic size
            float aspectRatio = mainCamera.aspect;
            float verticalSize = maxSize / 2f;
            float horizontalSize = maxSize / (2f * aspectRatio);
            
            mainCamera.orthographicSize = Mathf.Max(verticalSize, horizontalSize) * initialZoom;
            
            // Position camera above map center
            float cameraHeight = maxSize * 0.8f;
            float angleRad = cameraAngle * Mathf.Deg2Rad;
            float distance = cameraHeight / Mathf.Tan(angleRad);
            
            Vector3 cameraPos = new Vector3(
                centerX,
                cameraHeight,
                centerZ - distance
            );
            
            mainCamera.transform.position = cameraPos;
            
            // Look at map center
            Vector3 lookTarget = new Vector3(centerX, 0, centerZ);
            mainCamera.transform.LookAt(lookTarget);
            mainCamera.transform.RotateAround(lookTarget, Vector3.up, cameraRotation);
            
            // Set clipping planes
            mainCamera.nearClipPlane = 0.1f;
            mainCamera.farClipPlane = maxSize * 2f;
            
            // Configure CameraController bounds
            var cameraController = mainCamera.GetComponent<CameraController>();
            if (cameraController != null)
            {
                cameraController.SetBounds(
                    bounds.MinX, 
                    bounds.MaxX, 
                    bounds.MinY, 
                    bounds.MaxY
                );
                
                if (showDebugInfo)
                    Log.Info("[MapSetup] Camera controller bounds configured");
            }
            else if (showDebugInfo)
            {
                Log.Warn("[MapSetup] No CameraController found!");
            }
            
            if (showDebugInfo)
            {
                Log.Info($"[MapSetup] ✓ Camera configured:");
                Log.Info($"  Position: {mainCamera.transform.position}");
                Log.Info($"  Orthographic Size: {mainCamera.orthographicSize:F0}");
                Log.Info($"  Map Size: {sizeX/1000:F1}km × {sizeZ/1000:F1}km");
            }
        }
        
        /// <summary>
        /// Manual camera reconfiguration (use from Inspector context menu)
        /// </summary>
        [ContextMenu("Manually Setup Camera")]
        public void ReconfigureCamera()
        {
            var mapLoader = GetMapLoader();
            if (mapLoader != null)
            {
                SetupCamera(mapLoader.GlobalBounds);
            }
            else
            {
                Log.Warn("[MapSetup] No MapLoader found!");
            }
        }
        
        [ContextMenu("Reconfigure Lighting")]
        public void ReconfigureLighting()
        {
            SetupLighting();
        }

    }
}