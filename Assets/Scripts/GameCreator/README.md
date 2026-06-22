# GameCreator — przewodnik

> Dokumentacja modułu `Assets/Scripts/GameCreator/`.

## Asmdef + warstwa

**`RailwayManager.GameCreator`** (rootNamespace: `RailwayManager.GameCreator`),
refs: `Core, MainMenu, SharedUI` (+ TMP + InputSystem).

Scena `GameCreator.unity` — kreator nowej gry. Zakładki:
- **SinglePlayer:** Ogólnie | Rozgrywka
- **Multiplayer:** Ogólnie | Serwer (z difficulty + game rules pod server config — host-authoritative)

Mały moduł — 10 partials `GameCreatorUI` + smoke test (`GameCreatorSmokeTest.cs`).

## Co tu siedzi

Jedna klasa, 10 partials:

| Plik | Po co |
|---|---|
| **`GameCreatorUI.cs`** | Root partial — pola, konstanty layout (SidebarWidth=220, TopBar/BotBar=70), input limits (MaxGameName/Server/Password 40/40/32), lifecycle, i18n hot-reload subskrypcja, `_isDirty` flag + `MarkDirty()`, primitives (`NewGO/MakeTMP/FillRT/EnsureEventSystem`). `MakeTMP` ustawia default `NoWrap` + `Ellipsis` overflow (i18n safety dla DE/CZ tłumaczeń). |
| **`GameCreatorUI.Layout.cs`** | `BuildCanvas` (CanvasScaler przez `UITheme.ApplyCanvasScaler` — MUI-10 konwencja) / `BuildTopBar` / `BuildSidebar` (2 tabs) / `BuildContentArea` / `BuildBottomBar`. |
| **`GameCreatorUI.Sections.cs`** | `PopulateSection` dispatcher → 3 sekcje: Ogólnie / Rozgrywka (SP) / Serwer (MP, z Difficulty+Rules na dole). |
| **`GameCreatorUI.Rows.cs`** | Row builders: `InputRow`, `DropdownRow`, `SliderRow`, `ToggleRow`, `MakeRow`, `AddSpacer`. Wszystkie podpinają `MarkDirty()` do `onValueChanged`/`onEndEdit`. |
| **`GameCreatorUI.Difficulty.cs`** | Sekcja Difficulty parent: preset selector dropdown (`MultiLineDropdown`, subtitle via `UITheme.SecondaryText`) + 10 modifierów + tooltip + live preview. |
| **`GameCreatorUI.Difficulty.Presets.cs`** | Preset apply (Easy/Normal/Hard/Realistic → write `DifficultyPreset` fields). **Custom celowo NIE resetuje sliderów** (UX: kontynuacja od ostatniego ustawienia, nie reset do 1.0). |
| **`GameCreatorUI.Difficulty.Modifiers.cs`** | 10 indywidualnych modifierów (np. `BreakdownChanceMultiplier`, `OperationalCostMultiplier`). StartBudget jako PLN `TMP_InputField` (nie slider). |
| **`GameCreatorUI.Difficulty.Tooltip.cs`** | Buduje "?" mini-button + attach `TooltipTrigger` (MUI-3, SharedUI). Sam widget tooltip'a delegowany do centralnego `TooltipManager` singleton'a — własny ad-hoc system (pre-2026-05-14) zastąpiony żeby uniknąć duplikatu. Auto-fade, screen-edge clamp, hover delay 0.5s — wszystko z central. |
| **`GameCreatorUI.Difficulty.Rules.cs`** | 6 game rules toggles (z `GameRulesConfig` w Core). |
| **`GameCreatorUI.ApplyOnStart.cs`** | Przycisk „Start" → apply wszystkich settings → `SceneManager.LoadScene("Depot")`. Difficulty+Rules apply **także w MP** (host-authoritative). |
| **`GameCreatorUI.CancelConfirmation.cs`** | Modal confirmation gdy gracz klika „Wyjdź" + dotknął jakiejś kontrolki (`_isDirty == true`). Brak zmian → direct exit. |
| **`GameCreatorSmokeTest.cs`** | Smoke test regresji (2026-05-14, wzór z `CoreSmokeTest`/`DepotSmokeTest`). 8 testów: mapping helpers (Autosave/MaxPlayers/Preset dropdown ↔ value + roundtrip), `FormatSeedFieldText`, preset loc/desc keys non-empty, `DifficultyPresetCatalog.Get` neutrality contracts, `SaveActionsHook.IsRegistered` po Bootstrap. Per-test `[ContextMenu]` + `RunAll`. AutoSpawn na AfterSceneLoad. |

## Cross-system glue

