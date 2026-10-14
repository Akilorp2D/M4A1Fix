# M4A1Fix

An independent BepInEx patch plugin for Snoresville's M4A1 survivor in Risk of Rain 2.

## Fixes

- Revival priority: **Shrine of Shaping → one Dummy Link per death → Dio's Best Friend → final death**. Existing revival implementations retain ownership of consumption and respawn.
- Dummy AI recovery: reconnect existing Dummy leaders to the revived M4A1 body after Shrine revival, on the authoritative server.
- First-run preparation: asynchronously prepare original item-display assets before the first run to reduce the pause after pressing Ready.

The original M4A1Mod.dll is never replaced. Original deferred skill hooks remain unchanged; preparation does not guarantee elimination of every loading delay.

## Compatibility

Version 1.1.0 supports the inspected **M4A1 1.1.5 / Risk of Rain 2 1.5.0#1210*** pair. Exact binary and method contracts fail closed on unknown builds. See [compatibility](docs/COMPATIBILITY.md).

Single-player testing has confirmed the tested revival fixes and successful preparation before the first run. **Two-PC multiplayer testing is not completed.** Full model/skill regression coverage and precise transition benchmarks remain open.

## Installation

Install M4A1 1.1.5 and its dependencies first. With the game closed, copy the manual package's M4A1Fix.dll into `BepInEx/plugins/M4A1Fix/` in your selected installation or mod-manager profile. Keep exactly one active copy. Store backups outside `BepInEx/plugins`.

## Development

Read [BUILDING.md](BUILDING.md), [architecture](docs/ARCHITECTURE.md), [how the fixes work](docs/HOW_THE_FIXES_WORK.md), and [testing](docs/TESTING.md).

Target: net472. Production and test sources are included; game/mod/loader binaries are external local dependencies. Builds do not install or publish the mod. No batch build wrapper is required.

## License

MIT applies to this patch's source. Third-party binaries and artwork retain their own rights; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
