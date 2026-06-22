using System.Collections.Generic;
using UnityEngine;
using RailwayManager.Core;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Klasyfikator pozycji → województwo/kraj na podstawie załadowanych AdminRegion.
    /// Używa point-in-polygon z iteracją po trójkątach (AdminRegion.ContainsPoint).
    /// Singleton-like: trzymaj jeden instance per scene, buduj z AdminBoundaryLoader na starcie.
    /// </summary>
    public class VoivodeshipResolver
    {
        private readonly List<AdminRegion> _voivodeships;
        private readonly List<AdminRegion> _countries;

        public int VoivodeshipCount => _voivodeships.Count;
        public int CountryCount => _countries.Count;

        /// <summary>Tworzy resolver z załadowanej listy AdminRegion (rozdziela na kraje i województwa).</summary>
        public VoivodeshipResolver(List<AdminRegion> regions)
        {
            _voivodeships = new List<AdminRegion>();
            _countries = new List<AdminRegion>();
            if (regions == null) return;

            foreach (var r in regions)
            {
                if (r.adminLevel == 4) _voivodeships.Add(r);
                else if (r.adminLevel == 2) _countries.Add(r);
            }
        }

        /// <summary>
        /// Zwraca nazwę województwa zawierającego dany punkt, lub null jeśli poza granicami.
        /// O(k) per region z quick BBox reject, typowo testuje tylko 1-2 regiony realnie.
        /// </summary>
        public string GetVoivodeship(Vector2 worldPos)
        {
            foreach (var v in _voivodeships)
                if (v.ContainsPoint(worldPos))
                    return v.name;
            return null;
        }

        /// <summary>Zwraca pełny AdminRegion województwa (dla ISO code i innych metadanych).</summary>
        public AdminRegion GetVoivodeshipRegion(Vector2 worldPos)
        {
            foreach (var v in _voivodeships)
                if (v.ContainsPoint(worldPos))
                    return v;
            return null;
        }

        /// <summary>Zwraca nazwę kraju zawierającego dany punkt.</summary>
        public string GetCountry(Vector2 worldPos)
        {
            foreach (var c in _countries)
                if (c.ContainsPoint(worldPos))
                    return c.name;
            return null;
        }

        /// <summary>
        /// Zwraca ISO 3166-1 alpha-2 kod kraju (np. "PL", "DE") zawierającego punkt.
        /// Używane do wypełniania <c>RailwayStation.countryCode</c>/<c>CityPlace.countryCode</c>
        /// w stream processors + filter w UI per <see cref="GameState.ActiveDlcCountries"/>.
        /// Patrz: docs/design/dlc-multi-country.md
        /// </summary>
        public string GetCountryCode(Vector2 worldPos)
        {
            foreach (var c in _countries)
                if (c.ContainsPoint(worldPos))
                    return c.iso3166_1;
            return null;
        }

        /// <summary>
        /// Sprawdza czy trasa (polyline) przekracza granicę województwa.
        /// Zwraca true jeśli co najmniej dwa punkty polyline leżą w różnych województwach
        /// (lub jeden leży poza wszystkimi). Użycie: klasyfikator kategorii IRJ —
        /// międzywojewódzki vs wojewódzki.
        /// </summary>
        public bool CrossesVoivodeshipBorder(List<Vector2> polyline)
        {
            if (polyline == null || polyline.Count < 2) return false;

            string first = GetVoivodeship(polyline[0]);
            for (int i = 1; i < polyline.Count; i++)
            {
                string v = GetVoivodeship(polyline[i]);
                if (v != first)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Zwraca listę unikalnych województw przez które przechodzi trasa. Pomijane są
        /// null wpisy (punkty poza obszarem). Przydatne dla szczegółowej walidacji trasy.
        /// </summary>
        public List<string> GetVoivodeshipsOnRoute(List<Vector2> polyline)
        {
            var result = new List<string>();
            if (polyline == null) return result;

            foreach (var p in polyline)
            {
                string v = GetVoivodeship(p);
                if (v == null) continue;
                if (!result.Contains(v)) result.Add(v);
            }
            return result;
        }

        /// <summary>Quick check: czy resolver ma w ogóle załadowane województwa.</summary>
        public bool IsReady => _voivodeships.Count > 0;

        public void LogDiagnostics()
        {
            Log.Info($"[VoivodeshipResolver] {CountryCount} countries, {VoivodeshipCount} voivodeships");
            foreach (var v in _voivodeships)
                Log.Info($"[VoivodeshipResolver]   {v.name} (ISO: {v.iso3166_2 ?? "?"})");
        }
    }
}
