using NUnit.Framework;
using MapSystem;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// LOD-A: czyste mapowanie orthoSize → LOD (MapLod). Wzór musi być 1:1 z dawną logiką
    /// MapRenderer.UpdateLayerVisibility (progi domyślne 1000/2000/4000/8000/16000).
    /// </summary>
    public class MapLodTests
    {
        [Test]
        public void DefaultThresholds_BoundaryMapping()
        {
            // ortho > prog → wyzszy LOD; rownosc NIE przekracza (uzyte '>').
            Assert.AreEqual(0, MapLod.LodForOrtho(500f));
            Assert.AreEqual(0, MapLod.LodForOrtho(1000f));   // == prog → jeszcze LOD0
            Assert.AreEqual(1, MapLod.LodForOrtho(1000.1f));
            Assert.AreEqual(1, MapLod.LodForOrtho(2000f));
            Assert.AreEqual(2, MapLod.LodForOrtho(2000.1f));
            Assert.AreEqual(2, MapLod.LodForOrtho(4000f));
            Assert.AreEqual(3, MapLod.LodForOrtho(4000.1f));
            Assert.AreEqual(3, MapLod.LodForOrtho(8000f));
            Assert.AreEqual(4, MapLod.LodForOrtho(8000.1f));
            Assert.AreEqual(4, MapLod.LodForOrtho(16000f));
            Assert.AreEqual(5, MapLod.LodForOrtho(16000.1f));
            Assert.AreEqual(5, MapLod.LodForOrtho(60000f));
        }

        [Test]
        public void CustomThresholds_Respected()
        {
            Assert.AreEqual(0, MapLod.LodForOrtho(50f, 100f, 200f, 400f, 800f, 1600f));
            Assert.AreEqual(3, MapLod.LodForOrtho(500f, 100f, 200f, 400f, 800f, 1600f));
            Assert.AreEqual(5, MapLod.LodForOrtho(5000f, 100f, 200f, 400f, 800f, 1600f));
        }

        [Test]
        public void Overload_MatchesExplicitDefaults()
        {
            for (float o = 0f; o <= 40000f; o += 137f)
                Assert.AreEqual(
                    MapLod.LodForOrtho(o, MapLod.DefaultLOD1, MapLod.DefaultLOD2, MapLod.DefaultLOD3, MapLod.DefaultLOD4, MapLod.DefaultLOD5),
                    MapLod.LodForOrtho(o));
        }
    }
}
