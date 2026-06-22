using NUnit.Framework;
using UnityEngine;
using RailwayManager.SharedUI;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// TD-029: helper StretchTop (top-stretch dla zawartości scrolla). Weryfikuje kotwice/pivot/
    /// sizeDelta — bug-pattern był: content nie wypełnia szerokości lub rośnie w złą stronę.
    /// AddHLG/AddVLG/MakeSlider (typy UnityEngine.UI) NIE testowane tu — test asmdef ma
    /// overrideReferences=true bez ugui; defaulty childControl=true są explicit, slider wyjęty
    /// ze sprawdzonego SettingsScreenUI.BuildSliderInternal.
    /// </summary>
    public class UIPrimitivesTests
    {
        GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void StretchTop_SetsTopStretchAnchorsAndPivot()
        {
            _go = new GameObject("t", typeof(RectTransform));
            var rt = UIPrimitives.StretchTop(_go);

            Assert.IsNotNull(rt);
            Assert.AreEqual(new Vector2(0f, 1f), rt.anchorMin, "anchorMin = lewy-góra");
            Assert.AreEqual(new Vector2(1f, 1f), rt.anchorMax, "anchorMax = prawy-góra (pełna szerokość)");
            Assert.AreEqual(new Vector2(0.5f, 1f), rt.pivot, "pivot y=1 → rośnie w dół");
            Assert.AreEqual(Vector2.zero, rt.sizeDelta);
            Assert.AreEqual(Vector2.zero, rt.anchoredPosition);
        }

        [Test]
        public void StretchTop_NullSafe()
        {
            Assert.IsNull(UIPrimitives.StretchTop((GameObject)null));
            Assert.IsNull(UIPrimitives.StretchTop((RectTransform)null));
        }
    }
}
