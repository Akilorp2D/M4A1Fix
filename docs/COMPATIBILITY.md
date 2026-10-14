# Compatibility

Supported current pair:

| Binary | SHA-256 |
|---|---|
| M4A1Mod.dll 1.1.5 | 023B3FE8F86F9DCE8FD9F203BA3580D565A9940D2E0E82AB3FA129EDC33CC6F0 |
| RoR2.dll 1.5.0#1210* | A5F7D83C7E6872FDD6099FDC299ADB7D4FA03CBF465E526D6F5EFCC35C2C281D |

The older exact M4A1 1.1.4 contract is retained for its separate legacy game contract. It is not accepted as a substitute for 1.1.5 on the current game. Warmup requires the inspected 1.1.5 initializer. Legacy gameplay has not been newly retested.

File identities, active plugin identity, signatures and loaded method IL are checked before patch installation. Current vanilla contracts include OnBodyDeath, TryReviveOnBodyDeath, RespawnExtraLifeShrine and IsExtraLifePendingServer. Unknown or transformed incompatible contracts reject the patch with diagnostics.

Current Shrine uses unmanaged server buff storage. The selected typed reader follows vanilla HasUnmanagedBuff; legacy selects HasBuff. The fix does not consume buffs itself or change vanilla revival restrictions. See [how the fixes work](HOW_THE_FIXES_WORK.md).

BepInEx can load RoR2 from memory with an empty Assembly.Location. Only in that case does disk identity validation use BepInEx.Paths.ManagedPath/RoR2.dll; loaded IL validation remains independent. A nonempty invalid reported location is not silently replaced.

File/IL identity cannot prove every other mod's effective hook ordering. Mods changing display rules at run start can also affect early warmup assumptions. The original M4A1 is inspected as binary metadata/IL; no original source repository or fork is provided.

GUID: `com.m4a1fix.revivepriority`. Plugin version: 1.1.0. Assembly/file version: 1.1.0.0. Distributed DLL SHA-256: `6A38F0141E03C3E45C38D2B7B9FB2C7F5CE2C42B920D5F3946C389BF61C0450A`.

**Two-PC multiplayer remains untested.** Build/contract checks do not replace Unity acceptance. Use [testing](TESTING.md).
