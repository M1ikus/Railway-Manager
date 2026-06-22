using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M7-1: Katalog interwałów przeglądów per-seria pojazdu.
    ///
    /// Ładuje <c>Assets/StreamingAssets/Fleet/inspection_intervals.json</c>
    /// przy starcie. Lookup via <see cref="GetForSeries(string)"/> — zwraca
    /// <see cref="InspectionIntervals"/> dla podanej seriesId (fallback na
    /// default z <see cref="FleetBalanceConstants"/> gdy brak wpisu).
    /// </summary>
    public static class InspectionCatalog
    {
        static Dictionary<string, InspectionIntervals> _bySeriesId = new();
        public static bool IsLoaded { get; private set; }

        [Serializable] private class IntervalsWrapper { public List<IntervalsEntry> intervals = new(); }
        [Serializable] private class IntervalsEntry
        {
            public string seriesId = string.Empty;
            public int p1LimitHours = FleetBalanceConstants.DefaultInspectionHoursP1;
            public int p2LimitDays = FleetBalanceConstants.DefaultInspectionDaysP2;
            public int p3LimitKm = FleetBalanceConstants.DefaultInspectionKmP3;
            public int p4LimitKm = FleetBalanceConstants.DefaultInspectionKmP4;
            public int p4LimitYears = FleetBalanceConstants.DefaultInspectionYearsP4;
            public int p5LimitKm = FleetBalanceConstants.DefaultInspectionKmP5;
            public int p5LimitYears = FleetBalanceConstants.DefaultInspectionYearsP5;
        }

        public static void LoadAll()
        {
            if (IsLoaded) return;
            _bySeriesId.Clear();

            string path = Path.Combine(AppPaths.FleetCatalogDir, "inspection_intervals.json");
            if (!File.Exists(path))
            {
                Log.Warn($"[InspectionCatalog] File not found: {path} — fallback do defaultów");
                IsLoaded = true;
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var parsed = JsonUtility.FromJson<IntervalsWrapper>(json);
                if (parsed?.intervals != null)
                {
                    foreach (var entry in parsed.intervals)
                    {
                        if (string.IsNullOrEmpty(entry.seriesId)) continue;
                        _bySeriesId[entry.seriesId] = new InspectionIntervals
                        {
                            p1LimitHours = entry.p1LimitHours,
                            p2LimitDays = entry.p2LimitDays,
                            p3LimitKm = entry.p3LimitKm,
                            p4LimitKm = entry.p4LimitKm,
                            p4LimitYears = entry.p4LimitYears,
                            p5LimitKm = entry.p5LimitKm,
                            p5LimitYears = entry.p5LimitYears,
                        };
                    }
                }
                IsLoaded = true;
                Log.Info($"[InspectionCatalog] Loaded: {_bySeriesId.Count} series intervals");
            }
            catch (Exception e)
            {
                Log.Error($"[InspectionCatalog] Load failed: {e.Message}");
                IsLoaded = true; // mark loaded to avoid retry
            }
        }

        /// <summary>
        /// Zwraca interwały dla serii. Gdy brak wpisu → zwraca obiekt z wartościami default.
        /// Zawsze zwraca nie-null.
        /// </summary>
        public static InspectionIntervals GetForSeries(string seriesId)
        {
            if (!IsLoaded) LoadAll();
            if (string.IsNullOrEmpty(seriesId)) return InspectionIntervals.CreateDefault();
            return _bySeriesId.TryGetValue(seriesId, out var iv) ? iv : InspectionIntervals.CreateDefault();
        }
    }
}
