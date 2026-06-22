#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RailwayManager.EditorTools
{
    /// <summary>
    /// MUI-11: zamienia ikonę "ciemna/kolorowa sylwetka na białym tle" (typowy output ChatGPT/DALL-E,
    /// który nie umie przezroczystości) na "biała maska na przezroczystym" — gotową pod kolorowy kafelek
    /// (Wariant B / styl City Bus Manager) i tintowanie przez UITheme.
    ///
    /// <para>Algorytm per piksel: alpha = ramp(białe tło → sylwetka) z anti-aliasingiem na krawędzi,
    /// RGB = biały. Sylwetka (dowolny kolor ciemniejszy od progu) staje się w pełni nieprzezroczysta,
    /// białe tło → przezroczyste. Respektuje istniejącą alphę (działa też na już-przezroczystych inputach).</para>
    ///
    /// <para>Po normalizacji ustawia import: Sprite (2D/UI), alphaIsTransparency, no-mip, Bilinear,
    /// maxSize 512 (ostro do 4K). Plik nadpisywany w miejscu. Działa na zaznaczonych .png LUB folderze.</para>
    /// </summary>
    public static class IconMaskTool
    {
        // Progi luminancji (0..1): poniżej LumOpaque = pełna sylwetka, powyżej LumClear = tło (przezroczyste),
        // pomiędzy = miękka krawędź (AA). Dobrane pod ciemne sylwetki na białym (output ChatGPT).
        const float LumOpaque = 0.90f; // węższe okno = ostrzejsza krawędź maski (mniej miękkiego AA)
        const float LumClear  = 0.94f;
        const int   MaxSize   = 128;   // dość rozdzielczości (mniej pikselozy), no-mip = bez rozmycia

        [MenuItem("Railway Manager/Icons/Normalizuj zaznaczone → biala maska")]
        public static void NormalizeSelected()
        {
            var paths = CollectPngPaths();
            if (paths.Count == 0)
            {
                Debug.LogWarning("[IconMaskTool] Zaznacz pliki .png lub folder z ikonami w oknie Project.");
                return;
            }

            int done = 0;
            foreach (var p in paths)
                if (NormalizeFile(p)) done++;

            AssetDatabase.Refresh();
            foreach (var p in paths)
                ConfigureImporter(p);
            AssetDatabase.Refresh();

            Debug.Log($"[IconMaskTool] Znormalizowano {done}/{paths.Count} ikon → biala maska (Sprite, max {MaxSize}, no-mip).");
        }

        static List<string> CollectPngPaths()
        {
            var result = new List<string>();
            foreach (var o in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(path)) continue;

                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { path }))
                    {
                        string tp = AssetDatabase.GUIDToAssetPath(guid);
                        if (tp.ToLowerInvariant().EndsWith(".png") && !result.Contains(tp))
                            result.Add(tp);
                    }
                }
                else if (path.ToLowerInvariant().EndsWith(".png") && !result.Contains(path))
                {
                    result.Add(path);
                }
            }
            return result;
        }

        static bool NormalizeFile(string assetPath)
        {
            string full = Path.GetFullPath(assetPath);
            if (!File.Exists(full)) return false;

            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!src.LoadImage(File.ReadAllBytes(full)))
            {
                Object.DestroyImmediate(src);
                return false;
            }

            int w = src.width, h = src.height;
            var px = src.GetPixels32();
            Object.DestroyImmediate(src);

            // Idempotencja: jeśli wejście ma już sporo przezroczystości → to gotowa maska (np. ponowne
            // uruchomienie). NIE konwertuj ponownie (lum białej maski ~1 dałby alpha 0 = skasowanie).
            // Konwersję lum→biel robimy tylko dla surowego, nieprzezroczystego obrazu z AI.
            int transparentCount = 0;
            for (int i = 0; i < px.Length; i++)
                if (px[i].a < 128) transparentCount++;
            bool alreadyMask = transparentCount > px.Length * 0.05f;

            if (!alreadyMask)
            {
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    float lum = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                    float srcA = c.a / 255f;
                    float a = Mathf.Clamp01((LumClear - lum) / (LumClear - LumOpaque));
                    a = Mathf.Min(a, srcA);
                    px[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            // Trim przezroczystych marginesów + re-pad do kwadratu z jednolitym marginesem. Bez tego
            // ikona z większym pustym marginesem (różne outputy AI) wygląda mniejsza od innych na kaflu;
            // po trim WSZYSTKIE wypełniają kafelek tak samo.
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a > 6) // niski próg = pełne zaokrąglone końce w bbox (nie obcinane)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }

            int outW, outH;
            Color32[] outPx;
            if (maxX < minX) // nic nieprzezroczystego — nie tnij
            {
                outW = w; outH = h; outPx = px;
            }
            else
            {
                int cw = maxX - minX + 1, ch = maxY - minY + 1;
                int margin = Mathf.RoundToInt(Mathf.Max(cw, ch) * 0.11f); // margines proporcjonalny do większego boku
                // NIE kwadratujemy płótna — równy margines z każdej strony = DOKŁADNE centrowanie
                // (offX/offY = margin), brak błędu ±0.5px → brak asymetrii lewa/prawa.
                outW = cw + margin * 2;
                outH = ch + margin * 2;
                outPx = new Color32[outW * outH]; // domyślnie (0,0,0,0) = transparent
                int offX = margin, offY = margin;
                for (int yy = 0; yy < ch; yy++)
                    for (int xx = 0; xx < cw; xx++)
                        outPx[(offY + yy) * outW + (offX + xx)] = px[(minY + yy) * w + (minX + xx)];
            }

            // ZAWSZE świeża tekstura RGBA32 (LoadImage potrafi dać RGB24 dla PNG bez alpha → gubiona alpha).
            var outTex = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
            outTex.SetPixels32(outPx);
            outTex.Apply();
            File.WriteAllBytes(full, outTex.EncodeToPNG());
            Object.DestroyImmediate(outTex);
            return true;
        }

        static void ConfigureImporter(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter imp) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;           // no-mip = ostro; symetrię daje mały MaxSize (łagodne pomniejszenie), nie mipmapy
            imp.filterMode = FilterMode.Bilinear;
            imp.maxTextureSize = MaxSize;
            imp.SaveAndReimport();
        }
    }
}
#endif
