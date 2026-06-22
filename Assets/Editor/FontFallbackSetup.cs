using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using TMPro;

namespace RailwayManager.EditorTools
{
    /// <summary>
    /// Editor utility do setupu Noto Sans / Noto Sans JP / Noto Sans Symbols 2 jako
    /// fallback chain dla LiberationSans SDF (default TMP font).
    ///
    /// Po wrzuceniu TTF do Assets/Fonts/ — kliknij <c>Tools > TMP > Setup Font Fallbacks</c>.
    /// Generuje TMP_FontAsset SDF (dynamic atlas mode — glyphs add'owane on-demand)
    /// per font + dodaje do fallback list.
    ///
    /// Dynamic atlas znaczy że SDF asset zaczyna pusty, glyphy dodawane są przy
    /// pierwszym użyciu w runtime. Zaleta: brak pre-bake'owania wielkich atlasów
    /// (CJK to ~7MB pre-bake, dynamic generuje tylko używane). Wada: pierwsze
    /// pojawienie się glyph'a może mieć drobny stutter.
    ///
    /// MultiAtlas support: gdy atlas się zapełni, Unity tworzy dodatkowy. Konieczne
    /// dla CJK (>3000 unique znaków).
    /// </summary>
    public static class FontFallbackSetup
    {
        private const string DEFAULT_FONT_PATH =
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        private const string OUTPUT_FOLDER = "Assets/Fonts/SDF";

        // Po jednym Regular z każdej rodziny — reszta weights/italic/condensed nie jest
        // potrzebna na EA. M13-3 może rozbudować jeśli dojdą bold/italic w UI.
        private static readonly (string ttfPath, string assetName)[] FontsToSetup = new[]
        {
            ("Assets/Fonts/Noto_Sans/static/NotoSans-Regular.ttf",                "NotoSans-Regular SDF"),
            ("Assets/Fonts/Noto_Sans_JP/static/NotoSansJP-Regular.ttf",           "NotoSansJP-Regular SDF"),
            ("Assets/Fonts/Noto_Sans_Symbols_2/NotoSansSymbols2-Regular.ttf",     "NotoSansSymbols2-Regular SDF"),
        };

        [MenuItem("Tools/TMP/Setup Font Fallbacks")]
        public static void Setup()
        {
            var defaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DEFAULT_FONT_PATH);
            if (defaultFont == null)
            {
                Debug.LogError($"[FontFallbackSetup] Default TMP font nie znaleziony: {DEFAULT_FONT_PATH}");
                return;
            }

            // Upewnij się, że folder wyjściowy istnieje
            if (!AssetDatabase.IsValidFolder(OUTPUT_FOLDER))
            {
                Directory.CreateDirectory(OUTPUT_FOLDER);
                AssetDatabase.Refresh();
            }

            if (defaultFont.fallbackFontAssetTable == null)
                defaultFont.fallbackFontAssetTable = new List<TMP_FontAsset>();

            int created = 0, alreadyExist = 0, addedToFallback = 0;

