using UnityEngine;
using UnityEngine.InputSystem;

namespace DepotSystem
{
    public partial class WallBuildingSystem
    {
        private void HandleWallBuild()
        {
            if (DepotUIManager.Instance.IsPointerOverUI())
            {
                HideWallPreview();
                HideWallBuildHud();
                return;
            }

            Vector3 mouseWorld = GetMouseWorldPosition();
            if (mouseWorld == Vector3.zero)
            {
                HideWallBuildHud();
                return;
            }

            Vector3 snapped = SnapToGrid(mouseWorld);
            RoomBuildSubMode subMode = DepotUIManager.Instance.CurrentRoomSubMode;
            string roomLabel = GetRoomBuildModeLabel(subMode);

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (state == WallBuildState.PreviewingRect)
                {
                    PauseWallRectSelection(subMode);
                    return;
                }

                CancelBuild();
                return;
            }

            switch (state)
            {
                case WallBuildState.Idle:
                case WallBuildState.PlacingCornerA:
                    rectEndSelectionPaused = false;
                    HideWallPreview();
                    UpdateWallBuildHud(
                        snapped,
                        $"{roomLabel} • wskaz pierwszy rog",
                        "LMB ustaw pierwszy rog",
                        new Color(0.60f, 1f, 0.60f, 1f));

                    if (_toolBuild.Primary.WasPressedThisFrame() && IsInsideBuildableArea(snapped))
                    {
                        cornerA = snapped;
                        state = WallBuildState.PreviewingRect;
                        rectEndSelectionPaused = false;
                        UpdateWallBuildHud(
                            cornerA,
                            $"{roomLabel} • start ({cornerA.x:F0}, {cornerA.z:F0})",
                            "Wskaz przeciwlegly rog • ESC anuluj",
                            Color.white);
                    }
                    break;

                case WallBuildState.PreviewingRect:
                    if (rectEndSelectionPaused)
                    {
                        if (Vector3.Distance(snapped, cornerA) > Mathf.Max(0.25f, gridSize * 0.5f))
                        {
                            rectEndSelectionPaused = false;
                        }
                        else
                        {
                            HideWallPreview();
                            UpdateWallBuildHud(
                                cornerA,
                                $"{roomLabel} • start ({cornerA.x:F0}, {cornerA.z:F0})",
                                RectPausedMessage,
                                Color.white);
                            break;
                        }
                    }

                    var reason = GetRectValidationReason(cornerA, snapped);
                    UpdateRectPreview(cornerA, snapped, reason);

                    float width = Mathf.Abs(snapped.x - cornerA.x);
                    float depth = Mathf.Abs(snapped.z - cornerA.z);
                    Vector3 anchor = new Vector3((cornerA.x + snapped.x) * 0.5f, 0f, (cornerA.z + snapped.z) * 0.5f);
                    UpdateWallBuildHud(
                        anchor,
                        $"{roomLabel} • {width:F0} x {depth:F0} m",
                        GetWallValidationLabel(reason),
                        reason == WallBuildValidationReason.Ready
                            ? new Color(0.60f, 1f, 0.60f, 1f)
                            : previewInvalid);

                    if (_toolBuild.Primary.WasPressedThisFrame() && reason == WallBuildValidationReason.Ready)
                    {
                        BuildRectWalls(cornerA, snapped);
                        state = WallBuildState.PlacingCornerA;
                        HideWallBuildHud();
                    }

                    if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
                        PauseWallRectSelection(subMode);
                    break;
            }
        }

        private void PauseWallRectSelection(RoomBuildSubMode subMode)
        {
            if (state != WallBuildState.PreviewingRect)
            {
                CancelBuild();
                return;
            }

            rectEndSelectionPaused = true;
            HideWallPreview();
            UpdateWallBuildHud(
                cornerA,
                $"{GetRoomBuildModeLabel(subMode)} • start ({cornerA.x:F0}, {cornerA.z:F0})",
                RectPausedMessage,
                Color.white);
        }

