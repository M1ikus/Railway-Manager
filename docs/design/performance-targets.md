# Performance targets

> **Cel:** Konkretne cele wydajnościowe dla Railway Manager, per klasa PC.
> Używane podczas M12a Performance milestone oraz przy ogólnym profiling.

---

## Klasy sprzętowe

### Minimum spec
- **CPU:** Intel i5-6600 / AMD Ryzen 5 1600 (~2016-2017)
- **RAM:** 8 GB
- **GPU:** GTX 970 / RX 480 (4 GB VRAM)
- **Storage:** SSD recommended, HDD accepted
- **OS:** Windows 10 64-bit

### Recommended spec
- **CPU:** Intel i7-9700 / AMD Ryzen 7 3700X (~2019-2020)
- **RAM:** 16 GB
- **GPU:** RTX 2060 / RX 5700 (8 GB VRAM)
- **Storage:** SSD
- **OS:** Windows 10/11 64-bit

### Optimal spec (high-end experience)
- **CPU:** Intel i7-12700 / AMD Ryzen 7 5700X+
- **RAM:** 32 GB
- **GPU:** RTX 3070 / RX 6700 XT+
- **Storage:** NVMe SSD

---

## Cele wydajnościowe

### FPS targets

| Scenariusz | Minimum | Recommended | Optimal |
|-----------|---------|-------------|---------|
| Menu główne | 60+ | 60+ | 144+ |
| Depot 3D (pusta scena) | 60 | 90 | 144 |
| Depot 3D (zabudowana zajezdnia, 10 torów, sieć trakcyjna, ~5 pociągów) | 30 | 60 | 120 |
| Mapa 2D (warmińsko-mazurskie, 0 pociągów) | 60 | 90 | 144 |
| Mapa 2D (pełna Polska, 0 pociągów) | 30 | 60 | 90 |
| Mapa 2D (pełna Polska, 50 pociągów w ruchu) | 30 | 60 | 90 |
| Mapa 2D (stress test, 200 pociągów) | 20 | 40 | 60 |
| **Mapa 2D (endgame fanatyk, 1000 pociągów / 500k agentów) — M-Performance target** | **30** | **60** | **90** |

### RAM usage

| Scenariusz | Minimum | Recommended | Optimal |
|-----------|---------|-------------|---------|
| Menu główne | <1 GB | <1.5 GB | <2 GB |
| Depot 3D | <3 GB | <4 GB | <5 GB |
| Mapa warmińsko-mazurskie | <4 GB | <5 GB | <6 GB |
| Mapa pełna Polska | <6 GB | <8 GB | <12 GB |
| Pełna gra (depot + mapa + ~50 pociągów) | <7 GB | <10 GB | <14 GB |
| **Endgame fanatyk (1000 pociągów / 500k agentów) — M-Performance target** | **n/a** | **<14 GB** | **<20 GB** |

### Loading times

| Scenariusz | Minimum | Recommended | Optimal |
|-----------|---------|-------------|---------|
| Start gry → menu | <10s | <5s | <3s |
| Menu → Depot 3D (pusta scena) | <5s | <3s | <2s |
| Menu → Mapa warmińsko-mazurskie | <15s | <10s | <5s |
| Menu → Mapa pełna Polska | <30s | <20s | <10s |
| Save game | <3s | <2s | <1s |
| Load game (pełna sesja) | <15s | <10s | <5s |

### Network (M10 Multiplayer)

| Metryka | Target |
|---------|--------|
| Ping host ↔ client | <100 ms (EU), <150 ms (EU→US) |
| Bandwidth upload (host) | <500 KB/s per gracz |
| Bandwidth download (client) | <200 KB/s |
| Packet loss tolerance | <2% |
| Sync frequency (pociągi) | 10-20 Hz |

---

## Budżety (budget constraints)

### CPU per frame (60 FPS target = 16.6 ms)

| Podsystem | Budget (ms) |
|-----------|-------------|
| Input processing | <0.5 |
| UI (Canvas draws + updates) | <2 |
| Camera + rendering prep | <1 |
| Map tile manager + LOD | <2 |
| Train simulation (M9) | <3 |
| Economy tick (M6) | <1 |
| Personnel update (M8) | <1 |
| Physics (Unity internal) | <2 |
| Script overhead (GC, etc.) | <1 |
| **Headroom** | **<3** |
| **Total** | **≤16.6** |

