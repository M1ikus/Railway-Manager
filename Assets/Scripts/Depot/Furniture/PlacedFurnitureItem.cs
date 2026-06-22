using System;
using UnityEngine;

namespace DepotSystem.Furniture
{
    /// <summary>
    /// Instancja obiektu furniture postawionego w konkretnym depocie.
    ///
    /// Persistowany przez DepotSavable (MF-9). Per-depot scope dla MP-readiness:
    /// <see cref="depotId"/> jest globalnie unique, klucz dla per-player ownership w M10
    /// (OwnershipService lookup → host autoryzuje placement requests od klientów).
    /// </summary>
    [Serializable]
    public class PlacedFurnitureItem
    {
        // ── Tożsamość instancji ───────────────────────────
        public int instanceId = -1;          // unikalny ID per zajezdnia (>= 1)
        public string itemId = "";           // klucz do FurnitureCatalog (np. "desk_office")
        public int depotId = -1;             // globalnie unique — scope per gracz w M10

        // ── Position + rotation ───────────────────────────
        public Vector3 position;             // światowe XYZ
        public int rotation = 0;             // 0/90/180/270

        // ── Personel assignment ───────────────────────────
        public int assignedEmployeeId = -1;  // -1 = unassigned

        public override string ToString()
            => $"PlacedFurnitureItem(instance={instanceId}, item='{itemId}', depot={depotId}, " +
               $"pos={position}, rot={rotation}°, employee={assignedEmployeeId})";
    }
}
