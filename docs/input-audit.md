# Input Audit — historyczny dokument migracji

**Data audytu:** 2026-04-11
**Status (2026-04-14):** ✅ **Migracja zakończona.** Wszystkie 113 wywołań starego
Input Managera zostały zmigrowane do Unity Input System. Zostały tylko 2 komentarze
(nie wywołania) w `DepotOrbitCamera.cs` i `CameraController.cs`.

**Cel pierwotny:** Pełna dokumentacja każdego klawisza/przycisku/osi używanego w projekcie,
zorganizowana jako baza pod migrację na nowy Unity Input System.
**Metodyka:** grep `Input.GetKey*/GetMouseButton*/GetAxis/mousePosition/mouseScrollDelta`
we wszystkich plikach `Assets/Scripts/**.cs`.

**Jak używać dziś:** Dokument pozostaje cennym źródłem informacji o KTÓRY klawisz robi CO
w projekcie. Do modyfikacji bindingów patrz `Assets/Settings/InputActions.inputactions`
(7 action maps). Do wzorca użycia w kodzie: `docs/conventions.md` sekcja "Input System".

---

## Statystyki

- **113 wywołań** starego Input Managera w **18 plikach**.
- **Najgęstsze pliki** (najwięcej wywołań):
  - `DepotOrbitCamera.cs` — 22
  - `Map/Rendering/CameraController.cs` — 17
  - `WallBuildingSystem.cs` — 15
  - `PathBuildStateMachine.cs` — 12
  - `TurnoutPlacementStateMachine.cs` — 11

---

## 1. Kamery

### 1.1 `DepotOrbitCamera.cs` (scena Depot)

Orbitująca kamera z pivotem. Tryb **zawsze aktywny** w scenie Depot.

| Akcja | Bindings | Linia | Komentarz |
|---|---|---|---|
| Pan (ruch pivota) | `MMB drag` (mouse delta × panSpeed) | 97, 100, 102 | `GetMouseButtonDown(2)`, `GetMouseButton(2)` |
| Orbit | `RMB drag` (mouse delta × orbitSpeed) | 114, 117, 119 | `GetMouseButtonDown(1)`, `GetMouseButton(1)` |
| Pan klawiatura | `W/A/S/D` | 135–138 | Directly `GetKey`, normalized do Vector2 |
| Yaw lewo/prawo | `Q/E` | 154–155 | Obrót poziomy |
| Pitch góra/dół | `T/F` | 156–157 | Obrót pionowy |
| Zoom (scroll) | `Mouse ScrollWheel` (GetAxis) | 171 | `Input.GetAxis("Mouse ScrollWheel")` — **wyłączone gdy Ctrl wciśnięty** |
| Double-click focus | `LMB double` | 180 | `GetMouseButtonDown(0)` + timing |
| Mouse position (raycast) | — | 187 | `Input.mousePosition` do `ScreenPointToRay` |

**Warunek wyłączenia zoomu:** `Input.GetKey(LeftControl) || Input.GetKey(RightControl)` —
scroll z Ctrl jest zarezerwowany dla innych tool'ów (obracanie rozjazdu).

### 1.2 `Map/Rendering/CameraController.cs` (scena Map)

Ortograficzna 2D kamera. Tryb **zawsze aktywny** w scenie Map.

| Akcja | Bindings | Linia | Komentarz |
|---|---|---|---|
| Pan | `W/A/S/D` lub **arrow keys** | 118–124 | Komentarz: "Read directly — GetAxis mixes in gamepad sticks causing drift" |
| Speed boost | `LeftShift / RightShift` | 133 | Szybkie przesuwanie |
| Slow down | `LeftControl / RightControl` | 135 | Wolne przesuwanie (precyzyjne) |
| Zoom | `Mouse ScrollWheel` | 191 | `GetAxis("Mouse ScrollWheel")` |
| Pan (mouse drag) | `MMB drag` | 218, 225, 233, 265 | `GetMouseButtonDown/Up(2)` |
| Extra Q/E | `Q/E` | 276, 279 | Dodatkowy ruch (prawdopodobnie nieużywany, sprawdzić) |
| Horizontal/Vertical axes | `GetAxis("Horizontal")/("Vertical")` | 328–329 | **Duplikat** do linii 118–124, prawdopodobnie martwy kod |

