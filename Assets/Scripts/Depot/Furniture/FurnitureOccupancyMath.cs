using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem.Furniture
{
    /// <summary>
    /// TD-034 A: czysta matematyka wyboru wolnego mebla pod rezerwację (occupancy slotów —
    /// krzesła/sofy/WC/szafki). Spośród WOLNYCH kandydatów wybiera najbliższy do punktu
    /// <c>from</c>, tie-break po instanceId rosnąco.
    ///
    /// <para>Deterministyczna — wynik nie zależy od kolejności wejścia (remis dystansu rozstrzyga
    /// niższe id), więc MP/EditMode-friendly (dyscyplina z TrackOccupancyMath/NavSeparation).</para>
    ///
    /// Mieszka w asmdef Depot (operuje na meblach Depot); używana przez <see cref="FurnitureOccupancyService"/>.
    /// Bez stanu Unity → testowalna w EditMode.
    /// </summary>
    public static class FurnitureOccupancyMath
    {
        // Próg remisu dystansu² — meble stoją na siatce 1×1m, więc równo-odległe instancje
        // (np. dwa krzesła) są częste i tie-break po id musi być stabilny.
        const float TieEpsilonSqr = 1e-4f;

        /// <summary>
        /// Zwraca instanceId najbliższego WOLNEGO mebla, lub -1 gdy żaden nie jest wolny / lista pusta/null.
        /// Dystans w XZ (Vector2). Remis dystansu² (w granicach <see cref="TieEpsilonSqr"/>) → niższy instanceId.
        /// </summary>
        public static int PickNearestFree(IReadOnlyList<(int id, Vector2 pos, bool occupied)> candidates, Vector2 from)
        {
            if (candidates == null) return -1;

            int bestId = -1;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c.occupied) continue;

                float sqr = (c.pos - from).sqrMagnitude;

                bool better;
                if (bestId < 0) better = true;                                  // pierwszy wolny
                else if (sqr < bestSqr - TieEpsilonSqr) better = true;          // istotnie bliżej
                else if (sqr <= bestSqr + TieEpsilonSqr && c.id < bestId) better = true; // remis → niższe id
                else better = false;

                if (better) { bestId = c.id; bestSqr = sqr; }
            }

            return bestId;
        }
    }
}
