using UnityEngine;
using UnityEngine.InputSystem;

namespace DepotSystem
{
    public partial class WallBuildingSystem
    {
        // ═══════════════════════════════════════════
        //  SELEKCJA (tryb Select)
        // ═══════════════════════════════════════════

        private void HandleSelect()
        {
            if (DepotUIManager.Instance.IsPointerOverUI()) return;

            if (_toolBuild.Primary.WasPressedThisFrame())
            {
                if (Mouse.current != null)
                {
                    Ray ray = mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
                    if (Physics.Raycast(ray, out RaycastHit hit, 500f))
                    {
                        var wall = FindWallByHit(hit);
                        if (wall != null)
                        {
                            SelectWall(wall);
                            return;
                        }
                    }
                }
                // Kliknięto w puste — odznacz
                ClearSelection();
            }

            // Delete zaznaczonej ściany
            if (selectedWall != null && _toolBuild.Delete.WasPressedThisFrame())
            {
                RemoveWall(selectedWall);
                selectedWall = null;
            }
        }

        private void SelectWall(WallSegment wall)
        {
            ClearSelection();
            selectedWall = wall;
            SetWallColor(wall, selectedColor);
        }

        private void ClearSelection()
        {
            if (selectedWall != null)
            {
                SetWallColor(selectedWall, wallColor);
                selectedWall = null;
            }
        }
    }
}
