# M13 — Settings + Save/Load

> **Status:** Plan (pre-implementation), 2026-04-26
> **Zależności:** Wszystkie milestone'y M0-M9, M-PL ✅ (każdy moduł musi dostarczyć `ISavable` impl). Mechanizm rebindowania zależy od `InputSystem_Actions.inputactions` (już używany).
> **Niezależny od:** M-Models, M10 Multiplayer, M14 Beta (Steam Cloud delegowane do M14 ale interfejs gotowy).
> **Cel:** Pełne menu ustawień (grafika/audio/gameplay/sterowanie/język/interfejs), framework lokalizacji dla 7 języków (5 z contentem na EA, 2 puste fallback), system save/load z modularną architekturą bundle'a + per-module schema versioning + migrator chain.

---

## 1. Filozofia

M13 to **infrastruktura, nie content**. Trzy podsystemy:

1. **Settings** — must-have dla EA. Bez tego gracz nie zmieni rozdzielczości / głośności / klawiszy
2. **i18n** — must-have dla EA jeśli celujemy w więcej niż polski rynek. Framework w M13, content rolloutuje się w trakcie M13 (PL+EN+DE+CZ na EA, JP na infrastruktura ale bez treści, RU/UK puste fallback)
3. **Save/Load** — **blocker dla M6.5 Rebalance i M-Balance**. Bez save'a każda iteracja balansu wymaga restartu gry (decyzja 2026-04-19 → przesunięcie M6.5 z post-M8 na post-M13). Bez save'a gracz traci grę przy każdym ALT+F4 — niedopuszczalne dla EA.

**Kluczowe zasady:**

- **Modular save** — pojedynczy `.rmsave` na dysku (atomowy zapis), modułowa struktura w środku (każdy moduł osobny JSON + osobny `schemaVersion` + osobny migrator chain). Failed deserialize jednego modułu = warning + load reszty (graceful degradation), nie hard-fail całego save'a
- **Pre-EA aggressive, post-EA strict** — w pre-EA dążymy do back-compat, ale dopuszczamy breaking changes z komunikatem "save z wersji X niekompatybilny — alpha". Od EA day 1 strict back-compat wymagany
- **i18n od dnia 1 w infrastrukturze, content sukcesywnie** — wszystkie 7 języków mają zarezerwowane sloty resource files od początku. Brak treści = fallback chain (selectedLang → EN → key)
- **Steam Cloud przygotowane, nie zintegrowane** — `ISaveStorage` interfejs z `LocalDiskStorage` (M13) i `SteamCloudStorage` (M14 stub). Switch w M14 to wymiana implementacji, nie refactor architektury
- **Save jest readable** — JSON + gzip (czytelny po dekompresji). HMAC chroni przed modyfikacją (gracz może otworzyć i zobaczyć, ale modyfikacja invalidates podpis → ostrzeżenie "modified save"). Kompatybilne z MP — host weryfikuje podpisy gości

---

## 2. Rozstrzygnięte decyzje

