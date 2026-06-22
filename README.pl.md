# Railway Manager

*English version: [README.md](README.md)*

Symulator zarządzania firmą przewozów kolejowych w Polsce, oparty na rzeczywistych
danych [OpenStreetMap](https://www.openstreetmap.org/). Gracz wciela się w operatora
kolejowego — buduje własną zajezdnię w pełnym 3D, planuje rozkłady jazdy na realnej
sieci kolejowej, zarządza flotą taboru i personelem.

**Status:** Pre-alpha, aktywny rozwój.
**Platforma:** PC (Steam), Early Access.
**Engine:** Unity 6000.4.0f1 (zobacz `ProjectSettings/ProjectVersion.txt`), Universal Render Pipeline (URP 17.4), New Input System.

> **Uwaga:** to repozytorium **source-available** — udostępnione do podglądu i referencji,
> nie jest open source. Zobacz [LICENSE](LICENSE).

---

## Uruchomienie

1. Otwórz projekt w Unity Hub (`6000.4.0f1` lub kompatybilna).
2. Otwórz `Assets/Scenes/MainMenu.unity` — punkt wejścia gry.
3. Kliknij Play.

Dla pracy nad modułem: `Depot.unity` (edytor zajezdni 3D), `MapScene.unity`
(mapa Polski 2D), `GameCreator.unity` (kreator nowej gry).

---

## Struktura projektu

```
Assets/
├── Scenes/                — sceny Unity (MainMenu, GameCreator, Depot, MapScene)
├── Scripts/               — kod C# pogrupowany w assemblies (granice przez asmdef)
│   ├── Core/              (RailwayManager.Core)        — bez zależności
│   ├── Fleet/             (RailwayManager.Fleet)       — dane taboru
│   ├── SharedUI/          (RailwayManager.SharedUI)    — wspólne UI (TopBar)
│   ├── MainMenu/          (RailwayManager.MainMenu)
│   ├── GameCreator/       (RailwayManager.GameCreator) — kreator nowej gry
│   ├── Depot/             (RailwayManager.Depot)       — system zajezdni 3D
│   ├── Map/               (RailwayManager.Map)         — mapa 2D + OSM
│   ├── Timetable/         (RailwayManager.Timetable)   — rozkłady, obiegi, ruch, ekonomia
│   ├── Maintenance/       (RailwayManager.Maintenance) — utrzymanie taboru, warsztaty
│   ├── Personnel/         (RailwayManager.Personnel)   — personel, zmiany, cykl dnia 3D
│   └── SaveLoad/          (RailwayManager.SaveLoad)    — zapis/odczyt (najwyższa warstwa)
├── Models/                — modele 3D (placeholdery do czasu finalnego artu)
├── Materials/             — materiały i shadery
├── StreamingAssets/
│   └── Maps/Poland/       — skompresowane binarne mapy OSM (*.bin)
├── Plugins/               — zewnętrzne DLL (K4os.Compression.LZ4, LibTessDotNet)
└── Tests/                 — testy EditMode/PlayMode (NUnit, Unity Test Framework)
```

---

## Architektura

Projekt używa **Assembly Definitions** do wymuszenia granic modułów. Graf zależności
(strzałka w górę = „warstwa wyżej zależy od niższej"):

```
Core  (bez zależności)
  ↑
Fleet · SharedUI · MainMenu
  ↑
GameCreator · Depot
  ↑
Map
  ↑
Timetable    (rozkłady, obiegi, ruch 2D, ekonomia)
  ↑
Maintenance  (utrzymanie taboru, warsztaty)
  ↑
Personnel    (personel, zmiany, cykl dnia 3D)
  ↑
SaveLoad     (najwyższa warstwa — referuje wszystko, nikt nie referuje jej)
```

**Kierunkowość:** Depot **nie** zależy od Map (dawny cykl zerwany przez przeniesienie
`DepotRailwayIntegration` do Map). Timetable referuje Depot tylko dla cross-system glue
(handshake Map↔Depot, warsztaty), nie dla czystej logiki rozkładów.
Pełne konwencje: [`docs/conventions.md`](docs/conventions.md).

---

## Kluczowe systemy

- **Depot (3D)** — edytor i symulacja zajezdni: tory, rozjazdy, sieć trakcyjna,
  ścieżki/parkingi, hale. Tabor stoi na torach, można nim manewrować. Koordynator:
  `DepotManager`. Szczegóły: [`docs/depot/README.md`](docs/depot/README.md).
- **Map (2D)** — Polska z OpenStreetMap: tory, stacje, LOD, chunk loading, renderowane
  jako meshe z danych binarnych (`Assets/StreamingAssets/Maps/Poland/*.bin`).
- **Fleet** — katalog taboru (EN57, EU07, FLIRT, EU160 Griffin, SA138, Impuls…),
  konfigurator pojazdów, rynek wtórny, koszyk zamówień, system przeglądów.
- **Sieć trakcyjna** — 3-stopniowy pipeline w `Depot/Catenary/`: klasyfikacja stref →
  generacja przewodów → optymalizacja podpór, na realnych danych PKP.
- **Rozkłady (Timetable)** — kreator rozkładów: kategorie handlowe IRJ, trasy,
  częstotliwość, kalendarz dni, time-aware A* pathfinding, kaskadowe bloki sygnałowe.
- **Obiegi (Circulations)** — obiegi taboru niezależne od rozkładów, per-day przydział
  pojazdów z drag & drop, lock składu, auto-generator.
- **Ruch pociągów** — symulacja świata 2D + zajezdni 3D w FixedUpdate (50 Hz,
  deterministyczne, MP-friendly), z handshakiem Map↔Depot.
- **Ekonomia** — agent-based pasażerowie, OD matrix, system biletowy, modyfikatory
  popytu (rush hour / weekendy / sezonowość), dotacje wojewódzkie, reputacja.
- **Utrzymanie (Maintenance)** — zużycie per-komponent (8 komponentów), awarie +
  ratunek, warsztaty (kanały serwisowe), magazyn części, zewnętrzne ZNTK.
- **Personel (Personnel)** — 10 ról, skille 1–5★, turnusy załóg, harmonogramy
  multi-day z hotelami, cykl dnia 3D (pracownicy chodzą po zajezdni).
- **Save/Load** — pojedynczy `.rmsave` (gzip + podpis HMAC) z sekcjami per-moduł,
  wersjonowanie schematu + migrator chain, i18n (PL/EN/DE/CZ).

---

## Konwencja językowa

To projekt polski (tematyka polskiej kolei) i kod to odzwierciedla:

- **Polski** dla nazewnictwa domenowego i większości komentarzy — np. `Pojazd`,
  `Obieg`, `Rozkład`, `Stacja` oraz kody kategorii IRJ (`EI`, `EN`, `RO`, `MP`…),
  które odwzorowują realne nazewnictwo PKP.
- **Angielski** dla generic infrastructure — `Service`, `Manager`, `Factory` itd.

Planowana jest stopniowa migracja kodu w stronę angielskiego. README dostępne po
polsku i [angielsku](README.md).

---

## Wymagania deweloperskie

- **Unity 6000.4.0f1** (lub kompatybilna)
- **Git** — repo używa `.gitattributes` (normalizacja LF) i `.editorconfig`
- **Git Smart Merge** zalecany dla plików scen/prefabów Unity (YAML)
  (patrz `docs/setup/git-smartmerge.md`)
- **IDE:** Rider / Visual Studio / VS Code (wszystkie czytają `.editorconfig`)

---

## Dane mapy i licencje

Publiczne repozytorium celowo **nie** zawiera pełnych bundle'i map OSM
(`poland-v7.bin`, `init-state-pl.bin`) — są duże i dystrybuowane oficjalnymi kanałami
gry/danych.

Dane mapy © OpenStreetMap contributors, na licencji Open Database License (ODbL) 1.0.
Model dystrybucji danych, atrybucja OSM i placeholdery pod download opisane w
[docs/DATA_LICENSES.md](docs/DATA_LICENSES.md). Komponenty zewnętrzne — inwentarz w
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

---

## Licencja

Repozytorium jest obecnie publiczne wyłącznie do **podglądu i referencji**; **nie jest
open source**. Aktualne warunki w [LICENSE](LICENSE). Projekt prywatny, w aktywnym
rozwoju; materiały zewnętrzne pozostają objęte własnymi licencjami i warunkami.
