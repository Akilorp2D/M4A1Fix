# Building and testing M4A1Fix

## Prerequisites

- Windows, a .NET SDK with MSBuild/RoslynCodeTaskFactory, and NuGet restore access or populated package caches.
- Your own Risk of Rain 2 installation with its Managed assemblies, including Unity.Addressables.dll and Unity.ResourceManager.dll.
- BepInEx 5 and 0Harmony from the same selected installation/profile core.
- Exact M4A1 1.1.5 DLL. Its expected SHA-256 and supported game image are in [compatibility](docs/COMPATIBILITY.md).
- The private compile-time package Microsoft.NETFramework.ReferenceAssemblies.net472 1.0.3. Do not add the game's mscorlib/netstandard facades as replacements.

## Compile from CMD

At the extracted source root, set these variables to your own files. The angle-bracket placeholders must be replaced before execution; no installation or profile is selected automatically.

```cmd
set "M4A1FIX_GAME_PATH=<your Risk of Rain 2 installation directory>"
set "M4A1FIX_M4A1_PATH=<full path to your M4A1Mod.dll>"
set "M4A1FIX_BEPINEX_CORE=<your selected BepInEx core directory>"
dotnet build src\M4A1Fix\M4A1Fix.csproj -c Release
```

The target stays net472. Native `build/M4A1Fix.Build.targets` validates required files and the upstream hash, stages output and atomically prepares `artifacts/M4A1Fix.dll`. Failed validation or locked files do not silently replace the previous candidate. Runtime loaded-method/game compatibility is checked separately by the plugin.

Explicit properties override environment variables: GamePath, M4A1ModPath, BepInExCorePath, BepInExDllPath and HarmonyDllPath. Other overrides are HlapiRuntimePath, NetworkingAssemblyPath and NetworkingReferenceName. GamePath and the selected profile core are independent. Production third-party references use Private=false and are not bundled.

## C# regression tests

The test project uses MSTest/VSTest on net472. It requires the same real dependencies plus the generated MMHOOK_RoR2.dll from the selected profile and a separate frozen M4A1Fix 1.0.1 Thunderstore package for equivalence tests. Neither is an internal instruction/report; both are external test data and must be supplied explicitly. No Skill, agent configuration or QA document is needed.

The frozen package must contain `plugins/M4A1Fix/M4A1Fix.dll` with SHA-256 `70D19CB9D57DFC06AF3E9B7AFF98291B5E7EFEE34ED2A07FDE024EF3D781DA7D`. Obtain it from the M4A1Fix 1.0.1 release, not an arbitrary rebuild. The default sibling-baseline path in the test project is optional: override it as below.

```cmd
set "M4A1FIX_HOOK_PATH=<full path to your MMHOOK_RoR2.dll>"
set "M4A1FIX_BASELINE_ARCHIVE=<full path to the frozen 1.0.1 package ZIP>"
dotnet test tests\M4A1Fix.Tests\M4A1Fix.Tests.csproj -c Release -p:GamePath="%M4A1FIX_GAME_PATH%" -p:M4A1ModPath="%M4A1FIX_M4A1_PATH%" -p:BepInExDllPath="%M4A1FIX_BEPINEX_CORE%\BepInEx.dll" -p:HarmonyDllPath="%M4A1FIX_BEPINEX_CORE%\0Harmony.dll" -p:HookAssemblyPath="%M4A1FIX_HOOK_PATH%" -p:BaselineArchivePath="%M4A1FIX_BASELINE_ARCHIVE%" --settings tests\patch-tests.runsettings
```

The runsettings selects x86 for the inspected loader. Tests copy external references only to ignored test output. Managed scheduler, rollback, contracts and IL control-flow checks do not execute Unity Addressables or multiplayer.

## Native MSBuild integration tests

Install Python 3 and provide the official hash-verified original DLLs at these ignored relative paths:

- `local-deps/Snoresville-M4A1-1.1.4/M4A1Mod.dll` — SHA-256 `600188D306782F6C1B26EA1BDA4A615BFF9AC4437318873D5728CD58B26A7154`.
- `local-deps/Snoresville-M4A1-1.1.5/M4A1Mod.dll` — SHA-256 `023B3FE8F86F9DCE8FD9F203BA3580D565A9940D2E0E82AB3FA129EDC33CC6F0`.

```cmd
python -m unittest discover -s tests -p test_msbuild.py -v
```

These tests import real build targets into disposable projects and exercise hash rejection, missing dependencies, staging and Windows file locks. Fixture placeholders are never used as gameplay dependencies.

## Output and rebuild identity

Version metadata is 1.1.0 / assembly and file 1.1.0.0. Source/debug paths and toolchains can change the DLL hash on rebuild. Record your own hash separately; a clean-export verification build does not replace the distributed binary. For runtime scenarios, see [testing](docs/TESTING.md).