| ID | Pytanie | Decyzja |
|---|---|---|
| **D1** | Format save | **JSON + gzip + HMAC**. Czytelny po dekompresji, mały plik, anti-tamper przez HMAC. (rozstrzygnięte 2026-04-14) |
| **D2** | Storage filesystem | **Pojedynczy `.rmsave` (gzipped tarball)** na dysku, modułowa struktura w środku (osobne JSONy per moduł + manifest). Atomowy zapis + share-friendly + Steam Cloud-friendly. (2026-04-26) |
| **D3** | Per-module versioning | **Każdy moduł ma własny `schemaVersion`** + własny migrator chain. Migracja modularna — `PersonnelMigrator v3→v4` nie rusza Fleet/Economy. (2026-04-26) |
| **D4** | Pre-EA migration policy | **Best-effort kompatybilność wsteczna jako cel architektury**, ale dopuszczalne breaking changes w pre-EA z komunikatem "save z wersji X niekompatybilny — alpha". Od EA day 1 strict back-compat wymagany. (2026-04-26) |
| **D5** | i18n framework | **Własny rollout** (nie Unity Localization Package — cięższy niż potrzebujemy, dependency niewarte feature setu). Resource files JSON pod `Resources/Locale/{lang}/strings.json` + `LocalizedText` MonoBehaviour. (2026-04-26) |
| **D6** | Języki na EA day 1 | **PL, EN, DE, CZ** (4 języki z contentem). Profesjonalne sign-off przed launch. (2026-04-26) |
| **D7** | Języki w infrastrukturze | **+ JP, RU, UK** (3 dodatkowe — puste resource files z fallbackiem do EN). Font atlas dla JP gotowy do podmiany content'em. JP: dodanie content'u w M14 jeśli znajdzie się tłumacz, w przeciwnym razie post-EA. RU/UK: wprowadzenie po EA gdy społeczność zainteresowana. (2026-04-26) |
| **D8** | Tłumaczenie — pipeline | **Hybrydowe**: ogólniki (UI generyczne, error messages, button labels) — ja (Claude) z machine translation review. PKP-specific (kategorie IRJ, terminologia kolejowa, regulaminy) — user szuka konsultantów per język. DE/CZ/JP dodatkowo wymaga lokalnej terminologii Bahn/ČD/JR. (2026-04-26) |
| **D9** | Default language | **`SteamApps.GetCurrentGameLanguage()`** jako pierwszy wybór. Fallback na `CultureInfo.CurrentCulture` gdy Steam offline / dev mode. Zawsze manual override w settings. (2026-04-26) |
| **D10** | Lokalizacja on-the-fly | **TAK, on-the-fly z `OnLanguageChanged` event**. Wszystkie `LocalizedText` subscribe i re-render. Bez restartu gry. (2026-04-26) |
| **D11** | UI scale | **100% only na EA** + info "więcej opcji w późniejszych wersjach". Pełen slider 50-150% post-launch (M12d / post-1.0). Powód: solo dev nie ma pasma na QA pełnego layoutu na 5+ wartościach scale'a. (2026-04-26) |
| **D12** | Steam Cloud | **Implementacja w M14**, ale `ISaveStorage` interfejs z `LocalDiskStorage` + `SteamCloudStorage` (stub) gotowy w M13. Switch w M14 = wymiana impl, nie refactor. (2026-04-26) |
| **D13** | Save w MP | **Tylko host zapisuje**. Klienci nie save'ują indywidualnie. Rozłączenie hosta = gracze tracą progress (lub host migration post-launch). (rozstrzygnięte wcześniej, OPEN_QUESTIONS) |
| **D14** | HMAC | **HMAC podpis na całość bundle'a** (manifest sumuje hashe modułów). Save jest readable, modyfikacja invalidates → warning "modified save". MP: host weryfikuje. (2026-04-14) |
| **D15** | Sloty | **10 manualnych** (gracz nazywa, zarządza) + **5 rotujących auto-save** (osobna sekcja UI). |
| **D16** | Auto-save cadence | **Co 5 min game time** + Quick-save F5 + Quick-load F9. Throttling: nie zapisuj gdy gracz w menu / mid-modal (czeka do unblock). |
| **D17** | "Continue" w MainMenu | **Najnowszy slot** (manual lub auto, whatever recent — porównanie po `lastModified`). Brak save'ów = przycisk wyszarzony. |
| **D18** | Save przy zamknięciu | **Best-effort** przez `Application.wantsToQuit` callback — próbuje zapisać, ale nie blokuje exit'u jeśli błąd (gracz nie ma czekać 30s na ALT+F4). To jest **dodatkowy** auto-save, nie zastępuje cyklicznego. |
| **D19** | Asmdef | **Settings → Core** (już tam jest `Log`, podobny scope). **Save/Load → nowy `RailwayManager.SaveLoad.asmdef`** z ref do wszystkich pozostałych asmdef (musi widzieć wszystkie ISavable impl). **i18n → SharedUI** (text rendering jest UI concern). |
| **D20** | Save bundle structure | **Manifest + module sections** (patrz §5.1). Manifest zawiera: `gameVersion`, `bundleSchemaVersion`, `playtime`, `gameTimeIso`, `moduleVersions: {fleet: 1, timetable: 1, ...}`, `hmac`. Każdy moduł = osobny `<module>.json` w bundle. |
| **D21** | ISavable kontrakt | **`ISavable<T>` z `int SchemaVersion`, `T Serialize()`, `void Deserialize(T data, int sourceVersion)`**. T = POCO data class per moduł (np. `FleetSaveData`). Migration triggers przed Deserialize jeśli `sourceVersion < SchemaVersion`. |
| **D22** | Failed module load | **Graceful degradation** — module fails → `Log.Warning("[SaveLoad] Module 'personnel' failed to load (v3→v4 migration error). Initialized to default state.")` → reszta save'a ładuje się normalnie. Gracz dostaje toast z listą failed modules. |
| **D23** | Settings persistence | **PlayerPrefs** (proste settings: liczby, stringi, bool) + binary blob dla rebinding overrides. Wbudowane Unity, zero ceremonii. |
| **D24** | Rebindowanie API | **`InputActionRebindingExtensions`** standard Unity (`SaveBindingOverridesAsJson` / `LoadBindingOverridesFromJson`). Persistence w PlayerPrefs jako JSON string. |
| **D25** | Migration policy — failed | **Try-load-anyway-with-warning** (D22 graceful) — nie hard-fail. Jeśli migrator throwa exception → moduł init default state + log warning + toast graczowi. Jeśli **gameVersion** w save jest **wyższy** niż gra (downgrade) → hard-fail z komunikatem "Save z nowszej wersji gry, zaktualizuj". |
| **D26** | Save plików — folder | **`%USERPROFILE%/Documents/Railway Manager/Saves/`** (Windows). MacOS/Linux post-launch jeśli port. Steam Cloud sync z tego folderu. |
| **D27** | Quick-save w pauzie | **TAK, F5 zawsze działa** (nawet w pauzie / menu). Quick-load F9 → confirmation dialog "Stracisz niezapisany progress, OK?". |
| **D28** | Screenshot w slot info | **TAK, opcjonalny** — render do RT przed save, zapis jako `screenshot.png` w bundle. ~50KB per save, daje wizualny preview w slot list. Pominięty gdy auto-save (perf), tylko manualne. |
| **D29** | Save w trakcie kursu | **TAK** — `TrainRunSimulator` runtime state (pozycje, prędkości, opóźnienia, occupied blocks) serializowany jako część `runtime.json`. Restore dokładny — pociąg kontynuuje z miejsca zapisu. Cross-sub M9 TBD rozstrzygnięty (2026-04-26). |
| **D30** | Save w trakcie manewru | **TAK** — `DepotMovementSimulator` task queue + bieżący ruch serializowany. Restore wznawia manewr. Edge case: instant-resolve gdy gracz wraca do Depot scene. |
| **D31** | Compression level | **gzip default level (6)** — kompromis czasu kompresji vs rozmiaru. Tuning post-EA jeśli load time problem. |
| **D32** | Save bundle rozmiar — target | **<5 MB dla mid-game (1 zajezdnia, 30 pojazdów, 50 rozkładów, 10 obiegów, 50 pracowników)**, **<20 MB dla late-game (multi-depot, 200 pojazdów, 200 rozkładów, 50 obiegów, 200 pracowników)**. Monitoring w M-Balance. |
| **D33** | Trudność — gdzie wybierana | **Per-save w GameCreator (kreator nowej gry), nie w runtime Settings**. Niemodyfikowalna mid-game. Persystowana w `world.json` save bundle (`difficulty` field). Powód: trudność wpływa na balans całej rozgrywki — zmiana mid-game zafałszowałaby statystyki i Steam achievements. Cheaty / sandbox mode → POST-EA. (2026-04-26) |
| **D34** | Settings — liczba zakładek | **5 zakładek:** Sterowanie / Grafika / Dźwięk / **Język** / Ogólne. "Ogólne" zawiera 2 podsekcje (heading + separator): Rozgrywka / Interfejs. Język wydzielony jako osobna zakładka — ważna decyzja gracza, prostsza nawigacja (standard w grach typu Cities Skylines: Localization tab). FPS limit przeniesione z Grafiki do Ogólne→Rozgrywka (perf/feel, nie estetyka). (2026-04-26 — zmiana z 4 na 5) |
| **D35** | Trudność — presety + custom editor | **4 presety + Custom editor** w GameCreator. Presety: **Łatwy / Normalny / Trudny / Realistyczny** (gotowe zestawy modifierów). **Custom**: edytor z sliderami per parametr — gdy gracz modyfikuje slider, label presetu zmienia się na "Custom". Zakres modifierów (initial, M-Balance tunning): start budget multiplier, operational cost multiplier, breakdown chance multiplier, passenger demand multiplier, salary multiplier, subsidy multiplier, delay propagation factor, random event frequency, hotel cost multiplier, ticket price tolerance. Persystencja: `world.json` zawiera `difficulty: {preset: "Custom" \| "Easy" \| ..., modifiers: {...}}`. Save/Load named custom preset → POST-EA quality of life. (2026-04-26) |
| **D36** | Per-save game rules toggle'i | **Koncepcja:** osobno od difficulty modifierów (D35) GameCreator ma sekcję **"Reguły gry"** — toggle'i tak/nie konfigurowane przy starcie nowej gry, niemodyfikowalne mid-game (jak GameRules w Cities Skylines / OpenTTD). Przykłady: "Awarie taboru (M7) on/off", "Zarządzanie personelem (M8) on/off / auto", "Dotacje wojewódzkie on/off", "Losowe zdarzenia on/off master toggle", "Konkurencja AI on/off" (post-launch), "Ograniczenia historyczne", "Strajki on/off" (post-1.0). **Scope w M13-13: tylko infrastruktura** (extensible pattern: enum `GameRule`, `GameRulesConfig` POCO, registration mechanism dla modułów żeby query `GameRulesService.IsEnabled(GameRule.VehicleBreakdowns)`). **Konkretna lista toggle'i + per-toggle behavior (jak moduł reaguje na "wyłączony") = M-Balance** (bo wymaga playtestingu czy "casual mode bez awarii" jest wystarczająco grywalny). (2026-04-26) |
| **D37** | Loading Scene | **Pre-EA:** minimal `LoadingScreen.unity` z progress barem 0-100% + rotating tip text (placeholder lista 5-10 hardcoded). Bez artwork, bez animacji. `LoadingScreenManager` (singleton) z `LoadSceneAsync(sceneName, onProgress, onComplete)` API. Hook do MainMenu (New Game / Continue), SaveLoadUI (Load), GameCreator (Start Game). Pre-EA scope to "nie pokazuj black screen przy długich operacjach". **Pełen polish (artwork backgrounds per scene type, animations np. lokomotywa po pasku, async resource pre-loading, pełna baza 50+ tipów z M11/M12) → M12c Visual.** Scene switch Depot↔MapScene zostaje instant (additive scenes + CanvasGroup toggle, ~instant — overkill loading screen). Decyzja 2026-04-26: jako podetap M13 (M13-10), renumeracja M13-10..13 → M13-11..14. (2026-04-26) |

---

## 3. Settings — zakres

**5 zakładek** w lewym tabbar'ze (decyzja 2026-04-26): **Sterowanie / Grafika / Dźwięk / Język / Ogólne**. Język wydzielony jako osobna zakładka (ważna decyzja gracza, prostsza nawigacja). "Ogólne" zawiera 2 podsekcje: Rozgrywka / Interfejs. FPS limit w Ogólne→Rozgrywka (perf/feel, nie estetyka).

### 3.1 Sterowanie (rebinding)

