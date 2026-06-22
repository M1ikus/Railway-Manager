#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Unity.VectorGraphics;
using UnityEditor;
using UnityEngine;

namespace RailwayManager.EditorTools
{
    /// <summary>
    /// MUI-11 pipeline ikon (model City Bus Manager): rasteryzuje zrodlowy <c>.svg</c> do PNG
    /// o stalej rozdzielczosci i konfiguruje import jako Sprite (RGBA32 nieskompresowany,
    /// mipmapy + Trilinear) — dokladnie jak robi CBM (pre-rasteryzowany PNG, oversampling, brak
    /// kompresji). Dzieki temu w RUNTIME nie ma zadnej rasteryzacji ani mipmap-rebuildu:
    /// <see cref="RailwayManager.SharedUI.UiIcons.Get"/> bierze gotowy Sprite przez
    /// <c>Resources.Load&lt;Sprite&gt;</c> — caly katalog jedzie jednym czystym importem.
    ///
    /// <para><b>Render przez VectorUtils renderuje SAMA TRESC</b> sprite'a (bounds geometrii),
    /// wiec PNG jest content-tight (bez pustego marginesu viewBox 1024) i wypelnia kafel jak w CBM.</para>
    ///
    /// <para>SVG zostaje jako zrodlo w <c>Assets/Art/Icons/</c>, PNG laduje do
    /// <c>Assets/Resources/Icons/</c>. Zrodla NIE moga zostac w Resources, bo
    /// <c>Resources.Load</c> kolidowaloby (svg→Texture2D vs png→Sprite o tej samej nazwie).</para>
    /// </summary>
    public static class SvgToPngBaker
    {
        const int    TargetSize = 256; // dluzszy bok PNG (CBM: ~125-164px; 256 = zapas pod 4K + mipmapy)
        const int    AntiAlias  = 8;   // supersampling rasteryzacji wektora (gladkie krawedzie)
        const string OutFolder  = "Assets/Resources/Icons";
        const string SrcFolder  = "Assets/Art/Icons";
        const string VectorMat  = "Packages/com.unity.vectorgraphics/Runtime/Materials/Unlit_Vector.mat";

        [MenuItem("Railway Manager/Icons/Bake SVG -> PNG (CBM)")]
        public static void BakeSelected()
        {
            var svgPaths = new List<string>();
            foreach (var o in Selection.objects)
            {
                string p = AssetDatabase.GetAssetPath(o);
                if (!string.IsNullOrEmpty(p) && p.ToLowerInvariant().EndsWith(".svg"))
                    svgPaths.Add(p);
            }
            if (svgPaths.Count == 0)
            {
                Debug.LogWarning("[SvgToPngBaker] Zaznacz pliki .svg w oknie Project.");
                return;
            }

            if (!Directory.Exists(SrcFolder)) Directory.CreateDirectory(SrcFolder);
            if (!Directory.Exists(OutFolder)) Directory.CreateDirectory(OutFolder);

            int done = 0;
            foreach (var svgPath in svgPaths)
                if (Bake(svgPath)) done++;

            AssetDatabase.Refresh();
            Debug.Log($"[SvgToPngBaker] Zbakowano {done}/{svgPaths.Count} ikon → PNG " +
                      $"(Sprite, dluzszy bok {TargetSize}px, RGBA32, mip+Trilinear).");
        }

        static bool Bake(string svgPath)
        {
            string id = Path.GetFileNameWithoutExtension(svgPath);

            // 1. Parsuj SVG → tesseluj → zbuduj sprite (content-bounded: bounds = sama geometria)
            SVGParser.SceneInfo sceneInfo;
            using (var reader = new StreamReader(svgPath))
                sceneInfo = SVGParser.ImportSVG(reader);

            var tess = new VectorUtils.TessellationOptions
            {
                StepDistance         = 1f,
                MaxCordDeviation     = 0.5f,
                MaxTanAngleDeviation = 0.1f,
                SamplingStepSize     = 0.01f,
            };
            var geoms = VectorUtils.TessellateScene(sceneInfo.Scene, tess);
            var sprite = VectorUtils.BuildSprite(geoms, 100f, VectorUtils.Alignment.Center, Vector2.zero, 128);
            if (sprite == null)
            {
                Debug.LogError($"[SvgToPngBaker] BuildSprite zwrocil null dla '{id}'.");
                return false;
            }

            // 2. Rozmiar PNG zachowujacy proporcje tresci (dluzszy bok = TargetSize)
            Vector3 b = sprite.bounds.size;
            float aspect = (b.y > 0f) ? b.x / b.y : 1f;
            int w, h;
            if (aspect >= 1f) { w = TargetSize; h = Mathf.Max(1, Mathf.RoundToInt(TargetSize / aspect)); }
            else              { h = TargetSize; w = Mathf.Max(1, Mathf.RoundToInt(TargetSize * aspect)); }

            // 3. Rasteryzuj sprite → Texture2D (Unlit_Vector material, supersampling AA)
            var mat = AssetDatabase.LoadAssetAtPath<Material>(VectorMat);
            var tex = VectorUtils.RenderSpriteToTexture2D(sprite, w, h, mat, AntiAlias);
            Object.DestroyImmediate(sprite);
            if (tex == null)
            {
                Debug.LogError($"[SvgToPngBaker] RenderSpriteToTexture2D zwrocil null dla '{id}'.");
                return false;
            }

            // 4. Zapisz PNG
            string pngPath = $"{OutFolder}/{id}.png";
            File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            // 5. Przenies zrodlowy SVG poza Resources (uniknij kolizji nazw przy Resources.Load)
            string normalized = svgPath.Replace('\\', '/');
            if (normalized.StartsWith("Assets/Resources/"))
            {
                string destSvg = AssetDatabase.GenerateUniqueAssetPath($"{SrcFolder}/{id}.svg");
                string moveErr = AssetDatabase.MoveAsset(svgPath, destSvg);
                if (!string.IsNullOrEmpty(moveErr))
                    Debug.LogWarning($"[SvgToPngBaker] Nie przeniesiono '{svgPath}': {moveErr}");
            }

            // 6. Skonfiguruj import PNG (model CBM: Sprite, RGBA32, mip + Trilinear, NPOT bez skalowania)
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(pngPath) is TextureImporter imp)
            {
                imp.textureType         = TextureImporterType.Sprite;
                imp.spriteImportMode    = SpriteImportMode.Single;
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled       = true;
                imp.filterMode          = FilterMode.Trilinear;
                imp.wrapMode            = TextureWrapMode.Clamp;
                imp.npotScale           = TextureImporterNPOTScale.None; // NIE skaluj NPOT do POT (zniekształciłoby)
                imp.textureCompression  = TextureImporterCompression.Uncompressed; // RGBA32 jak CBM
                imp.maxTextureSize      = 512;
                imp.SaveAndReimport();
            }
            return true;
        }
    }
}
#endif
