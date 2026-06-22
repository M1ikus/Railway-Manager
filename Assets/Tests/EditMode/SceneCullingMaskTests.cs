using NUnit.Framework;
using RailwayManager.Core;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// Etap B RouteMapPreview: separacja warstw kamer. Weryfikuje że maski kamer
    /// (duża mapa / podgląd / depot) zawierają dokładnie właściwe warstwy — żeby
    /// kafle były wspólne, nakładki dużej mapy NIE przeciekały do podglądu, a
    /// zawartość podglądu NIE pojawiała się ani na dużej mapie, ani w zajezdni.
    /// </summary>
    public class SceneCullingMaskTests
    {
        private static bool Has(int mask, int layer) => (mask & (1 << layer)) != 0;

        [Test]
        public void Layers_AreDistinct()
        {
            Assert.AreNotEqual(SceneController.MapLayer, SceneController.MapOverlayLayer);
            Assert.AreNotEqual(SceneController.MapLayer, SceneController.MapPreviewLayer);
            Assert.AreNotEqual(SceneController.MapOverlayLayer, SceneController.MapPreviewLayer);
            Assert.AreNotEqual(SceneController.MapLayer, SceneController.UiLayer);
            Assert.AreNotEqual(SceneController.MapOverlayLayer, SceneController.UiLayer);
            Assert.AreNotEqual(SceneController.MapPreviewLayer, SceneController.UiLayer);
        }

        [Test]
        public void MapCameraMask_HasTilesUiOverlay_NotPreview()
        {
            int m = SceneController.MapCameraCullingMask;
            Assert.IsTrue(Has(m, SceneController.MapLayer), "duża mapa musi widzieć kafle");
            Assert.IsTrue(Has(m, SceneController.UiLayer), "duża mapa musi widzieć UI");
            Assert.IsTrue(Has(m, SceneController.MapOverlayLayer), "duża mapa musi widzieć swoje nakładki");
            Assert.IsFalse(Has(m, SceneController.MapPreviewLayer), "zawartość podglądu NIE może być na dużej mapie");
        }

        [Test]
        public void PreviewCameraMask_OnlyPreviewLayer()
        {
            // Podgląd renderuje WYŁĄCZNIE własną warstwę (własne kafle OSM + linia/markery).
            int m = SceneController.PreviewCameraCullingMask;
            Assert.IsTrue(Has(m, SceneController.MapPreviewLayer), "podgląd musi widzieć swoją zawartość/kafle");
            Assert.IsFalse(Has(m, SceneController.MapLayer), "podgląd NIE renderuje głównych kafli (ma własne we własnym LOD)");
            Assert.IsFalse(Has(m, SceneController.MapOverlayLayer), "linia dużej mapy NIE może przeciekać do podglądu");
            Assert.IsFalse(Has(m, SceneController.UiLayer), "podgląd NIE renderuje UI (rekurencja RawImage)");
        }

        [Test]
        public void DepotExclusions_ClearAllMapLayers_KeepOthers()
        {
            int allOnes = ~0;
            int m = SceneController.ApplyDepotCullingExclusions(allOnes);
            Assert.IsFalse(Has(m, SceneController.MapLayer), "Depot nie renderuje kafli");
            Assert.IsFalse(Has(m, SceneController.MapOverlayLayer), "Depot nie renderuje nakładek dużej mapy");
            Assert.IsFalse(Has(m, SceneController.MapPreviewLayer), "Depot nie renderuje zawartości podglądu");
            // Warstwy nie-mapowe zostają nietknięte
            Assert.IsTrue(Has(m, 0), "Default zostaje");
            Assert.IsTrue(Has(m, SceneController.UiLayer), "UI zostaje");
        }

        [Test]
        public void DepotExclusions_Idempotent()
        {
            int once = SceneController.ApplyDepotCullingExclusions(~0);
            int twice = SceneController.ApplyDepotCullingExclusions(once);
            Assert.AreEqual(once, twice);
        }
    }
}