- **Lista action map'ów** — Camera / TimeControl / UI / Tools (Depot)
- **Per action: current binding + przycisk "Zmień..."** → modal "Press a key..." → save override do PlayerPrefs
- **Conflict detection** — gdy próbujesz przypisać klawisz już używany → ostrzeżenie + opcja swap
- **Czułość myszy** — slider 0.5x - 2.0x (osobno camera vs UI scroll)
- **Inwersja osi Y** — toggle (kamera orbit Depot)
- **Reset do domyślnych** — przycisk per-action + global "Reset all"

### 3.2 Grafika

- **Rozdzielczość** — dropdown z `Screen.resolutions` (filtered to 16:9 + 16:10 + 21:9 ultrawide)
- **Tryb okna** — Fullscreen / Borderless Window / Windowed
- **VSync** — Off / On / Half (60→30)
- **Preset jakości** — Low / Medium / High / Ultra (zmienia poniższe en masse)
- **Cienie** — Off / Low / Medium / High
- **Tekstury** — Low / Medium / High / Ultra (mip bias)
- **Odległość LOD** — slider 0.5x - 2.0x (mnożnik LOD bias)
- **Antialiasing** — Off / FXAA / SMAA / TAA
- **Bloom + post-process** — toggle *(dostępne na URP (Universal Render Pipeline 17.4, aktywny od 2026-06-17 / M-URP) przez Volume override; implementacja TODO M12 — sama migracja dostarczyła pipeline + materiały (URP/Lit, URP/Unlit przez `MaterialFactory`), nie features post-processingu. Flaga `SettingsData.PostProcessing` nadal no-op do czasu M12 — patrz `depot-visual-direction.md`)*

> **Limit FPS** — przeniesione do Ogólne→Rozgrywka (D34, decyzja 2026-04-26): FPS to bardziej "feel/performance" niż estetyka.

### 3.3 Dźwięk

- **Master volume** — slider 0-100%
- **Music volume** — slider 0-100%
- **SFX volume** — slider 0-100%
- **Voice/announcements volume** — slider 0-100% (placeholder na EA, pełen system M12b)
- **Wycisz w tle** — toggle (gdy `Application.isFocused == false`)

### 3.4 Język

Osobna zakładka (D34, decyzja 2026-04-26 — wydzielone z "Ogólne"):

- **Dropdown** z 7 języków (PL/EN/DE/CZ/JP + RU/UK puste fallback z labelem "(beta — partial translation)")
- **Default** = `SteamApps.GetCurrentGameLanguage()` mapped na nasze locale codes, fallback `CultureInfo.CurrentCulture`
- **Na zmianie** → emit `OnLanguageChanged` → wszystkie `LocalizedText` re-render
- **Info pod dropdown'em** — "Tłumaczenia DE/CZ przygotowane, JP w toku, RU/UK community-driven (post-EA)"

### 3.5 Ogólne

Dwie podsekcje (heading + separator wewnątrz zakładki, scrollable jeśli urośnie):

#### 3.5.1 Rozgrywka

- **Domyślna prędkość czasu** — x1 / x5 / x25 / x150 / x500
- **Limit FPS** — Off / 30 / 60 / 120 / 144 / Unlimited (przeniesione z Grafiki — D34)
- **Auto-pauza przy zdarzeniach** — toggle per kategoria: awaria pociągu / awaria infrastruktury / krytyczna decyzja / strajk (placeholder dla post-1.0) / kolizja
- **Pokaż tutorial przy nowej grze** — toggle
- **Częstotliwość losowych zdarzeń** — Off / Rzadko / Normalnie / Często (placeholder na EA, system M12d)
- **Powiadomienia** — toggle per typ: opóźnienia / pieniądze / personel / inne

> **Trudność NIE jest w Settings** (D33). Wybierana per-save w **GameCreator** (kreator nowej gry), niemodyfikowalna mid-game. Powód: trudność wpływa na balans całej rozgrywki (start budget, koszty, częstość awarii, popyt) — zmiana mid-game zafałszowałaby achievementy i statystyki.

#### 3.5.2 Interfejs

- **UI scale** — single value 100% (info "więcej opcji wkrótce")
- **Tooltips delay** — slider 0-2s
- **Pokaż skróty klawiszowe w tooltips** — toggle
- **Color-blind mode** — Off / Protanopia / Deuteranopia / Tritanopia (placeholder na EA, pełen rollout post-launch)

---

## 4. Lokalizacja (i18n)

### 4.1 Architektura

```
RailwayManager.SharedUI/
├── Localization/
│   ├── LocalizationService.cs       static singleton
│   ├── LocalizedText.cs              MonoBehaviour (TMP wrapper)
│   ├── LocaleCode.cs                 enum: PL, EN, DE, CZ, JP, RU, UK
│   ├── LocaleResolver.cs             Steam → CultureInfo → fallback chain
│   └── NumberFormatService.cs        currency/date/number per locale
└── Resources/
    └── Locale/
        ├── pl/strings.json
        ├── en/strings.json
        ├── de/strings.json
        ├── cz/strings.json
        ├── jp/strings.json
        ├── ru/strings.json (empty initially)
        └── uk/strings.json (empty initially)
```

### 4.2 Resource file format

Hierarchiczne klucze, JSON nested:

```json
{
  "main_menu": {
    "title": "Railway Manager",
    "buttons": {
      "new_game": "Nowa gra",
      "continue": "Kontynuuj",
      "load": "Wczytaj",
      "settings": "Ustawienia",
      "quit": "Wyjdź"
    }
  },
  "fleet": {
    "panel": {
      "title": "Mój tabor",
      "tabs": {
        "my_fleet": "Mój tabor",
        "market": "Rynek",
        "configurator": "Konfigurator",
        "cart": "Koszyk"
      }
    }
  }
}
```

Klucz lookup: `LocalizationService.Get("fleet.panel.tabs.my_fleet")`.

### 4.3 LocalizedText component

```csharp
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    [SerializeField] string key;
    [SerializeField] bool autoUpdate = true; // re-render on language change

    void OnEnable() {
        Refresh();
        if (autoUpdate) LocalizationService.OnLanguageChanged += Refresh;
    }
    void OnDisable() {
        LocalizationService.OnLanguageChanged -= Refresh;
    }
    void Refresh() {
        GetComponent<TMP_Text>().text = LocalizationService.Get(key);
    }
}
```

Plus wariant `LocalizedTextFormatted` dla string interpolation z params (`"Pociąg {0} opóźniony o {1} min"`).

### 4.4 Font atlases

- **Noto Sans** — Latin Extended + Cyrillic. Pokrywa PL/EN/DE/CZ/RU/UK
- **Noto Sans JP** — CJK Joyo + Hiragana + Katakana. Wymaga osobnego atlasu (CJK to ~3000 znaków często używanych, dynamic font atlas runtime gen, +20-30 MB build size)
- **Fallback:** TMP automatycznie sięga po fallback font gdy znak nie istnieje w głównym
- **Decyzja:** Noto Sans default, Noto Sans JP w fallback chain. Dla JP locale → Noto Sans JP jako primary

### 4.5 Number/date/currency formatting

```csharp
public static class NumberFormatService {
    public static string FormatCurrency(decimal amount, LocaleCode locale = default);
    public static string FormatDate(DateTime date, LocaleCode locale = default);
    public static string FormatNumber(decimal n, int decimals, LocaleCode locale = default);
}
```

Implementacja przez `CultureInfo`:
- PL → `pl-PL` → `1 234,56 zł`
- EN → `en-US` → `1,234.56 PLN` (waluta zawsze PLN bo gra w polskich realiach)
- DE → `de-DE` → `1.234,56 €` (NIE konwertujemy walut — tylko formatting; wszystko w PLN)
- JP → `ja-JP` → `1,234.56 円` (znak yen zostaje, kwota w PLN — to jest sim, nie kursy walut)

