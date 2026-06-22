using System.Collections.Generic;
using RailwayManager.Fleet;

namespace DepotSystem
{
    /// <summary>
    /// TD-032: czysta logika łączenia/dzielenia składów — footprint merge/split, kolejność vehicleIds
    /// nos→tył wg geometrii, dopasowanie FleetConsistData. Bez Unity/sceny → w pełni testowalne w EditMode.
    ///
    /// Konwencje (z TD-031): footprint track-local [FrontDistM ≤ RearDistM]; DirSign +1 = nos ku EndPosition
    /// (nos = RearDistM), -1 = nos ku StartPosition (nos = FrontDistM). vehicleIds[0] = pojazd na NOSIE (loko/przód).
    /// </summary>
    public static class ConsistCouplingMath
    {
        /// <summary>Współrzędna nosa składu na osi toru wg DirSign.</summary>
        public static float NoseCoord(float front, float rear, int dirSign)
            => dirSign >= 0 ? rear : front;

        /// <summary>Czy skład A jest z PRZODU (nos dalej w kierunku mergedDir) niż B. aNose/bNose = współrzędne nosów.</summary>
        public static bool IsAFront(float aNose, float bNose, int mergedDir)
            => mergedDir >= 0 ? aNose >= bNose : aNose <= bNose;

        /// <summary>
        /// Footprint scalonego składu: span [spanMin, spanMax] (= min frontów / max rearów obu) re-anchorowany
        /// do DOKŁADNEJ sumy długości (nos mergedDir stały) → luka styku (~ContactGap) domknięta do 0.
        /// Zwraca (front, rear) z front ≤ rear.
        /// </summary>
        public static (float front, float rear) MergeFootprint(float spanMin, float spanMax, int mergedDir, float summedLengthM)
        {
            if (mergedDir >= 0)
            {
                float rear = spanMax;                 // nos przy max
                return (rear - summedLengthM, rear);
            }
            float frontEdge = spanMin;                // nos przy min
            return (frontEdge, frontEdge + summedLengthM);
        }

        /// <summary>Lista pojazdów w orientacji merged — odwrócona gdy DirSign składu przeciwny do mergedDir.</summary>
        public static List<int> OrderInMerged(List<int> ids, int dirSign, int mergedDir)
        {
            if (ids == null) return new List<int>();
            if ((dirSign >= 0) == (mergedDir >= 0)) return new List<int>(ids);
            var rev = new List<int>(ids);
            rev.Reverse();
            return rev;
        }

        /// <summary>
        /// Scalona kolejność vehicleIds nos→tył: pojazdy składu PRZEDNIEGO (od nosa) + pojazdy składu TYLNEGO
        /// (od nosa), każdy znormalizowany do orientacji merged. Caller wyznacza który skład jest z przodu
        /// (geometria, <see cref="IsAFront"/>).
        /// </summary>
        public static List<int> MergeVehicleOrder(List<int> frontIds, int frontDir,
                                                  List<int> rearIds, int rearDir, int mergedDir)
        {
            var result = OrderInMerged(frontIds, frontDir, mergedDir);
            result.AddRange(OrderInMerged(rearIds, rearDir, mergedDir));
            return result;
        }

        /// <summary>
        /// Split footprintu rodzica na część PRZEDNIĄ (od nosa, długość frontLen) i TYLNĄ (tailLen).
        /// frontLen + tailLen == długość rodzica. Oba zachowują DirSign rodzica. Zwraca dwa footprinty (front,rear).
        /// </summary>
        public static ((float front, float rear) frontPart, (float front, float rear) tailPart)
            SplitFootprint(float parentFront, float parentRear, int parentDir, float frontLen, float tailLen)
        {
            if (parentDir >= 0)
            {
                // nos = parentRear; część przednia od nosa w dół
                float fFront = parentRear - frontLen;
                return ((fFront, parentRear), (parentFront, fFront));
            }
            // nos = parentFront; część przednia od nosa w górę
            float fRear = parentFront + frontLen;
            return ((parentFront, fRear), (fRear, parentRear));
        }

        /// <summary>Znajduje FleetConsistData zawierający dany vehicleId (membership). Null gdy żaden.</summary>
        public static FleetConsistData FindConsistByVehicleId(IReadOnlyList<FleetConsistData> consists, int vehicleId)
        {
            if (consists == null) return null;
            for (int i = 0; i < consists.Count; i++)
            {
                var c = consists[i];
                if (c?.vehicleIds != null && c.vehicleIds.Contains(vehicleId)) return c;
            }
            return null;
        }

        /// <summary>Unikalna nazwa: baseName, potem „baseName (2)", „(3)"… aż nie koliduje z existingNames.</summary>
        public static string DedupConsistName(IEnumerable<string> existingNames, string baseName)
        {
            var set = new HashSet<string>(existingNames ?? System.Array.Empty<string>());
            if (!set.Contains(baseName)) return baseName;
            for (int n = 2; n < 1000; n++)
            {
                string candidate = $"{baseName} ({n})";
                if (!set.Contains(candidate)) return candidate;
            }
            return baseName + " (x)";
        }
    }
}
