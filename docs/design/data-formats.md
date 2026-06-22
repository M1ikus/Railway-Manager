# Data formats — specyfikacja formatów plików

> **Cel:** Referencja dla wszystkich formatów plików używanych przez Railway Manager.
> Przy debug'u plików, migracji wersji, tworzeniu tooli.

---

## Formap binary (.bin) — mapa OSM

**Lokalizacja:** `Assets/StreamingAssets/Maps/Poland/poland.bin` lub `warminsko-mazurskie-v7.bin`

**Format:** v8 (`FORMAP04`, docelowy — lossless re-encode v7, ~44% mniejszy) + v7 (`FORMAP03`, aktualnie na dysku), wcześniej v5 (non-tiled), v6 (tiled, single LOD). RM czyta wszystkie po magicu (`MapLoader.LoadMap` dispatch).

**Przyszłe pakiety krajów / DLC:** patrz `docs/design/country-dlc-map-packages.md`.
Ten dokument opisuje docelowy podział `map.bin` / `rail_graph.bin`, `MapPackage`,
`CountryRules` oraz stacje graniczne/off-map.

**Magic numbers:**
- v5: `FORMAP01`
- v6: `FORMAP02`
- v7: `FORMAP03`
- v8: `FORMAP04`

### Struktura v7 (aktualna)

```
[Header]
  magic: char[8]              "FORMAP03"
  version: int32              7
  tileSize: float32           (np. 10000.0 dla 10km tiles)
  globalBounds: BBox {
    minX: float32
    minY: float32
    maxX: float32
    maxY: float32
  }
  tilesX: int32               (liczba tile'ów po X)
  tilesY: int32               (liczba tile'ów po Y)
  lodCount: int32             (zwykle 6: LOD 0-5)

[Tile Index]
  per tile (tilesX * tilesY entries):
    gridX: int32
    gridY: int32
    per LOD (lodCount entries):
      offset: int64            (pozycja w pliku)
      compressedSize: int32
      decompressedSize: int32

[Tile Data — LZ4 compressed]
  per tile × per LOD:
    LZ4-decompressed {
      per layer (LayerCount=12):
        featureCount: int32
        per feature:
          MeshGeometry (see below)
    }
```

### Struktura v8 (`FORMAP04`, docelowa)

Lossless re-encode v7 (te same dane, ten sam LOD/tiling), ~44% mniejszy na dysku, dekompresja
nadal LZ4-HC (K4os, bez Zstd). **Reader RM:** `MapLoader.LoadMapV8` + `BinaryFormat.ReadHeaderV8`/
`ReadTileIndexEntryV8` + `FeatureCodecV8` + `BlockDecoderV8` + podpis `MapSigning`/`MapSignatureVerifier`.
Źródło prawdy formatu: `D:\Gry\formap\BinaryFormatV8.cs` + `docs/format-v8.md`.

```
[Header 128 B]
  magic: char[8]              "FORMAP04"
  version: int32              8
  tileSize: float32
  globalBounds: BBox          (4× float32)
  tilesX, tilesY: int32
  totalTiles: int32
  indexOffset: int64
  layerCount: int32           NOWE — czytane dynamicznie (nie hardcode 13)
  lodCount: int32             NOWE — czytane dynamicznie (nie hardcode 6)
  compressionType: int32      NOWE — 0=LZ4-HC, 1=Zstd (RM odrzuca 1)
  signatureLength: int32      NOWE — 0=unsigned, 64=Ed25519-signed
  reserved: byte[60]

[String table]                global metadata interning
  count: varint, per string: varint byteLen + UTF-8 bytes

[Tile LOD blocks]             per tile × per LOD, lokalizowane WYŁĄCZNIE przez index
  (BRAK int32 length-prefiksu — to różnica vs v7)
  LZ4-pickle bloku, body = varint structLen + struct section + shuffled X plane + shuffled Y plane
    struct section: per warstwa { int32 layerType, int32 featureCount, per feature FeatureCodecV8 record }
    feature record: flags(1B: idx/hole/seg/junc/meta/wide) + vertexCount(varint) + opcjonalne sekcje (varint)
                    — metadata to indeksy do string table; bbox NIE zapisany (liczony z wierzchołków)
    X/Y planes: wszystkie wierzchołki bloku, SoA-split, byte-shuffled (stride 4 float32), totalVerts=Σ vertexCount

[Tile index @ indexOffset]
  per tile: TileID(i64) GridX(i32) GridY(i32) Bounds(4×f32),
            lodCount × LODInfo { FileOffset(i64) CompressedSize(i32) UncompressedSize(i32) LayerMask(i32) },
            lodCount × 32-byte SHA-256 (ZAWSZE obecne, też unsigned — czytaj/pomiń!),
            layerCount × int32 featureCounts

[Signature]                   OPCJONALNE (gdy signatureLength==64): trailing 64-byte Ed25519
  podpis nad CAŁYM regionem indexu [indexOffset .. fileLen-64); klucz pub embedded w MapSignatureVerifier
```

