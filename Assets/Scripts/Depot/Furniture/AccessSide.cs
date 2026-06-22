namespace DepotSystem.Furniture
{
    /// <summary>
    /// Strona obiektu furniture z której pracownik podchodzi do interakcji.
    /// Walidacja MF-6: kratka 1×1m po stronie accessSide musi być wolna,
    /// inaczej funkcje obiektu są blokowane (warning ikon).
    /// </summary>
    public enum AccessSide
    {
        Front,
        Back,
        Left,
        Right,
        All       // dostęp z każdej strony (np. krzesło, lampa, plant)
    }
}
