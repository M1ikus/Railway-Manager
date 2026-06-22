# Country DLC and map packages

> **Cel:** Notatka projektowa po rozmowie o modelu biznesowym i technicznej
> obsludze przyszlych krajow jako DLC. To nie jest spec do natychmiastowej
> implementacji; ma pilnowac kierunku, zeby obecne decyzje mapowe nie zamknely
> drogi do Czech, Niemiec i innych krajow.

---

## Decyzja produktowa

**Podstawka:** pelna Polska jako kompletny sandbox.

**DLC:** inne kraje jako platne ekspansje mapowe, np. Czechy, Slowacja, Niemcy,
Austria lub inne regiony, w zaleznosci od zainteresowania graczy i mozliwosci
researchu.

Uzasadnienie:
- najmocniejsza przewaga projektu to autentyczna znajomosc polskiej kolei;
- dodanie wielu krajow do podstawki zwieksza zakres, research i ryzyko bledow;
- gracze symulatorow/transport tycoonow akceptuja ekspansje map jako DLC, jesli
  podstawka jest pelna i uczciwa;
- mechaniki gry powinny byc rozwijane darmowymi aktualizacjami, a platne DLC
  powinno sprzedawac nowe obszary, dane, scenariusze i lokalny content.

Nie planowac day-one DLC ani season passa na starcie. Najpierw podstawka/EA
musi udowodnic core loop.

---

## Stan obecny

Projekt jest **czesciowo przygotowany mapowo**, ale nie ma jeszcze systemu DLC.

Co pomaga:
- format mapy v7 (`FORMAP03`) jest kafelkowany, wielowarstwowy i wspiera LOD;
- `MapLoader` laduje plik binarny przez sciezke `mapFileName`, wiec technicznie
  moze wczytac inny dataset;
- generator `.osm.pbf -> .bin` jest osobnym narzedziem, co pasuje do pipeline'u
  budowania map dla wielu krajow.

Co jest jeszcze polsko-centryczne:
- runtime zaklada jeden aktywny plik mapy, aktualnie `Maps/Poland/...`;
- logika rozkladow ma polskie pojecia: wojewodztwa, obszary konstrukcyjne,
  katalog IRJ, kod kraju EVN `51`;
- `station_tracks.json` jest globalny, a docelowo powinien byc per pakiet mapy;
- inicjalizacja timetable potrafi synchronicznie doladowac wszystkie tile do
  budowy grafu, co bedzie problemem przy wielu krajach.

Wniosek: nie trzeba teraz implementowac DLC, ale nowy kod powinien unikac
zakladania, ze "swiat = Polska".

---

## Docelowa koncepcja

Zamiast jednego wielkiego pliku Europy, kazdy kraj/region powinien byc osobnym
pakietem mapy.

Przykladowa struktura:

```text
StreamingAssets/Maps/
  catalog.json

  PL/
    map_package.json
    map.bin
    rail_graph.bin
    station_tracks.json
    border_gateways.json
    country_rules.json

  CZ/
    map_package.json
    map.bin
    rail_graph.bin
    station_tracks.json
    border_gateways.json
    country_rules.json
```

Przykladowy `map_package.json`:

```json
{
  "id": "pl",
  "displayName": "Polska",
  "countryIso": "PL",
  "binPath": "Maps/PL/map.bin",
  "railGraphPath": "Maps/PL/rail_graph.bin",
  "stationTracksPath": "Maps/PL/station_tracks.json",
  "borderGatewaysPath": "Maps/PL/border_gateways.json",
  "countryRulesPath": "Maps/PL/country_rules.json",
  "mapVersion": "2026.04",
  "requiredDlcId": null,
  "projection": "game-europe-v1"
}
```

Kluczowa zasada: wszystkie pakiety musza uzywac tej samej projekcji i tego samego
originu swiata. Jesli Polska i Czechy beda niezaleznie normalizowane do `0,0`,
nie da sie sensownie laczyc tras transgranicznych.

---

## CountryRules

Polskie reguly nie powinny byc hardcoded w logice uniwersalnej. Docelowo kraj
powinien dostarczac wlasne reguly:

```json
{
  "countryIso": "PL",
  "evnCountryCode": "51",
  "primaryAdminLevel": 4,
  "primaryAdminName": "wojewodztwo",
  "trainCategorySystem": "PL_IRJ",
  "trainNumberingSystem": "PL_CONSTRUCTION_AREA",
  "defaultVoltage": "3000 V DC",
  "currency": "PLN"
}
```

Implementacyjnie warto docelowo miec:
- `AdminRegionResolver` zamiast samego `VoivodeshipResolver`;
- interfejs lub strategie dla klasyfikacji kategorii pociagow;
- krajowy generator/walidator numerow pociagow;
- krajowy kod EVN w danych, nie w stalej `PolandCountryCode`.

---

## Stacje graniczne bez DLC

Podstawka moze obslugiwac ruch miedzynarodowy w wersji "off-map", bez pelnej mapy
sasiedniego kraju.

Przykladowy `border_gateways.json`:

```json
{
  "gateways": [
    {
      "id": "pl_cz_zebrzydowice",
      "country": "PL",
      "stationName": "Zebrzydowice",
      "targetCountry": "CZ",
      "targetName": "Bohumin",
      "requiresMapPackage": "cz",
      "offMapTravelMinutes": 18,
      "offMapDistanceKm": 22
    }
  ]
}
```

