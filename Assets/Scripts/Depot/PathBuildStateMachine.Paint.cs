using System.Collections.Generic;
using UnityEngine;

namespace DepotSystem
{
    public partial class PathBuildStateMachine
    {
        private void HandlePaintMode(PathBuildSubMode subMode)
        {
            // Switching away from parking should clear the rectangle workflow first.
            if (state == BuildState.PlacingCornerA || state == BuildState.PreviewingRect)
                CancelBuild();

            if (_toolBuild.Cancel.WasPressedThisFrame())
            {
                CancelBuild();
                return;
            }

            var mousePos = GetMouseWorldPosition();
            if (!mousePos.HasValue)
            {
                if (state != BuildState.WaitingConfirm)
                {
                    HidePreviewCell();
                    HidePreviewHud();
                }

                return;
            }

            Vector2Int cell = WorldToCell(mousePos.Value);

            switch (state)
            {
                case BuildState.Idle:
                    ShowCursorPreview(cell, subMode);
                    if (_toolBuild.Primary.WasPressedThisFrame())
                    {
                        pendingSubMode = subMode;
                        pendingCells.Clear();
                        AddCellsToPending(cell, subMode);
                        state = BuildState.Painting;
                        UpdatePendingVisuals();
                        UpdatePendingPaintHud(subMode, "Trzymaj LMB i maluj • zwolnij, aby obejrzec podglad");
                    }
                    break;

                case BuildState.Painting:
                    AddCellsToPending(cell, subMode);
                    UpdatePendingVisuals();
                    UpdatePendingPaintHud(subMode, "Zwolnij LMB, aby zostawic podglad");

                    if (_toolBuild.Primary.WasReleasedThisFrame())
                    {
                        state = BuildState.WaitingConfirm;
                        HidePreviewCell();
                        UpdatePendingPaintHud(subMode, "LMB zatwierdz • ESC anuluj");
                    }
                    break;

                case BuildState.WaitingConfirm:
                    if (_toolBuild.Primary.WasPressedThisFrame())
                    {
                        var batchCells = new List<Vector2Int>();
                        DepotSystem.Undo.UndoManager.Silenced = true;
                        try
                        {
                            foreach (var pendingCell in pendingCells)
                            {
                                if (placedCells.Contains(pendingCell))
                                    continue;

                                PlaceCell(pendingCell, pendingSubMode);
                                batchCells.Add(pendingCell);
                            }
                        }
                        finally
                        {
                            DepotSystem.Undo.UndoManager.Silenced = false;
                        }

                        if (batchCells.Count > 0)
                        {
                            DepotSystem.Undo.UndoManager.Record(
                                DepotSystem.Undo.UndoCategory.Sciezki,
                                new DepotSystem.Undo.PathCellsBatchCommand(batchCells, pendingSubMode, true));
                        }

                        ClearPendingVisuals();
                        pendingCells.Clear();
                        state = BuildState.Idle;
                        HidePreviewHud();
                    }
                    break;
            }
        }

        private void AddCellsToPending(Vector2Int cell, PathBuildSubMode subMode)
        {
            if (subMode == PathBuildSubMode.Road)
            {
                foreach (var brushCell in GetRoadBrushCells(cell))
                {
                    if (!placedCells.Contains(brushCell) && TryValidateRoadCell(brushCell, out _))
                        pendingCells.Add(brushCell);
                }

                return;
            }

            if (!placedCells.Contains(cell) && TryValidateCell(cell, subMode, out _))
                pendingCells.Add(cell);
        }

        private void ShowCursorPreview(Vector2Int cell, PathBuildSubMode subMode)
        {
            if (subMode == PathBuildSubMode.Road)
            {
                var brushCells = GetRoadBrushCells(cell);
                bool anyInvalid = false;
                int validCount = 0;

                foreach (var brushCell in brushCells)
                {
                    if (placedCells.Contains(brushCell))
                    {
                        anyInvalid = true;
                        continue;
                    }

                    if (TryValidateRoadCell(brushCell, out _))
                    {
                        validCount++;
                    }
                    else
                    {
                        anyInvalid = true;
                    }
                }

                ShowPreviewStrip(brushCells, !anyInvalid);
                UpdatePaintHoverHud(
                    cell,
                    subMode,
                    validCount,
                    brushCells.Count,
                    anyInvalid ? PathBuildValidationReason.BrushPartiallyBlocked : PathBuildValidationReason.Ready);
                return;
            }

            bool valid = TryValidateCell(cell, subMode, out var reason);
            ShowPreviewCell(CellToWorld(cell), valid);
            UpdatePaintHoverHud(cell, subMode, valid ? 1 : 0, 1, valid ? PathBuildValidationReason.Ready : reason);
        }

        private bool IsCellValidForRoad(Vector2Int cell)
        {
            return TryValidateRoadCell(cell, out _);
        }

        private bool IsCellValid(Vector2Int cell, PathBuildSubMode subMode)
        {
            return TryValidateCell(cell, subMode, out _);
        }

