using System.Collections.Generic;
using UnityEngine;
using formap;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Usługa "czy punkt jest w Polsce" — używana przez <see cref="CountryBorderOverlayRenderer"/>
    /// (szary overlay poza granicami) oraz w przyszłości przez M-PL-5 DLC locks.
    ///
    /// Polska jest zdefiniowana jako suma:
    /// - country polygon (admin_level=2, ISO3166-1=PL) jeśli jest w danych OSM
    /// - wszystkie województwa (admin_level=4) jeśli country polygon nie ma
    ///
    /// PIP test używa <see cref="AdminRegion.ContainsPoint"/> (barycentric coords
    /// na trójkątach). O(k) per region z bbox-reject, typowo testuje 1-2 regiony realnie.
    /// </summary>
    public static class CountryOverlayService
    {
        private static readonly List<AdminRegion> _polandRegions = new();
        private static BBox _polandBBox;
        private static bool _initialized;
        private static bool _hasCountryPolygon;

        public static bool IsInitialized => _initialized;
        public static BBox PolandBoundingBox => _polandBBox;
        public static bool HasCountryPolygon => _hasCountryPolygon;
        public static int RegionCount => _polandRegions.Count;

        /// <summary>
        /// Lista regionów PL (country polygon jeśli jest + wszystkie województwa).
        /// Używane przez <see cref="VoivodeshipGroundRenderer"/> do generowania mesh'ów ground.
        /// </summary>
        public static IReadOnlyList<AdminRegion> GetPolandRegions() => _polandRegions;

        /// <summary>Event firowany po udanej inicjalizacji. Subskrybuj żeby rescan-ować stare dane.</summary>
        public static event System.Action OnInitialized;

        /// <summary>
        /// Inicjalizuje service listą AdminRegion (typowo wynik <see cref="AdminBoundaryLoader.LoadFrom"/>).
        /// Bezpieczne do wielokrotnego wywołania — resetuje stan przed załadowaniem nowych danych.
        /// </summary>
        public static void Initialize(List<AdminRegion> regions)
        {
            _polandRegions.Clear();
            _initialized = false;
            _hasCountryPolygon = false;

            if (regions == null || regions.Count == 0)
            {
                Log.Warn("[CountryOverlayService] No admin regions — service disabled (fail-open).");
                return;
            }

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            int voivodeshipCount = 0;

            foreach (var r in regions)
            {
                bool isPoland = false;

                if (r.adminLevel == 2)
                {
                    // Country polygon — akceptujemy Polskę po wielu możliwych nazwach OSM
                    // (różne dump'y mogą mieć różne wpisy: "Polska", "Poland", "Republic of Poland",
                    // "Rzeczpospolita Polska"; iso3166_1 nie zawsze jest wypełniony przez formap).
                    bool isPolandCountry =
                        r.iso3166_1 == "PL"
                        || r.name == "Polska"
                        || r.name == "Poland"
                        || r.name == "Republic of Poland"
                        || r.name == "Rzeczpospolita Polska"
                        || (r.name != null && r.name.IndexOf("polsk", System.StringComparison.OrdinalIgnoreCase) >= 0);

                    if (isPolandCountry)
                    {
                        isPoland = true;
                        _hasCountryPolygon = true;
                    }
                }
                else if (r.adminLevel == 4)
                {
                    // Województwa są zawsze w Polsce (w Polish PBF nie powinno być innych)
                    isPoland = true;
                    voivodeshipCount++;
                }

                if (!isPoland) continue;

                _polandRegions.Add(r);

                minX = Mathf.Min(minX, r.boundingBox.MinX);
                maxX = Mathf.Max(maxX, r.boundingBox.MaxX);
                minY = Mathf.Min(minY, r.boundingBox.MinY);
                maxY = Mathf.Max(maxY, r.boundingBox.MaxY);
            }

            if (_polandRegions.Count == 0)
            {
                Log.Warn("[CountryOverlayService] No Polish regions found in admin data — service disabled.");
                return;
            }

            _polandBBox = new BBox { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };
            _initialized = true;

            Log.Info($"[CountryOverlayService] Initialized: {_polandRegions.Count} regions "
                     + $"(country polygon: {(_hasCountryPolygon ? "yes" : "no, using voivodeships sum")}, "
                     + $"{voivodeshipCount} voivodeships). "
                     + $"PL BBox: [{minX:F0},{minY:F0}] to [{maxX:F0},{maxY:F0}] = "
                     + $"{(maxX - minX) / 1000f:F0}x{(maxY - minY) / 1000f:F0} km.");

            OnInitialized?.Invoke();
        }

        /// <summary>
        /// M-DLC foundation 2026-05-04: alias dla <see cref="IsInsidePoland"/>.
        /// Aktualnie identyczne (base game = tylko PL aktywne), ale call-sites mogą
        /// migrować do tej nazwy. Po pełnym M-DLC milestone'ie wewnętrzna implementacja
        /// będzie iterować po wszystkich AdminRegion z <see cref="GameState.ActiveDlcCountries"/>
        /// — bez breaking changes dla callerów.
        ///
        /// Patrz: docs/design/dlc-multi-country.md
        /// </summary>
        public static bool IsInActiveCountries(Vector2 worldPos) => IsInsidePoland(worldPos);

        /// <summary>Test czy punkt (w world coords 2D — XZ z Unity) leży w Polsce.</summary>
        public static bool IsInsidePoland(Vector2 worldPos)
        {
            if (!_initialized) return true; // fail-open — bez danych traktujemy wszystko jako Polskę
            if (worldPos.x < _polandBBox.MinX || worldPos.x > _polandBBox.MaxX
                || worldPos.y < _polandBBox.MinY || worldPos.y > _polandBBox.MaxY)
                return false;

            // Preferuj country polygon (admin_level=2 = "Polska") — jeden dokładny outline bez luk.
            // Województwa (admin_level=4) w PBF mogą być niekompletne (np. mamy tylko 14/16) i mieć
            // szczeliny między granicami → punkt na granicy między województwami daje false negative.
            // Country polygon eliminuje ten problem.
            if (_hasCountryPolygon)
            {
                foreach (var r in _polandRegions)
                    if (r.adminLevel == 2 && r.ContainsPoint(worldPos)) return true;
                return false;
            }

            // Fallback: bez country polygon używamy sumę województw (z możliwymi lukami)
            foreach (var r in _polandRegions)
                if (r.ContainsPoint(worldPos)) return true;
            return false;
        }

        /// <summary>
        /// Szybki test: czy tile jest CAŁY poza Polską? Sprawdza 5×5 grid (25 punktów).
        /// False-positives (częściowy overlap z PL traktowany jako "in Poland") są OK —
        /// granica narysuje się naturalnie przez brak overlay'a na partial tiles.
        ///
        /// 25 punktów zamiast 5 (4 corners + center) eliminuje false-negatives wewnątrz PL
        /// gdzie 5-punktowy test mógł trafić w lukę między województwami (jeśli PBF nie ma
        /// country polygon i mamy tylko niekompletne województwa).
        /// </summary>
        public static bool IsTileFullyOutsidePoland(BBox tileBBox)
        {
            if (!_initialized) return false;

            // Quick reject: bbox tile poza bbox Polski = na pewno outside
            if (tileBBox.MaxX < _polandBBox.MinX || tileBBox.MinX > _polandBBox.MaxX
                || tileBBox.MaxY < _polandBBox.MinY || tileBBox.MinY > _polandBBox.MaxY)
                return true;

            // 5×5 grid = 25 sample points — gęsty enough żeby nie wpaść w lukę przy 10km tile
            const int N = 5;
            float dx = (tileBBox.MaxX - tileBBox.MinX) / (N - 1);
            float dy = (tileBBox.MaxY - tileBBox.MinY) / (N - 1);

            for (int ix = 0; ix < N; ix++)
            for (int iy = 0; iy < N; iy++)
            {
                var p = new Vector2(tileBBox.MinX + ix * dx, tileBBox.MinY + iy * dy);
                if (IsInsidePoland(p)) return false; // znaleziono punkt w PL → tile NIE jest fully outside
            }
            return true;
        }
    }
}
