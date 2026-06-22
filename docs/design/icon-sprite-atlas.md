# Icon and sprite atlas spec

> Cel: jeden punkt odniesienia dla ikon UI, ikon mapy i symboli paint/decal.
> Dokument domyka lukę miedzy `M-UIPolish`, `M-FleetConfigurator` i asset prep
> przed EA.

---

## Status

**Stan na 2026-06-15 (MUI-11):** **pelna wyliczona lista gotowa** — 446
ikon (pełny research CAŁEGO UI), kanoniczna lista per ikona + prompt w `icon-prompts.json`; rozbicie w sekcji
"Wymagany zestaw EA → UI full set". Stale realne PNG/SVG jeszcze NIE
wygenerowane — to nastepny krok (generacja + integracja `UIBuilders.MakeIconButton`).

**Stan na 2026-05-29:** brak realnego atlasu ikon. UI w wielu miejscach uzywa
labeli TMP (`TOR`, `EL`, `POM`, `X`), a paint editor renderuje symbole jako
kolorowe placeholdery.

**Najwazniejsze zrodla prawdy:**

- `docs/design/asset-needs-by-system.md` - szacowanie 60-80 ikon UI.
- `Assets/StreamingAssets/Fleet/decal_catalog.json` - katalog symboli paint UI.
- `Assets/Scripts/Fleet/Data/DecalDef.cs` - `spriteResourcePath` ladowany docelowo
  przez `Resources.Load`.
- `Assets/Scripts/Depot/UI/FleetPanelUI.PaintEditor.cs` i
  `Assets/Scripts/Depot/UI/PaintPreview3D.cs` - aktualne placeholdery.

---

## Zakres

### Wchodzi w zakres

1. **Ikony UI proceduralnego** - toolbar, subtoolbary, akcje, role personelu,
   ustawienia, save/load, alerty.
2. **Ikony mapy** - przede wszystkim typy stacji/halty/wezly, docelowo pod
   `MapRenderer.stationIconSprite` lub jego nastepce.
3. **Decals paint editor** - cyfry, strzalki, symbole informacyjne i ostrzegawcze
   nanoszone na tabor.
4. **Ikony social/menu** - tylko jezeli sa potrzebne dla MainMenu/Credits/Help
   i maja jasny status licencyjny.

### Poza zakresem

- Tekstury PBR modeli 3D.
- Branding realnych przewoznikow bez osobnej decyzji legal/licencja.
- Marketingowe key arty i screeny Steam.
- Workshop custom upload po EA.

---

## Format i import

### Pliki zrodlowe

| Typ | Preferowany source | Delivery do Unity | Uwagi |
|---|---|---|---|
| UI icon | SVG lub 1024 PNG master | PNG 256x256 transparent | Ikony moga byc tintowane przez UITheme. |
| Map icon | SVG lub 512 PNG master | PNG 128x128 transparent | Czytelne przy malych rozmiarach. |
| Decal | SVG monochrome | PNG 256x256 transparent | Wysoki kontrast, bez cienia i tla. |
| Social logo | Oficjalne SVG/PNG | PNG 64/128 transparent | Zachowac brand/trademark notes. |

**Unity import settings:**

- Texture Type: `Sprite (2D and UI)`.
- Sprite Mode: `Single`.
- Alpha Is Transparency: `On`.
- sRGB: `On`.
- Mip Maps: `Off` dla UI/decals.
- Max Size: 256 dla UI/decals, 128-256 dla map icons.
- Compression: `None` albo `High Quality`; nie dopuszczac artefaktow na krawedziach.
- Filter Mode: `Bilinear` dla UI, `Point` tylko dla celowo pixelowych ikon.
- Pivot: `Center`.

---

## Sciezki

### UI icons

Proponowana struktura:

```text
Assets/Sprites/UI/Icons/
  toolbar/
  room/
  track/
  catenary/
  path/
  actions/
  maintenance/
  personnel/
  misc/
```