**Pułapki readera** (patrz też `D:\Gry\formap\docs\rm-v8-integration.md`):
1. lodCount×32 B SHA-256 są zawsze w indexie (też unsigned) — pominięcie desynchronizuje stride całego indexu.
2. bloki bez length-prefiksu — lokalizacja przez `FileOffset`/`CompressedSize` z indexu.
3. bbox liczony z wierzchołków (`MeshGeometry.ComputeBoundingBox` w `BlockDecoderV8`) — załatwia konsumentów `BoundingBox` (AdminRegion PIP) transparentnie.
4. `layerCount`/`lodCount` z headera; RM aktualnie guarduje że == 13/6 (głośny błąd przy rozjeździe).

### MeshGeometry structure

**Plik:** `Assets/Scripts/Map/Core/MeshGeometry.cs`

```
BoundingBox: BBox (4 floats)
Vertices: List<Vector2>       (count + count × 2 floats)
Indices: List<int>             (count + count × ints)
HoleStarts: List<int>          (count + count × ints) — dla multipolygons
SegmentIds: List<int>          (count + count × ints) — tylko railways
JunctionIndices: List<int>     (count + count × ints) — tylko railways
Metadata: Dictionary<string, string> {
    count: int32
    per entry:
      keyLength: int32
      keyBytes: byte[UTF-8]
      valueLength: int32
      valueBytes: byte[UTF-8]
}
```

**WAŻNE:** Order reading ma znaczenie — zmiana kolejności pól psuje kompatybilność. `HoleStarts` MUSI być czytane przed `SegmentIds`.

### LayerType enum (12)

```csharp
public enum LayerType : int
{
    Highways = 0,         // Drogi — polylines
    Railways = 1,         // Tory kolejowe — polylines z SegmentIds + JunctionIndices
    Buildings = 2,        // Budynki — filled polygons
    Water = 3,            // Woda — polygons
    Industrial = 4,       // Strefy przemysłowe — polygons
    Military = 5,         // Strefy wojskowe — polygons
    Platforms = 6,        // Perony kolejowe — polygons z ref tagiem
    Forests = 7,          // Lasy — polygons
    POIs = 8,             // railway=station/halt/signal + inne POI — single points
    Waterways = 9,        // Rzeki, strumienie — polylines
    AdminBoundaries = 10, // Granice województw i kraju — polygons z admin_level
    Places = 11           // place=city/town/village — single points z populacją
}
public const int LayerCount = 12;
```

### Metadata keys (Railways)

- `maxspeed` — Vmax z OSM (np. "100", "RU:rural")
- `railway` — "rail", "tram", "subway"
- `usage` — "main", "branch", "industrial", "tourism"
- `service` — "siding", "spur", "yard", "crossover"
- `electrified` — "yes", "no", "contact_line", "rail"
- `voltage` — "3000" (V)
- `frequency` — "0" (DC)
- `gauge` — "1435"
- `tracks` — "1", "2"
- `railway:track_ref` — numer toru ("1", "2", "101a")
- `name` — nazwa linii

### Metadata keys (POIs — railway=signal)

- `railway` — "signal"
- `railway:signal:main:function` — "entry" / "exit" / "block" / "intermediate"
- `railway:signal:combined:function` — to samo dla combined
- `railway:signal:direction` — "forward" / "backward" / "both"
- `railway:signal:position` — "left" / "right"
- `ref` — numer referencyjny sygnału
- `name` — nazwa

### Metadata keys (Platforms)

- `railway` — "platform"
- `ref` — numery torów przy peronie (np. "1;3")
- `railway:track_ref` — dodatkowy numer
- `name` — "Peron 1"

---

