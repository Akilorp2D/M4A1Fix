# How M4A1Fix works

M4A1Fix is an independent BepInEx patch plugin. It does not modify or replace the original M4A1 binary. It preserves revival priority and reconnects Dummy AI after Shrine revival. The current candidate additionally prepares original item-display assets before the first run.

Read the ordinary C# policy in `Patches/RevivePriorityPolicy.cs` first. The only instruction rewriting lives in `Patches/IL/RevivePriorityTranspiler.cs`. Compatibility parsing is isolated under `Compatibility/IL/` and is not part of the gameplay algorithm.

## The original conflict

M4A1 wraps `CharacterMaster.OnBodyDeath` with its own `DummyLink.CharacterMaster_OnBodyDeath` callback. For a player-controlled M4A1, this callback can select a Dummy Link and schedule M4A1's existing revival path before handing control to the game's normal death handler.

The game's Shrine extra life is represented by `DLC2Content.Buffs.ExtraLifeBuff` on the dying body. Vanilla revival handles that buff in `CharacterMaster.TryReviveOnBodyDeath`. Selecting the M4A1 Dummy path before vanilla gets to this decision can spend or schedule a different revival source while the Shrine life is still available. Independent revival paths must not be started for the same death.

The fix does not reproduce either revival implementation. It changes which existing implementation receives the death first.

## Priority in ordinary C# terms

The relevant upstream decision can be understood as:

```csharp
PerformOriginalDeathCleanup();

if (UpstreamAlreadyWantsTheVanillaPath())
    InvokeOriginalDeathHandler();
else
    ContinueOriginalDummyOrDioDecision();
```

The fixed decision is:

```csharp
PerformOriginalDeathCleanup();

if (UpstreamAlreadyWantsTheVanillaPath())
    InvokeOriginalDeathHandler();
else if (NetworkServer.active && SelectedVanillaShrineReader(dyingBody, shrineExtraLifeBuff))
    InvokeOriginalDeathHandler();
else
    ContinueOriginalDummyOrDioDecision();
```

These examples explain the branch, not replacement code for the original mod. The real predicate also rejects a null body. Unrelated bodies and non-player M4A1s keep the upstream decision they already had.

On the supported game contract, vanilla checks and consumes the Shrine buff before its Dio transformation. Therefore sending the Shrine case to vanilla preserves Dummy Links and Dio for later deaths. Without Shrine, the original M4A1 Dummy choice remains ahead of its vanilla/Dio fallback. The intended observable sequence is:

**Shrine of Shaping → one Dummy Link per death → Dio's Best Friend → final death.**

The fix neither removes a buff nor gives/takes items, invokes an extra respawn, picks a second Dummy, or creates a parallel revival schedule. Those operations remain owned by vanilla and the original mod. Actual counts, mixed-source scenarios and final death must still be verified in the game; a control-flow test is not a resource-consumption runtime test.

## Why a targeted Harmony transpiler remains

The original callback does useful work before choosing Dummy or vanilla, including power-weapon cleanup. A whole-method Prefix or C# detour that skips this callback in the Shrine case would also skip that work, unless it copied upstream implementation details. A Postfix would run after the revival choice and would need to undo already scheduled work. A Prefix on vanilla cannot enforce priority when the upstream wrapper bypasses vanilla entirely.

The retained Harmony transpiler inserts only five instructions at the existing decision point. They mean:

1. Preserve the existing non-Dummy decision.
2. Otherwise ask the small server-side Shrine policy.
3. If the policy is true, jump to the callback's existing vanilla invocation.
4. Otherwise fall through to the existing Dummy decision.

The matcher verifies the callback argument loads, the unique Dummy-selection call and the expected local/brfalse branch. The injector preserves incoming jump labels and exception-region boundaries. It does not replace the rest of the method.

There is no fix-owned `ILHook` or new detour stack. Harmony modifies the upstream On callback, and a separate Harmony postfix handles AI recovery. `RevivePriorityPatch` owns installation/health, while `RevivePriorityTranspiler` owns matching and rewriting. A failed subsequent transpiler pass must not reuse a previous successful health result.

## Why Dummy AI needs repair

