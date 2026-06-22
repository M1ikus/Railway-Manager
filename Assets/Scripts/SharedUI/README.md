# SharedUI — przewodnik

> Dokumentacja modułu `Assets/Scripts/SharedUI/`.

## Asmdef + warstwa

**`RailwayManager.SharedUI`** (rootNamespace: `RailwayManager.SharedUI`),
refs: `Core`, `Unity.TextMeshPro`.

UI **infrastructure** — komponenty/helpers/lokalizacja używane przez WSZYSTKIE UI
w grze (Depot, Timetable, Personnel, MainMenu…). **Nie ma żadnego panelu domenowego** —
to czysty layer narzędzi. Jeden konkretny UI siedzi tu: `TopBarUI` (cross-scene HUD).

**Konsumenci:** prawie wszystkie wyższe asmdef (MainMenu/GameCreator/Depot/Map/Timetable/Personnel).

## Co tu siedzi

| Plik / folder | Po co |
|---|---|
| **`UITheme.cs` + `UITheme.Shapes.cs` + `UITheme.Palette.cs` + `UITheme.Fonts.cs` + `UITheme.Apply.cs`** | Central palette/typography/sizing — **partial class** rozbita 2026-05-15 na 5 plików (wcześniej 3, w tym 370 LOC siedziało w `TopBarUI.cs`). `UITheme.cs` Typography/Spacing/Sizing/Transitions/Status colors/`ApplyCanvasScaler`. `Shapes` cache rounded sprites. `Palette` kolory + enums + Darken/WithAlpha/FromHex/GetTextColor/GetReputationColor. `Fonts` LegacyFont/TmpFont + Runtime SDF bootstrap z fallback chain (BUG-006). `Apply` ApplyLegacyText/ApplyTmpText/ApplyButtonStyle/ApplyLegacyInputField/CreateButtonColorBlock/CreateInputColorBlock. |
| **`UIPrimitives.cs`** | Low-level LEGO bricks (2026-05-15 adoption pass Krok 1): `NewGO(name, parent)`, `Stretch(rt)`, `Fill(go)`, `MakeTMP(name, parent, fontSize?, role?, alignment?, style?)`. Single source dla operacji wcześniej zduplikowanych w `MenuScreenPrimitives` + `DepotUIPanelPrimitives`. **Generic, bez sizingu** — buduje GameObject + RectTransform + TMP. |
| **`UIBuilders.cs`** | Themed components (2026-05-15 Krok 2 rewrite — 387→288 LOC): `MakeLabel(role)`, `MakePanel(role)`, `MakeContainer(layout)`, `MakeSeparator`, `MakeButton(tone)`, `MakeIconButton(sprite, tone)`, `AttachTooltip`. **NIE ustawia sizeDelta** — caller decyduje (LayoutElement / anchors / manual). Delegate do `UIPrimitives` dla low-level. |
| **`TopBarUI.cs`** | Cross-scene HUD na górze ekranu (czas, data, pieniądze, reputacja, speed buttons). 462 LOC — sama klasa HUD MonoBehaviour po wydzieleniu UITheme partial. |
| **`TooltipManager.cs` + `TooltipTrigger.cs`** | MUI-3 system tooltipów. `TooltipTrigger` jako MonoBehaviour na elemencie UI → `TooltipManager` singleton pokazuje overlay. **Posiada CanvasScaler** — `UITheme.ApplyCanvasScaler`. Canvas reuse przez `TooltipOverlayCanvasMark` marker komponent (nie po `gameObject.name`). |
| **`MultiLineDropdown.cs`** | Custom dropdown z 2-liniowymi items (główna nazwa + subtitle). Idempotency check przez `SubtitleInjectedMark` marker komponent (nie text scanning). |
| **`IconGenerator.cs`** | Procedural ikony — stop-gap przed M-UIPolish MUI-11 (Unity AI Generators icons). |
| **`AsyncUI.cs`** | Helper dla async UI operations (np. F1.9 multi-state pathfinder UI). |
| **`UIMode.cs`** | Enum trybów UI (Basic/Advanced/Expert) — gated przez `PlayerProgressService`. M-TimetableUX F1.16 per-element gating. |
| **`PlayerProgressService.cs`** | Track unlock conditions (Advanced 5+ timetables, Expert 10h tutorial). Static state z Snapshot/RestoreFromSave persistowane przez `SaveLoad/Modules/SharedUISavable.cs`. Event `OnModeUnlocked`. |
| **`Suggestions/SuggestionMemoryService.cs`** | Wspólny memory dla 6 suggestion services w Timetable + Personnel CrewSwap. Don't-show-again, snooze, dismiss tracking. Per-save persistence przez `SharedUISavable`. |
| **`Localization/`** | i18n: `LocalizationService` (Newtonsoft JObject flatten), `LocaleResolver` (single registry dict, 7 locales: PL/EN/DE/CZ/JP/RU/UK), `LocaleCode` enum, `LocalizedText`/`LocalizedTextFormatted` (TMP wrappers — aktualnie dead code, nikt nie używa), `NumberFormatService` (currency = "zł" suffix wymuszony niezależnie od kultury — bug fix 2026-05-15), `SteamLanguageBridge` (M14 stub). |
| **`SharedUISmokeTest.cs`** | 7 testów po audycie 2026-05-15: FormatCurrency "zł" suffix, ThemeColors+Focus alias, ApplyCanvasScaler, Localization fallback, LocaleResolver registry coverage, PlayerProgress roundtrip, SuggestionMemory dismiss/snooze. AutoSpawn AfterSceneLoad + RunAll. |

## Cross-system glue

