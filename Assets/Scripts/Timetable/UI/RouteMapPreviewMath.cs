using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;
using formap;

namespace RailwayManager.Timetable.UI
{
    /// <summary>
    /// Czysta matematyka osadzonej mini-mapy OSM (<see cref="RouteMapPreview"/>):
    /// granice trasy, pokrycie kafli, dopasowanie kamery ortho do bbox, pan/zoom,
    /// adaptacyjna szerokość linii zależna od zoomu PODGLĄDU.
    ///
    /// Bez zależności od sceny/GPU/MonoBehaviour — w pełni testowalne w EditMode.
    /// Konwencja współrzędnych: świat 2D jako <see cref="Vector2"/> gdzie x = world X,
    /// y = world Z (northing) — spójne z polyline z RailwayGraph i RoutePreviewOverlay.
    /// </summary>
    public static class RouteMapPreviewMath
    {
        // --- Domyślne stałe widoku (UI rendering, NIE balans gry) ---
        /// <summary>Zapas wokół trasy przy fit-to-route (frakcja orthoSize).</summary>
        public const float DefaultMarginFrac = 0.12f;
        /// <summary>Najmocniejszy zoom in (połowa wysokości widoku ~0.5 km).</summary>
        public const float MinOrthoSizeM = 250f;
        /// <summary>Najmocniejszy zoom out (cała Polska mieści się z zapasem).</summary>
        public const float MaxOrthoSizeM = 600000f;
        /// <summary>Czułość scrolla (mirror CameraController.HandleZoom).</summary>
        public const float ZoomFactor = 1.5f;
        /// <summary>Skala szerokości linii (mirror RoutePreviewOverlay).</summary>
        public const float WidthScale = 0.01f;
        public const float MinWidthM = 2.5f;
        public const float MaxWidthM = 250f;

