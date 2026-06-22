namespace DepotSystem.Furniture
{
    /// <summary>
    /// MF-4 stub — service ownership lookup dla furniture/inventory operations.
    ///
    /// W EA single-player wszystko należy do gracza (zwraca <c>true</c>).
    /// W M10 podpinamy realny lookup w PlayerDepotRegistry (host-authoritative):
    /// klient sprawdza czy lokalnie posiada danego depota przed wysłaniem
    /// PlaceFurnitureRequest do hosta.
    ///
    /// Per-depot scope: <c>depotId</c> jest globalnie unique. W MP gracz A może mieć
    /// depot'y 1-3, gracz B ma 4-6, host trzyma autorytet, klient widzi UI tylko swoich.
    /// </summary>
    public static class OwnershipService
    {
        /// <summary>
        /// Lokalny gracz w EA single-player ma jeden depot z ID = 1.
        /// W M10: zastąpione przez NetworkClient.LocalDepotIds[0] lub podobne.
        /// </summary>
        public const int LocalDepotId = 1;

        /// <summary>
        /// Czy depot o danym ID należy do lokalnego gracza.
        /// EA: zawsze true (single-player, wszystko jest "lokalne").
        /// M10: lookup w PlayerDepotRegistry.GetOwner(depotId) == LocalPlayerId.
        /// </summary>
        public static bool IsOwnedByLocalPlayer(int depotId)
        {
            // EA single-player — jedyny "gracz" to lokalny user, wszystko mu należy.
            // Negative depotId (-1) traktujemy jako "nieprzypisany" → też OK lokalnie.
            return depotId >= 0;
        }
    }
}
