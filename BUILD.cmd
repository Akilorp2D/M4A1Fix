@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "EXPECTED_M4A1_SHA=600188D306782F6C1B26EA1BDA4A615BFF9AC4437318873D5728CD58B26A7154"

rem ---------------------------------------------------------------------------
rem Input contract:
rem   BUILD.cmd [GamePath] [M4A1Mod.dll] [BepInExCorePath-or-BepInEx.dll]
rem Environment equivalents:
rem   M4A1FIX_GAME_PATH
rem   M4A1FIX_M4A1_PATH
rem   M4A1FIX_BEPINEX_CORE
rem ---------------------------------------------------------------------------

set "GAME_PATH=%~1"
if not defined GAME_PATH set "GAME_PATH=%M4A1FIX_GAME_PATH%"
if not defined GAME_PATH set "GAME_PATH=E:\SteamLibrary\steamapps\common\Risk of Rain 2"

set "M4A1_PATH=%~2"
if not defined M4A1_PATH set "M4A1_PATH=%M4A1FIX_M4A1_PATH%"

set "BEPINEX_INPUT=%~3"
if not defined BEPINEX_INPUT set "BEPINEX_INPUT=%M4A1FIX_BEPINEX_CORE%"

if not exist "%GAME_PATH%\Risk of Rain 2_Data\Managed\Assembly-CSharp.dll" (
  echo [FAIL] Risk of Rain 2 managed assemblies were not found under:
  echo        %GAME_PATH%
  exit /b 2
)

set "BEPINEX_DLL="
set "HARMONY_DLL="
set "BEPINEX_SOURCE="

if defined BEPINEX_INPUT (
  if exist "%BEPINEX_INPUT%\BepInEx.dll" (
    set "BEPINEX_DLL=%BEPINEX_INPUT%\BepInEx.dll"
    set "HARMONY_DLL=%BEPINEX_INPUT%\0Harmony.dll"
    set "BEPINEX_SOURCE=explicit core directory"
  ) else if exist "%BEPINEX_INPUT%" (
    for %%I in ("%BEPINEX_INPUT%") do (
      if /I "%%~nxI"=="BepInEx.dll" (
        set "BEPINEX_DLL=%%~fI"
        set "HARMONY_DLL=%%~dpI0Harmony.dll"
        set "BEPINEX_SOURCE=explicit BepInEx.dll"
      )
    )
  )

  if not defined BEPINEX_DLL (
    echo [FAIL] The explicitly selected BepInEx location is invalid:
    echo        %BEPINEX_INPUT%
    echo        Pass either the BepInEx core directory or its BepInEx.dll.
    exit /b 3
  )
) else (
  if exist "%GAME_PATH%\BepInEx\core\BepInEx.dll" (
    set "BEPINEX_DLL=%GAME_PATH%\BepInEx\core\BepInEx.dll"
    set "HARMONY_DLL=%GAME_PATH%\BepInEx\core\0Harmony.dll"
    set "BEPINEX_SOURCE=direct-install fallback"
  ) else (
    echo [FAIL] No BepInEx core was selected and no direct-install fallback exists.
    echo        This script never auto-selects a Gale/r2modman profile.
    echo        Pass the third argument or set M4A1FIX_BEPINEX_CORE.
    exit /b 3
  )
)

if not exist "%BEPINEX_DLL%" (
  echo [FAIL] BepInEx.dll was not found at the selected location:
  echo        %BEPINEX_DLL%
  exit /b 3
)

if not exist "%HARMONY_DLL%" (
  echo [FAIL] 0Harmony.dll was not found beside the selected BepInEx.dll:
  echo        %HARMONY_DLL%
  exit /b 3
)

echo [PASS] BepInEx references selected via %BEPINEX_SOURCE%.
echo [INFO] BepInEx: %BEPINEX_DLL%
echo [INFO] Harmony: %HARMONY_DLL%

if not defined M4A1_PATH (
  echo [INFO] M4A1Mod.dll path was not supplied.
  echo [INFO] Checking only the direct-install BepInEx\plugins fallback under GamePath...
  if exist "%GAME_PATH%\BepInEx\plugins" (
    for /r "%GAME_PATH%\BepInEx\plugins" %%F in (M4A1Mod.dll) do (
      call :HASH "%%~fF" FOUND_HASH
      if /I "!FOUND_HASH!"=="%EXPECTED_M4A1_SHA%" if not defined M4A1_PATH set "M4A1_PATH=%%~fF"
    )
  )
)

if not defined M4A1_PATH (
  echo [FAIL] Exact M4A1Mod 1.1.4 DLL was not selected or found in the direct-install fallback.
  echo        For Gale/r2modman, pass its exact DLL as the second argument
  echo        or set M4A1FIX_M4A1_PATH.
  exit /b 4
)

if not exist "%M4A1_PATH%" (
  echo [FAIL] M4A1Mod.dll path does not exist:
  echo        %M4A1_PATH%
  exit /b 5
)

call :HASH "%M4A1_PATH%" ACTUAL_M4A1_SHA
if /I not "%ACTUAL_M4A1_SHA%"=="%EXPECTED_M4A1_SHA%" (
  echo [FAIL] M4A1Mod.dll SHA-256 mismatch.
  echo        Expected: %EXPECTED_M4A1_SHA%
  echo        Actual:   %ACTUAL_M4A1_SHA%
  exit /b 6
)

echo [PASS] Exact M4A1Mod dependency verified.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [FAIL] dotnet SDK was not found in PATH.
  exit /b 7
)

set "PROJECT=%ROOT%src\M4A1Fix\M4A1Fix.csproj"
echo [INFO] Building M4A1Fix 1.0.0...
dotnet build "%PROJECT%" -c Release ^
  -p:GamePath="%GAME_PATH%" ^
  -p:M4A1ModPath="%M4A1_PATH%" ^
  -p:BepInExDllPath="%BEPINEX_DLL%" ^
  -p:HarmonyDllPath="%HARMONY_DLL%" ^
  --nologo
if errorlevel 1 (
  echo [FAIL] Build failed.
  exit /b 8
)

set "DLL=%ROOT%src\M4A1Fix\bin\Release\M4A1Fix.dll"
if not exist "%DLL%" (
  echo [FAIL] Build reported success but M4A1Fix.dll was not found:
  echo        %DLL%
  exit /b 9
)

if not exist "%ROOT%artifacts" mkdir "%ROOT%artifacts"
del /q "%ROOT%artifacts\M4A1Fix.dll" >nul 2>nul
copy /y "%DLL%" "%ROOT%artifacts\M4A1Fix.dll" >nul
call :HASH "%ROOT%artifacts\M4A1Fix.dll" OUTPUT_SHA

echo [PASS] Build complete: %ROOT%artifacts\M4A1Fix.dll
echo [PASS] SHA-256: %OUTPUT_SHA%
exit /b 0

:HASH
set "HASH_VALUE="
for /f "skip=1 tokens=* delims=" %%H in ('certutil -hashfile "%~1" SHA256 2^>nul') do if not defined HASH_VALUE set "HASH_VALUE=%%H"
set "HASH_VALUE=!HASH_VALUE: =!"
set "%~2=!HASH_VALUE!"
exit /b 0
