using DepotSystem.Furniture.Functional;
using UnityEngine;

namespace DepotSystem.Furniture.Placement
{
    /// <summary>
    /// Partial: preview renderer lifecycle (Create/Cleanup) + stamped cuboid spawn
    /// (po confirm) z warning overlay (MF-8) + DoorAnimator dla drzwi (MF-11).
    /// </summary>
    public partial class FurniturePlacer
    {
        private void CreatePreviewRenderer()
        {
            var go = new GameObject("FurniturePreview");
            go.transform.SetParent(transform, worldPositionStays: false);
            _previewRenderer = go.AddComponent<FurniturePreviewRenderer>();
        }

        private void CleanupPreview()
        {
            if (_previewRenderer != null)
            {
                _previewRenderer.Clear();
                Destroy(_previewRenderer.gameObject);
                _previewRenderer = null;
            }
        }

        private void SpawnStampedVisual(FurnitureItem item, PlacedFurnitureItem instance)
        {
            // Stamped cuboid — niezależny od preview, zostaje na scenie po confirm.
            // MF-7: klik na stamped → selection + context menu (Move/Rotate/Delete).
            // MF-8: dorzucony FurnitureWarningOverlay (TMP 3D billboard zielony ✓ / czerwony ✗).
            // MF-11: dla drzwi (specialPlacement=WallCell) dorzucony DoorAnimator (placeholder
            //        rotacja 0°→90° gdy pracownik w 1.5m).
            var go = new GameObject($"PlacedFurniture_{instance.instanceId}_{item.id}");
            go.transform.SetParent(transform, worldPositionStays: false);
            var renderer = go.AddComponent<FurniturePreviewRenderer>();
            renderer.SetItem(item);
            renderer.SetPosition(instance.position);
            renderer.SetRotation(instance.rotation);
            _placedVisuals[instance.instanceId] = go;

            // MF-8: warning overlay child obok stamped cuboid, Y offset +1.5m
            var overlayGO = new GameObject($"WarningOverlay_{instance.instanceId}");
            overlayGO.transform.SetParent(go.transform, worldPositionStays: false);
            overlayGO.transform.localPosition = Vector3.zero;
            var overlay = overlayGO.AddComponent<FurnitureWarningOverlay>();
            _warningOverlays[instance.instanceId] = overlay;

            // MF-11: door animator (tylko door_basic, NIE track_gate — brama wjazdowa nie ma skrzydła)
            // MM-15: track_gate (WallCell ale Industrial bramowy) pomijamy DoorAnimator
            if (item.ParseSpecialPlacement() == SpecialPlacement.WallCell && item.id != "track_gate")
            {
                go.AddComponent<DoorAnimator>();
            }
        }
    }
}
