using NUnit.Framework;
using MapSystem;

namespace RailwayManager.Tests.EditMode
{
    /// <summary>
    /// Etap C RouteMapPreview: polityka kafli z pin-setem. Weryfikuje że kafel PINOWANY przez
    /// mini-podgląd jest zawsze renderowany (poza frustumem głównej kamery) i nigdy nie pada
    /// ofiarą LRU eviction — oraz że dla pinned=false zachowanie jest identyczne z oryginałem.
    /// </summary>
    public class TilePinPolicyTests
    {
        // ---------- ResolveAction (widoczność/utrzymanie) ----------

        [Test]
        public void Resolve_Visible_NotPinned_Render()
        {
            Assert.AreEqual(TilePinPolicy.TileAction.Render,
                TilePinPolicy.ResolveAction(inVisibleZone: true, pinned: false, inCacheZone: false));
        }

        [Test]
        public void Resolve_CacheOnly_NotPinned_CacheHide()
        {
            Assert.AreEqual(TilePinPolicy.TileAction.CacheHide,
                TilePinPolicy.ResolveAction(inVisibleZone: false, pinned: false, inCacheZone: true));
        }

        [Test]
        public void Resolve_Outside_NotPinned_Unload()
        {
            Assert.AreEqual(TilePinPolicy.TileAction.Unload,
                TilePinPolicy.ResolveAction(inVisibleZone: false, pinned: false, inCacheZone: false));
        }

        [Test]
        public void Resolve_PinnedOutsideEverything_Render()
        {
            // Sedno C: pinowany kafel poza frustumem głównej kamery MUSI być renderowany
            // (patrzy na niego kamera mini-podglądu).
            Assert.AreEqual(TilePinPolicy.TileAction.Render,
                TilePinPolicy.ResolveAction(inVisibleZone: false, pinned: true, inCacheZone: false));
        }

        [Test]
        public void Resolve_PinnedTrumpsCache()
        {
            // pinned ma priorytet nad cache (Render, nie CacheHide)
            Assert.AreEqual(TilePinPolicy.TileAction.Render,
                TilePinPolicy.ResolveAction(inVisibleZone: false, pinned: true, inCacheZone: true));
        }

        // ---------- CanEvict (ochrona przed LRU) ----------

        [Test]
        public void Evict_LoadedOutside_NotPinned_CanEvict()
        {
            Assert.IsTrue(TilePinPolicy.CanEvict(inVisibleZone: false, pinned: false, isLoaded: true));
        }

        [Test]
        public void Evict_Pinned_Protected()
        {
            // Sedno C: pinowany kafel nigdy nie jest evictowany, nawet poza visible zone.
            Assert.IsFalse(TilePinPolicy.CanEvict(inVisibleZone: false, pinned: true, isLoaded: true));
        }

        [Test]
        public void Evict_Visible_Protected()
        {
            Assert.IsFalse(TilePinPolicy.CanEvict(inVisibleZone: true, pinned: false, isLoaded: true));
        }

        [Test]
        public void Evict_Pending_NotEvicted()
        {
            // IsLoaded=false (w trakcie async load) — nie ruszać
            Assert.IsFalse(TilePinPolicy.CanEvict(inVisibleZone: false, pinned: false, isLoaded: false));
        }
    }
}
