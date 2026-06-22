using System.Collections.Generic;
using DepotSystem.Furniture;

namespace DepotSystem.RoomLevel
{
    /// <summary>
    /// MM-D9 — compound requirements dla "stanowisk pracy".
    ///
    /// Życiowa intuicja: biurko = komplet biurko + komputer + krzesło, nie 3 osobne meble.
    /// Zamiast wymagać `desk_office×3 + monitor_desk×3 + chair_basic×3` w lvl3 Office,
    /// spec używa `WorkstationOfficeComplete×3`.
    ///
    /// Compound count = greedy: liczba kompletów = min(count każdego komponentu w pokoju).
    /// Brak walidacji adjacency (krzesło dowolnie w pokoju, nie musi być przy biurku) — uproszczenie EA.
    /// Post-EA może dorzucić geometric pairing.
    ///
    /// Compound counting używany przez <see cref="RoomLevelService"/> (MM-2)
    /// do walidacji <see cref="FurnitureRequirement"/> z <see cref="FurnitureReqKind.Compound"/>.
    /// </summary>
    public static class WorkstationDefinitions
    {
        /// <summary>
        /// compoundName → array itemIds wymagane do utworzenia jednego kompletu.
        /// </summary>
        public static readonly Dictionary<string, string[]> Compounds = new()
        {
            ["WorkstationOfficeComplete"]  = new[] { "desk_office", "monitor_desk", "chair_basic" },
            ["WorkstationTrafficComplete"] = new[] { "traffic_console", "monitor_desk", "chair_basic" },
        };

        public static bool IsCompound(string id) => Compounds.ContainsKey(id);

        public static IReadOnlyList<string> GetComponents(string compoundName)
        {
            return Compounds.TryGetValue(compoundName, out var arr) ? arr : System.Array.Empty<string>();
        }

        /// <summary>
        /// Greedy count kompletów w danym pokoju. Iteruje listę placedItems, filtruje
        /// po cells w pokoju (caller dostarcza filtr), liczy każdy komponent. Zwraca
        /// min(count komponentów) — tyle kompletów można "zmontować" z dostępnych części.
        /// </summary>
        /// <param name="placedInRoom">Lista mebli już przefiltrowanych — w danym pokoju.</param>
        /// <param name="compoundName">Nazwa compound (klucz <see cref="Compounds"/>).</param>
        /// <returns>Liczba kompletów (0 gdy brak komponentów lub nieznany compound).</returns>
        public static int CountCompounds(IEnumerable<PlacedFurnitureItem> placedInRoom, string compoundName)
        {
            if (!Compounds.TryGetValue(compoundName, out var components)) return 0;
            if (placedInRoom == null) return 0;

            var counts = new Dictionary<string, int>();
            foreach (var c in components) counts[c] = 0;

            foreach (var inst in placedInRoom)
            {
                if (inst == null) continue;
                if (counts.ContainsKey(inst.itemId)) counts[inst.itemId]++;
            }

            int min = int.MaxValue;
            foreach (var c in components)
                if (counts[c] < min) min = counts[c];
            return min == int.MaxValue ? 0 : min;
        }
    }
}
