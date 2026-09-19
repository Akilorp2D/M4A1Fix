# Third-party dependencies and assets

M4A1Fix is an independent compatibility/fix plugin. The M4A1Fix **source code** is licensed under the MIT License; see `LICENSE`. That project license does not automatically apply to third-party software, game content, the upstream M4A1 mod, or upstream artwork.

M4A1Fix is built against and interoperates with third-party software including:

- **Risk of Rain 2 / RoR2 assemblies** — game/runtime API references only.
- **BepInEx** — plugin loader/runtime API.
- **Harmony / 0Harmony** — runtime patching API.
- **Microsoft .NET Framework Reference Assemblies (`net472`, 1.0.3)** — compile-time reference package only; not distributed with the mod.
- **Snoresville's M4A1 mod** — supported mod API/runtime target; M4A1Fix does not claim ownership of M4A1 code or assets.

This repository does not intentionally redistribute the game, BepInEx, Harmony, or M4A1 runtime binaries. Their respective licenses, terms, and ownership remain with their authors/rightsholders.

## Release icon provenance

`release/icon.png` is a derivative release asset visually based on the supplied upstream M4A1 icon artwork. M4A1Fix does **not** claim ownership of that upstream artwork. Ownership of the underlying M4A1 artwork, and any license or permission terms that apply to it, remain with the original rightsholder(s).

The supplied project material did not include a definitive license or permission statement for the upstream M4A1 artwork, so no additional rights are asserted or invented here. The MIT License for M4A1Fix source code does **not** automatically relicense the upstream M4A1 artwork or the portions of `release/icon.png` derived from it.

The separate ItemShareFix image supplied during release preparation was used only as a visual/layout reference for the general idea of an unmistakable `FIX` marking. It is not included or redistributed as M4A1Fix content.
