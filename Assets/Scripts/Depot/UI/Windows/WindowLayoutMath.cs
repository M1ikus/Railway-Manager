using UnityEngine;

namespace DepotSystem
{
    /// <summary>
    /// M-Windows P1: czysta matematyka układu pływających okien (kaskadowe pozycjonowanie +
    /// clamp paska tytułu do ekranu). Bez stanu Unity → testowalna w EditMode (wzór
    /// <see cref="DepotSystem.Furniture.FurnitureOccupancyMath"/> / <c>TrackOccupancyMath</c>).
    ///
    /// <para>Konwencja jednostek: wszystko w jednostkach canvasu; anchor i pivot okna = środek
    /// (0.5, 0.5), więc <c>anchoredPosition (0,0)</c> = środek warstwy. X rośnie w prawo,
    /// Y rośnie w górę (jak w Unity UI).</para>
    /// </summary>
    public static class WindowLayoutMath
    {
        /// <summary>
        /// Kaskadowe przesunięcie N-tego okna względem środka warstwy, zawijane co <paramref name="wrap"/>
        /// okien — żeby kolejne okna nie zakrywały się idealnie. Kolejne okna schodzą w prawo-dół.
        /// Bezpieczne dla <paramref name="index"/> &lt; 0.
        /// </summary>
        public static Vector2 CascadeOffset(int index, float step, int wrap)
        {
            if (wrap < 1) wrap = 1;
            int slot = ((index % wrap) + wrap) % wrap; // modulo bezpieczne dla ujemnych
            float d = slot * step;
            return new Vector2(d, -d); // -Y = w dół
        }

        /// <summary>
        /// Ogranicza pozycję okna tak, by pasek tytułu (górna krawędź okna) pozostał chwytalny:
        /// nigdy nad górną krawędzią warstwy oraz co najmniej <paramref name="keepMargin"/> px od dołu,
        /// a poziomo min <paramref name="keepMargin"/> px szerokości okna widoczne z każdej strony.
        /// </summary>
        /// <param name="desired">żądana anchoredPosition (środek-pivot) okna</param>
        /// <param name="windowSize">rozmiar okna (px)</param>
        /// <param name="parentSize">rozmiar warstwy/canvasu (px)</param>
        /// <param name="keepMargin">ile px okna trzymać widocznych (bok) / min odległość paska od dołu</param>
        public static Vector2 ClampTitleBarOnScreen(
            Vector2 desired, Vector2 windowSize, Vector2 parentSize, float keepMargin)
        {
            Vector2 parentHalf = parentSize * 0.5f;
            Vector2 winHalf = windowSize * 0.5f;

            // Poziomo: zostaw min keepMargin szerokości okna na ekranie z każdej strony.
            float maxX = parentHalf.x + winHalf.x - keepMargin;
            if (maxX < 0f) maxX = 0f;
            float x = Mathf.Clamp(desired.x, -maxX, maxX);

            // Pionowo: trzymaj GÓRĘ okna (pasek tytułu) w obrębie warstwy.
            float top = desired.y + winHalf.y;
            float topMax = parentHalf.y;                // nie wyżej niż górna krawędź
            float topMin = -parentHalf.y + keepMargin;  // co najmniej keepMargin od dołu
            if (topMin > topMax) topMin = topMax;
            top = Mathf.Clamp(top, topMin, topMax);
            float y = top - winHalf.y;

            return new Vector2(x, y);
        }

        /// <summary>
        /// Oblicza nowy rozmiar i pozycję okna przy resize za uchwyt krawędzi/rogu. Pivot okna =
        /// środek (0.5,0.5), więc zmiana rozmiaru wymaga przesunięcia środka, by przeciwległa
        /// krawędź została na miejscu. <paramref name="hx"/>/<paramref name="hy"/> = która krawędź
        /// jest ciągnięta: +1 prawo/góra, -1 lewo/dół, 0 = oś nieruszana (Y w górę jak w Unity UI).
        /// </summary>
        /// <param name="drag">przesunięcie kursora od początku drag (parent-local)</param>
        public static void Resize(
            Vector2 pos, Vector2 size, Vector2 drag, int hx, int hy,
            Vector2 minSize, Vector2 maxSize, out Vector2 newPos, out Vector2 newSize)
        {
            float newW = (hx == 0) ? size.x : Mathf.Clamp(size.x + hx * drag.x, minSize.x, maxSize.x);
            float newH = (hy == 0) ? size.y : Mathf.Clamp(size.y + hy * drag.y, minSize.y, maxSize.y);
            // przeciwległa krawędź stała → środek przesuwa się o połowę faktycznej zmiany, w stronę uchwytu
            float cx = pos.x + hx * (newW - size.x) * 0.5f;
            float cy = pos.y + hy * (newH - size.y) * 0.5f;
            newPos = new Vector2(cx, cy);
            newSize = new Vector2(newW, newH);
        }
    }
}
