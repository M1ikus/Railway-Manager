using System.Collections.Generic;
using UnityEngine;
using DepotSystem;

namespace DepotSystem.Furniture.Placement
{
    /// <summary>
    /// MF-4..5 — snap helper dla furniture placement.
    ///
    /// MF-4: snap do najbliższej kratki 1m (analogicznie do RoomDetectionSystem,
    /// który operuje na siatce 1×1m).
    ///
    /// MF-5: auto-snap-to-wall — gdy ściana w promieniu 1.5m, auto-rotate tak żeby
    /// <c>accessSide</c> był odwrócony od ściany (pracownik ma dostęp z otwartej
    /// strony). Bliższa ściana wygrywa (decyzja 2026-05-03 — pri smallest distance).
    ///
    /// Pivot obiektu = środek footprintu po rotacji. Snap działa na pivot, więc
    /// dla parzystego footprint pivot ląduje na grid line (między cells), dla
    /// nieparzystego na środek cell. Akceptowalne dla MVP.
    /// </summary>
    public static class FurnitureSnapDetector
    {
        /// <summary>Rozmiar pojedynczej cell w metrach (spójnie z RoomDetectionSystem).</summary>
        public const float CellSize = 1f;

        /// <summary>Maksymalny dystans (m) auto-snap-to-wall. Spec MF-5: 1.5m.</summary>
        public const float WallSnapToleranceMeters = 1.5f;

        /// <summary>
        /// Snap world position do najbliższej kratki 1×1m (Y zostaje 0).
        /// Pivot ląduje na cell corner (grid line). Używane w MF-4 jako prosty snap.
        ///
        /// MF-6: dla footprint-aware walidacji preferuj <see cref="SnapToGridForFootprint"/>
        /// — uwzględnia parity (parzysty footprint → corner, nieparzysty → cell center)
        /// żeby footprint cells były symmetric wokół pivota.
        /// </summary>
        public static Vector3 SnapToGrid(Vector3 worldPos)
        {
            return new Vector3(
                Mathf.Round(worldPos.x / CellSize) * CellSize,
                0f,
                Mathf.Round(worldPos.z / CellSize) * CellSize
            );
        }

        /// <summary>
        /// MF-6 — parity-aware snap. Pivot snapowany tak żeby footprint cells były
        /// symmetric wokół pivota:
        /// - Parzysty rozmiar w danej osi → pivot na cell corner (grid line, integer position)
        /// - Nieparzysty rozmiar → pivot na cell center (integer + 0.5)
        ///
        /// Przykład: footprint 2×1 (parzysty x, nieparzysty z) → snap pivot.x do integer,
        /// snap pivot.z do integer+0.5. Po tym <see cref="GetFootprintCells"/> zwraca
        /// jednoznaczne, symmetric cells.
        /// </summary>
        public static Vector3 SnapToGridForFootprint(Vector3 worldPos, int footprintCellsX, int footprintCellsY, int rotationDeg)
        {
            var (sizeX, sizeZ) = GetRotatedFootprintSize(footprintCellsX, footprintCellsY, rotationDeg);
            int sX = Mathf.RoundToInt(sizeX);
            int sZ = Mathf.RoundToInt(sizeZ);

            float snappedX = (sX % 2 == 0)
                ? Mathf.Round(worldPos.x / CellSize) * CellSize          // parzysty → corner
                : Mathf.Floor(worldPos.x / CellSize) * CellSize + 0.5f;  // nieparzysty → center
            float snappedZ = (sZ % 2 == 0)
                ? Mathf.Round(worldPos.z / CellSize) * CellSize
                : Mathf.Floor(worldPos.z / CellSize) * CellSize + 0.5f;

            return new Vector3(snappedX, 0f, snappedZ);
        }

        /// <summary>
        /// MF-6 — zwraca listę cells (Vector2Int grid coords) zajmowanych przez footprint
        /// obiektu w danej pozycji + rotacji. Pivot jest środkiem footprint, cells są
        /// symmetric wokół pivota (zakładamy że pivot był snapowany przez <see cref="SnapToGridForFootprint"/>).
        ///
        /// Przykład: pivot=(5, _, 5), footprint 2×1, rotation 0 → cells [(4,5), (5,5)].
        /// </summary>
        public static List<Vector2Int> GetFootprintCells(Vector3 pivot, int footprintCellsX, int footprintCellsY, int rotationDeg)
        {
            var (sizeX, sizeZ) = GetRotatedFootprintSize(footprintCellsX, footprintCellsY, rotationDeg);
            int sX = Mathf.RoundToInt(sizeX);
            int sZ = Mathf.RoundToInt(sizeZ);

            // xMin = floor(pivot.x - sX/2), xMax = xMin + sX - 1
            int xMin = Mathf.FloorToInt(pivot.x - sX * 0.5f);
            int zMin = Mathf.FloorToInt(pivot.z - sZ * 0.5f);

            var cells = new List<Vector2Int>(sX * sZ);
            for (int dx = 0; dx < sX; dx++)
                for (int dz = 0; dz < sZ; dz++)
                    cells.Add(new Vector2Int(xMin + dx, zMin + dz));

            return cells;
        }