**Uwaga:** `CameraController.cs` ma dwa systemy ruchu — `GetKey(W/A/S/D)` i `GetAxis("Horizontal/Vertical")`.
Komentarz w kodzie mówi że `GetAxis` drifuje przez gamepad — ale kod go nadal używa.
**Do weryfikacji przy migracji — czy linia 328–329 jest martwa, usunąć.**

---

## 2. State Machines (tryby budowania)

Wszystkie state machines są **jednocześnie w scenie** ale **aktywne tylko jeden na raz**
przez `DepotUIManager.CurrentTool`. Obecnie każdy z nich sprawdza sam w `Update()` czy
jest właściwy tryb — po migracji zastąpi to `InputActionMap.Enable/Disable`.

### 2.1 `TrackBuildStateMachine.cs` (tryb BuildTrack, sub-mode Track)

Stawianie zwykłych torów (klik start → klik koniec).

| Akcja | Bindings | Linia |
|---|---|---|
| Anuluj / wyjdź | `ESC` | 179, 228 |
| Usuń najbliższy tor | `Del` | 186 |
| Klik start / koniec | `LMB` | 194, 235 |
| Anuluj tryb aktywny | `RMB` | 228 | (`GetMouseButtonDown(1)` wspólnie z ESC) |
| Raycast hover | `mousePosition` | 873 |

### 2.2 `TurnoutPlacementStateMachine.cs` (sub-mode TurnoutR190/R300/DoubleCrossover)

Stawianie rozjazdów z obsługą trybu pary i odgałęzień.

| Akcja | Bindings | Linia |
|---|---|---|
| Obróć rozjazd | `Ctrl + Scroll` | 116, 118 |
| Obróć (keyboard) | `R` | 124 |
| Tryb pary | `P` (toggle) | 151 |
| Strona pary | `O` (auto/L/P cykl) | 160 |
| Odgałęzienie z powrotem | `U` | 168 |
| Anuluj | `ESC` | 188 |
| Postaw | `LMB` | 256 |
| Raycast hover | `mousePosition` | 1175, 1203, 1237 |

**Konflikt:** `Ctrl + Scroll` — `DepotOrbitCamera` wyłącza swój zoom gdy Ctrl jest wciśnięty,
żeby oddać scroll do TurnoutPlacementStateMachine. To **implicit coordination** przez
sprawdzanie Ctrl w dwóch miejscach — po migracji byłoby czystsze: osobny action
`Turnout/Rotate` z bindingiem `Ctrl+Scroll`, kamera nie musi nic wiedzieć.

### 2.3 `ElectrificationStateMachine.cs` (tryb BuildCatenary)

Malowanie sieci trakcyjnej po torach.

| Akcja | Bindings | Linia |
|---|---|---|
| Maluj (klik + drag) | `LMB` hold | 111, 143 | `GetMouseButtonDown/Up(0)` |
| Anuluj | `ESC` lub `RMB` | 91, 129 |
| Usuń sieć z toru | `Del` | 98 |
| Raycast | `mousePosition` | 433 |

### 2.4 `PathBuildStateMachine.cs` (tryb BuildPath)

Ścieżki i parkingi — dwa sub-mode'y (Path paint, Parking corner-to-corner).

| Akcja | Bindings | Linia | Tryb |
|---|---|---|---|
| Anuluj (wspólne) | `ESC` lub `RMB` | 114, 403, 642 | Wszystkie |
| Malowanie (drag) | `LMB` hold | 135, 150 | Path |
| Klik start/end | `LMB` | 160, 419, 431 | Parking |
| Anuluj | `RMB` | 437 | Parking |
| Raycast | `mousePosition` | 657, 933 |

### 2.5 `WallBuildingSystem.cs` (tryb BuildRoom + Demolish dla ścian)

Stawianie ścian hal, selekcja, usuwanie.