- **`GameCreatorContext` (z MainMenu)** ← read `Mode` w `Awake` → decyduje czy renderować zakładkę „Rozgrywka" (SP) czy „Serwer" (MP).
- **`DifficultyService` / `DifficultyPreset` / `DifficultyPresetCatalog` (Core/Difficulty/)** ← write — gracz wybiera preset + per-modifier overrides.
- **`GameRulesService` / `GameRulesConfig` (Core/GameRules/)** ← write — toggleable game rules.
- **`GameState.Money`** ← write w `ApplyDifficultyAndRulesOnStart` (preset multiplier × `DifficultyConstants.BaseStartingBudgetPln`).
- **`GameState.DepotName`** ← write w `ApplyGeneralOnStart` (jeśli nazwa niepusta).
- **`GameState.IsPaused`** ← write w `ApplyGameplayOnStart` (SP only, jeśli "pauza na start" zaznaczona).
- **`GameState.Seed`** ← write w `ApplySeedOnStart` (deterministic RNG dla debugowania crashów + MP-9 host-authoritative determinizm; 0 = baseline, dowolna liczba = powtarzalna sekwencja).
- **`SaveActionsHook.SetAutoSaveEnabled/SetAutoSaveIntervalSec/ResetRuntimeForNewGame`** ← invoke (Core hooks, AutoSaveService dostawca).
- **`GameCreatorContext.Server*`** ← write w `ApplyServerOnStart` (MP placeholder, M10 Mirror konsumuje przy host bootstrap).
- Po `LoadScene("Depot")`: **`GameState.HomeDepotStationId` set w osobnej nakładce `DepotLocationPickerUI`** (Timetable asmdef) — NIE w GameCreator.

## Gotchas

- **Procedural UI 10 partials** — idiom skopiowany z `MainMenuUI`. Każda nowa sekcja UI = nowy partial.
- **Kreator NIE używa `PlayerPrefs`** — settings idą do `SettingsService` (per-game lifetime) i `GameState` (per-session). PlayerPrefs zarezerwowany dla menu language preference + last-save-loaded.
- **Difficulty preset apply NIE blokuje per-modifier overrides** — gracz klika „Hard", potem może podnieść `BreakdownChanceMultiplier` jeszcze wyżej. Dropdown auto-switchuje na „Custom" gdy modyfikatory odbiegają od preset (`SwitchToCustomKeepingValues`).
- **MP host = full difficulty + game rules control** — Difficulty + Rules render'ują się pod Server config (host-authoritative). Klient nie wybiera, dostaje to co host.
- **MP server config (TD-022)** — `GameCreatorContext.ServerName/MaxPlayers/Password/Visibility` set w sekcji Serwer, konsumowane przez M10 Mirror. Walidacja: GameName/ServerName max 40 chars (`MaxGameNameLength`/`MaxServerNameLength`), Password max 32 (`MaxServerPasswordLength`), MaxPlayers 2-16. `ServerVisibility` cast clamp'owany do enum range.
- **Cancel confirmation modal** — TYLKO gdy `_isDirty == true` (gracz dotknął jakiejkolwiek kontrolki). Brak zmian → direct `LoadScene("MainMenu")`. `_isDirty` flag'ują wszystkie row buildery (Rows.cs) + Difficulty/Rules listeners. Programatyczne `SetValueWithoutNotify` (preset switch, locale reload) NIE flag'ują dirty.
- **i18n hot-reload** — `GameCreatorUI` subskrybuje `LocalizationService.OnLanguageChanged` i wywołuje `PopulateSection(_activeSection)` (rebuild contentu aktywnej zakładki). Top bar title + sidebar tabs + bottom bar buttons odświeżane manualnie w `OnLocaleChanged`.
- **`MakeTMP` default = `NoWrap` + `Ellipsis`** — chroni przed łamaniem row height (44-52px sztywne) gdy DE/CZ tłumaczenia są dłuższe. Multi-line cases (cancel modal body, tooltip, preset description) override do `TextWrappingModes.Normal` w callsite.
- **Sekcja Mapa wyrzucona 2026-05-14** — była dekoracyjna (5 mockowych pozycji, `_selectedMap` bez konsumenta). Wybór regionu/DLC wraca w M-DLC (`GameState.AddDlcCountry`/`ResetDlcCountries`).
- **Speed slider wyrzucony 2026-05-14** — slider 0.5-3x ciągły niezgodny z dyskretnymi przyciskami x1/x5/x25/x150/x500 w `TopBarUI`. Speed steruje się z top bara po wejściu do Depot (`GameState.TimeScale` zostaje default 1f).
- **Seed input** — `TMP_InputField` z `ContentType.IntegerNumber`, max 11 znaków (zakres `int.MinValue..MaxValue`). Puste pole = 0 (deterministyczne baseline). Render'owane w SP Rozgrywka + MP Serwer (host MP musi ustawić żeby klienci mieli ten sam — MP-9 determinizm). `_seedValue == 0` → `_fieldSeed.text` pozostaje pusty (UX: nie pokazuj wprost "0" gdy gracz nic nie wpisał).

## Powiązane docs

- `docs/design/game-creator.md` — design spec kreator + DepotLocationPicker
- `docs/design/balance-constants.md` — gdzie żyją Difficulty constants
