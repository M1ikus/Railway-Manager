# ADR-0004: M5 Obiegi przed M9 Ruch pociągów

**Status:** Accepted
**Date:** 2026-04-14
**Context:** Milestone reorganization — kolejność M5 vs M9

## Context

Oryginalny plan milestone'ów zakładał kolejność:
```
M4 Timetable → M5 Circulations → M6 Economy → M7 Maintenance → M8 Personnel → M9 Train movement
```

Podczas reorganizacji rozważaliśmy przeniesienie M9 wcześniej (żeby uzyskać "żyjący" game loop szybciej):
```
M4 → M9 → M6 → M-PL → M5 → reszta
```

Problem: **M9 Ruch pociągów wymaga wiedzy o tym, który fizyczny pojazd wykonuje który `TrainRun`**. Bez obiegów `Timetable.compositionMode` pozostaje w `Symbolic` (etykieta "3B+WR+2A"), a M9 nie ma jak:
- Dobrać właściwego prefaba 3D (EN57 vs EU07 vs SM42)
- Naliczać amortyzację i zużycie per konkretny pojazd
- Walidować że pojazd jest dostępny (nie zajęty w innym obiegu)
- Emitować events `OnKmTraveled` per pojazd (dla M7 Maintenance)

## Decision

**M5 Obiegi PRZED M9 Ruch pociągów.**

Nowa kolejność:
```
M4 → M4.5 (finalizacja UI) → M5 (pełne obiegi) → M9 (ruch + 3D modele) → M-PL (pełna Polska) → M6 (ekonomia) → reszta
```

**M5 dostaje zakres rozszerzony:**
1. Model danych `Circulation` (łańcuch `TrainRun`ów)
2. **Przypisywanie taboru** (`assignedFleetVehicleIds: List<int>`)
3. Update `Timetable.compositionMode` Symbolic → Concrete przy zatwierdzeniu obiegu
4. Walidacja konfliktów pojazdów (ten sam pojazd w 2 obiegach = error)
5. UI Circulations w MainTabBar (zakładka już zarezerwowana)
6. Dojazdy służbowe (auto-PW/LP) — można odłożyć do M5.5

## Consequences

### Positive
- **Poprawna architektura:** M9 TrainMovement ma wszystkie potrzebne dane (pojazd, przebieg, stan) w momencie spawnowania
- **Fleet ↔ Timetable decoupled:** M5 jest "mostem" między tymi systemami
- **Jasne zależności:** system-interactions.md pokazuje M5 jako hub dla obiegów
- **Maintenance (M7) gotowy od razu:** gdy M7 dochodzi, `conditionPercent` degradacji jest liczony per konkretny pojazd, nie symboliczny
- **Economy (M6) gotowy:** koszty eksploatacji naliczane per obieg (pojazd + personel + paliwo)

### Negative
- **Dłużej do "żyjącego" momentu:** first visible running train dopiero po M4.5 + M5 + M9
- **M5 staje się większy milestone:** musi być "pełny", nie tylko MVP

### Neutral
- Nie zmienia ogólnej długości projektu — tylko kolejność
- Wymaga że M5 ma solidny UI już na pierwszym iteration (nie można zrobić "kreator obiegu pod demo")

## Alternatives considered

1. **M9 z Symbolic pociągami** — dummy prefaby (prostokąty), żadnej integracji z Fleet
   - Odrzucone: duplikuje pracę, potem trzeba przepisać gdy M5 dojdzie
2. **M5-lite (1 pojazd per rozkład, bez obiegów)** — najmniejsze wsparcie dla M9
   - Odrzucone: nie daje prawdziwej wartości obiegów; gracz i tak chce łączyć kursy
3. **M9 przed M5, M5 jako "polish"** — ruch zanim mamy obiegi
   - Odrzucone: bez obiegów pociąg po jednym kursie siedzi bez użytku, gameplay jest rozbity

## References

- Dyskusja reorganizacji milestonów z użytkownikiem (sesja 2026-04-14)
- `docs/design/system-interactions.md` — M5 jako bridge Timetable ↔ Fleet
