#!/usr/bin/env python3
"""Write a BepInEx-format config template for Mine Together.

The values here are a snapshot of the code defaults, so the generator asserts every
(section, key, default) against the Bind calls in src/ before writing. If a default
changes in code, this fails loudly instead of shipping a stale template.

Usage: python3 tooling/make-config.py
Writes resources/config/com.bootstrapboys.minetogether.cfg
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "resources", "config", "com.bootstrapboys.minetogether.cfg")
GUID = "com.bootstrapboys.minetogether"
NAME = "Mine Together"

# section, key, type, default, acceptable, description  (bind order)
ENTRIES = [
    ("General", "Enabled", "Boolean", "true", None,
     "Enables/Disables the mod."),
    ("General", "VeinMine", "Boolean", "true", None,
     "Vein mining is always active while this is on. ToggleKey flips it at runtime."),
    ("General", "ResourceMultiplier", "Boolean", "true", None,
     "Scale world resources (mining, lumber, pickables, crates, dungeon loot)."),
    ("General", "FallingDeposits", "Boolean", "true", None,
     "Rock pieces fall once the deposit no longer rests on enough ground. Vanilla leaves them floating."),
    ("General", "AreaMining", "Boolean", "true", None,
     "Spread a pickaxe swing over the rocks around the impact point. Never runs while vein mining is active."),
    ("General", "ToggleKey", "KeyboardShortcut", "B + LeftAlt", None,
     "Hold Alt and press B to toggle vein mining on and off."),

    ("Server Settings", "AdditionalPrefabs", "String", "", None,
     "Extra prefab names treated as ore deposits."),
    ("Server Settings", "ExcludedPrefabs", "String", "", None,
     "Prefab names to ignore when detecting ore deposits."),
    ("Server Settings", "MaximumDistance", "Single", "6", "From 2 to 12",
     "Maximum contributor distance to an exposed deposit collider."),
    ("Server Settings", "MiningSpeedMultiplierPerPlayer", "Single", "0.5", "From 0 to 5",
     "Speed added per extra player. 100% is five times vanilla work per swing; 0.5 makes two "
     "players 150% and three 200% of that. 0 turns the bonus off. Only the host/server value is used."),
    ("Server Settings", "MultiplierPlayerRange", "Single", "10", "From 1 to 64",
     "Metres around the deposit in which a player counts towards MiningSpeedMultiplierPerPlayer."),

    ("Server Settings", "MiningMultiplier", "Single", "1", "From 1 to 10",
     "Multiplier for ore, stone and other mining node drops. 1 = vanilla."),
    ("Server Settings", "LumberMultiplier", "Single", "1", "From 1 to 10",
     "Multiplier for wood, fine wood, core wood and other tree drops. 1 = vanilla."),
    ("Server Settings", "PickableMultiplier", "Single", "1", "From 1 to 10",
     "Multiplier for hand-picked items (berries, mushrooms, flint, branches). 1 = vanilla."),
    ("Server Settings", "StructureMultiplier", "Single", "1", "From 1 to 10",
     "Multiplier for destructible crates and dungeon loot spawners. 1 = vanilla."),

    ("Server Settings", "PercentFootingNeeded", "Int32", "15", "From 0 to 90",
     "How much of a rock's original footing must survive; mine past that and the whole rock drops. "
     "0 turns the rule off."),
    ("Server Settings", "PercentSupportBoxSize", "Int32", "100", "From 25 to 150",
     "Size of each piece's support box relative to the piece. Lower makes pieces fall sooner."),

    ("Server Settings", "ScaleWithSkill", "Boolean", "true", None,
     "Grow the AOE radius with the Pickaxes skill, from RadiusMin at skill 0 to RadiusMax at skill 100."),
    ("Server Settings", "RadiusMin", "Single", "1", "From 0.5 to 8",
     "AOE radius in metres at Pickaxes 0 (or always, when ScaleWithSkill is off)."),
    ("Server Settings", "RadiusMax", "Single", "4", "From 0.5 to 10",
     "AOE radius in metres at Pickaxes 100."),
    ("Server Settings", "BonusDamage", "Single", "0", "From 0 to 50",
     "Pickaxe damage added to the AOE swing before it is divided between targets. "
     "0 keeps a vanilla-strength swing."),
    ("Server Settings", "MaxTargets", "Int32", "16", "From 1 to 32",
     "Most rocks one AOE swing may spread damage over."),
    ("Server Settings", "AOEExcludedPrefabs", "String", "blackmetal,obsidian,muddy,scrapiron", None,
     "Comma-separated name fragments. A rock whose prefab or display name contains one of these "
     "is never hit by the AOE effect. Unrelated to ExcludedPrefabs above, which filters deposits."),

    ("Debug", "Enable Debug", "Boolean", "false", None,
     "Write diagnostics to BepInEx/BSBLogs/MineTogether.log: environment, applied patches, "
     "resolved bindings, settings and what each module did. Errors are written even when this is off."),
]
def source_defaults():
    """Every config Bind in src/, as {(section, key): type}."""
    found = {}
    for name in sorted(os.listdir(os.path.join(ROOT, "src"))):
        if not name.endswith(".cs"):
            continue
        text = open(os.path.join(ROOT, "src", name), encoding="utf-8").read()
        consts = dict(re.findall(r'const string (\w+) = "([^"]+)"', text))
        for bind in re.finditer(
                r'(?:Config|config)\.Bind(?:<([^>]+)>)?\(\s*(?:"([^"]+)"|(\w+))\s*,\s*"([^"]+)"', text):
            kind, literal, token, key = bind.groups()
            section = literal or consts.get(token, token)
            found[(section, key)] = kind or "auto"
    return found


def main():
    declared = {(s, k): t for s, k, t, _d, _a, _desc in ENTRIES}
    actual = source_defaults()

    missing = sorted(set(actual) - set(declared))
    extra = sorted(set(declared) - set(actual))
    if missing or extra:
        print("CONFIG TEMPLATE OUT OF SYNC WITH SOURCE", file=sys.stderr)
        for s, k in missing:
            print(f"  in code, not in template: [{s}] {k}", file=sys.stderr)
        for s, k in extra:
            print(f"  in template, not in code: [{s}] {k}", file=sys.stderr)
        return 1

    lines = [f"## Settings file was created by plugin {NAME} v0.1.0",
             f"## Plugin GUID: {GUID}",
             ""]
    section = None
    for sec, key, kind, default, acceptable, description in ENTRIES:
        if sec != section:
            section = sec
            lines += [f"[{section}]", ""]
        lines.append(f"## {description}")
        lines.append(f"# Setting type: {kind}")
        lines.append(f"# Default value: {default}")
        if acceptable:
            lines.append(f"# Acceptable value range: {acceptable}")
        lines.append(f"{key} = {default}".rstrip())
        lines.append("")

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\r\n") as handle:
        handle.write("\n".join(lines).rstrip() + "\n")

    print(f"wrote {OUT} ({len(ENTRIES)} entries, {len({e[0] for e in ENTRIES})} sections)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