| Akcja | Bindings | Linia | Kontekst |
|---|---|---|---|
| Anuluj build | `ESC` | 199, 628, 787, 794 | Wiele trybów |
| Klik narożnik | `LMB` | 210, 220, 651 | Build |
| Anuluj ostatni | `RMB` | 229 | Build |
| Selekcja ściany | `LMB` | 734, 801 | Select |
| Usuń zaznaczoną | `Del` | 751 | Select |
| Raycast | `mousePosition` | 636, 736, 803, 1270 |

**Uwaga:** `WallBuildingSystem` ma 5x `Input.GetKeyDown(KeyCode.Escape)` — każdy w osobnym
sub-trybie. Klasyczny objaw polling approach który InputAction.performed rozwiązuje.

### 2.6 `HallActionStateMachine.cs` (sub-mode budowy hal — dach/drzwi/etc)

| Akcja | Bindings | Linia |
|---|---|---|
| Anuluj | `ESC` (×4 w różnych stanach) | 46, 57, 68, 79 |

---

## 3. Undo / Redo

### 3.1 `Undo/UndoInputHandler.cs`

| Akcja | Bindings | Linia |
|---|---|---|
| Cofnij | `Ctrl+Z` | 25–26 |

**Tylko Ctrl+Z, brak Redo (Ctrl+Y/Ctrl+Shift+Z).** Potencjalny feature gap.

---

## 4. Pojazdy i selekcja

### 4.1 `DepotVehicleManager.cs`

| Akcja | Bindings | Linia |
|---|---|---|
| Odznacz pojazd | `ESC` | 41 |
| Klik / wyślij pojazd | `LMB` | 78, 80 |
| Stop pojazdu | `Spacja` | 222 |

### 4.2 `UI/TrainPopupUI.cs`, `UI/BuildingPopupUI.cs`, `UI/TrackPopupUI.cs`

Popupy szczegółów. Każdy ma podobny pattern:

| Akcja | Bindings | Pliki |
|---|---|---|
| Klik obiektu → otwórz popup | `LMB` | TrainPopup:52, BuildingPopup:46, TrackPopup:76 |
| Zamknij popup | `ESC` | TrainPopup:55, BuildingPopup:49, TrackPopup:79 |
| Raycast | `mousePosition` | wszystkie |

---

## 5. UI — Menu i dialogi

### 5.1 `PauseMenuUI.cs`

| Akcja | Bindings | Linia |
|---|---|---|
| Otwórz / zamknij pauzę | `ESC` | 64 |

**Uwaga — złożona logika:** ESC w PauseMenuUI ma kaskadę warunków (z analizowanej
wcześniej sekcji 70–94):
1. Jeśli otwarty confirm dialog → zamknij confirm
2. Jeśli otwarte pause menu → zamknij pause
3. Jeśli aktywny build tool w Depot → reset tool do Select
4. Jeśli otwarty fleet panel → zamknij fleet panel / popup
5. W przeciwnym razie → otwórz pause menu

**To jest wzorcowy przykład "co ESC robi zależy od kontekstu"** — w nowym Input System
to rozwiązuje się przez priorytetowo ułożone action maps: pause UI, popup UI,
tool active, default. Każdy map ma swój ESC action, gdy wyżej-prioretetowy obsłuży,
niższe nie dostają zdarzenia.

### 5.2 `MainMenuUI.cs`

| Akcja | Bindings | Linia |
|---|---|---|
| Wyjdź / powrót | `ESC` | 77 |

### 5.3 `GameCreatorUI.cs`

| Akcja | Bindings | Linia |
|---|---|---|
| Anuluj / powrót | `ESC` | 119 |

### 5.4 `BranchReturnDialogUI.cs`

| Akcja | Bindings | Linia |
|---|---|---|
| Anuluj dialog | `ESC` | 63 |

---

## 6. Drobne UI

### 6.1 `Map/UI/MapZoomSliderUI.cs`

| Akcja | Bindings | Linia | Komentarz |
|---|---|---|---|
| Detekcja release myszy | `LMB up` | 45 | `GetMouseButtonUp(0)` + EventSystem — potrzebne by odczytać że drag slidera się skończył |

---

