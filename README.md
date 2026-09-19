# M4A1Fix

**M4A1Fix 1.0.0** is an independent Risk of Rain 2 compatibility/fix plugin for Snoresville's M4A1 mod.
It is not the M4A1 mod and does not redistribute M4A1 or game binaries.

## What it fixes

M4A1Fix contains two narrowly scoped fixes:

1. **Shrine of Shaping revive priority** — when M4A1 dies while the Shrine of Shaping extra-life buff is pending, the M4A1 death hook delegates that case back to the game's normal revive path before Dummy Link handling. This preserves the validated Shrine-before-other-revive-resource behavior.
2. **Dummy AI recovery after Shrine revival** — after a successful Shrine of Shaping revival of the exact `M4A1Body`, the server reassigns M4A1 dummy leader links so the dummies resume following and fighting instead of remaining tied to stale death-state AI.

## Supported dependency

The validated dependency is **M4A1Mod 1.1.4** with exact DLL SHA-256:

`600188D306782F6C1B26EA1BDA4A615BFF9AC4437318873D5728CD58B26A7154`

The plugin intentionally fails closed when the exact compatibility contract does not match.

## Installation

1. Install Risk of Rain 2 with BepInEx and the supported M4A1Mod 1.1.4.
2. Build or obtain `M4A1Fix.dll` 1.0.0.
3. Place **only the single `M4A1Fix.dll`** in a BepInEx plugin folder, for example `BepInEx\plugins\M4A1Fix\`.
4. Start the game normally.

Normal startup should report one `M4A1Fix 1.0.0` plugin and one installed runtime DLL.

## Compatibility and fail-closed behavior

The revive-priority patch validates the exact supported M4A1 assembly, the exact M4A1 death-hook IL, and the relevant Risk of Rain 2 revive methods before modifying anything. If that validation fails, the revive-priority patch is not left installed.

Shrine AI recovery is installed only after the revive-priority patch is confirmed healthy on the exact M4A1 death target and the loaded M4A1 plugin bytes match the supported hash. Failure of Shrine AI recovery does **not** remove an otherwise healthy revive-priority patch.

The Shrine AI recovery path is server-side, is restricted to the exact `M4A1Body`, and performs one bounded repair call:

`M4A1Mod.Survivors.M4A1.Utils.M4A1.ReassignDummyLinks(revivedBody)`

It does not call M4A1's full revive helper and does not alter revive resources, inventory, skill stocks, deployables, spawn logic, or RPC synchronization.

## Building

See [BUILDING.md](BUILDING.md). The repository does not contain Risk of Rain 2, Unity, BepInEx, Harmony, or M4A1 binaries.

## License

M4A1Fix source code is licensed under the [MIT License](LICENSE).

The MIT License applies to M4A1Fix source code. It does not automatically relicense Risk of Rain 2, BepInEx, Harmony, the upstream M4A1 mod, or upstream M4A1 artwork used as the visual basis for the release icon. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party ownership and provenance notes.