        private bool TryValidateRoadCell(Vector2Int cell, out PathBuildValidationReason reason)
        {
            return TryValidateCell(cell, PathBuildSubMode.Road, out reason);
        }

        private bool TryValidateCell(Vector2Int cell, PathBuildSubMode subMode, out PathBuildValidationReason reason)
        {
            if (placedCells.Contains(cell))
            {
                reason = PathBuildValidationReason.OccupiedCell;
                return false;
            }

            Vector3 center = CellToWorld(cell);

            if (buildableArea.HasValue)
            {
                var bounds = buildableArea.Value;
                if (!bounds.Contains(new Vector3(center.x, bounds.center.y, center.z)))
                {
                    reason = PathBuildValidationReason.OutsideBuildableArea;
                    return false;
                }
            }

            if (!ValidateCellCrossing(center, subMode))
            {
                reason = subMode == PathBuildSubMode.Road
                    ? PathBuildValidationReason.TrackCrossingBlocked
                    : PathBuildValidationReason.TurnoutCrossingBlocked;
                return false;
            }

            reason = PathBuildValidationReason.Ready;
            return true;
        }

        private List<Vector2Int> GetRoadBrushCells(Vector2Int centerCell)
        {
            int half = roadBrushSize / 2;
            var brushCells = new List<Vector2Int>(roadBrushSize * roadBrushSize);
            for (int dx = -half; dx < roadBrushSize - half; dx++)
            {
                for (int dy = -half; dy < roadBrushSize - half; dy++)
                    brushCells.Add(centerCell + new Vector2Int(dx, dy));
            }

            return brushCells;
        }

        private void PlaceCell(Vector2Int cell, PathBuildSubMode subMode)
        {
            if (pathGraph == null || visualBuilder == null) return;
            if (placedCells.Contains(cell)) return;

            PathEdgeType edgeType = subMode == PathBuildSubMode.Path
                ? PathEdgeType.Path
                : PathEdgeType.Road;

            placedCells.Add(cell);
            cellTypes[cell] = subMode;

            Vector3 center = CellToWorld(cell);
            int nodeId = pathGraph.GetOrCreateNode(center);
            cellToNodeId[cell] = nodeId;

            Vector2Int[] neighbors =
            {
                cell + Vector2Int.up,
                cell + Vector2Int.down,
                cell + Vector2Int.left,
                cell + Vector2Int.right
            };

            foreach (var neighbor in neighbors)
            {
                if (cellToNodeId.TryGetValue(neighbor, out int neighborNodeId))
                    pathGraph.AddEdge(nodeId, neighborNodeId, edgeType, gridSize);
            }

            var visual = visualBuilder.PlaceCell(center, gridSize, edgeType);
            if (visual != null)
                cellVisuals[cell] = visual;

            DepotSystem.Undo.UndoManager.Record(
                DepotSystem.Undo.UndoCategory.Sciezki,
                new DepotSystem.Undo.PathCellPlacedCommand(cell));
        }

        public void UndoPlaceCell(Vector2Int cell, PathBuildSubMode subMode)
        {
            PlaceCell(cell, subMode);
        }

        public void UndoRemoveCell(Vector2Int cell)
        {
            RemoveCell(cell);
        }

        private void RemoveCell(Vector2Int cell)
        {
            if (!placedCells.Contains(cell)) return;

            PathBuildSubMode savedSubMode = PathBuildSubMode.Path;
            if (cellTypes.TryGetValue(cell, out var knownSubMode))
            {
                savedSubMode = knownSubMode;
            }
            if (cellToNodeId.TryGetValue(cell, out int nodeIdForSave))
            {
                var node = pathGraph?.GetNode(nodeIdForSave);
                if (node != null && node.EdgeIds.Count > 0)
                {
                    var firstEdge = pathGraph.GetEdge(node.EdgeIds[0]);
                    if (firstEdge != null)
                    {
                        savedSubMode = firstEdge.EdgeType switch
                        {
                            PathEdgeType.Road => PathBuildSubMode.Road,
                            PathEdgeType.Parking => PathBuildSubMode.Parking,
                            _ => PathBuildSubMode.Path
                        };
                    }
                }
            }

            if (cellToNodeId.TryGetValue(cell, out int nodeId))
            {
                var node = pathGraph.GetNode(nodeId);
                if (node != null)
                {
                    var edgesToRemove = new List<int>(node.EdgeIds);
                    foreach (int edgeId in edgesToRemove)
                        pathGraph.RemoveEdge(edgeId);
                }

                cellToNodeId.Remove(cell);
            }

            if (cellVisuals.TryGetValue(cell, out var visual))
            {
                if (visual != null) Destroy(visual);
                cellVisuals.Remove(cell);
            }

            placedCells.Remove(cell);
            cellTypes.Remove(cell);

            DepotSystem.Undo.UndoManager.Record(
                DepotSystem.Undo.UndoCategory.Sciezki,
                new DepotSystem.Undo.PathCellRemovedCommand(cell, savedSubMode));
        }
    }
}