## 7. Konflikty i obserwacje

### 7.1 ESC jest w **13 miejscach**

Kod: `Input.GetKeyDown(KeyCode.Escape)` × 13.

- `TrackBuildStateMachine` (2×)
- `TurnoutPlacementStateMachine`
- `ElectrificationStateMachine` (2×)
- `PathBuildStateMachine` (3×)
- `WallBuildingSystem` (5×)
- `HallActionStateMachine` (4×)
- `DepotVehicleManager`
- `TrainPopupUI`, `BuildingPopupUI`, `TrackPopupUI`
- `BranchReturnDialogUI`
- `PauseMenuUI`
- `MainMenuUI`
- `GameCreatorUI`

**Każda instancja sama sprawdza czy jej tryb jest aktywny przez `enabled` / `DepotUIManager.CurrentTool`**.
ESC "przechodzi" przez wszystkie listenery w każdej klatce. Nic nie "consume" eventu
ESC — jeśli otwarty jest popup I build tool, oba zareagują na ten sam ESC.

**W nowym Input System:** jeden `InputAction` "UI/Cancel" (lub per-map Cancel action), który
`consume`'uje event po obsłużeniu przez pierwszy handler. Priorytetowanie przez
kolejność Enable/Disable map.

### 7.2 LMB jest w **~12 miejscach**

`GetMouseButtonDown(0)` rozrzucone po state machines, popupach, kamerze, `DepotVehicleManager`.
To jest normalne dla klik-by-klik UI, ale też wymaga koordynacji:
- Popup otwarty blokuje LMB dla state machines
- State machine aktywny blokuje LMB dla selekcji pojazdu

Obecnie to jest **implicit** (każdy sprawdza `enabled` + `IsPointerOverUI`). W nowym
systemie byłoby **explicit** przez Action Maps.

### 7.3 Ctrl jako modyfikator w **3 miejscach**

- `DepotOrbitCamera` — wyłącza zoom gdy Ctrl wciśnięty
- `TurnoutPlacementStateMachine` — Ctrl+Scroll obraca rozjazd
- `UndoInputHandler` — Ctrl+Z cofa

**Implicit coordination między kamerą i TurnoutPlacement** — obie sprawdzają Ctrl
niezależnie, by nie kolidować. Kruche.

### 7.4 Brakujące klawisze (gap analysis)

Nie widzę w kodzie:
- **Redo** (zwykle Ctrl+Y lub Ctrl+Shift+Z)
- **Save / Load** (brak systemu zapisu)
- **Quick menu / radial menu** (brak)
- **Screenshot** (brak dedykowanego klawisza, F12?)
- **Toggle fullscreen** (Alt+Enter, brak w kodzie)
- **Camera presets** (brak — stary kod miał R = reset, T = top view w komentarzach, ale aktualnie T = pitch up)

---

## 8. Wszystkie używane klawisze (zbiorcze zestawienie)

| Klawisz | Akcje |
|---|---|
| `W` | Pan up (OrbitCamera), Pan up (MapCamera) |
| `A` | Pan left (OrbitCamera), Pan left (MapCamera) |
| `S` | Pan down (OrbitCamera), Pan down (MapCamera) |
| `D` | Pan right (OrbitCamera), Pan right (MapCamera) |
| `Q` | Yaw left (OrbitCamera), Q extra (MapCamera) |
| `E` | Yaw right (OrbitCamera), E extra (MapCamera) |
| `T` | Pitch up (OrbitCamera) |
| `F` | Pitch down (OrbitCamera) |
| `R` | Rotate turnout |
| `P` | Toggle pair mode (turnout) |
| `O` | Pair side cycle (turnout) |
| `U` | Branch return (turnout) |
| `Z` | Undo (Ctrl+Z) |
| `Spacja` | Stop vehicle |
| `ESC` | Cancel (13 miejsc) |
| `Del` | Delete (4 miejsca) |
| `LeftControl` / `RightControl` | Modifier + slow camera |
| `LeftShift` / `RightShift` | Fast camera |
| `Arrow keys` (Up/Down/Left/Right) | Pan (MapCamera) |

