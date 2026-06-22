# Third-Party License Texts

This directory stores local copies of license texts for vendored third-party
DLLs that remain in `Assets/Plugins/`.

Current contents:

- `K4os.Compression.LZ4.LICENSE.txt`
- `LibTessDotNet.LICENSE.txt`
- `System.Runtime.CompilerServices.Unsafe.LICENSE.txt`

`LibTessDotNet.dll` is recorded here under `SGI-B-2.0` as a conservative
project policy because the upstream repository README and `LICENSE.txt` agree
on that license text, while the NuGet package metadata for `1.1.15` separately
advertises MIT. See `THIRD_PARTY_NOTICES.md` for the audit trail.
