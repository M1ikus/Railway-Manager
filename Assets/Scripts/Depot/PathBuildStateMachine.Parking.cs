using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DepotSystem
{
    public partial class PathBuildStateMachine
    {
        private void HandleParkingMode()
        {
            // Switching from painted path/road preview into parking should reset the old flow.
            if (state == BuildState.Painting || state == BuildState.WaitingConfirm)
                CancelBuild();

            if (_toolBuild.Cancel.WasPressedThisFrame())
            {
                if (state == BuildState.PreviewingRect)
                {
                    PauseParkingSelection();
                    return;
                }

                CancelBuild();
                return;
            }

            var mousePos = GetMouseWorldPosition();
            if (!mousePos.HasValue)
            {
                HidePreviewHud();
                return;
            }

            Vector3 snapped = SnapToGrid(mousePos.Value);
            HidePreviewCell();

            switch (state)
            {
                case BuildState.Idle:
                case BuildState.PlacingCornerA:
                    parkingEndSelectionPaused = false;
                    UpdatePreviewHud(
                        snapped,
                        "Parking • wskaz pierwszy rog",
                        "LMB ustaw pierwszy rog",
                        new Color(0.60f, 1f, 0.60f, 1f));

                    if (_toolBuild.Primary.WasPressedThisFrame())
                    {
                        pointA = snapped;
                        state = BuildState.PreviewingRect;
                        parkingEndSelectionPaused = false;
                        CreatePreviewFill();
                        UpdatePreviewHud(
                            pointA,
                            $"Parking • start ({pointA.x:F0}, {pointA.z:F0})",
                            "Wskaz przeciwlegly rog • ESC anuluj",
                            Color.white);
                    }
                    break;

                case BuildState.PreviewingRect:
                    if (parkingEndSelectionPaused)
                    {
                        if (Vector3.Distance(snapped, pointA) > Mathf.Max(0.25f, gridSize * 0.5f))
                        {
                            parkingEndSelectionPaused = false;
                            CreatePreviewFill();
                        }
                        else
                        {
                            HidePreviewFill();
                            UpdateParkingHud(pointA, pointA, PathBuildValidationReason.None, true);
                            break;
                        }
                    }

                    bool valid = TryValidateParking(pointA, snapped, out var reason);
                    UpdatePreviewFill(pointA, snapped, valid);
                    UpdateParkingHud(pointA, snapped, valid ? PathBuildValidationReason.Ready : reason, false);

                    if (_toolBuild.Primary.WasPressedThisFrame() && valid)
                    {
                        BuildParking(pointA, snapped);
                        state = BuildState.PlacingCornerA;
                        HidePreviewFill();
                        HidePreviewHud();
                    }

                    if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
                        PauseParkingSelection();
                    break;
            }
        }

        private void PauseParkingSelection()
        {
            if (state != BuildState.PreviewingRect)
            {
                CancelBuild();
                return;
            }

            parkingEndSelectionPaused = true;
            HidePreviewFill();
            UpdateParkingHud(pointA, pointA, PathBuildValidationReason.None, true);
        }

        private bool IsValidParking(Vector3 a, Vector3 b)
        {
            return TryValidateParking(a, b, out _);
        }

        private bool TryValidateParking(Vector3 a, Vector3 b, out PathBuildValidationReason reason)
        {
            float dx = Mathf.Abs(b.x - a.x);
            float dz = Mathf.Abs(b.z - a.z);
            if (dx < 3f || dz < 3f)
            {
                reason = PathBuildValidationReason.RectTooSmall;
                return false;
            }

            if (buildableArea.HasValue)
            {
                var bounds = buildableArea.Value;
                if (!bounds.Contains(new Vector3(a.x, bounds.center.y, a.z)) ||
                    !bounds.Contains(new Vector3(b.x, bounds.center.y, b.z)))
                {
                    reason = PathBuildValidationReason.OutsideBuildableArea;
                    return false;
                }
            }

            if (RectOverlapsTracks(a, b))
            {
                reason = PathBuildValidationReason.RectOverlapsTracks;
                return false;
            }

            reason = PathBuildValidationReason.Ready;
            return true;
        }

        private void BuildParking(Vector3 a, Vector3 b)
        {
            if (pathGraph == null || visualBuilder == null) return;

            float x0 = Mathf.Min(a.x, b.x);
            float x1 = Mathf.Max(a.x, b.x);
            float z0 = Mathf.Min(a.z, b.z);
            float z1 = Mathf.Max(a.z, b.z);

            RemoveCellsInRect(x0, x1, z0, z1);

            int nBL = pathGraph.GetOrCreateNode(new Vector3(x0, 0, z0));
            int nBR = pathGraph.GetOrCreateNode(new Vector3(x1, 0, z0));
            int nTL = pathGraph.GetOrCreateNode(new Vector3(x0, 0, z1));
            int nTR = pathGraph.GetOrCreateNode(new Vector3(x1, 0, z1));

            int e1 = pathGraph.AddEdge(nBL, nBR, PathEdgeType.Parking, 0f);
            pathGraph.AddEdge(nBR, nTR, PathEdgeType.Parking, 0f);
            pathGraph.AddEdge(nTR, nTL, PathEdgeType.Parking, 0f);
            pathGraph.AddEdge(nTL, nBL, PathEdgeType.Parking, 0f);

            var parkingVisual = visualBuilder.PlaceParkingLot(a, b, e1);

            DepotSystem.Undo.UndoManager.Record(
                DepotSystem.Undo.UndoCategory.Sciezki,
                new DepotSystem.Undo.ParkingPlacedCommand(a, b, e1, parkingVisual));
        }

        private void RemoveCellsInRect(float x0, float x1, float z0, float z1)
        {
            Vector2Int cellMin = WorldToCell(new Vector3(x0, 0, z0));
            Vector2Int cellMax = WorldToCell(new Vector3(x1 - 0.01f, 0, z1 - 0.01f));

            var toRemove = new List<Vector2Int>();
            foreach (var cell in placedCells)
            {
                if (cell.x >= cellMin.x && cell.x <= cellMax.x &&
                    cell.y >= cellMin.y && cell.y <= cellMax.y)
                    toRemove.Add(cell);
            }

            foreach (var cell in toRemove)
                RemoveCell(cell);
        }
    }
}
