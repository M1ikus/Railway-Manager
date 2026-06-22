using System;
using System.Collections.Generic;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-1: Pełna definicja malowania pojazdu. Przechowywana w VehicleConfiguration
    /// (przy zakupie) i FleetVehicleData (po zakupie). Serializowalna do shareable
    /// string'a (base64+gzip+JSON, M-FC-7) — MP-compatible.
    /// </summary>
    [Serializable]
    public class PaintDefinition
    {
        public int schemaVersion = 1;
        public string applyMode = "AllSegments"; // AllSegments | PerSegment
        public List<SegmentPaint> segments = new();
    }

    /// <summary>
    /// M-FC-1: Malowanie pojedynczego członu (lub całego pudła dla wagonu/loko).
    /// Layer system: bottom paint (kolor bazowy) + stripes (paski) + decals (symbole/teksty).
    /// </summary>
    [Serializable]
    public class SegmentPaint
    {
        public int segmentIndex;                // 0 = pierwszy człon
        public string baseColor = "#FFFFFF";    // bottom paint (hex RGB)
        public List<StripeLayer> stripes = new();
        public List<DecalLayer> decals = new();
    }

    /// <summary>
    /// M-FC-1: Pasek na boku pudła. Pre-EA: presetId daje gotowy template,
    /// procedural editor pozwala customizować position/thickness/color.
    /// Max 5 stripes per segment (limit z spec'a).
    /// </summary>
    [Serializable]
    public class StripeLayer
    {
        public string presetId;                 // "twoStripes", "fartuszek", "" gdy custom
        public float positionY;                 // 0.0 (gora) - 1.0 (dol)
        public float thickness;                 // 0.0 - 1.0 (% wysokosci pudla)
        public string color = "#000000";
        public StripeMode mode = StripeMode.Solid;
    }

    /// <summary>
    /// M-FC-1: Tryb renderowania paska.
    /// <c>Solid</c> — linia ciągła (pre-EA default).
    /// <c>Dashed</c> — linia przerywana (pre-EA).
    /// <c>Skew</c> — fartuszek/skos (post-EA, skomplikowany rendering).
    /// </summary>
    public enum StripeMode { Solid, Dashed, Skew }

    /// <summary>
    /// M-FC-1: Symbol lub tekst na pudle. SymbolId odnosi się do <c>DecalCatalog</c>
    /// (~25-30 symbolów pre-EA). CustomText dla numerów wagonu lub fikcyjnej nazwy
    /// firmy gracza (placeholder przed Steam Workshop custom logo upload post-EA).
    /// Max 8 decals per segment.
    /// </summary>
    [Serializable]
    public class DecalLayer
    {
        public string symbolId;                 // "wheelchair", "wifi", "warning-triangle", ...
        public float positionX;                 // 0.0 (lewo) - 1.0 (prawo)
        public float positionY;                 // 0.0 (gora) - 1.0 (dol)
        public float scale = 1.0f;
        public float rotation = 0f;             // stopnie
        public string color = "#000000";
        public string customText;               // dla decals tekstowych (numery wagonu, nazwa firmy)
    }
}
