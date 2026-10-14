"""Exercise the production MSBuild targets in disposable Windows fixtures."""

import ctypes
from ctypes import wintypes
import hashlib
import html
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
UPSTREAM = ROOT / "local-deps/Snoresville-M4A1-1.1.4/M4A1Mod.dll"
UPSTREAM_SHA = "600188D306782F6C1B26EA1BDA4A615BFF9AC4437318873D5728CD58B26A7154"
CURRENT_UPSTREAM = ROOT / "local-deps/Snoresville-M4A1-1.1.5/M4A1Mod.dll"
CURRENT_UPSTREAM_SHA = "023B3FE8F86F9DCE8FD9F203BA3580D565A9940D2E0E82AB3FA129EDC33CC6F0"


@unittest.skipUnless(os.name == "nt", "Windows file-lock regression checks")
class MsBuildRegressionTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue(UPSTREAM.is_file(), "Supply the verified 1.1.4 dependency in local-deps first")
        self.assertEqual(UPSTREAM_SHA, hashlib.sha256(UPSTREAM.read_bytes()).hexdigest().upper())
        self.scratch = tempfile.TemporaryDirectory(prefix="m4a1fix build regression ")
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        self.game = self.root / "game"
        managed = self.game / "Risk of Rain 2_Data/Managed"
        managed.mkdir(parents=True)
        for name in ("Assembly-CSharp.dll", "RoR2.dll", "UnityEngine.dll", "UnityEngine.CoreModule.dll",
                     "Unity.Addressables.dll", "Unity.ResourceManager.dll",
                     "com.unity.multiplayer-hlapi.Runtime.dll", "UnityEngine.UNETModule.dll"):
            (managed / name).write_bytes(b"fixture; not loaded")
        self.core = self.root / "core"
        self.core.mkdir()
        for name in ("BepInEx.dll", "0Harmony.dll"):
            (self.core / name).write_bytes(b"fixture; not loaded")
        self.mod = self.root / "M4A1Mod.dll"
        shutil.copy2(UPSTREAM, self.mod)
        project = self.root / "src/M4A1Fix"
        project.mkdir(parents=True)
        # Compile a small real net472 assembly to exercise artifact preparation.
        # Import the unmodified production validation and artifact targets.
        targets = html.escape(str(ROOT / "build/M4A1Fix.Build.targets"), quote=True)
        self.project = project / "M4A1Fix.csproj"
        self.project.write_text(r'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net472</TargetFramework><AssemblyName>M4A1Fix</AssemblyName>
<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
<ManagedPath>$(GamePath)\Risk of Rain 2_Data\Managed</ManagedPath>
<HlapiRuntimePath>$(ManagedPath)\com.unity.multiplayer-hlapi.Runtime.dll</HlapiRuntimePath>
<NetworkingAssemblyPath>$(ManagedPath)\UnityEngine.UNETModule.dll</NetworkingAssemblyPath></PropertyGroup>
<ItemGroup><PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies.net472" Version="1.0.3" PrivateAssets="All" /></ItemGroup>
<Import Project="''' + targets + '''" /></Project>''', encoding="utf-8")
        (project / "Fixture.cs").write_text("public class BuildFixture { public const int Value = 42; }", encoding="utf-8")
        self.artifact = self.root / "artifacts/M4A1Fix.dll"
        self.compiled = project / "bin/Release/M4A1Fix.dll"

    def run_build(self, extra=None):
        result = subprocess.run(
            ["dotnet", "build", str(self.project), "-c", "Release", "--nologo",
             "-p:GamePath=" + str(self.game), "-p:M4A1ModPath=" + str(self.mod),
             "-p:BepInExDllPath=" + str(self.core / "BepInEx.dll"),
             "-p:HarmonyDllPath=" + str(self.core / "0Harmony.dll")] + (extra or []),
            cwd=self.root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=90,
        )
        return result.returncode, result.stdout.decode("utf-8", errors="replace")

    def assert_failed(self, code, output):
        self.assertNotEqual(0, code, output)
        self.assertIn("error", output.lower())
        self.assertNotIn("M4A1Fix candidate:", output)
        self.assertNotIn("M4A1Fix candidate SHA-256:", output)

    def test_successful_candidate_matches_compiled_hash(self):
        code, output = self.run_build()
        self.assertEqual(0, code, output)
        expected = hashlib.sha256(self.compiled.read_bytes()).hexdigest()
        self.assertEqual(expected, hashlib.sha256(self.artifact.read_bytes()).hexdigest())
        self.assertIn("m4a1fix candidate sha-256: " + expected, output.lower())
        self.assertFalse(self.artifact.with_suffix(".dll.pending").exists())

    def test_current_upstream_hash_is_accepted(self):
        self.assertTrue(CURRENT_UPSTREAM.is_file(), "Supply the verified 1.1.5 dependency in local-deps first")
        self.assertEqual(CURRENT_UPSTREAM_SHA, hashlib.sha256(CURRENT_UPSTREAM.read_bytes()).hexdigest().upper())
        shutil.copy2(CURRENT_UPSTREAM, self.mod)
        code, output = self.run_build()
        self.assertEqual(0, code, output)
        self.assertEqual(hashlib.sha256(self.compiled.read_bytes()).digest(), hashlib.sha256(self.artifact.read_bytes()).digest())

    def test_missing_warmup_dependency_stops_before_compilation(self):
        for name in ("Unity.Addressables.dll", "Unity.ResourceManager.dll"):
            with self.subTest(dependency=name):
                path = self.game / "Risk of Rain 2_Data/Managed" / name
                previous = path.read_bytes()
                path.unlink()
                try:
                    code, output = self.run_build()
                    self.assert_failed(code, output)
                    self.assertIn(name.removesuffix(".dll") + " reference not found", output)
                    self.assertFalse(self.compiled.exists())
                finally:
                    path.write_bytes(previous)

    def test_clean_preserves_candidate_and_dependencies(self):
        code, output = self.run_build()
        self.assertEqual(0, code, output)
        self.assertTrue(self.artifact.is_file())
        clean = subprocess.run(["dotnet", "clean", str(self.project), "-c", "Release", "--nologo"],
                               cwd=self.root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=90)
        self.assertEqual(0, clean.returncode, clean.stdout.decode(errors="replace"))
        self.assertFalse(self.compiled.exists())
        self.assertTrue(self.artifact.is_file(), "The prepared handoff artifact is outside SDK bin/obj cleanup")
        self.assertTrue(self.mod.exists(), "Clean must preserve external dependencies")

    def test_wrong_dependency_stops_before_compilation(self):
        self.mod.write_bytes(b"unsupported upstream fixture")
        code, output = self.run_build()
        self.assert_failed(code, output)
        self.assertIn("M4A1Mod.dll SHA-256 mismatch", output)
        self.assertFalse(self.compiled.exists())

    def test_artifacts_path_failure_never_reports_success(self):
        (self.root / "artifacts").write_bytes(b"path occupied by a file")
        code, output = self.run_build()
        self.assert_failed(code, output)
        self.assertEqual(b"path occupied by a file", (self.root / "artifacts").read_bytes())

    def test_locked_candidate_preserves_previous_bytes(self):
        self.artifact.parent.mkdir()
        previous = b"previous candidate must survive"
        self.artifact.write_bytes(previous)
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                      wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        kernel.CreateFileW.restype = wintypes.HANDLE
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel.CloseHandle.restype = wintypes.BOOL
        handle = kernel.CreateFileW(str(self.artifact), 0x80000000, 0, None, 3, 0x80, None)
        self.assertNotEqual(ctypes.c_void_p(-1).value, handle, ctypes.get_last_error())
        try:
            code, output = self.run_build()
        finally:
            kernel.CloseHandle(handle)
        self.assert_failed(code, output)
        self.assertEqual(previous, self.artifact.read_bytes())
        self.assertFalse(self.artifact.with_suffix(".dll.pending").exists())

    def test_candidate_directory_is_preserved(self):
        self.artifact.mkdir(parents=True)
        sentinel = self.artifact / "keep.txt"
        sentinel.write_bytes(b"do not replace or clean this directory")
        code, output = self.run_build()
        self.assert_failed(code, output)
        self.assertEqual(b"do not replace or clean this directory", sentinel.read_bytes())
        self.assertEqual([sentinel], list(self.artifact.iterdir()))

    def test_staging_directory_is_preserved(self):
        staging = self.artifact.with_suffix(".dll.pending")
        staging.mkdir(parents=True)
        sentinel = staging / "keep.txt"
        sentinel.write_bytes(b"do not delete directory contents")
        code, output = self.run_build()
        self.assert_failed(code, output)
        self.assertEqual(b"do not delete directory contents", sentinel.read_bytes())
        self.assertEqual([sentinel], list(staging.iterdir()))
        self.assertFalse(self.artifact.exists())

    def test_missing_upstream_stops_before_compilation(self):
        self.mod.unlink()
        code, output = self.run_build()
        self.assert_failed(code, output)
        self.assertIn("Set M4A1ModPath", output)
        self.assertFalse(self.compiled.exists())

    def test_missing_game_path_stops_before_compilation(self):
        code, output = self.run_build(["-p:GamePath="])
        self.assert_failed(code, output)
        self.assertIn("Set GamePath", output)
        self.assertFalse(self.compiled.exists())

    def test_different_core_directories_are_rejected(self):
        other = self.root / "other core"
        other.mkdir()
        harmony = other / "0Harmony.dll"
        harmony.write_bytes(b"different selected core")
        code, output = self.run_build(["-p:HarmonyDllPath=" + str(harmony)])
        self.assert_failed(code, output)
        self.assertIn("same selected core directory", output)
        self.assertFalse(self.compiled.exists())

    def test_upstream_hash_cannot_be_overridden_by_property(self):
        self.mod.write_bytes(b"unsupported upstream fixture")
        code, output = self.run_build(["-p:_M4A1FixUpstreamHash=" + UPSTREAM_SHA])
        self.assert_failed(code, output)
        self.assertIn("M4A1Mod.dll SHA-256 mismatch", output)
        self.assertFalse(self.compiled.exists())

    def evaluate_references(self, core_input, extra=None):
        env = os.environ.copy()
        env["M4A1FIX_BEPINEX_CORE"] = str(core_input)
        result = subprocess.run(
            ["dotnet", "msbuild", str(ROOT / "src/M4A1Fix/M4A1Fix.csproj"), "--nologo",
             "-getProperty:BepInExDllPath,HarmonyDllPath"] + (extra or []),
            cwd=ROOT, env=env, capture_output=True, timeout=30,
        )
        self.assertEqual(0, result.returncode, result.stderr.decode(errors="replace"))
        return json.loads(result.stdout.decode("utf-8-sig"))["Properties"]

    def test_environment_accepts_core_directory_or_dll(self):
        for core_input in (self.core, self.core / "BepInEx.dll"):
            with self.subTest(core_input=str(core_input)):
                refs = self.evaluate_references(core_input)
                self.assertEqual(self.core / "BepInEx.dll", Path(refs["BepInExDllPath"]))
                self.assertEqual(self.core / "0Harmony.dll", Path(refs["HarmonyDllPath"]))

    def test_explicit_references_override_environment(self):
        other = self.root / "explicit core"
        other.mkdir()
        for name in ("BepInEx.dll", "0Harmony.dll"):
            (other / name).write_bytes(b"reference evaluation fixture")
        for property_arg in ("-p:BepInExCorePath=" + str(other), "-p:BepInExDllPath=" + str(other / "BepInEx.dll")):
            with self.subTest(property_arg=property_arg):
                refs = self.evaluate_references(self.core / "BepInEx.dll", [property_arg])
                self.assertEqual(other / "BepInEx.dll", Path(refs["BepInExDllPath"]))
                self.assertEqual(other / "0Harmony.dll", Path(refs["HarmonyDllPath"]))


if __name__ == "__main__":
    unittest.main()
