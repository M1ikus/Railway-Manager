# Third-Party Notices

This file is a working inventory of third-party components present in the
repository or shipped with the project.

It is intended to support public repository hygiene and release preparation.
It is not legal advice.

This file should be read together with:

- [LICENSE](LICENSE) - repository-level access terms
- [docs/DATA_LICENSES.md](docs/DATA_LICENSES.md) - OpenStreetMap-derived runtime data

Status values used in this file:

- `ok to keep` - the repository already contains a clear local license notice
- `verify` - likely fine, but the exact redistribution basis should be checked
- `consider removing` - not obviously needed in a public code repository, and
  removing it would simplify legal review

---

## Components

| Component | Path | Origin | License / Terms | Status | Notes |
| --- | --- | --- | --- | --- | --- |
| Noto Sans | `Assets/Fonts/Noto_Sans/` | Google Noto | SIL Open Font License 1.1 | `ok to keep` | Local notice present in `OFL.txt` and `README.txt`. |
| Noto Sans JP | `Assets/Fonts/Noto_Sans_JP/` | Google Noto | SIL Open Font License 1.1 | `ok to keep` | Local notice present in `OFL.txt` and `README.txt`. |
| Noto Sans Symbols 2 | `Assets/Fonts/Noto_Sans_Symbols_2/` | Google Noto | SIL Open Font License 1.1 | `ok to keep` | Local notice present in `OFL.txt`. |
| TextMesh Pro core assets | `Assets/TextMesh Pro/` | Unity / TextMesh Pro | Unity package / embedded asset terms plus bundled font notices | `verify` | Repository contains runtime assets, bundled fonts, and metadata with Unity `licenseType` markers. Keep only what is actually required by the game. |
| TextMesh Pro Examples & Extras | `Assets/TextMesh Pro/Examples & Extras/` | Unity / TextMesh Pro | Unity package sample content plus bundled font notices | `consider removing` | Large sample/demo payload. Repository searches did not find `GUID` references or text references outside this folder, so it is a strong candidate to keep local-only and exclude from the public repo. |
| LiberationSans font distributed with TMP | `Assets/TextMesh Pro/Fonts/LiberationSans.ttf` | TextMesh Pro bundle | SIL Open Font License 1.1 | `ok to keep` | Local notice present in `Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt`. |
| EmojiOne sprite assets bundled with TMP | `Assets/TextMesh Pro/Sprites/` | TextMesh Pro bundle | See bundled attribution | `verify` | Attribution file exists in `Assets/TextMesh Pro/Sprites/EmojiOne Attribution.txt`. Confirm whether all tracked TMP sprite assets are needed publicly. |
| Seamless Grass Textures | `Assets/Seamless Grass Textures/` | Unity Asset Store package (`productId: 328603`) | Asset Store terms - verify redistribution | `verify` | The package is not dead weight: `Assets/Scenes/Depot.unity` currently references `Grass III.mat` as `customGroundMaterial`. Public redistribution of raw package assets should be reviewed before leaving this folder in a public repo. |
| Seamless Grass Textures package archive | `Assets/Seamless Grass Textures/URP.unitypackage` | Unity Asset Store package (`productId: 328603`) | Asset Store terms - verify redistribution | `consider removing` | No tracked project assets currently reference this archive outside its own `.meta` file. It should remain local-only and stay out of the public repository. |
| K4os.Compression.LZ4 | `Assets/Plugins/K4os.Compression.LZ4.dll` | External .NET library by Milosz Krajewski | MIT | `ok to keep` | Vendored DLL reports `FileVersion 1.3.8.0` / `ProductVersion 1.3.8`, matching the official NuGet package `K4os.Compression.LZ4` 1.3.8 and upstream MIT license. Local license copy stored in `third_party/licenses/K4os.Compression.LZ4.LICENSE.txt`. |
| LibTessDotNet | `Assets/Plugins/LibTessDotNet.dll` | External .NET library by Remi Gillig / `speps` | SGI Free Software License B v2.0 (`SGI-B-2.0`) | `ok to keep` | Vendored DLL reports `FileVersion 1.1.15` / `ProductVersion 1.1.15`, matching `LibTessDotNet` 1.1.15. This repository records the DLL under `SGI-B-2.0` conservatively because the upstream repo README and `LICENSE.txt` agree on that license text, even though the NuGet Gallery About box separately advertises MIT. Local license copy stored in `third_party/licenses/LibTessDotNet.LICENSE.txt`. |
| System.Runtime.CompilerServices.Unsafe | `Assets/Plugins/System.Runtime.CompilerServices.Unsafe.dll` | Microsoft .NET library | MIT | `ok to keep` | Vendored DLL reports `FileVersion 5.0.20.51904` / `ProductVersion 5.0.0+cf258a14b70ad9069470a108f13765e0e5988f51`, matching the official `System.Runtime.CompilerServices.Unsafe` 5.0.0 package line. The package is deprecated, but its official license basis is MIT. Local license copy stored in `third_party/licenses/System.Runtime.CompilerServices.Unsafe.LICENSE.txt`. |