### Memory per system

| System | Budget (MB) |
|--------|------------|
| Unity engine + scene | <1500 |
| Map tiles (cached) | <2000 |
| Meshes + materials | <1000 |
| Textures | <1500 |
| Fleet data + prefabs | <500 |
| Timetable + reservations | <200 |
| Personnel | <100 |
| Runtime audio | <200 |
| **Total** | **<7 GB** |

### Draw calls

| Target | Batching |
|--------|----------|
| <1500 draw calls per frame | Static batching dla zajezdni, dynamic dla pociągów |
| <2000 draw calls (stress) | Przy 200 pociągach na mapie |

### Triangles (budżet trójkątów — per-asset × LOD × ile-na-ekranie)

> Pełny budżet zsyntetyzowany 2026-06-21 (recon: lista modeli z `asset-needs-by-system.md`,
> realne kotwice z bpy `BlenderSource/scripts/`, kalibracja branżowa Cities: Skylines 1/2,
> Transport Fever 2 + ogólne wytyczne PC mid-range). Powiązane: `asset-needs-by-system.md`
> (co i w jakim stylu).

**Cztery zasady ramowe:**
1. Budżetujemy **per LOD razy ile sztuk widać na ekranie**, nie suchą liczbę per model.
2. **Frontem KOSZTU per-klatkę (jedna scena) jest tor, nie tabor** — podkład (~228 tris co 0,6 m) +
   szyna (~62–110 tris co 1 m) instancjonowane na 10 torach ≈ ~880k tris naiwnie (~2/3 zajezdni) zanim
   postawisz pociąg. To *jednorazowy* problem optymalizacji systemu toru (LOD + instancing), bo
   infrastruktura to **stała, pojedyncza paleta na grę**. (NIE oznacza, że tabor jest mniej ważny — patrz pkt 3.)
3. **Tabor to inna oś ważności: kategoria OTWARTA i moddowalna.** Infrastruktura jest skończona, ale tabor
   rośnie bez końca — katalog rozszerzany przez nas, DLC oraz **potencjalne mody community**. Dlatego
   **budżet per-pojazd + kontrakt importu to twardy SPEC, nie sugestia**: każdy nowy mesh (nasz i moderski)
   musi się w niego zmieścić. Harness walidacji = `Assets/Tests/EditMode/EP07ModelImportTests.cs` (wzorzec dla
   każdego modelu); architektura modular `family→prefab` trzyma liczbę unikatowych meshy w ryzach; docelowo
   osobny *modding asset spec* (wzór Transport Fever 2 / Cities: Skylines — limity tris per LOD + rozmiary tekstur).
4. **Skala 1000 pociągów / 500k agentów = mapa 2D** (składy jako markery/billboardy, pasażerowie =
   POCO bez mesha). To problem draw calls + CPU, nie trójkątów. Ciężka geometria 3D żyje tylko
   w zajezdni (≤~5 składów w kadrze) i przy zoom-in na mapie.

**Sufity sceny (cel tris/klatkę):**

| Scena / spec | Cel |
|---|---|
| Depot 3D — **primary target: RTX 3060, 60 fps, ustawienia „ładne"** | **~2,5–3,0 mln** |
| Depot 3D — Recommended (RTX 2060), 60 fps | ~2,0 mln (LOD nieco agresywniej) |
| Depot 3D — Minimum (GTX 970 / 4 GB), 30 fps | ~1,2 mln (LOD bias + rzadszy detal) |
| Depot 3D — Optimal (RTX 3070+) | ~4 mln |
| Mapa 2D | trójkąty niskie — limit = draw calls (<1500 / <2000) + CPU |

**Tabor** (kamera bliska w zajezdni zoom 5–100 m; na mapie agresywny LOD):

