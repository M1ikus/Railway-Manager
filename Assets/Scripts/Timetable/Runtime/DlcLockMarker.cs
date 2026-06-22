using UnityEngine;

namespace RailwayManager.Timetable
{
    /// <summary>
    /// Komponent dołączony do GameObject'a reprezentującego kłódkę DLC na mapie.
    /// Trzyma metadata miasta (nazwa, region, flagi) do wykorzystania przez klikalność,
    /// tooltip, filtrowanie po regionie itd. (M-PL-5).
    /// </summary>
    public class DlcLockMarker : MonoBehaviour
    {
        public string displayName;                  // "Drezno"
        public DlcCityCatalog.Region region;        // Germany
        public string osmName;                      // "Dresden" — oryginalna nazwa OSM matched
        public int population;                      // z OSM population tag (0 jeśli brak)
        public Vector2 position2D;                  // (x=east, y=north) world coords
        public bool isCatalog;                      // true = hand-picked, false = fallback by population
    }
}