        /// <summary>
        /// MF-6 — zwraca cells po stronie <c>accessSide</c> obiektu (jedna linia 1×N lub N×1
        /// cells po krawędzi footprint w kierunku accessSide).
        ///
        /// Used by FurnitureValidator do sprawdzenia czy pracownik ma dojście (cells muszą
        /// być w pokoju + nie zajęte innym furniture).
        ///
        /// Dla AccessSide.All zwraca pustą listę (każda strona OK, brak walidacji dojścia).
        /// </summary>
        public static List<Vector2Int> GetAccessSideCells(Vector3 pivot, int footprintCellsX, int footprintCellsY, int rotationDeg, AccessSide side)
        {
            var result = new List<Vector2Int>();
            if (side == AccessSide.All) return result;

            var (sizeX, sizeZ) = GetRotatedFootprintSize(footprintCellsX, footprintCellsY, rotationDeg);
            int sX = Mathf.RoundToInt(sizeX);
            int sZ = Mathf.RoundToInt(sizeZ);
            int xMin = Mathf.FloorToInt(pivot.x - sX * 0.5f);
            int zMin = Mathf.FloorToInt(pivot.z - sZ * 0.5f);
            int xMax = xMin + sX - 1;
            int zMax = zMin + sZ - 1;

            // Lokalna direction accessSide w world coords po rotacji
            // Lokalne: Front=(0,1), Back=(0,-1), Left=(-1,0), Right=(1,0)
            // Rotacja 0: identycznie. Rotacja 90° CW (Unity Y rotation, view from above):
            // (x, z) → (z, -x). Powtarzane rotSteps razy.
            Vector2Int worldDir = side switch
            {
                AccessSide.Front => new Vector2Int(0, 1),
                AccessSide.Back  => new Vector2Int(0, -1),
                AccessSide.Left  => new Vector2Int(-1, 0),
                AccessSide.Right => new Vector2Int(1, 0),
                _                => new Vector2Int(0, 1),
            };
            int rotSteps = (((rotationDeg / 90) % 4) + 4) % 4;
            for (int i = 0; i < rotSteps; i++)
            {
                worldDir = new Vector2Int(worldDir.y, -worldDir.x);  // 90° CW from above
            }

            // Po rotacji worldDir = (0,1)/(0,-1)/(1,0)/(-1,0) — generuj line cells
            if (worldDir == new Vector2Int(0, 1))
            {
                // +Z direction: cells [(xMin, zMax+1)..(xMax, zMax+1)]
                for (int x = xMin; x <= xMax; x++) result.Add(new Vector2Int(x, zMax + 1));
            }
            else if (worldDir == new Vector2Int(0, -1))
            {
                for (int x = xMin; x <= xMax; x++) result.Add(new Vector2Int(x, zMin - 1));
            }
            else if (worldDir == new Vector2Int(1, 0))
            {
                for (int z = zMin; z <= zMax; z++) result.Add(new Vector2Int(xMax + 1, z));
            }
            else if (worldDir == new Vector2Int(-1, 0))
            {
                for (int z = zMin; z <= zMax; z++) result.Add(new Vector2Int(xMin - 1, z));
            }

            return result;
        }

        /// <summary>
        /// Pomocnik: po rotacji wymiary footprintu zmieniają się dla 90/270.
        /// Zwraca (sizeX, sizeZ) w metrach po uwzględnieniu rotation degree.
        /// </summary>
        public static (float sizeX, float sizeZ) GetRotatedFootprintSize(int footprintCellsX, int footprintCellsY, int rotationDeg)
        {
            int normRot = ((rotationDeg % 360) + 360) % 360;
            bool swap = normRot == 90 || normRot == 270;
            float sizeX = swap ? footprintCellsY : footprintCellsX;
            float sizeZ = swap ? footprintCellsX : footprintCellsY;
            return (sizeX, sizeZ);
        }

        /// <summary>Wynik auto-snap-to-wall lookup'u.</summary>
        public struct WallSnapResult
        {
            public bool hasSnap;
            public int suggestedRotationDeg;     // 0/90/180/270 (snapped do najbliższego cardinal)
            public float distanceToWall;         // metry, ≤ WallSnapToleranceMeters jeśli hasSnap
            public Vector3 closestPointOnWall;   // do diagnostyki / debug visualization
            public WallSegment nearestWall;
        }