**Notka:** waluta w grze ZAWSZE PLN. Lokalizujemy tylko **formatting** (separatory, znak waluty), nie konwersję kursów. Gracz JP widzi `1,234円` ale to znaczy "1234 PLN sformatowane po japońsku".

### 4.6 Text expansion handling

Każdy nowy tekst UI musi:
1. Mieć **content fit** lub min-width równe ~1.4× długości EN tekstu (DE/RU/UK często +30-40%)
2. Klucz w resource file z `comment` field opisujący kontekst (dla tłumaczy)
3. Test na **najdłuższym języku** (zwykle DE) przed merge

Refactor istniejących UI w **M13-4** (rollout PL+EN) przejdzie wszystkie panele z audytem layout'u.

---

## 5. Save/Load — architektura

### 5.1 Bundle structure

`save_001.rmsave` (gzipped tarball):

```
save_001.rmsave (gzip)
├── manifest.json          ← gameVersion, bundleSchemaVersion, playtime, gameTimeIso,
│                            moduleVersions: {fleet: 1, timetable: 1, ...}, hmac
├── world.json             ← gameTime, seed, trudność, weather/season state
├── fleet.json             ← FleetService state (vehicles, assignments, cart)
├── timetable.json         ← Timetable + Routes + Categories
├── circulations.json      ← Circulations + per-day vehicle assignments + TrainRuns
├── runtime.json           ← active TrainRuns (positions, speeds, delays, occupied blocks),
│                            DepotMovementSimulator queue, current manewry
├── economy.json           ← finanse, OD matrix state, reputation per voivodeship
├── maintenance.json       ← komponenty per pojazd, parts inventory, workshop slots,
│                            active breakdowns, in-progress rescues
├── personnel.json         ← pracownicy, turnusy, morale, fatigue, shift state
├── depot_3d.json          ← infrastruktura zajezdni (tory, sieć trakcyjna, budynki, ścieżki, rooms)
├── stats.json             ← playtime, achievements progress, history (per-line balance)
└── screenshot.png         ← optional, ~50 KB (manualny save only, auto-save skip dla perf)
```

### 5.2 Manifest

```json
{
  "gameVersion": "0.13.0-alpha",
  "bundleSchemaVersion": 1,
  "playtime": 18345.6,
  "gameTimeIso": "2027-03-15T14:23:00",
  "savedAt": "2026-12-23T20:15:43Z",
  "saveType": "manual",
  "slotName": "Przed reformą rozkładu",
  "moduleVersions": {
    "world": 1,
    "fleet": 1,
    "timetable": 1,
    "circulations": 1,
    "runtime": 1,
    "economy": 1,
    "maintenance": 1,
    "personnel": 1,
    "depot_3d": 1,
    "stats": 1
  },
  "hmac": "a1b2c3d4..."
}
```

`hmac` = SHA256(secret_key + concat(file_hashes_sorted_by_name)). Modyfikacja jakiegokolwiek pliku w bundle → hash mismatch → warning "modified save". Secret key hardcoded w build (nie chroni w 100% — anti-cheat best-effort, nie security boundary).

### 5.3 ISaveStorage abstraction

```csharp
public interface ISaveStorage {
    Task<bool> SaveAsync(string slotId, SaveBundle bundle);
    Task<SaveBundle?> LoadAsync(string slotId);
    Task<bool> DeleteAsync(string slotId);
    Task<List<SaveSlotInfo>> ListAsync();
    Task<bool> ExistsAsync(string slotId);
}

public class LocalDiskStorage : ISaveStorage { ... }   // M13
public class SteamCloudStorage : ISaveStorage { ... }   // M14 stub w M13
```

W M13-6 piszemy `LocalDiskStorage`. `SteamCloudStorage` wpisujemy jako stub (`throw new NotImplementedException("Wait for M14 Steamworks integration")`) — pełna impl w M14.

### 5.4 ISavable kontrakt dla modułów

```csharp
public interface ISavable<T> where T : class {
    string ModuleId { get; }              // "fleet", "timetable", ...
    int SchemaVersion { get; }            // bumped on breaking change
    T Serialize();
    void Deserialize(T data, int sourceVersion);  // sourceVersion < SchemaVersion → migrator already ran
}

// Przykład:
public class FleetService : ISavable<FleetSaveData> {
    public string ModuleId => "fleet";
    public int SchemaVersion => 1;
    public FleetSaveData Serialize() { ... }
    public void Deserialize(FleetSaveData data, int sourceVersion) { ... }
}
```

### 5.5 SaveOrchestrator

```csharp
public class SaveOrchestrator {
    private readonly ISaveStorage storage;
    private readonly ISaveRegistry registry;  // module registry (DI lub static)

    public async Task<bool> SaveAsync(string slotId, string slotName) {
        var bundle = new SaveBundle { Manifest = BuildManifest(slotName) };
        foreach (var module in registry.GetAll()) {
            bundle.AddModule(module.ModuleId, module.SchemaVersion, module.SerializeToJObject());
        }
        bundle.Manifest.Hmac = ComputeHmac(bundle);
        return await storage.SaveAsync(slotId, bundle);
    }

    public async Task<LoadResult> LoadAsync(string slotId) {
        var bundle = await storage.LoadAsync(slotId);
        if (bundle == null) return LoadResult.NotFound;
        if (!VerifyHmac(bundle)) return LoadResult.ModifiedSave;
        if (bundle.Manifest.GameVersion > CurrentVersion) return LoadResult.NewerVersion;

        var failedModules = new List<string>();
        foreach (var (moduleId, sourceVersion, json) in bundle.GetModules()) {
            var module = registry.Get(moduleId);
            if (module == null) { Log.Warning($"Unknown module {moduleId} — skipping"); continue; }

            if (sourceVersion < module.SchemaVersion) {
                json = MigrationRunner.Migrate(moduleId, sourceVersion, module.SchemaVersion, json);
            }

            try {
                module.Deserialize(json);
            } catch (Exception e) {
                Log.Warning($"[SaveLoad] Module '{moduleId}' failed: {e.Message}. Initialized to default.");
                module.InitializeDefault();
                failedModules.Add(moduleId);
            }
        }

        return failedModules.Count == 0 ? LoadResult.Success : LoadResult.PartialLoad(failedModules);
    }
}
```

### 5.6 Failed module = graceful degradation

Jeśli `personnel.json` failuje deserialize (np. v3→v4 migrator threw exception):
- `PersonnelService.InitializeDefault()` → empty employees, empty turnusy
- Toast graczowi: "Save uszkodzony w module: personel. Zainicjalizowano domyślnie."
- Reszta save'a (Fleet/Timetable/Circulations/Economy/...) ładuje się normalnie
- Gracz może próbować zatrudnić nowych pracowników i grać dalej

To jest **kluczowe dla pre-EA** — alfa-saves mogą mieć incomplete migrations, gracz nie traci 10h gameplay'u przez bug w jednym module.

---

## 6. SaveMigrator framework

### 6.1 IMigrator interface

```csharp
public interface IMigrator {
    string ModuleId { get; }      // który moduł
    int SourceVersion { get; }    // z wersji
    int TargetVersion { get; }    // do wersji (zwykle Source+1)
    JObject Migrate(JObject input);
}
```

Przykład:

```csharp
public class FleetMigrator_v1_v2 : IMigrator {
    public string ModuleId => "fleet";
    public int SourceVersion => 1;
    public int TargetVersion => 2;
    public JObject Migrate(JObject input) {
        // v2 dodało pole `liveryColor` — default magenta dla starych pojazdów
        foreach (var vehicle in input["vehicles"]) {
            vehicle["liveryColor"] ??= "#FF00FF";
        }
        return input;
    }
}
```

### 6.2 MigrationRunner

