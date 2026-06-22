namespace DepotSystem.Furniture.Functional
{
    /// <summary>
    /// MF-8 — runtime stan funkcjonalny postawionej instancji furniture.
    ///
    /// <see cref="IsActive"/> = czy obiekt jest funkcjonalnie aktywny (= dojście accessSide
    /// jest wolne, walidacja zwróciła Ok). Gdy false, funkcje obiektu są zablokowane:
    /// - Biurko nie liczy się jako WorkstationOffice (pracownik nie ma gdzie podejść)
    /// - Regał nie dolicza do PartInventory capacity (PartInventoryFurnitureBridge filter)
    /// - Kanał serwisowy nie liczy się jako workshop slot (M-Modernization)
    ///
    /// Recompute przy każdym placement/move/delete/rotate w danym depocie
    /// (FurniturePlacer.RecomputeAllFunctionalStates).
    ///
    /// Decyzja B14: brak dojścia = funkcja zablokowana (warning ikon + IsActive=false).
    /// Gracz świadomie postawił szafę plecami w kąt — wizualnie jest, funkcjonalnie nie.
    /// </summary>
    public struct FurnitureFunctionalState
    {
        /// <summary>Czy obiekt funkcjonalnie aktywny (dojście wolne, walidacja Ok).</summary>
        public bool IsActive;

        /// <summary>Powód zablokowania (Warning reason z FurnitureValidator) — dla UI tooltipa.</summary>
        public string BlockedReason;

        public static FurnitureFunctionalState Active() => new FurnitureFunctionalState { IsActive = true, BlockedReason = "" };
        public static FurnitureFunctionalState Blocked(string reason) => new FurnitureFunctionalState { IsActive = false, BlockedReason = reason ?? "" };
    }
}
