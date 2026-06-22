# Asset Creation Guide — bpy-first pipeline + styl

> **Cel:** jak tworzymy modele 3D i tekstury w Railway Manager, żeby był spójny styl i powtarzalny
> proces. Czytaj przed robieniem nowego assetu infrastruktury (tor/katenaria/hardware/budynki/meble).
> Powiązane: `asset-needs-by-system.md` (co potrzeba per system), `visual-style-guide.md` (ogólny styl
> gry). Skrypty: `BlenderSource/scripts/`.

---

## 1. Filozofia

- **bpy-first.** Infrastruktura (geometryczna/parametryczna) generowana **kodem Python (`bpy`)**, nie ręcznym klikaniem GUI. Skrypt = tekst w gicie, wymiary jako stałe zsynchronizowane z kodem gry, deterministyczne i powtarzalne. Tabor hero (organiczny) → partnerzy/GUI/posiadany EP07. **Postacie/personel (organiczne + rig) → zlecenie albo Asset Store (Synty POLYGON People), NIE bpy** — rigowanie postaci skryptem jest niewdzięczne; **na EA dążymy do realnych postaci** ze źródła zewnętrznego (Asset Store Synty POLYGON People), kapsuła zostaje tylko jako fallback (decyzja 2026-06-21, zmiana vs wcześniejszy plan kapsuł na EA). Kanciaste propy przemysłowe (outdoor equipment, meble) są natomiast wykonalne w bpy parametrycznie.
- **Research-driven.** Wymiary i materiały z **realnych źródeł** (normy PKP, datasheety producentów), nie z pamięci. Weryfikacja workflow (multi-agent + web) zanim modelujemy.
  - **Im element bardziej „wrażliwy kolejowo", tym research jest OBOWIĄZKOWY przed modelowaniem** — zwłaszcza tabor (lokomotywy/EZT/SZT) oraz geometria toru/rozjazdów. **Realne wymiary nadrabiają niewdzięczność bpy/stylizacji**: nawet kanciasta bryła czyta się jako „to konkretny pojazd", jeśli długość, rozstaw osi/wózków, szerokość pudła i krzywizna dachu są z datasheetu. Przy naszym stylu (stylized PBR, czytelność z kamery gry) **proporcje > mikro-detal**, a fani kolei wychwycą złe proporcje szybciej niż brak detalu. Trigger: zanim ruszysz wrażliwy element, odpal research workflow (krok 1 niżej) i **wpisz wymiary jako stałe skryptu** (precedens: `gen_rail.py` S49 z datasheetu, `gen_sleeper.py` IIB z normy). Kontrakt importu taboru (`EP07ModelImportTests.cs`) sanity-czekuje już realną długość — research dostarcza resztę proporcji.
- **Stylized PBR, NIE fototekstury.** Wszystkie tekstury procedural (Blender Shader Editor → bake PNG) lub hand-paint. Zero photoscan/ambientCG. CBM-target (umiarkowany szum, nie hyper-real).

---

## 2. Workflow (per asset)

```
1. RESEARCH   workflow web → spec (wymiary z normy, materiał, kolory) — gdy nie znamy
2. GEN        BlenderSource/scripts/gen_<asset>.py — geometria + procedural shader (parametryczny)
3. BAKE       bake_<asset>.py — UV + Cycles bake shader → PNG (albedo/normal/metallic-smoothness)
4. UNITY      import FBX + tekstury → materiał URP/Lit → prefab/slot → test w grze
```

Headless: `blender --background --factory-startup --python <skrypt>`.
Render podglądu: `render_<asset>.py` (Cycles, do oceny wyglądu przed Unity).

---

## 3. Konwencje techniczne (KRYTYCZNE — z lekcji)

### Geometria (gen_*.py)
- **Wymiary = stałe zsynchronizowane z kodem gry** (np. `PrefabTrackBuilder.Generators.cs` placeholder). Kod wygrywa z dokumentacją (placeholder Cube miał realne wartości).
- **Skala:** Blender 1 unit = 1 m. Buduj geometrię w docelowym wymiarze (NIE skaluj obiektu — wypal w geometrię).
- **Pivot = bottom-center** (origin na spodzie) — dla assetów stawianych na gruncie. Buduj wokół origin (spód na Z=0).
- **Orientacja:** długa oś modelu wzdłuż osi, którą kod instancjonuje (sprawdź `LookRotation` w generatorze gry).

### Eksport FBX → Unity (rozwiązane 2026-06-20)
- `axis_forward='-Z', axis_up='Y'` **+ `bake_space_transform=True`** — wypala konwersję Z-up→Y-up w geometrię. **Bez tego model stoi pionowo w Unity.** Unity import: **Bake Axis Conversion OFF** (Blender już zrobił).
- **Normale:** `bake_space_transform` odbija winding (Blender RH → Unity LH). `normals_make_consistent(inside=True)` kompensuje. Jeśli ściany „przezroczyste od kamery" (backface) → materiał **Render Face=Both** (obejście; docelowo one-sided = #13 TD).
- Unity import FBX: Scale Factor 1, Generate Colliders OFF (tor ma własne collidery).

