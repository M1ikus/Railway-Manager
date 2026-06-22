namespace DepotSystem.Furniture
{
    /// <summary>
    /// Specjalne reguły placement override'ujące standard cell-in-room walidację.
    /// Domyślnie None (standard rules: footprint mieści się w cells pokoju).
    /// WallCell używane wyłącznie przez drzwi (door_basic) — DoorPlacer w MF-11.
    /// </summary>
    public enum SpecialPlacement
    {
        None,        // standard: footprint mieści się w cells pokoju
        WallCell     // musi być na wall cell (granica 2 pokoi lub pokój/outside) — tylko drzwi
    }
}
