using System;
using System.Collections.Generic;

namespace DepotSystem.RoomLevel
{
    /// <summary>
    /// MM-1 — wymagania konkretnego lvla (1-5) dla konkretnego <see cref="RoomType"/>.
    ///
    /// Komplet warunków musi być spełniony równocześnie:
    /// - <see cref="minAreaSqM"/> — minimalna powierzchnia pomieszczenia
    /// - <see cref="furnitureRequirements"/> — wszystkie wymagane meble (ItemId/Function/Compound)
    ///
    /// Liczby z spec'a `memory/modernization_design.md` sekcja 2.2 (propozycyjne, do iteracji
    /// w M-Balance). Definiowane w <see cref="RoomLevelCatalog"/>.
    ///
    /// Bonus per lvl (gameplay impact) jest osobno — czyta go odpowiedni service:
    /// <see cref="RoomType.Hall"/> → WorkshopManager P-poziom inspection,
    /// <see cref="RoomType.Office"/> → OfficeService cap headcount + R&D speed,
    /// <see cref="RoomType.Dispatcher"/> → Dispatcher onboarding speed,
    /// <see cref="RoomType.TrafficController"/> → cap headcount (akcje od skill, MM-D12),
    /// <see cref="RoomType.Supervisor"/> → globalny morale,
    /// <see cref="RoomType.Social"/>/<see cref="RoomType.Bathroom"/> → morale per pracownik z accessSide free.
    /// </summary>
    [Serializable]
    public class RoomLevelRequirements
    {
        public RoomType roomType;
        public int level;            // 1-5
        public float minAreaSqM;
        public List<FurnitureRequirement> furnitureRequirements;

        /// <summary>MM-D22 / future scope: gdy true, lvl niedostępny w runtime (placeholder
        /// dla TrainingRoom post-EA). RoomLevelCatalog nie zwraca disabled entries.</summary>
        public bool disabled;

        public RoomLevelRequirements() { furnitureRequirements = new List<FurnitureRequirement>(); }

        public RoomLevelRequirements(RoomType roomType, int level, float minAreaSqM,
            params FurnitureRequirement[] reqs)
        {
            this.roomType = roomType;
            this.level = level;
            this.minAreaSqM = minAreaSqM;
            this.furnitureRequirements = new List<FurnitureRequirement>(reqs ?? Array.Empty<FurnitureRequirement>());
        }

        public override string ToString()
            => $"{roomType} lvl{level} (≥{minAreaSqM:F0}m², {furnitureRequirements.Count} req(s){(disabled ? ", disabled" : "")})";
    }
}
