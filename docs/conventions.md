# Konwencje kodu — Railway Manager

Krótki przewodnik po konwencjach przyjętych w projekcie. **Stosowany
stopniowo, przy okazji dotykania plików** — nie ma dnia "wielkiej migracji".
Nowy kod od dziś trzyma się tych zasad.

---

## Namespace

### Cel docelowy

Wszystkie namespace'y projektu pod jednym korzeniem **`RailwayManager`**:

```
RailwayManager.Core         ── enums, helpers, common types, klasa Log
RailwayManager.Map          ── system mapy 2D OSM
RailwayManager.Depot        ── scena zajezdni 3D
RailwayManager.Fleet        ── tabor, dane pojazdów, konfigurator
RailwayManager.Maintenance  ── utrzymanie, rescue, warsztaty, magazyn części
RailwayManager.SharedUI     ── wspólne UI (TopBar)
RailwayManager.MainMenu     ── menu główne
RailwayManager.GameCreator  ── kreator gry
RailwayManager.Timetable    ── rozkłady jazdy, pathfinding, rezerwacje
```

### Stan obecny

Mieszanka. Większość modułów używa starszych nazw, które są **tolerowane**:

| Obecnie | Docelowo |
|---|---|
| `DepotSystem` | `RailwayManager.Depot` |
| `DepotSystem.Undo` | `RailwayManager.Depot.Undo` |
| `MapSystem` | `RailwayManager.Map` |
| `MainMenu` | `RailwayManager.MainMenu` |
| `RailwayManager` (gołe) | `RailwayManager.Map.Rendering` (pliki w Map/Rendering/) |
| `RailwayManager.Fleet` | bez zmian (już OK) |
| `RailwayManager.Maintenance` | bez zmian — runtime M7; pliki fizycznie w `Timetable/Runtime/Maintenance` do czasu wydzielenia asmdef |
| `RailwayManager.SharedUI` | bez zmian (już OK) |
| `RailwayManager.GameCreator` | bez zmian (już OK) |
| `RailwayManager.Timetable` | bez zmian (już OK — nowy moduł M4) |
| `formap` | bez zmian — kod z osobnego projektu OSM, nie ruszać |

### Zasada migracji

- **Nowy plik / nowa klasa** → od razu w docelowym namespace
- **Edytujesz istniejący plik bez zmiany namespace** → zostaw jak jest
- **Edytujesz duży kawałek** lub przenosisz plik → przy okazji zmień namespace
- **Refaktor "tylko żeby zmienić namespace"** → nie. To koszt bez wartości.

### Co kod sprawdza

Każdy plik `.cs` w `Assets/Scripts/` **musi** mieć `namespace` (oprócz
plików z `formap` — to inny projekt). Globalny scope = wyjątek do poprawienia.

---

## Nazewnictwo

### Pliki C#

- **PascalCase** dla nazw plików: `TrackBuilder.cs`, `DepotManager.cs`
- **Nazwa pliku = nazwa głównej klasy**: `Foo.cs` zawiera `class Foo`
- Nie: `cameracontroller.cs`, `Mapsetup.cs`, `foo_bar.cs`

### Klasy / metody / pola

- **Klasy, metody publiczne, properties:** `PascalCase`
  ```csharp
  public class TrackGraph
  public void RebuildGraph()
  public int NodeCount { get; }
  ```
- **Pola publiczne (Inspector):** `camelCase`
  ```csharp
  public float trackGauge = 1.435f;
  public Material railMaterial;
  ```
- **Pola prywatne:** `_camelCase` (z podkreśleniem)
  ```csharp
  private GameObject _root;
  private List<Vehicle> _vehicles;
  ```
- **Stałe:** `PascalCase` (nie SCREAMING_SNAKE_CASE)
  ```csharp
  private const float MaxSpeed = 200f;
  ```
- **Enums:** `PascalCase` typ + `PascalCase` wartości
  ```csharp
  public enum ToolMode { Select, BuildTrack, BuildCatenary }
  ```

### Język nazw — angielski czy polski?

**Anglojęzyczne** dla:
- Nazw klas, metod, properties, pól
- Komentarzy XML doc (`/// <summary>`)
- Nazw plików
- Identyfikatorów w kodzie ogólnie

**Polski** dozwolony w:
- Stringach UI (i tak będą lokalizowane)
- Komentarzach inline (`// to jest tymczasowe rozwiązanie`)
- Polach `[Tooltip("...")]` w Inspectorze (Polski OK, ale nazwa pola po angielsku)

**Wyjątki które już są (do migracji przy okazji):**
- `UndoCategory.Tory`, `SiecTrakcyjna`, `Sciezki`, `Pomieszczenia`
- Nie ruszać hurtem, przy okazji refaktoru.