### Bake (bake_*.py)
- **Procedural shader NIE eksportuje się do FBX** — bake do PNG obowiązkowy dla wyglądu w grze.
- UV: `smart_project` w gen_*.py (to samo UV w FBX i w bake = spójne).
- Mapy: **Albedo** (DIFFUSE, sRGB), **Normal** (NORMAL, Non-Color), **Roughness** lub spakowana **MetallicSmoothness**.
- **URP nie ma slotu Roughness** — smoothness czytany z **alpha Metallic Map**. Packing: bake metallic (EMIT-trick: metallic→Emission, bake EMIT) + bake roughness → numpy `R=metallic, A=1-roughness` → 1 PNG RGBA (Non-Color). Wzór: `bake_rail.py`/`bake_ballast.py`.
- **Displacement → Normal:** dla tekstury (płaski mesh w grze) podłącz height przez **Bump → BSDF Normal** i bake NORMAL (bake nie łapie material-displacement). Wzór: `bake_ballast.py`.

### Unity material (URP/Lit)
- Base Map = Albedo, Normal Map = Normal (Texture Type=Normal map), Metallic Map = packed (Source=**Metallic Alpha**).
- MetallicSmoothness PNG: **sRGB OFF**. Albedo: sRGB ON.

### Procedural mesh w grze (gdy asset musi pasować do dowolnej geometrii)
- Niektóre rzeczy NIE są FBX — generowane meshem w C# (np. **podsypka/nasyp** w `GenerateBallast`, druty katenarii). Wtedy tylko **tekstura tile-able** z bpy + materiał. Mesh w world coords → `SetParent(parent, worldPositionStays:true)` + `position=Vector3.zero` (inaczej podwójny offset → niewidoczny). Winding dla normalnych w górę (kamera z góry).

---

## 4. Styl wizualny

- **Paleta stylized PBR** — albedo + normal + roughness/metallic. Umiarkowany szum, NIE hyper-real, NIE flat-vertex-color.
- **Procedural shader nodes:** Voronoi (kamienie/komórki), Wave (słoje drewna), Noise (szum/spękania), ColorRamp (palety), Bump/Displacement (relief). Warp coords (Noise→add) dla kanciastości.
- **Paleta = miks reference** (cool + warm), per-cell wariacja jasności (Voronoi Color → HueSaturation Value), per-cell połysk (→ roughness). NIE monochrom/binarne.
- **Displacement wypukły:** Midlevel=0 (wszystko ≥0 = w górę) LUB invert distance-to-edge (`0.5-dist`) gdy środki się zapadają. **Kalibracja siły/kierunku = interaktywnie w GUI** (Shading workspace, rendered viewport, suwak Scale) — headless to ślepe iteracje.
- **Tile-able:** docelowo Voronoi/Noise 4D (Clifford torus) + bake UV 0-1 bez paddingu. Anti-tiling: macro-variation overlay; hex-tiling (Mikkelsen) jako plan B.

---

## 5. Wykonane assety (rejestr — parametry)

| Asset | Typ | Wymiary | Geometria | Tekstury (1024px) | Materiał(y) | Procedural / uwagi |
|---|---|---|---|---|---|---|
| **Sleeper** (podkład) | FBX | 2,6 × 0,26 × 0,16 m (drewno IIB) | ~228 tris (bryła + węzeł K: podkładka Pm-49 + łapki + śruby) | Albedo / Normal / Roughness | `Sleeper_Wood` + `Metal_Fitting` (URP/Lit, RenderFace=Both, smoothness 0,3) | Wave słoje + Noise/Voronoi spękania + plamy kreozotu; pivot bottom-center; `gen_sleeper.py`+`bake_sleeper.py` |
| **Rail** (szyna S49) | FBX | profil 49E1 (H149/stopka125/główka67), segment 1 m | ~62 tris (profil 29 vty swept) | Albedo / Normal / **MetallicSmoothness** (packed) | `Rail_Steel` (URP/Lit, RenderFace=Both) | Rdza matowa + wąski lustrzany pasek toczny na czubku (metallic 1/rough 0,07); crown R300 zaokrąglony; `gen_rail.py`+`bake_rail.py` |
| **Ballast** (podsypka/nasyp) | **mesh proceduralny w C#** (`GenerateBallast`) | przekrój trapez: dół 3,5 m / korona 2,9 m @ 0,105 m + skarpy | ~724 v / 1080 tris per tor (loft wzdłuż polyline) | Albedo / Normal / MetallicSmoothness (tile ~2 m) | `Ballast.mat` (URP/Lit, RenderFace=Both) via `ballastMaterialOverride` | granit/bazalt łamany (Voronoi+warp) paleta reference; displacement→bump normal; `gen_ballast.py`+`bake_ballast.py` |

