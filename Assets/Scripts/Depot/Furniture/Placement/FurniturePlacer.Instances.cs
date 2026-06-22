using System.Collections.Generic;
using DepotSystem;
using DepotSystem.Furniture.Functional;
using UnityEngine;
using RailwayManager.Core;

namespace DepotSystem.Furniture.Placement
{
    /// <summary>
    /// Partial: zarządzanie kolekcją <c>_placedInstances</c> po confirm — restore z save,
    /// dorzucenie door instance (MF-11), clear all, delete, rotate, move, recompute
    /// functional states, lookup po instanceId.
    /// </summary>
    public partial class FurniturePlacer
    {
        /// <summary>
        /// MF-9 — restore placedInstances z save'u. Czyści aktualny stan, dodaje wszystkie
        /// instancje, spawnuje stamped visualizations i recompute functional states.
        ///
        /// Wywoływane przez <c>DepotSavable.Deserialize</c>. <paramref name="nextInstanceId"/>
        /// to counter z save'u (żeby nowe instance po load miały kolejne ID, nie zderzały się).
        /// </summary>
        public void RestoreFromSave(List<PlacedFurnitureItem> instances, int nextInstanceId)
        {
            // Cleanup current state (analogicznie do ClearAllPlaced ale bez emit event'u)
            _placedInstances.Clear();
            foreach (var kvp in _placedVisuals)
                if (kvp.Value != null) Destroy(kvp.Value);
            _placedVisuals.Clear();
            _functionalStates.Clear();
            _warningOverlays.Clear();

            if (instances != null)
            {
                foreach (var instance in instances)
                {
                    if (instance == null) continue;
                    var item = FurnitureCatalog.FindById(instance.itemId);
                    if (item == null)
                    {
                        Log.Warn($"[FurniturePlacer] RestoreFromSave: itemId '{instance.itemId}' not found in catalog (skipped)");
                        continue;
                    }
                    _placedInstances.Add(instance);
                    SpawnStampedVisual(item, instance);
                }
            }

            _placedInstancesCount = _placedInstances.Count;
            _nextInstanceId = nextInstanceId > 0 ? nextInstanceId : Mathf.Max(1, _placedInstances.Count + 1);

            Log.Info($"[FurniturePlacer] RestoreFromSave: restored {_placedInstances.Count} instances, nextInstanceId={_nextInstanceId}");

            // Recompute states + emit (bridge w Timetable zaktualizuje capacity)
            RecomputeAllFunctionalStates();
        }

        /// <summary>MF-9: counter dla DepotSavable serializacji.</summary>
        public int NextInstanceId => _nextInstanceId;

        /// <summary>
        /// MF-11: dorzuca door instance bez przechodzenia przez standardowy placement flow.
        /// Wywoływane przez DoorPlacer.ConfirmPlacement po pomyślnym TryAddDoorOpening.
        /// Spawn stamped visual + DoorAnimator + RecomputeAllFunctionalStates.
        /// Zwraca nowy instanceId.
        /// </summary>
        public int AddDoorInstance(FurnitureItem item, int depotId, Vector3 position, int rotation)
        {
            var instance = new PlacedFurnitureItem
            {
                instanceId = _nextInstanceId++,
                itemId = item.id,
                depotId = depotId,
                position = position,
                rotation = rotation,
                assignedEmployeeId = -1
            };
            _placedInstances.Add(instance);
            _placedInstancesCount = _placedInstances.Count;
            SpawnStampedVisual(item, instance);
            RecomputeAllFunctionalStates();
            return instance.instanceId;
        }

        /// <summary>
        /// Czyści wszystkie placed instances + ich visualizations (smoke test cleanup).
        /// MF-9 zastąpi tę metodę przez DepotSavable load/clear.
        /// </summary>
        public void ClearAllPlaced()
        {
            int count = _placedInstances.Count;
            _placedInstances.Clear();
            _placedInstancesCount = 0;
            _nextInstanceId = 1;
            foreach (var kvp in _placedVisuals)
                if (kvp.Value != null) Destroy(kvp.Value);
            _placedVisuals.Clear();
            // MF-8: cleanup functional + overlays
            _functionalStates.Clear();
            _warningOverlays.Clear();
            Log.Info($"[FurniturePlacer] ClearAllPlaced: usunięto {count} instances + ich visualizations");
            OnPlacementStateChanged?.Invoke();
        }

