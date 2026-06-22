# MainMenu — przewodnik

> Dokumentacja modułu `Assets/Scripts/MainMenu/`.

## Asmdef + warstwa

**`RailwayManager.MainMenu`** (rootNamespace: `RailwayManager.MainMenu`),
refs: `Core, SharedUI` (+ TMP + InputSystem).

⚠ **Heads-up:** `rootNamespace` mówi `RailwayManager.MainMenu`, ale **plików w środku
deklaruje namespace `MainMenu`** (legacy). Pomyłka w `using` to popularny błąd —
sprawdź konkretny plik.

Punkt wejścia gry. Scena `MainMenu.unity` — pierwsza po starcie executable.

**Konsumenci:** GameCreator i Depot.

## Co tu siedzi

| Plik | Po co |
|---|---|
| **`MainMenuUI.cs`** | Główny menu UI (Nowa gra/Wczytaj/Multiplayer/Settings/Help/Mods/Credits/Wyjdź). Procedural build — `BuildCanvas` w `Awake`. Trzyma referencje do screen UIs + listę `_screens` (`IMenuScreen`) dla iteracji w ReturnToMenu/HandleEscape/OnLocaleChanged. **Klasa pomocnicza `MenuScreenPrimitives`** (na końcu pliku) — publiczne `NewGO/MakeTMP/Fill/CreateTMP/CreateButton/CreateTopBar/BuildVerticalScrollArea/CreateFullscreenRoot`, single source dla wszystkich ekranów. |
| **`IMenuScreen.cs`** | Interfejs (`IsVisible`/`Show`/`Hide`/`RefreshLanguage`) implementowany przez 6 ekranów — kontrakt dla MainMenuUI żeby iterować po liście zamiast hardcodować per-screen. |
| **`LoadGameScreenUI.cs`** | Lista save slotów + delete + load. Czyta `SaveSlotSummary` z SaveLoad. |
| **`SettingsScreenUI.cs` + 4 partials** (`Sidebar`, `BottomBar`, `Sections`, `RowBuilders`) | Settings (audio/video/i18n/input rebinding). Apply Cancel/Apply pattern. Cancel discardujе lokalny `_working`; preview-applied rebindings revertowane przez snapshot JSON (rebindings idą do PlayerPrefs natychmiast, audio/video tylko przez explicit Apply). Dropdowny używają `_activeDropdowns` dla in-place refresh przy zmianie języka (zamiast pełnego repopulate sekcji). |
| **`RebindModalUI.cs`** | Modal do rebindowania klawiszy (M13). Listen for input action → save. |
| **`HelpScreenUI.cs` / `ModsScreenUI.cs` / `CreditsScreenUI.cs`** | Static screens. Mods to placeholder pre-EA (`CreateModRow` i `ModEntry` usunięte 2026-05-14, dorobić gdy mod loader wejdzie). |
| **`MultiplayerScreenUI.cs`** | M10 prototype UI — lobby, host/join. `MockServers` array + `OnJoinServer` no-op czekają na M10 Mirror. |
| **`GameCreatorContext.cs`** | **Statyczny cross-scene context** przekazany do sceny `GameCreator.unity`. Pola: `Mode` (SP/MP), `ServerName/MaxPlayers/Password/Visibility` (TD-022 placeholder dla M10 Mirror). |
| **`MenuButtonHover.cs`** | Hover animacja TMP labela menu (kolor tekstu). Różni się od `SharedUI.HoverImageColor` (kolor Image bg). |

## Cross-system glue

- **`GameCreatorContext`** — set w `MainMenuUI` (klik „Nowa gra" / „Multiplayer"), read w `GameCreatorUI.Start`. Mode SP/MP decyduje o zakładkach w kreatorze (SP: Ogólnie/Mapa/Rozgrywka, MP: Ogólnie/Mapa/Serwer).
- **`SettingsService`** (Core) ← MainMenu pisze; **`RebindingService`** (Core) ← MainMenu drives Action Rebinding flow.
- **`LoadGameScreenUI`** → SaveLoad asmdef (przez `SaveActionsHook` w Core, bo MainMenu nie referuje SaveLoad).
- **`PlayerProgressService`** (SharedUI) ← MainMenu read'uje tutorial hours dla Expert UI unlock display.

## Gotchas

- **Namespace asymetria**: `RailwayManager.MainMenu` w `.asmdef`, ale w plikach `namespace MainMenu`. Gdy dodajesz import — `using MainMenu;` z innych asmdef. Możesz spotkać `using RailwayManager.MainMenu;` (rzadko, dla nowszych). Sprawdź konkret.
- **Cancel asymetria w Settings:** audio/video/locale są applied **tylko przez explicit Apply** — kontrolki modyfikują lokalny `_working` bez side-effectu na `SettingsService`. Rebindings to wyjątek: idą do PlayerPrefs natychmiast (preview live), więc snapshot+restore JSON przy Cancel jest tylko dla nich.
- **Dodajesz nowy ekran?** Zaimplementuj `IMenuScreen` (`IsVisible`/`Show`/`Hide`/`RefreshLanguage`), w MainMenuUI dorzuć `BuildXxxScreen` + `_screens.Add(xxxScreen)`. Bez tego nowy ekran NIE zostanie ukryty przez ESC/ReturnToMenu i NIE dostanie i18n refreshu przy zmianie języka.
- **Procedural UI helpers** — używaj `MenuScreenPrimitives.NewGO/MakeTMP/Fill/CreateTMP/CreateButton/CreateTopBar/BuildVerticalScrollArea` zamiast pisać własne. Lokalne `private static` aliasy w klasach są tylko skrótami do unikania kwalifikacji nazwą — niе dodawaj nowych implementacji.
- **`MainMenuUI` proceduralny** — żadnych prefabów. Cały layout w `Awake` → `BuildCanvas` → row builders. **Idiom skopiowany przez `GameCreatorUI`** (partial × 11).
- **Hover komponenty**: TMP label color → `MenuButtonHover` (lokalny), Image bg color → `SharedUI.HoverImageColor`. Nie kopiuj `SaveRowHover`/`ServerRowHover` — te zostały zkonsolidowane.
- **Workshop / Steam URL pattern**: pre-launch URL z sentinelem `APPID` powoduje że button.interactable=false (`ModsScreenUI.IsWorkshopUrlReady`). Po podmianie APPID w M14 button sam się odblokuje. Idiom do skopiowania dla podobnych pre-Steam features.
- **`GameCreatorContext.ResetServerConfig()`** wywołać gdy gracz wraca do MainMenu z kreatora MP — stałe stale state.
- **`MultiplayerScreenUI` stub** — M10 Mirror integration jeszcze nie zaczęta. `MockServers` + `OnJoinServer` są oznaczone `TODO (M10 Mirror)`. Można dodać UI ale logika lobby/host/join czeka na M10.
- **Scena MainMenu.unity** używa `MapScene` additive load od razu po Start? **NIE** — Map ładowany dopiero po wejściu do Depot scene. `MainMenu` jest sam.
- **i18n keys**: dodając nowy `LocalizationService.Get("klucz")` PAMIĘTAJ dodać wpis do `Assets/Resources/Locale/{pl,en,de,cz}/strings.json` — inaczej UI pokaże `[klucz]` jako placeholder. Klucze NIE mogą zawierać kropek w segmentach gdy parent jest stringiem (FlattenJObject składa key'em `parent.child`, więc segment z kropką da konflikt).
