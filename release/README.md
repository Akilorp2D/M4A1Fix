# Release payload

The final manual-install release payload for M4A1Fix 1.0.0 contains the single production plugin:

```text
M4A1Fix.dll
```

The repository also contains `release/icon.png` (256x256 PNG) for release/package presentation. The icon is **not** embedded into or required by the production DLL. See `../THIRD_PARTY_NOTICES.md` for upstream-art provenance and licensing boundaries.

Optional release documentation and a SHA-256 manifest may accompany the DLL. The runtime release contains only one plugin DLL.

Build output is intentionally not committed to the source repository. Use the root `BUILD.cmd` to produce `artifacts\M4A1Fix.dll` from local dependencies.
