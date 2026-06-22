using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using RailwayManager.Core;

namespace RailwayManager.SharedUI
{
    /// <summary>
    /// MUI-1 partial UITheme — font assets + runtime TMP font bootstrap.
    /// Wydzielone z <see cref="TopBarUI"/>.cs 2026-05-15.
    ///
    /// <para><b>BUG-006 background:</b> Unity "Setup Font Fallbacks" tworzy puste sloty
    /// fileID:0 w `fallbackFontAssetTable`, przez co glyphy U+21BA/✓/⚠ nie renderują.
    /// Bootstrap explicit ładuje NotoSans/NotoSansSymbols2/NotoSansJP z `Resources/Fonts/`.</para>
    /// </summary>
    public static partial class UITheme
    {
        private static Font _legacyFont;
        private static TMP_FontAsset _tmpFont;

        public static Font LegacyFont => _legacyFont ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootstrapTmpFont()
        {
            _tmpFont = null;
            _ = TmpFont;
        }

        public static TMP_FontAsset TmpFont
        {
            get
            {
                if (_tmpFont != null)
                    return _tmpFont;

                _tmpFont = CreateRuntimeFontAsset();
                if (_tmpFont == null)
                    _tmpFont = TMP_Settings.defaultFontAsset;
                if (_tmpFont == null)
                    _tmpFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

                return _tmpFont;
            }
        }

        private static TMP_FontAsset CreateRuntimeFontAsset()
        {
            Font sourceFont = ResolveRuntimeSourceFont();
            if (sourceFont == null)
                return null;

            TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                90,
                9,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);

            if (fontAsset == null)
                return null;

            fontAsset.name = "RM Runtime UI Font";

            // Fallback chain — ładujemy explicit z Resources/Fonts/ zamiast polegać na
            // LiberationSans SDF.fallbackFontAssetTable (BUG-006: Setup Font Fallbacks
            // tworzy puste sloty fileID:0, glyphy U+21BA/✓/⚠ etc. nie renderują).
            //
            // Kolejność ma znaczenie: TMP iteruje od pierwszego do ostatniego, każdy fallback
            // sprawdzony osobno przez glyph lookup. NotoSans (PL diacritics + extended Latin)
            // → NotoSansSymbols2 (rotation arrows + math) → NotoSansJP (CJK).
            //
            // Wszystkie 3 fonty mają AtlasPopulationMode.Dynamic — TMP dorzuca glyphy
            // on-demand z source TTF przy pierwszym użyciu.
            var fallbackChain = new List<TMP_FontAsset>();
            TryAddFallback(fallbackChain, "Fonts/NotoSans-Regular SDF");
            TryAddFallback(fallbackChain, "Fonts/NotoSansSymbols2-Regular SDF");
            TryAddFallback(fallbackChain, "Fonts/NotoSansJP-Regular SDF");

            // Plus dziedziczone z LiberationSans SDF (gdyby user kiedyś je dorzucił przez
            // Setup Font Fallbacks — defense in depth, deduplicate przy add).
            var sourceFallbackAsset = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            if (sourceFallbackAsset != null && sourceFallbackAsset.fallbackFontAssetTable != null)
            {
                foreach (var inherited in sourceFallbackAsset.fallbackFontAssetTable)
                {
                    if (inherited != null && !fallbackChain.Contains(inherited))
                        fallbackChain.Add(inherited);
                }
            }

            fontAsset.fallbackFontAssetTable = fallbackChain;

            TMP_Settings.defaultFontAsset = fontAsset;
            return fontAsset;
        }

        private static void TryAddFallback(List<TMP_FontAsset> chain, string resourcePath)
        {
            var asset = Resources.Load<TMP_FontAsset>(resourcePath);
            if (asset == null)
            {
                Log.Warn($"[UITheme] Fallback font not found: Resources/{resourcePath}");
                return;
            }
            if (!chain.Contains(asset))
                chain.Add(asset);
        }

        private static Font ResolveRuntimeSourceFont()
        {
            TMP_FontAsset fallbackAsset = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
            if (fallbackAsset != null && fallbackAsset.sourceFontFile != null)
                return fallbackAsset.sourceFontFile;

            TMP_FontAsset defaultAsset = TMP_Settings.defaultFontAsset;
            if (defaultAsset != null && defaultAsset.sourceFontFile != null)
                return defaultAsset.sourceFontFile;

            return LegacyFont;
        }
    }
}
