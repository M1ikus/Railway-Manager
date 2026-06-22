using UnityEngine;
using UnityEngine.Rendering;

namespace RailwayManager.Core.Rendering
{
    /// <summary>
    /// Render kamery do jej <c>targetTexture</c> na żądanie, pipeline-agnostycznie (M-URP / URP-5).
    ///
    /// Built-in RP: <c>Camera.Render()</c>. URP/SRP: <c>Camera.Render()</c> NIE działa —
    /// trzeba <c>RenderPipeline.SubmitRenderRequest</c>. Używamy <c>RenderPipeline.StandardRequest</c>
    /// (typ z <c>UnityEngine.Rendering</c> core — NIE wymaga referencji do pakietu URP w asmdef),
    /// który URP wspiera. Fallback na <c>Camera.Render()</c> gdy SRP nieaktywny lub nie wspiera żądania.
    ///
    /// Używane przez render-to-RenderTexture on-demand: RouteMapPreview, DepotMinimapUI,
    /// SchemaThumbnailGenerator (kamery z <c>enabled=false</c> + manual render).
    /// </summary>
    public static class CameraRenderUtil
    {
        /// <summary>Renderuje kamerę do jej aktualnego targetTexture (synchronnie, jak Camera.Render()).</summary>
        public static void Render(Camera cam)
        {
            if (cam == null) return;

            if (GraphicsSettings.currentRenderPipeline != null)
            {
                var req = new RenderPipeline.StandardRequest { destination = cam.targetTexture };
                if (RenderPipeline.SupportsRenderRequest(cam, req))
                {
                    RenderPipeline.SubmitRenderRequest(cam, req);
                    return;
                }
            }

            cam.Render();
        }
    }
}
