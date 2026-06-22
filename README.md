# Railway Manager

*Polish version: [README.pl.md](README.pl.md)*

A railway company management simulator set in Poland, built on real
[OpenStreetMap](https://www.openstreetmap.org/) data. The player runs a rail
operator — builds a depot in full 3D, plans timetables on the real Polish rail
network, and manages rolling stock and staff.

**Status:** Pre-alpha, in active development.
**Platform:** PC (Steam), Early Access.
**Engine:** Unity 6000.4.0f1 (see `ProjectSettings/ProjectVersion.txt`), Universal Render Pipeline (URP 17.4), New Input System.

> **Note:** this is a **source-available** repository for viewing and reference only,
> not open source. See [LICENSE](LICENSE).

---

## Running

1. Open the project in Unity Hub (`6000.4.0f1` or compatible).
2. Open `Assets/Scenes/MainMenu.unity` — the game entry point.
3. Press Play.

For module-focused work: `Depot.unity` (3D depot editor), `MapScene.unity`
(2D Poland map), `GameCreator.unity` (new-game creator).

---

## Project structure

```
Assets/
├── Scenes/                — Unity scenes (MainMenu, GameCreator, Depot, MapScene)
├── Scripts/               — C# grouped into assemblies (asmdef-enforced boundaries)
│   ├── Core/              (RailwayManager.Core)        — no dependencies
│   ├── Fleet/             (RailwayManager.Fleet)       — rolling-stock data
│   ├── SharedUI/          (RailwayManager.SharedUI)    — shared UI (TopBar)
│   ├── MainMenu/          (RailwayManager.MainMenu)
│   ├── GameCreator/       (RailwayManager.GameCreator) — new-game creator
│   ├── Depot/             (RailwayManager.Depot)       — 3D depot system
│   ├── Map/               (RailwayManager.Map)         — 2D map + OSM
│   ├── Timetable/         (RailwayManager.Timetable)   — schedules, circulations, movement, economy
│   ├── Maintenance/       (RailwayManager.Maintenance) — rolling-stock upkeep, workshops
│   ├── Personnel/         (RailwayManager.Personnel)   — staff, shifts, 3D day cycle
│   └── SaveLoad/          (RailwayManager.SaveLoad)    — save/load (top layer)
├── Models/                — 3D models (placeholders pending final art)
├── Materials/             — materials and shaders
├── StreamingAssets/
│   └── Maps/Poland/       — compressed binary OSM maps (*.bin)
├── Plugins/               — vendored DLLs (K4os.Compression.LZ4, LibTessDotNet)
└── Tests/                 — EditMode/PlayMode tests (NUnit, Unity Test Framework)
```

---

## Architecture

The project uses **Assembly Definitions** to enforce module boundaries. Dependency
graph (an arrow upward means "the layer above depends on the one below"):

```
Core  (no dependencies)
  ↑
Fleet · SharedUI · MainMenu
  ↑
GameCreator · Depot
  ↑
Map
  ↑
Timetable    (schedules, circulations, 2D movement, economy)
  ↑
Maintenance  (rolling-stock upkeep, workshops)
  ↑
Personnel    (staff, shifts, 3D day cycle)
  ↑
SaveLoad     (top layer — references everything, referenced by nothing)
```

**Directionality:** Depot does **not** depend on Map (a former cycle was broken by
moving `DepotRailwayIntegration` into Map). Timetable references Depot only for
cross-system glue (the Map↔Depot handshake, workshops), not for pure scheduling logic.
Full conventions: [`docs/conventions.md`](docs/conventions.md).

---

## Key systems

- **Depot (3D)** — editor and simulation of a rail depot: tracks, turnouts, catenary,
  paths/parking, halls. Rolling stock sits on tracks and can be shunted. Coordinator:
  `DepotManager`. Details: [`docs/depot/README.md`](docs/depot/README.md).
- **Map (2D)** — Poland from OpenStreetMap: tracks, stations, LOD, chunk loading,
  rendered as meshes from binary data (`Assets/StreamingAssets/Maps/Poland/*.bin`).
- **Fleet** — rolling-stock catalog (EN57, EU07, FLIRT, EU160 Griffin, SA138, Impuls…),
  vehicle configurator, secondary market, order cart, inspection system.
- **Catenary** — 3-stage pipeline in `Depot/Catenary/`: zone classification → wire
  generation → support optimization, based on real PKP data.
- **Timetable** — schedule creator: IRJ commercial categories, routes, frequency,
  day calendar, time-aware A* pathfinding, cascading signal-block sections.
- **Circulations** — vehicle rotations independent of timetables, per-day vehicle
  assignment with drag & drop, composition lock, auto-generator.
- **Train movement** — 2D world simulation + 3D depot simulation in FixedUpdate
  (50 Hz, deterministic, multiplayer-friendly), with a Map↔Depot handshake.
- **Economy** — agent-based passengers, OD matrix, ticketing, demand modifiers
  (rush hour / weekends / seasonality), regional subsidies, reputation.
- **Maintenance** — per-component wear (8 components), breakdowns + rescue, workshops
  (service pits), parts inventory, external repair shops.
- **Personnel** — 10 roles, 1–5★ skills, crew rotations, multi-day schedules with
  hotels, a 3D day cycle (staff walk the depot).
- **Save/Load** — single `.rmsave` (gzipped, HMAC-signed) with per-module sections,
  schema versioning + migrator chain, i18n (PL/EN/DE/CZ).

---

## Language convention

This is a Polish project (a Polish-railway theme), and the codebase reflects that:

- **Polish** for rail-domain naming and most comments — e.g. `Pojazd` (vehicle),
  `Obieg` (circulation), `Rozkład` (timetable), `Stacja` (station), and IRJ category
  codes (`EI`, `EN`, `RO`, `MP`…) that mirror real PKP terminology.
- **English** for generic infrastructure — `Service`, `Manager`, `Factory`, etc.

A gradual migration of the codebase toward English is planned. This README is provided
in both English and [Polish](README.pl.md).

---

## Developer requirements

- **Unity 6000.4.0f1** (or compatible)
- **Git** — the repo uses `.gitattributes` (LF normalization) and `.editorconfig`
- **Git Smart Merge** recommended for Unity YAML scene/prefab files
  (see `docs/setup/git-smartmerge.md`)
- **IDE:** Rider / Visual Studio / VS Code (all read `.editorconfig`)

---

## Map data and licenses

The public repository intentionally does **not** include the full OSM runtime map
bundles (`poland-v7.bin`, `init-state-pl.bin`) — they are large and distributed
through official game/data channels.

Map data is © OpenStreetMap contributors, under the Open Database License (ODbL) 1.0.
The data-distribution model, OSM attribution, and download placeholders are described
in [docs/DATA_LICENSES.md](docs/DATA_LICENSES.md). Third-party components are inventoried
in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

---

## License

This repository is currently public for **viewing and reference only**; it is **not
open source**. Current terms are in [LICENSE](LICENSE). Private project in active
development; external materials remain under their own licenses and terms.