Nazewnictwo: `ico_<set>_<name>.png`, np.:

- `ico_toolbar_track.png`
- `ico_toolbar_catenary.png`
- `ico_room_dispatch.png`
- `ico_action_modernize.png`
- `ico_role_driver.png`

### Map icons

Proponowana struktura:

```text
Assets/Sprites/Map/Icons/
  station-main.png
  station-regional.png
  halt.png
  depot-link.png
```

Te pliki powinny byc powiazane z `MapRenderer` dopiero po decyzji projektowej dotyczacej typow stacji.

### Decals

Katalog danych jest juz w:

```text
Assets/StreamingAssets/Fleet/decal_catalog.json
```

Kazdy wpis ma `spriteResourcePath`, np. `Decals/wheelchair`. To oznacza docelowy
plik sprite pod:

```text
Assets/Resources/Decals/wheelchair.png
```

Uwaga: dzis `BuildSymbolCell` i `PaintPreview3D.BuildDecal` nadal renderuja
placeholdery. Samo dodanie PNG nie wystarczy - MUI/FC follow-up musi podmienic
placeholdery na `Resources.Load<Sprite>(decal.spriteResourcePath)`.

---

## Wymagany zestaw EA

### UI pilot - MUI-11

Minimalny pilot powinien pokryc jeden przeplyw od toolbaru do popupu:

| ID | Plik | Uzycie |
|---|---|---|
| `toolbar-track` | `ico_toolbar_track.png` | Build track |
| `toolbar-catenary` | `ico_toolbar_catenary.png` | Build catenary |
| `toolbar-path` | `ico_toolbar_path.png` | Paths/roads |
| `toolbar-room` | `ico_toolbar_room.png` | Room build |
| `toolbar-delete` | `ico_toolbar_delete.png` | Demolish/reset |
| `action-select` | `ico_action_select.png` | Select mode |
| `action-undo` | `ico_action_undo.png` | Undo |
| `action-redo` | `ico_action_redo.png` | Redo |
| `action-save` | `ico_action_save.png` | Save |
| `action-load` | `ico_action_load.png` | Load |
| `misc-settings` | `ico_misc_settings.png` | Settings |
| `misc-alert` | `ico_misc_alert.png` | Warning/error |

### UI full set - MUI-12 — pelny manifest (wyliczony)

> **PEŁNY RESEARCH 2026-06-15:** wyczerpujący skan CAŁEGO UI (14 obszarow, 689 surowych
> znalezisk → po deduplikacji **446 unikalnych ikon**). Zastępuje wcześniejszy częściowy skan
> (224 — sam toolbar + panele zajezdni, gubił m.in. CTA MainMenu/GameCreator, popupy, ekrany).
> **Kanoniczna, kompletna lista per ikona + gotowy prompt AI: `docs/design/icon-prompts.json`**
> (v2, 8 pól/ikona, w tym `needsPressFeedback`). Poniższa tabela to rozbicie ilościowe; sekcja
> „Kubelek A" niżej to czytelny SKRÓT wcześniejszego skanu 224 (NIE pełna lista — pełna jest w JSON).

**Podsumowanie:** **446 ikon** (224 z pierwszego skanu + 222 z pełnego researchu). **282** to
klikalne przyciski wymagające „press feel" (`ButtonPressFeedback`). 47 decali = mono-maski na
taborze (tintowane), markery mapy = 2-kolorowe. Uniwersalne afordancje (close/back/edit/delete/
add/confirm/cancel/search…) skonsolidowane do `ico_misc_*` (reuse cross-modul, NIE duplikowane).

| Set | Liczba |
|---|---:|
| MainMenu | 53 |
| Decal | 47 |
| Furniture | 46 |
| Misc | 41 |
| Personnel | 38 |
| Maintenance | 34 |
| Timetable | 30 |
| Fleet | 29 |
| GameCreator | 25 |
| Toolbar | 17 |
| Map | 17 |
| FleetConfigurator | 13 |
| TrackBuildSubMode | 12 |
| RoomBuildSubMode | 10 |
| Economy | 10 |
| Modernization | 9 |
| Outdoor | 5 |
| Path | 3 |
| RoomAction | 3 |
| Catenary | 2 |
| SaveLoad | 2 |
| **Razem** | **446** |