        private void UpdateRectPreview(Vector3 a, Vector3 bRaw, WallBuildValidationReason reason)
        {
            Vector3 b = SnapToGrid(bRaw);
            bool valid = reason == WallBuildValidationReason.Ready;
            Color color = valid ? previewValid : previewInvalid;
            previewValidationReason = reason;

            Vector3 c0 = new Vector3(a.x, 0.15f, a.z);
            Vector3 c1 = new Vector3(b.x, 0.15f, a.z);
            Vector3 c2 = new Vector3(b.x, 0.15f, b.z);
            Vector3 c3 = new Vector3(a.x, 0.15f, b.z);

            SetLinePositions(previewLines[0], c0, c1, color);
            SetLinePositions(previewLines[1], c1, c2, color);
            SetLinePositions(previewLines[2], c2, c3, color);
            SetLinePositions(previewLines[3], c3, c0, color);

            if (previewFill != null)
            {
                previewFill.SetActive(true);
                Vector3 center = (a + b) * 0.5f;
                center.y = 0.05f;
                previewFill.transform.position = center;
                previewFill.transform.localScale = new Vector3(
                    Mathf.Abs(b.x - a.x),
                    1f,
                    Mathf.Abs(b.z - a.z));

                var meshRenderer = previewFill.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                    meshRenderer.material.color = new Color(color.r, color.g, color.b, 0.15f);
            }

            if (previewParent != null)
                previewParent.SetActive(true);
        }

        private bool IsValidRect(Vector3 a, Vector3 b)
        {
            return GetRectValidationReason(a, b) == WallBuildValidationReason.Ready;
        }

        private WallBuildValidationReason GetRectValidationReason(Vector3 a, Vector3 b)
        {
            float dx = Mathf.Abs(b.x - a.x);
            float dz = Mathf.Abs(b.z - a.z);

            if (dx < minBuildingSize || dz < minBuildingSize)
                return WallBuildValidationReason.TooSmall;

            var requiredMin = GetRequiredMinSizeForCurrentSubMode();
            if (requiredMin.HasValue)
            {
                var (minW, minD) = requiredMin.Value;
                bool fitsRoomType = (dx >= minW && dz >= minD) || (dx >= minD && dz >= minW);
                if (!fitsRoomType)
                    return WallBuildValidationReason.RoomTypeTooSmall;
            }

            if (!IsInsideBuildableArea(a) || !IsInsideBuildableArea(b))
                return WallBuildValidationReason.OutsideBuildableArea;

            if (RectOverlapsExistingWalls(a, b))
                return WallBuildValidationReason.OverlapWalls;

            if (RectOverlapsTracks(a, b))
                return WallBuildValidationReason.OverlapTracks;

            return WallBuildValidationReason.Ready;
        }

        private (float minW, float minD)? GetRequiredMinSizeForCurrentSubMode()
        {
            if (DepotUIManager.Instance == null) return null;
            if (DepotUIManager.Instance.CurrentTool != ToolMode.BuildRoom) return null;
            if (DepotUIManager.Instance.CurrentRoomAction != RoomActionMode.BuildRoom) return null;

            RoomType type = SubModeToRoomType(DepotUIManager.Instance.CurrentRoomSubMode);
            if (!RoomRequirements.MinSize.ContainsKey(type)) return null;

            var (minW, minD, _) = RoomRequirements.MinSize[type];
            return (minW, minD);
        }

        private static RoomType SubModeToRoomType(RoomBuildSubMode mode) => mode switch
        {
            RoomBuildSubMode.Hall => RoomType.Hall,
            RoomBuildSubMode.Storage => RoomType.Storage,
            RoomBuildSubMode.Dispatcher => RoomType.Dispatcher,
            RoomBuildSubMode.Office => RoomType.Office,
            RoomBuildSubMode.Social => RoomType.Social,
            RoomBuildSubMode.Supervisor => RoomType.Supervisor,
            RoomBuildSubMode.Bathroom => RoomType.Bathroom,
            RoomBuildSubMode.Locker => RoomType.Locker,
            RoomBuildSubMode.Corridor => RoomType.Corridor,
            RoomBuildSubMode.TrafficController => RoomType.TrafficController,
            _ => RoomType.None
        };

