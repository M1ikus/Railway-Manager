using System;
using System.Collections.Generic;
using RailwayManager.Core;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M7-5: Katalog zewnętrznych warsztatów (ZNTK/NEWAG/PESA/Cegielski).
    ///
    /// Ładuje <c>Assets/StreamingAssets/Fleet/external_workshops.json</c> przy
    /// pierwszym użyciu. Lookup via <see cref="GetById"/> oraz filter via
    /// <see cref="FindCompatible"/> (level + vehicle type).
    /// </summary>
    public static class ExternalWorkshopCatalog
    {
        static readonly List<ExternalWorkshop> _all = new();
        public static bool IsLoaded { get; private set; }

        [Serializable] private class Wrapper { public List<ExternalWorkshop> workshops = new(); }

        public static void LoadAll()
        {
            if (IsLoaded) return;
            _all.Clear();
            _all.AddRange(JsonCatalogLoader.LoadList<Wrapper, ExternalWorkshop>(
                "external_workshops.json",
                w => w.workshops,
                w => !string.IsNullOrEmpty(w.id) && !string.IsNullOrEmpty(w.name),
                "ExternalWorkshopCatalog"));
            IsLoaded = true;
        }

        public static IReadOnlyList<ExternalWorkshop> GetAll()
        {
            if (!IsLoaded) LoadAll();
            return _all;
        }

        public static ExternalWorkshop GetById(string id)
        {
            if (!IsLoaded) LoadAll();
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var w in _all)
                if (w.id == id) return w;
            return null;
        }

        /// <summary>
        /// Lista zakładów obsługujących dany poziom + zgodny specjalizacją z typem pojazdu.
        /// Brak specjalizacji w zakładzie = obsługuje wszystkie typy.
        /// </summary>
        public static List<ExternalWorkshop> FindCompatible(InspectionLevel level, FleetVehicleType type)
        {
            if (!IsLoaded) LoadAll();
            var result = new List<ExternalWorkshop>();
            foreach (var w in _all)
            {
                if (!w.CanPerformLevel(level)) continue;
                if (!w.CanServeType(type)) continue;
                result.Add(w);
            }
            return result;
        }
    }
}