#### Kubelek A — do wygenerowania

**Toolbar (6)** — zastepuje labele `SEL/TOR/EL/DR/POM/X`:
`toolbar-select` [pilot], `toolbar-track` [pilot], `toolbar-catenary` [pilot],
`toolbar-path` [pilot], `toolbar-room` [pilot], `toolbar-demolish` [pilot].
(`toolbar-demolish` = burzenie obiektow 3D, ROZDZIELONE od `misc-delete-trash` =
usun wiersz danych.)

**RoomBuildSubMode (10)** — labele `HAL/MAG/DSP/BIO/SOC/NAC/LAZ/SZT/KOR/DR`:
`hall`, `storage`, `dispatcher`, `office`, `social`, `supervisor`, `bathroom`,
`locker`, `corridor`, `trafficcontroller`.

**TrackBuildSubMode (11)** — labele `TOR/R19/R30/KRZ/SCH/MYJ/OBR/KAN/PAL/WOD`:
`track`, `r190`, `r300`, `crossover`, `schemas`, `washzone`, `turntable`,
`pitlift`, `fuelstation`, `waterservice`, `crossover-scissors` (rezerwa).
(outdoor equipment wspoldzieli glif z setem Outdoor.)

**Catenary (2):** `catenary-add` (ON), `catenary-remove` (OFF).

**Path (3):** `path-sidewalk` (P), `path-road` (D), `path-parking` (PK).

**RoomAction (3):** `roomaction-build` (mur), `roomaction-halttrack` (TOR w hali),
`roomaction-electrify` (EL/piorun).

**Outdoor (5):** `wash`, `turntable`, `pitlift`, `fuelstation`, `waterservice`
(zastepuje kolorowe placeholder-cuboidy; wspoldzieli glif z TrackBuildSubMode).

**Furniture (38)** — zgodnie z `builtin_catalog.json`, zastepuje 3-literowe labele
(KRZ/STO/LAM/KOS/ROS/TAB/ZEG/WIE/GAS/REG/BIU/SZA/DRU/MON/PUL/PIT/PI2/PI3/NAR/UZY/
WOZ/BRA/PAL/LAK/WOD/BGR/SZC/SZ2/SZ3/DET/SUS/REI/PAL/KUC/AUT/SOF/WC/PRY +
sink/locker/bench/door). Wszystkie SVG.

**Modernization (9):** `dispatch-out`, `dispatch-refuel`, `dispatch-workshop`,
`dispatch-wash`, `action-modernize` (zebatka+upgrade; warianty EN57/EU07/SM42 =
label tekstowy), `action-modify` (bogie/comfort/function), `room-upgrade`
(Awansuj), `room-max`, `selfpaint`.

**Maintenance (17):** 12 komponentow pojazdu — `engine`, `brake`, `doors`, `ac`,
`body`, `wheels`, `electrical`, `interior`, `lights`, `toilets`, `pantograph`,
`coupling` (SVG); + `eta-clock` (SVG); + 4 AI: `rescue-locomotive`,
`rescue-inbound`, `rescue-returning`, `external-inspection`.

**Personnel (14):** 10 rol (AI, ilustracyjne sylwetki): `driver`, `conductor`,
`mechanic`, `cleaner`, `washbay`, `office`, `research`, `ticketclerk`,
`dispatcher`, `trafficcontroller`; + 4 SVG: `tab-staff`, `hotel-tier`,
`alert-nodesk`, `workflow-walking`. (9 tabow panelu reuzywa ikon rol/akcji.)

