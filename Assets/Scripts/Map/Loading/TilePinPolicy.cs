namespace MapSystem
{
    /// <summary>
    /// Czysta polityka decyzji per-kafel w <see cref="TileManager"/> — wydzielona z
    /// <c>UpdateTileLoading</c>/<c>EvictOldestIfOverCap</c>, żeby reguły (zwłaszcza ochrona
    /// kafli PINOWANYCH przez RouteMapPreview) były testowalne w EditMode bez sceny/kamery.
    ///
    /// Semantic preservation: dla pinned=false wynik jest identyczny z oryginalną logiką
    /// (visible→render, cache→hide, reszta→unload; eviction pomija tylko visible+pending).
    /// </summary>
    public static class TilePinPolicy
    {
        public enum TileAction
        {
            /// <summary>Renderuj (SetActive true) + odśwież LastAccessTime.</summary>
            Render,
            /// <summary>Trzymaj w pamięci, ukryj (SetActive false) + odśwież LastAccessTime.</summary>
            CacheHide,
            /// <summary>Poza strefą zainteresowania — kandydat do odładowania.</summary>
            Unload
        }

        /// <summary>
        /// Decyzja widoczności/utrzymania kafla. Kafel PINOWANY jest zawsze renderowany
        /// (niezależnie od frustum głównej kamery) — bo patrzy na niego kamera mini-podglądu.
        /// </summary>
        public static TileAction ResolveAction(bool inVisibleZone, bool pinned, bool inCacheZone)
        {
            if (inVisibleZone || pinned) return TileAction.Render;
            if (inCacheZone) return TileAction.CacheHide;
            return TileAction.Unload;
        }

        /// <summary>
        /// Czy kafel może zostać usunięty przez LRU eviction. Chronione: w strefie widocznej,
        /// PINOWANE, oraz pending (jeszcze nie załadowane — IsLoaded=false).
        /// </summary>
        public static bool CanEvict(bool inVisibleZone, bool pinned, bool isLoaded)
        {
            return isLoaded && !inVisibleZone && !pinned;
        }
    }
}