---

## Not Covered As Third-Party

The following are intentionally not treated as third-party components here:

- `Assets/Plugins/RailwayManager.GraphData.dll`
  - appears to be a first-party project artifact tied to the private `formap`
    pipeline
- OpenStreetMap-derived runtime map bundles
  - covered separately by `docs/DATA_LICENSES.md`
- Unity packages declared only in `Packages/manifest.json`
  - package management metadata is present, but those packages are not tracked
    here as vendored repository assets unless their content is actually copied
    into `Assets/`

---

## DLL Audit Sources

- `K4os.Compression.LZ4.dll`
  - file metadata from the vendored DLL: `FileVersion 1.3.8.0`,
    `ProductVersion 1.3.8`
  - official package page: <https://www.nuget.org/packages/K4os.Compression.LZ4/1.3.8>
  - upstream license: <https://raw.githubusercontent.com/MiloszKrajewski/K4os.Compression.LZ4/refs/heads/master/LICENSE>
- `LibTessDotNet.dll`
  - file metadata from the vendored DLL: `FileVersion 1.1.15`,
    `ProductVersion 1.1.15`
  - official package page: <https://www.nuget.org/packages/LibTessDotNet/1.1.15>
  - upstream repo README and license:
    <https://github.com/speps/LibTessDotNet>,
    <https://github.com/speps/LibTessDotNet/blob/master/LICENSE.txt>
  - note: the package page currently shows `MIT license` in the NuGet `About`
    box while the embedded README and upstream license file both state
    `SGI FREE SOFTWARE LICENSE B (Version 2.0, Sept. 18, 2008)`
- `System.Runtime.CompilerServices.Unsafe.dll`
  - file metadata from the vendored DLL: `FileVersion 5.0.20.51904`,
    `ProductVersion 5.0.0+cf258a14b70ad9069470a108f13765e0e5988f51`
  - official package page:
    <https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe/5.0.0>
  - upstream runtime license:
    <https://github.com/dotnet/runtime/blob/main/LICENSE.TXT>

---

## Recommended Follow-Up

1. If the project is later opened in Unity after this cleanup, sanity-check
   that no production scene or prefab unexpectedly recreates a dependency on
   `Assets/TextMesh Pro/Examples & Extras/`.
2. Decide whether `Assets/Seamless Grass Textures/` should remain in the public
   repository or move to a private distribution path after material
   replacements exist for scenes that still depend on it.
3. If you want to be extra thorough, ask the `LibTessDotNet` maintainer to
   clarify why the NuGet `About` metadata shows MIT while the upstream README
   and `LICENSE.txt` both state `SGI-B-2.0`.
4. Expand this file whenever a new external asset pack, font, plugin, or
   bundled binary is added to `Assets/`.
