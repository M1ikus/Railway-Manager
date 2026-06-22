using UnityEngine;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// Procedural generator prostych ikon UI — Texture2D rasterized w runtime (BeforeSceneLoad cache).
    ///
    /// Dlaczego: TTFs w projekcie nie zawierają wielu rzadkich glyphów Unicode (np. U+21BA ↺
    /// rotation arrow, BUG-006). Zamiast bundlować osobny font wektorowy, generujemy ikony
    /// programatycznie (deterministic shape, niezależne od fontu, brak ryzyka licencyjnego).
    ///
    /// Wzór anticlockwise rotation arrow (↺):
    /// - Arc 270° z gap'em u góry (1 godzina to 30°)
    /// - Arrowhead na końcu CCW arc'u (kierunek przeciwny do zegara = wskazuje "do tyłu")
    ///
    /// Long-term: M-UIPolish MUI-11 wprowadzi proper SVG/PNG ikony przez Unity AI Generators.
    /// Ten generator jest fallback'iem do tego czasu.
    /// </summary>
    public static class IconGenerator
    {
        private static Sprite _resetSprite;
        private static Sprite _searchSprite;

        /// <summary>
        /// Anticlockwise rotation arrow icon (zastępuje glyph U+21BA którego nie ma w fontach).
        /// 64×64 px, biały na transparent — kolorowanie przez Image.color tint.
        /// </summary>
        public static Sprite GetResetSprite()
        {
            if (_resetSprite != null) return _resetSprite;
            _resetSprite = CreateResetSprite();
            return _resetSprite;
        }

        /// <summary>
        /// Magnifying glass icon (zastępuje glyph U+1F50D 🔍 którego nie ma w fontach).
        /// Kółko + uchwyt wystający w prawy-dolny róg. 64×64 px, biały na transparent.
        /// </summary>
        public static Sprite GetSearchSprite()
        {
            if (_searchSprite != null) return _searchSprite;
            _searchSprite = CreateSearchSprite();
            return _searchSprite;
        }

        private static Sprite CreateResetSprite()
        {
            const int size = 64;
            const float center = (size - 1) * 0.5f;
            const float radius = size * 0.30f;
            const float thickness = 2.6f;
            // Arc geometry (Unity coords: 0° = +X, CCW positive)
            // Gap u góry (60° otwarcia, od 60° do 120°). Arc widoczny: 120° → 360° → 60° (CCW 300°).
            const float gapStartDeg = 60f;
            const float gapEndDeg = 120f;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[size * size];
            Color32 transparent = new Color32(255, 255, 255, 0);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = transparent;

            // ─── 1. Rasterize arc (white pixels with alpha = AA edge) ───
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float edgeDist = thickness - Mathf.Abs(dist - radius);
                if (edgeDist <= 0f) continue;

                float angleDeg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                if (angleDeg < 0) angleDeg += 360f;

                // Skip gap (between gapStart and gapEnd CCW)
                if (angleDeg >= gapStartDeg && angleDeg <= gapEndDeg) continue;

                byte a = (byte)Mathf.Clamp(edgeDist * 255f, 0f, 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }

            // ─── 2. Rasterize arrowhead at gapEndDeg (CCW arc end, pointing "into" gap) ───
            // Tip pozycja: dokładnie na arc end + lekkie wysunięcie do wewnątrz gap'u
            float tipAngleRad = gapEndDeg * Mathf.Deg2Rad;
            Vector2 arcEndPos = new Vector2(
                center + Mathf.Cos(tipAngleRad) * radius,
                center + Mathf.Sin(tipAngleRad) * radius);

            // Tangent CCW na arc end = (-sin, cos). Arrow tip wskazuje TYŁ (do gap'u) = -tangent.
            Vector2 tangent = new Vector2(-Mathf.Sin(tipAngleRad), Mathf.Cos(tipAngleRad));
            Vector2 normal = new Vector2(Mathf.Cos(tipAngleRad), Mathf.Sin(tipAngleRad));

            const float arrowLen = 7f;
            const float arrowHalfWidth = 4.5f;
            Vector2 tip = arcEndPos - tangent * arrowLen;
            Vector2 baseOuter = arcEndPos + normal * arrowHalfWidth;
            Vector2 baseInner = arcEndPos - normal * arrowHalfWidth;

            DrawTriangle(pixels, size, tip, baseOuter, baseInner);

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            // Sprite z full-rect mesh (no tight-packing artifacts dla małych UI elementów)
            return Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f,
                extrude: 0,
                meshType: SpriteMeshType.FullRect);
        }

        /// <summary>
        /// Rasterize wypełniony trójkąt (a, b, c) jako biały solid pixel + AA na krawędziach.
        /// Algorytm: bounding box + barycentric inside test z floating-point edge distance.
        /// </summary>
        private static void DrawTriangle(Color32[] pixels, int size, Vector2 a, Vector2 b, Vector2 c)
        {
            int xMin = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int xMax = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int yMin = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int yMax = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));

            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                // Sub-pixel sampling 2×2 dla AA na krawędziach trójkąta
                int hits = 0;
                for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                {
                    Vector2 sp = new Vector2(x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f);
                    if (PointInTriangle(sp, a, b, c)) hits++;
                }

                if (hits == 0) continue;

                int idx = y * size + x;
                byte newAlpha = (byte)(hits * 64); // 0/64/128/192/256 (cap 255)
                if (newAlpha > 255) newAlpha = 255;

                Color32 existing = pixels[idx];
                if (newAlpha > existing.a)
                    pixels[idx] = new Color32(255, 255, 255, newAlpha);
            }
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hasNeg && hasPos);
        }

        private static Sprite CreateSearchSprite()
        {
            const int size = 64;
            // Kółko (lupa) lekko przesunięte w lewy-górny róg, żeby uchwyt zmieścił się w prawym-dolnym
            const float circleCx = 25f;
            const float circleCy = 39f; // Unity y-up, więc "góra" = wysokie y
            const float circleR  = 14f;
            const float circleThickness = 2.6f;
            // Uchwyt: linia 45° z brzegu kółka do prawego-dolnego rogu
            const float handleStartAngleDeg = -45f; // -45° = right-down direction (cos+, sin-)
            const float handleLen = 18f;
            const float handleThickness = 3.2f;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 0);

            // ─── 1. Circle (lupa rim) ───
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - circleCx;
                float dy = y - circleCy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float edgeDist = circleThickness - Mathf.Abs(dist - circleR);
                if (edgeDist <= 0f) continue;
                byte a = (byte)Mathf.Clamp(edgeDist * 255f, 0f, 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }

            // ─── 2. Handle (linia z brzegu kółka) ───
            float handleAngleRad = handleStartAngleDeg * Mathf.Deg2Rad;
            Vector2 handleStart = new Vector2(
                circleCx + Mathf.Cos(handleAngleRad) * circleR,
                circleCy + Mathf.Sin(handleAngleRad) * circleR);
            Vector2 handleEnd = new Vector2(
                handleStart.x + Mathf.Cos(handleAngleRad) * handleLen,
                handleStart.y + Mathf.Sin(handleAngleRad) * handleLen);

            DrawLine(pixels, size, handleStart, handleEnd, handleThickness);

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            return Sprite.Create(
                tex,
                new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f,
                extrude: 0,
                meshType: SpriteMeshType.FullRect);
        }

        /// <summary>
        /// Rasterize linię między dwoma punktami z thickness — Distance Field-style edge AA.
        /// </summary>
        private static void DrawLine(Color32[] pixels, int size, Vector2 a, Vector2 b, float thickness)
        {
            int xMin = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - thickness));
            int xMax = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + thickness));
            int yMin = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - thickness));
            int yMax = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + thickness));

            Vector2 ab = b - a;
            float abSq = Vector2.Dot(ab, ab);
            if (abSq < 0.0001f) return;

            for (int y = yMin; y <= yMax; y++)
            for (int x = xMin; x <= xMax; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                Vector2 ap = p - a;
                float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / abSq);
                Vector2 closest = a + ab * t;
                float dist = Vector2.Distance(p, closest);
                float edgeDist = thickness - dist;
                if (edgeDist <= 0f) continue;

                byte newAlpha = (byte)Mathf.Clamp(edgeDist * 255f, 0f, 255f);
                int idx = y * size + x;
                Color32 existing = pixels[idx];
                if (newAlpha > existing.a)
                    pixels[idx] = new Color32(255, 255, 255, newAlpha);
            }
        }
    }
}
