# ADR-0003: Unity New Input System Only (bez Input Manager)

**Status:** Accepted
**Date:** ~2026-04-11 (migration completed)
**Context:** Core — system obsługi klawiatury/myszy/gamepada

## Context

Unity ma dwa równoległe systemy inputu:
1. **Input Manager (stary)** — `Input.GetKey()`, `Input.GetAxis()`, fixed mappings w Project Settings
2. **Input System (package)** — `InputAction`, `.inputactions` asset, rebindable, composite

Można używać:
- **Only Input Manager (Old)** — tylko stary
- **Only Input System (New)** — tylko nowy
- **Both** — oba równolegle

Wczesne prace projektu używały **Both** mode — działało ale były pułapki:
- `Input.GetAxis("Horizontal")` mieszał się z gamepad sticks (drifting)
- `Input.GetMouseButtonDown` i `Mouse.current.leftButton.wasPressedThisFrame` mogły dać różne wyniki
- Rebindowanie klawiszy (M13) wymaga New Input System
- 113 wywołań starego API rozsianych po 18 plikach

## Decision

**New Input System Only**.

1. Zmiana w Project Settings: `Active Input Handling = New Only`
2. Migracja wszystkich wywołań `Input.*` na New Input System API
3. Stworzenie 7 action maps w `Assets/Settings/InputActions.inputactions`:
   - `Camera.Depot`, `Camera.Map`
   - `Tool.Build`, `Tool.Turnout`
   - `Vehicle`
   - `UI.Popup`, `UI.PauseMenu`
4. Wszystkie moduły używające inputu muszą mieć `Unity.InputSystem` w asmdef references
5. Stary `Input.*` API jest **niedostępne** — kompilator krzyczy

## Consequences

### Positive
- **Rebindable keybindings** — M13 Settings Settings dostanie pełne rebinding z UI
- **Composite actions** — WSAD jako jeden Vector2, nie 4 osobne klawisze
- **Gamepad support** — gratis z nowego systemu (dla post-launch)
- **Czystsza architektura** — jasny podział action maps per kontekst (kamera vs build tool vs UI)
- **Race conditions naprawione** — direct device access (`Keyboard.current.escapeKey`) dla hardcoded keys, action maps dla rebindable
- **0 Input Manager calls** — audyt w `docs/input-audit.md` potwierdza migrację (zostały 2 komentarze, 0 wywołań)

### Negative
- **Rebinding complexity** — trudniej debug niż `Input.GetKey`
- **InputSystemUIInputModule issues** — `IsPointerOverUI()` zachowuje się inaczej niż w starym systemie (patrz conventions.md pkt 1 pułapek)
- **API mental model** — każdy nowy deweloper musi nauczyć się `WasPressedThisFrame` vs `IsPressed` vs `ReadValue`
- **Wymóg asmdef reference** — każdy moduł używający inputu musi dodać `Unity.InputSystem`

### Neutral
- EventSystem tworzone proceduralnie MUSI używać `InputSystemUIInputModule`
- Scroll wheel zwraca Vector2, nie float — trzeba pomnożyć przez 0.1f żeby odpowiadać starym skalom

## Alternatives considered

1. **Both mode z preferencją dla New** — chaotyczne, zachowuje wszystkie problemy starego API
2. **Pozostanie przy Input Manager** — brak rebindowania, brak gamepada, brak composite
3. **Own input wrapper na Input.*** — reinventing the wheel, mniej możliwości niż New Input System

## References

- `docs/input-audit.md` — pełny audyt migracji (historyczny, ale ma listę wszystkich klawiszy)
- `docs/conventions.md` sekcja "Input System" — wzorzec użycia + pułapki z migracji
- Asset: `Assets/Settings/InputActions.inputactions`
- Wrapper: `Assets/Scripts/Core/InputActions.cs`