        /// <summary>MF-7: lookup po instanceId — używane przez FurnitureSelector / context menu.</summary>
        public GameObject GetVisualFor(int instanceId)
        {
            return _placedVisuals.TryGetValue(instanceId, out var go) ? go : null;
        }

        /// <summary>MF-8: functional state per instance (Active/Blocked + reason).</summary>
        public FurnitureFunctionalState GetFunctionalState(int instanceId)
        {
            return _functionalStates.TryGetValue(instanceId, out var s) ? s : FurnitureFunctionalState.Active();
        }

        /// <summary>
        /// MF-8: re-evaluation funkcjonalnego stanu wszystkich postawionych instancji.
        /// Wywoływane po każdej zmianie listy placed (confirm/move/delete/rotate).
        ///
        /// Per instance: Validator.Validate (excluding self z placedInstances) → Ok = Active,
        /// Warning/Error = Blocked z reason. Update warning overlay component (zielony ✓ /
        /// czerwony ✗). Po wszystkich emit OnPlacementStateChanged dla bridge'a.
        /// </summary>
        public void RecomputeAllFunctionalStates()
        {
            if (_roomSystem == null) _roomSystem = DepotServices.Get<RoomDetectionSystem>();
            var rooms = _roomSystem != null ? _roomSystem.Rooms : null;

            _functionalStates.Clear();

            // Build temp list bez self dla każdego validation call
            for (int i = 0; i < _placedInstances.Count; i++)
            {
                var instance = _placedInstances[i];
                if (instance == null) continue;
                var item = FurnitureCatalog.FindById(instance.itemId);
                if (item == null) continue;

                // Temp list bez self
                _placedInstances.Remove(instance);
                var validation = FurnitureValidator.Validate(
                    item,
                    instance.depotId,
                    instance.position,
                    instance.rotation,
                    rooms,
                    _placedInstances);
                _placedInstances.Insert(i, instance);

                FurnitureFunctionalState state = validation.Level == ValidationLevel.Ok
                    ? FurnitureFunctionalState.Active()
                    : FurnitureFunctionalState.Blocked(validation.Reason);

                _functionalStates[instance.instanceId] = state;

                if (_warningOverlays.TryGetValue(instance.instanceId, out var overlay) && overlay != null)
                {
                    overlay.SetState(state);
                }
            }

            OnPlacementStateChanged?.Invoke();
        }

        /// <summary>MF-7: lookup PlacedFurnitureItem po instanceId.</summary>
        public PlacedFurnitureItem GetInstance(int instanceId)
        {
            for (int i = 0; i < _placedInstances.Count; i++)
                if (_placedInstances[i].instanceId == instanceId) return _placedInstances[i];
            return null;
        }

        /// <summary>
        /// MF-7: usuwa instancję + jej visualization. Zwraca true gdy się udało.
        /// Sprawdza ownership (M10 readiness) — depot musi należeć do lokalnego gracza.
        /// </summary>
        public bool DeleteInstance(int instanceId, bool refund = true)
        {
            var instance = GetInstance(instanceId);
            if (instance == null)
            {
                Log.Warn($"[FurniturePlacer] DeleteInstance: instance #{instanceId} nie istnieje");
                return false;
            }

            if (!OwnershipService.IsOwnedByLocalPlayer(instance.depotId))
            {
                Log.Warn($"[FurniturePlacer] DeleteInstance: depot {instance.depotId} nie nalezy do lokalnego gracza");
                return false;
            }

            _placedInstances.Remove(instance);
            _placedInstancesCount = _placedInstances.Count;

            if (_placedVisuals.TryGetValue(instanceId, out var go))
            {
                if (go != null) Destroy(go);
                _placedVisuals.Remove(instanceId);
            }
            // MF-8: cleanup functional state + warning overlay
            _functionalStates.Remove(instanceId);
            _warningOverlays.Remove(instanceId);

            Log.Info($"[FurniturePlacer] DeleteInstance: usunieto #{instanceId} '{instance.itemId}' z depot={instance.depotId} (pozostalo: {_placedInstances.Count})");

            // M-Economy Faza 5: zwrot kosztu mebla. refund:false dla pickupu move'a (zaraz re-charge
            // przy ConfirmPlacement) — inaczej move+cancel = darmowa kasa.
            if (refund)
                ConstructionBilling.Refund(ConstructionCosts.FurnitureGroszy(), "furniture_refund", instance.itemId);

            // MF-8: re-evaluate (delete obiektu może odblokować dojście innym obok)
            RecomputeAllFunctionalStates();
            return true;
        }