            foreach (var (ttfPath, assetName) in FontsToSetup)
            {
                if (!File.Exists(ttfPath))
                {
                    Debug.LogWarning($"[FontFallbackSetup] TTF nie znaleziony: {ttfPath} — pominięto");
                    continue;
                }

                string sdfAssetPath = $"{OUTPUT_FOLDER}/{assetName}.asset";
                var sdfAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(sdfAssetPath);

                bool needsRebuild = sdfAsset == null
                    || sdfAsset.atlasTextures == null
                    || sdfAsset.atlasTextures.Length == 0
                    || sdfAsset.atlasTextures[0] == null;

                if (needsRebuild)
                {
                    // Usuń broken asset (np. m_AtlasTextures = null po wcześniejszym setup'ie bez init'u)
                    if (sdfAsset != null)
                    {
                        AssetDatabase.DeleteAsset(sdfAssetPath);
                        Debug.Log($"[FontFallbackSetup] Usunięto broken asset (brak atlas texture): {sdfAssetPath}");
                    }

                    var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
                    if (sourceFont == null)
                    {
                        Debug.LogError($"[FontFallbackSetup] Failed to load Font z {ttfPath}");
                        continue;
                    }

                    // Dynamic atlas — glyphs add'owane on-demand (zero pre-bake)
                    sdfAsset = TMP_FontAsset.CreateFontAsset(
                        sourceFont,
                        samplingPointSize:    90,
                        atlasPadding:         9,
                        renderMode:           GlyphRenderMode.SDFAA,
                        atlasWidth:           2048,
                        atlasHeight:          2048,
                        atlasPopulationMode:  AtlasPopulationMode.Dynamic,
                        enableMultiAtlasSupport: true);

                    sdfAsset.name = assetName;

                    // KRYTYCZNE — kolejność operacji:
                    // 1) CreateAsset najpierw (asset na disk → AddObjectToAsset działa)
                    // 2) TryAddCharacters dopiero potem (tworzy atlas Texture2D)
                    // 3) AddObjectToAsset(texture, sdfAsset) — bez tego Texture2D nie jest sub-asset'em
                    //    i Unity destroy'uje go przy editor reload (MissingReferenceException przy następnym uruchomieniu).
                    AssetDatabase.CreateAsset(sdfAsset, sdfAssetPath);

                    sdfAsset.TryAddCharacters(new uint[] { 32u });

                    if (sdfAsset.atlasTextures != null)
                    {
                        foreach (var tex in sdfAsset.atlasTextures)
                        {
                            if (tex == null) continue;
                            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(tex)))
                                AssetDatabase.AddObjectToAsset(tex, sdfAsset);
                        }
                    }
                    if (sdfAsset.material != null
                        && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(sdfAsset.material)))
                    {
                        AssetDatabase.AddObjectToAsset(sdfAsset.material, sdfAsset);
                    }

                    EditorUtility.SetDirty(sdfAsset);
                    Debug.Log($"[FontFallbackSetup] Utworzono TMP_FontAsset: {sdfAssetPath} (atlas: {sdfAsset.atlasTextures?.Length ?? 0} texture)");
                    created++;
                }
                else
                {
                    Debug.Log($"[FontFallbackSetup] Już istnieje (OK): {sdfAssetPath}");
                    alreadyExist++;
                }

                // Add to fallback chain (skip jeśli już jest)
                if (!defaultFont.fallbackFontAssetTable.Contains(sdfAsset))
                {
                    defaultFont.fallbackFontAssetTable.Add(sdfAsset);
                    addedToFallback++;
                    Debug.Log($"[FontFallbackSetup] Dodano do fallback chain: {sdfAsset.name}");
                }
            }

            EditorUtility.SetDirty(defaultFont);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[FontFallbackSetup] Zakończone. " +
                $"Utworzono: {created}, już istniało: {alreadyExist}, " +
                $"dodano do fallback chain: {addedToFallback}. " +
                $"Łącznie fallback chain: {defaultFont.fallbackFontAssetTable.Count} assetów."
            );
        }

        /// <summary>
        /// Diagnostyka — wypisuje aktualny fallback chain do Console.
        /// </summary>
        [MenuItem("Tools/TMP/Print Fallback Chain")]
        public static void PrintChain()
        {
            var defaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DEFAULT_FONT_PATH);
            if (defaultFont == null)
            {
                Debug.LogError($"[FontFallbackSetup] Default TMP font nie znaleziony: {DEFAULT_FONT_PATH}");
                return;
            }

            var chain = defaultFont.fallbackFontAssetTable;
            if (chain == null || chain.Count == 0)
            {
                Debug.Log($"[FontFallbackSetup] Fallback chain dla '{defaultFont.name}' jest pusty.");
                return;
            }

            Debug.Log($"[FontFallbackSetup] Fallback chain dla '{defaultFont.name}' ({chain.Count} assets):");
            for (int i = 0; i < chain.Count; i++)
            {
                var f = chain[i];
                string status = f != null ? f.name : "<NULL>";
                Debug.Log($"  [{i}] {status}");
            }
        }
    }
}