- **`UITheme.*`** — single source of truth dla wszystkich UI. Konsumowane przez UIBuilders/UIPrimitives + bezpośrednio w panelach.
- **`UIPrimitives.NewGO/Stretch/MakeTMP`** — single source dla operacji wcześniej duplikowanych w `MenuScreenPrimitives` (MainMenu) + `DepotUIPanelPrimitives` (Depot). Oba wrappery delegują do `UIPrimitives` (zachowując własne API).
- **`UIIntents`** (z Core) — emit/subscribe pattern dla cross-asmdef UI open/close.
- **`PlayerProgressService.OnModeUnlocked`** event → notification system + UI gating w Timetable F1.16.
- **`LocalizationService.OnLocaleChanged`** → wszystkie `LocalizedText` rebuildują się automatycznie.
- **`TopBarUI`** subskrybuje `GameState.OnDayEnded` i `ReputationManager` (mirror via `GameState.GlobalReputation` — Timetable Economy NIE jest tu referowane bo SharedUI to niższa warstwa).

## Choosing UI helpers (Krok 3 wytyczna 2026-05-15)

Trzy warstwy abstrakcji — wybierz najniższą która pasuje:

1. **`UITheme.*` direct** — gdy potrzebujesz tylko koloru/typografii/spacingu (`UITheme.PrimaryAccent`, `UITheme.Spacing.Md`, `UITheme.Typography.H2`).
2. **`UIPrimitives.NewGO/Stretch/Fill/MakeTMP`** — gdy budujesz custom layout. Generic, bez sizingu, bez theme'u poza TMP font/color.
3. **`UIBuilders.MakeLabel/MakePanel/MakeContainer/MakeSeparator/MakeButton/MakeIconButton`** — gdy chcesz themed component (rounded surface, button color block, typography role). Caller dodaje LayoutElement/sizeDelta sam.

**Domain-specific helpery NIE w SharedUI** — zostają w swoim asmdef:
- `DepotSystem.DepotUIPanelPrimitives` — `CreateOptionButton` (icon+label+accent bar), `CreateListButton`, `CreateSectionHeader`, `CreateHorizontalPanel`, `ApplyOptionButtonState`. Per-state styling dla Depot toolbarów.
- `MainMenu.MenuScreenPrimitives` — `CreateFullscreenRoot`, `CreateTopBar` (back button + title), `CreateButton` (z explicit width/height), `BuildVerticalScrollArea` (full hierarchia ScrollRect+Viewport+Content+Scrollbar).
- Per-panel `private MakeBtn/MakeRow/MakeConfirmBtn` w 6 plikach (Timetable/CirculationListUI, VehicleAssignmentModal, Depot/PauseMenuUI, GameCreator/SettingsScreen Rows) — różnią się sizingiem między panelami. **Nie wymuszamy migracji** — okazyjnie, gdy panel jest dotykany z innego powodu.

## Gotchas

- **`UITheme` jest `partial` × 5 plików** (od 2026-05-15): `UITheme.cs` (Typography/Spacing/Sizing/Transitions/Status/CanvasScaler), `UITheme.Shapes.cs` (rounded sprite cache), `UITheme.Palette.cs` (kolory + UIThemeTextRole/UIButtonTone enums + Darken/WithAlpha/FromHex/GetTextColor), `UITheme.Fonts.cs` (LegacyFont/TmpFont/runtime SDF bootstrap), `UITheme.Apply.cs` (apply helpers + color blocks). **NIE duplikuj** definicji między partials.
- **Canvas Scaler**: każde `gameObject.AddComponent<CanvasScaler>()` musi przejść przez `UITheme.ApplyCanvasScaler(scaler)` (MUI-10 + 2026-05-15 audit = 17 callsite zmigrowanych). Helper ustawia ScaleWithScreenSize + ref 1920×1080 + MatchWidthOrHeight + match 0.5.
- **`UIBuilders.MakeButton/MakeIconButton` NIE ustawia sizeDelta** (od 2026-05-15 Kroku 2). Wcześniej hardcode (160, 36) konfliktował z każdym panelem. Caller dodaje `LayoutElement.preferredWidth/Height` lub manual `rt.sizeDelta = ...`.
- **`LocalizedText`/`LocalizedTextFormatted` to dead code** (2026-05-15 audit). `LocalizationService.Get(key)` używane w 1135 miejscach direct, MonoBehaviour wrappery w 0 plikach. Decyzja czy usunąć vs migrować odłożona.
- **`Localization` keys żyją w `Resources/Locale/<locale>/strings.json`** — fallback chain `current → EN → "[key]"`. Hierarchical JSON flattened do dotted keys w bootstrap.
- **Currency format ZAWSZE `"X zł"`** (suffix), niezależnie od locale. `NumberFormatService.FormatCurrency` wymusza `CurrencySymbol = "zł"` w NumberFormatInfo (bug fix 2026-05-15 — wcześniej `ToString("C", culture)` dawał $/€/¥ per kultury). Tylko separatory tysięcy/dziesiętne lokalizują.
- **`TopBarUI` był do 2026-05-10 host'em GameClock tick** — teraz tick w `Core/GameClock.cs`. TopBarUI tylko czyta `GameState.GameTimeSeconds` i renderuje.
- **`SuggestionMemoryService` cross-system**: każda sugestia ma `(SuggestionType, string contextKey)` jako kompozytowy klucz. Per-save persistence przez `SharedUISavable`.
- **Smoke test**: `SharedUISmokeTest` (7 testów × ~35 assertów) — `[ContextMenu]` "SharedUI: Run ALL smoke tests" w Inspector po AutoSpawn.

## Powiązane docs

- `docs/design/balance-constants.md` — gdzie żyją SharedUI constants (NumberFormat thresholds)
