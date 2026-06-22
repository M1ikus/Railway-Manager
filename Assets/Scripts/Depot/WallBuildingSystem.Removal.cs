using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public partial class WallBuildingSystem
    {
        // ═══════════════════════════════════════════
        //  USUWANIE — public API + Undo restore
        // ═══════════════════════════════════════════

        public void RemoveWall(WallSegment wall)
        {
            // Zapisz do undo
            var savedStart = wall.startPos;
            var savedEnd = wall.endPos;
            var savedHeight = wall.height;
            var savedBuildingId = wall.buildingId;

            if (wall.wallObject != null)
                Destroy(wall.wallObject);
            allWalls.Remove(wall);
            OnWallsChanged?.Invoke();

            DepotSystem.Undo.UndoManager.Record(
                DepotSystem.Undo.UndoCategory.Pomieszczenia,
                new DepotSystem.Undo.WallRemovedCommand(savedStart, savedEnd, savedHeight, savedBuildingId));
        }

        /// <summary>
        /// Usuwa wszystkie 4 ściany budynku.
        /// </summary>
        public void RemoveBuilding(int buildingId)
        {
            var toRemove = allWalls.FindAll(w => w.buildingId == buildingId);

            // Zapisz dane wszystkich ścian do undo
            var savedWalls = new List<(Vector3, Vector3, float)>();
            foreach (var w in toRemove)
                savedWalls.Add((w.startPos, w.endPos, w.height));

            foreach (var wall in toRemove)
            {
                if (wall.wallObject != null)
                    Destroy(wall.wallObject);
            }
            allWalls.RemoveAll(w => w.buildingId == buildingId);
            OnWallsChanged?.Invoke();

            if (savedWalls.Count > 0)
            {
                DepotSystem.Undo.UndoManager.Record(
                    DepotSystem.Undo.UndoCategory.Pomieszczenia,
                    new DepotSystem.Undo.BuildingRemovedCommand(savedWalls));
            }
        }

        /// <summary>Public wrapper — odtwórz ścianę z danych (bez nagrywania undo)</summary>
        public void UndoCreateWall(Vector3 start, Vector3 end, float height, int buildingId)
        {
            var seg = new WallSegment
            {
                wallId = nextWallId++,
                startPos = start,
                endPos = end,
                height = height,
                buildingId = buildingId
            };
            seg.wallObject = BuildWallMesh(seg);
            allWalls.Add(seg);
            OnWallsChanged?.Invoke();
        }

        /// <summary>Public wrapper — odtwórz cały budynek z listy ścian (bez nagrywania undo)</summary>
        public void UndoCreateBuilding(List<(Vector3 start, Vector3 end, float height)> walls)
        {
            int buildingId = nextBuildingId++;
            foreach (var (s, e, h) in walls)
            {
                var seg = new WallSegment
                {
                    wallId = nextWallId++,
                    startPos = s,
                    endPos = e,
                    height = h,
                    buildingId = buildingId
                };
                seg.wallObject = BuildWallMesh(seg);
                allWalls.Add(seg);
            }
            OnWallsChanged?.Invoke();
        }

        /// <summary>Public wrapper — usuń budynek bez nagrywania undo</summary>
        public void UndoRemoveBuilding(int buildingId)
        {
            var toRemove = allWalls.FindAll(w => w.buildingId == buildingId);
            foreach (var wall in toRemove)
            {
                if (wall.wallObject != null)
                    Destroy(wall.wallObject);
            }
            allWalls.RemoveAll(w => w.buildingId == buildingId);
            OnWallsChanged?.Invoke();
        }
    }
}
