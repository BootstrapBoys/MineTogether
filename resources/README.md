# Mine Together

Vein mining, world resource multipliers, rock collapse and area-of-effect mining in one mod.

**This is a beta.** Expect rough edges: behaviour and settings can still change between builds, and the diagnostics section below is the fastest way to report something that misbehaves.

## Configuration

Every module has a switch in `[General]`:

- `Enabled` - master switch. Off leaves the mod loaded but inert.
- `VeinMine` - vein mining, toggled in game with `ToggleKey` (default Alt+B).
- `ResourceMultiplier` - world resource scaling.
- `FallingDeposits` - rock collapse.
- `AreaMining` - area-of-effect pickaxe swings.

`[Server Settings]` holds everything the host decides: deposit detection, mining speed, the four
resource multipliers, the collapse rule and the AOE tuning. `[Debug]` holds the logging switch. A
ready-made config ships in `resources/config/`.

## Server sync

The host owns `[General] Enabled` and everything under `[Server Settings]`. Clients receive those
values on join and again whenever the host changes one, so nobody plays by different rules.
Nothing is written to a client's config file - the values live in memory for the session.

Client-local settings stay local: the personal `VeinMine` toggle and its hotkey, plus `[Debug]`.
The AOE effect itself is still applied by the attacker's game, so the host's radius and damage
values are what honest clients use, but a modified client can ignore them.

## What it does

**Vein mining** - always active: hit a deposit with a pickaxe and the whole thing is worked,
buried parts included. Alt+B toggles it off and on - a message confirms the new state - and the choice is saved. Progress is
shared between players and kept, so you can pause, repair a pickaxe and continue. Ordinary hits
also count. 100% mining speed is five times the vanilla work per swing, so a vein falls in a fifth of the
hits. `MiningSpeedMultiplierPerPlayer` (default 0.5) adds half of that baseline for every extra
player within `MultiplierPlayerRange` metres - two players run at 150%, three at 200%. The
HUD shows the absolute figure (5x, 7.5x, 10x). The host's values are the ones used.

**Resource yields** - `[Server Settings]`:

- `MiningMultiplier` - ore, stone and other mining node drops.
- `LumberMultiplier` - wood, fine wood, core wood and other tree drops.
- `PickableMultiplier` - berries, mushrooms, flint, branches and other hand-picked items.
- `StructureMultiplier` - destructible crates and dungeon loot spawners.

These scale the stacks the game itself generates, so a vein-mined deposit gets the multiplier
once, not twice. Creature drops are deliberately untouched: this mod never patches
`CharacterDrop`, which is what keeps other loot mods (Creature Level and Loot Control, EpicLoot)
working alongside it. Set every multiplier to 1 for vanilla yields.

**Rock collapse** - `[Server Settings]`:

- `PercentFootingNeeded` (default 15) - how much of a rock's original footing must survive. Mine
  past that and the rest of the deposit drops.
- `PercentSupportBoxSize` (default 100) - size of each piece's support box relative to the piece.

**AOE mining** - `[Server Settings]`, on every ordinary pickaxe swing:

- `ScaleWithSkill` / `RadiusMin` / `RadiusMax` - radius grows from 1 m at Pickaxes 0 to 4 m at
  Pickaxes 100 by default.
- `BonusDamage` (default 0) - added to the swing before it is divided between targets.
- `MaxTargets`, `ExcludedPrefabs` (default `blackmetal,obsidian,muddy,scrapiron`).

No modifier key: whenever vein mining is not running, a swing is area-of-effect. AOEMining's Left
Shift requirement is deliberately not carried over.

Damage is **divided** between the rocks in range, not multiplied per rock, so one swing does not
out-damage a normal hit. AOE never runs while vein mining is active, and never runs while the mod
is finishing a vein.

**Debug** - `[Debug] Enable Debug` logs each footing check. Local only, not synced.

## Diagnostics

Set `[Debug] Enable Debug = true` and the mod writes `BepInEx/BSBLogs/MineTogether.log`, truncated
on every launch so it always describes the installed build. It records the version and environment,
the state of the five switches, every Harmony patch that actually attached, whether each by-name
game binding resolved, the settings in force, and a per-world summary of sessions, drops,
collapses, AOE swings, sync results and errors.

Errors are written even with the switch off, so a failure leaves a trail without leaving
diagnostics running. Copy the file out before relaunching - the next start truncates it.

`MineTogether.prefabs.tsv` in the config folder lists every destructible prefab the mod recognises,
with its adapter, drop list and damage modifiers. It is the fastest way to see why a deposit is not
being treated as mineable.

## Install

Server and every client: BepInEx 5.4.2351+ and Jotunn 2.30.2. Drop `MineTogether.dll` in
`BepInEx/plugins/`. The server controls vein mining speed, the collapse settings and the
multipliers; clients control their own `ToggleKey` and AOE settings.

## Compatibility

- Does not touch creature loot, so loot-table mods keep working.
- Conflicts with other vein-mining mods (do not run VeinMining beside this) and with other mods
  that rewrite `MineRock5` support, such as PlsCollapse.

## Credits

The vein mining engine is a port of **VeinMining** by BranchOfYore (MIT). The MIT notice is in
`THIRD-PARTY-NOTICES.md`. Rock collapse is an independent implementation of the behaviour
PlsCollapse provides; AOE mining of the behaviour AOEMining provides. No code from either mod is
used.
