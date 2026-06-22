using System.Collections.Generic;
using UnityEngine;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// MUI-11: centralny loader kolorowych ikon UI z <c>Resources/Icons/</c>.
    ///
    /// <para>Wbudowany moduł Vector Graphics (Unity 6) importuje <c>.svg</c> jako
    /// <b>Texture2D</b> (jedyna sensowna opcja dla uGUI — druga to UI Toolkit VectorImage).
    /// uGUI <see cref="UnityEngine.UI.Image"/> potrzebuje <see cref="Sprite"/>, więc budujemy
    /// Sprite z tekstury w runtime + cache'ujemy (wzór jak <see cref="IconGenerator"/>).</para>
    ///
    /// <para>Zwraca <c>null</c> gdy brak pliku — caller (np. <c>CreateOptionButton</c>,
    /// <c>BuildMenuUI</c>) robi wtedy fallback do glifu TMP. Dzięki temu wpinanie ikon jest
    /// przyrostowe: dopóki sprite'a nie ma, przycisk pokazuje stary 3-literowy tekst.</para>
    ///
    /// <para><b>Tint:</b> ikony są KOLOROWE — NIE tintujemy ich barwą (to zabiłoby
    /// wielokolorowość). Konsument może co najwyżej przygasić alpha dla stanu disabled.</para>
    /// </summary>
    public static class UiIcons
    {
        private const string ResourceFolder = "Icons/";
        private static readonly Dictionary<string, Sprite> _cache = new();

        /// <summary>
        /// Zwraca Sprite dla <paramref name="id"/> (nazwa pliku bez rozszerzenia, np.
        /// <c>"ico_toolbar_track"</c>) lub <c>null</c> gdy brak assetu w <c>Resources/Icons/</c>.
        /// </summary>
        public static Sprite Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            if (_cache.TryGetValue(id, out var cached))
                return cached;

            // Najpierw spróbuj jako Sprite (gdyby kiedyś import dawał Sprite bezpośrednio),
            // potem jako Texture2D (aktualna ścieżka: SVG → Texture2D).
            Sprite sprite = Resources.Load<Sprite>(ResourceFolder + id);
            if (sprite == null)
            {
                var tex = Resources.Load<Texture2D>(ResourceFolder + id);
                if (tex != null)
                    sprite = BuildMipmappedSprite(tex, id);
            }

            // Cache'ujemy tylko sukces — miss nie blokuje (gracz może doimportować asset
            // później w edytorze i kolejny Get spróbuje ponownie).
            if (sprite != null)
                _cache[id] = sprite;

            return sprite;
        }

        /// <summary>
        /// Buduje Sprite z mipmapami + Trilinear z tekstury bez mipmap (importer SVG ich nie
        /// generuje). To technika CBM: duża tekstura (512) jest pomniejszana do ~66px kafla —
        /// bez mipmap bilinear PODPRÓBKOWUJE cienkie linie (czyta 2 z ~8 texeli szyny) → "raz
        /// grubsza, raz cieńsza". Mipmapy = Unity liczy z góry poprawnie przefiltrowane mniejsze
        /// poziomy, Trilinear blenduje między nimi → równomierny downscale na każdej rozdzielczości.
        ///
        /// <para>Blit→ReadPixels robi kopię działającą też dla non-readable source (import SVG).</para>
        /// </summary>
        private static Sprite BuildMipmappedSprite(Texture2D src, string id)
        {
            int w = src.width, h = src.height;

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            var mipped = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: true)
            {
                name = id + "_mip",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            mipped.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
            var px = mipped.GetPixels32();               // odczyt PRZED makeNoLongerReadable (do bbox)
            mipped.Apply(updateMipmaps: true, makeNoLongerReadable: true);

            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);

            // Przytnij przezroczysty margines: SVG ma zwykle viewBox 1024×1024, a sama ikona zajmuje
            // ~1/3 płótna (reszta puste pole). Bez trim ikona tonie w kaflu i renderuje się malutka
            // (cienkie ~1-2px cechy = maksymalna wrażliwość na sub-pixel → "raz lewa, raz prawa grubsza").
            // Po trim treść WYPEŁNIA kafel (jak w CBM) → cechy ~2× większe → równiej i ostrzej.
            Rect contentRect = ComputeContentRect(px, w, h);

            var sprite = Sprite.Create(mipped, contentRect, new Vector2(0.5f, 0.5f), pixelsPerUnit: 100f);
            sprite.name = id;
            return sprite;
        }

        /// <summary>
        /// BBox nieprzezroczystych pikseli + delikatny jednolity margines, w pikselach tekstury
        /// (origin dolny-lewy, zgodnie z konwencją <see cref="Sprite.Create(Texture2D, Rect, Vector2, float)"/>).
        /// Gdy nic nieprzezroczystego — zwraca cały prostokąt (nie tnie).
        /// </summary>
        private static Rect ComputeContentRect(Color32[] px, int w, int h)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (px[row + x].a > 8)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX < minX)
                return new Rect(0f, 0f, w, h);

            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            int pad = Mathf.RoundToInt(Mathf.Max(cw, ch) * 0.06f); // żeby cechy nie dotykały samej krawędzi
            int x0 = Mathf.Max(0, minX - pad);
            int y0 = Mathf.Max(0, minY - pad);
            int x1 = Mathf.Min(w, maxX + 1 + pad);
            int y1 = Mathf.Min(h, maxY + 1 + pad);
            return new Rect(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>Czyści cache (np. po reimportcie assetów w edytorze). Rzadko potrzebne.</summary>
        public static void ClearCache() => _cache.Clear();
    }
}
