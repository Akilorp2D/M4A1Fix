# Building M4A1Fix 1.0.0

## Requirements

- Windows with a .NET SDK providing `dotnet` and access to NuGet restore.
- A local Risk of Rain 2 installation.
- BepInEx 5 core files from the exact installation/profile you choose to build against.
- The exact supported M4A1Mod 1.1.4 DLL.

No game, M4A1, BepInEx, Harmony, or other third-party DLL is committed to this repository. Build references are resolved from paths you explicitly select on your machine.

## Compile framework strategy

M4A1Fix targets **.NET Framework 4.7.2 (`net472`)** for compilation. The project pins the compile-only NuGet package:

```text
Microsoft.NETFramework.ReferenceAssemblies.net472 1.0.3
```

The package is marked `PrivateAssets=All`; it is used only to supply a coherent .NET Framework 4.7.2 reference assembly set to the compiler. It is **not** copied into the mod output and is not a runtime dependency. Restored NuGet packages are not committed to this repository.

This is intentional. The selected Risk of Rain 2/BepInEx/Harmony runtime used by this project exposes Harmony `CodeInstruction` branch labels through the classic `mscorlib` framework identity. Compiling the same source as SDK `netstandard2.1` caused `System.Reflection.Emit.Label` to be seen simultaneously through incompatible `netstandard.dll` and `mscorlib.dll` identities. Targeting `net472` makes the compiler use one .NET Framework identity for `Label` while keeping all game/mod runtime assemblies as external API references.

**Do not add `mscorlib.dll`, `netstandard.dll`, or other framework facade assemblies from the game directory as manual `<Reference>` items.** The framework reference set comes only from the pinned `net472` reference-assemblies package; game/BepInEx/Harmony/M4A1 assemblies remain explicit external references.

A normal `dotnet build` performs NuGet restore automatically. If restore access is restricted, restore the pinned package from an approved NuGet cache/source before building.

## Build command contract

Run from a normal **Command Prompt** in the repository root:

```cmd
BUILD.cmd [GamePath] [M4A1Mod.dll] [BepInExCorePath-or-BepInEx.dll]
```

Arguments:

1. `GamePath` — Risk of Rain 2 game root. This supplies the game's managed assemblies only.
2. `M4A1Mod.dll` — exact supported M4A1Mod 1.1.4 DLL.
3. `BepInExCorePath-or-BepInEx.dll` — either the selected profile's `BepInEx\core` directory or that directory's `BepInEx.dll`.

The BepInEx selection must provide both `BepInEx.dll` and `0Harmony.dll` in the same core directory.

The script does **not** scan for or auto-select Gale/r2modman profiles.

Environment-variable equivalents:

- `M4A1FIX_GAME_PATH`
- `M4A1FIX_M4A1_PATH`
- `M4A1FIX_BEPINEX_CORE`

`M4A1FIX_BEPINEX_CORE` may point either to the selected `BepInEx\core` directory or directly to its `BepInEx.dll`.

If no BepInEx argument/environment variable is supplied, the script uses `<GamePath>\BepInEx\core` only when a direct-install BepInEx actually exists there.

The default game path, when no first argument or environment variable is supplied, is:

```text
E:\SteamLibrary\steamapps\common\Risk of Rain 2
```

## Direct-install example

For a game installation where BepInEx and M4A1 are installed directly below the game root:

```cmd
BUILD.cmd "D:\Games\Risk of Rain 2" "D:\Games\Risk of Rain 2\BepInEx\plugins\M4A1\M4A1Mod.dll" "D:\Games\Risk of Rain 2\BepInEx\core"
```

If the direct-install tree contains the exact supported M4A1 bytes, omitting arguments 2 and 3 is also supported because the script may use the direct-install fallbacks:

```cmd
BUILD.cmd "D:\Games\Risk of Rain 2"
```

## Gale/r2modman selected-profile example

Select the exact profile yourself and pass its dependency paths explicitly. The game path remains independent from the profile path:

```cmd
BUILD.cmd "D:\Games\Risk of Rain 2" "D:\ModProfiles\SelectedProfile\BepInEx\plugins\M4A1\M4A1Mod.dll" "D:\ModProfiles\SelectedProfile\BepInEx\core"
```

You may pass the profile's `BepInEx.dll` instead of its core directory:

```cmd
BUILD.cmd "D:\Games\Risk of Rain 2" "D:\ModProfiles\SelectedProfile\BepInEx\plugins\M4A1\M4A1Mod.dll" "D:\ModProfiles\SelectedProfile\BepInEx\core\BepInEx.dll"
```

These paths are examples only. The repository does not hardcode a personal Gale/r2modman profile name or AppData location.

## Environment-variable example

```cmd
set "M4A1FIX_GAME_PATH=D:\Games\Risk of Rain 2"
set "M4A1FIX_M4A1_PATH=D:\ModProfiles\SelectedProfile\BepInEx\plugins\M4A1\M4A1Mod.dll"
set "M4A1FIX_BEPINEX_CORE=D:\ModProfiles\SelectedProfile\BepInEx\core"
BUILD.cmd
```

## Dependency verification

Before invoking `dotnet build`, `BUILD.cmd`:

- verifies the selected game managed-assembly tree exists;
- validates both selected `BepInEx.dll` and sibling `0Harmony.dll`;
- verifies the exact M4A1Mod SHA-256:
  `600188D306782F6C1B26EA1BDA4A615BFF9AC4437318873D5728CD58B26A7154`;
- passes the selected BepInEx and Harmony file paths explicitly to the project.

No game launch or runtime action is performed.

## Expected output

A successful build copies exactly the production assembly to:

```text
artifacts\M4A1Fix.dll
```

`artifacts/`, `bin/`, and `obj/` are ignored by Git.

## Direct MSBuild properties

The project uses the pinned compile-only `Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3 package and supports these explicit local-reference properties:

- `GamePath`
- `M4A1ModPath`
- `BepInExCorePath`
- `BepInExDllPath`
- `HarmonyDllPath`
- `HlapiRuntimePath`
- `NetworkingAssemblyPath`
- `NetworkingReferenceName`

Example with explicit file references:

```cmd
dotnet build src\M4A1Fix\M4A1Fix.csproj -c Release -p:GamePath="D:\Games\Risk of Rain 2" -p:M4A1ModPath="D:\Mods\M4A1Mod.dll" -p:BepInExDllPath="D:\SelectedProfile\BepInEx\core\BepInEx.dll" -p:HarmonyDllPath="D:\SelectedProfile\BepInEx\core\0Harmony.dll"
```

Expected production assembly: **`M4A1Fix.dll`**.