**Timetable (12):** `vehicle-assign`, `deadhead`, `broken-vehicle`, `autogen`,
`stoptype-ph`, `stoptype-pt`, `stoptype-zd`, `stoptype-transit`,
`frequency-takt`, `frequency-single`, `calendar`, `workflow-step` (numer=tekst).

**Map (8):** 4 AI — `station-main`, `station-halt`, `depot-link`, `train-status`
(1 glif pociagu + tint 5 statusow, NIE 5 plikow); 4 SVG — `follow-camera`,
`platform`, `track` (wspoldz.), `dlc-lock` (wspoldz. z `misc-lock`).

**Decal (27)** — bitmapy do tekstury pojazdu (osobne od UI arrow): `digit-0..9`
(10), `wheelchair`, `bicycle`, `wifi`, `usb`, `toilet`, `quiet`, `reservation`,
`first-class`, `child`, `restaurant`, `warning-triangle`, `no-lean`,
`no-smoking`, `arrow-up/down/left/right`. (`custom-text`/`company-name` = tekst,
Kubelek B.)

**FleetConfigurator (13)** — wyposazenie/safety/voltage (dzis czysty checkbox):
`equipment-ac` (wspoldz.), `equipment-sockets`, `equipment-wifi` (wspoldz.),
`equipment-pis`, `safety-ca`, `safety-shp`, `safety-etcs-l1`, `safety-etcs-l2`,
`voltage-3kvdc`, `voltage-15kvac`, `voltage-25kvac`, `paint-export`,
`paint-import`.

**Economy (6):** `revenue`, `costs`, `subsidy`, `net`, `balance`, `topbar-money`.

**Misc (36)** — uniwersalne afordancje (duzo reuse cross-modul):
`settings` [pilot], `alert` [pilot], `action-select` [pilot], `action-undo`
[pilot], `action-redo` [pilot], `action-save` [pilot], `action-load` [pilot],
`close`, `success`, `error`, `arrow-up/down/left/right`,
`chevron-up/down/right`, `add`, `remove-minus`, `delete-trash`, `edit`,
`search`, `filter`, `sort`, `info`, `help`, `lock`, `unlock`, `drag-handle`,
`copy-duplicate`, `refresh`, `spinner-loading`, `overflow-menu`, `move-arrows`,
`rotate`, `reputation`.
Opcjonalne (decyzja przy MUI-12 wg realnej potrzeby; ujęte w pełnym katalogu 446 jeśli były w skanie):
`settings-graphics/audio/controls/language`, `multiplayer`, `mods`,
`map-zoom-in/out`, `map-visibility-toggle`, `pin-favorite`.

#### Kubelek B — zostaje tekstem/liczba (NIE ikona)

Kody IRJ (EI/EC/EN/MP/MH/RO/PW/TC/ZN), StopType short-code (PH/PT/ZD), przeglady
P1-P5, mnozniki predkosci x1/x5/x25/x150/x500, symbole walut (zl/€/$/Kc), numery
torow/track_ref, "Slot #N", decal `custom-text`/`company-name`, nazwy
miast/wsi (TMP), OSM attribution (prawny tekst), numery workflow-step (1-4),
workflow-state debug labels (OffShift/WorkingMobile/AwaitingDeparture itp.).

#### Kubelek C — zostaje kolorem/tintem (NIE sprite)

Paski morale/fatigue/capacity (`█░▓` + kolor), gwiazdki skill (★/☆ fill),
statusy zatrudnienia (kropka/tlo kolor), statusy dyspozytora/ruchu,
pending/research/payment (tlo wiersza), component-health, parts-stock,
breakdown-severity (reuse alert + tint), slot-status, fazy ZNTK, statusy obiegu
(active/suspended/archived kropka), tokeny `color-*` UITheme, `vehicle-status-dot`,
wykres bilansu (slupki proceduralne), hotel-tier roznicowanie (tint),
markery mini-mapy proceduralne (waypoint/ghost/polyline/wezel/signal —
LineRenderer/quad), checkbox/radio (shapes + tint).