| Asset | LOD0 | LOD1 | LOD2 | dalej | na ekranie |
|---|---|---|---|---|---|
| Lokomotywa body (EP07/EU160/754/SM42) | 8–12k (cap 15k) | 4–5k | 1–1,5k | billboard | 1/skład |
| Wagon UIC-X/Z body | 4–7k | 2k | 0,8k | billboard | N/skład |
| EZT/SZT człon czołowy (cab) | 6–10k | 3–4k | 1–1,5k | billboard | rodzina |
| EZT/SZT człon środkowy | 4–7k | 2–3k | 0,8k | billboard | rodzina |
| Wózek (3 typy) | 0,8–1,5k | 0,3k | merge | – | 2/pojazd |
| Pantograf | 0,4–0,8k | 0,15k | quad | – | 1–2/poj. el. |
| Zderzaki / drzwi | 0,1–0,3k | merge | – | – | kilka/poj. |
| **Skład (loco + 5 wagonów) — suma** | **~40–55k** | ~18k | ~6k | billboard | ≤5 w zajezdni |

**Tor i rozjazdy** (dominanta budżetu):

| Asset | LOD0 | LOD1 | dalej | uwaga |
|---|---|---|---|---|
| Rail 1 m (✅ bpy) | ~60–110 | ~12 (box) | scalony pas | per-metr blisko / scalony pas daleko |
| Sleeper (✅ bpy) | ~228 | ~24 (box bez węzła K) | strip / cull | **priorytet LOD+instancing #1** |
| Ballast (✅ C# procedural) | ~6/segment | – | – | OK jak jest |
| Rozjazd (M-Models R&D, ~20–30 mesh) | dziób 5 wariantów FBX + reszta swept/instanced | detal close-up tylko LOD0 | – | ryzyko bottleneck — LOD+instancing wymagane |

Reguła toru: blisko kamery = instancjonowane szyna+podkład z pełnym detalem; daleko =
**jeden scalony pas low-poly z normal mapą** (tnie i trójkąty, i draw calls naraz).

**Budynki + wyposażenie zajezdni:**

| Asset | LOD0 | dalej | na ekranie |
|---|---|---|---|
| Hala + pomieszczenia (procedural mesh) | shell ≤8k | – | 1 zajezdnia (open-top, boxy = tanie) |
| Słup katenarii | ~200 | LOD1 ~40 | dziesiątki → instancing |
| Izolator / drut | drobne / LineRenderer | – | setki — pilnuj overdraw drutu |
| Outdoor (Wash/Turntable/PitLift/Fuel) | 200–800 | ~½ | 1–kilka |
| Płot: słupek + siatka | 80–150 słupek | – | dziesiątki → instancing |
| Meble (38 obiektów) | 100–400 (cap 500) | opc. | dziesiątki → instancing identycznych |

**Personel** (druga masowa instancja — plus koszt skinningu/animacji):

| Asset | LOD0 | LOD1 | LOD2 | dalej | na ekranie |
|---|---|---|---|---|---|
| Base mesh + 10 outfitów | ~2k | ~0,8k | ~0,3k | billboard | dziesiątki–setki |

**Tło, nawierzchnie i mapa 2D:**

| Asset | LOD0 | dalej | na ekranie |
|---|---|---|---|
| Teren grass (podłoże) | ~500 (procedural grid) | – | 1 (✅ `LushGrass_Light`) |
| Chodnik / parking (PathVisualBuilder) | ~2/segment (mesh strip) | – | płaskie → koszt = materiał + draw calls (static batch) |
| Droga tła + krawężniki (GroundGenerator) | ~2/segment (mesh strip) | – | poza fence; static batch |
| Magazyny tła (5–8) | 200–800 | LOD1 ~100 / impostor | 5–8 |
| Ambient props (drzewa/ławki/latarnie) | 300–1,5k | ~½ / billboard | dziesiątki → instancing |
| Skyline backdrop | ~200 total | – | 1 (już low) |
| Pociąg na mapie 2D | LOD2 ~1–2k (zoom-in) | billboard/marker (oddalenie) | do 1000 = markery instancjonowane |

**Trzy twarde reguły wykonawcze:**
1. **Instancing obowiązkowy** dla wszystkiego powtarzalnego (podkłady, szyny, słupy, płot, identyczne
   składy, pracownicy per outfit) — `BatchRendererGroup` / GPU Resident Drawer. Bez tego sama
   zajezdnia = tysiące draw calls. *(Instancing tnie draw calls, nie trójkąty.)*
2. **LOD obowiązkowy** dla masowych i hero: tor (podkład #1), tabor, personel. ~50% redukcji
   wierzchołków/poziom, billboard w dali.
3. **Detal → normal/AO mapy, nie geometria** (zgodne z `visual-style-guide.md`: „szum służy
   materiałowi, nie zagłusza sylwetki"). Szyby = czarny panel (zero geo wnętrza), hale bez dachu,
   oklejenia = dekale/maski.

**EP07 — zderzenie z rzeczywistością:** real mesh = 15 710 tris, bez LOD, livery wypalona
w teksturach. Mieści się jako LOD0 hero (zoom-in), ale wymaga: (a) łańcucha LOD (≥ LOD1 ~5k,
LOD2 ~1,5k, billboard), (b) przeteksturowania na bazę neutralną + maska zon pod paint editor,
(c) reuse dla całej `EU07_family`. Branżowo spójne: CS1 daje pojazdom 500–1000 tris bo są
malutkie top-down; RM ma kamerę zoom-in 5–100 m, więc 8–15k hero jest uzasadnione — ale
**wymusza LOD**.

---

## Optimizations (M12a Performance milestone)

### Priorytety

1. **Object pooling**
   - Pociągi spawning/despawning
   - Particle effects (dym, iskry)
   - UI popupy
   - Markery na mapie

2. **LOD system**
   - Pociągi (4 poziomy)
   - Budynki zajezdni
   - Catenary (uproszczone przy oddaleniu)
   - Mapa 2D (LOD 0-5, gotowe z v7 bin)

3. **Batching**
   - Static batching dla budynków, torów, ogrodzeń
   - Dynamic batching dla małych ruchomych elementów
   - GPU instancing dla powtarzających się mesh'ów (słupy catenary, drzewa)

4. **Culling**
   - Frustum culling (Unity built-in)
   - Occlusion culling dla Depot 3D (Unity built-in, wymaga bake)
   - Distance culling dla ambient (M12c)

5. **Memory management**
   - Tile cache tuning (unload radius)
   - Texture streaming dla mapy
   - Garbage collection profiling
   - Unity AsyncOperation gdzie możliwe

6. **Shader optimization**
   - Mobile-friendly shaders gdzie możliwe
   - Brak fancy effects w mapie 2D (flat colors + simple lighting)

### Profiling tools

- **Unity Profiler** — CPU, Memory, Rendering tabs
- **Frame Debugger** — identyfikacja drogich draw calls
- **Deep Profiler** — hierarchia script calls
- **Memory Profiler** (Package) — alokacje, referencje

---

## Benchmarki per milestone

### M9 Train movement
- **Test 1:** 10 pociągów w ruchu, warmińsko-mazurskie
  - Target: 60 FPS recommended spec
  - Max frame time: 20 ms
- **Test 2:** 50 pociągów w ruchu, warmińsko-mazurskie
  - Target: 30 FPS minimum spec, 60 recommended
- **Test 3:** 200 pociągów w ruchu (stress)
  - Target: 20 FPS minimum, 40 recommended

### M-PL Pełna Polska
- **Test 1:** Start gry, ładowanie pełnej mapy
  - Target: <20s recommended, <30s minimum
- **Test 2:** Pan kamerą po całej Polsce (Warszawa → Gdańsk → Kraków → Wrocław)
  - Target: brak hitch'y >100ms, FPS stabilny
- **Test 3:** Długa trasa (Warszawa → Rzeszów, ~300km) z ruchem pociągu
  - Target: load trasy <1s, ruch płynny

### M12a Performance milestone — success criteria

Po M12a gra musi osiągać:
- ✅ Recommended spec: 60 FPS w podstawowych scenariuszach (depot, mapa, 50 pociągów)
- ✅ Minimum spec: 30 FPS
- ✅ RAM < 8 GB dla typowej sesji na pełnej Polsce
- ✅ Loading time <15s dla pełnej mapy

---

## Regresja testowa

Każdy commit do `main` powinien przejść **performance smoke test**:

1. Build gry w Release mode
2. Uruchom sekwencję: Menu → Depot 3D → Mapa warmińsko-mazurskie → utwórz rozkład
3. Zmierz: FPS średni, FPS min, RAM peak, loading time
4. Porównaj z baseline (poprzedni commit)
5. Jeśli regresja >10% — investigate przed merge

**Note:** Ten test jest manualny do czasu M14 Beta (wtedy warto zautomatyzować przez CI).