        /// <summary>
        /// MF-7: rotuje instancję +90° (cykl 0/90/180/270). Walidacja — jeśli Error
        /// (kolizja po rotacji), rollback do poprzedniej rotacji. Warning jest akceptowany
        /// (decyzja B14 — gracz może mieć obiekt bez dojścia).
        /// </summary>
        public bool RotateInstance(int instanceId)
        {
            var instance = GetInstance(instanceId);
            if (instance == null) return false;
            if (!OwnershipService.IsOwnedByLocalPlayer(instance.depotId)) return false;

            int oldRotation = instance.rotation;
            int newRotation = (oldRotation + 90) % 360;
            instance.rotation = newRotation;

            // Walidacja z aktualnym placedInstances (excluding self żeby nie kolidował sam ze sobą).
            // Tymczasowo wyciągamy self, walidujemy, wracamy.
            _placedInstances.Remove(instance);
            var rooms = _roomSystem != null ? _roomSystem.Rooms : null;
            var validation = FurnitureValidator.Validate(
                FurnitureCatalog.FindById(instance.itemId),
                instance.depotId,
                instance.position,
                newRotation,
                rooms,
                _placedInstances);
            _placedInstances.Add(instance);

            if (validation.Level == ValidationLevel.Error)
            {
                instance.rotation = oldRotation;
                Log.Warn($"[FurniturePlacer] RotateInstance #{instanceId}: rollback ({oldRotation}° → {newRotation}° walidacja Error: {validation.Reason})");
                return false;
            }

            // Update visual rotation
            if (_placedVisuals.TryGetValue(instanceId, out var go) && go != null)
            {
                var renderer = go.GetComponent<FurniturePreviewRenderer>();
                if (renderer != null) renderer.SetRotation(newRotation);
            }

            string warningSuffix = validation.Level == ValidationLevel.Warning ? $" [WARNING: {validation.Reason}]" : "";
            Log.Info($"[FurniturePlacer] RotateInstance #{instanceId}: {oldRotation}° → {newRotation}°{warningSuffix}");

            // MF-8: re-evaluate (rotate zmienia accessSide direction — może odblokować/zablokować dojście)
            RecomputeAllFunctionalStates();
            return true;
        }

        /// <summary>
        /// MF-7: rozpoczyna placement przeniesienia istniejącej instancji.
        /// Usuwa visual instance'a, aktywuje placement mode z item z catalog'u.
        /// Po confirm istniejąca instancja jest replaced (delete + create).
        /// Esc → cancel = zachowaj instancję na poprzedniej pozycji.
        /// </summary>
        public bool MoveInstance(int instanceId)
        {
            var instance = GetInstance(instanceId);
            if (instance == null) return false;
            if (!OwnershipService.IsOwnedByLocalPlayer(instance.depotId)) return false;

            var item = FurnitureCatalog.FindById(instance.itemId);
            if (item == null)
            {
                Log.Error($"[FurniturePlacer] MoveInstance: item '{instance.itemId}' nie znaleziony w katalogu");
                return false;
            }

            // 2026-05-08: snapshot ze starej pozycji ZANIM usuniemy instancję — żeby
            // CancelPlacement (Esc) mogło przywrócić ją na oryginalnym miejscu.
            // Zachowujemy też assignedEmployeeId żeby personel nie tracił reference.
            _moveSnapshot = new PlacedFurnitureItem
            {
                instanceId = instance.instanceId,
                itemId = instance.itemId,
                depotId = instance.depotId,
                position = instance.position,
                rotation = instance.rotation,
                assignedEmployeeId = instance.assignedEmployeeId,
            };
            _isMoveOperation = true;

            int oldRotation = instance.rotation;
            DeleteInstance(instanceId, refund: false); // M-Economy: pickup move'a — koszt wraca przy ConfirmPlacement

            StartPlacement(item, instance.depotId);
            _currentRotationDeg = oldRotation;  // zachowaj rotację z istniejącej instancji
            Log.Info($"[FurniturePlacer] MoveInstance: aktywowano move dla #{instanceId} '{item.id}' " +
                     $"(snapshot @ {_moveSnapshot.position}, rot={_moveSnapshot.rotation}°)");
            return true;
        }
    }
}
