using System;
using System.Collections.Generic;
using UnityEngine;
using DepotSystem.Furniture;
using DepotSystem.Furniture.Placement;
using DepotSystem.OutdoorEquipment;

namespace DepotSystem.Nav
{
    /// <summary>Zbiór przeszkód nav (napompowane NavRect) + waypointy świateł drzwi.</summary>
    public struct NavObstacleSet
    {
        public List<NavRect> Obstacles;
        public List<Vector2> DoorWaypoints;
        public static NavObstacleSet Empty()
            => new NavObstacleSet { Obstacles = new List<NavRect>(), DoorWaypoints = new List<Vector2>() };
    }

    /// <summary>
    /// TD-033: buduje zbiór przeszkód nav z danych zajezdni — ściany (split na otworach przejezdnych
    /// Door/TrackGate; Window = solid, bo ma parapet), outdoor equipment (AABB), meble (footprint AABB).
    /// Przeszkody napompowane o promień kapsuły. Czysta konwersja (per-źródło helpery testowalne w EditMode);
    /// cache + invalidacja + gizmos żyją w `DepotNavService` (warstwa MonoBehaviour).
    /// </summary>
    public static class NavObstacleSetBuilder
    {
        public const float DefaultWorkerRadius = 0.35f;  // kapsuła scale 0.6 → r≈0.3 + margines
        public const float WallThickness = 0.2f;         // = WallBuildingSystem.wallThickness (default)

        /// <summary>Pełny build ze scenowych systemów.</summary>
        public static NavObstacleSet Build(WallBuildingSystem walls, OutdoorEquipmentPlacer equipment,
            FurniturePlacer furniture, float inflate)
        {
            var set = NavObstacleSet.Empty();

            if (walls != null)
            {
                Func<OpeningType, float> widthFn = walls.GetOpeningWidth;
                var all = walls.AllWalls;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null)
                        AddWall(all[i], WallThickness, inflate, widthFn, set.Obstacles, set.DoorWaypoints);
            }

            if (equipment != null)
            {
                var placed = equipment.Placed;
                for (int i = 0; i < placed.Count; i++)
                    if (placed[i] != null) AddEquipment(placed[i], inflate, set.Obstacles);
            }

            if (furniture != null)
            {
                var inst = furniture.PlacedInstances;
                for (int i = 0; i < inst.Count; i++)
                    if (inst[i] != null) AddFurniture(inst[i], inflate, set.Obstacles);
            }

            return set;
        }

        /// <summary>
        /// Jedna ściana → solid NavRect-y (split tylko na otworach PRZEJEZDNYCH: Door + TrackGate;
        /// Window zostaje solid) + waypoint świetła w środku każdego przejezdnego otworu.
        /// </summary>
        public static void AddWall(WallSegment seg, float thickness, float inflate,
            Func<OpeningType, float> openingWidth, List<NavRect> obstacles, List<Vector2> doorWaypoints)
        {
            Vector2 a = new Vector2(seg.startPos.x, seg.startPos.z);
            Vector2 b = new Vector2(seg.endPos.x, seg.endPos.z);
            float len = (b - a).magnitude;
            if (len < 1e-3f) return;
            Vector2 dir = (b - a) / len;

            var gaps = new List<Vector2>(); // (start, end) wzdłuż ściany
            if (seg.openings != null)
            {
                for (int i = 0; i < seg.openings.Count; i++)
                {
                    var op = seg.openings[i];
                    if (op.type != OpeningType.Door && op.type != OpeningType.TrackGate) continue; // Window = parapet = solid
                    float w = openingWidth != null ? openingWidth(op.type) : 1.2f;
                    float s = Mathf.Clamp(op.distanceOnWall - w * 0.5f, 0f, len);
                    float e = Mathf.Clamp(op.distanceOnWall + w * 0.5f, 0f, len);
                    if (e > s)
                    {
                        gaps.Add(new Vector2(s, e));
                        doorWaypoints.Add(a + dir * op.distanceOnWall); // światło otworu (na osi ściany)
                    }
                }
                gaps.Sort((x, y) => x.x.CompareTo(y.x));
            }

            float cursor = 0f;
            for (int i = 0; i < gaps.Count; i++)
            {
                if (gaps[i].x > cursor + 1e-3f)
                    obstacles.Add(NavRect.FromSegment(a + dir * cursor, a + dir * gaps[i].x, thickness).Inflate(inflate));
                cursor = Mathf.Max(cursor, gaps[i].y);
            }
            if (cursor < len - 1e-3f)
                obstacles.Add(NavRect.FromSegment(a + dir * cursor, a + dir * len, thickness).Inflate(inflate));
        }

        /// <summary>Outdoor equipment → AABB (XZ) z cornerA/cornerB, napompowany.</summary>
        public static void AddEquipment(PlacedOutdoorEquipment e, float inflate, List<NavRect> obstacles)
        {
            Vector2 a = new Vector2(e.cornerA.x, e.cornerA.z);
            Vector2 b = new Vector2(e.cornerB.x, e.cornerB.z);
            obstacles.Add(NavRect.FromAabb(Vector2.Min(a, b), Vector2.Max(a, b)).Inflate(inflate));
        }

        /// <summary>Mebel → footprint AABB (z katalogu po itemId). Fallback 1×1 gdy brak w katalogu.</summary>
        public static void AddFurniture(PlacedFurnitureItem f, float inflate, List<NavRect> obstacles)
        {
            var item = FurnitureCatalog.FindById(f.itemId);
            int fx = item != null ? item.footprintCells.x : 1;
            int fy = item != null ? item.footprintCells.y : 1;
            AddFurnitureFootprint(f.position, fx, fy, f.rotation, inflate, obstacles);
        }

        /// <summary>Pure: footprint mebla → AABB komórek (1 m) → napompowany NavRect. Testowalne bez katalogu.</summary>
        public static void AddFurnitureFootprint(Vector3 pivot, int footprintCellsX, int footprintCellsY,
            int rotationDeg, float inflate, List<NavRect> obstacles)
        {
            var cells = FurnitureSnapDetector.GetFootprintCells(pivot, footprintCellsX, footprintCellsY, rotationDeg);
            if (cells == null || cells.Count == 0) return;
            int minX = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxZ = int.MinValue;
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                if (c.x < minX) minX = c.x;
                if (c.x > maxX) maxX = c.x;
                if (c.y < minZ) minZ = c.y;
                if (c.y > maxZ) maxZ = c.y;
            }
            obstacles.Add(NavRect.FromAabb(new Vector2(minX, minZ), new Vector2(maxX + 1, maxZ + 1)).Inflate(inflate));
        }
    }
}