        /// <summary>
        /// Bounding-box punktów polyline. paddingM dorzuca jednolity margines w metrach.
        /// Zwraca false dla pustej/nullowej listy (bounds = default).
        /// </summary>
        public static bool TryGetBounds(IReadOnlyList<Vector2> points, out BBox bounds, float paddingM = 0f)
        {
            bounds = default;
            if (points == null || points.Count == 0) return false;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 p = points[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            bounds = MakeBBox(minX, minY, maxX, maxY, paddingM);
            return true;
        }

        /// <summary>
        /// Bounding-box wielu polilinii (np. obieg = sekwencja tras / wszystkie rozkłady).
        /// Pomija null/puste polilinie. Zwraca false gdy brak jakiegokolwiek punktu.
        /// </summary>
        public static bool TryGetBounds(IEnumerable<IReadOnlyList<Vector2>> polylines, out BBox bounds, float paddingM = 0f)
        {
            bounds = default;
            bool any = false;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            if (polylines != null)
            {
                foreach (var poly in polylines)
                {
                    if (poly == null) continue;
                    for (int i = 0; i < poly.Count; i++)
                    {
                        any = true;
                        Vector2 p = poly[i];
                        if (p.x < minX) minX = p.x;
                        if (p.x > maxX) maxX = p.x;
                        if (p.y < minY) minY = p.y;
                        if (p.y > maxY) maxY = p.y;
                    }
                }
            }
            if (!any) return false;
            bounds = MakeBBox(minX, minY, maxX, maxY, paddingM);
            return true;
        }

        private static BBox MakeBBox(float minX, float minY, float maxX, float maxY, float paddingM)
        {
            return new BBox
            {
                MinX = minX - paddingM,
                MinY = minY - paddingM,
                MaxX = maxX + paddingM,
                MaxY = maxY + paddingM
            };
        }

        /// <summary>
        /// Lista tileID (Cantor) pokrywających bbox przy stałym <see cref="TileGrid.TILE_SIZE"/>.
        /// Pusta gdy bbox zdegenerowany (max &lt; min).
        /// </summary>
        public static List<long> TilesCovering(BBox bounds)
        {
            var result = new List<long>();
            if (bounds.MaxX < bounds.MinX || bounds.MaxY < bounds.MinY) return result;

            var (gx0, gy0) = TileGrid.WorldToGrid(bounds.MinX, bounds.MinY);
            var (gx1, gy1) = TileGrid.WorldToGrid(bounds.MaxX, bounds.MaxY);
            for (int gx = gx0; gx <= gx1; gx++)
                for (int gy = gy0; gy <= gy1; gy++)
                    result.Add(TileGrid.GetTileID(gx, gy));
            return result;
        }

        /// <summary>
        /// Liczba kafli pokrywających bbox bez alokacji listy — do capów / decyzji o LOD.
        /// </summary>
        public static int TileCount(BBox bounds)
        {
            if (bounds.MaxX < bounds.MinX || bounds.MaxY < bounds.MinY) return 0;
            var (gx0, gy0) = TileGrid.WorldToGrid(bounds.MinX, bounds.MinY);
            var (gx1, gy1) = TileGrid.WorldToGrid(bounds.MaxX, bounds.MaxY);
            return (gx1 - gx0 + 1) * (gy1 - gy0 + 1);
        }

        /// <summary>
        /// Dopasowanie kamery ortho do bbox z zachowaniem aspektu RenderTexture.
        /// rtAspect = szerokość/wysokość RT. center = środek bbox (świat XZ);
        /// orthoSize = połowa wysokości widoku (Unity ortho convention), clampowane.
        /// </summary>
        public static void FitOrtho(BBox bounds, float rtAspect, out Vector2 center, out float orthoSize,
            float marginFrac = DefaultMarginFrac, float minOrthoSize = MinOrthoSizeM, float maxOrthoSize = MaxOrthoSizeM)
        {
            center = new Vector2((bounds.MinX + bounds.MaxX) * 0.5f, (bounds.MinY + bounds.MaxY) * 0.5f);

            float w = Mathf.Max(0f, bounds.MaxX - bounds.MinX);
            float h = Mathf.Max(0f, bounds.MaxY - bounds.MinY);
            if (rtAspect <= 0f) rtAspect = 1f;

            // wysokość widoczna = 2*ortho; szerokość widoczna = 2*ortho*aspect
            float needByHeight = h * 0.5f;
            float needByWidth = (w * 0.5f) / rtAspect;
            orthoSize = Mathf.Max(needByHeight, needByWidth) * (1f + Mathf.Max(0f, marginFrac));
            orthoSize = Mathf.Clamp(orthoSize, minOrthoSize, maxOrthoSize);
        }

        /// <summary>
        /// Przelicza pikselowy delta przeciągania po RawImage na przesunięcie świata (XZ)
        /// do DODANIA do pozycji kamery, tak by zawartość podążała za kursorem
        /// (kamera jedzie przeciwnie do drag). rtPixelSize = rozmiar RawImage/RT w px.
        /// </summary>
        public static Vector2 PanScreenDeltaToWorld(Vector2 screenDelta, float orthoSize, Vector2 rtPixelSize)
        {
            if (rtPixelSize.y <= 0f) return Vector2.zero;
            // Dla ortho top-down piksele w X i Y mają tę samą skalę świata:
            // worldPerPixel = wysokość_widoku / wysokość_px = (2*ortho)/rtPixelSize.y.
            float worldPerPixel = (2f * orthoSize) / rtPixelSize.y;
            // drag w prawo (dx>0) → kamera w lewo (-x); drag w górę (dy>0) → kamera w dół (-z)
            return new Vector2(-screenDelta.x * worldPerPixel, -screenDelta.y * worldPerPixel);
        }

        /// <summary>
        /// Nowy orthographicSize po scrollu (dodatni scroll = zoom IN → mniejszy ortho).
        /// Mirror CameraController: mult = 1 - scroll*zoomFactor*0.1, clamp do [min,max].
        /// </summary>
        public static float ZoomStep(float orthoSize, float scrollDelta,
            float minOrthoSize = MinOrthoSizeM, float maxOrthoSize = MaxOrthoSizeM, float zoomFactor = ZoomFactor)
        {
            float mult = 1f - scrollDelta * zoomFactor * 0.1f;
            if (mult < 0.01f) mult = 0.01f; // guard przed inwersją / zerem
            return Mathf.Clamp(orthoSize * mult, minOrthoSize, maxOrthoSize);
        }

        /// <summary>
        /// Adaptacyjna szerokość linii trasy zależna od zoomu PODGLĄDU
        /// (formuła RoutePreviewOverlay, ale na orthoSize kamery podglądu).
        /// </summary>
        public static float AdaptiveWidth(float orthoSize,
            float widthScale = WidthScale, float minWidth = MinWidthM, float maxWidth = MaxWidthM)
        {
            return Mathf.Clamp(orthoSize * widthScale, minWidth, maxWidth);
        }
    }
}
