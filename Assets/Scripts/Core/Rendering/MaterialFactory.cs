using UnityEngine;
using UnityEngine.Rendering;

namespace RailwayManager.Core.Rendering
{
    /// <summary>
    /// Centralna fabryka materiałów proceduralnych — JEDYNE miejsce w projekcie, które "wie"
    /// na jakim render pipeline gramy (Built-in vs URP). Wszystkie call-site'y typu
    /// <c>new Material(Shader.Find(...))</c> powinny iść przez tę klasę, dzięki czemu
    /// migracja Built-in → URP (milestone M-URP) sprowadza się do zmian TUTAJ, a nie
    /// w ~75 rozproszonych plikach.
    ///
    /// Stan 2026-06-17: projekt na Built-in RP (<see cref="IsSrpActive"/> == false). Po
    /// instalacji pakietu URP (M-URP / URP-3) <c>GraphicsSettings.currentRenderPipeline</c>
    /// przestaje być null i fabryka automatycznie zaczyna zwracać URP/Lit + property URP.
    ///
    /// Spec: memory/urp_migration_design.md
    /// </summary>
    public static class MaterialFactory
    {
        // --- Nazwy shaderów ---
        private const string UrpLit            = "Universal Render Pipeline/Lit";
        private const string UrpUnlit          = "Universal Render Pipeline/Unlit";
        private const string BuiltinStandard   = "Standard";
        private const string BuiltinUnlitColor = "Unlit/Color";
        private const string SpritesDefault    = "Sprites/Default";
        private const string ErrorShaderName   = "Hidden/InternalErrorShader";

        // --- Cache shaderów (Shader.Find jest drogi — patrz nota perf w MapRenderer.cs) ---
        private static Shader _litShader;
        private static Shader _unlitShader;
        private static Shader _lineShader;

        /// <summary>true gdy aktywny Scriptable Render Pipeline (URP/HDRP). Built-in ⇒ false.</summary>
        public static bool IsSrpActive => GraphicsSettings.currentRenderPipeline != null;

        /// <summary>Czyści cache shaderów. Wołać po zmianie pipeline w runtime (głównie testy).</summary>
        public static void ResetCache()
        {
            _litShader = null;
            _unlitShader = null;
            _lineShader = null;
        }

        // ====================================================================
        //  Tworzenie materiałów
        // ====================================================================

        /// <summary>
        /// Materiał PBR: URP/Lit (gdy SRP) ?? Standard (Built-in). Pojazdy, tor, podsypka,
        /// ściany, katenaria, ground, snap pointy.
        /// </summary>
        public static Material CreateLit()
        {
            if (_litShader == null)
            {
                _litShader = IsSrpActive
                    ? (Shader.Find(UrpLit) ?? Shader.Find(BuiltinStandard) ?? ErrorShader())
                    : (Shader.Find(BuiltinStandard) ?? Shader.Find(UrpLit) ?? ErrorShader());
            }
            return new Material(_litShader);
        }

        /// <summary>
        /// Materiał bez oświetlenia: URP/Unlit ?? Unlit/Color ?? Sprites/Default. Markery mapy,
        /// kapsuły personelu, overlaye (granica/woj./woda/DLC lock).
        /// </summary>
        public static Material CreateUnlit()
        {
            if (_unlitShader == null)
            {
                _unlitShader = IsSrpActive
                    ? (Shader.Find(UrpUnlit) ?? Shader.Find(BuiltinUnlitColor) ?? Shader.Find(SpritesDefault) ?? ErrorShader())
                    : (Shader.Find(BuiltinUnlitColor) ?? Shader.Find(SpritesDefault) ?? Shader.Find(UrpUnlit) ?? ErrorShader());
            }
            return new Material(_unlitShader);
        }

        /// <summary>
        /// Materiał dla LineRenderer / preview / fill quady. Sprites/Default działa w obu
        /// pipeline'ach, więc jeden wspólny wariant.
        /// </summary>
        public static Material CreateLine()
        {
            if (_lineShader == null)
                _lineShader = Shader.Find(SpritesDefault) ?? Shader.Find(BuiltinUnlitColor) ?? ErrorShader();
            return new Material(_lineShader);
        }

        private static Shader ErrorShader() => Shader.Find(ErrorShaderName);

        // ====================================================================
        //  Ustawianie property (pipeline-aware)
        // ====================================================================

        /// <summary>Kolor bazowy: _BaseColor (URP) i/lub _Color (Built-in/Sprites). Ustawia te, które istnieją.</summary>
        public static void SetBaseColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            if (m.HasProperty(ColorId)) m.SetColor(ColorId, c);
        }

        /// <summary>
        /// Metallic + smoothness (0-1). Smoothness mapuje się na _Smoothness (URP/Lit) lub
        /// _Glossiness (Built-in Standard) — semantyka identyczna, więc 1:1.
        /// </summary>
        public static void SetMetallicSmoothness(Material m, float metallic, float smoothness)
        {
            if (m == null) return;
            if (m.HasProperty(MetallicId))   m.SetFloat(MetallicId, metallic);
            if (m.HasProperty(SmoothnessId)) m.SetFloat(SmoothnessId, smoothness);  // URP/Lit
            if (m.HasProperty(GlossinessId)) m.SetFloat(GlossinessId, smoothness);  // Built-in Standard
        }