## Pipeline mapy: RM vs CBM (recon 2026-06-13)

> Reverse-engineering City Bus Manager (nasz target wizualny, Unity 6000.2.12f1, plugin **GO Map**) — workflowy
> `map-pipeline-compare-cbm-rm` + `cbm-map-flat-or-extruded`. Wniosek nadrzędny: **wizualnie obie mapy są identyczne
> (płaski ortho top-down OSM), różni je wyłącznie pipeline pod spodem.**

**Widok — IDENTYCZNY [potwierdzone]:** obie kamery ortograficzne top-down (CBM `set_OrthographicMode`/`orthographicSize`
≈ nasz `CameraController.cam.orthographic`). Budynki w OBU = **płaskie poligony**. CBM ma w GO Map pełny aparat ekstruzji
3D (`SimpleExtruder`/`useRealHeight`), ale go **wyłącza** (we wszystkich 7 scenach `useRealHeight=0`, `polygonHeight=0`;
zdekompresowane kafle `.map` mają wierzchołki budynków na Y=0, jeden materiał `M_Building` bez sides/roof). **CBM NIE jest
precedensem za 3D mapą — wybrał płasko, jak my.**

| | RM (offline-bake) | CBM (runtime GO Map) |
|---|---|---|
| Źródło | własny `poland-v7.bin` (formap, offline) | Mapbox vector tiles (PBF) online + cache `.map` (MessagePack) z CDN |
| Ładowanie | tile-index + streaming frustum, coroutine frame-budget (5ms/8 kafli), LRU 256, `init-state-pl.bin` <15s | preload ~40 `.map` + 16 miast offline, reszta on-demand z CDN, threadpool decode |
| Render | runtime Unity mesh per-warstwa-per-kafel (UInt32 index), ~2.5k GO (po TD-026) | per-feature → combined mesh per-kafel (Poly2Tri), GameObjectPool |
| Routing | `RailwayGraph` z warstwy Railways | Itinero `.routerdb` (memory-mapped Reminiscence) + A* (piesi) |
| Spatial query | colliderowy (TD-027 🔴) | `SpatialHashGrid`/`DynamicQuadtree`/KD-tree |

**Lekcje:**
- **TD-027:** CBM używa spatial index zamiast colliderów = nasze „idealne rozwiązanie" potwierdzone (patrz `TECH_DEBT.md` TD-027).
- **Przewaga RM (zostawić):** offline-first (cała Polska bez internetu/CDN/Mapbox API/kosztów), pre-baked = tani runtime
  (zero triangulacji w grze), własny format = warstwy specyficznie kolejowe + kontrola LOD. **DLC krajów = pre-bake
  kolejnych binarek formap, NIE runtime streaming.**
- **3D budynki:** NIE „bo CBM" (oni płasko). Gdybyśmy kiedyś chcieli relief — osobna decyzja M12, wymagałaby building
  height w formap (obecnie brak w v7). Niski priorytet.

---

## StationTrackData JSON

**Lokalizacja:** `Assets/StreamingAssets/TimetableData/station_tracks.json`

**Cel:** Edytowalny przez użytkownika override dla przypisania torów do peronów per stacja.

**Format:**

```json
{
  "stations": [
    {
      "name": "Olsztyn Główny",
      "tracks": [
        { "trackRef": "1", "hasPlatform": true },
        { "trackRef": "2", "hasPlatform": true },
        { "trackRef": "3", "hasPlatform": true },
        { "trackRef": "5", "hasPlatform": false }
      ]
    },
    {
      "name": "Elbląg",
      "tracks": [
        { "trackRef": "1", "hasPlatform": true },
        { "trackRef": "2", "hasPlatform": true }
      ]
    }
  ]
}
```

**Zasady:**
- `name` musi dokładnie odpowiadać OSM `name` tag stacji
- `trackRef` to wartość z `railway:track_ref` edge'a w grafie
- `hasPlatform: true` — tor dostępny dla postojów pasażerskich
- `hasPlatform: false` — tor tylko techniczny / bez peronu
- Jeśli stacja nie jest w JSON — automatyczny fallback do OSM data
- Usunięcie pliku → regeneracja przy starcie gry (`StationTrackData.Generate`)

---

## Save game format (M13 — planowane)

**Lokalizacja (planowana):** `%USERPROFILE%/Documents/Railway Manager/Saves/`

