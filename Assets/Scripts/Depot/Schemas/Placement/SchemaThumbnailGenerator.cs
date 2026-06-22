using System;
using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using RailwayManager.Core.Rendering;

namespace DepotSystem.Schemas.Placement
{
    /// <summary>
    /// Generator thumbnail'a (preview) dla schematu — top-down render geometrii do PNG base64.
    ///
    /// Używany przez:
    /// - MD-6 SchemaSaveDialogUI: render przy "Save as preset" (auto-thumbnail w nowym JSON-ie)
    /// - MD-2 TurnoutSchemaCatalog: render dla built-in JSON-ów (gdy previewPngBase64 puste)
    /// - MD-10 browse panel: preview tile per schema
    ///
    /// Implementacja:
    /// 1. Tworzy temp Camera z orthographic projection (no shadows, no skybox, transparent BG)
    /// 2. Tworzy temp GameObject z LineRenderer per track entry (geometria w lokalnych coords)
    /// 3. Camera lookat top-down nad bounds.center, ortho size = max(bounds.size.x, z) / 2 + padding
    /// 4. Render once to RenderTexture, encode jako PNG bytes → base64 string
    /// 5. Cleanup all temp objects
    ///
    /// 1-time call, ~50-100ms freeze (acceptable wg spec'a "save thumbnail render performance").
    /// </summary>
    public static class SchemaThumbnailGenerator
    {
        public const int DefaultThumbnailSize = 256;
        public const float CameraPaddingFactor = 1.15f;
        public const float CameraHeight = 100f;

        // Kolor toru (zielony jak preview valid)
        private static readonly Color TrackColor = new Color(0.3f, 1.0f, 0.3f, 1f);
        private static readonly Color TurnoutColor = new Color(1.0f, 0.4f, 0.4f, 1f);
        private static readonly Color BackgroundColor = new Color(0.08f, 0.08f, 0.12f, 1f);
        private const float LineWidth = 0.4f;

        /// <summary>
        /// Renderuje thumbnail i zwraca base64-encoded PNG string.
        /// Pusty string gdy renderowanie się nie powiedzie.
        /// </summary>
        public static string RenderThumbnailBase64(SchemaGeometry geometry, int size = DefaultThumbnailSize)
        {
            if (geometry == null)
            {
                Log.Error("[SchemaThumbnailGenerator] Geometry is null");
                return "";
            }
            if (geometry.tracks.Count == 0 && geometry.turnouts.Count == 0)
            {
                Log.Warn("[SchemaThumbnailGenerator] Geometry is empty");
                return "";
            }

            // Compute bounds w lokalnych coords
            Bounds bounds = ComputeRenderBounds(geometry);
            float orthoSize = Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f * CameraPaddingFactor;
            if (orthoSize < 1f) orthoSize = 5f;

            // Temp render objects (na wysokim Y żeby nie kolidować z istniejącą sceną)
            const float renderYOffset = 1000f;
            var rootGO = new GameObject("ThumbnailRender_Temp");
            rootGO.transform.position = new Vector3(0, renderYOffset, 0);

            // BUG-042: deklaracja przed try — żeby finally mogło je sprzątnąć przy exception.
            RenderTexture rt = null;
            Texture2D tex = null;
            GameObject camGO = null;

            try
            {
                // Stwórz LineRenderers per track
                CreateLineRenderers(rootGO.transform, geometry);

                // Camera setup
                camGO = new GameObject("ThumbnailCamera_Temp");
                camGO.transform.position = new Vector3(bounds.center.x, renderYOffset + CameraHeight, bounds.center.z);
                camGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);  // top-down

                var cam = camGO.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = orthoSize;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = BackgroundColor;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = CameraHeight * 2f;
                cam.cullingMask = ~0;  // wszystko (LineRenderers we render w temp scene)
                cam.allowMSAA = true;

                // Render to texture
                rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
                rt.antiAliasing = 2;
                cam.targetTexture = rt;
                CameraRenderUtil.Render(cam);

                // Read pixels do Texture2D
                RenderTexture.active = rt;
                tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;

                // Encode PNG → base64
                byte[] pngBytes = tex.EncodeToPNG();
                string base64 = Convert.ToBase64String(pngBytes);

                Log.Info($"[SchemaThumbnailGenerator] Rendered {size}x{size} thumbnail ({pngBytes.Length} bytes, base64 {base64.Length} chars)");
                return base64;
            }
            catch (Exception e)
            {
                Log.Error($"[SchemaThumbnailGenerator] Render failed: {e.Message}");
                return "";
            }
            finally
            {
                // BUG-042: cleanup w finally — exception po Render fail nie zostawia
                // nieprzydzielonej pamięci RT/Texture/GameObject.
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (camGO != null) UnityEngine.Object.DestroyImmediate(camGO);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (rootGO != null) UnityEngine.Object.DestroyImmediate(rootGO);
            }
        }