### Boolowe pola/metody

Prefiks **`is/has/should/can`**:
```csharp
public bool isElectrified;
public bool hasPantograph;
private bool ShouldRebuildMesh()
```

Nie: `electrified`, `pantograph`, `rebuild`.

---

## Logowanie

Używamy własnej klasy `RailwayManager.Core.Log` zamiast `Debug.Log`:

```csharp
using RailwayManager.Core;

Log.Info("[DepotManager] Initialized");
Log.Warn("[CatenaryGenerator] Skipping invalid wire span");
Log.Error("[MapLoader] Failed to load tile: " + path);
Log.Debug("[TrackGraph] Node count: " + nodeCount);  // verbose, off in release
```

**Zasada:** prefiks `[ClassName]` w pierwszej kolejności wiadomości.

`Log.Debug()` jest opakowane w `[Conditional("DEBUG_LOG_VERBOSE")]` — w
buildach release jest kompletnie usunięte (zero kosztu). `Log.Info/Warn/Error`
zawsze działają.

`UnityEngine.Debug.Log` użyć tylko gdy faktycznie chcesz logować bez prefiksu
klasy (rzadko).

---

## Input System

Projekt używa **Unity Input System package** (`com.unity.inputsystem`) w trybie
**New Input System Only**. Stary `UnityEngine.Input` (Input Manager) jest
zdezaktywowany — kompilator krzyczy przy próbie użycia.

### Asset i wrapper

- **Asset:** `Assets/Settings/InputActions.inputactions` — definiuje 7 action maps
- **Wrapper C#:** `Assets/Scripts/Core/InputActions.cs` — auto-generowany przez
  Unity na podstawie asset'a, namespace `RailwayManager.Core`
- **Package reference:** każdy asmdef używający Input System musi mieć
  `"Unity.InputSystem"` w `references`

### Action maps

```
Camera.Depot    — kamera orbitująca w scenie Depot (WSAD, Q/E, T/F, MMB, RMB, scroll)
Camera.Map      — kamera ortograficzna w scenie Map (WSAD+Arrows, MMB, scroll, Q/E)
Tool.Build      — wspólne dla narzędzi budowania (LMB, RMB, ESC, Del, Ctrl+Z, Ctrl+Shift+Z)
Tool.Turnout    — dodatkowe dla rozjazdów (R, P, O, U, scroll)
Vehicle         — selekcja pojazdów (LMB, Space, ESC)
UI.Popup        — dormant (rebinding w przyszłości)
UI.PauseMenu    — dormant (rebinding w przyszłości)
```

### Wzorzec MonoBehaviour

Każda klasa używająca Input System powinna mieć:

```csharp
using UnityEngine;
using UnityEngine.InputSystem;
using RailwayManager.Core;

public class MyBehaviour : MonoBehaviour
{
    private InputActions _inputActions;
    private InputActions.CameraDepotActions _cameraActions;  // lub inny map

    void Awake()
    {
        _inputActions = new InputActions();
        _cameraActions = _inputActions.CameraDepot;
    }

    void OnEnable()  { _cameraActions.Enable(); }
    void OnDisable() { _cameraActions.Disable(); }
    void OnDestroy() { _inputActions?.Dispose(); }

    void Update()
    {
        if (_cameraActions.Primary.WasPressedThisFrame()) { ... }
        Vector2 pan = _cameraActions.Pan.ReadValue<Vector2>();
    }
}
```

### Rodzaje Action API

| Typ | Użycie | API |
|---|---|---|
| **Button** (trigger) | Klik, klawisz pressed | `.WasPressedThisFrame()` |
| **Button** (release) | LMB up po dragu | `.WasReleasedThisFrame()` |
| **Button** (hold) | MMB trzymane | `.IsPressed()` |
| **Value** (Vector2) | Composite WSAD, mouse delta | `.ReadValue<Vector2>()` |
| **Value** (float Axis) | Q/E, T/F (1DAxis composite) | `.ReadValue<float>()` |

### Direct device access (ominięcie action maps)

Dla prostych spraw (ESC zamykający popup, mouse position dla raycast) bezpośrednie
device access jest **pragmatyczne i bezpieczniejsze** niż tworzenie action:

```csharp
// Keyboard — dla pojedynczych klawiszy bez action map
if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
    ClosePopup();

// Mouse position — dla raycast, bez action
if (Mouse.current != null)
{
    Vector2 mousePos = Mouse.current.position.ReadValue();
    Ray ray = mainCamera.ScreenPointToRay(mousePos);
}
```