`CharacterMaster` survives a death, but the revived `CharacterBody` and its health component may be new objects. An existing Dummy AI leader can still reference the previous body. A successful Shrine revival needs the existing Dummies to follow and fight for the revived body again.

After `CharacterMaster.RespawnExtraLifeShrine` returns, the recovery postfix:

- runs only on the authoritative server;
- obtains the current body from the master;
- requires a live health component and the exact `M4A1Body` index;
- requires the priority patch to remain healthy;
- calls `M4A1Mod.Survivors.M4A1.Utils.M4A1.ReassignDummyLinks(revivedBody)` once.

It does not call the full M4A1 revive helper, rebuild inventory/deployables, spawn another body, or introduce explicit RPC synchronization. It uses the existing upstream leader-reassignment operation. Other peers' visible state and AI behavior still require multiplayer evidence.

## Targets and ownership

| Component | Target or operation |
|---|---|
| Priority transpiler | `M4A1Mod.Survivors.M4A1.Hooks.DummyLink.CharacterMaster_OnBodyDeath(orig_OnBodyDeath, CharacterMaster, CharacterBody)` |
| Existing branch landmarks | `orig_OnBodyDeath.Invoke` and `Utils.M4A1.GetSomeChildDummyLink` |
| AI postfix | `RoR2.CharacterMaster.RespawnExtraLifeShrine()` |
| Bounded upstream repair | `Utils.M4A1.ReassignDummyLinks(CharacterBody)` |

The priority and recovery patches have separate Harmony owners. Recovery installation failure does not remove a healthy priority patch. Failed rollback retains its owner handle so plugin destruction can retry cleanup. Cleanup failures are reported instead of being treated as success.

## Compatibility boundaries

`M4A1Compatibility` coordinates two explicit contracts. `M4A1AssemblyContract` verifies the supported original file/loaded callback and the active plugin identity. `RoR2ReviveContract` verifies vanilla signatures, pending-state behavior, canonical fingerprints and readable structural landmarks. Generic IL reading/formatting is separate from these decisions.

The current candidate accepts exact M4A1 1.1.5 with the inspected current RoR2 image, plus the separately retained legacy 1.1.4 contract. These are explicit pairs, not unrestricted version ranges. See [compatibility](COMPATIBILITY.md) for the supported binary identities.

On the current game, Shrine uses HasUnmanagedBuff(BuffDef) / RemoveUnmanagedBuff(BuffDef, int). The fix selects a typed open-instance delegate to that same reader only after the whole contract passes. The legacy contract selects HasBuff instead. Reading the wrong storage can miss a Shrine life even when the visible buff count differs. The fix never removes either buff itself.

CurrentRoR2ReviveContract checks the game file identity and signatures/canonical bodies of OnBodyDeath, TryReviveOnBodyDeath, RespawnExtraLifeShrine and IsExtraLifePendingServer. The upstream file/raw callback checks remain. The new game keeps Shrine before Dio and still schedules RespawnExtraLifeShrine. Respawn now uses RespawnArgs with wasRevivedMidStage; the postfix still reacquires the current live body afterward.

Vanilla's cannotRevive, DisableDefenses and DontConsumeRevivalItems checks remain vanilla-owned. This change does not redefine upstream Dummy behavior under those restrictions or add priorities for Seeker, equipment or other DLC revival sources. Test those interactions separately if required.

Other mods can change the transformed stream or hook ordering. Startup registration and later health checks reduce risk but cannot prove compatibility with every mod stack. See [testing](TESTING.md) for gameplay and multiplayer regression scenarios.

## First-run presentation preparation

RunStartWarmup waits for game content readiness, then starts the original MercBody display addresses in small async batches. AssetWarmupJob yields across frames until completion. Only then does ItemDisplayWarmupContract invoke original LazyInit; original Run.Start still applies item rules normally. Starting a run early cancels preparation and preserves the original path. This changes presentation preparation timing, not revival logic or network authority. Preparation waits for BodyCatalog readiness, starts at most eight unique addresses per frame and releases only its owned prefetch handles. Failure restores original cache state; original fallback remains available. See [architecture](ARCHITECTURE.md) and [testing](TESTING.md).