```csharp
public static class MigrationRunner {
    private static readonly Dictionary<string, List<IMigrator>> migrators = ...;

    public static JObject Migrate(string moduleId, int from, int to, JObject input) {
        var current = from;
        var data = input;
        while (current < to) {
            var migrator = migrators[moduleId].FirstOrDefault(m => m.SourceVersion == current);
            if (migrator == null) {
                throw new MigrationGapException($"No migrator from v{current} for module {moduleId}");
            }
            data = migrator.Migrate(data);
            current = migrator.TargetVersion;
        }
        return data;
    }
}
```

Migratory autodiscovered przez reflection (`Type.GetTypes().Where(t => typeof(IMigrator).IsAssignableFrom(t))`).

### 6.3 Pre-EA vs post-EA policy

**Pre-EA:**
- Brak gwarancji back-compat
- `MigrationGapException` → graceful (load reszty, default ten module)
- Settings UI ma flag "Wersja alpha — niektóre save'y mogą być nieczytelne"

**Post-EA (od day 1 — 2026-12-23):**
- Strict back-compat — każda nowa wersja MUSI mieć migratory dla wszystkich poprzednich
- `MigrationGapException` → hard error, nie release'ujemy patch'a bez migratorów
- Tested: stare save'y z każdej minor wersji ładują się w current

---

## 7. Architektura

### 7.1 Asmdef

| Asmdef | Co dochodzi | Refs |
|---|---|---|
| `RailwayManager.Core` | `SettingsService`, PlayerPrefs wrapper | (bez nowych) |
| `RailwayManager.SharedUI` | `LocalizationService`, `LocalizedText`, `NumberFormatService` | Core |
| `RailwayManager.SaveLoad` (NEW) | `SaveOrchestrator`, `SaveRegistry`, `BundleSerializer`, `LocalDiskStorage`, `SteamCloudStorage` (stub), `MigrationRunner`, wszystkie `IMigrator` impl | Core, Fleet, Timetable, Map, Depot, **Personnel**, **Maintenance** (jeśli wydzielone w przyszłości) |

`RailwayManager.SaveLoad.asmdef` jest na końcu łańcucha zależności — widzi wszystkie moduły, żaden nie widzi jego. To jest celowe: moduły dostarczają `ISavable` impl jako część własnego kodu, a `SaveLoad` jest "zbieraczem" przez registry.

**Ostrzeżenie:** dodanie `SaveLoad` na końcu = każda zmiana w nim recompile'uje wszystko poniżej. Akceptowalne, bo M13 to onetime impl.

### 7.2 Folder structure

```
Assets/Scripts/Core/Settings/
├── SettingsService.cs
├── SettingsData.cs
├── GraphicsSettings.cs
├── AudioSettings.cs
├── GameplaySettings.cs
├── ControlSettings.cs
└── InterfaceSettings.cs

Assets/Scripts/SharedUI/Localization/
├── LocalizationService.cs
├── LocaleCode.cs
├── LocaleResolver.cs
├── LocalizedText.cs
├── LocalizedTextFormatted.cs
├── NumberFormatService.cs
└── Resources/Locale/{lang}/strings.json

Assets/Scripts/SaveLoad/
├── Runtime/
│   ├── SaveOrchestrator.cs
│   ├── SaveRegistry.cs
│   ├── SaveBundle.cs
│   ├── BundleSerializer.cs
│   ├── ISavable.cs
│   ├── ISaveStorage.cs
│   ├── LocalDiskStorage.cs
│   ├── SteamCloudStorage.cs (stub)
│   ├── HmacService.cs
│   └── AutoSaveService.cs
├── Migrations/
│   ├── IMigrator.cs
│   ├── MigrationRunner.cs
│   └── (per moduł migrators dorzucane w trakcie)
└── UI/
    ├── SaveLoadUI.cs
    ├── SlotListView.cs
    └── SlotDetailsView.cs

Assets/Scripts/SharedUI/Settings/
└── SettingsMenuUI.cs (+ partial classes per zakładka jeśli urośnie)
```

---

## 8. UI

### 8.1 SettingsMenuUI

Fullscreen panel z lewym tabbarem (**5 zakładek**: Sterowanie / Grafika / Dźwięk / Język / Ogólne) i prawą zawartością. Bottom bar: Apply / Cancel / Reset section / Reset all.

Zawartość per zakładka per §3. Zakładka **Ogólne** ma 2 podsekcje (Rozgrywka / Interfejs) jako headings + separators wewnątrz scrollable area.

Hook do MainMenu (przycisk "Ustawienia") + Pause menu (in-game ESC → "Ustawienia"). To samo UI w obu kontekstach.

**Refaktor partials** (M13-1, decyzja 2026-04-26): klasa rozbita na 5 partial files dla utrzymania (zgodnie z konwencją FleetPanelUI z M3):
- `SettingsScreenUI.cs` — base, Build entry, Show/Hide, Apply/Cancel/Reset handlers, RefreshLanguage, primitives (NewGO/MakeTMP/FillRT)
- `SettingsScreenUI.Sidebar.cs` — BuildSidebar, ApplySidebarState
- `SettingsScreenUI.BottomBar.cs` — BuildBottomBar, AddBottomBarButton
- `SettingsScreenUI.Sections.cs` — PopulateControl/Graphics/Audio/Language/General + FpsToIndex/IndexToFps helpers
- `SettingsScreenUI.RowBuilders.cs` — Slider/IntSlider/Toggle/Dropdown/Heading/Info row helpers + MakeRow + BuildSliderInternal

### 8.2 SaveLoadUI

Fullscreen panel z dwoma sekcjami:
- **Manual saves (10 slotów)** — grid 2x5 lub lista z screenshot preview, slot info (nazwa, data, gameTime, playtime), akcje (Load / Rename / Delete / Duplicate)
- **Auto-saves (5 slotów)** — lista, info, Load only (read-only — nie można rename/delete, automatyczna rotacja)

Hook do MainMenu (przycisk "Wczytaj") + Pause menu ("Zapisz" / "Wczytaj"). "Continue" w MainMenu = quick-action (najnowszy slot, bez otwierania pełnego UI).

### 8.3 Localization integration

Wszystkie text componenty UI dostają `LocalizedText` z `key`. Refactor istniejących (M13-4) to **mechaniczna robota** ale duża skala — każdy plik UI musi być przejrzany.

Kolejność rolloutu:
1. MainMenu + GameCreator + SettingsMenu (małe, izolowane)
2. SaveLoadUI (świeże w M13)
3. TopBar
4. FleetPanelUI (9 partials)
5. TimetableCreatorUI + CategoryEditor + TimetableListUI
6. CirculationListUI + VehicleAssignmentModal
7. PersonnelMainTabUI (9 tabów — Mój personel / Rekrutacja / Dyspozytura / Turnusy / Nastawnia / Warsztaty / Biuro+R&D / Kasy)
8. WorkshopsPanelUI + PartsPanelUI + MaintenanceAlertsUI + RescueDispatchUI
9. FinancePanelUI
10. Popup'y na mapie (StationPopup / TrackPopup / TrainPopup) — gdy będą gotowe (M9 ma TBD)

---

## 9. Podetapy implementacji

### M13-1 — SettingsService + UI menu (2 sesje)

