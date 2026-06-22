# ADR-0002: Bloki semaforowe station-to-station z kaskadowym fallbackiem

**Status:** Accepted
**Date:** 2026-04-14 (commits 2808722, 378a4b7, c8aaabd)
**Context:** M4 Timetable — system rezerwacji odcinków toru

## Context

Pociągi potrzebują systemu rezerwacji odcinków toru żeby uniknąć kolizji (dwa pociągi na tym samym fragmencie jednotorowej linii). W realnej kolei używa się **bloków semaforowych** — fragment toru między dwoma semaforami. Na jednym bloku może być max jeden pociąg.

**Dane z OSM:**
- `railway=signal` POI ~5911 sygnałów na warmińsko-mazurskim
- Z function tag: 374 exit, 181 entry, 174 block (SBL), 120 intermediate
- Większość sygnałów **nie ma** `railway:signal:*:function` — to tarcze ostrzegawcze, manewrowe, speed_limit, itp.

**Próby wcześniejsze:**
1. **Sampling co N-ty node** — 100 rezerwacji per trasa, grube przybliżenie, bloki nie pasowały do rzeczywistości
2. **Blocki per edge** — każdy edge był blokiem, co dawało 186k bloków globalnie, bezużyteczne
3. **Junction nodes jako boundary** — 2310 junctions + 5816 signals = 9619 boundary nodes, 103k bloków, zbyt granularne
4. **Sygnały jako boundary** — duplikaty, pary forward/backward, wyniki niespójne między kierunkami (40 vs 61 bloków)

## Decision

**Bloki station-to-station z kaskadowym fallbackiem semaforowym:**

1. **Granice bloków:** `railway=station` POI — główne stacje (nie halts/przystanki)
2. **Halt (`railway=halt`) ignorowany** — nie dzieli bloków, tylko punkt postoju na szlaku
3. **Semafory** dzielą szlak między stacjami na pod-bloki (SBL) jeśli są:
   - **exit** (najbliższy `from` station) → początek szlaku
   - **block** (SBL) → dzielenie szlaku
   - **entry** (najbliższy `to` station) → koniec szlaku
4. **Kaskadowy fallback** gdy brakuje sygnałów:
   - Oba końce mają sygnały → exit → block → entry
   - Tylko exit → exit → station
   - Tylko entry → station → entry
   - Brak sygnałów → station → station
5. **Track separation** przez `segmentId` z OSM:
   - Klucz bloku = hash 5 sample edges w środku bloku po `segmentId`
   - Single-track: oba kierunki mają ten sam segId → kolizja (poprawnie)
   - Dual-track: każdy tor ma inny segId → niezależne rezerwacje

**Optymalizacje:**
- Snap radius sygnałów do node'ów grafu: **10m** (4m odległość między torami dwutorowej + margines)
- Deduplikacja sygnałów po pozycji: precision F2 (1cm)
- Exit/entry szukane w **pierwszych/ostatnich 20% szlaku** (zapobiega łapaniu sygnałów sąsiednich stacji)
- Block signals dedupe by proximity: < 5 route indices apart są łączone (pary forward/backward na tym samym słupku)

## Consequences

### Positive
- **Działa z realnymi danymi OSM:** 29 bloków Iława↔Działdowo (zgodnie z oczekiwaniami)
- **Symetryczne wyniki:** 29 vs 28 w obie strony (różnica 1 przez niesymetrię danych OSM)
- **Track separation działa:** dwa pociągi w przeciwnych kierunkach na dwutorowej nie kolidują
- **SBL dzielenie działa:** szlak z block signalami jest dzielony na mniejsze bloki
- **Graceful degradation:** nawet jeśli OSM nie ma sygnałów (np. Olsztyn-Elbląg jednotorowa bez SBL), system używa fallback station-to-station

### Negative
- **Direction matching** nie zaimplementowany — asymetria 29/28 zamiast 30/30
- **Bloki są "logiczne", nie fizyczne** — nie są to faktyczne OSM segments, tylko logiczne zakresy trasy
- **Dedupe threshold arbitralny** (5 route indices) — mogą się zdarzyć edge cases
- **Nie działa dla niestandardowych konfiguracji** — np. linie prywatne bez znakowania

### Neutral
- Wymaga filtrowania tylko `main:function` i `combined:function` — `distant`, `crossing`, `shunting`, `speed_limit` są ignorowane
- `BlockSectionBuilder/BlockSectionGraph` kod pozostał jako infrastruktura pod możliwą przyszłą rozbudowę (dead code, do cleanup)

## Alternatives considered

1. **Pełny OSM signal network:** każdy sygnał = boundary. Odrzucone — zbyt granularne, 100k+ bloków, nierealistyczne
2. **Time-based reservations na każdym edge:** zbyt kosztowne, nie pokazuje bloków graczowi
3. **Junction-based:** 2310 junctions, nadal zbyt gęsto (rozjazdy w obrębie stacji dają micro-blocki)
4. **Manualnie zdefiniowane bloki per linia:** niepraktyczne, wymaga ogromnej ilości danych
5. **Direction matching od razu:** za skomplikowane, wymaga parsingu OSM way digitization direction — odłożone do późniejszej optymalizacji

## References

- Commits: `2808722`, `378a4b7`, `c8aaabd`
- Kod:
  - `Assets/Scripts/Timetable/Data/SignalInfo.cs` — struct semafora
  - `Assets/Scripts/Timetable/Runtime/TimetableInitializer.cs` — `LoadSignals()`, `ParseSignalFunction()`
  - `Assets/Scripts/Timetable/Runtime/ReservationManager.cs` — `BuildRouteBlocks()`, `ComputeBlockKey()`
- Dyskusja w sesji: zbadanie OSM signal metadata keys (5911 signals, 849 z function)