Jesli gracz nie ma DLC, pociag moze:
- dojechac do stacji granicznej;
- przejsc w stan `OffMapInternational`;
- zniknac z mapy na okreslony czas;
- wrocic, zakonczyc kontrakt albo wygenerowac przychod/koszt za odcinek poza
  mapa, w zaleznosci od projektu ekonomii.

To pozwala miec polska podstawke z posmakiem polaczen miedzynarodowych bez
produkcji calego kraju.

---

## Stacje graniczne z DLC

Po zainstalowaniu sasiedniego kraju gateway powinien zmienic sie z "off-map" w
prawdziwe polaczenie grafow.

Preferowane podejscie:
- generator zapisuje stabilne identyfikatory OSM (`osm_way_id`, `osm_node_id`)
  dla krawedzi, wezlow, stacji i sygnalow;
- pakiety map sa generowane w tej samej przestrzeni wspolrzednych;
- `border_connectors.json` laczy znane wezly po obu stronach granicy;
- pathfinding widzi polaczenie jako normalna krawedz grafu.

Przykladowy konektor:

```json
{
  "connectors": [
    {
      "fromPackage": "pl",
      "fromNodeOsmId": 123456,
      "toPackage": "cz",
      "toNodeOsmId": 789012,
      "lineName": "Zebrzydowice-Bohumin"
    }
  ]
}
```

Laczenie tylko po pozycji jest mozliwe jako fallback, ale mniej bezpieczne. Do
DLC lepiej miec OSM IDs w danych.

---

## Render mapy vs graf kolejowy

Docelowo warto rozdzielic dane renderingu od danych gameplayowych:

```text
map.bin        -> kafle renderingu, warstwy wizualne, LOD, budynki, woda, lasy
rail_graph.bin -> lekki graf kolejowy, stacje, perony, sygnaly, edge metadata
```

Powod:
- rendering powinien dzialac tile streamingiem;
- pathfinding i timetable nie powinny ladowac wszystkich kafli renderingu tylko po
  to, zeby zbudowac graf;
- wiele krajow bedzie wymagalo laczenia lekkich grafow, nie wielkich mesh datasetow.

To jest wazny kierunek przed pelna Polska + DLC, ale nie blokuje aktualnych M5/M9.

---

## Multi-map runtime

Przy wielu pakietach nie wystarczy sam `tileId`, bo rozne pakiety moga miec tile
o tym samym identyfikatorze lub miec overlap/buffer przy granicy.

Klucze runtime powinny byc docelowo:

```text
(packageId, tileId)
(packageId, nodeId)
(packageId, stationId)
```

albo uzywac globalnych stabilnych ID generowanych przez pipeline.

Proponowane komponenty docelowe:
- `MapPackageCatalog` - lista dostepnych/zainstalowanych pakietow;
- `InstalledContentService` - sprawdza, czy DLC/pakiet jest dostepny;
- `MapPackageLoader` - laduje manifest, dane kraju i sciezki plikow;
- `MultiMapLoader` - sklada aktywny swiat z jednego lub wielu pakietow;
- `BorderGatewayService` - obsluguje off-map i realne konektory.

---

## Save/load

Save nie powinien zapisywac tylko `mapFile = poland.bin`. Powinien zapisac:

```json
{
  "world": {
    "activeMapPackages": ["pl"],
    "mapPackageVersions": {
      "pl": "2026.04"
    }
  }
}
```

Dzieki temu save bedzie wiedzial, jakich DLC wymaga. Jesli brakuje pakietu,
gra moze pokazac komunikat i nie ladowac save'a albo uruchomic tryb ograniczony,
jesli brakujacy kraj nie byl krytyczny dla aktywnych obiektow.

---

## Roadmapa implementacyjna

1. **Teraz / przed M-PL**
   - Zostaw Polske jako jedyny aktywny kraj.
   - Nie implementuj DLC runtime.
   - Unikaj nowych hardcodow typu `Poland`, `voivodeship`, `PL` w generycznych
     systemach.

2. **M-PL / pelna Polska**
   - Wprowadz `MapPackage` dla Polski, nawet jesli jest tylko jeden pakiet.
   - Przenies `station_tracks.json` do katalogu pakietu mapy.
   - Zapisuj w save `activeMapPackages`.

3. **Przed pierwszym DLC**
   - Dodaj `CountryRules`.
   - Zmien `VoivodeshipResolver` w kierunku ogolnego `AdminRegionResolver`.
   - Dodaj `border_gateways.json` i off-map international.
   - Dodaj OSM IDs do formatu generowanego przez narzedzie mapowe.

4. **Pierwsze DLC**
   - Zbuduj drugi pakiet mapy w tej samej projekcji.
   - Polacz grafy recznymi/OSM-ID konektorami granicznymi.
   - Uruchom testy tras transgranicznych i save/load z brakujacym DLC.

---

## Otwarte pytania

- Ktory kraj pierwszy: Czechy, Slowacja, Niemcy Wschodnie czy inny region?
- Czy gracze bez DLC moga dolaczac do multiplayer hosta z DLC?
- Czy off-map international generuje przychod, czy tylko sluzy jako kontrakt/wyjscie
  z mapy?
- Czy `rail_graph.bin` powstanie razem z M-PL, czy dopiero przed pierwszym DLC?
- Jak wersjonowac mapy, gdy OSM data zmienia sie po wydaniu save'ow?