### Decals - catalog obecny

`decal_catalog.json` zawiera obecnie 29 pozycji:

- `digit-0` ... `digit-9`
- `custom-text`
- `company-name`
- `icon-wheelchair`
- `icon-bicycle`
- `icon-wifi`
- `icon-usb`
- `icon-toilet`
- `icon-quiet`
- `icon-reservation`
- `icon-first-class`
- `icon-child`
- `icon-restaurant`
- `warning-triangle`
- `warning-no-lean`
- `warning-no-smoking`
- `arrow-up`
- `arrow-down`
- `arrow-left`
- `arrow-right`

Do EA wystarczy zrobic 27 real PNG dla wpisow z niepustym
`spriteResourcePath`. `custom-text` i `company-name` sa tekstowe i nie
wymagaja bitmapy.

---

## Styl

Wspolny jezyk wizualny:

- **Kolorowe ikony (NIE tylko czarno-biale)** — decyzja gracza 2026-06-15. Flat,
  ograniczona SPOJNA paleta (max ~2-3 kolory per ikona), zharmonizowana z `UITheme`/PKP.
- Geometryczne, czytelne sylwetki; bez mikrodetalu (musza dzialac przy 16/24/32 px).
- Stroke 2-3 px w source 64 px albo proporcjonalny w SVG.
- Przezroczyste tlo, bez baked shadow.
- UI icons: **pelen kolor zachowany, BEZ hue-tintu** przez `UITheme`. Stan disabled =
  przygaszenie alpha; stan aktywny sygnalizuje przycisk (accent bar + tlo + bold label),
  nie przekolorowanie ikony. Paleta dobrana tak, by ikona czytala sie na jasnym i na
  akcentowym (navy) tle przycisku — unikac ciemnego navy w samej ikonie.
- Decals powinny dzialac jako czarne lub biale maski na kolorze gracza.
- Nie uzywac tekstu w ikonach, chyba ze jest to celowo znak typu `1`, `Aa`,
  numer, klasa albo strzalka.

---

## Integracja w kodzie + interakcja (MUI-11)

> **Zaimplementowane 2026-06-15.** Mechanizm wpięcia ikon + reakcja na naciśnięcia.

**Mechanizm wpięcia (Depot toolbary):** centralny builder
`DepotUIPanelPrimitives.CreateOptionButton(parent, name, icon, label, w, h, Sprite iconSprite = null)`.
Gdy `iconSprite != null` → ikona renderuje się jako tintowalny `Image`-dziecko zamiast
3-literowego glifu TMP. Gdy `null` → fallback do glifu (`icon` string). **Wstecznie
kompatybilne** — istniejące call-site bez sprite'a działają bez zmian. Refactor pokrywa
**sub-toolbary** (Track/Catenary/Path/Room) + RoomBuildPanel (idą przez ten builder).
**Uwaga:** główny `BuildMenuUI` (pasek TOR/EL/DR/POM/X) oraz `ToolbarUI` mają WŁASNE inline
buildery — obsługę ikon dodaje się tam osobno, analogicznym wzorcem (pole `iconImage` +
warunek sprite/glif + tint w `UpdateVisuals`). `BuildMenuUI` zrobione 2026-06-15 (pilot).
TopBar / Personnel / Maintenance / Timetable → osobny sweep MUI-12.

