# Gameplay regression checks

Automated-test prerequisites and commands are in [BUILDING.md](../BUILDING.md). Compilation and managed tests do not prove Unity gameplay or multiplayer behavior.

## Startup and presentation

Close the game, back up the previous fix outside plugins and install exactly one supported M4A1Fix.dll. Expect `RevivePriority enabled.`, `ShrineAIRecovery enabled.` and warmup registration. Wait for `RunStartWarmup: ready before first run`, compare first/second run transitions, then repeat after restarting the game.

A fallback/Failed/Skipped diagnostic means preparation did not complete; save the full log. The logged background time includes yielded frames, not a continuous freeze. Also start a run immediately on a fresh process: incomplete preparation may cancel, but the original run must still work.

Check item models, skills and Dummy copying of grenades and Particle Cannon with the intended original configuration. Deferred original skill-hook registration remains unchanged.

## Revival

Use a natural Shrine interaction and ordinary lethal damage; avoid debug instant-final-death commands that bypass the revival pipeline.

| Setup | Expected |
|---|---|
| Shrine only | One Shrine life spent; one revival |
| One or several Dummies | Exactly one Dummy spent per death |
| Dio only | One Dio through vanilla revival |
| Shrine + Dummy + Dio | Shrine spent; Dummy and Dio preserved |
| Dummy + Dio | One Dummy spent; Dio preserved |
| Shrine + two Dummies + Dio, repeated deaths | Shrine, Dummy, Dummy, Dio, final death |
| No source | Final death |

After Shrine revival, observe Dummy following and combat. Check another survivor for regressions. Record resource counts, candidate identity, configuration and full logs for each exercised scenario.

## Multiplayer gate

**Not yet tested on two physical PCs.** Use identical supported binaries on host/client. Test remote-client-controlled M4A1 and host-controlled M4A1, mixed-source priority, sequential depletion, no extra revival, AI visible on both peers and independent local warmup. Supply both logs and observations. Only exercised scenarios can pass; unrun cases remain unverified.

Historical PlaySoundNetworkRequest errors have no proven root cause or shipped fix here.
