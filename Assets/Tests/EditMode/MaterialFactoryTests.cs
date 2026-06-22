using System.Collections.Generic;
using NUnit.Framework;
using RailwayManager.Core.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// Kontrakty MaterialFactory (M-URP / URP-1). Testy są pipeline-aware: asercje wrażliwe
    /// na pipeline rozgałęziają się na MaterialFactory.IsSrpActive, więc przeżyją flip Built-in→URP.
    /// W obecnym stanie repo (Built-in) IsSrpActive == false.
    /// </summary>
    public class MaterialFactoryTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private Material Track(Material m)
        {
            if (m != null) _spawned.Add(m);
            return m;
        }

        [SetUp]
        public void SetUp() => MaterialFactory.ResetCache();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned)
                if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        [Test]
        public void IsSrpActive_MatchesGraphicsSettings()
        {
            Assert.AreEqual(GraphicsSettings.currentRenderPipeline != null, MaterialFactory.IsSrpActive);
        }

        [Test]
        public void CreateLit_ReturnsValidMaterial()
        {
            var m = Track(MaterialFactory.CreateLit());
            Assert.IsNotNull(m, "CreateLit zwrócił null");
            Assert.IsNotNull(m.shader, "materiał bez shadera");
            Assert.AreNotEqual("Hidden/InternalErrorShader", m.shader.name, "spadł na error shader — brak Standard i URP/Lit");
            if (MaterialFactory.IsSrpActive)
                StringAssert.Contains("Universal Render Pipeline", m.shader.name);
            else
                Assert.AreEqual("Standard", m.shader.name);
        }

        [Test]
        public void CreateUnlit_ReturnsValidMaterial()
        {
            var m = Track(MaterialFactory.CreateUnlit());
            Assert.IsNotNull(m);
            Assert.IsNotNull(m.shader);
            Assert.AreNotEqual("Hidden/InternalErrorShader", m.shader.name);
        }

        [Test]
        public void CreateLine_UsesSpritesDefault()
        {
            var m = Track(MaterialFactory.CreateLine());
            Assert.IsNotNull(m);
            Assert.AreEqual("Sprites/Default", m.shader.name);
        }

        [Test]
        public void CreateLit_CachesShaderAcrossCalls()
        {
            var a = Track(MaterialFactory.CreateLit());
            var b = Track(MaterialFactory.CreateLit());
            Assert.AreSame(a.shader, b.shader, "shader powinien być cache'owany (jedno Shader.Find)");
        }

        [Test]
        public void SetBaseColor_AppliesColor()
        {
            var m = Track(MaterialFactory.CreateLit());
            var red = new Color(1f, 0f, 0f, 1f);
            MaterialFactory.SetBaseColor(m, red);

            // Standard ma _Color, URP/Lit ma _BaseColor — sprawdzamy ten, który istnieje.
            Color actual = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.GetColor("_Color");
            Assert.AreEqual(1f, actual.r, 0.001f);
            Assert.AreEqual(0f, actual.g, 0.001f);
            Assert.AreEqual(0f, actual.b, 0.001f);
        }

        [Test]
        public void SetMetallicSmoothness_AppliesValues()
        {
            var m = Track(MaterialFactory.CreateLit());
            MaterialFactory.SetMetallicSmoothness(m, 0.7f, 0.3f);

            Assert.IsTrue(m.HasProperty("_Metallic"));
            Assert.AreEqual(0.7f, m.GetFloat("_Metallic"), 0.001f);

            // smoothness: URP/Lit → _Smoothness, Built-in → _Glossiness
            if (m.HasProperty("_Smoothness"))
                Assert.AreEqual(0.3f, m.GetFloat("_Smoothness"), 0.001f);
            if (m.HasProperty("_Glossiness"))
                Assert.AreEqual(0.3f, m.GetFloat("_Glossiness"), 0.001f);
        }

        [Test]
        public void SetEmission_EnablesKeywordAndColor()
        {
            var m = Track(MaterialFactory.CreateLit());
            var c = new Color(0.2f, 0.5f, 0.1f, 1f);
            MaterialFactory.SetEmission(m, c);

            Assert.IsTrue(m.IsKeywordEnabled("_EMISSION"), "keyword _EMISSION powinien być włączony");
            if (m.HasProperty("_EmissionColor"))
            {
                var e = m.GetColor("_EmissionColor");
                Assert.AreEqual(0.2f, e.r, 0.001f);
                Assert.AreEqual(0.5f, e.g, 0.001f);
            }
        }

        [Test]
        public void SetTransparent_UsesPipelineAppropriateSetup()
        {
            var m = Track(MaterialFactory.CreateLit());
            MaterialFactory.SetTransparent(m);

            if (MaterialFactory.IsSrpActive)
            {
                if (m.HasProperty("_Surface"))
                    Assert.AreEqual(1f, m.GetFloat("_Surface"), 0.001f, "URP transparent: _Surface=1");
                Assert.AreEqual((int)RenderQueue.Transparent, m.renderQueue, "URP transparent render queue");
            }
            else
            {
                Assert.IsTrue(m.HasProperty("_Mode"), "Built-in Standard ma _Mode");
                Assert.AreEqual(3f, m.GetFloat("_Mode"), 0.001f, "Built-in transparent: _Mode=3");
                Assert.IsTrue(m.IsKeywordEnabled("_ALPHABLEND_ON"), "Built-in transparent keyword");
                Assert.AreEqual(3000, m.renderQueue, "Built-in transparent render queue");
                Assert.AreEqual((int)BlendMode.SrcAlpha, m.GetInt("_SrcBlend"), "Built-in _SrcBlend");
                Assert.AreEqual((int)BlendMode.OneMinusSrcAlpha, m.GetInt("_DstBlend"), "Built-in _DstBlend");
                Assert.AreEqual(0, m.GetInt("_ZWrite"), "Built-in _ZWrite=0");
            }
        }

        [Test]
        public void SetDoubleSided_DisablesCull()
        {
            var m = Track(MaterialFactory.CreateLit());
            MaterialFactory.SetDoubleSided(m);
            if (m.HasProperty("_Cull"))
                Assert.AreEqual(0f, m.GetFloat("_Cull"), 0.001f);
        }

        [Test]
        public void Setters_NullMaterial_DoNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                MaterialFactory.SetBaseColor(null, Color.white);
                MaterialFactory.SetMetallicSmoothness(null, 0.5f, 0.5f);
                MaterialFactory.SetEmission(null, Color.white);
                MaterialFactory.SetTransparent(null);
                MaterialFactory.SetDoubleSided(null);
                MaterialFactory.SetPbrMaps(null, null, null, null);
            });
        }
    }
}
