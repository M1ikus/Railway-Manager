using System;

namespace DepotSystem.RoomLevel
{
    /// <summary>
    /// MM-1 — typ wymagania wyposażenia w <see cref="RoomLevelRequirements"/>.
    /// Trzy warianty:
    /// <list type="bullet">
    /// <item><c>ItemId</c> — konkretny mebel z FurnitureCatalog (np. "desk_office")</item>
    /// <item><c>Function</c> — każdy mebel z daną <c>ObjectFunction</c> liczy się (np. "ServicePit" = pit_small/medium/large/lift_aux)</item>
    /// <item><c>Compound</c> — pakiet kilku mebli liczonych razem jako "stanowisko" (zob. <see cref="WorkstationDefinitions"/>)</item>
    /// </list>
    /// </summary>
    public enum FurnitureReqKind
    {
        ItemId,
        Function,
        Compound,
    }

    /// <summary>
    /// MM-1 — pojedyncze wymaganie wyposażenia dla danego lvla pokoju (decyzja MM-D9).
    ///
    /// Przykłady:
    /// <code>
    /// new(FurnitureReqKind.ItemId, "cabinet_archive", 3)         // 3 szafy
    /// new(FurnitureReqKind.Function, "ServicePit", 2)           // 2 dowolne kanały (small/medium/large)
    /// new(FurnitureReqKind.Compound, "WorkstationOfficeComplete", 5)  // 5 stanowisk (greedy count)
    /// </code>
    /// </summary>
    [Serializable]
    public class FurnitureRequirement
    {
        public FurnitureReqKind kind;
        public string id;       // itemId / ObjectFunction string / compound name
        public int count;

        public FurnitureRequirement() { }

        public FurnitureRequirement(FurnitureReqKind kind, string id, int count)
        {
            this.kind = kind;
            this.id = id;
            this.count = count;
        }

        public override string ToString() => $"{kind}:{id}×{count}";
    }
}