**Format:** JSON (debug-friendly) lub MessagePack (compact binary) — **TBD**

**Struktura (wysoki poziom):**

```json
{
  "meta": {
    "saveVersion": 1,
    "gameVersion": "0.4.0",
    "saveName": "Moja kampania",
    "savedAt": "2026-04-14T12:34:56",
    "playtime": 3600,
    "screenshot": "base64..."
  },
  "gameState": {
    "gameTimeSeconds": 86400.0,
    "timeScale": 1.0,
    "isPaused": false,
    "money": 150000,
    "difficulty": "Normal"
  },
  "player": {
    "company": {
      "name": "PKP Mazowsze",
      "color": "#FF4400",
      "founded": "2026-04-01"
    },
    "depots": [...],
    "fleet": [...],
    "timetables": [...],
    "circulations": [...],
    "trainRuns": [...],
    "personnel": [...]
  },
  "world": {
    "mapFile": "poland.bin",
    "mapVersion": "2025-12-24",
    "reservations": {
      "platforms": [...],
      "blocks": [...]
    },
    "economy": {
      "passengerDemand": [...],
      "dailyBalance": {...}
    }
  },
  "stats": {
    "totalPassengers": 12345,
    "totalRevenue": 98765,
    "achievementProgress": {...}
  }
}
```

**Versioning:** Każdy save ma `saveVersion: int`. `SaveMigrator` (M13) konwertuje starsze wersje.

---

## InputActions (.inputactions)

**Lokalizacja:** `Assets/Settings/InputActions.inputactions`

**Format:** Unity Input System JSON (standardowy format Unity).

**Struktura:** 7 action maps:
- `Camera.Depot` — kamera w zajezdni (WSAD, Q/E, T/F, MMB, RMB, scroll)
- `Camera.Map` — kamera na mapie (WSAD+Arrows, MMB, scroll, Q/E)
- `Tool.Build` — wspólne dla narzędzi (LMB, RMB, ESC, Del, Ctrl+Z, Ctrl+Shift+Z)
- `Tool.Turnout` — dodatkowe dla rozjazdów (R, P, O, U, scroll)
- `Vehicle` — selekcja pojazdów (LMB, Space, ESC)
- `UI.Popup` — dormant (rebinding future)
- `UI.PauseMenu` — dormant (rebinding future)

**Wrapper C#:** `Assets/Scripts/Core/InputActions.cs` — auto-generowany przez Unity z asset'a.

---

## TimetableTuningConstants (constants class)

**Plik:** `Assets/Scripts/Timetable/Catalogs/TimetableTuningConstants.cs`

**Cel:** Wszystkie wartości kalibracyjne w jednym miejscu. Łatwo zmienić balans bez grzebania w logice.

**Kategorie (wysoki poziom):**
- Fizyka: `DefaultAcceleration`, `DefaultDeceleration`, `MaxSpeedKmh`
- Stops: `MinPassengerStopSec`, `MinTechnicalStopSec`
- Reverse: `EmuReverseSec=120`, `LocoReverseSec=600`
- Kategorie IRJ: progi % postojów (80/40 dla osobowy/pospieszny/ekspres)
- Aglomeracje: `AgglomerationMinStationCount`

Patrz `docs/design/balance-constants.md` dla pełnego opisu.

---

## Assets/Prefabs structure

```
Assets/Prefabs/
├── Depot/
│   ├── Track/          — szyna_podklad.fbx, rozjazdy, zwrotnice
│   ├── Catenary/       — słupy SBS/STB, wysięgniki, izolatory
│   ├── Buildings/      — ściany, drzwi, okna, hale
│   ├── Fence/          — ogrodzenie segments, bramy
│   └── Vehicles/       — prefaby pociągów (M9+)
├── Map/                — markery stacji, ikony POI
├── UI/                 — popup'y, dialogi, panele
└── Shared/             — prefaby używane w wielu scenach
```

---

## Modele 3D (FBX) — konwencje importu

Patrz `docs/design/asset-pipeline.md` dla pełnej specyfikacji.

Kluczowe:
- Skala 1 jednostka Unity = 1 metr
- Pivot: środek pojazdu XZ, Y=0 na szynie
- Koła jako osobne GameObjects (Unity runtime rotation)
- Pantograf jako hierarchia z Animator clip Up/Down
- LOD 0/1/2/3 (ostatni = billboard)
