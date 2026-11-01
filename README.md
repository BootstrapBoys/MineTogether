# Mine Together

[![beta](https://img.shields.io/badge/status-beta-orange)](#status)

Valheim BepInEx mod: always-on vein mining with a hotkey toggle, area-of-effect pickaxe swings,
world resource multipliers and rock collapse in one plugin.

The user-facing documentation, install steps and configuration reference live in
[`resources/README.md`](resources/README.md). This file covers the repository itself.

## Status

**Beta.** Behaviour and settings can still change between builds. Release `0.1.0` is the first
public build and has not been through a long soak test.

## What it does

- **Vein mining** - swing a pickaxe at a deposit and the whole vein is worked, buried parts
  included. `Alt+B` toggles the feature; progress is shared between players and saved.
- **Resource multipliers** - mining, lumber, pickables, crates and dungeon loot, scaled
  independently. Creature drops are deliberately untouched so loot mods keep working.
- **Rock collapse** - deposits fall once their surviving footing drops below a configured
  percentage, instead of leaving pieces floating.
- **AOE mining** - a pickaxe swing spreads its damage across nearby rocks, divided between them.
- **Host-authoritative settings** - the master switch and everything under `[Server Settings]`
  sync to clients on join and whenever the host changes one.

## Requirements

| | |
|---|---|
| BepInEx | 5.4.2351+ (BepInExPack Valheim 5.4.23xx) |
| Jotunn | 2.30.2 (hard dependency) |
| Game | Valheim 1.0.21 |

## Install

Server and every client: drop `MineTogether.dll` into `BepInEx/plugins/`. The config file
(`BepInEx/config/com.bootstrapboys.minetogether.cfg`) is generated on first launch.

## Build from source

The project compiles against the real game assemblies and Jotunn, both of which are gitignored
because they are not redistributable.

```
refs/
  game/          # copy of valheim_Data/Managed + BepInEx/core (you supply)
  mods/Jotunn.dll
```

```bash
export VALHEIM_DIR=/path/to/refs/game
export BEPINEX_DIR=$VALHEIM_DIR/BepInEx
./tooling/build.sh
```

Output lands in `artifacts/debug/` as `MineTogether.dll` plus a `build-info.txt` with the version,
UTC build time and sha256. `bin/` and `obj/` are removed by the script.

### Tools

| script | purpose |
|---|---|
| `tooling/build.sh` | release build, then cleans intermediate output |
| `tooling/make-config.py` | regenerates the config template and asserts it matches the `Bind` calls |
| `tooling/audit-targets.py` | flags patch targets that name an overloaded method (Harmony refuses those) |
| `tooling/import-icon.py` | converts square art into the 256x256 store icon |
| `tooling/make-icon.py` | drawn fallback icon, used when there is no art |

## Layout

```
src/                    plugin sources
resources/              manifest, README, changelog, icon, config template
tooling/                build, config generator, audits, icon generator
artifacts/              build output (gitignored)
```

## Diagnostics

Set `[Debug] Enable Debug = true` and the mod writes `BepInEx/BSBLogs/MineTogether.log`, truncated
each launch: environment, applied Harmony patches, resolved reflection bindings, the settings in
force, and a per-world summary of sessions, drops, collapses, AOE swings, sync results and errors.
Errors are written even when the switch is off.

## Credits

The vein mining engine is a port of [VeinMining](https://thunderstore.io/c/valheim/p/BranchOfYore/VeinMining/)
by BranchOfYore (MIT). Rock collapse and AOE mining are independent implementations of behaviour
provided by PlsCollapse and AOEMining; neither ships a licence, so no code from either is used.
See [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
