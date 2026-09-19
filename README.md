# M4A1Fix

**M4A1Fix** is a small compatibility/fix plugin for the **M4A1** mod in Risk of Rain 2.

It fixes an issue in M4A1's death and revival handling where multiple available revival sources could be consumed by a single death.

The fix is intended for **M4A1Mod 1.1.4**.

## What it fixes

In the affected M4A1 version, dying while multiple revival sources were available could cause more than one of them to be consumed at the same time.

For example, if M4A1 had:

- the **Shrine of Shaping** extra-life effect;
- a **Dummy Link**;
- a revival item;

a single death could consume multiple revival sources even though only one was needed.

**M4A1Fix** corrects the revival priority and consumption behavior.

Now:

- if the Shrine of Shaping extra life is available, that revival is used;
- Dummy Link is not consumed at the same time;
- revival items are not consumed together with it;
- when Dummy Link is used for revival, other revival items should not be consumed unnecessarily;
- one death should consume only the revival source that is actually used.

## Compatibility

M4A1Fix 1.0.0 is intended for:

- **Risk of Rain 2**
- **BepInEx**
- **M4A1Mod 1.1.4**
- **M4A1Fix 1.0.0**

This fix was created for M4A1Mod 1.1.4, where the issue described above is present.

A future M4A1 update may change or fix the affected revival behavior. In that case, M4A1Fix may no longer be required or may need an update.

## Installation

1. Install **BepInEx**.
2. Install **M4A1Mod 1.1.4**.
3. Download the M4A1Fix release archive.
4. Extract the included `M4A1Fix` folder into:

   `BepInEx\plugins\`

The final installation should look like this:

`BepInEx\plugins\M4A1Fix\M4A1Fix.dll`

5. Start the game normally.

## Testing status

**M4A1Fix 1.0.0** has been tested in single-player.

The corrected death and revival behavior works correctly in the tested scenarios.

### Multiplayer

Dedicated multiplayer runtime testing has not yet been completed for version 1.0.0.

This does not indicate a known multiplayer issue; multiplayer behavior is simply not yet independently validated.

## Source code

The source code for M4A1Fix is public.

This project is a small fix for a specific issue in M4A1, so the code may be studied, modified, or reused under the terms of the MIT License.

If the original M4A1 developer wants to integrate this fix, or parts of it, directly into M4A1, that is fully welcome.

The goal of this project is to fix the issue, not to maintain a competing implementation of the same mechanic.

## Building

The repository contains the source code and files required to build M4A1Fix locally.

See [BUILDING.md](BUILDING.md) for build instructions.

For convenience, the repository also includes `BUILD.cmd` for local builds.

## AI-assisted development

AI tools were used during development to assist with code analysis, implementation support, test workflow organization, and documentation.

Final runtime validation and release decisions were performed by the project owner.

## License

The M4A1Fix source code is licensed under the **MIT License**.

See [LICENSE](LICENSE).
