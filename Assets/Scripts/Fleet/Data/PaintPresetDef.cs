using System;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-6: Definicja preset'u paska (stripe). Ładowane z
    /// <c>StreamingAssets/Fleet/paint_presets.json</c>. Gracz w paint editor (M-FC-7)
    /// wybiera preset z listy → cienki StripeLayer dodaje się do SegmentPaint
    /// z parametrami z preset'u, ale color jest do kustomizacji.
    /// </summary>
    [Serializable]
    public class PaintPresetDef
    {
        public string id;                       // np. "twoStripes", "fartuszek"
        public string displayName;              // do wyświetlenia w UI
        public string description;              // tooltip

        // Template StripeLayer — gracz może override color w editor
        public float positionY;                 // 0.0 (góra) - 1.0 (dół)
        public float thickness;                 // 0.0 - 1.0 (% wysokości pudła)
        public StripeMode mode = StripeMode.Solid;
        public string defaultColor = "#FFFFFF"; // sugerowany kolor

        // Czy preset wprowadza wiele pasków na raz (np. "góra+dół").
        // Płaska struktura (nie rekursywna) by uniknąć JsonUtility serialization depth limit.
        public bool isCompound;
        public CompoundStripePart[] compoundParts;
    }

    /// <summary>
    /// M-FC-6: Pojedyncza część preset'u compound (płaski POCO, bez recursji).
    /// </summary>
    [Serializable]
    public class CompoundStripePart
    {
        public float positionY;
        public float thickness;
        public StripeMode mode = StripeMode.Solid;
        public string defaultColor = "#FFFFFF";
    }
}
