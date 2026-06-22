using DepotSystem;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.Furniture.Placement
{
    /// <summary>
    /// Partial: public placement API (Start/Cancel/Confirm). Lifecycle samego placement
    /// session-a (aktywuje preview, czyści po Esc, dorzuca do _placedInstances po LMB).
    /// </summary>
    public partial class FurniturePlacer
    {
        // ════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════

        /// <summary>
        /// Aktywuje placement mode dla danego obiektu w danym depocie.
        /// Sprawdza ownership (M10 stub), tworzy preview cuboid.
        /// </summary>
        public void StartPlacement(FurnitureItem item, int depotId)
        {
            if (item == null)
            {
                Log.Error("[FurniturePlacer] StartPlacement: item is null");
                return;
            }

            // Authority check (MF-4 + MP-ready). EA: zawsze true.
            if (!OwnershipService.IsOwnedByLocalPlayer(depotId))
            {
                Log.Warn($"[FurniturePlacer] StartPlacement: depotId={depotId} nie należy do lokalnego gracza");
                return;
            }

            // MF-11: drzwi (specialPlacement=WallCell) używają osobnego flow w DoorPlacer
            if (item.ParseSpecialPlacement() == SpecialPlacement.WallCell)
            {
                CancelPlacement();  // jeśli był aktywny standardowy placement
                var doorPlacer = DoorPlacer.Instance;
                if (doorPlacer == null)
                {
                    var go = new GameObject("DoorPlacer (auto-created by FurniturePlacer)");
                    doorPlacer = go.AddComponent<DoorPlacer>();
                }
                doorPlacer.StartDoorPlacement(item, depotId);
                return;
            }

            CleanupPreview();

            _currentItem = item;
            _currentItemId = item.id;
            _currentRotationDeg = item.defaultRotation;
            _currentDepotId = depotId;
            _wallSnapActive = false;
            _wallSnapDistance = 0f;
            _userManualRotation = false;
            _lastValidation = ValidationResult.Ok();
            _validationLevel = "Ok";
            _validationReason = "";

            CreatePreviewRenderer();
            _previewRenderer.SetItem(item);
            _previewRenderer.SetRotation(_currentRotationDeg);

            _isActive = true;
            Log.Info($"[FurniturePlacer] Started placement: '{item.id}' ({item.displayName}) " +
                     $"footprint={item.footprintCells.x}x{item.footprintCells.y}, depot={depotId}");
        }

        /// <summary>Anuluje placement (Esc lub external call). Czyści preview.
        /// 2026-05-08: gdy w trybie Move (po MoveInstance), przywraca instancję
        /// na oryginalne miejsce z _moveSnapshot.</summary>
        public void CancelPlacement()
        {
            if (!_isActive) return;
            Log.Info($"[FurniturePlacer] Cancelled placement: '{_currentItemId}'");

            // Move rollback — restore snapshot do _placedInstances + spawn visual
            if (_isMoveOperation && _moveSnapshot != null)
            {
                var item = FurnitureCatalog.FindById(_moveSnapshot.itemId);
                if (item != null)
                {
                    _placedInstances.Add(_moveSnapshot);
                    _placedInstancesCount = _placedInstances.Count;
                    SpawnStampedVisual(item, _moveSnapshot);
                    Log.Info($"[FurniturePlacer] Move CANCEL → przywrócono instance #{_moveSnapshot.instanceId} " +
                             $"'{_moveSnapshot.itemId}' @ {_moveSnapshot.position}");
                    RecomputeAllFunctionalStates();
                }
                _moveSnapshot = null;
                _isMoveOperation = false;
            }

            CleanupPreview();
            _isActive = false;
            _currentItem = null;
            _currentItemId = null;
            _currentDepotId = -1;
        }

        /// <summary>
        /// Zatwierdza placement — dorzuca PlacedFurnitureItem do _placedInstances + spawnuje
        /// stamped cuboid visualization. Tool zostaje aktywny — gracz może stawiać kolejne.
        ///
        /// W MF-4 brak walidacji (MF-6 dorzuci). W MF-9 dorzucimy persistence przez DepotSavable.
        /// </summary>
        public void ConfirmPlacement()
        {
            if (!_isActive || _currentItem == null)
            {
                Log.Warn("[FurniturePlacer] ConfirmPlacement: no active placement");
                return;
            }

            // Authority recheck (paranoia — depot ownership mógł się zmienić w MP)
            if (!OwnershipService.IsOwnedByLocalPlayer(_currentDepotId))
            {
                Log.Warn($"[FurniturePlacer] ConfirmPlacement: depot {_currentDepotId} no longer owned, cancelling");
                CancelPlacement();
                return;
            }

            // MF-6: blokuj Error, pozwól Ok i Warning (Warning = brak dojścia, decyzja B14)
            if (_lastValidation.Level == ValidationLevel.Error)
            {
                Log.Warn($"[FurniturePlacer] ConfirmPlacement: walidacja Error → blocked. Reason: {_lastValidation.Reason}");
                return;
            }

            // M-Economy Faza 5: pobierz koszt mebla — tylko NOWE (move = już opłacony, refund przy
            // pickupie zsuppressowany). Blokada gdy nie stać („nie stać → nie buduj").
            if (!_isMoveOperation &&
                !ConstructionBilling.TryCharge(ConstructionCosts.FurnitureGroszy(), "furniture", _currentItem.id))
            {
                Log.Warn($"[FurniturePlacer] ConfirmPlacement: brak srodkow na mebel '{_currentItem.id}' → blocked");
                return;
            }

            // 2026-05-08: dla Move zachowaj assignedEmployeeId ze snapshot żeby personel
            // nadal miał reference do tego mebla (Office worker przy biurku, mechanik przy
            // ServicePit itd.).
            int preservedEmployeeId = _isMoveOperation && _moveSnapshot != null
                ? _moveSnapshot.assignedEmployeeId
                : -1;

            var instance = new PlacedFurnitureItem
            {
                instanceId = _nextInstanceId++,
                itemId = _currentItem.id,
                depotId = _currentDepotId,
                position = _snappedWorldPos,
                rotation = _currentRotationDeg,
                assignedEmployeeId = preservedEmployeeId
            };
            _placedInstances.Add(instance);
            _placedInstancesCount = _placedInstances.Count;

            SpawnStampedVisual(_currentItem, instance);

            string warningSuffix = _lastValidation.Level == ValidationLevel.Warning
                ? $" [WARNING: {_lastValidation.Reason}]"
                : "";
            string moveSuffix = _isMoveOperation
                ? $" [MOVE from #{_moveSnapshot?.instanceId} preserved employee={preservedEmployeeId}]"
                : "";
            Log.Info($"[FurniturePlacer] CONFIRM: instance #{instance.instanceId} '{instance.itemId}' " +
                     $"at {instance.position} rot={instance.rotation}° depot={instance.depotId} " +
                     $"(total placed: {_placedInstances.Count}){warningSuffix}{moveSuffix}");

            // 2026-05-08: clear move state — confirm = move complete (snapshot nie jest już potrzebny)
            _moveSnapshot = null;
            _isMoveOperation = false;

            // MF-8: re-evaluate functional states (placement nowego obiektu może zablokować accessSide
            // istniejącego obok) + emit event dla bridge'a (PartInventoryFurnitureBridge w Timetable).
            RecomputeAllFunctionalStates();
        }
    }
}