        private bool RectOverlapsTracks(Vector3 a, Vector3 b)
        {
            Vector3 center = new Vector3(
                (a.x + b.x) * 0.5f,
                1f,
                (a.z + b.z) * 0.5f);
            Vector3 halfExtents = new Vector3(
                Mathf.Abs(b.x - a.x) * 0.5f,
                1.5f,
                Mathf.Abs(b.z - a.z) * 0.5f);

            Collider[] hits = Physics.OverlapBox(center, halfExtents, Quaternion.identity);
            foreach (var hit in hits)
            {
                if (hit.CompareTag("Track"))
                    return true;
            }

            return false;
        }

        private bool RectOverlapsExistingWalls(Vector3 a, Vector3 b)
        {
            float minX = Mathf.Min(a.x, b.x) + wallThickness;
            float maxX = Mathf.Max(a.x, b.x) - wallThickness;
            float minZ = Mathf.Min(a.z, b.z) + wallThickness;
            float maxZ = Mathf.Max(a.z, b.z) - wallThickness;

            foreach (var wall in allWalls)
            {
                float wMinX = Mathf.Min(wall.startPos.x, wall.endPos.x);
                float wMaxX = Mathf.Max(wall.startPos.x, wall.endPos.x);
                float wMinZ = Mathf.Min(wall.startPos.z, wall.endPos.z);
                float wMaxZ = Mathf.Max(wall.startPos.z, wall.endPos.z);

                float halfT = wallThickness * 0.5f;
                if (wMaxX + halfT > minX && wMinX - halfT < maxX &&
                    wMaxZ + halfT > minZ && wMinZ - halfT < maxZ)
                    return true;
            }

            return false;
        }

        private void BuildRectWalls(Vector3 a, Vector3 b)
        {
            Vector3 snappedB = SnapToGrid(b);
            float x0 = Mathf.Min(a.x, snappedB.x);
            float x1 = Mathf.Max(a.x, snappedB.x);
            float z0 = Mathf.Min(a.z, snappedB.z);
            float z1 = Mathf.Max(a.z, snappedB.z);

            int buildingId = nextBuildingId++;

            Vector3 bl = new Vector3(x0, 0, z0);
            Vector3 br = new Vector3(x1, 0, z0);
            Vector3 tr = new Vector3(x1, 0, z1);
            Vector3 tl = new Vector3(x0, 0, z1);

            CreateWallSegment(bl, br, buildingId);
            CreateWallSegment(tl, tr, buildingId);
            CreateWallSegment(bl, tl, buildingId);
            CreateWallSegment(br, tr, buildingId);

            HideWallPreview();
            OnWallsChanged?.Invoke();

            DepotSystem.Undo.UndoManager.Record(
                DepotSystem.Undo.UndoCategory.Pomieszczenia,
                new DepotSystem.Undo.BuildingPlacedCommand(buildingId));
        }

        private WallSegment CreateWallSegment(Vector3 start, Vector3 end, int buildingId)
        {
            var seg = new WallSegment
            {
                wallId = nextWallId++,
                startPos = start,
                endPos = end,
                height = wallHeight,
                buildingId = buildingId
            };

            seg.wallObject = BuildWallMesh(seg);
            allWalls.Add(seg);
            return seg;
        }

        private void HideWallPreview()
        {
            if (previewParent != null)
                previewParent.SetActive(false);

            HideWallBuildHud();
        }

        private void CancelBuild()
        {
            state = WallBuildState.Idle;
            rectEndSelectionPaused = false;
            previewValidationReason = WallBuildValidationReason.None;
            HideWallPreview();
        }
    }
}