        /// <summary>Emisja: włącza keyword _EMISSION + ustawia _EmissionColor (te same w Built-in i URP/Lit).</summary>
        public static void SetEmission(Material m, Color c)
        {
            if (m == null) return;
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty(EmissionColorId)) m.SetColor(EmissionColorId, c);
        }

        /// <summary>Wyłącza backface culling (_Cull = 0). Overlaye/woda/ground rysowane dwustronnie.</summary>
        public static void SetDoubleSided(Material m)
        {
            if (m != null && m.HasProperty(CullId)) m.SetFloat(CullId, 0f); // 0 = Off
        }

        /// <summary>
        /// Przełącza materiał w tryb przezroczysty. GOTCHA migracji: Built-in Standard używa
        /// _Mode=3 + keyword _ALPHABLEND_ON, a URP/Lit _Surface=1 + _SURFACE_TYPE_TRANSPARENT —
        /// inny mechanizm. Built-in branch odwzorowuje istniejące call-site'y (WallBuildingSystem.Mesh,
        /// DepotFenceSystem) — zero zmiany wizualnej na Built-in.
        /// </summary>
        public static void SetTransparent(Material m)
        {
            if (m == null) return;
            if (IsSrpActive)
            {
                // URP/Lit transparent (kanoniczny setup)
                if (m.HasProperty(SurfaceId))  m.SetFloat(SurfaceId, 1f); // 0 opaque / 1 transparent
                if (m.HasProperty(BlendId))    m.SetFloat(BlendId, 0f);   // 0 = alpha blend
                if (m.HasProperty(SrcBlendId)) m.SetFloat(SrcBlendId, (float)BlendMode.SrcAlpha);
                if (m.HasProperty(DstBlendId)) m.SetFloat(DstBlendId, (float)BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty(ZWriteId))   m.SetFloat(ZWriteId, 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                // Built-in Standard transparent — PEŁNY mirror oryginalnych call-site'ów
                // (WallBuildingSystem.CreateWindowGlass, DepotFenceSystem mesh) → parytet 1:1.
                if (m.HasProperty(ModeId))     m.SetFloat(ModeId, 3f); // 3 = Transparent
                if (m.HasProperty(SrcBlendId)) m.SetInt(SrcBlendId, (int)BlendMode.SrcAlpha);
                if (m.HasProperty(DstBlendId)) m.SetInt(DstBlendId, (int)BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty(ZWriteId))   m.SetInt(ZWriteId, 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 3000;
            }
        }

        /// <summary>
        /// PBR mapy (normal / metallic-gloss / AO) — używane przez ExitTrackController (szyny).
        /// Nazwy property w większości przenoszą się Built-in↔URP/Lit; różni się keyword
        /// metallic-gloss mapy i źródło smoothness (gloss scale Built-in vs _Smoothness URP).
        /// </summary>
        public static void SetPbrMaps(Material m, Texture normal, Texture metallicGloss, Texture occlusion, float smoothness = 1f)
        {
            if (m == null) return;

            if (normal != null && m.HasProperty(BumpMapId))
            {
                m.SetTexture(BumpMapId, normal);
                m.EnableKeyword("_NORMALMAP");
            }

            if (metallicGloss != null && m.HasProperty(MetallicGlossMapId))
            {
                m.SetTexture(MetallicGlossMapId, metallicGloss);
                m.EnableKeyword(IsSrpActive ? "_METALLICSPECGLOSSMAP" : "_METALLICGLOSSMAP");
                if (m.HasProperty(MetallicId)) m.SetFloat(MetallicId, 1f);

                if (IsSrpActive)
                {
                    if (m.HasProperty(SmoothnessId)) m.SetFloat(SmoothnessId, smoothness);
                }
                else
                {
                    if (m.HasProperty(GlossMapScaleId)) m.SetFloat(GlossMapScaleId, smoothness);
                }
            }

            if (occlusion != null && m.HasProperty(OcclusionMapId))
                m.SetTexture(OcclusionMapId, occlusion);
        }

        // ====================================================================
        //  Cached property IDs (Shader.PropertyToID — szybsze niż string lookup)
        // ====================================================================
        private static readonly int BaseColorId        = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId            = Shader.PropertyToID("_Color");
        private static readonly int MetallicId         = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId       = Shader.PropertyToID("_Smoothness");
        private static readonly int GlossinessId       = Shader.PropertyToID("_Glossiness");
        private static readonly int EmissionColorId    = Shader.PropertyToID("_EmissionColor");
        private static readonly int CullId             = Shader.PropertyToID("_Cull");
        private static readonly int ModeId             = Shader.PropertyToID("_Mode");
        private static readonly int SurfaceId          = Shader.PropertyToID("_Surface");
        private static readonly int BlendId            = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendId         = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId         = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId           = Shader.PropertyToID("_ZWrite");
        private static readonly int BumpMapId          = Shader.PropertyToID("_BumpMap");
        private static readonly int MetallicGlossMapId = Shader.PropertyToID("_MetallicGlossMap");
        private static readonly int GlossMapScaleId    = Shader.PropertyToID("_GlossMapScale");
        private static readonly int OcclusionMapId     = Shader.PropertyToID("_OcclusionMap");
    }
}
