using System;
using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// MM-11: static catalog modyfikacji posiadanych pojazdów.
    /// Ładowane lazy z <c>vehicle_modifications.json</c>.
    /// </summary>
    public static class VehicleModificationCatalog
    {
        static readonly List<VehicleModification> _all = new();
        public static bool IsLoaded { get; private set; }

        [Serializable] private class Wrapper
        {
            public int schemaFormatVersion = 0;
            public List<VehicleModification> modifications = new();
        }

        public static void LoadAll()
        {
            if (IsLoaded) return;
            _all.Clear();
            _all.AddRange(JsonCatalogLoader.LoadList<Wrapper, VehicleModification>(
                "vehicle_modifications.json",
                w => w.modifications,
                m => !string.IsNullOrEmpty(m.modId),
                "VehicleModificationCatalog"));
            IsLoaded = true;
        }

        public static IReadOnlyList<VehicleModification> GetAll()
        {
            if (!IsLoaded) LoadAll();
            return _all;
        }

        public static VehicleModification GetByModId(string modId)
        {
            if (!IsLoaded) LoadAll();
            if (string.IsNullOrEmpty(modId)) return null;
            foreach (var m in _all)
                if (m.modId == modId) return m;
            return null;
        }

        /// <summary>Lista modyfikacji applicable dla danego pojazdu (po typach + bogie + length).</summary>
        public static List<VehicleModification> GetApplicableFor(FleetVehicleData v)
        {
            var result = new List<VehicleModification>();
            if (v == null) return result;
            if (!IsLoaded) LoadAll();

            foreach (var m in _all)
            {
                if (!IsApplicable(m, v)) continue;
                result.Add(m);
            }
            return result;
        }

        public static bool IsApplicable(VehicleModification m, FleetVehicleData v)
        {
            if (m == null || v == null) return false;

            // Vehicle type filter
            if (m.applicableVehicleTypes != null && m.applicableVehicleTypes.Length > 0)
            {
                bool typeOk = false;
                string vTypeStr = v.type.ToString();
                foreach (var t in m.applicableVehicleTypes)
                    if (t == vTypeStr) { typeOk = true; break; }
                if (!typeOk) return false;
            }

            // Length filter
            if (v.lengthM < m.minVehicleLengthM) return false;
            if (v.lengthM > m.maxVehicleLengthM) return false;

            // Current bogie filter (BogieReplacement only)
            if (m.requiresCurrentBogie != null && m.requiresCurrentBogie.Length > 0)
            {
                string currentBogie = v.sourceConfiguration?.bogieTypeId ?? "";
                bool bogieOk = false;
                foreach (var b in m.requiresCurrentBogie)
                    if (b == currentBogie) { bogieOk = true; break; }
                if (!bogieOk) return false;
            }

            return true;
        }
    }
}
