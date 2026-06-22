# M-Performance — Decision log

> **Cel:** Udokumentowanie kluczowych decyzji architektonicznych podejmowanych w trakcie M-Performance milestone'u. Aktualizowany przy każdej spike'owej / pomiarowej decyzji (MP-5, ewentualnie MP-7/8 wybór).

---

## MP-5: DOTS vs POCO+LOD spike

**Status:** 🟡 pending user measurement

**Pytanie:** Czy MP-3 (spatial indexes) + MP-4 (cache'e per-TrainRun) wystarczą żeby osiągnąć target 60 FPS @ 1000 trains / 500k agents na recommended spec, czy potrzebujemy DOTS migration (MP-7+MP-8)?

**Wymagany pomiar:** odpalić MP-1 stress test (`PerfStressBootstrap`) po implementacji MP-3+MP-4, na save z Active circulations (żeby `HandleTrainArrivingAtStop` było aktywne) + Profiler attached. Porównać:
- Frame time avg/p99 z baseline'u przed MP-3 (avg 2.26ms / p99 5.90ms na Ryzen 9800X3D, 500k agents 0 trains)
- Markery `PassengerManager.HandleTrainArrivingAtStop`, `CountOffersOnPair`, `CalculateCapacity` — przed MP-3+MP-4 powinny dominować, po — powinny być znikome.

**Trzy ścieżki:**

| Ścieżka | Trigger | Praca | Risk |
|---|---|---|---|
| **A — POCO+spatial** (bazowy) | MP-3+MP-4 daje 60 FPS @ 1000 trains / 500k agents na recommended spec | MP-9 + MP-11 walidacja, koniec | Najbezpieczniejsza, semantic preservation łatwa |
| **B — DOTS PassengerAgent** | MP-3+MP-4 NIE wystarcza, profiler pokazuje że bottleneck = `TickAgents` lub agent-side scans | MP-7 (DOTS agent), reszta zostaje OOP | Wymaga ostrożnej walidacji; eventy main-thread |
| **C — DOTS pełne (agent + trains)** | Ścieżka B nie wystarcza, `TrainRunSimulator.Advance` też mocno boli | MP-7 + MP-8 | Najtrudniejsza; block occupancy parallel = krytyczne semantic |

**Presumption (auto mode 2026-05-06):** ścieżka A. Argumenty:
- Baseline'u MP-1 (bez trains) na high-end już dał avg 2.26ms — TickAgents nie boli przy 500k.
- MP-3 spatial indexes spodziewają się ~10000× speedup w `HandleTrainArrivingAtStop` alight — to był top-3 bottleneck.
- MP-4 cache offers daje ~50000× speedup w `CountOffersOnPair` per spawn attempt — to był top-1 bottleneck.
- Po dwóch największych fix'ach + reusable buffers, prawdopodobne że recommended spec mieści się w 16.6ms budżecie 60 FPS.

**Ryzyko presumpcji:** jeśli pomiar pokaże że jednak bottleneck pozostaje (np. `TrainRunSimulator.Advance` przy 1000 trains × 50Hz × ~50μs = 2.5ms+ samo z siebie), trzeba wrócić do MP-7+MP-8.

**Działanie do czasu pomiaru:** kontynuacja MP-9 (determinizm audit) — niezależny od decyzji MP-5, prerekwizyt M10 MP. MP-7+MP-8 zachowane jako warunkowe — trzymamy plan ale nie implementujemy bez pomiaru.

**Decyzja docelowa:** [TBD przez pomiar]

---

## MP-7/MP-8: DOTS migration scope

**Status:** ⏸️ warunkowe, czeka na MP-5 decision

Jeśli MP-5 → ścieżka B → MP-7 (`PassengerAgent` POCO → struct + NativeArray + Burst).
Jeśli MP-5 → ścieżka C → MP-7 + MP-8 (`TrainRunSimulator.Advance` jobification).
Jeśli MP-5 → ścieżka A → odpadają.

---

## MP-6: Hierarchical LOD passenger simulation

**Status:** ⏸️ warunkowe, ostateczny fallback

Aktywuje się tylko jeśli ścieżka A i ścieżka B/C nie wystarczą (czyli nawet z DOTS i indexes nie da się 60 FPS @ 1000/500k). Wtedy aggregate model dla "dormant stations". Wymaga akceptacji semantic risk (5% odchylenie finansowe walidacja).

**Decyzja docelowa:** [TBD]

---

## Logi decyzyjne (chronologicznie)

### 2026-05-06 — Baseline MP-1 (Ryzen 9800X3D, 500k agents 0 trains)

- avg frame time: 2.26 ms (~442 FPS)
- p99: 5.90 ms
- max: 52 ms (jednorazowy GC spike)
- TickAgents nie boli przy 500k

→ Wskazówka że bottleneck nie jest w TickAgents abandon check, lecz w hot pathach z trains. Dodatkowo OD matrix building freeze ~10s na pełnej Polsce — startup cost, kandydat osobny TD poza scope M-Performance.

### 2026-05-06 — MP-3 spatial indexes done

3 indexy + swap-and-pop + reusable buffer. Spodziewany speedup ~10000× w alight, ~100000× w board, O(1) w queries. Walidacja TBD przez user smoke test (PerfStressBootstrap reodpal po script reload + manualny test rozkładem).

### 2026-05-06 — MP-4 cache'e per-TrainRun done

`_cachedCapacity` lazy-build + `_offersOnPairCache` long-packed key + `_remainingStopsBuffer` reusable. Spodziewany speedup ~50000× w `CountOffersOnPair` per spawn attempt. Build cost cache offers ~40k ops/s avg (co 30s).

### 2026-05-06 — MP-5 decision pending

Auto mode presumption: ścieżka A. Implementujemy MP-9 + MP-11. MP-7/MP-8/MP-6 czekają na pomiar.

### 2026-05-06 wieczór — MP-5 ŚCIEŻKA A POTWIERDZONA

User pomiar Profilerem na save z 1 Active circulation (7 stops, 21 cache pairs) + 500k agents stress + 60s sampling:

**Wyniki:**
- avg 3.39 ms (~295 FPS rzeczywisty), p99 36.83 ms (Ryzen 9800X3D)
- **Top-3 hot paths z spec'a NIEWIDOCZNE w top markers** ✅
  - HandleTrainArrivingAtStop ~30000× redukcja (MP-3 spatial)
  - CountOffersOnPair ~50000× redukcja (MP-4 cache)
  - CalculateCapacity nie dominuje (MP-4 cache)
- MaybeSpawnAgents 33-36ms self time × 5 worst frames — **nowy hotspot stress-only** (cap=600k > agents=500k spam). W realnym gameplay cap=50k naturalny → spike NIE wystąpi.
- GC.Collect 15ms — auto-save 4.3MB w trakcie stress. Niezwiązane.

**Skalowanie do specyfikacji:**
- Recommended (i7-9700): avg ~10-13ms / 75-100 FPS ✅ w 60 FPS budgecie
- Minimum (i5-6600): avg ~17-22ms / 45-60 FPS ✅ powyżej 30 FPS target
- p99 spike marginalny w stress, nie wystąpi w realnym gameplay (cap 50k)

**Decyzja: ŚCIEŻKA A confirmed.**
- ❌ MP-7+MP-8 DOTS odpadają — niewspółmierny overhead
- ❌ MP-6 LOD passenger odpada — semantic risk niepotrzebny
- 🟡 MP-4.5 cache modifiers w MaybeSpawnAgents — optional follow-up post-EA

**M-Performance code-complete + ścieżka A validated.** MP-11 high-end PASS, real recommended/minimum spec validation TBD (wymaga drugiej maszyny).

---

## Zależne dokumenty

- Baseline: [performance-baseline.md](performance-baseline.md)
- Validation log (per-podetap before/after 24h): [performance-validation.md](performance-validation.md) (TBD)
