# System Zajezdni 3D (Depot)

Edytor i symulacja zajezdni kolejowej w 3D dla Railway Manager. Pozwala
budować tory, rozjazdy, sieć trakcyjną, ścieżki/parkingi, ogrodzenie i
hale, oraz zarządzać taborem stojącym w zajezdni.

Scena: `Assets/Scenes/Depot.unity`
Namespace: `DepotSystem`
Unity: `6000.4.0f1` (zobacz `ProjectSettings/ProjectVersion.txt`)

---

## Architektura

`DepotManager` jest **koordynatorem** — sam nie zawiera logiki, tylko
trzyma referencje do podsystemów i odpala je w `Start()`. Każdy podsystem
ma jasno wydzieloną odpowiedzialność.

```
DepotManager (koordynator)
├── GroundGenerator              ── teren + siatka pomocnicza
├── DepotFenceSystem             ── ogrodzenie i bramy
├── TrackGraph                   ── graf topologiczny torów (węzły, krawędzie, rozjazdy)
├── PrefabTrackBuilder           ── wizualne tory (szyny + podkłady) wzdłuż polyline
├── TrackBuildStateMachine       ── obsługa wejścia podczas budowania torów
├── ParallelTrackGenerator       ── generowanie torów równoległych
├── TurnoutPlacer                ── stawianie rozjazdów
├── TurnoutPlacementStateMachine ── obsługa wejścia podczas stawiania rozjazdów
├── CatenaryGenerator            ── sieć trakcyjna (3-etapowy pipeline)
├── ElectrificationStateMachine  ── tryb elektryfikacji (malowanie po torach)
├── WallBuildingSystem           ── ściany hal i budynków
├── RoomDetectionSystem          ── detekcja zamkniętych pomieszczeń
├── HallActionStateMachine       ── obsługa wejścia podczas budowy hal
├── PathBuildStateMachine        ── ścieżki i parkingi
├── SnapPointSystem              ── snap-to-grid dla obiektów
├── TrainSpawnSystem             ── spawn pociągów na torach zajezdni
├── VehicleController            ── ruch pojazdów po torach
├── ExitTrackController          ── tory wyjazdowe (połączenie z mapą 2D)
└── DepotOrbitCamera             ── kamera orbitująca z pivotem
```

UI ma własny koordynator: **`DepotUIManager`** (singleton) trzyma aktualny
`ToolMode` i `*SubMode`, emituje eventy do paneli UI.

---

## Tryby narzędzi

`enum ToolMode` (`Depot/UI/DepotUIManager.cs`):

| Tryb | Co robi |
|---|---|
| `Select` | Domyślny — selekcja obiektów, brak budowania |
| `BuildTrack` | Stawianie torów i rozjazdów (sub-tryby: `Track`, `TurnoutR190`, `TurnoutR300`, `DoubleCrossoverR190`, `WashZone`, `Turntable`, `PitLift`) |
| `BuildCatenary` | Malowanie sieci trakcyjnej po istniejących torach |
| `BuildPath` | Ścieżki piesze i parkingi |
| `BuildRoom` | Ściany hal i pomieszczeń |
| `Demolish` | Usuwanie obiektów |

Aktywny tryb pochodzi z `DepotUIManager.Instance.CurrentTool`. Zmiana
trybu emituje `OnToolChanged`. Sub-tryby mają osobne eventy
(`OnTrackSubModeChanged`, etc.).

---

## Sterowanie

Klawisze są **dynamicznie pokazywane w grze** przez `KeyboardLegendUI`
(panel po prawej stronie ekranu, aktualizuje się na zmianę narzędzia).
Pełna lista jest źródłem prawdy w
`Assets/Scripts/Depot/UI/KeyboardLegendUI.cs` — metoda `GetShortcuts()`.

Skróty wspólne:

| Skrót | Akcja |
|---|---|
| `Ctrl+Z` | Cofnij (per-kategoria, patrz Undo) |
| `ESC` | Anuluj aktualne narzędzie |
| `Del` | Usuń najbliższy obiekt narzędzia |

Kamera (`DepotOrbitCamera`):

