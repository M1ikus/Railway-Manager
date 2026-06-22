# Release runbook

> Cel: powtarzalna procedura robienia buildow Railway Manager na Windows,
> przygotowania paczki QA oraz publikacji na Steam beta/default branch.
>
> Zweryfikowano 2026-05-29 na podstawie lokalnego projektu i aktualnych
> oficjalnych dokumentow Unity/Steamworks.

---

## Aktualny stan projektu

| Obszar | Stan |
|---|---|
| Unity Editor | `6000.4.0f1` z `ProjectSettings/ProjectVersion.txt` |
| Product name | `Railway Manager` |
| Company name | `RailwayManager` |
| `Application.version` | `ProjectSettings.asset` `bundleVersion`, obecnie `1.0` |
| Target EA | Windows x64 / Steam |
| Build script | Brak wlasnego `BuildPipeline`/`BuildScripts` w `Assets/Editor` |
| Build output | `Build/` i `Builds/` sa ignorowane przez git |
| Public data | OSM-derived `.bin` sa ignorowane przez git, ale moga wejsc do publicznego builda zgodnie z `docs/DATA_LICENSES.md` |

Aktywne sceny w `ProjectSettings/EditorBuildSettings.asset`:

1. `Assets/Scenes/MainMenu.unity`
2. `Assets/Scenes/Depot.unity`
3. `Assets/Scenes/GameCreator.unity`
4. `Assets/Scenes/MapScene.unity`

Wylaczone stale wpisy:

- `Assets/Scenes/LoadingScene.unity` - plik nie istnieje.
- `Assets/Scenes/GameplaySingleplayer.unity` - plik nie istnieje.

Nie wlaczac tych scen bez dodania realnych plikow.

---

## Zrodla zewnetrzne

- Unity: command line build wymaga `-projectPath` i `-quit`; zalecane sa
  `-batchmode`, `-logFile` oraz jawny `-buildTarget` albo build profile.
- Unity: dla Windows standalone build path musi konczyc sie `.exe`.
- Steamworks: build Steam to upload przez SteamPipe/SteamCMD do jednego lub
  wielu depotow; po uploadzie build ustawia sie live z App Admin.
- Steamworks: build testowy powinien najpierw trafic na beta branch; branch
  prywatny powinien miec haslo ustawione przed ustawieniem buildu live.

Linki:

- <https://docs.unity3d.com/kr/current/Manual/build-command-line.html>
- <https://docs.unity3d.com/kr/6000.0/Manual/build-path-requirements.html>
- <https://partner.steamgames.com/doc/sdk/uploading?language=english>
- <https://partner.steamgames.com/doc/store/application/builds>

---

## Kanaly release

| Kanal | Dla kogo | Cel | Gdzie trafia |
|---|---|---|---|
| Local dev build | developer | szybki smoke poza Editorem | `Builds/Local/` |
| Internal QA | ty + zaufani testerzy | pelny regression pass | zip/paczka prywatna |
| Steam beta branch | testerzy Steam | test instalacji, patchingu, branchy | Steam beta branch, najlepiej z haslem |
| Steam default branch | public | realny release/update | Steam default branch |

Default branch traktowac jak produkcje. Po premierze Steam wymaga dodatkowej
autoryzacji dla update'u default branch; nie robic tego jako ostatniego kroku
bez checklisty.

---

## Przed buildem

### Git i wersja

1. `git status --short`
2. Potwierdz, ze w zmianach sa tylko rzeczy przeznaczone do release.
3. Zaktualizuj `ProjectSettings/ProjectSettings.asset` `bundleVersion`, bo to
   zasila `Application.version`, save metadata, MainMenu i Credits.
4. Ustal numer:
   - dev: `0.2.0-dev.N`
   - QA: `0.2.0-qa.N`
   - Steam beta: `0.2.0-beta.N`
   - EA public: `0.2.0-ea`
5. Po buildzie oznacz commit tagiem, np. `v0.2.0-qa.3`.

### Dane i licencje

1. Sprawdz `Assets/StreamingAssets/Maps/Poland/`:
   - `warminsko-mazurskie-v7.bin`
   - `poland-v7.bin`
   - `init-state-pl.bin`
2. Jezeli build publiczny zawiera OSM-derived `.bin`, wykonaj checklist z
   `docs/DATA_LICENSES.md`.
3. Sprawdz `THIRD_PARTY_NOTICES.md` po kazdym dodaniu asset packa, fontu,
   plugina albo ikon z zewnetrznego zrodla.
4. Upewnij sie, ze w publicznej paczce nie ma prywatnych plikow roboczych,
   credentiali Steam, source PSD/BLEND od partnerow, ani niepotrzebnych paczek
   Asset Store.

### Compile gate

Minimalnie przed buildem:

```powershell
dotnet restore RM-0.2.slnx
dotnet build RM-0.2.slnx --no-restore
```

Potem otworz projekt w Unity i upewnij sie, ze Console nie ma compile errors.

Zakres regresji dobierz z `docs/QA_REGRESSION_MATRIX.md`. Dla release candidate
minimum to Tier 0, Tier 1, Tier 2 oraz wszystkie dotkniete obszary z Tier 3.

---

## Lokalny build Windows

### Manualnie w Unity

1. Otworz projekt w Unity `6000.4.0f1`.
2. `File > Build Profiles` albo `File > Build Settings`.
3. Target: Windows / x86_64.
4. Scenes in Build: tylko aktywne sceny z listy w tym dokumencie.
5. Development Build:
   - `On` dla Local dev/Internal QA.
   - `Off` dla Steam beta/default, chyba ze to swiadomy debug build.
