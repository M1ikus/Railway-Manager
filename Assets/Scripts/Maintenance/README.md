# Maintenance — przewodnik

> Dokumentacja modułu `Assets/Scripts/Maintenance/`.

## Asmdef + warstwa

**`RailwayManager.Maintenance`** (rootNamespace pusty — pliki używają `RailwayManager.Maintenance` lub
sub-namespace `RailwayManager.Maintenance.Movement`, `.Furniture`). Refs: `Core, Depot, Fleet, Map,
SharedUI, Timetable` (+ TMP + InputSystem).

**Powstał ze splitu Timetable asmdef 2026-05-15** — wcześniej cały M7 (~4000 LOC) mieszkał w
`Assets/Scripts/Timetable/Runtime/Maintenance/` z namespace `RailwayManager.Maintenance` ale BEZ
własnego asmdef. Skutek: WorkshopManager/RescueService/PartInventory** logicznie należące do M7
były związane lifecycle Timetable assembly. Split rozwiązał dług architektoniczny #1 z auditu
modułu Timetable 2026-05-15.

**Warstwa:** **wyżej** niż Timetable, **niżej** niż Personnel. Maintenance referuje Timetable
(dla `TrainRun`/`SimulatedTrain`/`TrainRunSimulator` events) — Timetable NIE referuje Maintenance
(cykl zerwany przez event-pattern, patrz "Bootstrap" niżej).

## Sub-systemy

| Sub-system | Pliki | Po co |
|---|---|---|
| **Bootstrap** | `MaintenanceBootstrapper.cs` | `[RuntimeInitializeOnLoadMethod]` subskrypcja na `TrainRunSimulator.OnSimulatorBootstrapped` → bootstrap 7 singletonów (RescueDispatchUI/RescueService/WorkshopManager/WorkshopsPanelUI/PartInventoryService/PartsPanelUI/MaintenanceAlertsUI). Wcześniej te calls były w `TrainRunSimulator.Awake` (linie 238-257). Idempotent przez `EnsureExists` guards. |
| **Warsztaty (M7-4 / MM-8)** | `WorkshopManager.cs` (1116 LOC, slot per ServicePit po MM-8 refactor), `WorkshopsPanelUI.{BuildUI,External,Refresh,cs}` (4 partials), `WorkshopHallLevelSmokeTest.cs`, `ServicePitFurnitureBridge.cs` | Sloty warsztatowe (`WorkshopSlot` per `ServicePit` PlacedFurnitureItem), wykonawca przeglądów (Idle/EnRoute/Inspecting/Completed states), external ZNTK jobs (3 fazy: DeliveringOut/InInspection/DeliveringBack). Hook Personnel: `SlotSpeedMultiplierHook` (skill mechanika × duration multiplier 0.6-1.66). |
| **Rescue (M7-3)** | `RescueService.cs` (M7-3c pathfinding + ETA execution), `RescueDispatchUI.cs` (UI pokazywane gdy pociąg w AwaitingRescue, auto-dispatch po 15 min game time) | Pathfinding rescue loko z najbliższego depotu z wolną loko, holowanie broken consist z powrotem, vehicles → InRepair. |
| **Magazyn części (M7-6)** | `PartInventoryService.cs`, `PartsPanelUI.cs`, `PartInventoryFurnitureBridge.cs` | 12 typów części z OnDayEnded delivery + capacity bridge na meble `PartStorageShelf`. |
| **Alerts (M7-7)** | `MaintenanceAlertsUI.cs` | Floating badges (overdue inspections / awarie / brak części). |
| **Bridges (asymetria asmdef)** | `PartInventoryFurnitureBridge.cs` (namespace `RailwayManager.Maintenance.Furniture`), `ServicePitFurnitureBridge.cs` (namespace `RailwayManager.Maintenance.Furniture`), `OutdoorEquipmentMovementBridge.cs` (namespace `RailwayManager.Maintenance.Movement`) | Bridges są tu **bo Depot nie może referować Timetable/Maintenance** (hierarchia asmdef). Subskrybują eventy Depot (`FurniturePlacer.OnPlacementStateChanged`). |

