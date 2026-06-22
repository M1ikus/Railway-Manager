# ADR-0001: Union-Find dla PathfindingGraph

**Status:** Accepted
**Date:** 2026-04-11 (approx., commit 64f2733)
**Context:** M4 Timetable — pathfinding po grafie OSM railways

## Context

Przy budowaniu `PathfindingGraph` z OSM features (warstwa Railways) potrzebowaliśmy mergeować współdzielone wierzchołki między way'ami żeby otrzymać spójny graf topologiczny.

**Pierwsze podejście:** Spatial hash z fixed `cellSize` — każdy vertex snapowany do najbliższego istniejącego node'a w promieniu `cellSize` lub tworzy nowy.

**Problem:** Krótsze way'e (poniżej 20m długości) kolapsowały do pojedynczego nodu bez edge'ów, stając się isolated componentami. Graf był fragmentowany — 2961 komponentów na warmińsko-mazurskim, największy component = 9.7% nodes. Pathfinding nie działał dla większości tras.

Próbowaliśmy:
- Różne cellSize (1-100m) — mniejsze traciło więcej way'ów, większe mergowało niezwiązane równoległe linie
- Junction-only anchoring — pomagało tylko trochę
- Ręczne LOD fixing — komplikacja bez zysku

## Decision

Użyjemy **Union-Find (Disjoint Set)** podejścia:

1. Każdy vertex każdego feature startuje jako osobny "raw node"
2. Union-Find merguje pozycje w tolerancji (`cellSizeM=10f`) **transytywnie** — jeśli A≈B i B≈C, to A≈C nawet jeśli |A-C|>tolerance
3. Finalne PathfindingGraph nodes = unique UF components
4. Edges z feature chain (dedup przez `HashSet<long>(from,to)`)

**Dodatkowa optymalizacja:** `junctionOnlyMerge=true` — merguje tylko junction vertices (OSM shared nodes). Non-junction vertices NIE mergują między wayami, nawet jeśli są blisko. Zapobiega fałszywym skrótom gdzie równoległe linie się mijają.

## Consequences

### Positive
- **Spójny graf:** 47 komponentów (z 2961), największy 95.2%
- **Pathfinding działa:** A* Olsztyn Główny → Elbląg 93.8 km w 0.01s
- **Stabilne na pełnej Polsce:** nie wymaga ręcznego tuningu cellSize per region
- **Naturalne merge** — dzięki transitivity załatwia krawędzie tile'ów (feature replication)

### Negative
- **Bardziej pamięciożerne:** Union-Find wymaga `int[]` o rozmiarze `rawVertices.Count` (zamiast spatial hash który jest lazy)
- **Wymaga 2 passów:** pierwszy zbiera raw positions, drugi merguje
- **Trudniej debug:** gdy coś jest nie tak, trudniej prześledzić "skąd się wziął ten node"

### Neutral
- Wymaga `UnionFind.cs` jako dodatkowa klasa utility
- Dla małych map nie ma zauważalnej różnicy w wydajności

## Alternatives considered

1. **Większe cellSize (20-50m):** false positives — linie równoległe mergowane
2. **Spatial hash + post-processing:** dodanie heurystyk naprawiających isolated components — kod staje się nieprzewidywalny
3. **Z góry narzucone OSM relations:** nie istnieją w danych OSM railways
4. **A* na surowym OSM XML bez preprocessing:** za wolne dla realtime route creation

## References

- Commit: `64f2733` — "fix(timetable): Union-Find approach..."
- Kod: `Assets/Scripts/Timetable/Runtime/PathfindingGraph.cs` — metoda `BuildFromFeaturesUnionFind`
- Kod: `Assets/Scripts/Timetable/Runtime/UnionFind.cs`
- Debug commits: 77fc573, 902ddac, 2b9fc74 (historia prób różnych cellSize)