6. Build path:
   - `Builds/Local/RailwayManager/Railway Manager.exe`
   - albo `Builds/SteamBeta/RailwayManager/Railway Manager.exe`

### Command line

Unity 6 zaleca jawnie podac `-buildTarget` albo `-activeBuildProfile`. Poniewaz
projekt nie ma jeszcze trackowanego build profile ani custom build scriptu,
bezpieczny wariant to build target + `-build`.

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.4.0f1\Editor\Unity.exe"
$project = "D:\Gry\RM-0.2"
$out = "D:\Gry\RM-0.2\Builds\Local\RailwayManager\Railway Manager.exe"
$log = "D:\Gry\RM-0.2\Logs\unity-build-windows.log"

& $unity `
  -projectPath $project `
  -quit `
  -batchmode `
  -buildTarget StandaloneWindows64 `
  -build $out `
  -logFile $log
```

Jezeli kiedys powstanie `Assets/Editor/BuildScripts.cs`, uzyj
`-executeMethod BuildScripts.BuildWindows64`, ale nadal podawaj
`-buildTarget StandaloneWindows64` w tej samej komendzie. Nie polegaj na zmianie
targetu wewnatrz skryptu batchmode.

---

## Smoke po buildzie

Testowac build `.exe`, nie tylko Unity Editor.

1. Uruchom `Railway Manager.exe`.
2. MainMenu pokazuje wersje z `Application.version`.
3. New Game -> GameCreator -> Depot.
4. Depot laduje sie bez czarnego ekranu.
5. Build track: postaw prosty tor, cofnij undo, postaw ponownie.
6. Fleet panel: otworz rynek/flote, kup lub skonfiguruj pojazd testowy.
7. MapScene: wejdz na mape, sprawdz czy `init-state-pl.bin` lub fallback map
   load dziala.
8. Timetable: utworz prosty rozklad warm-maz.
9. Save/load: zapisz, wyjdz do menu, wczytaj.
10. Zamknij gre i sprawdz logi/persistent data.

Logi i save'y sa pod `Application.persistentDataPath`; katalogi projektu:

- `Saves`
- `Logs`
- `CustomFurniture`
- `CustomSchemas`

---

## Pakowanie QA

1. Zatrzymaj gre.
2. Sprawdz, ze w folderze builda sa:
   - `Railway Manager.exe`
   - `Railway Manager_Data/`
   - pliki Unity runtime wymagane przez build
   - `StreamingAssets/` wewnatrz Data, z katalogami Fleet/Maps/etc.
3. Wygeneruj checksum:

```powershell
Get-FileHash "Builds\SteamBeta\RailwayManager\Railway Manager.exe" -Algorithm SHA256
```

4. Spakuj caly folder builda, nie tylko `.exe`.
5. Nazwa paczki:
   - `RailwayManager_0.2.0-qa.3_win64.zip`
6. Do notatek release wpisz:
   - commit SHA
   - branch
   - Unity version
   - build channel
   - najwazniejsze zmiany
   - znane problemy
   - checksum

---

## Steam upload

Steamworks SDK i SteamCMD nie powinny byc commitowane do repo.

Proponowana lokalna struktura prywatna:

```text
D:\Tools\steamworks_sdk\
  tools\ContentBuilder\
    builder\steamcmd.exe
    content\railway-manager\
    output\
    scripts\
      app_build_<APPID>.vdf
      depot_build_<DEPOTID>.vdf
```

Przygotowanie:

1. Skopiuj caly build Windows do `tools/ContentBuilder/content/railway-manager/`.
2. W `app_build_<APPID>.vdf` ustaw `ContentRoot` na ten katalog.
3. W `depot_build_<DEPOTID>.vdf` mapuj caly folder rekurencyjnie do root depotu.
4. Najpierw zrob `Preview "1"` albo upload na beta branch.
5. Dla prywatnej bety ustaw haslo branch przed ustawieniem builda live.

Przyklad uruchomienia:

```powershell
cd D:\Tools\steamworks_sdk\tools\ContentBuilder\builder
.\steamcmd.exe +login <steam_builder_user> +run_app_build ..\scripts\app_build_<APPID>.vdf +quit
```

Po uploadzie:

1. Otworz `https://partner.steamgames.com/apps/builds/<APPID>`.
2. Ustaw build live na beta branch.
3. Zainstaluj gre z tej galezi w Steam Client.
4. Powtorz smoke po instalacji Steam, bo to testuje realny depot layout.
5. Dopiero po QA przenies build na default branch.

---

## Release notes template

```md
# Railway Manager <version> - <channel>

Commit: <sha>
Unity: 6000.4.0f1
Build: Windows x64
Data bundle: <warminsko / full PL / both>

## Highlights
- ...

## Fixes
- ...

## Known issues
- ...

## QA
- dotnet build: pass/fail
- Unity build: pass/fail
- Smoke: pass/fail
- Save/load: pass/fail
- Performance: pass/fail
```

---

## Blockery release

Nie publikowac buildu, jezeli:

- `dotnet build` albo Unity compile ma error.
- Aktywna scena w build settings wskazuje nieistniejacy plik.
- MainMenu/New Game/Depot/MapScene nie przechodza smoke.
- Save/load potrafi skorumpowac save albo zgubic wersje.
- Publiczny build zawiera OSM `.bin`, ale nie wykonano checklist z
  `docs/DATA_LICENSES.md`.
- W paczce sa credentiale, prywatne source assety, lokalne AI/workspace configi
  albo pliki, ktore powinny zostac local-only.
- Nie ma release notes z commit SHA i numerem wersji.