| Skrót | Akcja |
|---|---|
| `W` / `A` / `S` / `D` | Przesuwanie pivota |
| `Q` / `E` | Yaw (obrót poziomy) |
| `T` / `F` | Pitch (obrót pionowy) |
| `MMB drag` | Pan |
| `RMB drag` | Orbit |
| `Scroll` | Zoom |

---

## Sieć trakcyjna

`CatenaryGenerator` to orkiestrator 3-etapowego pipeline:

1. **`ZoneClassifier`** — klasyfikuje tory na strefy (rozjazdy, łuki,
   równoległe, proste).
2. **`WirePathGenerator`** — generuje logiczne linie przewodów z punktami
   kontrolnymi.
3. **`SupportOptimizer`** — wybiera minimalne podpory (słupy / bramki) i
   `CatenaryVisualBuilder` rysuje meshe.

Reguły fizyczne: spacing słupów wg promienia łuku, sekcjonowanie,
naprężania.

---

## Undo

`DepotSystem.Undo.UndoManager` (statyczny) trzyma **4 niezależne stacki**
po jednym na kategorię:

```
UndoCategory.Tory
UndoCategory.SiecTrakcyjna
UndoCategory.Sciezki
UndoCategory.Pomieszczenia
```

Rozmiar stacka konfigurowalny przez `UndoSettings.MaxUndos`. Re-entrancy
guard `IsUndoing` zapobiega rekurencyjnemu nagrywaniu.

Komendy w `Assets/Scripts/Depot/Undo/UndoCommands.cs` — każda akcja
budowania ma swoją `IUndoCommand` (TrackPlaced, TurnoutPlaced,
CatenaryToggle, PathCellPlaced, BuildingPlaced, WallRemoved, ...).

`Ctrl+Z` jest obsługiwany przez `UndoInputHandler`.

---

## Tabor w zajezdni

- **`TrainSpawnSystem`** — spawnuje pociągi (`TrainInstance`) na torach
  zajezdni przez `SpawnRequest`.
- **`VehicleController`** — ruch jednostek (`VehicleUnit`) po grafie torów.
- **`DepotVehicle`** — komponent na pojeździe (selekcja klikiem,
  wyświetlanie info, animacje).
- **`DepotVehicleManager`** — selekcja, kursor, target marker,
  zarządzanie listą pojazdów.

Panel UI: `Depot/UI/FleetPanelUI.cs` (zakładka Tabor w głównym pasku).

---

## Integracja z mapą 2D

`DepotRailwayIntegration` opcjonalnie łączy graf zajezdni z `RailwayGraph`
mapy 2D. Domyślnie wyłączone (`autoConnectToNetwork = false`). Główne API:

- `GetDepotTracksInfo()` — zwraca `List<DepotTrackInfo>` torów zajezdni
- `GetIntegrationStatus()` — `IntegrationStatus`
- `DisconnectFromRailway()` — rozłącz

Tory wyjazdowe (przejście pociągu z 3D do mapy 2D) obsługuje
`ExitTrackController`.

---

## Setup sceny

Scena `Depot.unity` jest już **skonfigurowana automatycznie** — wystarczy ją
otworzyć i kliknąć Play. `DepotManager` inicjalizuje wszystkie podsystemy
w `Start()`, nie wymaga ręcznej konfiguracji w Inspectorze.

Ustawienia w `DepotManager` (przez Inspector):
- `generateDefaultLayout = true` — zbuduje domyślny układ torów wjazdowych przy Start
- `spawnTestTrains = true` — doda testowe pociągi (debug)

Modele i prefaby:
- Tory: `railPrefab`, `sleeperPrefab` w `PrefabTrackBuilder`. FBX-y w `Assets/Models/Depot/`.
- Pociągi: prefaby w `Assets/Prefabs/Depot/Vehicles/`.

---

## Powiązane dokumenty

- [`../design/depot-3d.md`](../design/depot-3d.md) — pełna specyfikacja Depot 3D (skonsolidowana)
- [`../setup/git-smartmerge.md`](../setup/git-smartmerge.md) — Unity Smart Merge dla git
