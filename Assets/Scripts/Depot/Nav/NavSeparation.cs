using UnityEngine;

namespace DepotSystem.Nav
{
    /// <summary>
    /// TD-033 H: czysta matematyka miękkiej separacji NPC (rozpychanie nakładających się kapsuł).
    /// Deterministyczna (zależy tylko od pozycji + indeksu, nie od kolejności iteracji) → MP/EditMode-friendly.
    /// Wywoływana przez EmployeeWalkSimulator po ruchu (dwuprzebiegowo: oblicz → zastosuj).
    /// </summary>
    public static class NavSeparation
    {
        /// <summary>
        /// Liczy displacement per pozycja (XZ). Każda para w promieniu odpycha się o połowę nakładania.
        /// Pozycje pokrywające się rozsuwane deterministycznie wg parzystości indeksu (stabilna kolejność
        /// = posortowane employeeId w callerze).
        /// </summary>
        public static void ComputeDisplacements(Vector2[] positions, int count, float radius, Vector2[] outDisp)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 pi = positions[i];
                Vector2 push = Vector2.zero;
                for (int j = 0; j < count; j++)
                {
                    if (j == i) continue;
                    Vector2 d = pi - positions[j];
                    float dist = d.magnitude;
                    if (dist > 1e-3f && dist < radius)
                        push += d / dist * (radius - dist) * 0.5f;
                    else if (dist <= 1e-3f)
                        push += new Vector2((i & 1) == 0 ? 0.02f : -0.02f, 0f); // pokrywające się → deterministyczny rozsiew
                }
                outDisp[i] = push;
            }
        }
    }
}