**Loader:** `RailwayManager.SharedUI.UiIcons.Get(id)` (np. `"ico_toolbar_track"`) →
`Resources.Load<Texture2D>("Icons/"+id)` → `Sprite.Create` + cache; null gdy brak → fallback
glif. SVG w Unity 6 (wbudowany moduł Vector Graphics) importuje się jako **Texture2D**
(Generated Asset Type; druga opcja „UI Toolkit Vector Image" jest dla UIElements, nie uGUI).

**Ładowanie + stan:** ikony to **kolorowe sprite'y** (pełen kolor) na przezroczystości w
`Assets/Resources/Icons/<id>` (spójnie z `Resources/Decals/`). `ApplyOptionButtonState`
trzyma `IconImage.color = white` (pełen kolor) gdy odblokowane, a przy locked przygasza
(`alpha 0.35`). **Bez hue-tintu** — kolor jest własnością ikony. Stan active/hover sygnalizuje
przycisk (ColorBlock tła + accent bar + bold label), NIE przekolorowanie ikony. Uwaga: paleta
ikon musi czytać się na akcentowym (navy) tle active — walidacja przy wpięciu w realny toolbar
(jasne/średnie tony, unikać ciemnego navy w samej ikonie). Decals zostają osobno (mono maski
tintowane na kolor gracza).

**Reakcja na naciśnięcia (infra istnieje):** `UITheme.ApplyButtonStyle` ustawia
`Button.transition = ColorTint` + `CreateButtonColorBlock` (normal / highlighted=hover /
pressed / selected / disabled, `fadeDuration` 100 ms). Toolbary dodatkowo mają pasek-akcent
przełączany w `ApplyOptionButtonState`. Zgodne z `visual-style-guide.md` §4.4 (hover/press/
selected/disabled muszą się wyraźnie różnić, ruch 100-250 ms, bez gumowego bounce) i CBM-target
(100 ms snappy). Opcjonalny subtelny scale-punch na press (≈0.96, 100 ms) → MUI-14 polish.

**CBM (`depot-visual-direction.md` + `asset-needs-by-system.md`):** ikona pozostaje
**monochromatyczną maską**; kolor-akcent kategorii akcji (pomarańcz/zielony/…) niesie
**przycisk** (accent bar / tint tła), NIE sama ikona.

**Format dostawy (do weryfikacji w Unity):** preferowany SVG (skalowalny, ostry 16-64 px).
Jeśli projekt importuje `.svg` jako Sprite (pakiet Vector Graphics) → SVG bezpośrednio w
`Resources/Icons/`. Jeśli nie → te same maski jako PNG 256 (import-settings wyżej). Resolver
`Resources.Load<Sprite>($"Icons/{id}")` z null-fallback (brak pliku → builder pokazuje glif TMP)
działa dla obu formatów. Próbka 3 plików SVG (`ico_toolbar_track`, `ico_misc_settings`,
`ico_misc_alert`) wrzucona do `Resources/Icons/` jako test importu.

## Wariant wizualny — Wariant B (decyzja 2026-06-15)

Wybrany kierunek: **glif (biały LUB kolorowy) na kolorowym kafelku** (styl City Bus Manager). Kolor
kafelka niesie kategorię akcji; glif **nie musi być biały** — może być biały, jasny lub kolorowy,
byle miał **silny kontrast** na kafelku (jasne/jaskrawe tony, ew. subtelny jasny obrys). Decyzja
2026-06-15: NIE wymuszamy bieli (decale to wyjątek — mono-maska tintowana na kolor gracza).
Plusy: spójność, czytelność na ciemnym UI (`AppBackground 14141F`), stany hover/press/aktywny =
jaśniejszy/ciemniejszy kafelek + biały pasek-akcent. To unieważnia wcześniejszy zapis „kolorowe
ikony, bez hue-tintu" — kolor jest na kafelku, nie w glifie.

Paleta kafelków per kategoria (hex): Toolbar/TrackBuild/FleetConfigurator `#3E7CB0`,
Catenary `#C8893C`, Path `#4F9E5B`, RoomBuild/RoomAction/Furniture `#2E9080`, Outdoor `#3E97B0`,
Modernization `#5A6BB0`, Maintenance `#D98A3D`, Personnel `#7E6BA8`, Timetable `#4E63B0`,
Map `#5E7A99`, Economy `#4F9E5B`, Misc `#4A5566`; destrukcyjne (Demolish) `#C44C3A`.

Pilot wpięty w `BuildMenuUI` 2026-06-15 (5 narzędzi: glif + kafelek kategorii). Kod tintuje przez
`Color.white` (identyczność) → kolorowy sprite pokaże swoje kolory bez zmian, więc kolorowe ikony
działają bez zmiany kodu.

**Katalog promptów AI:** `docs/design/icon-prompts.json` — per ikona gotowy prompt (preamble
„white-glyph" + subject) do generacji w Grok/ChatGPT. Wyjątki: Decals = mono maska na taborze,
Map = 2-kolorowy marker. To realizuje wymóg „przy każdej ikonie prompt".

## Press feel + zakres (2026-06-15)

**„Ładne naciśnięcia":** komponent `SharedUI.ButtonPressFeedback` — subtelny scale-punch
(1.0→0.94, ~100 ms, unscaled time = działa w pauzie, bez gumowego bounce) uzupełniający
ColorBlock tint. **Auto-attach** w 5 centralnych builderach: `UIBuilders.MakeButton` /
`MakeIconButton`, Depot `DepotUIPanelPrimitives.CreateOptionButton` + `BuildMenuUI.CreateToolButton`,
MainMenu `MenuScreenPrimitives.CreateButton`. Przyciski spoza tych builderów dostają go w sweepie
MUI-12 (lub `AddComponent<ButtonPressFeedback>()` ręcznie).

**Zakres — domknięty (pełny research 2026-06-15):** CTA MainMenu (Nowa gra / Kontynuuj / Wczytaj /
Ustawienia / Multiplayer / O grze / Wyjście), GameCreator, ekrany, popupy i wszystkie tekstowe
przyciski SĄ już objęte w katalogu (446 ikon). Tekstowe CTA = **ikona + tekst** (nie icon-only),
z press feel. `needsPressFeedback=true` przy 282 wpisach — to klikalne przyciski/zakładki.

## Licencje

1. AI/custom icons - zachowac prompt/source note w asset trackerze, potwierdzic
   komercyjne uzycie.
2. game-icons.net - CC BY 3.0, wymaga atrybucji w `THIRD_PARTY_NOTICES.md`.
3. Simple Icons / brand logos - sprawdzic licencje i trademark rules per marka;
   nie traktowac tego jak zwyklych ikon UI.
4. Ikony z Asset Store - trzymac invoice/license w prywatnym asset logu.
5. Nie dodawac realnych logotypow przewoznikow do decals bez osobnej decyzji.

---

## Acceptance checklist

Kazda ikona/decal przed merge:

- [ ] Czytelna przy 16 px, 24 px, 32 px i 64 px.
- [ ] Krawedzie nie sa rozmyte ani poszarpane po imporcie Unity.
- [ ] Tlo jest w pelni transparentne.
- [ ] Dziala po tintowaniu na jasnym i ciemnym tle.
- [ ] Nazwa pliku zgadza sie z konwencja.
- [ ] Dla decals: `spriteResourcePath` zgadza sie z lokalizacja w `Resources/`.
- [ ] Licencja/source wpisane w `THIRD_PARTY_NOTICES.md` lub asset trackerze.
- [ ] Nie ma tekstu, ktory powinien zostac lokalizowany.
- [ ] Tooltip/caption w UI nadal wyjasnia akcje.

---

## Kolejnosc wdrozenia

1. **MUI-11 pilot:** wygenerowac 12 ikon z tabeli pilot, wpiac w jeden builder
   `UIBuilders.MakeIconButton`.
2. **Decal prep:** dodac `Assets/Resources/Decals/*.png` dla wpisow katalogu.
3. **FC renderer follow-up:** `BuildSymbolCell` i `PaintPreview3D.BuildDecal`
   powinny ladowac sprite przez `spriteResourcePath`, z fallbackiem do placeholdera.
4. **MUI-12 sweep:** zastapic TMP-label buttons ikonami w toolbarach i popupach.
5. **Map icons:** po decyzji o typach stacji podmienic placeholder cube na
   sprite/mesh marker z jasna hierarchia.
