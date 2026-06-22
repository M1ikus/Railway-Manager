using System;
using System.Collections.Generic;
using RailwayManager.Core;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// MM-10: static catalog ścieżek modernizacji (z <c>modernization_paths.json</c>).
    ///
    /// Ładowane lazy przy pierwszym lookup (LoadAll()). Dla danego pojazdu z
    /// <c>seriesId="EN57"</c> zwraca wszystkie ścieżki z <c>sourceSeriesId="EN57"</c>
    /// (w EA jeden sourceSeries = jedna ścieżka, ale architektura wspiera wiele).
    /// </summary>
    public static class ModernizationPathCatalog
    {
        static readonly List<ModernizationPath> _all = new();
        public static bool IsLoaded { get; private set; }

        [Serializable] private class Wrapper
        {
            public int schemaFormatVersion = 0;
            public List<ModernizationPath> paths = new();
        }

        public static void LoadAll()
        {
            if (IsLoaded) return;
            _all.Clear();
            _all.AddRange(JsonCatalogLoader.LoadList<Wrapper, ModernizationPath>(
                "modernization_paths.json",
                w => w.paths,
                p => !string.IsNullOrEmpty(p.pathId),
                "ModernizationPathCatalog"));
            IsLoaded = true;
        }

        public static IReadOnlyList<ModernizationPath> GetAll()
        {
            if (!IsLoaded) LoadAll();
            return _all;
        }

        public static ModernizationPath GetByPathId(string pathId)
        {
            if (!IsLoaded) LoadAll();
            if (string.IsNullOrEmpty(pathId)) return null;
            foreach (var p in _all)
                if (p.pathId == pathId) return p;
            return null;
        }

        /// <summary>Lista ścieżek dostępnych dla pojazdu z danym <c>seriesId</c>.</summary>
        public static List<ModernizationPath> GetForSourceSeries(string sourceSeriesId)
        {
            if (!IsLoaded) LoadAll();
            var result = new List<ModernizationPath>();
            if (string.IsNullOrEmpty(sourceSeriesId)) return result;
            foreach (var p in _all)
                if (p.sourceSeriesId == sourceSeriesId) result.Add(p);
            return result;
        }
    }
}