- `SettingsService` static singleton w Core (`RailwayManager.Core.Settings`)
- `SettingsData` POCO + per-section data classes (`ControlSettings`, `GraphicsSettings`, `AudioSettings`, `LanguageSection`, `GeneralSettings` z dwiema podsekcjami: `GameplaySection`, `InterfaceSection`)
- PlayerPrefs persistence (key prefix `Settings.`)
- `SettingsMenuUI` fullscreen panel z **5 zakładkami** (Sterowanie / Grafika / Dźwięk / Język / Ogólne); zakładka Ogólne ma 2 podsekcje (heading + separator) — Rozgrywka / Interfejs
- **Klasa rozbita na 5 partial files** (Sidebar / BottomBar / Sections / RowBuilders + base) dla utrzymania (konwencja FleetPanelUI)
- Apply / Cancel / Reset do defaults pattern (per zakładka + global "Reset all")
- Hook do MainMenu i Pause menu (in-game ESC)
- UI scale stub: dropdown z jedną opcją "100%" + tooltip "Więcej opcji w późniejszych wersjach"
- Audio settings → `AudioMixer` (placeholder mixer na EA, pełna integracja w M12b)
- **Trudność NIE jest tu** (D33) — zostawić sloft/skeleton w GameCreator scenie do M13-7 lub osobnego sub-stepa (zależnie kiedy GameCreator update wejdzie)

**Deliverable:** Gracz otwiera Ustawienia → widzi 5 zakładek z kontrolkami → zmienia wartości → Apply → wartości persystują po restart. Zakładka Ogólne ma czytelne sekcje Rozgrywka/Interfejs (Język to osobna zakładka).

### M13-2 — Rebindowanie klawiszy (1-2 sesje)

- `InputActionRebindingExtensions` integration
- `RebindModalUI` — "Press a key to rebind"
- `BindingConflictDetector` — gdy próbujesz przypisać używany klawisz → ostrzeżenie + opcja swap
- `ControlSettingsUI` — lista action map'ów + per-action buttony "Zmień..."
- Reset do defaults per action + global
- Persistence: `InputAction.SaveBindingOverridesAsJson()` → PlayerPrefs blob

**Deliverable:** Gracz w zakładce Sterowanie widzi listę akcji → klika Zmień → modal z press-a-key → conflict detection działa → reset do default działa → po restart bindings są zachowane.

### M13-3 — i18n framework + 7-language infrastructure (2 sesje)

- `LocalizationService` static singleton w SharedUI
- `LocaleCode` enum (PL/EN/DE/CZ/JP/RU/UK)
- `LocaleResolver` — Steam autodetect → CultureInfo fallback → manual override
- `LocalizedText` MonoBehaviour (TMP wrapper)
- `LocalizedTextFormatted` (string.Format z params)
- `NumberFormatService` (currency/date/number per CultureInfo)
- Resource files structure pod `Resources/Locale/{lang}/strings.json`
- TMP font atlases:
  - **Noto Sans** (Latin Extended + Cyrillic — pokrywa 6 z 7 języków)
  - **Noto Sans JP** (CJK — fallback chain dla JP)
- `OnLanguageChanged` event
- Test: zmiana języka w runtime → re-render placeholder testu

**Deliverable:** Klucz lookup `LocalizationService.Get("test.hello")` zwraca poprawny string per locale. Zmiana języka w runtime re-renderuje. Brak treści → fallback na EN → fallback na klucz. Steam language autodetect działa (jeśli Steam offline, CultureInfo działa).

### M13-4 — i18n content rollout PL+EN (3-4 sesje)

- **Audit hardcoded strings** — skanowanie wszystkich UI plików, ekstrakcja stringów do CSV
- **Generator klucz → string** — convention `<scene/panel>.<section>.<element>` (np. `fleet.panel.tabs.market`)
- **Stworzenie `strings_pl.json`** — wszystkie istniejące polskie stringi pod kluczami
- **Stworzenie `strings_en.json`** — machine translation (DeepL / GPT) + manual review
- **Rollout `LocalizedText`** na panelach (kolejność z §8.3):
  1. MainMenu + GameCreator + SettingsMenu + SaveLoadUI
  2. TopBar + FleetPanelUI (9 partials)
  3. TimetableCreatorUI + CategoryEditor + TimetableListUI + CirculationListUI
  4. PersonnelMainTabUI (9 tabs) + WorkshopsPanelUI + PartsPanelUI + MaintenanceAlertsUI + FinancePanelUI
- Layout audit per panel — DE/JP test layoutu po dodaniu treści w M13-5/M13-6 (tutaj tylko PL+EN test)

**Deliverable:** Cały istniejący UI używa `LocalizedText`. Zmiana języka PL↔EN w runtime → wszystkie ekrany re-renderują. Brak hardcoded polskich stringów w UI plikach (assertion script).

### M13-5 — i18n content DE+CZ (1-2 sesje)

- Po M13-4 klucze są stable
- `strings_de.json` — machine translation + user-supplied PKP/Bahn-terminologia
- `strings_cz.json` — analogicznie + ČD-terminologia
- Layout audit — DE jest najdłuższy, sprawdzenie czy nic się nie rozjeżdża. Fix per panel jeśli trzeba (min-width, content size fitter)

**Deliverable:** Gracz wybiera DE/CZ w settings → cały UI w wybranym języku. Layout nie rozjeżdża się.

### M13-6 — Save backbone (`SaveBundle` + `ISaveStorage`) (2 sesje)

- `RailwayManager.SaveLoad.asmdef` setup
- `SaveBundle` model (manifest + module sections jako Dictionary<string, JObject>)
- `BundleSerializer` — gzip + JSON serialization (Newtonsoft.Json)
- `HmacService` — SHA256 podpis (secret key hardcoded w build constants)
- `ISaveStorage` interfejs
- `LocalDiskStorage` impl (folder `%USERPROFILE%/Documents/Railway Manager/Saves/`)
- `SteamCloudStorage` stub (`throw new NotImplementedException()`)
- `ISavable<T>` interfejs
- `SaveRegistry` — moduły rejestrują się przez `Register(ISavable)` w bootstrap
- `SaveOrchestrator` — Save/Load coordination
- Test: dummy module impl + save → load → verify roundtrip

**Deliverable:** Backbone gotowy. Można save'ować/load'ować dummy moduły. Bundle file na dysku jest gzipped JSON, czytelny po dekompresji. HMAC weryfikacja działa (modyfikacja invalidates).

### M13-7 — Per-module Serialize/Deserialize — Phase 1 (statyczne dane) (4-5 sesji)

Każdy istniejący moduł dostaje `ISavable<T>` impl + POCO `<Module>SaveData`:

| Moduł | SaveData zawiera |
|---|---|
| `World` | gameTime, seed, trudność, weather/season state |
| `Fleet` | vehicles (z assignments do circulations), cart state, market refresh state |
| `Timetable` | Routes + Timetables + Categories + StationTrackData overrides |
| `Circulations` | Circulations + per-day vehicle assignments + status + names |
| `Economy` | finanse, OD matrix state, reputation per voivodeship, daily history |
| `Maintenance` | komponenty per pojazd, parts inventory, workshop slot assignments |
| `Personnel` | pracownicy, turnusy (CrewCirculations), morale/fatigue stany, shifts, hotels bookings |
| `Depot3D` | tory, sieć trakcyjna, budynki, ścieżki, rooms, equipment |
| `Stats` | playtime, achievements progress, per-line balance history |

Każdy ma `SchemaVersion = 1`. `Serialize()` zbiera state, `Deserialize()` restore'uje.

**Notka:** dla każdego modułu testujemy roundtrip (serialize → deserialize → verify equals). Edge cases: empty state (nowa gra), full state (mid-game).

**Deliverable:** Save bundle zawiera wszystkie moduły jako osobne JSONy. Load → wszystkie moduły poprawnie restorują state. Roundtrip testy passed.

### M13-8 — Per-module Serialize/Deserialize — Phase 2 (runtime state) (2-3 sesji)

Najbardziej tricky część — **żywe stany symulacji**:

- `TrainRunSimulator` runtime — pozycje (lat/lon na polyline), prędkości, opóźnienia, occupied blocks, current segment index
- `DepotMovementSimulator` — task queue, current manewry per pojazd, occupied tracks
- `PassengerManager` — agent state (ile pasażerów na każdej stacji, w pociągach, OD matrix flow)
- `BreakdownService` — active breakdowns + self-repair attempts + timestamps
- `RescueService` — in-progress rescues (rescue loco position, route, ETA)
- Personnel cykl 3D — aktywni pracownicy w 3D (pozycje na PathGraph, current task, target room)

