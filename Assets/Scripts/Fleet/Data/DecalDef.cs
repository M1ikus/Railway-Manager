using System;

namespace RailwayManager.Fleet
{
    /// <summary>
    /// M-FC-6: Definicja symbolu (decal) — element katalogu dostępnego do umieszczenia
    /// w paint editor (M-FC-7). Ładowane z <c>StreamingAssets/Fleet/decal_catalog.json</c>.
    /// Sprite atlas wskazywany przez <c>spriteResourcePath</c> (np. "Decals/wheelchair") —
    /// load przez Resources.Load. Pre-EA: ~25-30 ikon. Post-EA: Steam Workshop custom upload.
    /// </summary>
    [Serializable]
    public class DecalDef
    {
        public string id;                       // np. "wheelchair", "warning-triangle", "digit-7"
        public string displayName;              // do wyświetlenia w UI
        public string category;                 // "info" / "warning" / "digit" / "letter" / "arrow" / "logo"
        public string spriteResourcePath;       // Resources/<path> bez ext, np. "Decals/wheelchair"
        public bool supportsCustomText;         // gdy true, gracz może wpisać own tekst (numer wagonu)
    }
}
