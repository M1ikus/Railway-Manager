namespace MapSystem
{
    /// <summary>
    /// Czyste mapowanie orthographicSize → poziom LOD kafli (0 = pełny detal, 5 = najgrubszy).
    /// Wydzielone z <c>MapRenderer.UpdateLayerVisibility</c>, żeby ten sam wzór mógł użyć
    /// zarówno główna mapa (globalny LOD), jak i mini-mapa OSM (RouteMapPreview — własny LOD
    /// liczony z WŁASNEGO zoomu, niezależnie od głównej kamery). W Map asmdef, bo MapRenderer
    /// nie może referować Timetable (kierunek asmdef).
    /// </summary>
    public static class MapLod
    {
        // Domyślne progi (metry orthoSize) — muszą odpowiadać polom MapRenderer.zoomLOD1..5.
        public const float DefaultLOD1 = 1000f;
        public const float DefaultLOD2 = 2000f;
        public const float DefaultLOD3 = 4000f;
        public const float DefaultLOD4 = 8000f;
        public const float DefaultLOD5 = 16000f;

        /// <summary>orthoSize → LOD (0-5) wg podanych progów. Wzór 1:1 z MapRenderer.UpdateLayerVisibility.</summary>
        public static int LodForOrtho(float ortho, float l1, float l2, float l3, float l4, float l5)
        {
            if (ortho > l5) return 5;
            if (ortho > l4) return 4;
            if (ortho > l3) return 3;
            if (ortho > l2) return 2;
            if (ortho > l1) return 1;
            return 0;
        }

        /// <summary>orthoSize → LOD (0-5) wg domyślnych progów (dla konsumentów bez własnych progów).</summary>
        public static int LodForOrtho(float ortho)
            => LodForOrtho(ortho, DefaultLOD1, DefaultLOD2, DefaultLOD3, DefaultLOD4, DefaultLOD5);
    }
}