Wszystko serializowane do `runtime.json` (osobny moduł, bo wymaga koordynacji wielu serwisów).

**Save w trakcie kursu:** pozycja pociągu na polyline w sekundach (np. `currentSegmentIndex: 12, distanceFromStart: 145.7`). Restore: spawn pojazdu na tej pozycji + restore prędkości + restore occupied blocks.

**Save w trakcie manewru:** task queue zachowuje sekwencję, current task ma pozycję + target. Restore wznawia od miejsca zatrzymania (lub instant-resolve gdy gracz nie w Depot scene).

**Deliverable:** Save w środku kursu / manewru / cyklu pracownika → load → wszystko kontynuuje bez glitche. Acceptance: 5-minutowy gameplay → save → reload → state identyczny.

### M13-9 — Auto-save + Quick-save (1-2 sesje)

- `AutoSaveService` — ticker co 5 min game time
- Rotujący slot (`autosave_001` ... `autosave_005`, oldest overwritten)
- Quick-save F5 — natychmiastowy save do dedykowanego slot'u `quicksave`
- Quick-load F9 — load `quicksave` z confirmation dialog
- Throttling: nie zapisuj gdy gracz w menu / mid-modal / mid-drag (czeka do unblock)
- `Application.wantsToQuit` callback — best-effort save przed exit (5s timeout, jeśli nie zdąży to exit anyway)

**Deliverable:** Gracz nie traci progress przy ALT+F4 (auto-save sprzed max 5 min lub quit-save). F5/F9 działa w każdej chwili (poza confirmation dialogami).

### M13-10 — Loading Scene (1-2 sesje)

**Cel:** rozwiązać "freeze gry" przy długich operacjach (New Game init, Load Save, scene transitions). Pre-EA = minimal progress bar + tip text. Pełen polish (artwork, animations, async resource pre-loading) → M12c Visual.

- `LoadingScreen.unity` — minimal scene z czarnym tłem, progress bar 0-100% w środku, "Loading..." text + rotating tip text
- `LoadingScreenManager` (singleton) — API:
  - `LoadSceneAsync(string sceneName, Action<float> onProgress, Action onComplete)`
  - `RunLongOperationAsync(IEnumerator operation, string title, Action<float> onProgress)` — dla load save (które nie jest loadem sceny)
- `LoadingScreenUI` — procedural (jak inne UI), progress bar (Slider component max=1), title TMP, tip TMP
- 5-10 placeholder tipów (hardcoded po polsku/angielsku, z LocalizationService.Get) — np.:
  - "Naciśnij F5 żeby quick-save w dowolnej chwili"
  - "Możesz zmienić język w Ustawieniach → Język"
  - "Każdy obieg taboru można edytować z poziomu zakładki Obiegi"
- Hook integracje:
  - **MainMenu → New Game** (przed `SceneManager.LoadScene("Depot")`)
  - **MainMenu → Continue** (load najnowszego save + transition do Depot)
  - **SaveLoadUI → Load** (M13-11) — load save'a z progress callback
  - **GameCreator → Start Game** (po Custom difficulty/rules confirm)

**Scene switch Depot↔MapScene:** zostaje **instant** (additive scenes + CanvasGroup toggle ~instant). Loading screen tu byłby overkill. (D37)

**Deliverable:** Gracz nie widzi black screen / freeze podczas New Game / Load / Continue. Progress bar pokazuje 0-100%, tip text rotates. Scene switch Depot↔MapScene zostaje instant.

### M13-11 — SaveLoadUI (2 sesje)

- Fullscreen panel z dwoma sekcjami (Manual / Auto-saves)
- 10 manualnych slotów (grid 2×5 lub lista)
- 5 auto-save slotów (read-only — Load only)
- Slot info: nazwa, data savedAt, gameTime, playtime, screenshot preview (jeśli jest)
- Operacje: New Save (override prompt), Load, Rename, Delete, Duplicate
- Confirmation dialogi dla destructive actions
- "Continue" w MainMenu = jump-action do najnowszego slot'a (porównanie po `lastModified`)
- Hook do Pause menu
- **Load** wywołuje `LoadingScreenManager.RunLongOperationAsync` (M13-10) z progress callback z deserializacji per moduł

**Deliverable:** Gracz może w pełni zarządzać save'ami z UI. Continue w MainMenu działa. Confirmation dialogi przed Delete / Override. Load pokazuje loading screen z progress.

### M13-12 — SaveMigrator framework (1-2 sesje)

- `IMigrator` interfejs
- `MigrationRunner` z autodiscovery (reflection)
- Per-moduł migrator chain
- Pre-EA flag: w SettingsMenu pole "Wersja: 0.13.0-alpha — niektóre save'y mogą nie być kompatybilne między wersjami"
- Test: stworzyć fake `FleetMigrator_v1_v2` (np. dodaje pole `liveryColor` z default), bump `FleetService.SchemaVersion = 2`, save w v1, load w v2, verify migracja zadziałała

**Deliverable:** Migrator chain działa. Save z v1 ładuje się w v2 z poprawną migracją. Brak migratora = graceful degradation (warning + module init default).

### M13-13 — GameCreator difficulty selector + Custom editor (1-2 sesje)

**Cel:** dodać do istniejącej sceny `GameCreator.unity` widget wyboru trudności (4 presety + Custom editor). Trudność persystowana w `world.json` save bundle (D33).

- `DifficultyPreset` enum: `Easy`, `Normal`, `Hard`, `Realistic`, `Custom`
- `DifficultyModifiers` POCO — slot dla 8-12 mnożników (start budget, operational cost, breakdown chance, passenger demand, salary, subsidy, delay propagation, random event frequency, hotel cost, ticket price tolerance — finalna lista w M-Balance)
- `DifficultyPresetCatalog` — gotowe zestawy modifierów per preset:
  - **Łatwy:** start +50%, koszty -25%, breakdown -50%, passenger demand +20%, eventy rzadziej
  - **Normalny:** wszystko 1.0× (baseline)
  - **Trudny:** start -25%, koszty +25%, breakdown +50%, passenger demand -10%
  - **Realistyczny:** start -50%, koszty +50%, breakdown +100%, eventy częściej, ekspresowe wymogi (bez tolerancji opóźnień)
- `DifficultySelectorUI` w `GameCreator.unity`:
  - 4 przyciski presetów (toggle group, single selection)
  - Przycisk **"Custom..."** → expand panel z sliderami per modifier (group: Ekonomia / Operacje / Realizm)
  - Slider modyfikacja → preset auto-switch na "Custom" + value zachowane
  - Reset to preset button (wraca do wybranego presetu sliders)
  - Tooltips z opisem każdego modifiera
- `GameStartConfig` extension — pole `difficulty: DifficultyConfig` propagowane do `WorldService.Initialize()` przy starcie nowej gry
- Integracja z M13-7 (Per-module Serialize Phase 1) — `WorldSaveData` zawiera `difficulty` field

**Game rules infrastructure (D36):**
- `GameRule` enum — placeholder values (np. `VehicleBreakdowns`, `PersonnelManagement`, `Subsidies`, `RandomEvents`, `Tutorial`) z możliwością rozszerzania
- `GameRulesConfig` POCO — `Dictionary<GameRule, bool>` z defaultami
- `GameRulesService` static singleton z `IsEnabled(GameRule)` query API
- `GameRulesSelectorUI` w GameCreator — sekcja "Reguły gry" z listą toggle'i (placeholder na 1-2 toggles na M13, pełna lista w M-Balance)
- Persystencja w `world.json` razem z difficulty (D36 cross-references D35)
- **Konwencja:** moduły które używają toggle'i sprawdzają `GameRulesService.IsEnabled(...)` w bootstrap albo per-tick (zależnie od kosztu). Wyłączony moduł = brak update'ów / hard skip / auto-handling (per-toggle decyzja w M-Balance)