        /// <summary>
        /// MF-5 — szuka najbliższej ściany w promieniu 1.5m od pivota i sugeruje rotację
        /// taką żeby <c>accessSide</c> obiektu patrzył w przeciwnym kierunku (od ściany).
        ///
        /// Bliższa ściana wygrywa (smallest distance). Gdy brak ściany w 1.5m → hasSnap=false,
        /// gracz steruje rotacją ręcznie przez R.
        ///
        /// Konwencja: lokalne +Z (forward) cuboida = strona <c>AccessSide.Front</c>.
        /// Pozostałe accessSide mają offset rotation:
        /// - Front:   0°   (lokalne +Z patrzy od ściany)
        /// - Back:    180° (lokalne -Z patrzy od ściany)
        /// - Left:    +90° (lokalne -X patrzy od ściany)
        /// - Right:   -90° (lokalne +X patrzy od ściany)
        /// - All:     0°   (każda strona OK, brak rotacji)
        /// </summary>
        public static WallSnapResult AutoSnapToWall(Vector3 pivot, FurnitureItem item, WallBuildingSystem wallSystem)
        {
            var result = new WallSnapResult { hasSnap = false, distanceToWall = float.MaxValue };

            if (item == null || wallSystem == null) return result;
            if (wallSystem.AllWalls == null || wallSystem.AllWalls.Count == 0) return result;

            // Znajdź najbliższą ścianę w 1.5m
            WallSegment nearestWall = null;
            float nearestDist = float.MaxValue;
            Vector3 nearestPoint = Vector3.zero;

            foreach (var wall in wallSystem.AllWalls)
            {
                if (wall == null) continue;
                Vector3 closest = ClosestPointOnSegment(pivot, wall.startPos, wall.endPos);
                float dist = Vector2.Distance(
                    new Vector2(pivot.x, pivot.z),
                    new Vector2(closest.x, closest.z)
                );
                if (dist < nearestDist && dist <= WallSnapToleranceMeters)
                {
                    nearestDist = dist;
                    nearestWall = wall;
                    nearestPoint = closest;
                }
            }

            if (nearestWall == null) return result;

            // Kierunek "od ściany w stronę pivota" (XZ plane, normalized)
            Vector3 fromWall = new Vector3(pivot.x - nearestPoint.x, 0f, pivot.z - nearestPoint.z);
            if (fromWall.sqrMagnitude < 0.0001f)
            {
                // Pivot dokładnie na ścianie — użyj normal'a ściany jako fallback
                Vector3 wallDir = nearestWall.Direction;
                fromWall = new Vector3(-wallDir.z, 0f, wallDir.x);  // perpendicular CCW
            }
            fromWall.Normalize();

            // Bazowa rotacja: lokalne +Z obiektu wskazuje fromWall
            // Atan2(x, z) bo Unity Y-up, +Z = forward, kąt liczony od osi Z (forward) do osi X (right)
            float baseRotation = Mathf.Atan2(fromWall.x, fromWall.z) * Mathf.Rad2Deg;

            // Adjust dla accessSide
            AccessSide side = item.ParseAccessSide();
            float accessOffset = side switch
            {
                AccessSide.Front => 0f,
                AccessSide.Back  => 180f,
                AccessSide.Left  => 90f,
                AccessSide.Right => -90f,
                AccessSide.All   => 0f,
                _                => 0f,
            };

            float rawRotation = baseRotation + accessOffset;
            // Snap do najbliższego cardinal (0/90/180/270)
            int snapped = Mathf.RoundToInt(rawRotation / 90f) * 90;
            int normalized = ((snapped % 360) + 360) % 360;

            result.hasSnap = true;
            result.suggestedRotationDeg = normalized;
            result.distanceToWall = nearestDist;
            result.closestPointOnWall = nearestPoint;
            result.nearestWall = nearestWall;
            return result;
        }

        /// <summary>Najbliższy punkt na odcinku [a,b] do punktu p (XZ plane only, Y zwraca 0).</summary>
        private static Vector3 ClosestPointOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 p2 = new Vector2(p.x, p.z);
            Vector2 a2 = new Vector2(a.x, a.z);
            Vector2 b2 = new Vector2(b.x, b.z);
            Vector2 ab = b2 - a2;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return new Vector3(a.x, 0f, a.z);
            float t = Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / lenSq);
            Vector2 closest = a2 + t * ab;
            return new Vector3(closest.x, 0f, closest.y);
        }
    }
}
