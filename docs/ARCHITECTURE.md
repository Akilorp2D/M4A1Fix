# Architecture

M4A1Fix is a separate BepInEx plugin. Plugin.Awake installs the revival priority patch, then AI repair and optional first-run display warmup. Failed compatibility disables the affected work with a diagnostic; it is not bypassed.

| Area | Responsibility |
|---|---|
| Plugin.cs | Loader identity, startup, content-ready registration and independent teardown |
| Patches/RevivePriorityPolicy.cs | Authoritative-server/non-null-body guard and selected vanilla Shrine predicate |
| Patches/RevivePriorityPatch.cs | Harmony installation and exact owner/method health |
| Patches/IL/RevivePriorityTranspiler.cs | Identify original vanilla/Dummy decision, inject five instructions and preserve labels/EH |
| Patches/ShrineAiRecoveryPatch.cs | Reacquire live M4A1 body after Shrine respawn and call original ReassignDummyLinks |
| Patches/RunStartWarmup.cs | Wait for content and BodyCatalog readiness, discover display keys, start asynchronous operations and log timing |
| Runtime/AssetWarmupJob.cs | Managed bounded scheduling, readiness-independent operation completion, cancellation and owned cleanup |
| Compatibility/ItemDisplayWarmupContract.cs | Exact original initialization fingerprints and cache-state rollback |
| Compatibility/M4A1AssemblyContract.cs | Upstream file/hash, loaded death-hook shape and BepInEx instance identity |
| Compatibility/RoR2ReviveContract.cs | Select legacy/current vanilla Shrine reader after validation |
| Compatibility/CurrentRoR2ReviveContract.cs | Current disk identity, loaded revival canonical fingerprints, unmanaged-buff reader |
| Compatibility/IL/ | Original IL decoding, canonical formatting and fingerprints; no hooks are installed here |
| Runtime/HarmonyPatchLifecycle.cs | Reported owner cleanup and registered patch checks |
| build/M4A1Fix.Build.targets | Real dependency validation and checked atomic artifact preparation |

There is no fix-owned ILHook. Harmony's transpiler targets the original M4A1 On callback, retaining power-weapon cleanup and the original Dummy/Dio behavior. A Prefix replacing the entire handler would duplicate upstream logic; it is not used.

The warmup invokes only original LazyInit after successful asynchronous prefetch. Original Run.Start still applies the item-display rules and installs deferred skill hooks. It does not fabricate a Run, alter inventory or introduce RPCs. A run starting early cancels preparation and leaves the original path available.

Game assemblies can be memory-loaded by BepInEx after preloader rewriting. Empty Assembly.Location falls back to the selected BepInEx.Paths.ManagedPath/RoR2.dll for disk hashing; loaded method bodies are independently checked.

Private game lifecycle methods cannot all execute in a standalone CLR test host. Tests and compilation do not replace Unity/operator acceptance.