**Konwencje plików:**
```
BlenderSource/scripts/gen_<asset>.py     — generator geometrii + shader
BlenderSource/scripts/bake_<asset>.py    — bake tekstur do PNG
BlenderSource/scripts/render_<asset>.py  — podgląd Cycles (opcjonalny)
BlenderSource/Trackwork/<asset>.blend|.fbx
Assets/Models/Trackwork/<asset>.fbx + <Material>.mat
Assets/Textures/Trackwork/<asset>_Albedo|Normal|MetallicSmoothness.png
```

---

## 5.1 Grunt / trawa — pipeline shader → bake → Unity (2026-06-22)

Trawa zajezdni to **płaski plan + tekstura** (decyzja user: NIE geometria 3D, jak CBM), podpięta pod
`GroundGenerator.customGroundMaterial` = `LushGrass_Light.mat`. Pipeline (skrypty `BlenderSource/scripts/`):

1. **`bake_grass_shader.py`** — procedural shader „rozdeptana trawa": Voronoi-komórki + **per-komórka
   losowy obrót** (`VectorRotate`) + maska kształtu źdźbła, 6 warstw (różne offsety) = gęste smugi w
   różnych kierunkach. Bake **TOP-DOWN ORTHO** (płaski shader → czysty kafel) → `Assets/Textures/Trackwork/Grass_Albedo.png` (2048).
2. **`make_grass_seamless.py`** — seamless: offset o pół + feather do wersji rolled (numpy, wrap). Nie psuje kafla.
3. **`match_grass_color.py`** — per-kanałowy gain w **linear** tak, by średni kolor = referencja (`ref_grass.png`). Trzyma ton 1:1 niezależnie od zmian w bake. Analiza referencji: **`analyze_ref_grass.py`** (mean/median sRGB+linear, saturacja, luminancja, winieta, FFT skali detalu).
4. **`make_grass_normal.py`** — normal z wysokości (luminancja albedo, gradient `roll` = seamless) → `Grass_Normal.png` (relief jak podsypka).
5. **Unity `LushGrass_Light.mat`** (URP/Lit): BaseMap=Grass_Albedo, BumpMap=Grass_Normal + keyword **`_NORMALMAP`** (bez niego URP ignoruje bump) + `_BumpScale`, **aniso 16** (krytyczny lewar „ostrości" gruntu pod kątem), tile ~5 m (`m_Scale`), **`_BaseColor` SKALIBROWANY pod światło zajezdni** przez **`calc_basecolor.py`**.

**Kalibracja koloru = pętla numeryczna:** Unity doświetla teksturę, więc kolor w grze ≠ tekstura. Proces: screen z gry → `calc_basecolor.py` sampluje go vs referencja → liczy `_BaseColor` per-kanał tak, by **kolor w grze = referencja**. `ref_grass.png` (screen z CBM) i `ingame_grass.png` to **DEBUG — NIE commitować** (CBM = cudzy asset; w `.gitignore`).

**Status: względnie skończone, polish odłożony (pre/post-EA TBD, 2026-06-22).** Kolor dopasowany
numerycznie (mean+saturacja ≈ referencja), struktura = rozdeptana trawa z reliefem, seamless, aniso.
**Do dociągnięcia w polish:** kształt/częstotliwość/gęstość źdźbeł nie są 1:1 z CBM (per-komórkowe
źdźbła bardziej „geometryczne" niż naturalne); final color/relief tuning; ew. macro-łaty per-świat
(większe plamy jak na referencji) w shaderze zamiast w kaflu.

**Lekcje (żeby polish nie zaczynał od zera):** czysty proceduralny SZUM nie odda trawy (4 ślepe uliczki:
gładki noise=rozmaz / Voronoi-komórki=moho / Wave=wydmy / flow-rotate=wiry). Działa: **per-komórka
obrócone źdźbło + bake top-down**. Particle hair 3D wygląda świetnie, ale top-down bake = ciemny (widać
glebę między stojącymi źdźbłami) — dlatego shader, nie hair. **Aniso** to główny powód „rozmycia" gruntu.

---

## 6. Otwarte / TD

- **#13** normale one-sided (zdjąć RenderFace=Both — perf przy tysiącach instancji).
- **TD-045** szyny na łukach (proste 1 m segmenty nie spasowują krzywizny).
- **TD-046** szyny/podkłady nie dociągają do końca toru.
- **Seamless tile** tłucznia (4D) + **smar pod szyną** (maska URP, czarna smuga przy stopkach) — z researchu #11, niezrobione.
