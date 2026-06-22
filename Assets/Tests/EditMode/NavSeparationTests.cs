using NUnit.Framework;
using UnityEngine;
using DepotSystem.Nav;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>TD-033 Etap H: czysta matematyka miękkiej separacji NPC (deterministyczna).</summary>
    public class NavSeparationTests
    {
        [Test]
        public void FarApart_NoPush()
        {
            var pos = new[] { new Vector2(0f, 0f), new Vector2(2f, 0f) };
            var outp = new Vector2[2];
            NavSeparation.ComputeDisplacements(pos, 2, 0.8f, outp);
            Assert.AreEqual(0f, outp[0].magnitude, 1e-4f);
            Assert.AreEqual(0f, outp[1].magnitude, 1e-4f);
        }

        [Test]
        public void Overlapping_PushApart()
        {
            var pos = new[] { new Vector2(0f, 0f), new Vector2(0.4f, 0f) };
            var outp = new Vector2[2];
            NavSeparation.ComputeDisplacements(pos, 2, 0.8f, outp);
            Assert.Less(outp[0].x, 0f, "Kapsuła 0 odpychana w -x.");
            Assert.Greater(outp[1].x, 0f, "Kapsuła 1 odpychana w +x.");
            Assert.AreEqual(0.2f, Mathf.Abs(outp[0].x), 1e-3f, "Połowa nakładania = (0.8-0.4)*0.5.");
        }

        [Test]
        public void Coincident_DeterministicSplit()
        {
            var pos = new[] { new Vector2(5f, 5f), new Vector2(5f, 5f) };
            var outp = new Vector2[2];
            NavSeparation.ComputeDisplacements(pos, 2, 0.8f, outp);
            Assert.Greater(outp[0].x, 0f, "Pokrywające się: index 0 → +x.");
            Assert.Less(outp[1].x, 0f, "index 1 → -x.");
        }

        [Test]
        public void Deterministic_SameInputSameOutput()
        {
            var pos = new[] { new Vector2(0f, 0f), new Vector2(0.3f, 0.1f), new Vector2(0.1f, 0.2f) };
            var a = new Vector2[3]; var b = new Vector2[3];
            NavSeparation.ComputeDisplacements(pos, 3, 0.8f, a);
            NavSeparation.ComputeDisplacements(pos, 3, 0.8f, b);
            for (int i = 0; i < 3; i++) Assert.AreEqual(a[i], b[i], $"disp[{i}] deterministyczne.");
        }
    }
}