| Mysz | Akcje |
|---|---|
| `LMB` | Klik / drag / potwierdź (~12 miejsc) |
| `LMB double` | Focus camera |
| `LMB drag` | Paint catenary, paint path, drag slider |
| `RMB` | Anuluj (w build tools), orbit (OrbitCamera) |
| `MMB drag` | Pan (obie kamery) |
| `Scroll` | Zoom (obie kamery) |
| `Ctrl+Scroll` | Rotate turnout |
| `mousePosition` | Raycast (~12 miejsc) |

---

## 9. Propozycja Action Maps (wstępna — do dyskusji przed migracją)

**7 action maps** zgrupowanych logicznie:

### `Camera.Depot` (aktywna w scenie Depot, nie w popup)
- `Pan` — Vector2 composite z WSAD
- `Yaw` — Axis z Q/E
- `Pitch` — Axis z T/F
- `PanDrag` — MMB + Mouse Delta
- `OrbitDrag` — RMB + Mouse Delta
- `Zoom` — Axis z Scroll (z modyfikatorem NOT Ctrl)
- `Focus` — Button, LMB double-click
- `Point` — Vector2 z mouse position (raycast)

### `Camera.Map` (aktywna w scenie Map)
- `Pan` — Vector2 composite z WSAD + Arrows
- `SpeedModifier` — Button z Shift (fast)
- `SlowModifier` — Button z Ctrl (slow)
- `PanDrag` — MMB + Mouse Delta
- `Zoom` — Axis z Scroll
- `Point` — Vector2 z mouse position

### `Tool.Build` (ogólny dla wszystkich narzędzi budowania)
Wspólne dla TrackBuild, TurnoutPlacement, ElectrificationStateMachine, PathBuild, WallBuilding, Hall.

- `Primary` — Button z LMB
- `PrimaryHold` — Button z LMB hold (dla drag-paint)
- `Cancel` — Button z ESC lub RMB (composite)
- `Delete` — Button z Del
- `Undo` — Button z Ctrl+Z
- `Redo` — Button z Ctrl+Y **(nowy, do dodania)**
- `Point` — Vector2 z mouse position

### `Tool.Turnout` (dodatkowe do Tool.Build gdy aktywny sub-tryb rozjazdu)
- `Rotate` — Button z R
- `RotateScroll` — Axis z Ctrl+Scroll
- `PairToggle` — Button z P
- `PairSideCycle` — Button z O
- `BranchReturn` — Button z U

### `Vehicle` (aktywna gdy tryb Select w Depot)
- `Select` — Button z LMB
- `Stop` — Button z Spacja
- `Deselect` — Button z ESC

### `UI.Popup` (aktywna gdy dowolny popup otwarty — Track/Train/Building/BranchReturn)
- `Close` — Button z ESC
- `ClickOutside` — Button z LMB (do zamykania klikiem poza)

### `UI.PauseMenu` (aktywna zawsze, ale z **najwyższym priorytetem** ESC)
- `Toggle` — Button z ESC

---

## 10. Kolejność działania dla ESC (prioritized resolution)

Po migracji, gdy wciśnięty `ESC`, kolejność obsługi:

```
1. UI.PauseMenu.Toggle → jeśli otwarty Confirm dialog → zamknij Confirm, consume
2. UI.PauseMenu.Toggle → jeśli otwarte Pause menu → zamknij Pause, consume
3. UI.Popup.Close → jeśli otwarty popup szczegółów → zamknij, consume
4. Tool.Build.Cancel → jeśli aktywny build tool → reset tool do Select, consume
5. Vehicle.Deselect → jeśli zaznaczony pojazd → odznacz, consume
6. UI.PauseMenu.Toggle → otwórz Pause menu (fallback)
```

Żadne z tych `consume` nie istnieją dziś — obecnie wszystkie 13 listenerów ESC
reaguje równolegle na ten sam klik, każdy z osobna sprawdzając warunki. W nowym
Input System osiąga się to przez `InputActionMap.Enable/Disable` i `action.performed`
z `Interactions` typu `Press` + priority resolution.

---

## 11. Status pola wejścia (nie dotyczy migracji)