## Bootstrap — wzorzec inwersji event

Cykl został zerwany przez event-pattern (split 2026-05-15):

1. **Timetable** definiuje `TrainRunSimulator.OnSimulatorBootstrapped` (static `event System.Action`)
   emit'owany na końcu `Awake()` po pełnym bootstrap'ie wewnętrznych singletonów (handshake services,
   Economy services, popup UI, MapClickHandler).
2. **Maintenance** subskrybuje przez `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` w
   `MaintenanceBootstrapper.RegisterEarlyHook` — fire'uje gdy Unity loaduje domain (wcześnie, przed
   jakimkolwiek Awake MonoBehaviour). Handler `BootstrapMaintenanceServices` wywołuje 7× `EnsureExists`.
3. Defensive fallback: jeśli `TrainRunSimulator.Instance != null` w momencie subskrypcji (domain reload
   PO Awake), `BootstrapMaintenanceServices` woła się natychmiast. Idempotent.

**Nie dodawaj nowych Maintenance services przez `TrainRunSimulator.Awake` bezpośrednio** —
dorzuć call do `MaintenanceBootstrapper.BootstrapMaintenanceServices`.

## Cross-system glue (hooks installed przez inne asmdef)

- **`WorkshopManager.SlotSpeedMultiplierHook`** ← `RailwayManager.Personnel.WorkshopAssignmentService`
  (skill mechanika → speed multiplier 0.6-1.66).
- **`BreakdownService.SelfRepairBonusHook`** ← `RailwayManager.Personnel.WorkshopAssignmentService`
  (skill mechanika → bonus self-repair).
- **`FurniturePlacer.OnPlacementStateChanged`** (Depot event) → bridges
  (`PartInventoryFurnitureBridge`, `ServicePitFurnitureBridge`) recompute capacity/slots.
- **`GameState.OnDayEnded`** → `PartInventoryService` (parts delivery).
- **`TrainRunSimulator.OnTrainArrivingAtStop/Departing/Despawned`** → konsumowane wewnętrznie
  przez `MaintenanceAlertsUI` (overdue inspections).

## Gotchas

- **Maintenance referuje Timetable**, nie odwrotnie. Nie dodawaj `using RailwayManager.Maintenance`
  w Timetable assembly — kompilator to złapie.
- **`WorkshopManager` slot per ServicePit, nie per Hall** (MM-8 refactor 2026-05-05). Source of truth:
  `FurniturePlacer.PlacedInstances` z `ObjectFunction.ServicePit`. Walidacja `vehicle.lengthM <=
  slot.maxVehicleLength`.
- **`RescueDispatchUI` namespace** to `RailwayManager.Maintenance` (przeniesione z `RailwayManager.
  Timetable.Simulation` przy split'cie). Jeśli widzisz starsze referencje — refresh.
- **MP-9 deterministic RNG** — `BreakdownService.Roll` (~55k random rolls/s) używa `RandomRegistry.
  GetRng("BreakdownService")`, nie `UnityEngine.Random`. Wszystkie nowe `Roll`-style methods muszą
  iść przez `RandomRegistry`.
- **Save/Load**: `MaintenanceSavable` + `MaintenanceJobsSavable` (w SaveLoad asmdef) reflection-discovers
  `ISavable` w Maintenance. SaveLoad już referuje Maintenance asmdef (split 2026-05-15).

## Powiązane docs

- `docs/design/m7-maintenance.md` — pełny spec M7
- `docs/design/balance-constants.md` — `MaintenanceConstants`
- `docs/TECH_DEBT.md` — TD-007 (punctuality KPI), TD-012 (migration chain not battle-tested)
