# Fleet — przewodnik

> Dokumentacja modułu `Assets/Scripts/Fleet/`.

## Asmdef + warstwa

**`RailwayManager.Fleet`** (rootNamespace: `RailwayManager.Fleet`), refs: **`Core` only**.

Druga warstwa (obok SharedUI). Data-heavy, **brak MonoBehaviour managerów** (poza
kilkoma services jako static class). Czysta logika domeny taboru — żadnych UI,
żadnej fizyki, żadnego scene state'u.

**Konsumenci:** Depot (`FleetPanelUI`), Map, Timetable, Personnel, SaveLoad.

## Co tu siedzi

| Folder | Pliki | Po co |
|---|---|---|
| **Data/** | `FleetVehicleData`, `FleetConsistData`, `FleetMarketVehicle`, `FleetVariantSpec`, `FleetFamily`, `VehicleConfiguration`, `VehicleComponents` (8 komponentów M7), `SeatLayoutModels`/`SeatCount`/`SeatZoneSlot`, `WagonBodyDef`/`WagonBogieDef`/`DoorConfig`/`PantographConfig`, `PaintDefinition`/`PaintPresetDef`/`DecalDef`, `InspectionIntervals`/`InspectionSchedule`/`MaintenanceRecord`, `ComponentRiskFactors`, `CartItem`, `ExternalWorkshop`/`WorkshopLevel`, `PaintingJob`, `ModernizationPath`/`VehicleModification`, `VehiclePosition`, `NewVehicleModel`, `FleetEnums`, `ValidationResult` | DTO/POCO — czyste struktury danych. Wszystko `[Serializable]` (Unity Inspector + Save/Load). |
| **Catalogs/** | `FleetCatalog` (15-25 pojazdów EA ładowane z `fleet_catalog_ea.json`), `PartCatalog` (12 typów części M7), `ExternalWorkshopCatalog` (5 ZNTK), `PaintPresetsCatalog`, `DecalCatalog`, `ZoneDescriptorCatalog`, `InspectionCatalog`, `ModernizationPathCatalog`, `VehicleModificationCatalog`, `FleetBalanceConstants`, `FleetConstants` | Static loaders z JSON w `StreamingAssets/Fleet/`. **Magic numbers zakazane** w runtime — wszystko w `FleetBalanceConstants`. |
| **Runtime/** | `FleetService` (singleton static — OwnedVehicles, Consists, Market, Cart), `DegradationService` (per-km, 12 components, risk factors), `BreakdownService` (awarie + self-repair), `MarketGenerator`/`FleetMarketRefreshService` (rynek wtórny), `CartProcessor`, `MaintenanceCostCalculator`, `EvnGenerator` (numery taborowe EVN), `ComfortCalculator`, `InteriorMixCalculator`, `MarketLiveryGenerator`, `PaintSerializer`, `PaintingJobService`, `SelfPaintingService`, `OutdoorEquipmentJobService`, `ModernizationJobService`, `VehicleModificationJobService` | Serwisy aplikacyjne. **`FleetService` to authoritative state taboru** — singleton w stylu `GameState`. |

## Cross-system glue

- **`FleetService.OwnedVehicles/Consists/Market/Cart`** — read-only views (BUG-035), mutacja przez `AddOwnedVehicle`/`RemoveOwnedVehicle`/`LoadSnapshot` z auto-Notify.
- **`FleetService.OnOwnedChanged/OnConsistsChanged/OnMarketChanged/OnCartChanged`** events → UI (FleetPanelUI partials w Depot), Maintenance, Timetable composition lookups.
- **`BreakdownService.Roll()`** — używa `RandomRegistry.GetRng("BreakdownService")` (MP-9 determinism). Największe źródło RNG w grze (~55k rolls/s przy stress).
- **`DegradationService`** — subskrybuje `GameState.OnDayEnded` i `TrainRunSimulator` per-km hook.
- **`MaintenanceCostCalculator`** ← konsumowane przez `EconomyManager` (Timetable).
- **Save/Load:** `FleetSavable` (w SaveLoad) reflection-discovers state. `PaintSerializer` ma własny serializer dla shareable paint defs.

## Gotchas

- **Brak MonoBehaviour managers** (poza job services). `FleetService` jest **static class** — `DontDestroyOnLoad` niepotrzebne, ale wymaga **`ResetAll`** przy „Nowa gra" (analogicznie `VehicleLocationService.ResetAll`).
- **`OwnedVehicles` to `IReadOnlyList`** — nigdy nie castuj na `List<>` i nie modyfikuj bezpośrednio. Inaczej `OnOwnedChanged` nie strzeli → UI desync.
- **`FleetVehicleData.componentRisk`** per seria (data layer M-Fleet) wpływa na `BreakdownService` failure roll — modyfikacja JSON-a wymaga regen save'a (degradation persists per pojazd).
- **`FleetCatalog.Load` ładuje JSON z `StreamingAssets/Fleet/fleet_catalog_ea.json`** — w Editor path inny niż build. Test path-aware.
- **EVN (Europejski Numer Vehicle)** generowany przez `EvnGenerator` z control digit. Format `XX-XXXXX-X` dla wagonów osobowych, inne dla lokomotyw/EZT. Nie ręczne generowanie.
- **Paint editor (M-FleetConfigurator)**: multi-zone + paski + decals. `PaintDefinition` serializowany przez `PaintSerializer` do shareable JSON (Workshop tarea pre-EA stub).
- **Modernization vs Modification:** `ModernizationPath` = External ZNTK + Internal Hall lvl5 (np. EN57→Ryba). `VehicleModification` = wymiana wózków/wyposażenia/funkcji wagonu. To **dwa różne katalogi** (M-Modernization MM-10 vs MM-11).
- **`InspectionIntervals.json`** = stale (D2/P3/P4/P5) per seria — czytaj z catalog, nie hardkoduj.

## Powiązane docs

- `docs/design/m6-economy.md` — jak Fleet linkuje się z ekonomią
- `docs/design/m7-maintenance.md` — Degradation/Breakdown/Workshop integration