**Kiedy action, kiedy direct:**
- **Action** — jeśli bindowanie ma być rebindowalne w ustawieniach, jeśli composite (WSAD), jeśli z modyfikatorem (Ctrl+Z)
- **Direct** — dla hardcoded system keys (ESC zamykający UI), mouse position (zawsze current), szybkie fixy

### Pułapki z migracji — do pamięci

1. **`IsPointerOverUI()` jest bardziej agresywny w `InputSystemUIInputModule`.**
   Sprawdzaj ESC/keyboard **przed** guardem `IsPointerOverUI()` — klawisze
   nie zależą od pozycji kursora. Tylko klik powinien respektować ten guard.

2. **Wiele action maps z tym samym klawiszem (np. ESC) może się konfliktować.**
   Dla ESC popupów użyj direct `Keyboard.current.escapeKey.wasPressedThisFrame`
   zamiast `UI.Popup.Close.WasPressedThisFrame()`.

3. **Kolejność Update vs LateUpdate.** Gdy popup w Update zamyka się na ESC,
   a PauseMenu w Update też widzi ESC — race condition. Rozwiązanie:
   PauseMenu w `LateUpdate` + popupy ustawiają `PauseMenuUI.LastEscConsumedFrame`
   w Update. Unity gwarantuje że wszystkie `Update` wykonują się przed wszystkimi `LateUpdate`.

4. **Scroll wheel w Input System** zwraca `Vector2` (y = wertykalny scroll),
   nie `float`. Skala ~raw delta, trzeba pomnożyć przez `0.1f` żeby dopasować
   do feel starego `Input.GetAxis("Mouse ScrollWheel")`.

5. **EventSystem tworzone proceduralnie** musi używać `InputSystemUIInputModule`,
   nie `StandaloneInputModule`. W `Active Input Handling = New Only` stary moduł
   rzuca `InvalidOperationException`.

6. **`Mouse.current.position.ReadValue()`** zwraca `Vector2`. `ScreenPointToRay`
   przyjmuje `Vector3` (automatyczna konwersja OK).

### Asmdef references

Każdy asmdef używający Input System musi mieć:

```json
"references": [
    ...
    "Unity.InputSystem"
]
```

Obecnie: Core, Depot, Map, MainMenu, GameCreator, Timetable (6 z 8). Fleet i SharedUI
nie mają — nie potrzebują.

---

## State machines

Skomplikowane tryby narzędzi (Track, Turnout, Catenary, Path, Hall) używają
**state machine MonoBehaviours** z sufiksem `*StateMachine`:

```
TrackBuildStateMachine
TurnoutPlacementStateMachine
ElectrificationStateMachine
PathBuildStateMachine
HallActionStateMachine
```

Każdy state machine zarządza wejściem (mouse/keyboard) i deleguje pracę
do "service" klas (`PrefabTrackBuilder`, `TurnoutPlacer`, `CatenaryGenerator`...).

**Nie wciskać input handling do service'ów** — service buduje, state machine
decyduje kiedy.

---

## Plików nie powinno być za dużych

**Soft limit:** 1000 linii. Jeśli przekraczasz, zastanów się czy plik nie ma
wielu odpowiedzialności.

**Hard limit:** 2000 linii. Powyżej — refaktor jest pilny.

Wyjątek: plik klasy z dużą ilością prostych get/set lub generowane Unity YAML.

Dla dużych klas UI rozważ **`partial class`** — dzieli plik na wiele bez
zmiany struktury klasy.

---

## Komentarze

- **`/// <summary>`** dla publicznych klas i metod publicznych jeśli nie są
  oczywiste z nazwy
- **Inline `//`** tylko gdy logika nie jest oczywista, lub gdy wyjaśniasz
  *dlaczego*, nie *co*
- **Nie pisać komentarzy które powtarzają nazwę metody:**
  ```csharp
  // ŹLE:
  // Initializes the depot.
  void InitializeDepot() { ... }
  ```
- Komentarze do TODO/HACK/FIXME są OK, ale staraj się ich nie zostawiać
  na długo. Aktualnie w projekcie ~16 takich znaczników.

---

## Assembly Definitions (.asmdef)

Projekt używa 10 assemblies do egzekwowania granic modułów przez kompilator:

```
RailwayManager.Core        (Assets/Scripts/Core/)       ── bez zależności
RailwayManager.Fleet       (Assets/Scripts/Fleet/)      ── Core
RailwayManager.SharedUI    (Assets/Scripts/SharedUI/)   ── Core
RailwayManager.MainMenu    (Assets/Scripts/MainMenu/)   ── Core, SharedUI
RailwayManager.GameCreator (Assets/Scripts/GameCreator/)── Core, MainMenu, SharedUI
RailwayManager.Depot       (Assets/Scripts/Depot/)      ── Core, Fleet, SharedUI, MainMenu
RailwayManager.Map         (Assets/Scripts/Map/)        ── Core, SharedUI, Depot
RailwayManager.Timetable   (Assets/Scripts/Timetable/)  ── Core, Fleet, Map, Depot, SharedUI
RailwayManager.Personnel   (Assets/Scripts/Personnel/)  ── Core, Timetable, Depot, Fleet, SharedUI
RailwayManager.SaveLoad    (Assets/Scripts/SaveLoad/)   ── Core, SharedUI, Fleet, Timetable, Personnel, Depot
```

