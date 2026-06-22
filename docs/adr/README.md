# Architecture Decision Records (ADR)

> **Cel:** Zapisywanie kluczowych decyzji projektowych z uzasadnieniem.
> Nie "co", ale **dlaczego** tak zrobiliśmy.
>
> **Format:** Lightweight ADR (Michael Nygard's format)

---

## Format

Każdy ADR to pojedynczy plik markdown w `docs/adr/NNNN-title.md` gdzie `NNNN` to 4-cyfrowy numer sekwencyjny.

### Template

```markdown
# ADR-NNNN: Title

**Status:** Accepted | Proposed | Superseded by ADR-XXXX | Deprecated
**Date:** YYYY-MM-DD
**Context:** M-X milestone / system

## Context
(Jaki problem próbujemy rozwiązać? Co zmusiło do podjęcia decyzji?)

## Decision
(Co zdecydowaliśmy? Konkretnie.)

## Consequences
### Positive
- ...

### Negative
- ...

### Neutral
- ...

## Alternatives considered
(Co jeszcze rozważaliśmy i dlaczego odrzucone)

## References
(Linki do code, commits, dyskusji)
```

---

## Lista ADRs

- [ADR-0001: Union-Find dla PathfindingGraph](0001-union-find-pathfinding.md)
- [ADR-0002: Bloki semaforowe station-to-station z signal fallback](0002-signal-block-system.md)
- [ADR-0003: New Input System Only mode](0003-new-input-system.md)
- [ADR-0004: M5 Obiegi przed M9 Ruch](0004-circulations-before-movement.md)

---

## Kiedy pisać ADR

Pisz ADR gdy:
- Podjąłeś decyzję która **zmienia architekturę** (nie tylko lokalny fix)
- Decyzja miała **alternatywy** które odrzuciłeś (pokaż dlaczego)
- Decyzja **wpłynie na przyszłą pracę** (referencja dla kolejnych milestone'ów)
- Decyzja jest **nieoczywista** (żeby za 6 miesięcy nie szukać "dlaczego tak")

**NIE pisz ADR dla:**
- Lokalnych bug fixes
- Rutynowych refaktorów
- Drobnych decyzji kosmetycznych
- Rzeczy które są "oczywiste" (np. "użyjemy C# bo Unity")

---

## Kiedy ADR jest "Superseded"

Gdy podejmujesz nową decyzję która zastępuje poprzednią:
1. Utwórz nowy ADR z `Status: Accepted` i notką `Supersedes ADR-XXXX`
2. Edytuj stary ADR: `Status: Superseded by ADR-YYYY`
3. **Nie usuwaj starego** — zostaje jako historia