        /// <summary>
        /// Renderuje thumbnail jako Texture2D (do live preview w UI, bez encode'owania do PNG).
        /// Caller odpowiada za Destroy texture.
        /// </summary>
        public static Texture2D RenderThumbnailTexture(SchemaGeometry geometry, int size = DefaultThumbnailSize)
        {
            string base64 = RenderThumbnailBase64(geometry, size);
            if (string.IsNullOrEmpty(base64)) return null;

            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                return tex;
            }
            catch (Exception e)
            {
                Log.Error($"[SchemaThumbnailGenerator] Decode failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Decoduje base64 string na Texture2D. Używane przez UI do wyświetlania thumbnail'a
        /// z JSON-a (np. browse panel MD-10).
        /// </summary>
        public static Texture2D DecodeBase64ToTexture(string base64, int defaultSize = DefaultThumbnailSize)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                var tex = new Texture2D(defaultSize, defaultSize, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);  // auto-resize do PNG metadata
                return tex;
            }
            catch (Exception e)
            {
                Log.Error($"[SchemaThumbnailGenerator] DecodeBase64ToTexture failed: {e.Message}");
                return null;
            }
        }

        // ════════════════════════════════════════
        //  PRIVATE HELPERS
        // ════════════════════════════════════════

        private static Bounds ComputeRenderBounds(SchemaGeometry geometry)
        {
            if (geometry.bounds.size.sqrMagnitude > 0.01f)
                return geometry.bounds;

            // Fallback — recompute jeśli puste
            geometry.ComputeBounds();
            if (geometry.bounds.size.sqrMagnitude > 0.01f)
                return geometry.bounds;

            // Nadal puste — fallback na min size
            return new Bounds(Vector3.zero, new Vector3(10, 0, 10));
        }

        private static void CreateLineRenderers(Transform parent, SchemaGeometry geometry)
        {
            var trackMat = MaterialFactory.CreateLine();
            MaterialFactory.SetBaseColor(trackMat, TrackColor);
            var turnoutMat = MaterialFactory.CreateLine();
            MaterialFactory.SetBaseColor(turnoutMat, TurnoutColor);

            // Tory jako LineRenderer'y
            for (int i = 0; i < geometry.tracks.Count; i++)
            {
                var track = geometry.tracks[i];
                if (track.polyline == null || track.polyline.Count < 2) continue;

                var go = new GameObject($"ThumbTrack_{i}");
                go.transform.SetParent(parent, worldPositionStays: false);

                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.startWidth = LineWidth;
                lr.endWidth = LineWidth;
                lr.startColor = TrackColor;
                lr.endColor = TrackColor;
                lr.material = trackMat;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.positionCount = track.polyline.Count;
                lr.SetPositions(track.polyline.ToArray());
            }

            // Rozjazdy jako małe markery (krótkie LineRenderers w +X kierunku z origin)
            for (int i = 0; i < geometry.turnouts.Count; i++)
            {
                var t = geometry.turnouts[i];
                var go = new GameObject($"ThumbTurnout_{i}");
                go.transform.SetParent(parent, worldPositionStays: false);

                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.startWidth = LineWidth * 1.5f;
                lr.endWidth = LineWidth * 0.5f;
                lr.startColor = TurnoutColor;
                lr.endColor = TurnoutColor;
                lr.material = turnoutMat;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.positionCount = 2;
                lr.SetPosition(0, t.origin);
                lr.SetPosition(1, t.origin + t.direction.normalized * 1.5f);
            }
        }
    }
}
