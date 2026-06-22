# Data Licences and OSM Distribution

This document describes how Railway Manager handles OpenStreetMap-derived runtime
data in public builds and public documentation.

This is operational guidance for the project, not legal advice.

---

## Scope

This document applies to runtime data bundles generated from OpenStreetMap by
the private `formap` pipeline, including:

- `Assets/StreamingAssets/Maps/Poland/poland-v7.bin`
- `Assets/StreamingAssets/Maps/Poland/init-state-pl.bin`
- future country bundles generated from OpenStreetMap for DLC or map packages

This document does not define the license for:

- game source code
- Unity scenes, prefabs, and UI code
- proprietary art, audio, writing, and branding
- proprietary data overlays that are kept separate from OSM-derived bundles

---

## Public Repo Policy

The public code repository intentionally does not include the full runtime map
bundles.

Reasons:

- the files are very large
- they are released only through official game/data channels
- the repository is meant to host source code and documentation, not the full
  shipping data payload

Implication:

- a fresh public clone may not contain the full Poland runtime map data
- this is expected and should be documented, not treated as an accidental repo
  breakage

---

## OSM Attribution

Minimum attribution text for the game, website, and release documentation:

`Map data from OpenStreetMap contributors, available under the Open Database License (ODbL) 1.0.`

Canonical copyright page:

- `https://www.openstreetmap.org/copyright`

Recommended placement:

- in-game `Credits`, `About`, or `Data Licences`
- release notes / store support page
- project website or release landing page

### Attribution requirements (OSMF Attribution Guidelines, adopted 2021-06-25)

Attribution is REQUIRED whenever a Produced Work is used publicly (ODbL). OSMF safe-harbour:

- **Text:** credit "OpenStreetMap" and make clear the data is under the ODbL. Acceptable
  forms: `© OpenStreetMap contributors`, `© OpenStreetMap`, or `Map data from OpenStreetMap`
  (when rendered to our own design). The word "OpenStreetMap" should be a **link to
  `https://www.openstreetmap.org/copyright`** (documents data sources + the ODbL).
- **Visibility:** must be shown to anyone who views/uses the map — **without requiring any
  interaction** (do not hide it behind a click). Placed in the vicinity of the work, or
  where users would customarily expect it.
- **Legibility:** font, size, colour, contrast, position, and **time on screen** must let the
  typical viewer read and comprehend it. WCAG recommended.
- **Access to detail:** if origin/licence detail is not in the text itself, provide a way to
  reach it (e.g. a clickable link).
- **No misleading impression:** other logos/text must not imply the data is NOT from OSM.
  Attribution may appear at the same time as / next to other attributions (e.g. the renderer's).

### Computer games and simulations (verbatim — this is RM's case)

> For video or computer games, attribution can be provided either by a splash screen on
> application startup, in the game view, during gameplay, on the credits page, in the menu,
> or in another suitable location. Detailed information must be provided in a suitable
> location if the initial attribution does not provide detailed information.
>
> Attribution text on a splash screen must be easily legible and visible such that the
> typical viewer has time to comprehend the attribution, though it may appear on the screen
> at the same time as other attribution (e.g., for the game's renderer).

**RM implementation plan (before EA):**
- Full, detailed attribution `© OpenStreetMap contributors` as a **clickable link** to
  openstreetmap.org/copyright on the **Credits / About / Data Licences screen**.
- Plus an always-visible, legible credit near the map (`MapScene`) — a screen corner or a
  startup splash, shown long enough to read; may sit next to renderer/asset attributions.
- If `.bin` data ships separately (see Distribution Model below), include OSM attribution
  + ODbL text/link in that package's readme/metadata (ODbL "Databases" requirement).

---

## Distribution Model For Public Builds

If a public build ships with OSM-derived `.bin` data, the same recipients should
also be offered a machine-readable copy of those data files outside the game
package.

Project policy:

1. The Steam build may include the required `.bin` files.
2. The project should also provide the same OSM-derived data files as a separate
   download for recipients of the public build.
3. If the store package uses DRM or other access restrictions, the parallel data
   download should remain available without those extra restrictions.
4. The separate download should include a short licence notice or link back to
   this document.

The private generator `formap` does not need to be published as part of this
policy.

---

## Future Domain Placeholder

When the project domain is ready, publish stable URLs similar to:

- `https://YOUR-DOMAIN.example/data/poland-v7.bin.zip`
- `https://YOUR-DOMAIN.example/data/init-state-pl.bin.zip`
- `https://YOUR-DOMAIN.example/data/DATA-LICENSES.txt`
- `https://YOUR-DOMAIN.example/data/openstreetmap/`

Replace `YOUR-DOMAIN.example` with the real production domain before the first
public release.

Suggested contents:

- `poland-v7.bin.zip` - runtime map bundle used by the game
- `init-state-pl.bin.zip` - prebuilt logic/pathfinding bundle derived from the
  runtime map bundle
- `DATA-LICENSES.txt` - short notice pointing to ODbL 1.0 and OSM attribution
- optional landing page with mirrors, checksums, and version history

---

## Steam Release Checklist

Before the first public Steam release:

1. Confirm the in-game credits/about/licences screen contains the OSM attribution as a
   **clickable link** to openstreetmap.org/copyright, AND that an always-visible, legible
   credit appears near the map / on the startup splash (per "Computer games and simulations"
   — shown without interaction, on screen long enough to read).
2. Publish the parallel data download on the project domain.
3. Verify that the download contains the same public `.bin` data shipped in the
   build.
4. Add a visible link from support docs or the project website to the data
   download.
5. Re-check that proprietary non-OSM overlays, if any, are stored separately
   from the OSM-derived bundles.

---

## Separation Rule For Future Proprietary Data

If future map-related data should remain proprietary, keep it in separate files
or separate feature layers wherever possible.

Do not mix proprietary factual map corrections directly into the OSM-derived
bundle unless the project is prepared to release those corrections under the
same data terms as the bundle.