Następujące klasy używają `InputField` / `TMP_InputField` — to są komponenty UI Unity,
NIE part Input Managera. Przy migracji na nowy Input System **nie wymagają zmian
logicznych**, tylko trzeba się upewnić że scena ma `InputSystemUIInputModule` zamiast
`StandaloneInputModule` na EventSystem.

- `TopBarUI.cs` — depotNameInput (InputField)
- `TrackPopupUI.cs` — trackNameInput, lengthInput, radiusInput, leftCountInput, rightCountInput, spacingInput
- `BranchReturnDialogUI.cs` — spacingInput, radiusInput
- `FleetPanelUI.SeatLayout.cs` — cntInput (per zone, dynamicznie tworzone)
- `FleetPanelUI.cs` — `_searchField` (TMP_InputField)
- `FleetPanelUI.Market.cs` — `_marketSearchField`

---

## 12. Plan migracji (w dużych blokach)

**Etap 0:** Ten dokument (✅ gotowe).

**Etap 1:** Włączenie package `com.unity.inputsystem` + ustawienie **Active Input Handling = Both**
(tymczasowo, żeby nic się nie wywaliło).

**Etap 2:** Stworzenie `InputActions.inputactions` asset z 7 action maps z sekcji 9.

**Etap 3:** Generowanie wrappera C# (auto-generated `InputActions.cs` z asset'a).

**Etap 4:** Podłączenie `EventSystem` → zmiana na `InputSystemUIInputModule` w 4 scenach:
- `MainMenu.unity`
- `GameCreator.unity`
- `Depot.unity`
- `MapScene.unity`

**Etap 5:** Migracja **kamer** (największe pliki):
- `DepotOrbitCamera.cs`
- `Map/Rendering/CameraController.cs`

**Etap 6:** Migracja **state machines budowania**:
- `TrackBuildStateMachine`
- `TurnoutPlacementStateMachine`
- `ElectrificationStateMachine`
- `PathBuildStateMachine`
- `WallBuildingSystem`
- `HallActionStateMachine`

**Etap 7:** Migracja **UI / popupów**:
- `PauseMenuUI` (najbardziej złożony ESC flow)
- `TrainPopupUI`, `BuildingPopupUI`, `TrackPopupUI`
- `BranchReturnDialogUI`
- `MainMenuUI`, `GameCreatorUI`

**Etap 8:** Migracja **drobnych**:
- `DepotVehicleManager`
- `UndoInputHandler`
- `MapZoomSliderUI`

**Etap 9:** Test wszystkich 4 scen, wszystkich tools, wszystkich popupów.

**Etap 10:** Wyłączenie `Active Input Handling = New Input System Only` (zamiast Both).
Usunięcie ewentualnych pozostałości starego kodu.

**Etap 11:** Dokumentacja nowego systemu w `docs/conventions.md`.

---

## 13. Notatki / ryzyka

- **Additive loading Depot + MapScene:** istnieją jednocześnie 2 `EventSystem` i
  będą 2 `InputSystemUIInputModule`. Trzeba przetestować — Unity może się rozjeżdżać.
  Potencjalnie: jeden globalny EventSystem zarządzany przez SceneController.
- **`CameraController.cs` ma martwy kod** (linie 328–329 `GetAxis Horizontal/Vertical`
  duplikujące keyboard input). Wyczyść przy migracji.
- **Ctrl+Scroll "podział"** między kamerą i TurnoutPlacement — przy migracji można
  to zrobić czyściej: Turnout ma swój action `Rotate` z bindingiem `Ctrl+Scroll`,
  kamera nie musi nic wiedzieć. Priorytet action maps załatwi sprawę.
- **Redo nie istnieje** — okazja żeby dodać przy okazji migracji (`Ctrl+Shift+Z` lub `Ctrl+Y`).
- **Brak gamepada** w istniejącym kodzie — po migracji **automatycznie** dostaje
  wsparcie dla pada (dzięki compositon bindings w Action Maps). Do przemyślenia które
  akcje nie mają sensu na padzie (np. precyzyjne klikanie na tor z dużym zoomem).