**Kierunkowość (brak cykli):**
- `Depot` NIE zależy od `Map` (dawny cykl zerwany przez przeniesienie
  `DepotRailwayIntegration.cs` z `Depot/` do `Map/`).
- `Map` zależy od `Depot` (dla `DepotManager` w `DepotRailwayIntegration`).
- `Timetable` zależy od `Map` + `Fleet` + `Depot` — moduł runtime z dostępem
  do wszystkich niższych warstw. Referencja do `Depot` jest dozwolona dla
  cross-system glue (np. `DepotMapHandshakeService`, warsztaty skanujące
  `RoomDetectionSystem`), nie dla czystej logiki rozkładów/pathfindingu.
- `Personnel` zależy od `Timetable` (M8 — CrewCheckHook, CrewAssignmentService,
  WorkshopAssignmentService z hook-based integracją). Wprowadzony 2026-04-21.
- `SaveLoad` jest **najwyższą warstwą** — referuje wszystkie moduły z ISavable
  (Fleet, Timetable, Personnel, Depot, plus Economy/Maintenance/Circulations
  jako pliki w obrębie `Timetable.asmdef`). Nikt nie referuje SaveLoad —
  bootstrap odkrywa ISavable przez reflection.
- `Fleet`, `SharedUI` zależą tylko od `Core` — wymienialne/izolowane.

**Asymetria asmdef (świadoma):**
- `Depot` NIE referuje `Timetable` mimo że niektóre Depot features potrzebują
  cross-system glue (np. `PartInventoryFurnitureBridge` z M-Furniture). Glue
  jest implementowane po stronie wyższej warstwy (Timetable namespace) lub
  przez event subscription pattern. Zachowuje to czystość kierunku zależności.

**Przy dodawaniu nowego pliku:** umieść go w folderze odpowiadającym jego
module. Asmdef obejmuje folder + wszystkie podfoldery. Namespace pliku
NIE musi odpowiadać asmdef — Unity patrzy na lokalizację fizyczną.

**Jeśli nowy plik potrzebuje typu z innego modułu:** dodaj dependency
w odpowiednim `.asmdef` (pole `references`). Jeśli Unity krzyczy
"Cyclic assembly reference" — masz cykl do rozplątania (najczęściej przez
przeniesienie jednej klasy do Core albo wyciągnięcie interfejsu).

---

## Prefabrykaty

Struktura folderów w `Assets/Prefabs/`:

```
Assets/Prefabs/
├── Depot/
│   ├── Track/          — szyna, podkład, rozjazdy
│   ├── Catenary/       — słupy, wysięgniki, izolatory sekcyjne
│   ├── Buildings/      — ściany, drzwi, okna, hale
│   ├── Fence/          — ogrodzenie, bramy
│   └── Vehicles/       — lokomotywy, wagony, EZT/SZT
├── Map/                — markery stacji, ikony POI
├── UI/                 — popup'y, dialogi, panele (gdy używane jako prefaby)
└── Shared/             — prefaby używane w wielu scenach
```

**Zasady:**

- **Nazwa prefaba = nazwa zawartości**, PascalCase: `Szyna_1m.prefab`, `SlupTrakcji_SBS_7m.prefab`
- **Prefab wariant** (Unity Prefab Variant) dla wariantów modelu — np. `SlupTrakcji_SBS_7m_nowe.prefab`
- **Zagnieżdżone prefaby** (Nested Prefabs) są OK, Unity je dobrze obsługuje
- **Nie trzymać prefabów obok kodu** (np. w `Assets/Scripts/Depot/`) — zawsze w `Assets/Prefabs/`
- **Materiały prefabów** w `Assets/Materials/<Module>/`, nie wewnątrz prefab foldera

**Referencje w kodzie:** prefaby wpinane przez Inspector w publiczne pola
(`public GameObject railPrefab`). Nie używać `Resources.Load` chyba że konieczne.

---

## Wyjątki od konwencji

Każda konwencja ma wyjątki. Jeśli widzisz coś co łamie zasady ale działa
i nikt tego nie dotyka — **nie ruszaj dla samej zasady**. Ten dokument
nie jest po to żeby wymuszać hurtowe refaktory, tylko żeby nowy kod był
spójny.