**Deliverable:** Gracz w GameCreator widzi 4 presety + opcję Custom + sekcję "Reguły gry" (placeholder toggle'i). Wybór persystowany w `world.json`. Mid-game zmiana niemożliwa (settings UI nie ma ani trudności, ani game rules — D33/D36).

**Notka uchitka:** sama implementacja modifierów (jak każdy modifier wpływa na gameplay) → **M-Balance**. Konkretna lista game rules toggle'i + per-toggle behavior (jak moduł reaguje na "wyłączony") również → **M-Balance**. W M13-12 robimy tylko **pipeline + extensible infrastructure** (data flow GameCreator → WorldService → moduły, query API `GameRulesService.IsEnabled`, persystencja). Faktyczne value tuning + lista toggles + cross-system testing → M-Balance.

### M13-14 — Smoke testing + edge cases (1-2 sesje)

Test scenariusze:
- **Mid-flight save** — pociąg w środku kursu Hel→Zakopane, save → reload → kontynuuje
- **Mid-manewr save** — wagon przesuwany w zajezdni, save → reload → manewr resume
- **Mid-workshop save** — pojazd w trakcie P3, save → reload → naprawa kontynuuje
- **Mid-breakdown save** — awaria + self-repair w toku, save → reload → state zachowany
- **Mid-rescue save** — rescue loco w trasie, save → reload → ETA ten sam
- **Personnel mid-cycle** — pracownik w środku turnusu z noclegiem w hotelu, save → reload → kontynuuje
- **Corrupt save** — manual edit JSON w bundle → reload → "modified save" warning
- **Schema mismatch** — bump wersji jednego modułu → reload starszego save'a → migrator runs
- **Failed module** — celowo break migrator → reload → graceful degradation, reszta ładuje się
- **Quick load mid-game** — F9 podczas active gameplay → load quicksave

**Deliverable:** Wszystkie scenariusze passed. Save/Load nie traci stanu. Edge cases handled gracefully. Performance: save 30 pojazdów + 50 rozkładów + 50 pracowników < 2s, load < 5s.

---

## 10. Integracja z innymi systemami

| System | Integracja |
|---|---|
| **Wszystkie istniejące moduły** | Każdy implementuje `ISavable<T>` + rejestruje się w `SaveRegistry.Register()` w bootstrap (Initializer pattern, jak istniejące `*Initializer.cs`) |
| **MainMenu** | "Continue" / "Wczytaj" / "Ustawienia" przyciski hook to nowych UI |
| **GameCreator** | (a) Po kreacji nowej gry → init wszystkich modułów do default state (zamiast load); (b) **Difficulty selector + Custom editor (M13-12, D33/D35)** — gracz wybiera preset lub customizuje sliders, config propagowany do `WorldService.Initialize()` i persystowany w `world.json`; (c) **Game rules section (M13-12, D36)** — toggle'i tak/nie (np. "Awarie taboru", "Zarządzanie personelem", "Dotacje") konfigurowane przy starcie nowej gry, niemodyfikowalne mid-game; konkretna lista i behavior → M-Balance |
| **GameRulesService** | Nowy serwis (M13-12) — query API `IsEnabled(GameRule)` używane przez moduły do skip'owania / auto-handling gdy reguła wyłączona. Lista toggle'i dorzucana iteracyjnie w M-Balance |
| **TopBar** | Save indicator (ikonka + tooltip "Ostatni auto-save: 3 min temu") opcjonalnie |
| **InputSystem_Actions** | Rebinding override loaded z PlayerPrefs przy startup |
| **GameState** | Pause / unpause przy save/load aby uniknąć race conditions |
| **EconomyManager** | Per-module save = stan finansowy (balance, history) |
| **TrainRunSimulator** | Najtrudniejsza integracja — runtime state w środku kursu |
| **DepotMovementSimulator** | Runtime task queue + current manewry |
| **WorkshopManager** | Active slot assignments + workshop progress |
| **PersonnelService** | Pracownicy + turnusy + morale + shift state + cykl 3D positions |

**Wszystkie istniejące Initializery** dostają w M13-7/M13-8 dodatkową responsibility: rejestracja `ISavable` impl w `SaveRegistry`.

---

## 11. Pytania otwarte

- **[TBD]** Mod support w save — czy moddowane save'y są oznaczone? (POST-EA, gdy będzie modding framework)
- **[TBD]** Save export/import — gracz może wysłać save znajomemu (osobny przycisk "Eksportuj do pliku")? (POST-EA quality of life)
- **[TBD]** Save thumbnail — render screenshot do RT przed save kosztuje 50-100ms freeze. Czy akceptowalne dla manual save? (M13-10 testing)
- **[TBD]** Steam Cloud quota — Steam daje 100MB-1GB per app. Wystarczy dla 10 manualnych + 5 auto = 15 saves × max 20MB = 300MB? Tak. Ale monitoring w M14
- **[TBD]** Settings reset behavior — Reset to defaults globalny czy per-zakładka? (M13-1 detail)
- **[TBD]** Encryption — HMAC chroni przed modyfikacją, ale save jest readable (gracz widzi ile ma pieniędzy w JSON). Encryption chroniłaby przed szpiegowaniem, ale zwiększa lock-in (gracz nie może debugować swojego save). **Rekomendacja: brak encryption, HMAC wystarczy** dla anti-cheat best-effort
- **[TBD]** Multi-monitor support w settings — które monitor, primary detection (POST-EA M12 jeśli ktoś poprosi)
- **[TBD]** Cloud save conflict resolution — gdy gracz gra na 2 maszynach (Steam Cloud sync) i save'y się rozjechają. Standard Steam dialog "Local vs Cloud — wybierz" (M14)

---

## 12. Success metrics / Definition of Done

**M13 ukończone gdy:**

- ✅ Gracz może w pełni skonfigurować grę przez Settings UI (5 zakładek: Sterowanie / Grafika / Dźwięk / Język / Ogólne)
- ✅ Wszystkie kluczowe akcje gry mają domyślne klawisze + można je zmienić w Sterowanie
- ✅ Gra startuje w języku Steam'a (lub system locale gdy offline), gracz może zmienić w runtime
- ✅ **5 języków na EA (PL/EN/DE/CZ/JP) mają pełen content** (PL/EN profesjonalne, DE/CZ/JP konsultanci PKP/Bahn/ČD/JR). RU/UK fallback do EN (puste resource files, infrastruktura ready)
- ✅ Gracz w GameCreator wybiera trudność (4 presety + Custom editor z sliderami), config persystowany w `world.json`
- ✅ Loading Scene minimal (progress bar + rotating tip) pojawia się przy New Game / Continue / Load Save (D37). Pełen polish (artwork, animacje) → M12c
- ✅ Gracz może zapisać 10 manualnych save'ów + ma 5 rotujących auto-save'ów + Quick-save F5/F9
- ✅ Save w trakcie dowolnej akcji (kurs / manewr / warsztat / breakdown / rescue / cykl pracownika) → load → state restored
- ✅ Failed module load = graceful degradation (warning + load reszty), nie hard-fail
- ✅ Modyfikacja save'a invalidates HMAC → "modified save" warning
- ✅ Migrator framework działa (test fake v1→v2)
- ✅ Steam Cloud interfejs gotowy (stub), implementacja w M14
- ✅ Performance: save mid-game (30 vehicles, 50 timetables, 50 employees) < 2s, load < 5s
- ✅ Bundle size: < 5 MB mid-game, < 20 MB late-game

**Estimated total scope: 26-38 sesji** (z M13-10 Loading Scene 1-2 sesje). Największa pojedyncza robota: M13-4 i18n content rollout (audyt + LocalizedText na każdym istniejącym tekście UI).
